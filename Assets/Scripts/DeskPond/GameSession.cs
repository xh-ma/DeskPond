using UnityEngine;
using UnityEngine.InputSystem;

namespace DeskPond
{
    [DefaultExecutionOrder(-50)]
    public class GameSession : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] CatPresenter cat;
        [SerializeField] BasketPresenter basket;
        [SerializeField] FishingRig rig;
        [SerializeField] Camera worldCamera;
        [SerializeField] float waterSurfaceY = -5.05f;

        SaveData _save;
        GameHud _hud;
        BasketMenu _menu;
        StatsPanel _stats;
        GlobalInputCounter _input;
        ActivityLog _activity;
        ForegroundWatcher _foreground;
        RateModel _pacing;
        PetDragger _dragger;
        DesktopWindow _window;
        CatState _state = CatState.Idle;
        float _catchElapsed;
        float _saveCooldown;
        bool _dirty;
        int _idleFishGranted;
        bool _giftSent;
        readonly System.Random _rng = new System.Random();

        /// <summary>Enabled by the -debuginput command line flag; logs every resolved click.</summary>
        static readonly bool _logInput =
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-debuginput") >= 0;

        public int Coins => _save != null ? _save.coins : 0;
        public int BasketFish => _save != null ? _save.basketFish : 0;
        public int Capacity => ShopDefs.Capacity(_save != null ? _save.basketLevel : 0);
        public bool BasketFull => _save != null && _save.basketFish >= Capacity;
        public int FishPrice => ShopDefs.FishPrice(_save != null ? _save.lureLevel : 0);
        public SaveData Save => _save;
        public GlobalInputCounter Input => _input;
        public GameConfig Config => config;

        /// <summary>The activity history, or null when logging is switched off in the config.</summary>
        public ActivityLog Activity => _activity;

        /// <summary>Learned pacing model. Null until Start has run.</summary>
        public RateModel Pacing => _pacing;

        /// <summary>
        /// Whether window titles are being recorded. The save wins once the player has chosen;
        /// until then the config default applies.
        /// </summary>
        public bool RecordTitles
        {
            get
            {
                if (_save != null && _save.recordTitles >= 0)
                    return _save.recordTitles == 1;
                return config != null && config.recordWindowTitles;
            }
        }

        /// <summary>Turns title recording on or off from the panel and remembers the choice.</summary>
        public void SetRecordTitles(bool on)
        {
            if (_save == null)
                return;

            _save.recordTitles = on ? 1 : 0;
            _dirty = true;
            Persist();

            // Turning it off should also drop what was already captured, otherwise the switch
            // only stops new titles and quietly leaves the old ones on disk.
            if (!on && _activity != null && config != null)
                _activity.ForgetTitles(config.retentionDays);

            Debug.Log("DeskPond activity: window titles " + (on ? "on" : "off"));
        }

        /// <summary>Seconds of unbroken idle time. Resets to zero on any counted input.</summary>
        public float IdleSeconds { get; private set; }

        /// <summary>True once idle long enough that the cat is visibly waiting it out.</summary>
        public bool IsIdle => config != null && IdleSeconds >= config.idleThresholdSeconds;

        /// <summary>0..1 progress toward the next bite.</summary>
        public float CatchProgress =>
            _save == null || _save.nextCatchTarget <= 0
                ? 0f
                : Mathf.Clamp01((float)_save.inputsTowardCatch / _save.nextCatchTarget);

        public void Bind(GameConfig gameConfig, CatPresenter catPresenter,
            BasketPresenter basketPresenter, Camera cam)
        {
            config = gameConfig;
            cat = catPresenter;
            basket = basketPresenter;
            worldCamera = cam;
        }

        void Start()
        {
            ResolveRefs();
            if (cat != null)
            {
                cat.ApplyConfig(config);
                cat.Bind(cat.GetComponent<SpriteRenderer>());
            }
            if (basket != null)
                basket.Bind(basket.GetComponent<SpriteRenderer>(), basket.transform.Find("FullCue"));

            _save = SaveSystem.LoadOrCreate();
            ClampBasket();

            _pacing = new RateModel(_save.pacing);
            _save.pacing = _pacing.State;
            StartActivityLog();

            if (_save.nextCatchTarget <= 0)
                RollNextTarget();

            _hud = FindFirstObjectByType<GameHud>();
            if (_hud == null)
                _hud = GameHud.Create();

            _menu = FindFirstObjectByType<BasketMenu>();
            if (_menu == null)
                _menu = BasketMenu.Create(this, config);

            _stats = FindFirstObjectByType<StatsPanel>();
            if (_stats == null)
                _stats = StatsPanel.Create(this, config);
            _menu.OnStatsRequested = ToggleStats;

            _window = DesktopWindow.Create();
            _window.ApplyConfig(config);
            _window.HitTest = ScreenPointInteractive;
            _dragger = FindFirstObjectByType<PetDragger>();
            if (_dragger == null && cat != null && basket != null)
                _dragger = PetDragger.Create(this, cat.transform, basket.transform, worldCamera);
            if (_dragger != null)
                _dragger.SetWindow(_window);

            _hud.Bind(this, cat != null ? cat.transform : null, basket != null ? basket.transform : null,
                worldCamera, config != null ? config.petZoom : 3f);

            ApplySkins();
            Enter(BasketFull ? CatState.Idle : CatState.Waiting);
            RefreshView();

            // -gift forces one on every launch, for checking the drift without wiping the save.
            var forceGift = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-gift") >= 0;
            if (forceGift || !_save.welcomeGiftGiven)
            {
                _save.welcomeGiftGiven = true;
                _dirty = true;
                StartCoroutine(WelcomeGift());
            }
            Debug.Log("DeskPond save path: " + SaveSystem.FilePath);
            var configName = config != null ? config.name : "NULL";
            var inputRange = config != null && config.adaptivePacing
                ? $"adaptive({config.minutesPerFish}min, {_pacing.Samples}/{RateModel.WarmupSamples} " +
                  $"samples, median={_pacing.MedianRate:0.#}/min)"
                : ShopDefs.MinInputs(_save.rodLevel) + "-" + ShopDefs.MaxInputs(_save.rodLevel);
            var hooked = _input != null && _input.IsGlobal;
            Debug.Log($"DeskPond config: asset={configName} " +
                      $"idleThreshold={config.idleThresholdSeconds}s " +
                      $"fishEvery={config.idleSecondsPerFish}s " +
                      $"giftEvery={config.idleSecondsPerGift}s " +
                      $"inputsPerFish={inputRange} globalHook={hooked}");
        }

        void Update()
        {
            AccumulateInput();
            HandleClick();
            TickState(Time.deltaTime);
            TickActivity(Time.deltaTime);
            TickAutosave(Time.deltaTime);

            // The open panel must stay clickable even though it is not part of the pet sprite.
            if (_window != null && _menu != null && _menu.PointerOverMenu())
                _window.PointerOverPet = true;
            if (_window != null && _stats != null && _stats.PointerOverPanel())
                _window.PointerOverPet = true;
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
                Persist();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                Persist();
        }

        void OnApplicationQuit()
        {
            Persist();
            CommitActivity();
        }

        void OnDestroy()
        {
            if (_dirty)
                Persist();
            CommitActivity();
        }

        /// <summary>Writes the slice in progress, which the interval flush would otherwise lose.</summary>
        void CommitActivity()
        {
            if (_activity == null || config == null)
                return;

            _foreground?.Commit(_activity, RecordTitles);
            _activity.FlushNow(config.retentionDays, config.titleRetentionDays);
        }

        void ResolveRefs()
        {
            if (config == null)
                config = GameRuntimeSetup.LoadConfig();
            if (config == null)
                config = ScriptableObject.CreateInstance<GameConfig>();
            if (cat == null)
                cat = FindFirstObjectByType<CatPresenter>();
            if (basket == null)
                basket = FindFirstObjectByType<BasketPresenter>();
            if (worldCamera == null)
                worldCamera = Camera.main;
            if (rig == null)
                rig = FindFirstObjectByType<FishingRig>();
            if (rig == null && cat != null && basket != null)
                rig = FishingRig.Create(cat.transform, basket.transform, waterSurfaceY);
            if (rig != null)
                rig.ApplyConfig(config);

            _input = FindFirstObjectByType<GlobalInputCounter>();
            if (_input == null)
                _input = GlobalInputCounter.Create();
        }

        /// <summary>Folds this frame's counted clicks and keystrokes into progress toward a bite.</summary>
        void AccumulateInput()
        {
            if (_input == null || _save == null)
                return;

            var events = _input.TakeFrameEvents();

            // Both of these need every frame, not just busy ones: the pacing model closes a sample
            // on the clock, and a quiet minute is still part of the record.
            _pacing?.Tick(Time.deltaTime, events);
            _activity?.AddInputs(events, _foreground?.CurrentApp,
                RecordTitles ? _foreground?.CurrentTitle : null);

            if (events <= 0)
            {
                TickIdle(Time.deltaTime);
                return;
            }

            // Any activity breaks the idle streak. Idle rewards require an unbroken stretch, so
            // they cannot be farmed by nudging the mouse every few minutes.
            IdleSeconds = 0f;
            _idleFishGranted = 0;
            _giftSent = false;
            _save.lifetimeInputs += events;

            // A full basket stops production, but the work still counts toward lifetime stats.
            if (BasketFull || _state == CatState.Catch)
                return;

            _save.inputsTowardCatch += events;
            _dirty = true;

            if (_save.inputsTowardCatch >= _save.nextCatchTarget)
                TryCatch();
        }

        /// <summary>
        /// Idle time is never saved, so the game has to be running to earn from it. The clock
        /// runs through each reward instead of resetting, so one long stretch pays fish and a
        /// gift box. Any input clears it; see AccumulateInput.
        /// </summary>
        void TickIdle(float dt)
        {
            if (config == null || _save == null)
                return;

            IdleSeconds += dt;
            var earned = IdleSeconds - config.idleThresholdSeconds;
            if (earned <= 0f)
                return;

            // Fish: one per completed idleSecondsPerFish interval.
            var due = Mathf.FloorToInt(earned / Mathf.Max(1f, config.idleSecondsPerFish));
            if (due > _idleFishGranted && !BasketFull && _state != CatState.Catch)
            {
                _idleFishGranted = due;
                TryCatch();
            }

            // Gift box: once per idle streak, at idleSecondsPerGift.
            if (!_giftSent && earned >= config.idleSecondsPerGift)
            {
                _giftSent = true;
                Debug.Log($"DeskPond gift: idle={IdleSeconds:0.0}s earned={earned:0.0}s " +
                          $"threshold={config.idleSecondsPerGift}s " +
                          $"sinceStart={Time.timeSinceLevelLoad:0.0}s");
                SpawnGift();
            }
        }

        /// <summary>
        /// A welcome gift on a brand new save, so the drifting box is introduced up front rather
        /// than only after a 30 minute idle stretch. Once only: the box pays coins when every
        /// skin is owned, so a per-launch gift would be farmable by restarting.
        /// </summary>
        System.Collections.IEnumerator WelcomeGift()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, config.welcomeGiftDelaySeconds));
            Debug.Log("DeskPond: welcome gift (first run).");
            SpawnGift();
        }

        /// <summary>The gift box currently on screen, drifting or parked. Null when there is none.</summary>
        public DriftingGift ActiveGift { get; set; }

        void SpawnGift()
        {
            // Only one box at a time. A parked box waits indefinitely for a click, so idle time
            // would otherwise stack several on the same spot.
            if (cat == null || worldCamera == null || ActiveGift != null)
                return;

            DriftingGift.Spawn(this, cat.transform, worldCamera, waterSurfaceY,
                config.giftDrift, config.giftDriftSeconds);
        }

        /// <summary>Shows the "you got X" bubble above the cat when a gift box is opened.</summary>
        public void ShowGiftBubble(string reward)
        {
            if (_hud != null && cat != null)
                _hud.ShowBubble("Got " + reward + "!", cat.transform);
        }

        /// <summary>
        /// Called by the gift box when it reaches the cat. Unlocks and equips a random skin the
        /// player does not own; returns its name, or null when everything is already unlocked.
        /// </summary>
        public string GrantGiftSkin()
        {
            if (_save == null)
                return null;

            if (!ShopDefs.TryPickUnownedSkin(_save, _rng, out var isCat, out var index))
            {
                // Nothing left to unlock, so pay coins instead and the wait still counts.
                _save.coins += 250;
                _dirty = true;
                Persist();
                RefreshView();
                return "250c (all skins owned)";
            }

            ShopDefs.GrantSkin(_save, isCat, index);
            if (isCat)
                _save.catSkin = index;
            else
                _save.basketSkin = index;

            ApplySkins();
            _dirty = true;
            Persist();
            RefreshView();
            return (isCat ? "cat skin " : "basket skin ") + ShopDefs.SkinName(isCat, index);
        }

        void TickState(float dt)
        {
            if (_state == CatState.Catch)
            {
                _catchElapsed += dt;
                if (_catchElapsed >= config.catchAnimDuration)
                    Enter(BasketFull ? CatState.Idle : CatState.Waiting);
                return;
            }

            if (_state == CatState.Idle && !BasketFull)
                Enter(CatState.Waiting);
            else if (_state == CatState.Waiting && BasketFull)
                Enter(CatState.Idle);
        }

        void TryCatch()
        {
            if (!BasketFull)
            {
                _save.basketFish += 1;
                _save.lifetimeFish += 1;
                ClampBasket();
            }

            _save.inputsTowardCatch = 0;
            RollNextTarget();
            _dirty = true;
            Persist();
            Enter(CatState.Catch);
        }

        /// <summary>
        /// Prices the next fish. Once the pacing model has seen enough active minutes it sets the
        /// cost from this user's own input rate; until then, and whenever adaptive pacing is off,
        /// it falls back to the fixed range so a brand new save still behaves.
        /// </summary>
        void RollNextTarget()
        {
            var level = _save.rodLevel;
            var scale = config != null ? Mathf.Max(0.01f, config.inputCostScale) : 1f;

            if (config != null && config.adaptivePacing && _pacing != null
                && _pacing.TryGetTarget(config.minutesPerFish, ShopDefs.RodFactor(level),
                    config.pacingMinInputs, config.pacingMaxInputs, config.pacingSpread, _rng,
                    out var adaptive))
            {
                _save.nextCatchTarget = Mathf.Max(1, Mathf.RoundToInt(adaptive * scale));
                return;
            }

            var roll = Random.Range(ShopDefs.MinInputs(level), ShopDefs.MaxInputs(level) + 1);
            _save.nextCatchTarget = Mathf.Max(1, Mathf.RoundToInt(roll * scale));
        }

        void StartActivityLog()
        {
            if (config == null || !config.trackActivity)
                return;

            _activity = ActivityLog.Load();
            _foreground = new ForegroundWatcher();

            if (!_foreground.IsSupported)
                Debug.Log("DeskPond activity: foreground sampling unavailable on this platform.");
            else
                Debug.Log($"DeskPond activity: keep={config.retentionDays}d " +
                          $"titles={(RecordTitles ? config.titleRetentionDays + "d" : "off")} " +
                          $"poll={config.foregroundPollSeconds}s file={ActivityLog.FilePath}");
        }

        void TickActivity(float dt)
        {
            if (_activity == null || config == null)
                return;

            _foreground?.Tick(dt, _activity, config.foregroundPollSeconds, RecordTitles);
            _activity.Tick(dt, config.retentionDays, config.titleRetentionDays);
        }

        /// <summary>Opens the activity panel beside the basket, from the shop's stats button.</summary>
        void ToggleStats()
        {
            if (_stats == null)
                return;

            var anchor = basket != null && worldCamera != null
                ? (Vector2)worldCamera.WorldToScreenPoint(basket.transform.position)
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.4f);
            _stats.ToggleAt(anchor);
        }

        /// <summary>Deletes the activity history. Wired to the panel so it is not a hidden file.</summary>
        public void ClearActivity()
        {
            _activity?.Clear();
            AppIcons.ClearCache();
            Debug.Log("DeskPond activity: history cleared.");
        }

        public void SellAll()
        {
            if (_save == null || _save.basketFish <= 0)
                return;

            _save.coins += _save.basketFish * FishPrice;
            _save.basketFish = 0;
            _dirty = true;
            Persist();

            if (_state == CatState.Idle)
                Enter(CatState.Waiting);

            RefreshView();
        }

        public bool TryBuyUpgrade(UpgradeKind kind)
        {
            if (_save == null)
                return false;

            var level = ShopDefs.Level(_save, kind);
            var cost = ShopDefs.NextCost(kind, level);
            if (cost < 0 || _save.coins < cost)
                return false;

            _save.coins -= cost;
            ShopDefs.SetLevel(_save, kind, level + 1);

            // A shorter rod target should take effect immediately rather than after the current fish.
            if (kind == UpgradeKind.Rod)
                _save.nextCatchTarget = Mathf.Max(_save.inputsTowardCatch + 1,
                    Mathf.RoundToInt(_save.nextCatchTarget * ShopDefs.RodFactor(1)));

            _dirty = true;
            Persist();
            RefreshView();
            return true;
        }

        public bool TryBuySkin(bool isCat, int index)
        {
            if (_save == null)
                return false;

            var skins = ShopDefs.Skins(isCat);
            if (index < 0 || index >= skins.Length)
                return false;

            // Owned skins re-equip for free; unowned ones are bought once and then kept.
            if (!ShopDefs.OwnsSkin(_save, isCat, index))
            {
                if (_save.coins < skins[index].Cost)
                    return false;
                _save.coins -= skins[index].Cost;
                ShopDefs.GrantSkin(_save, isCat, index);
            }

            if (isCat)
                _save.catSkin = index;
            else
                _save.basketSkin = index;

            ApplySkins();
            _dirty = true;
            Persist();
            RefreshView();
            return true;
        }

        void ApplySkins()
        {
            if (_save == null)
                return;

            if (cat != null)
                cat.SetSkin(_save.catSkin);
            if (basket != null)
                basket.SetSkin(_save.basketSkin);
        }

        void Enter(CatState next)
        {
            _state = next;
            if (next == CatState.Catch)
                _catchElapsed = 0f;
            if (cat != null)
                cat.SetState(next);
            if (rig != null)
            {
                rig.SetWaiting(next == CatState.Waiting);
                if (next == CatState.Catch)
                    rig.PlayCatch(config.catchAnimDuration);
            }

            RefreshView();
        }

        /// <summary>Primary press, from the OS while the overlay is up, otherwise from Unity.</summary>
        bool PrimaryPressed() =>
            _window != null && _window.IsActive ? _window.PrimaryPressedThisFrame : WasPrimaryPressed();

        Vector2 Pointer() =>
            _window != null && _window.IsActive && _window.CursorValid
                ? _window.CursorScreen
                : PointerScreen();

        void HandleClick()
        {
            if (!PrimaryPressed() || basket == null || worldCamera == null)
                return;

            var screen = Pointer();

            // Clicks landing on either panel are handled by the UI, not by the world.
            if (_menu != null && _menu.ContainsScreenPoint(screen))
                return;
            if (_stats != null && _stats.ContainsScreenPoint(screen))
                return;

            // A parked gift box takes priority; it sits right next to the cat and basket.
            var worldPoint = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            worldPoint.z = 0f;
            if (ActiveGift != null && ActiveGift.ContainsWorldPoint(worldPoint))
            {
                if (_logInput)
                    Debug.Log("DeskPond click: opened gift box");
                ActiveGift.OpenNow();
                return;
            }

            var ray = worldCamera.ScreenPointToRay(screen);
            var hit = Physics2D.GetRayIntersection(ray, 100f);
            Collider2D collider = hit.collider;
            if (collider == null)
            {
                var world = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
                collider = Physics2D.OverlapPoint(world);
            }

            var onBasket = collider != null && collider.GetComponentInParent<BasketPresenter>() == basket;

            if (_logInput)
                Debug.Log($"DeskPond click: screen=({screen.x:0},{screen.y:0}) " +
                          $"hit={(collider != null ? collider.name : "NULL")} onBasket={onBasket} " +
                          $"menuOpen={(_menu != null && _menu.IsOpen)}");

            if (onBasket && _menu != null)
            {
                var basketScreen = worldCamera.WorldToScreenPoint(basket.transform.position);
                _menu.ToggleAt(new Vector2(basketScreen.x, basketScreen.y));

                if (_logInput)
                    Debug.Log("DeskPond menu -> open=" + _menu.IsOpen);
            }
        }

        /// <summary>
        /// Whether a screen point should capture clicks instead of passing them to the desktop:
        /// the open panel, or the pet itself. The pet's colliders are expanded by a small margin so
        /// the window has already stopped being click-through by the time the cursor lands on it.
        /// </summary>
        bool ScreenPointInteractive(Vector2 screen)
        {
            if (_menu != null && (_menu.IsInteracting || _menu.ContainsScreenPoint(screen)))
                return true;

            if (_stats != null && (_stats.IsInteracting || _stats.ContainsScreenPoint(screen)))
                return true;

            if (worldCamera == null)
                return false;

            var world = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            world.z = 0f;

            // A parked gift box must also capture clicks, or it cannot be opened.
            if (ActiveGift != null && ActiveGift.ContainsWorldPoint(world))
                return true;

            return OverlapsWithMargin(cat != null ? cat.GetComponent<Collider2D>() : null, world)
                   || OverlapsWithMargin(basket != null ? basket.GetComponent<Collider2D>() : null, world);
        }

        static bool OverlapsWithMargin(Collider2D col, Vector3 world)
        {
            if (col == null)
                return false;
            var bounds = col.bounds;
            bounds.Expand(new Vector3(0.35f, 0.35f, 1000f));
            return bounds.Contains(world);
        }

        static bool WasPrimaryPressed()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                return true;

            var touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }

        static Vector2 PointerScreen()
        {
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.position.ReadValue();

            var touch = Touchscreen.current;
            return touch != null ? touch.primaryTouch.position.ReadValue() : Vector2.zero;
        }

        void ClampBasket()
        {
            if (_save.basketFish < 0)
                _save.basketFish = 0;
            if (_save.basketFish > Capacity)
                _save.basketFish = Capacity;
        }

        /// <summary>Progress changes every keystroke, so writes are throttled instead of immediate.</summary>
        void TickAutosave(float dt)
        {
            if (!_dirty)
                return;

            _saveCooldown -= dt;
            if (_saveCooldown <= 0f)
            {
                Persist();
                _saveCooldown = 10f;
            }
        }

        void RefreshView()
        {
            if (basket != null)
                basket.SetFull(BasketFull);
            if (_hud != null)
                _hud.Refresh(this);
            if (_menu != null)
                _menu.Refresh();
        }

        void Persist()
        {
            if (_save == null)
                return;
            SaveSystem.Write(_save);
            _dirty = false;
            _saveCooldown = 10f;
        }

        [ContextMenu("Force Catch")]
        void ContextCatch()
        {
            if (_save != null)
                TryCatch();
        }

        [ContextMenu("Sell All")]
        void ContextSell() => SellAll();

        [ContextMenu("Add 1000 Coins")]
        void ContextCoins()
        {
            if (_save == null)
                return;
            _save.coins += 1000;
            _dirty = true;
            Persist();
            RefreshView();
        }

        [ContextMenu("Reset Save File")]
        void ContextResetSave()
        {
            _save = new SaveData { version = SaveSystem.CurrentVersion };
            RollNextTarget();
            ApplySkins();
            Enter(CatState.Waiting);
            Persist();
            RefreshView();
        }
    }
}
