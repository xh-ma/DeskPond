using System.Collections.Generic;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// The game: turns clicks, keystrokes and idle time into bites, decides what is caught, and
    /// owns the save. Everything visible is delegated: <see cref="Angler"/> (the pet),
    /// <see cref="PondView"/> (the water), <see cref="PondMenu"/> (the panel), and so on.
    ///
    /// The loop: the cat fishes on its own. Typing and clicking drop bait crumbs that draw fish in,
    /// so focused work brings bites faster; sitting idle still earns a slow trickle. Each bite has
    /// a small chance to be golden, which waits for a click.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class GameSession : MonoBehaviour
    {
        [SerializeField] GameConfig config;

        SaveData _save;
        GameHud _hud;
        PondMenu _menu;
        public PondMenu Menu => _menu;
        StatsPanel _stats;
        GlobalInputCounter _input;
        ActivityLog _activity;
        ForegroundWatcher _foreground;
        RateModel _pacing;
        DesktopWindow _window;
        PondView _pond;
        Angler _angler;
        Critters _critters;
        Ambience _ambience;
        Camera _camera;

        float _saveCooldown;
        bool _dirty;
        int _idleFishGranted;
        bool _biteDue;
        float _sinceBite = 9999f;
        float _bait;
        float _fullSeconds;
        float _crowRoll = 60f;
        float _moodClock;
        LetterPanel _letter;
        float _streak;
        bool _streakLive;
        float _visitorClock = 420f;
        float _slowPoll;
        WeatherKind _weatherShown;
        float _petCooldown, _petGiftCooldown;
        bool _writing;
        float _noteUntil;
        readonly System.Random _rng = new System.Random();
        List<string> _ghosts;

        public int Coins => _save != null ? _save.coins : 0;
        public int BasketFish => _save != null ? _save.catches.Count : 0;
        public int Capacity => ShopDefs.Capacity(_save != null ? _save.basketLevel : 0);
        public bool BasketFull => _save != null && _save.catches.Count >= Capacity;
        public SaveData Save => _save;
        public GlobalInputCounter Input => _input;
        public GameConfig Config => config;
        public ActivityLog Activity => _activity;
        public RateModel Pacing => _pacing;
        public PondView Pond => _pond;
        public Angler Pet => _angler;

        public bool RecordTitles
        {
            get
            {
                if (_save != null && _save.recordTitles >= 0)
                    return _save.recordTitles == 1;
                return config != null && config.recordWindowTitles;
            }
        }

        public void SetRecordTitles(bool on)
        {
            if (_save == null)
                return;
            _save.recordTitles = on ? 1 : 0;
            MarkDirty(true);
            if (!on && _activity != null && config != null)
                _activity.ForgetTitles(config.retentionDays);
        }

        public float IdleSeconds { get; private set; }
        public bool IsIdle => config != null && IdleSeconds >= config.idleThresholdSeconds;

        public float CatchProgress =>
            _save == null || _save.nextCatchTarget <= 0
                ? 0f
                : Mathf.Clamp01((float)_save.inputsTowardCatch / _save.nextCatchTarget);

        public DriftingGift ActiveGift { get; set; }
        public MailBottle ActiveMail { get; set; }

        /// <summary>Seconds of the work stretch going on now (it ends after two idle minutes).</summary>
        public float StreakSeconds => _streak;

        /// <summary>0..3: how much the current stretch is tilting the odds toward rarer fish.</summary>
        public int StreakTier => _streak >= 3600f ? 3 : _streak >= 1800f ? 2 : _streak >= 900f ? 1 : 0;

        /// <summary>0..5, from the saved affection.</summary>
        public int BondLevel => _save == null ? 0 : Mathf.Clamp(Mathf.FloorToInt(_save.affection / 20f), 0, 5);

        /// <summary>
        /// Play time left until the next gift box, or -1 while one is on screen waiting to be
        /// opened. Counts while the app runs, whether the user is busy or idle; nothing resets it.
        /// </summary>
        public float GiftSecondsLeft
        {
            get
            {
                if (_save == null || ActiveGift != null)
                    return -1f;
                return Mathf.Max(0f, GiftSecondsTotal - _save.giftSeconds);
            }
        }

        public float GiftSecondsTotal =>
            GiftOverride > 0f ? GiftOverride : config != null ? Mathf.Max(10f, config.giftEverySeconds) : 3600f;

        /// <summary>-giftevery=N on the command line, for checking the timer without waiting an hour.</summary>
        static readonly float GiftOverride = ReadGiftOverride();

        static float ReadGiftOverride()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("-giftevery=") && float.TryParse(a.Substring(11), out var s))
                    return Mathf.Max(5f, s);
            return 0f;
        }

        /// <summary>
        /// Banks play time toward the next gift. Paused while a box is out, so unopened boxes
        /// never stack up.
        /// </summary>
        void TickGift(float dt)
        {
            if (_save == null || ActiveGift != null)
                return;
            _save.giftSeconds += dt;
            if (_save.giftSeconds < GiftSecondsTotal)
                return;
            Debug.Log($"DeskPond gift: {GiftSecondsTotal:0}s of play time banked, sending a gift box.");
            _save.giftSeconds = 0f;
            MarkDirty(false);
            SpawnGift();
        }

        /// <summary>Legacy entry point kept for the scene's serialized reference.</summary>
        public void Bind(GameConfig gameConfig, Object a, Object b, Camera cam)
        {
            config = gameConfig;
            _camera = cam;
        }

        // ------------------------------------------------------------------ startup

        void Start()
        {
            if (config == null)
                config = GameRuntimeSetup.LoadConfig();
            _camera = Camera.main;

            _save = SaveSystem.LoadOrCreate();
            _pacing = new RateModel(_save.pacing);
            _save.pacing = _pacing.State;
            if (_save.nextCatchTarget <= 0)
                RollNextTarget();

            _input = FindFirstObjectByType<GlobalInputCounter>() ?? GlobalInputCounter.Create();
            StartActivityLog();

            _window = DesktopWindow.Create();
            _window.ApplyConfig(config);
            _window.SetHideInFullscreen(_save.hideInFullscreen);
            _window.SetSizeStep(_save.sizeStep);
            _window.HitTest = ScreenPointInteractive;
            _window.DisplayChanged += OnDisplayChanged;

            _ambience = Ambience.Create();
            _ambience.SetEnabled(_save.soundOn, _save.volume);

            _hud = FindFirstObjectByType<GameHud>() ?? GameHud.Create();
            _pond = PondView.Create();
            _angler = Angler.Create(this, _pond, _window, _hud);
            _critters = Critters.Create(_window, _angler);
            _pond.MaxDepth = _window.MaxPondDepth;
            _pond.SetDepth(_save.pondDepth);
            _pond.DepthChanged += OnDepthChanged;
            _hud.Bind(this, _angler.CatTransform, _angler.BucketTransform, _camera, _window.Zoom);

            _menu = PondMenu.Create(this);
            _stats = StatsPanel.Create(this, config);
            _letter = LetterPanel.Create();

            _angler.BucketClicked = ToggleMenu;
            _angler.CatClicked = PetCat;

            _weatherShown = Weather.Now;
            _pond.SetWeather(_weatherShown);
            StartBond();
            EnsureQuest();
            if (_save.firstRunDay == 0)
                _save.firstRunDay = Stamp(System.DateTime.Now);
            // Sets finished before this update existed still earn their prize.
            AnnounceSets();
            foreach (var a in System.Environment.GetCommandLineArgs())
            {
                if (a == "-visitor")
                    _visitorClock = 8f;
                if (a == "-mail")
                    _save.letterSeconds = LetterEverySeconds - 6f;
            }

            ApplyLooks();
            ApplySettings();
            OnDisplayChanged();

            var forceGift = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-gift") >= 0;
            if (forceGift || !_save.welcomeGiftGiven)
            {
                _save.welcomeGiftGiven = true;
                MarkDirty(false);
                StartCoroutine(WelcomeGift());
            }

            Debug.Log("DeskPond save path: " + SaveSystem.FilePath + " globalHook=" + _input.IsGlobal);
        }

        void StartActivityLog()
        {
            if (config == null || !config.trackActivity || SnapshotMode.Active)
                return;
            _activity = ActivityLog.Load();
            _foreground = new ForegroundWatcher();
        }

        void OnDepthChanged(int depth, bool done)
        {
            _angler.Relayout();
            if (!done)
                return;
            _save.pondDepth = depth;
            MarkDirty(true);
        }

        public void SetPondDepth(int depth)
        {
            _pond.SetDepth(depth);
            OnDepthChanged(_pond.Depth, true);
        }

        void OnDisplayChanged()
        {
            if (_pond == null || _angler == null)
                return;
            _pond.MaxDepth = _window.MaxPondDepth;
            _pond.SetWindowWidth(_window.WorldWidth);
            _angler.Relayout();
            var norm = _save.petNormX >= 0f ? _save.petNormX : 0.62f;
            _angler.SetWorldX(norm * _window.WorldWidth);
            _hud?.Bind(this, _angler.CatTransform, _angler.BucketTransform, _camera, _window.Zoom);
            _menu?.ClampIntoScreen();
            _stats?.ClampIntoScreen();
            _letter?.ClampIntoScreen();
        }

        public void StorePetPosition()
        {
            if (_save == null || _window == null || _window.WorldWidth <= 0f)
                return;
            _save.petNormX = Mathf.Clamp01(_angler.transform.position.x / _window.WorldWidth);
            MarkDirty(false);
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            var dt = Time.deltaTime;
            _pond.TickGrip(_window, _camera);
            AccumulateInput(dt);
            TickFishing(dt);
            TickGift(Time.unscaledDeltaTime);
            TickStreak(dt);
            TickSlow(dt);
            TickVisitors(dt);
            TickLetters(Time.unscaledDeltaTime);
            _petCooldown -= dt;
            _petGiftCooldown -= dt;
            TickMoods(dt);
            TickThief(dt);
            HandleClicks();
            TickActivity(dt);
            TickAutosave(dt);

            var panelOpen = (_menu != null && _menu.IsOpen) || (_stats != null && _stats.IsOpen);
            if (panelOpen || _angler.QteOpen)
                _window.Fast("panel/qte");
            if (_menu != null && _menu.PointerOverMenu())
                _window.PointerOverPet = true;
            if (_stats != null && _stats.PointerOverPanel())
                _window.PointerOverPet = true;

            _critters.SetGullVisiting(_menu != null && _menu.IsOpen);

            // The bucket count shows when a catch lands, or while the cursor rests on the bucket.
            if (_camera != null && _window.CursorValid)
            {
                var c = _window.CursorScreen;
                var w = _camera.ScreenToWorldPoint(new Vector3(c.x, c.y, 0f));
                w.z = 0f;
                if (_angler.BucketContains(w))
                    _hud.FlashBucketCount(0.3f);
                if (_angler.CatContains(w))
                    _hud.FlashGiftTimer(0.3f);
            }
            _ambience.Suspend(_window.HiddenForFullscreen);
        }

        void OnApplicationQuit()
        {
            Persist();
            CommitActivity();
        }

        void OnDestroy()
        {
            if (_window != null)
                _window.DisplayChanged -= OnDisplayChanged;
            if (_dirty)
                Persist();
            CommitActivity();
        }

        void CommitActivity()
        {
            if (_activity == null || config == null)
                return;
            _foreground?.Commit(_activity, RecordTitles);
            _activity.FlushNow(config.retentionDays, config.titleRetentionDays);
        }

        void TickActivity(float dt)
        {
            if (_activity == null || config == null)
                return;
            _foreground?.Tick(dt, _activity, config.foregroundPollSeconds, RecordTitles);
            _activity.Tick(dt, config.retentionDays, config.titleRetentionDays);
        }

        // ------------------------------------------------------------------ input -> bait -> bites

        void AccumulateInput(float dt)
        {
            if (_input == null || _save == null)
                return;

            var events = _input.TakeFrameEvents();
            _pacing?.Tick(dt, events);
            _activity?.AddInputs(events, _foreground?.CurrentApp, RecordTitles ? _foreground?.CurrentTitle : null);

            // Bait in the water follows how busy the user is, and fades when they stop.
            var target = Mathf.Clamp01(_input.Rate / 4f);
            _bait = Mathf.MoveTowards(_bait, target, dt * (target > _bait ? 0.08f : 0.02f));
            _pond.SetBait(_bait);

            if (events <= 0)
            {
                TickIdle(dt);
                return;
            }

            IdleSeconds = 0f;
            _streakLive = true;
            _idleFishGranted = 0;
            _save.lifetimeInputs += events;
            _angler.Crumbs(events);

            if (BasketFull || _biteDue)
                return;
            _save.inputsTowardCatch += events;
            MarkDirty(false);
            if (_save.inputsTowardCatch >= _save.nextCatchTarget)
                _biteDue = true;
        }

        void TickIdle(float dt)
        {
            if (config == null || _save == null)
                return;

            IdleSeconds += dt;
            var earned = IdleSeconds - config.idleThresholdSeconds;
            if (earned <= 0f)
                return;

            var due = Mathf.FloorToInt(earned / Mathf.Max(1f, config.idleSecondsPerFish));
            if (due > _idleFishGranted && !BasketFull)
            {
                _idleFishGranted = due;
                _biteDue = true;
            }

        }

        void TickFishing(float dt)
        {
            if (_angler.IsBusy)
                return;

            _save.dryMinutes += dt / 60f;

            // Line out at all times unless the bucket is full.
            if (!LineOut && !BasketFull)
            {
                _angler.Cast(() => _lineOut = true);
                return;
            }

            _sinceBite += dt;
            if (!_biteDue || BasketFull || !LineOut)
                return;

            // Earned, but the fish take their time: a bite never follows the last one sooner than
            // the minimum gap, so fast typing cannot fill the bucket in a minute.
            var gap = (config != null ? config.minSecondsBetweenBites : 90f) * ShopDefs.RodFactor(_save.rodLevel);
            if (_sinceBite < gap)
                return;
            _sinceBite = 0f;

            _biteDue = false;
            _save.inputsTowardCatch = 0;
            RollNextTarget();

            var night = _pond.IsNight;
            var pondId = Looks.Worn(_save, LookSlot.Pond);
            // A long unbroken stretch of work tilts the odds: rarer tiers, and more golden bites.
            var tier = StreakTier;
            if (_rng.NextDouble() < ShopDefs.GoldenChance(_save.rodLevel) + tier * 0.01f)
            {
                var prize = Catalog.RollLegendary(_rng, pondId, night);
                _angler.PlayGolden(prize, Catalog.Get("boot"), (item, won) =>
                {
                    Land(item);
                    if (won)
                        Ambience.Play(Ambience.Sfx.Coin);
                }, 3.2f);
            }
            else
            {
                var item = Catalog.RollNormal(_rng, pondId, night, _save.lureLevel + tier, _pond.Weather,
                    Weather.Season(System.DateTime.Now));
                _angler.PlayCatch(item, Caption(item), () => Land(item));
            }

            _lineOut = false;
        }

        bool _lineOut;
        bool LineOut => _lineOut;

        void Land(CatchDef item)
        {
            if (item == null)
                return;
            var cm = item.MaxCm > 0f ? Mathf.Round(Mathf.Lerp(item.MinCm, item.MaxCm, (float)_rng.NextDouble()) * 10f) / 10f : 0f;
            _save.Record(item.Id, cm);
            if (!BasketFull)
                _save.catches.Add(item.Id);
            _save.lifetimeFish++;
            _save.dryMinutes = 0f;
            _angler.SetBucketFill(_save.catches.Count, Capacity);
            _hud?.FlashBucketCount(3f);
            AnnounceSets();
            MarkDirty(true);
            _menu?.Refresh();
        }

        /// <summary>Hands out the prize for any dex set that has just been completed.</summary>
        void AnnounceSets()
        {
            var done = DexSets.ClaimFinished(_save);
            if (done == null)
                return;
            foreach (var set in done)
            {
                Debug.Log("DeskPond: dex set complete: " + set.Id);
                _hud?.ShowBubble("Set complete: " + set.Name + "! Got " + set.Reward.Name + ".",
                    _angler != null ? _angler.CatTransform : null, 5f);
            }
            Ambience.Play(Ambience.Sfx.Coin);
            MarkDirty(true);
        }

        // ------------------------------------------------------------------ focus streak

        /// <summary>
        /// Counts the current stretch of work. It keeps running through short pauses and ends
        /// after two idle minutes; ending a long one gets a word from the cat. Only the count of
        /// input events is ever looked at, never what was typed.
        /// </summary>
        void TickStreak(float dt)
        {
            if (_save == null || !_streakLive)
                return;

            if (IdleSeconds < 120f)
            {
                _streak += dt;
                _angler.Excitement = StreakTier / 3f;
                return;
            }

            var seconds = Mathf.Max(0f, _streak - 120f);
            if (seconds >= 900f)
            {
                Notify(Mathf.FloorToInt(seconds / 60f) + " min streak! Nice work.");
                _angler.PlayHappy();
            }
            if (seconds > _save.bestStreak)
            {
                _save.bestStreak = seconds;
                MarkDirty(false);
            }
            _streak = 0f;
            _streakLive = false;
            _angler.Excitement = 0f;
        }

        // ------------------------------------------------------------------ weather, day rollover

        /// <summary>Things that only need looking at every so often: the sky and the date.</summary>
        void TickSlow(float dt)
        {
            _slowPoll -= dt;
            if (_slowPoll > 0f)
                return;
            _slowPoll = 20f;

            var now = Weather.Now;
            if (now != _weatherShown)
            {
                _weatherShown = now;
                _pond.SetWeather(now);
                if (!_angler.IsBusy)
                    Notify(Weather.Announce(now));
            }
            EnsureQuest();
        }

        // ------------------------------------------------------------------ deep-water visitors

        void TickVisitors(float dt)
        {
            if (!_pond.CanHostVisitor)
                return;
            _visitorClock -= dt;
            if (_visitorClock > 0f)
                return;
            _visitorClock = 360f + (float)_rng.NextDouble() * 420f;
            _pond.SpawnVisitor(_rng.Next(2) == 0 ? 1 : 2);
            Debug.Log("DeskPond: something big is passing through the deep water.");
        }

        void GreetVisitor()
        {
            var kind = _pond.ClaimVisitor();
            if (kind == 0)
                return;
            if (kind == 2 && !BasketFull)
            {
                Land(Catalog.Get("pearl"));
                Notify("A jellyfish dropped a pearl!");
            }
            else
            {
                var tip = (kind == 1 ? 40 : 70) + _rng.Next(41);
                _save.coins += tip;
                Notify(kind == 1 ? "The whale left a tip! +" + tip : "The jellyfish left +" + tip + "!");
                MarkDirty(true);
            }
            Ambience.Play(Ambience.Sfx.Coin);
            _angler.PlayHappy();
        }

        // ------------------------------------------------------------------ petting and the bond

        static int Stamp(System.DateTime d) => d.Year * 10000 + d.Month * 100 + d.Day;

        static System.DateTime FromStamp(int s) =>
            new System.DateTime(Mathf.Max(1, s / 10000), Mathf.Clamp(s / 100 % 100, 1, 12), Mathf.Clamp(s % 100, 1, 28));

        /// <summary>The bond fades a little for each day the cat goes unpetted.</summary>
        void StartBond()
        {
            if (_save.lastPetDay <= 0)
                return;
            var today = System.DateTime.Now.Date;
            var days = (int)(today - FromStamp(_save.lastPetDay)).TotalDays;
            if (days < 2)
                return;
            _save.affection = Mathf.Max(0f, _save.affection - (days - 1) * 6f);
            // Move the mark up to yesterday so the same days are not charged again next launch.
            _save.lastPetDay = Stamp(today.AddDays(-1));
            MarkDirty(false);
        }

        void PetCat()
        {
            _angler.PlayPet();
            Ambience.Play(Ambience.Sfx.Meow, 0.8f);
            if (_save == null)
                return;

            if (_petCooldown <= 0f)
            {
                var before = BondLevel;
                var today = Stamp(System.DateTime.Now);
                // The first scratch of the day counts for more.
                _save.affection = Mathf.Min(100f, _save.affection + (_save.lastPetDay == today ? 3f : 8f));
                _save.lastPetDay = today;
                _petCooldown = 20f;
                if (BondLevel > before)
                    Notify("Bond level " + BondLevel + "! Purr.");
                MarkDirty(false);
            }

            // A cat that likes you brings you things.
            if (BondLevel >= 3 && _petGiftCooldown <= 0f && _rng.NextDouble() < 0.25)
            {
                var found = 5 + _rng.Next(16);
                _save.coins += found;
                _petGiftCooldown = 900f;
                Notify("The cat found you " + found + " dried fish.");
                Ambience.Play(Ambience.Sfx.Coin, 0.6f);
                MarkDirty(true);
            }
        }

        // ------------------------------------------------------------------ the gull's daily request

        /// <summary>What a finished request pays, on top of the normal sale price.</summary>
        public int QuestReward
        {
            get
            {
                var d = Catalog.Get(_save != null ? _save.questItem : null);
                return d == null ? 0 : Mathf.Max(40, d.Price * _save.questNeed * 3 + 30);
            }
        }

        /// <summary>Picks today's request. The same day always gives the same one.</summary>
        void EnsureQuest()
        {
            if (_save == null)
                return;
            var today = Stamp(System.DateTime.Now);
            if (_save.questDay == today && Catalog.Get(_save.questItem) != null)
                return;

            var pool = new List<CatchDef>();
            foreach (var d in Catalog.All)
                if ((d.Rarity == Rarity.Common || d.Rarity == Rarity.Uncommon) && !d.Weather.HasValue && d.Season < 0
                    && !d.NightOnly && d.Pond == null)
                    pool.Add(d);
            var day = new System.Random(today);
            var pick = pool[day.Next(pool.Count)];
            _save.questDay = today;
            _save.questItem = pick.Id;
            _save.questNeed = pick.Rarity == Rarity.Common ? 3 + day.Next(2) : 2;
            _save.questHave = 0;
            _save.questDone = false;
            MarkDirty(false);
        }

        void QuestCount(string id, int count)
        {
            if (_save.questDone || count <= 0 || id != _save.questItem)
                return;
            _save.questHave = Mathf.Min(_save.questNeed, _save.questHave + count);
            if (_save.questHave < _save.questNeed)
                return;
            _save.questDone = true;
            var pay = QuestReward;
            _save.coins += pay;
            Notify("Gull: a deal's a deal! +" + pay);
        }

        // ------------------------------------------------------------------ letters from the cat

        /// <summary>Play time between letters. -mailevery=N overrides it for testing.</summary>
        static readonly float LetterEverySeconds = ReadMailOverride();

        static float ReadMailOverride()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("-mailevery=") && float.TryParse(a.Substring(11), out var s))
                    return Mathf.Max(10f, s);
            return 2700f;
        }

        void TickLetters(float dt)
        {
            if (_save == null || _writing || ActiveMail != null || (_letter != null && _letter.IsOpen))
                return;
            _save.letterSeconds += dt;
            // The very first letter comes early, so the bottle is met in the first session.
            var due = _save.letters.Count == 0 ? Mathf.Min(240f, LetterEverySeconds) : LetterEverySeconds;
            if (_save.letterSeconds < due || _angler.IsBusy || _angler.IsDragging)
                return;
            _save.letterSeconds = 0f;
            StartCoroutine(WriteLetter());
        }

        /// <summary>The cat gets its notebook out for a few seconds, then tosses the bottle.</summary>
        System.Collections.IEnumerator WriteLetter()
        {
            _writing = true;
            _noteUntil = Time.time + 4f;
            yield return new WaitForSeconds(4f);
            while (_angler.IsBusy)
                yield return null;

            var ctx = LetterNow();
            var letter = CatLetters.Pick(_save, ctx, _rng);
            if (letter.Answers != null)
                _save.lastReply = null;
            ActiveMail = MailBottle.Spawn(this, _angler.CatTransform, letter, CatLetters.Render(letter, ctx));
            MarkDirty(false);
            _writing = false;
            Debug.Log("DeskPond: the cat sent a letter: " + letter.Id);
        }

        LetterContext LetterNow()
        {
            var now = System.DateTime.Now;
            var found = 0;
            foreach (var d in Catalog.All)
                if (_save.DexCount(d.Id) > 0)
                    found++;
            var last = _save.catches.Count > 0 ? Catalog.Get(_save.catches[_save.catches.Count - 1]) : null;
            return new LetterContext
            {
                Hour = _pond.Hour,
                Weather = _pond.Weather,
                Season = Weather.Season(now),
                Days = _save.firstRunDay > 0 ? Mathf.Max(0, (int)(now.Date - FromStamp(_save.firstRunDay)).TotalDays) + 1 : 1,
                Fish = _save.lifetimeFish,
                DexFound = found,
                DexTotal = Catalog.All.Length,
                StreakMinutes = _streak / 60f,
                BestStreakMinutes = _save.bestStreak / 60f,
                Bond = BondLevel,
                BucketFull = BasketFull,
                DryMinutes = _save.dryMinutes,
                LastCatch = last != null ? last.Name : null,
                Coins = _save.coins,
            };
        }

        Vector2 LetterAnchor()
        {
            var p = _camera.WorldToScreenPoint(_angler.CatTransform.position);
            return new Vector2(p.x, p.y + 34f * _window.Zoom);
        }

        void OpenMail()
        {
            var mail = ActiveMail;
            if (mail == null)
                return;
            var letter = mail.Letter;
            var text = mail.Text;
            mail.Take();
            ActiveMail = null;
            Ambience.Play(Ambience.Sfx.Plop, 0.6f);

            _save.letters.Add(CatLetters.Pack(letter.Id, text));
            while (_save.letters.Count > 40)
                _save.letters.RemoveAt(0);
            MarkDirty(true);

            _letter.Show(text, letter.Replies, LetterAnchor(), tag =>
            {
                if (tag != null)
                {
                    // The cat answers a reply within a couple of minutes, not next hour.
                    _save.lastReply = tag;
                    _save.letterSeconds = Mathf.Max(_save.letterSeconds, LetterEverySeconds - 120f);
                    Notify("Sent it back!");
                    MarkDirty(true);
                }
                _angler.PlayHappy(1f, false);
            });
        }

        /// <summary>Re-reads a kept letter from the mail list.</summary>
        public void ReadLetter(int index)
        {
            if (_save == null || index < 0 || index >= _save.letters.Count)
                return;
            _letter.Show(CatLetters.TextOf(_save.letters[index]), null, LetterAnchor(), null);
        }

        string Caption(CatchDef item)
        {
            switch (item.Id)
            {
                case "bottle":
                    var n = _rng.Next(Catalog.BottleNotes.Length);
                    if (!_save.notesFound.Contains(n))
                        _save.notesFound.Add(n);
                    return "\"" + Catalog.BottleNotes[n] + "\"";
                case "ghost":
                    return "Ghost file: " + GhostName();
                default:
                    return item.Name + "!";
            }
        }

        string GhostName()
        {
            if (_save.recycleGhosts)
            {
                _ghosts ??= RecycleBin.ReadNames(40);
                if (_ghosts.Count > 0)
                    return _ghosts[_rng.Next(_ghosts.Count)];
            }
            return Catalog.GhostNames[_rng.Next(Catalog.GhostNames.Length)];
        }

        void RollNextTarget()
        {
            var level = _save.rodLevel;
            var scale = config != null ? Mathf.Max(0.01f, config.inputCostScale) : 1f;
            if (config != null && config.adaptivePacing && _pacing != null
                && _pacing.TryGetTarget(config.minutesPerFish, ShopDefs.RodFactor(level),
                    config.pacingMinInputs, config.pacingMaxInputs, config.pacingSpread, _rng, out var adaptive))
            {
                _save.nextCatchTarget = Mathf.Max(1, Mathf.RoundToInt(adaptive * scale));
                return;
            }

            var roll = Random.Range(ShopDefs.MinInputs(level), ShopDefs.MaxInputs(level) + 1);
            _save.nextCatchTarget = Mathf.Max(1, Mathf.RoundToInt(roll * scale));
        }

        // ------------------------------------------------------------------ moods and events

        void TickMoods(float dt)
        {
            if (_angler.IsBusy)
                return;

            _moodClock += dt;
            Angler.Mood mood;
            if (Time.time < _noteUntil)
                mood = Angler.Mood.Note;
            else if (BasketFull)
                mood = Angler.Mood.Doze;
            else if (_save.dryMinutes >= 30f)
                // A long dry spell: alternate between doing the books and giving up to sunbathe.
                mood = Mathf.FloorToInt(_moodClock / 180f) % 2 == 0 ? Angler.Mood.Note : Angler.Mood.Sun;
            else if (IdleSeconds > 150f)
                mood = Angler.Mood.Doze;
            else if (_save.affection < 15f)
                // Not petted in days: a cross little face until someone makes it up to them.
                mood = Angler.Mood.Sulk;
            else
                mood = Angler.Mood.Wait;
            _angler.SetMood(mood);
        }

        /// <summary>A full bucket left alone too long attracts a crow.</summary>
        void TickThief(float dt)
        {
            if (!BasketFull || _critters.CrowActive)
            {
                if (!BasketFull)
                    _fullSeconds = 0f;
                return;
            }

            _fullSeconds += dt;
            if (_fullSeconds < 180f)
                return;
            _crowRoll -= dt;
            if (_crowRoll > 0f)
                return;
            _crowRoll = 60f;
            if (_rng.NextDouble() > 0.3)
                return;

            _critters.SendCrow(StealOne, stolen =>
            {
                if (stolen)
                {
                    _angler.PlayAngry();
                    _hud.ShowBubble("Hey! Thief!", _angler.CatTransform, 2.5f);
                }
                else
                {
                    _angler.PlayHappy();
                }
            });
        }

        string StealOne()
        {
            for (var i = _save.catches.Count - 1; i >= 0; i--)
            {
                var def = Catalog.Get(_save.catches[i]);
                if (def == null || !def.IsFish)
                    continue;
                _save.catches.RemoveAt(i);
                _angler.SetBucketFill(_save.catches.Count, Capacity);
                MarkDirty(true);
                _menu?.Refresh();
                return def.Icon;
            }
            return null;
        }

        System.Collections.IEnumerator WelcomeGift()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, config.welcomeGiftDelaySeconds));
            SpawnGift();
        }

        void SpawnGift()
        {
            if (ActiveGift != null || _angler == null)
                return;
            DriftingGift.Spawn(this, _angler.CatTransform, _camera, _pond.SurfaceWorldY,
                config.giftDrift, config.giftDriftSeconds);
        }

        /// <summary>A short message in the cat's speech bubble (sales, purchases).</summary>
        public void Notify(string text) => _hud?.ShowBubble(text, _angler != null ? _angler.CatTransform : null, 2.5f);

        public void ShowGiftBubble(string reward) => _hud?.ShowBubble("Got " + reward + "!", _angler.CatTransform);

        /// <summary>Opens a gift box: a random look the player does not own yet, or dried fish.</summary>
        public string GrantGiftSkin()
        {
            if (!Looks.TryPickUnowned(_save, _rng, out var slot, out var def))
            {
                _save.coins += 250;
                MarkDirty(true);
                return "250 dried fish";
            }

            Looks.Grant(_save, slot, def.Id);
            Looks.Wear(_save, slot, def.Id);
            ApplyLooks();
            MarkDirty(true);
            return def.Name;
        }

        // ------------------------------------------------------------------ clicks

        void HandleClicks()
        {
            if (!_window.PrimaryPressedThisFrame || _camera == null)
                return;
            var screen = _window.CursorScreen;
            if (PointerOnPanel(screen))
                return;

            var world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            world.z = 0f;
            if (_pond.IsDraggingGrip)
                return;
            if (ActiveGift != null && ActiveGift.ContainsWorldPoint(world))
            {
                ActiveGift.OpenNow();
                return;
            }

            if (ActiveMail != null && ActiveMail.ContainsWorldPoint(world))
            {
                OpenMail();
                return;
            }

            if (_pond.VisitorContains(world))
            {
                GreetVisitor();
                return;
            }

            if (_critters.CrowContains(world))
            {
                _critters.Shoo();
                return;
            }

        }

        public bool PointerOnPanel(Vector2 screen) =>
            (_menu != null && _menu.ContainsScreenPoint(screen)) || (_stats != null && _stats.ContainsScreenPoint(screen))
            || (_letter != null && _letter.ContainsScreenPoint(screen));

        bool ScreenPointInteractive(Vector2 screen)
        {
            if (_menu != null && (_menu.IsInteracting || _menu.ContainsScreenPoint(screen)))
                return true;
            if (_stats != null && (_stats.IsInteracting || _stats.ContainsScreenPoint(screen)))
                return true;
            if (_letter != null && _letter.ContainsScreenPoint(screen))
                return true;
            if (_camera == null || _angler == null)
                return false;

            var world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            world.z = 0f;
            if (ActiveGift != null && ActiveGift.ContainsWorldPoint(world))
                return true;
            if (ActiveMail != null && ActiveMail.ContainsWorldPoint(world))
                return true;
            if (_pond.VisitorContains(world))
                return true;
            if (_critters.CrowContains(world) || _pond.GripContains(world))
                return true;
            return _angler.Contains(world) || _angler.IsDragging;
        }

        void ToggleMenu()
        {
            if (_menu == null)
                return;
            _menu.ToggleAt(_camera.WorldToScreenPoint(_angler.BucketTransform.position));
        }

        public void ToggleStats()
        {
            if (_stats == null)
                return;
            var anchor = _camera.WorldToScreenPoint(_angler.BucketTransform.position);
            _stats.ToggleAt(anchor);
        }

        public void ClearActivity()
        {
            _activity?.Clear();
            AppIcons.ClearCache();
        }

        // ------------------------------------------------------------------ shop

        public int BucketValue()
        {
            var total = 0;
            foreach (var id in _save.catches)
            {
                var d = Catalog.Get(id);
                if (d != null)
                    total += d.Price;
            }
            return total;
        }

        public void SellAll()
        {
            if (_save.catches.Count == 0)
                return;
            _save.coins += BucketValue();
            var wanted = 0;
            foreach (var id in _save.catches)
                if (id == _save.questItem)
                    wanted++;
            _save.catches.Clear();
            QuestCount(_save.questItem, wanted);
            _angler.SetBucketFill(0, Capacity);
            Ambience.Play(Ambience.Sfx.Coin);
            MarkDirty(true);
        }

        public void SellAt(int index)
        {
            if (index < 0 || index >= _save.catches.Count)
                return;
            var d = Catalog.Get(_save.catches[index]);
            _save.coins += d != null ? d.Price : 0;
            _save.catches.RemoveAt(index);
            if (d != null)
                QuestCount(d.Id, 1);
            _angler.SetBucketFill(_save.catches.Count, Capacity);
            Ambience.Play(Ambience.Sfx.Coin, 0.6f);
            MarkDirty(true);
        }

        /// <summary>Moves a live fish from the bucket into the desktop tank.</summary>
        public bool MoveToTank(int index)
        {
            if (index < 0 || index >= _save.catches.Count || _save.tank.Count >= ShopDefs.TankCapacity)
                return false;
            var d = Catalog.Get(_save.catches[index]);
            if (d == null || !d.IsFish)
                return false;
            _save.tank.Add(d.Id);
            _save.catches.RemoveAt(index);
            _angler.SetBucketFill(_save.catches.Count, Capacity);
            ApplySettings();
            MarkDirty(true);
            return true;
        }

        public void ReleaseFromTank(int index)
        {
            if (index < 0 || index >= _save.tank.Count)
                return;
            _save.tank.RemoveAt(index);
            ApplySettings();
            MarkDirty(true);
        }

        public bool TryBuyUpgrade(UpgradeKind kind)
        {
            var level = ShopDefs.Level(_save, kind);
            var cost = ShopDefs.NextCost(kind, level);
            if (cost < 0 || _save.coins < cost)
                return false;
            _save.coins -= cost;
            ShopDefs.SetLevel(_save, kind, level + 1);
            if (kind == UpgradeKind.Rod)
                _save.nextCatchTarget = Mathf.Max(_save.inputsTowardCatch + 1,
                    Mathf.RoundToInt(_save.nextCatchTarget * ShopDefs.RodFactor(1)));
            Ambience.Play(Ambience.Sfx.Coin);
            ApplyLooks();
            MarkDirty(true);
            return true;
        }

        /// <summary>Wardrobe tile clicked: buy it if needed, then wear it.</summary>
        public bool TryWear(LookSlot slot, LookDef def)
        {
            if (def == null)
                return false;
            if (slot == LookSlot.Pond && def is PondDef pd)
            {
                var boat = Looks.Find(Looks.Boats, Looks.Worn(_save, LookSlot.Boat));
                if (boat.Tier < pd.MinBoatTier)
                    return false;
            }

            if (!Looks.Owns(_save, slot, def.Id))
            {
                if (def.RewardOnly || _save.coins < def.Cost)
                    return false;
                _save.coins -= def.Cost;
                Looks.Grant(_save, slot, def.Id);
                Ambience.Play(Ambience.Sfx.Coin);
            }

            Looks.Wear(_save, slot, def.Id);
            ApplyLooks();
            _angler.PlayHappy(0.8f, slot != LookSlot.Pond);
            MarkDirty(true);
            return true;
        }

        void ApplyLooks()
        {
            _angler.ApplyLooks(_save);
            _pond.SetTheme(Looks.Find(Looks.Ponds, Looks.Worn(_save, LookSlot.Pond)));
        }

        // ------------------------------------------------------------------ settings

        public void ApplySettings()
        {
            _pond.SetFullWidth(_save.fullPond);
            _window.SetPondMode(_save.fullPond);
            _ambience.SetEnabled(_save.soundOn, _save.volume);
            _window.SetHideInFullscreen(_save.hideInFullscreen);
        }

        public void SetSound(bool on) { _save.soundOn = on; ApplySettings(); MarkDirty(true); }
        public void SetVolume(float v) { _save.volume = Mathf.Clamp01(v); ApplySettings(); MarkDirty(true); }
        public void SetFullPond(bool on) { _save.fullPond = on; ApplySettings(); MarkDirty(true); }
        public void SetHideFullscreen(bool on) { _save.hideInFullscreen = on; ApplySettings(); MarkDirty(true); }

        public void SetRecycleGhosts(bool on)
        {
            _save.recycleGhosts = on;
            _ghosts = null;
            MarkDirty(true);
        }

        public void SetSizeStep(int step)
        {
            _save.sizeStep = Mathf.Clamp(step, -2, 3);
            _window.SetSizeStep(_save.sizeStep);
            MarkDirty(true);
        }

        public void Quit()
        {
            Persist();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ persistence

        public void MarkDirty(bool now)
        {
            _dirty = true;
            if (now)
            {
                Persist();
                _menu?.Refresh();
            }
        }

        void TickAutosave(float dt)
        {
            if (!_dirty)
                return;
            _saveCooldown -= dt;
            if (_saveCooldown <= 0f)
                Persist();
        }

        void Persist()
        {
            if (_save == null)
                return;
            SaveSystem.Write(_save);
            _dirty = false;
            _saveCooldown = 10f;
        }

        /// <summary>Snapshot mode: dress the pet and set the scene without spending anything.</summary>
        public void DebugLook(string coat, string hat, string boat, string pond, int depth, float hour, bool menu,
            PondMenu.Tab tab = PondMenu.Tab.Wardrobe)
        {
            Looks.Wear(_save, LookSlot.Coat, coat);
            Looks.Wear(_save, LookSlot.Hat, hat);
            Looks.Wear(_save, LookSlot.Boat, boat);
            Looks.Wear(_save, LookSlot.Pond, pond);
            _pond.SetHourOverride(hour);
            _save.catches.Clear();
            _save.catches.AddRange(new[] { "carp", "koi", "boot", "bluegill", "bottle" });
            ApplyLooks();
            SetPondDepth(depth);
            _bait = 0.8f;
            _save.tank.Clear();
            _save.tank.AddRange(new[] { "koi", "goldfish", "golden_koi", "bluegill" });
            if (menu)
                _menu.OpenTab(tab, _camera.WorldToScreenPoint(_angler.BucketTransform.position));
            else
                _menu.Close();
        }

        /// <summary>Snapshot mode: a sky, something in the deep water, and a letter waiting.</summary>
        public void DebugScene(WeatherKind weather, int visitor, bool mail)
        {
            Weather.Forced = weather;
            _weatherShown = weather;
            _pond.SetWeather(weather);
            _pond.ClearVisitor();
            if (visitor != 0)
                _pond.SpawnVisitor(visitor);
            if (mail && ActiveMail == null)
            {
                var letter = CatLetters.Get("q_day");
                ActiveMail = MailBottle.Spawn(this, _angler.CatTransform, letter, letter.Text);
            }
        }

        public void DebugOpenMail() => OpenMail();

        public void DebugCloseLetter() => _letter.Close();

        public void DebugPet() => PetCat();

        [ContextMenu("Force Bite")]
        void ContextBite() => _biteDue = true;

        [ContextMenu("Add 1000 Coins")]
        void ContextCoins()
        {
            _save.coins += 1000;
            MarkDirty(true);
        }
    }
}
