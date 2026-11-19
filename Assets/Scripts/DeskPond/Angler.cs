using System;
using System.Collections;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// The pet on the water: boat, cat, hat, fish bucket, lantern, rod, line and bobber, and the
    /// fishing sequence that ties them together. Owns no game state; GameSession decides what is
    /// caught and when, and this makes it look like the cat did it.
    ///
    /// Layout is in art pixels relative to the root, which sits on the floor (y = 0) at the
    /// boat's centre. Every placement is snapped to the art-pixel grid.
    /// </summary>
    public class Angler : MonoBehaviour
    {
        // Cat sheet frames: 0-3 tail sway, then moods painted over frame 0.
        const int Blink = 4, Happy = 5, Wide = 6, Squint = 7, Cross = 8;

        public enum Mood
        {
            Wait,
            Doze,
            Note,
            Sun,
            Angry,
            Busy,
            Sulk
        }

        GameSession _session;
        PondView _pond;
        DesktopWindow _window;
        GameHud _hud;

        SpriteRenderer _boatBack, _boatFront, _cat, _hat, _glasses, _bucket, _fill, _lantern, _glow;
        SpriteRenderer _bobber, _mark, _held, _note, _zz, _heart;
        float _heartClock = -1f;

        /// <summary>0..1: how worked up the cat is by a long work stretch. Speeds the tail.</summary>
        public float Excitement { get; set; }
        SpriteRenderer _lineSr;
        Texture2D _lineTex;
        Color32[] _linePx;
        const int LineW = 120, LineH = 72, LineOx = 92; // canvas, and root's x inside it

        Atlas.Anim _catAnim, _lanternAnim;
        BoatDef _boat = Looks.Boats[0];
        LookDef _hatDef = Looks.Hats[0];
        int _rodLevel;

        // Pose, in art px relative to the root.
        int _catDip;
        Vector2Int _catPos;
        Vector2 _rodBase;
        Vector2 _rodTip;
        Vector2 _bobberPos;     // where the bobber is now
        Vector2 _castPoint;     // where it rests
        bool _lineOut;
        float _lineSag = 4f;

        float _clock;
        float _blinkTimer = 3f;
        float _blinkLeft;
        float _waitSeconds;
        int _faceOverride = -1;
        float _hop;              // art px, added to the cat
        float _shake;            // seconds left of a nervous shake
        bool _busy;
        bool _qteOpen;
        bool _qteHit;
        float _zzClock;
        Mood _mood = Mood.Wait;
        readonly System.Random _rng = new System.Random();

        // Dragging the pet along the water.
        bool _grabbed, _dragging;
        Vector2 _grabScreen;
        float _grabOffset;
        float _baitClock;

        public bool IsBusy => _busy;
        public bool QteOpen => _qteOpen;
        public Transform CatTransform => _cat != null ? _cat.transform : transform;
        public Transform BucketTransform => _bucket != null ? _bucket.transform : transform;
        public bool IsDragging => _dragging;

        /// <summary>Fired on a click (not a drag) on the bucket.</summary>
        public Action BucketClicked;

        /// <summary>Fired on a click (not a drag) on the cat.</summary>
        public Action CatClicked;

        public static Angler Create(GameSession session, PondView pond, DesktopWindow window, GameHud hud)
        {
            var go = new GameObject("PetRoot");
            var a = go.AddComponent<Angler>();
            a._session = session;
            a._pond = pond;
            a._window = window;
            a._hud = hud;
            a.Build();
            return a;
        }

        void Build()
        {
            var t = transform;
            _boatBack = Atlas.MakeRenderer("BoatBack", t, 5);
            _bobber = Atlas.MakeRenderer("Bobber", t, 8);
            _lineSr = Atlas.MakeRenderer("RodAndLine", t, 9);
            _cat = Atlas.MakeRenderer("Cat", t, 10);
            _note = Atlas.MakeRenderer("Notebook", t, 11);
            _hat = Atlas.MakeRenderer("Hat", t, 12);
            _glasses = Atlas.MakeRenderer("Glasses", t, 13);
            _boatFront = Atlas.MakeRenderer("BoatFront", t, 14);
            _fill = Atlas.MakeRenderer("BucketFill", t, 15);
            _bucket = Atlas.MakeRenderer("Bucket", t, 16);
            _glow = Atlas.MakeRenderer("LanternGlow", t, 7);
            _lantern = Atlas.MakeRenderer("Lantern", t, 18);
            _held = Atlas.MakeRenderer("Held", t, 22);
            _mark = Atlas.MakeRenderer("Mark", t, 25);
            _zz = Atlas.MakeRenderer("Zz", t, 25);
            _heart = Atlas.MakeRenderer("Heart", t, 25);
            _heart.sprite = Atlas.Get("heart");
            _heart.enabled = false;

            _note.sprite = Atlas.Get("notebook");
            _note.enabled = false;
            _held.enabled = false;
            _mark.enabled = false;
            _zz.sprite = Atlas.Get("zz");
            _zz.enabled = false;
            _lanternAnim = Atlas.GetAnim("lantern");
            _glow.sprite = GlowSprite();
            _glow.enabled = false;

            _lineTex = new Texture2D(LineW, LineH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "RodLine"
            };
            _linePx = new Color32[LineW * LineH];
            _lineSr.sprite = Sprite.Create(_lineTex, new Rect(0, 0, LineW, LineH),
                new Vector2((float)LineOx / LineW, 0f), Atlas.Ppu, 0, SpriteMeshType.FullRect);
        }

        // ------------------------------------------------------------------ looks

        public void ApplyLooks(SaveData save)
        {
            if (save == null)
                return;
            _catAnim = Atlas.GetAnim("cat_" + Looks.Worn(save, LookSlot.Coat)) ?? Atlas.GetAnim("cat_ginger");
            _boat = Looks.Find(Looks.Boats, Looks.Worn(save, LookSlot.Boat));
            _hatDef = Looks.Find(Looks.Hats, Looks.Worn(save, LookSlot.Hat));
            _rodLevel = save.rodLevel;

            _boatBack.sprite = Atlas.Get("boat_" + _boat.Id + "_back");
            _boatFront.sprite = Atlas.Get("boat_" + _boat.Id + "_front");
            _bucket.sprite = Atlas.Get("bucket_" + Looks.Worn(save, LookSlot.Bucket));
            _catDip = _boat.Id == "raft" ? 2 : _boat.Id == "sail" || _boat.Id == "paper" ? 4 : 5;

            var hatId = _hatDef.Id;
            _hat.enabled = hatId != "none" && hatId != "sun";
            _glasses.enabled = hatId == "sun";
            if (_hat.enabled)
                _hat.sprite = Atlas.Get(_hatDef.Icon);
            if (_glasses.enabled)
                _glasses.sprite = Atlas.Get("glasses_sun");

            SetBucketFill(save.catches != null ? save.catches.Count : 0, ShopDefs.Capacity(save.basketLevel));
            LayoutStatic();
        }

        public void SetBucketFill(int count, int capacity)
        {
            if (_fill == null)
                return;
            var k = capacity > 0 ? (float)count / capacity : 0f;
            _fill.enabled = k > 0.2f;
            _fill.sprite = Atlas.Get(k >= 1f ? "bucket_fill_full" : "bucket_fill_half");
        }

        int GunwaleY => _pond.SurfaceY - _boat.Sink + (_boatFront.sprite != null ? (int)_boatFront.sprite.rect.height : 8);

        void LayoutStatic()
        {
            var g = GunwaleY;
            Place(_boatFront, 0, _pond.SurfaceY - _boat.Sink);
            Place(_boatBack, 0, _boat.Id == "sail" ? g - 3 : g - 4);

            _catPos = new Vector2Int(_boat.CatX, g - _catDip);
            Place(_bucket, _boat.BucketX, _pond.SurfaceY + _boat.BucketY - (_boat.Id == "tub" ? 3 : 0));
            Place(_fill, _boat.BucketX + 1, _pond.SurfaceY + _boat.BucketY - (_boat.Id == "tub" ? 3 : 0) + 8);
            Place(_lantern, _boat.LanternX, g + _boat.LanternY);
            Place(_glow, _boat.LanternX, g + _boat.LanternY + 6);

            _rodBase = new Vector2(_catPos.x - 5, _catPos.y + 9);
        }

        void Place(SpriteRenderer sr, float x, float y) =>
            sr.transform.localPosition = new Vector3(x / Atlas.Ppu, y / Atlas.Ppu, 0f);

        // ------------------------------------------------------------------ public moments

        /// <summary>Puts the pet at a world x, keeping the rod and cast inside the pond.</summary>
        public void SetWorldX(float x)
        {
            var w = _window != null ? _window.WorldWidth : 40f;
            var minX = 72f / Atlas.Ppu;
            var maxX = Mathf.Max(minX, w - 34f / Atlas.Ppu);
            x = Mathf.Clamp(x, minX, maxX);
            transform.position = Atlas.Snap(new Vector3(x, 0f, 0f));
            _pond.SetCat(transform.position.x);
            _window?.Follow(transform.position.x, false);
        }

        public void SetMood(Mood mood) => _mood = mood;

        /// <summary>The water moved (depth changed): re-seat everything on the new surface.</summary>
        public void Relayout()
        {
            LayoutStatic();
            _castPoint.y = _pond.SurfaceY - 1;
            if (!_lineOut)
                _bobberPos = _castPoint;
        }

        public void Cast(Action done = null)
        {
            if (_busy)
                return;
            StartCoroutine(CastRoutine(done));
        }

        /// <summary>An ordinary bite: dip, reel, show the catch, drop it in the bucket.</summary>
        public void PlayCatch(CatchDef item, string caption, Action landed)
        {
            if (_busy)
                return;
            StartCoroutine(CatchRoutine(item, caption, landed));
        }

        /// <summary>
        /// A golden bite: the bobber thrashes and a gold "!" waits for a click. Hit, and the
        /// prize is landed; miss, and the fish tows the cat along the water and leaves a boot.
        /// </summary>
        public void PlayGolden(CatchDef prize, CatchDef consolation, Action<CatchDef, bool> landed, float window)
        {
            if (_busy)
                return;
            StartCoroutine(GoldenRoutine(prize, consolation, landed, window));
        }

        /// <summary>The player clicked during a golden bite.</summary>
        public void HitQte()
        {
            if (_qteOpen)
                _qteHit = true;
        }

        public void PlayAngry(float seconds = 3f)
        {
            _faceOverride = Cross;
            _shake = seconds;
            StartCoroutine(ClearFace(seconds));
        }

        public void PlayHappy(float seconds = 1.2f, bool hop = true)
        {
            _faceOverride = Happy;
            if (hop)
                StartCoroutine(Hop(3f, 0.35f));
            StartCoroutine(ClearFace(seconds));
        }

        /// <summary>Petted: a happy face and a heart that floats up off the cat.</summary>
        public void PlayPet()
        {
            PlayHappy(1.4f, false);
            _heartClock = 0f;
        }

        public void Crumbs(int events)
        {
            if (!_lineOut || events <= 0)
                return;
            _baitClock -= events * 0.12f;
            if (_baitClock > 0f)
                return;
            _baitClock = 0.35f;
            var from = transform.position + new Vector3((_catPos.x - 6) / Atlas.Ppu, (_catPos.y + 10) / Atlas.Ppu, 0f);
            _pond.Crumb(from.x, from.y, transform.position.x + _castPoint.x / Atlas.Ppu);
        }

        IEnumerator ClearFace(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _faceOverride = -1;
        }

        IEnumerator Hop(float height, float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                _hop = Mathf.Sin(t / seconds * Mathf.PI) * height;
                yield return null;
            }
            _hop = 0f;
        }

        // ------------------------------------------------------------------ sequences

        IEnumerator CastRoutine(Action done)
        {
            _busy = true;
            _lineOut = false;
            _bobber.enabled = false;
            _faceOverride = Squint;

            // Wind up: rod back over the shoulder, then whip forward.
            yield return SwingRod(new Vector2(10, 18), 0.22f);
            yield return SwingRod(new Vector2(-24, 14), 0.14f);

            var dist = 26 + _rng.Next(16);
            _castPoint = new Vector2(_catPos.x - dist, _pond.SurfaceY - 1);
            var from = _rodTip;
            _bobber.enabled = true;
            _bobber.sprite = Atlas.Get("bobber");
            _lineOut = true;
            for (var t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                var k = t / 0.45f;
                _bobberPos = Vector2.Lerp(from, _castPoint, k) + new Vector2(0f, Mathf.Sin(k * Mathf.PI) * 10f);
                _lineSag = 1f;
                yield return null;
            }

            _bobberPos = _castPoint;
            _lineSag = 4f;
            _pond.Ripple(WorldX(_castPoint.x));
            Ambience.Play(Ambience.Sfx.Plop);
            _faceOverride = Happy;
            yield return new WaitForSeconds(0.5f);
            _faceOverride = -1;
            _waitSeconds = 0f;
            _busy = false;
            done?.Invoke();
        }

        IEnumerator SwingRod(Vector2 tipOffset, float seconds)
        {
            var start = _rodTip == Vector2.zero ? RestTip() : _rodTip;
            var end = _rodBase + tipOffset;
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                _rodTip = Vector2.Lerp(start, end, t / seconds);
                yield return null;
            }
            _rodTip = end;
        }

        Vector2 RestTip() => _rodBase + new Vector2(-17, 13);

        IEnumerator Bite(bool gold, float seconds)
        {
            var wasAsleep = _mood == Mood.Doze || _mood == Mood.Sun || _mood == Mood.Note;
            _mood = Mood.Busy;
            _note.enabled = false;
            _zz.enabled = false;
            _mark.sprite = Atlas.Get(gold ? "mark_gold" : "mark_bite");
            _mark.enabled = true;
            _faceOverride = Wide;
            if (wasAsleep)
            {
                // Startled awake: jump, fur up.
                StartCoroutine(Hop(4f, 0.3f));
                _shake = 0.4f;
            }

            if (gold)
            {
                _bobber.sprite = Atlas.Get("bobber_gold");
                Ambience.Play(Ambience.Sfx.Splash);
            }

            var splash = 0f;
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var dip = gold ? (Mathf.Sin(t * 40f) > 0f ? -3 : 1) : (Mathf.Sin(t * 14f) > 0.3f ? -2 : 0);
                _bobberPos = _castPoint + new Vector2(gold ? Mathf.Sin(t * 23f) * 2f : 0f, dip);
                _rodTip = RestTip() + new Vector2(0f, dip * 0.6f);
                splash -= Time.deltaTime;
                if (gold && splash <= 0f)
                {
                    splash = 0.35f;
                    _pond.Splash(WorldX(_castPoint.x), 5);
                }

                if (gold && _qteHit)
                    break;
                _mark.enabled = !gold || Mathf.Repeat(t, 0.4f) < 0.28f;
                PlaceMark();
                yield return null;
            }

            _mark.enabled = false;
        }

        IEnumerator CatchRoutine(CatchDef item, string caption, Action landed)
        {
            _busy = true;
            yield return Bite(false, 0.7f);
            yield return Reel(item, caption, landed);
        }

        IEnumerator GoldenRoutine(CatchDef prize, CatchDef consolation, Action<CatchDef, bool> landed, float window)
        {
            _busy = true;
            _qteHit = false;
            _qteOpen = true;
            yield return Bite(true, window);
            _qteOpen = false;

            if (_qteHit)
            {
                Ambience.Play(Ambience.Sfx.Meow);
                yield return Reel(prize, "LEGENDARY! " + prize.Name, () => landed(prize, true), big: true);
                yield break;
            }

            // Missed it: the fish takes off and tows the cat along the pond.
            _faceOverride = Wide;
            _shake = 2.6f;
            var dir = transform.position.x > 160f / Atlas.Ppu ? -1f : 1f;
            var speed = 34f / Atlas.Ppu;
            for (var t = 0f; t < 2.6f; t += Time.deltaTime)
            {
                SetWorldX(transform.position.x + dir * speed * Time.deltaTime);
                _rodTip = RestTip() + new Vector2(dir * 4f, -3f + Mathf.Sin(t * 30f));
                _bobberPos = _castPoint + new Vector2(Mathf.Sin(t * 20f) * 2f, -2f);
                if (Mathf.Repeat(t, 0.3f) < Time.deltaTime * 1.5f)
                    _pond.Splash(transform.position.x + (_catPos.x - 8 * dir) / Atlas.Ppu, 3);
                yield return null;
            }

            _session?.StorePetPosition();
            yield return Reel(consolation, "...a boot.", () => landed(consolation, false));
            _faceOverride = Blink;
            yield return new WaitForSeconds(1.2f);
            _faceOverride = -1;
        }

        IEnumerator Reel(CatchDef item, string caption, Action landed, bool big = false)
        {
            _faceOverride = Squint;
            Ambience.Play(Ambience.Sfx.Reel);
            var from = _castPoint;
            for (var t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                var k = t / 0.8f;
                _rodTip = RestTip() + new Vector2(6f * k, 6f * k + Mathf.Sin(t * 35f) * 1.2f);
                _bobberPos = Vector2.Lerp(from, from + new Vector2(10, 0), k);
                _lineSag = 0f;
                yield return null;
            }

            // Out of the water and up over the cat's head.
            _pond.Splash(WorldX(_bobberPos.x), big ? 14 : 8);
            Ambience.Play(Ambience.Sfx.Splash);
            _bobber.enabled = false;
            _held.sprite = Atlas.Get(item.Icon, new Vector2(0.5f, 0.5f));
            _held.enabled = true;
            var showAt = new Vector2(_catPos.x, _catPos.y + 32);
            var start = _bobberPos;
            for (var t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                var k = t / 0.5f;
                var p = Vector2.Lerp(start, showAt, k) + new Vector2(0f, Mathf.Sin(k * Mathf.PI) * 14f);
                Place(_held, p.x, p.y);
                _held.transform.localRotation = Quaternion.Euler(0f, 0f, (1f - k) * 70f);
                _bobberPos = p + new Vector2(0, 4);
                yield return null;
            }

            _held.transform.localRotation = Quaternion.identity;
            _lineOut = false;
            _rodTip = _rodBase + new Vector2(-8, 16);
            _faceOverride = Happy;
            StartCoroutine(Hop(big ? 5f : 3f, 0.4f));
            if (_hud != null)
                _hud.ShowBubble(caption, _held.transform, big ? 3.2f : 2.2f);
            if (item.Rarity >= Rarity.Rare)
                Ambience.Play(Ambience.Sfx.Meow);

            for (var t = 0f; t < (big ? 2.2f : 1.3f); t += Time.deltaTime)
            {
                var bob = Mathf.Sin(t * 6f) * 1.2f;
                Place(_held, showAt.x, showAt.y + bob);
                yield return null;
            }

            // Into the bucket.
            var bucket = new Vector2(_boat.BucketX + 1, _bucket.transform.localPosition.y * Atlas.Ppu + 10);
            for (var t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                var k = t / 0.45f;
                var p = Vector2.Lerp(showAt, bucket, k) + new Vector2(0f, Mathf.Sin(k * Mathf.PI) * 8f);
                Place(_held, p.x, p.y);
                _held.transform.localRotation = Quaternion.Euler(0f, 0f, -k * 100f);
                yield return null;
            }

            _held.enabled = false;
            _held.transform.localRotation = Quaternion.identity;
            Ambience.Play(Ambience.Sfx.Plop);
            landed?.Invoke();
            _faceOverride = -1;
            _rodTip = RestTip();
            _busy = false;
            _mood = Mood.Wait;
        }

        float WorldX(float localArtX) => transform.position.x + localArtX / Atlas.Ppu;

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            var dt = Time.deltaTime;
            _clock += dt;
            if (!_busy && _lineOut)
                _waitSeconds += dt;

            TickDrag();
            TickBoat();
            TickCat(dt);
            TickBobber();
            TickLantern();
            DrawLine();
            var hull = _boatFront.bounds;
            _pond.SetBoatSpan(hull.min.x, hull.max.x, hull.min.y);

            if (_busy)
                _window.Fast("angler-busy");
            else if (_shake > 0f)
                _window.Fast("angler-shake");
            else if (_hud != null && _hud.BubbleVisible)
                _window.Fast("bubble");
        }

        void TickBoat()
        {
            // A slow one-pixel bob on the water.
            var bob = Mathf.Sin(_clock * 1.6f) > 0.2f ? 1 : 0;
            var lift = new Vector3(0f, bob / Atlas.Ppu, 0f);
            _boatFront.transform.localPosition = Atlas.Snap(new Vector3(0f, (_pond.SurfaceY - _boat.Sink) / Atlas.Ppu, 0f) + lift);
        }

        void TickCat(float dt)
        {
            if (_catAnim == null)
                return;

            var frame = TailFrame();
            switch (_mood)
            {
                case Mood.Doze:
                case Mood.Sun:
                    frame = _mood == Mood.Sun ? Happy : Blink;
                    break;
                case Mood.Note:
                    frame = Squint;
                    break;
                case Mood.Angry:
                case Mood.Sulk:
                    frame = Cross;
                    break;
            }

            // Blinks while awake and waiting.
            if (_mood == Mood.Wait)
            {
                _blinkTimer -= dt;
                if (_blinkTimer <= 0f)
                {
                    _blinkLeft = 0.14f;
                    _blinkTimer = 2.5f + (float)_rng.NextDouble() * 4f;
                }
                if (_blinkLeft > 0f)
                {
                    _blinkLeft -= dt;
                    frame = Blink;
                }
            }

            if (_faceOverride >= 0)
                frame = _faceOverride;
            _cat.sprite = _catAnim.Frames[Mathf.Clamp(frame, 0, _catAnim.Frames.Length - 1)];

            var shake = 0;
            if (_shake > 0f)
            {
                _shake -= dt;
                shake = Mathf.Sin(_clock * 60f) > 0f ? 1 : -1;
            }

            var bob = _mood == Mood.Doze ? (Mathf.Sin(_clock * 1.5f) > 0f ? -1 : 0) : 0;
            var boatBob = Mathf.Sin(_clock * 1.6f) > 0.2f ? 1 : 0;
            var y = _catPos.y + Mathf.RoundToInt(_hop) + bob + boatBob;
            Place(_cat, _catPos.x + shake, y);
            var hatId = _hatDef.Id;
            if (hatId == "bow")
                Place(_hat, _catPos.x + 4.5f + shake, y + 17);           // tucked at the right ear
            else
                Place(_hat, _catPos.x - 1 + shake, y + (hatId == "flower" || hatId == "can" ? 17 : 15)); // brim just above the eyes
            Place(_glasses, _catPos.x - 2 + shake, y + 12);

            _note.enabled = _mood == Mood.Note;
            if (_note.enabled)
                Place(_note, _catPos.x - 1, y + 6 + (Mathf.Sin(_clock * 3f) > 0f ? 1 : 0));

            var sleepy = _mood == Mood.Doze || _mood == Mood.Sun;
            _zz.enabled = sleepy;
            if (sleepy)
            {
                _zzClock += dt;
                var k = Mathf.Repeat(_zzClock, 2.4f) / 2.4f;
                Place(_zz, _catPos.x + 8 + k * 6f, y + 22 + k * 10f);
                _zz.color = new Color(1f, 1f, 1f, 1f - k);
            }

            if (_mark.enabled)
                PlaceMark();

            _heart.enabled = _heartClock >= 0f;
            if (_heart.enabled)
            {
                _heartClock += dt;
                var k = _heartClock / 1.1f;
                Place(_heart, _catPos.x + 7 + Mathf.RoundToInt(Mathf.Sin(k * 6f) * 1.5f), y + 20 + Mathf.RoundToInt(k * 12f));
                _heart.color = new Color(1f, 1f, 1f, Mathf.Clamp01(2f - k * 2f));
                _window?.Fast("heart");
                if (k >= 1f)
                    _heartClock = -1f;
            }
        }

        int TailFrame()
        {
            // Ping-pong through the four tail frames at 4 fps: 0 1 2 3 2 1.
            var step = Mathf.FloorToInt(_clock * (4f + Excitement * 5f)) % 6;
            return step < 4 ? step : 6 - step;
        }

        void PlaceMark()
        {
            var y = _cat.transform.localPosition.y * Atlas.Ppu + 27;
            Place(_mark, _catPos.x + 9, y + (Mathf.Sin(_clock * 12f) > 0 ? 1 : 0));
        }

        void TickBobber()
        {
            if (!_lineOut)
            {
                _bobber.enabled = false;
                if (!_busy)
                    _rodTip = RestTip();
                return;
            }

            _bobber.enabled = true;
            if (!_busy)
            {
                // Floating: a lazy bob, a nudge now and then.
                var bob = Mathf.Sin(_clock * 2.1f) > 0.5f ? -1 : 0;
                _bobberPos = _castPoint + new Vector2(0, bob);
                _rodTip = RestTip() + new Vector2(0, Mathf.Sin(_clock * 0.9f) > 0.6f ? 1 : 0);
            }

            Place(_bobber, Mathf.Round(_bobberPos.x), Mathf.Round(_bobberPos.y) - 2);
            _pond.SetBobber(WorldX(_castPoint.x));
        }

        void TickLantern()
        {
            var dark = _pond.Darkness;
            var on = dark > 0.25f;
            _lantern.enabled = true;
            _lantern.sprite = on ? _lanternAnim.Frames[_lanternAnim.FrameAt(_clock)] : Atlas.Get("lantern_off");
            _lantern.enabled = on || _pond.Hour > 16.5f;
            _glow.enabled = on;
            if (on)
                _glow.color = new Color(1f, 1f, 1f, Mathf.Clamp01(dark) * (Mathf.Sin(_clock * 7f) > -0.6f ? 0.85f : 0.65f));
            _pond.SetLantern(on ? WorldX(_boat.LanternX) : -1f);
        }

        // ------------------------------------------------------------------ rod and line

        int _lineKey = int.MinValue;

        void DrawLine()
        {
            // Only redraw and re-upload when something moved; most ticks nothing has.
            var tipK = _rodTip == Vector2.zero ? RestTip() : _rodTip;
            var bobK = Mathf.RoundToInt(_cat.transform.localPosition.y * Atlas.Ppu);
            unchecked
            {
                var key = Mathf.RoundToInt(tipK.x) * 73856093 ^ Mathf.RoundToInt(tipK.y) * 19349663
                          ^ Mathf.RoundToInt(_bobberPos.x) * 83492791 ^ Mathf.RoundToInt(_bobberPos.y) * 2654435
                          ^ bobK * 40503 ^ (_lineOut ? 1 : 0) ^ Mathf.RoundToInt(_lineSag) * 977 ^ _rodLevel * 31
                          ^ Mathf.RoundToInt(_rodBase.x) * 7919;
                if (key == _lineKey)
                    return;
                _lineKey = key;
            }

            Array.Clear(_linePx, 0, _linePx.Length);
            var rod = RodColor(_rodLevel);
            var rodDark = new Color32((byte)(rod.r * 0.6f), (byte)(rod.g * 0.6f), (byte)(rod.b * 0.6f), 255);
            var tip = _rodTip == Vector2.zero ? RestTip() : _rodTip;
            var catBob = Mathf.RoundToInt(_cat.transform.localPosition.y * Atlas.Ppu) - _catPos.y;
            var b = _rodBase + new Vector2(0, catBob);
            var t = tip + new Vector2(0, catBob);

            Line(b, t, rod);
            Line(b + new Vector2(0, -1), t + new Vector2(0, -1), rodDark);
            // Cork handle.
            Line(b, Vector2.Lerp(b, t, 0.18f), new Color32(0x8a, 0x52, 0x30, 255));

            if (_lineOut)
            {
                var end = _bobberPos + new Vector2(0, 3);
                var line = new Color32(240, 244, 255, 200);
                var steps = Mathf.CeilToInt(Vector2.Distance(t, end));
                var prev = t;
                for (var i = 1; i <= steps; i++)
                {
                    var k = (float)i / steps;
                    var p = Vector2.Lerp(t, end, k) - new Vector2(0, Mathf.Sin(k * Mathf.PI) * _lineSag);
                    Line(prev, p, line);
                    prev = p;
                }
            }

            _lineTex.SetPixels32(_linePx);
            _lineTex.Apply(false, false);
        }

        static Color32 RodColor(int level)
        {
            switch (level)
            {
                case 0: return new Color32(0xc8, 0x9a, 0x5a, 255);
                case 1: return new Color32(0xd9, 0x5a, 0x5a, 255);
                case 2: return new Color32(0x5a, 0x8a, 0xd8, 255);
                case 3: return new Color32(0x4a, 0x4a, 0x5a, 255);
                default: return new Color32(0xf6, 0xc8, 0x40, 255);
            }
        }

        void Line(Vector2 a, Vector2 b, Color32 c)
        {
            int x0 = Mathf.RoundToInt(a.x) + LineOx, y0 = Mathf.RoundToInt(a.y);
            int x1 = Mathf.RoundToInt(b.x) + LineOx, y1 = Mathf.RoundToInt(b.y);
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            var err = dx + dy;
            for (var guard = 0; guard < 400; guard++)
            {
                if (x0 >= 0 && x0 < LineW && y0 >= 0 && y0 < LineH)
                    _linePx[y0 * LineW + x0] = c;
                if (x0 == x1 && y0 == y1)
                    break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        static Sprite GlowSprite()
        {
            // A pixel-art halo: solid warm pixels, thinned out by an ordered dither toward the edge,
            // rather than a translucent blob (which reads as brown over a dark desktop).
            const int n = 30;
            int[] bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                var density = d < 0.3f ? 0.55f : d < 0.6f ? 0.3f : d < 1f ? 0.1f : 0f;
                var on = (bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f < density;
                px[y * n + x] = on ? new Color32(255, 226, 150, 255) : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), Atlas.Ppu);
        }

        // ------------------------------------------------------------------ hit testing and drag

        Bounds WorldBounds(SpriteRenderer sr, float margin)
        {
            var b = sr.bounds;
            b.Expand(new Vector3(margin, margin, 1000f));
            return b;
        }

        public bool CatContains(Vector3 world) => _cat.sprite != null && WorldBounds(_cat, 2f / Atlas.Ppu).Contains(world);

        public bool BucketContains(Vector3 world) => _bucket.sprite != null && WorldBounds(_bucket, 3f / Atlas.Ppu).Contains(world);

        public bool BobberContains(Vector3 world) =>
            _bobber.enabled && WorldBounds(_bobber, 10f / Atlas.Ppu).Contains(world);

        /// <summary>Everything that should capture the mouse: the cat, boat and bucket, and the
        /// bobber during a golden bite. Slightly generous so the window stops being click-through
        /// just before the cursor arrives.</summary>
        public bool Contains(Vector3 world)
        {
            if (WorldBounds(_cat, 4f / Atlas.Ppu).Contains(world) || WorldBounds(_boatFront, 3f / Atlas.Ppu).Contains(world)
                || WorldBounds(_bucket, 4f / Atlas.Ppu).Contains(world))
                return true;
            return _qteOpen && BobberContains(world);
        }

        void TickDrag()
        {
            if (_window == null || Camera.main == null)
                return;
            var screen = _window.CursorScreen;
            var world = Camera.main.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            world.z = 0f;

            if (_window.PrimaryPressedThisFrame && _session != null && !_session.PointerOnPanel(screen))
            {
                if (_qteOpen && (BobberContains(world) || Contains(world)))
                {
                    HitQte();
                    return;
                }

                if (CatContains(world) || BucketContains(world) || WorldBounds(_boatFront, 2f / Atlas.Ppu).Contains(world))
                {
                    _grabbed = true;
                    _grabScreen = screen;
                    _grabOffset = transform.position.x - world.x;
                }
            }

            if (!_grabbed)
                return;

            if (!_dragging && Vector2.Distance(screen, _grabScreen) > 5f && !_busy)
                _dragging = true;
            if (_dragging)
            {
                SetWorldX(world.x + _grabOffset);
                _window.PointerOverPet = true;
                _window.WantSmooth = true;
            }

            if (!_window.PrimaryDown)
            {
                if (_dragging)
                    _session?.StorePetPosition();
                else if (BucketContains(world))
                    BucketClicked?.Invoke();
                else if (CatContains(world))
                    CatClicked?.Invoke();
                _grabbed = false;
                _dragging = false;
            }
        }
    }
}
