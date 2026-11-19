using System;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// The water, drawn procedurally in layers. Depth is adjustable: a shallow strip by default,
    /// and dragging the grab tab up opens a deeper window onto the seabed (cliffs, sand, coral,
    /// kelp, fish schools, bubbles, light rays), after the underwater pixel references.
    ///
    /// Layers, back to front (sorting order):
    ///   -3 Bed     water gradient, far rocks, cliffs, sand, coral. Baked; rebuilt only when the
    ///              size, theme or light changes, so a tall pond costs nothing per frame.
    ///   -2 Life    kelp and weed swaying, fish schools, bubbles, caustics, drifting specks.
    ///              Sparse, redrawn ~8 times a second.
    ///   -1 Rays    two baked sets of light shafts, cross-faded by colour alpha (no redraw).
    ///    3 Surface surface line, crests, reeds, lily pads, crumbs and splashes above water.
    ///   20 Front   translucent water over the hull so the boat sits in it, bait-fish shadows,
    ///              crumbs sinking, morning mist.
    ///
    /// Units are art pixels: x = 0 is the window's left edge, y = 0 the floor.
    /// </summary>
    public class PondView : MonoBehaviour
    {
        public const int MinDepth = 7;
        const int Air = 18;
        const int StripBelow = 12;
        const float TickSeconds = 0.125f;
        const int LocalWidth = 200;
        const int DeepFrom = 16;

        public int Depth { get; private set; } = MinDepth;
        public int MaxDepth { get; set; } = 120;
        public int SurfaceY => Depth;
        public float SurfaceWorldY => Depth / Atlas.Ppu;
        public bool IsNight { get; private set; }
        public bool IsDawn { get; private set; }
        public float Darkness { get; private set; }
        public float LeftPx => _left;
        public float RightPx => _left + _width;
        public bool IsDraggingGrip => _gripDrag;

        /// <summary>Raised while the depth changes (live during a drag) and once on release.</summary>
        public event Action<int, bool> DepthChanged;

        PondDef _theme = Looks.Ponds[0];
        bool _full;
        int _windowPx = 640;
        float _catPx = 320f;
        float _bobberPx = -1f;
        float _lanternPx = -1f;
        float _bait;
        float _boatL = -1f, _boatR = -1f;
        int _hullBottom = -1;

        int _left, _width, _depthBuilt = -1, _stripY, _stripH;
        string _themeBuilt;
        int _palKey;
        bool _fullBuilt;
        float _rebuildCooldown;

        Texture2D _bedTex, _lifeTex, _raysATex, _raysBTex, _surfTex, _frontTex, _skyTex;
        Color32[] _bed, _life, _surf, _front, _sky;
        SpriteRenderer _bedSr, _lifeSr, _raysA, _raysB, _surfSr, _frontSr, _grip, _skySr;

        // Weather falls through a band of air above the water, in its own layer in front of
        // the pet. The layer is switched off entirely on a clear day.
        const int SkyH = 60;
        WeatherKind _weather = WeatherKind.Clear;

        // A rare visitor crossing the deep water: 0 none, 1 whale, 2 jellyfish.
        int _visitor;
        float _visitorT, _visitorSeconds, _visitorSpark;
        int _visitorDir = 1;
        bool _visitorClaimed;
        int[] _floorH, _cliffL, _cliffR;
        float _clock, _tick;
        float _hourOverride = -1f;
        bool _gripDrag;
        readonly System.Random _rng = new System.Random(7);

        struct Speck { public float X, Y, Vx, Vy, Life; public byte Kind; }
        readonly Speck[] _specks = new Speck[48];

        struct Shadow { public float X, Y, Speed, Turn; public int Dir; public bool Alive; }
        readonly Shadow[] _shadows = new Shadow[7];

        static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        static float Dither(int x, int y) => (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;

        bool Deep => Depth >= DeepFrom;

        public static PondView Create()
        {
            var go = new GameObject("Pond");
            var v = go.AddComponent<PondView>();
            v._bedSr = Atlas.MakeRenderer("Bed", go.transform, -3);
            v._lifeSr = Atlas.MakeRenderer("Life", go.transform, -2);
            v._raysA = Atlas.MakeRenderer("RaysA", go.transform, -1);
            v._raysB = Atlas.MakeRenderer("RaysB", go.transform, -1);
            v._surfSr = Atlas.MakeRenderer("Surface", go.transform, 3);
            v._frontSr = Atlas.MakeRenderer("Front", go.transform, 20);
            v._skySr = Atlas.MakeRenderer("Sky", go.transform, 24);
            v._skySr.enabled = false;
            v._grip = Atlas.MakeRenderer("Grip", go.transform, 26);
            v._grip.sprite = Atlas.Get("grip", new Vector2(0.5f, 0.5f));
            foreach (var a in Environment.GetCommandLineArgs())
                if (a.StartsWith("-hour=") && float.TryParse(a.Substring(6), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var h))
                    v._hourOverride = h;
            return v;
        }

        public void SetTheme(PondDef theme) => _theme = theme ?? Looks.Ponds[0];
        public void SetFullWidth(bool full) => _full = full;
        public void SetWindowWidth(float worldWidth) => _windowPx = Mathf.Max(64, Mathf.RoundToInt(worldWidth * Atlas.Ppu));
        public void SetCat(float worldX) => _catPx = worldX * Atlas.Ppu;
        public void SetBobber(float worldX) => _bobberPx = worldX < 0f ? -1f : worldX * Atlas.Ppu;
        public void SetLantern(float worldX) => _lanternPx = worldX < 0f ? -1f : worldX * Atlas.Ppu;
        public void SetBait(float level) => _bait = Mathf.Clamp01(level);

        /// <summary>Where the hull is, so the translucent front water only covers the boat.</summary>
        public void SetBoatSpan(float leftWorld, float rightWorld, float bottomWorld)
        {
            _boatL = leftWorld * Atlas.Ppu;
            _boatR = rightWorld * Atlas.Ppu;
            _hullBottom = Mathf.RoundToInt(bottomWorld * Atlas.Ppu);
        }

        public void SetDepth(int depth)
        {
            depth = Mathf.Clamp(depth, MinDepth, Mathf.Max(MinDepth, MaxDepth));
            if (depth == Depth)
                return;
            Depth = depth;
            _tick = 0f;
        }

        public void SetHourOverride(float hour) => _hourOverride = hour;

        public WeatherKind Weather => _weather;

        public void SetWeather(WeatherKind weather)
        {
            if (weather == _weather)
                return;
            _weather = weather;
            _tick = 0f;
        }

        // ------------------------------------------------------------------ deep-water visitors

        /// <summary>Deep enough for something big to swim through.</summary>
        public bool CanHostVisitor => Depth >= 30 && _visitor == 0 && _theme.Id != "lava";

        public bool VisitorActive => _visitor != 0;

        /// <summary>Sends a whale (1) or a bloom of jellyfish (2) across the deep water.</summary>
        public void SpawnVisitor(int kind)
        {
            if (!CanHostVisitor)
                return;
            _visitor = kind;
            _visitorT = 0f;
            _visitorSeconds = kind == 1 ? 26f : 34f;
            _visitorDir = _rng.Next(2) == 0 ? 1 : -1;
            _visitorClaimed = false;
            _visitorSpark = 0f;
        }

        public void ClearVisitor() => _visitor = 0;

        Vector2 VisitorCentre()
        {
            var span = _width + 90;
            var x = _visitorDir > 0 ? _visitorT * span - 45 : _width + 45 - _visitorT * span;
            var y = Depth * (_visitor == 1 ? 0.5f : 0.55f) + Mathf.Sin(_clock * 0.5f) * Depth * 0.05f;
            return new Vector2(x, y);
        }

        public bool VisitorContains(Vector3 world)
        {
            if (_visitor == 0 || _visitorClaimed)
                return false;
            var c = VisitorCentre();
            var px = world.x * Atlas.Ppu - _left;
            var py = world.y * Atlas.Ppu;
            return Mathf.Abs(px - c.x) < (_visitor == 1 ? 28f : 24f) && Mathf.Abs(py - c.y) < 13f;
        }

        /// <summary>The visitor was clicked. Returns its kind once, then 0.</summary>
        public int ClaimVisitor()
        {
            if (_visitor == 0 || _visitorClaimed)
                return 0;
            _visitorClaimed = true;
            _visitorSpark = 1.2f;
            return _visitor;
        }

        void TickVisitor(float dt)
        {
            if (_visitor == 0)
                return;
            // Once greeted it hurries off.
            _visitorT += dt / _visitorSeconds * (_visitorClaimed ? 2.5f : 1f);
            _visitorSpark -= dt;
            if (_visitorT >= 1f || Depth < 24)
                _visitor = 0;
        }

        public float Hour => _hourOverride >= 0f ? _hourOverride : (float)DateTime.Now.TimeOfDay.TotalHours;

        // ------------------------------------------------------------------ effects

        public void Crumb(float fromWorldX, float fromWorldY, float toWorldX)
        {
            var fx = fromWorldX * Atlas.Ppu;
            var fy = fromWorldY * Atlas.Ppu;
            var tx = toWorldX * Atlas.Ppu + (float)(_rng.NextDouble() * 10 - 5);
            var t = Mathf.Sqrt(2f * Mathf.Max(1f, fy - SurfaceY) / 60f);
            Spawn(new Speck { X = fx, Y = fy, Vx = (tx - fx) / Mathf.Max(0.2f, t), Vy = 6f, Life = 4f, Kind = 0 });
        }

        public void Splash(float worldX, int count = 8)
        {
            var x = worldX * Atlas.Ppu;
            for (var i = 0; i < count; i++)
                Spawn(new Speck
                {
                    X = x, Y = SurfaceY + 1, Vx = (float)(_rng.NextDouble() * 40 - 20),
                    Vy = 25f + (float)_rng.NextDouble() * 30f, Life = 1.2f, Kind = 1
                });
            Ripple(worldX);
        }

        public void Ripple(float worldX) => Spawn(new Speck { X = worldX * Atlas.Ppu, Y = SurfaceY, Life = 1.2f, Kind = 2 });

        void Spawn(Speck s)
        {
            for (var i = 0; i < _specks.Length; i++)
                if (_specks[i].Life <= 0f)
                {
                    _specks[i] = s;
                    return;
                }
        }

        // ------------------------------------------------------------------ grip

        public bool GripContains(Vector3 world)
        {
            if (!_grip.enabled)
                return false;
            var b = _grip.bounds;
            b.Expand(new Vector3(4f / Atlas.Ppu, 4f / Atlas.Ppu, 1000f));
            return b.Contains(world);
        }

        /// <summary>Drag the tab up or down to set the depth. Returns true while dragging.</summary>
        public bool TickGrip(DesktopWindow window, Camera cam)
        {
            if (window == null || cam == null)
                return false;
            var screen = window.CursorScreen;
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

            if (!_gripDrag && window.PrimaryPressedThisFrame && GripContains(world))
                _gripDrag = true;
            if (!_gripDrag)
                return false;

            window.WantSmooth = true;
            window.Fast("grip");
            window.PointerOverPet = true;
            var want = Mathf.Clamp(Mathf.RoundToInt(world.y * Atlas.Ppu) - 2, MinDepth, MaxDepth);
            if (want != Depth)
            {
                Depth = want;
                _tick = 0f;
                DepthChanged?.Invoke(Depth, false);
            }

            if (!window.PrimaryDown)
            {
                _gripDrag = false;
                DepthChanged?.Invoke(Depth, true);
            }
            return true;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            var dt = Time.deltaTime;
            _clock += dt;
            _rebuildCooldown -= dt;
            TickSpecks(dt);
            TickShadows(dt);
            TickVisitor(dt);
            AnimateRays();

            _tick -= dt;
            if (_tick > 0f)
                return;
            _tick = TickSeconds;
            Redraw();
        }

        void TickSpecks(float dt)
        {
            for (var i = 0; i < _specks.Length; i++)
            {
                ref var s = ref _specks[i];
                if (s.Life <= 0f)
                    continue;
                s.Life -= dt;
                if (s.Kind == 2)
                    continue;
                s.X += s.Vx * dt;
                s.Y += s.Vy * dt;
                if (s.Y > SurfaceY)
                    s.Vy -= 60f * dt;
                else
                {
                    if (s.Kind == 1)
                        s.Life = 0f;
                    s.Vx *= 0.9f;
                    s.Vy = -1.5f;
                    if (s.Y < SurfaceY - StripBelow + 1)
                        s.Life = 0f;
                }
            }
        }

        void TickShadows(float dt)
        {
            var want = Mathf.RoundToInt(_bait * _shadows.Length);
            var target = _bobberPx >= 0f ? _bobberPx : _catPx - 30f;
            for (var i = 0; i < _shadows.Length; i++)
            {
                ref var f = ref _shadows[i];
                if (i < want && !f.Alive)
                {
                    f.Alive = true;
                    f.X = target + (_rng.Next(2) == 0 ? -40 : 40);
                    f.Y = 2 + _rng.Next(Mathf.Max(1, Mathf.Min(StripBelow, Depth) - 4));
                    f.Speed = 4f + (float)_rng.NextDouble() * 6f;
                    f.Dir = f.X < target ? 1 : -1;
                }
                else if (i >= want && f.Alive && _rng.NextDouble() < dt * 0.2)
                    f.Alive = false;

                if (!f.Alive)
                    continue;
                f.X += f.Dir * f.Speed * dt;
                f.Turn -= dt;
                if ((f.Dir > 0 && f.X > target + 14) || (f.Dir < 0 && f.X < target - 14) || f.Turn < -6f)
                {
                    f.Dir = -f.Dir;
                    f.Turn = 0f;
                }
            }
        }

        void AnimateRays()
        {
            if (!_raysA.enabled)
                return;
            var strength = Mathf.Lerp(0.55f, 0.14f, Darkness);
            var k = 0.5f + 0.5f * Mathf.Sin(_clock * 0.35f);
            _raysA.color = new Color(1f, 1f, 1f, strength * k);
            _raysB.color = new Color(1f, 1f, 1f, strength * (1f - k));
        }

        // ------------------------------------------------------------------ layout

        bool Layout()
        {
            var width = _full ? _windowPx : Mathf.Min(LocalWidth + (Deep ? 40 : 0), _windowPx);
            var left = _full ? 0 : Mathf.Clamp(Mathf.RoundToInt(_catPx - width * 0.55f), 0, Mathf.Max(0, _windowPx - width));
            var resized = width != _width || Depth != _depthBuilt || _bedTex == null;
            _left = left;
            _width = width;
            transform.position = new Vector3(left / Atlas.Ppu, 0f, 0f);

            if (resized)
            {
                _stripY = Mathf.Max(0, Depth - StripBelow);
                _stripH = Depth + Air - _stripY;
                Remake(ref _bedTex, ref _bed, _bedSr, width, Depth, 0);
                Remake(ref _lifeTex, ref _life, _lifeSr, width, Depth, 0);
                Color32[] none = null;
                Remake(ref _raysATex, ref none, _raysA, width, Depth, 0);
                none = null;
                Remake(ref _raysBTex, ref none, _raysB, width, Depth, 0);
                Remake(ref _surfTex, ref _surf, _surfSr, width, _stripH, _stripY);
                Remake(ref _frontTex, ref _front, _frontSr, width, _stripH, _stripY);
                Remake(ref _skyTex, ref _sky, _skySr, width, SkyH, Depth);
                _floorH = new int[width];
                _cliffL = new int[Depth + Air];
                _cliffR = new int[Depth + Air];
            }

            // The grip sits at the right end of the water, just above the surface.
            var gx = (_full ? _windowPx - 10 : left + width - 6) - left;
            _grip.transform.localPosition = new Vector3(gx / Atlas.Ppu, (Depth + 7) / Atlas.Ppu, 0f);
            return resized;
        }

        static void Remake(ref Texture2D tex, ref Color32[] buf, SpriteRenderer sr, int w, int h, int y)
        {
            if (tex != null)
                Destroy(tex);
            tex = new Texture2D(w, Mathf.Max(1, h), TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = sr.name
            };
            buf = new Color32[w * Mathf.Max(1, h)];
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, w, Mathf.Max(1, h)), Vector2.zero, Atlas.Ppu, 0, SpriteMeshType.FullRect);
            sr.transform.localPosition = new Vector3(0f, y / Atlas.Ppu, 0f);
        }

        // ------------------------------------------------------------------ redraw

        void Redraw()
        {
            var resized = Layout();
            var pal = Palette(Hour, out var night, out var dawn, out var darkness);
            IsNight = night;
            IsDawn = dawn;
            Darkness = darkness;

            // The baked bed follows the light in steps, not every frame.
            var key = Quant(pal[1]) * 31 + Quant(pal[3]) * 7 + Mathf.RoundToInt(darkness * 8f);
            // Textures that were just remade are blank, so those rebuild at once; a light change
            // can wait out the cooldown.
            var stale = _themeBuilt != _theme.Id || _fullBuilt != _full || key != _palKey;
            if (resized || (stale && _rebuildCooldown <= 0f))
            {
                BuildBed(pal, darkness);
                _depthBuilt = Depth;
                _themeBuilt = _theme.Id;
                _fullBuilt = _full;
                _palKey = key;
                _rebuildCooldown = 20f;
            }

            DrawLife(pal);
            DrawStrips(pal, night, dawn);
            DrawSky();
        }

        // ------------------------------------------------------------------ weather

        void DrawSky()
        {
            var on = _weather != WeatherKind.Clear;
            if (_skySr.enabled != on)
                _skySr.enabled = on;
            if (!on || _sky == null)
                return;

            Array.Clear(_sky, 0, _sky.Length);
            var t = _clock;
            var w = _width;
            switch (_weather)
            {
                case WeatherKind.Rain:
                {
                    var drop = new Color32(190, 214, 255, 255);
                    for (var i = 0; i < w / 4; i++)
                    {
                        var x = Hash(i * 13 + 1) % w;
                        var y = SkyH - 1 - (int)Mathf.Repeat(t * 90f + Hash(i * 7 + 3) % (SkyH * 2), SkyH * 2);
                        // A short slanted streak; the long gap between repeats keeps it a shower.
                        Sky(x, y, drop, 170);
                        Sky(x + 1, y + 1, drop, 140);
                        Sky(x + 1, y + 2, drop, 90);
                    }
                    break;
                }
                case WeatherKind.Snow:
                {
                    var flake = new Color32(255, 255, 255, 255);
                    for (var i = 0; i < w / 7; i++)
                    {
                        var fall = 7f + Hash(i * 5) % 6;
                        var y = SkyH - 1 - (int)Mathf.Repeat(t * fall + Hash(i * 11 + 2) % SkyH, SkyH);
                        var x = Hash(i * 17 + 5) % w + Mathf.RoundToInt(Mathf.Sin(t * 0.9f + i) * 2.5f);
                        Sky(x, y, flake, 235);
                        if (i % 3 == 0)
                            Sky(x + 1, y, flake, 150);
                    }
                    break;
                }
                case WeatherKind.Fog:
                {
                    var mist = new Color32(236, 240, 248, 255);
                    for (var x = 0; x < w; x++)
                    {
                        var wx = x + _left;
                        var density = 0.55f + 0.45f * Mathf.Sin(wx * 0.035f + t * 0.2f) * Mathf.Sin(wx * 0.013f - t * 0.09f);
                        density *= EdgeAlpha(x, w);
                        for (var y = 0; y < 30; y++)
                        {
                            var fall = 1f - y / 30f;
                            if (Dither(wx + (int)(t * 1.5f), y) < density * fall * 0.7f)
                                Sky(x, y, mist, 120);
                        }
                    }
                    break;
                }
            }

            _skyTex.SetPixels32(_sky);
            _skyTex.Apply(false, false);
        }

        void Sky(int x, int y, Color32 c, byte a)
        {
            if (x < 0 || y < 0 || x >= _width || y >= SkyH)
                return;
            c.a = a;
            _sky[y * _width + x] = c;
        }

        static int Quant(Color32 c) => (c.r >> 4) * 256 + (c.g >> 4) * 16 + (c.b >> 4);

        Color32 WaterAt(Color32[] pal, int x, int y)
        {
            // Deep at the floor, mid, then surface colour near the top, dithered between bands.
            var k = Depth <= 1 ? 1f : (float)y / (Depth - 1);
            var p = k * 2.2f + (Dither(x + _left, y) - 0.5f) * 0.6f;
            return p < 0.8f ? pal[3] : p < 1.7f ? pal[2] : pal[1];
        }

        // ------------------------------------------------------------------ bed (baked)

        void BuildBed(Color32[] pal, float darkness)
        {
            var w = _width;
            var d = Depth;
            var bed = _theme.Bed;
            var shade = 1f - darkness * 0.55f;
            Color32 Lit(Color32 c, int y)
            {
                // Fog: the deeper, the more of the water colour shows through.
                var k = (float)y / Mathf.Max(1, d - 1);
                var vis = 0.62f + 0.38f * k;
                var water = WaterAt(pal, 0, y);
                return Color32.Lerp(water, new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), 255), vis);
            }

            // Column profiles: seabed height, cliffs at the pond's ends.
            var deep = Deep;
            for (var x = 0; x < w; x++)
            {
                var wx = x + _left;
                _floorH[x] = !deep ? 1 + (Hash(wx / 9) % 3 == 0 ? 1 : 0)
                    : 2 + Mathf.RoundToInt((Mathf.Sin(wx * 0.05f) * 0.5f + 0.5f) * 2f + Mathf.Sin(wx * 0.17f + 1f));
            }

            var cliffW = deep ? Mathf.Clamp(d / 3, 8, 26) : 0;
            for (var y = 0; y < d + Air; y++)
            {
                var k = Mathf.Clamp01((float)y / d);
                var taper = Mathf.Pow(1f - k, 0.55f);
                var bumpL = (Hash(y / 3 + 11) % 4) - 1;
                var bumpR = (Hash(y / 3 + 77) % 4) - 1;
                _cliffL[y] = cliffW == 0 ? -1 : Mathf.RoundToInt(cliffW * (0.45f + 0.55f * taper)) + bumpL;
                _cliffR[y] = cliffW == 0 ? w : w - 1 - (Mathf.RoundToInt(cliffW * (0.4f + 0.6f * taper)) + bumpR);
                if (y >= d - 1 && cliffW > 0)
                {
                    // The banks poke out above the water a little.
                    var above = y - (d - 1);
                    _cliffL[y] = above < 3 ? _cliffL[y] - above * 3 : -1;
                    _cliffR[y] = above < 3 ? _cliffR[y] + above * 3 : w;
                }
            }

            for (var y = 0; y < d; y++)
            for (var x = 0; x < w; x++)
            {
                var wx = x + _left;
                var edge = EdgeAlpha(x, w);
                if (edge <= 0f || (edge < 1f && Dither(wx, y) > edge))
                {
                    _bed[y * w + x] = default;
                    continue;
                }

                Color32 c = WaterAt(pal, x, y);

                if (deep)
                {
                    // Far rocks: soft silhouettes in the haze.
                    var far = d * (0.22f + 0.14f * Mathf.Sin(wx * 0.021f + 2f) + 0.06f * Mathf.Sin(wx * 0.09f));
                    if (y < far)
                        c = Color32.Lerp(c, Lit(bed[0], y), 0.35f);

                    // Mid mounds in a wide pond.
                    var mound = MoundHeight(wx, d);
                    if (y < mound)
                        c = Lit(RockPixel(wx, y, mound, bed), y);
                }

                // Sand.
                var fh = _floorH[x];
                if (y < fh)
                {
                    var top = y == fh - 1;
                    var ripple = ((wx + y * 3) % 7) == 0;
                    c = Lit(top || ripple ? bed[4] : bed[3], y);
                    if (Hash(wx * 7 + y) % 23 == 0)
                        c = Lit(bed[0], y);
                }

                // Cliffs.
                if (x <= _cliffL[y] || x >= _cliffR[y])
                {
                    var exposedTop = y + 1 < d + Air && !(x <= _cliffL[y + 1] || x >= _cliffR[y + 1]);
                    var exposedSide = x == _cliffL[y] || x == _cliffR[y];
                    c = Lit(exposedTop ? bed[2] : exposedSide ? bed[1] : RockBlock(wx, y, bed), y);
                }

                _bed[y * w + x] = c;
            }

            if (deep)
                PlaceCoral(bed, d);

            _bedTex.SetPixels32(_bed);
            _bedTex.Apply(false, false);
            BuildRays(pal);
        }

        float MoundHeight(int wx, int d)
        {
            if (!_full && _width < 300)
                return 0f;
            var cell = wx / 140;
            var cx = cell * 140 + 30 + Hash(cell * 5) % 80;
            var half = 18 + Hash(cell * 9) % 22;
            var dist = Mathf.Abs(wx - cx);
            if (dist > half)
                return 0f;
            var h = d * (0.18f + (Hash(cell * 3) % 20) / 100f);
            return h * Mathf.Sqrt(1f - (float)dist / half);
        }

        static Color32 RockBlock(int wx, int y, Color32[] bed)
        {
            // Blocky strata, offset every other course, like stacked stone.
            var bx = (wx + ((y / 3) % 2) * 3) / 6;
            var h = Hash(bx * 31 + (y / 3) * 17);
            return h % 5 == 0 ? bed[2] : h % 3 == 0 ? bed[0] : bed[1];
        }

        static Color32 RockPixel(int wx, int y, float top, Color32[] bed) =>
            y >= top - 1 ? bed[2] : RockBlock(wx, y, bed);

        void PlaceCoral(Color32[] bed, int d)
        {
            var w = _width;
            var count = w / 18;
            for (var i = 0; i < count; i++)
            {
                var x = Hash(i * 13 + _left / 18) % w;
                if (x < 2 || x >= w - 2 || x <= _cliffL[Mathf.Min(_floorH[x], d - 1)] + 1)
                    continue;
                var baseY = _floorH[x];
                var mound = MoundHeight(x + _left, d);
                if (mound > baseY)
                    baseY = Mathf.CeilToInt(mound);
                if (baseY >= d - 4)
                    continue;
                var colour = Hash(i * 7) % 2 == 0 ? bed[7] : bed[8];
                var h = 3 + Hash(i * 3) % Mathf.Max(2, Mathf.Min(8, d / 6));
                // A little branching fan: a trunk, two forks.
                for (var y = 0; y < h; y++)
                {
                    Set(_bed, w, d, x, baseY + y, colour);
                    if (y == h / 2)
                    {
                        for (var j = 1; j <= 2; j++)
                        {
                            Set(_bed, w, d, x - j, baseY + y + j, colour);
                            Set(_bed, w, d, x + j, baseY + y + j - 1, colour);
                        }
                    }
                }
                Set(_bed, w, d, x, baseY + h, Color32.Lerp(colour, new Color32(255, 255, 255, 255), 0.4f));
            }

            // Bright little pebbles on the sand.
            for (var i = 0; i < w / 12; i++)
            {
                var x = Hash(i * 19 + 4) % w;
                Set(_bed, w, d, x, _floorH[x], bed[4]);
            }
        }

        static void Set(Color32[] buf, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || buf[y * w + x].a == 0)
                return;
            buf[y * w + x] = c;
        }

        void BuildRays(Color32[] pal)
        {
            var on = Deep;
            _raysA.enabled = on;
            _raysB.enabled = on;
            if (!on)
                return;
            var w = _width;
            var d = Depth;
            var px = new Color32[w * d];
            for (var pass = 0; pass < 2; pass++)
            {
                Array.Clear(px, 0, px.Length);
                var light = pal[0];
                var n = Mathf.Max(2, w / 45);
                for (var r = 0; r < n; r++)
                {
                    var x0 = (Hash(r * 29 + pass * 101) % (w + d / 2)) - d / 4;
                    var width = 3 + Hash(r * 7 + pass) % 6;
                    var reach = d * (0.55f + (Hash(r * 11 + pass) % 40) / 100f);
                    for (var y = d - 2; y >= 0; y--)
                    {
                        var fromTop = d - 1 - y;
                        if (fromTop > reach)
                            break;
                        var fade = 1f - fromTop / reach;
                        var sx = x0 + Mathf.RoundToInt(fromTop * 0.35f);
                        for (var i = 0; i < width; i++)
                        {
                            var x = sx + i;
                            if (x < 0 || x >= w || _bed[y * w + x].a == 0)
                                continue;
                            var edgeFade = 1f - Mathf.Abs(i - width / 2f) / (width / 2f + 0.5f);
                            if (Dither(x, y) < fade * (0.35f + 0.65f * edgeFade))
                                px[y * w + x] = new Color32(light.r, light.g, light.b, 110);
                        }
                    }
                }

                var tex = pass == 0 ? _raysATex : _raysBTex;
                tex.SetPixels32(px);
                tex.Apply(false, false);
            }
        }

        // ------------------------------------------------------------------ life (animated)

        void DrawLife(Color32[] pal)
        {
            Array.Clear(_life, 0, _life.Length);
            var w = _width;
            var d = Depth;
            var t = _clock;
            var bed = _theme.Bed;

            if (d >= 10)
            {
                switch (_theme.Id)
                {
                    case "kelp":
                        for (var i = 0; i < w / 14; i++)
                            Kelp(Hash(i * 37 + 3) % w, Mathf.RoundToInt(d * (0.5f + (Hash(i * 5) % 40) / 100f)), i, bed[5], bed[6], t, true);
                        break;
                    case "pond":
                        // Lily stems from the pads down to the mud, and clumps of weed.
                        for (var i = 0; i < 3; i++)
                            Stem(LilyX(i), d, bed[6], t, i);
                        for (var i = 0; i < w / 40; i++)
                            Kelp(Hash(i * 41 + 9) % w, Mathf.Min(d - 3, 4 + Hash(i) % Mathf.Max(2, d / 3)), i, bed[5], bed[6], t, false);
                        break;
                    case "sea":
                        for (var i = 0; i < w / 30; i++)
                            Kelp(Hash(i * 23 + 1) % w, Mathf.Min(d - 3, 3 + Hash(i * 3) % Mathf.Max(2, d / 4)), i, bed[5], bed[6], t, false);
                        break;
                }
            }

            if (_visitor != 0)
                DrawVisitor(pal, t);

            if (Deep)
            {
                FishSchool(pal, t);
                Bubbles(t, _theme.Id == "lava" ? bed[8] : pal[0]);
                Caustics(pal, t);
                Drift(pal, t);
            }

            _lifeTex.SetPixels32(_life);
            _lifeTex.Apply(false, false);
        }

        int LilyX(int i) => (Hash(i * 97 + _left / 40) % 100) * _width / 100;

        void Life(int x, int y, Color32 c, byte a = 255)
        {
            if (x < 0 || y < 0 || x >= _width || y >= Depth || _bed[y * _width + x].a == 0)
                return;
            if (x <= _cliffL[y] || x >= _cliffR[y])
                return;
            c.a = a;
            _life[y * _width + x] = c;
        }

        void Kelp(int x, int h, int seed, Color32 c, Color32 dark, float t, bool leaves)
        {
            if (x < 0 || x >= _width)
                return;
            var y0 = _floorH[x];
            for (var y = 0; y < h; y++)
            {
                var k = (float)y / Mathf.Max(1, h);
                var sway = Mathf.RoundToInt(Mathf.Sin(t * 0.9f + seed + y * 0.18f) * 2.2f * k);
                var px = x + sway;
                Life(px, y0 + y, (y + seed) % 4 == 0 ? dark : c);
                if (leaves && y % 4 == 2)
                {
                    var side = (y / 4 + seed) % 2 == 0 ? -1 : 1;
                    Life(px + side, y0 + y, c);
                    Life(px + side * 2, y0 + y + 1, c);
                }
            }
        }

        void Stem(int x, int d, Color32 c, float t, int seed)
        {
            for (var y = _floorH[Mathf.Clamp(x, 0, _width - 1)]; y < d - 1; y++)
            {
                var k = (float)y / d;
                Life(x + Mathf.RoundToInt(Mathf.Sin(t * 0.7f + seed + y * 0.08f) * 1.4f * (1f - k)), y, c);
            }
        }

        void FishSchool(Color32[] pal, float t)
        {
            // A school crossing every half minute or so, darker than the water.
            var period = 34f;
            var phase = Mathf.Repeat(t + 9f, period) / period;
            var dir = Mathf.FloorToInt((t + 9f) / period) % 2 == 0 ? 1 : -1;
            var span = _width + 60;
            var cx = dir > 0 ? phase * span - 30 : _width + 30 - phase * span;
            var cy = Depth * 0.45f + Mathf.Sin(t * 0.6f) * Depth * 0.1f;
            var dark = Color32.Lerp(pal[3], new Color32(0, 0, 0, 255), 0.45f);
            for (var i = 0; i < 9; i++)
            {
                var fx = Mathf.RoundToInt(cx + (Hash(i * 3) % 24 - 12) + Mathf.Sin(t * 2f + i) * 1.5f);
                var fy = Mathf.RoundToInt(cy + (Hash(i * 5 + 1) % 10 - 5) + Mathf.Sin(t * 1.3f + i * 2) * 1f);
                Life(fx, fy, dark, 230);
                Life(fx - dir, fy, dark, 230);
                Life(fx - dir * 2, fy + 1, dark, 200);
                Life(fx - dir * 2, fy - 1, dark, 200);
                if (Mathf.Sin(t * 3f + i) > 0.9f)
                    Life(fx, fy + 1, pal[4], 200);
            }

            // A big slow silhouette now and then, far back.
            var bigPhase = Mathf.Repeat(t, 70f) / 70f;
            if (Depth >= 30 && bigPhase < 0.5f)
            {
                var bx = Mathf.RoundToInt(bigPhase * 2f * (_width + 80) - 40);
                var by = Mathf.RoundToInt(Depth * 0.3f);
                var c = Color32.Lerp(pal[3], pal[2], 0.3f);
                for (var dx = 0; dx < 16; dx++)
                {
                    var half = dx < 12 ? Mathf.RoundToInt(Mathf.Sin(dx / 12f * Mathf.PI) * 3f) : dx - 12;
                    for (var dy = -half; dy <= half; dy++)
                        Life(bx - dx, by + dy, c, 160);
                }
            }
        }

        void DrawVisitor(Color32[] pal, float t)
        {
            var c = VisitorCentre();
            var cx = Mathf.RoundToInt(c.x);
            var cy = Mathf.RoundToInt(c.y);
            var dir = _visitorDir;

            if (_visitor == 1)
            {
                // A whale: a long dark body with a pale belly, a fluke that beats slowly, and an eye.
                var back = Color32.Lerp(pal[3], new Color32(10, 14, 40, 255), 0.55f);
                var belly = Color32.Lerp(pal[2], new Color32(220, 232, 255, 255), 0.45f);
                const int len = 46;
                for (var i = 0; i < len; i++)
                {
                    // i runs from the nose back to the tail stock.
                    var k = i / (float)len;
                    var half = Mathf.RoundToInt(Mathf.Sin(Mathf.Pow(k, 0.7f) * Mathf.PI) * 7f) + (k < 0.5f ? 1 : 0);
                    var x = cx + dir * (len / 2 - i);
                    for (var dy = -half; dy <= half; dy++)
                        Life(x, cy + dy, dy < -half / 2 ? belly : back, 240);
                }

                var beat = Mathf.RoundToInt(Mathf.Sin(t * 1.4f) * 2f);
                for (var i = 0; i < 9; i++)
                {
                    var x = cx - dir * (len / 2 + i);
                    var spread = i < 3 ? 1 : i - 1;
                    for (var dy = -spread; dy <= spread; dy++)
                        if (i < 3 || Mathf.Abs(dy) >= spread - 2)
                            Life(x, cy + dy + beat * i / 8, back, 240);
                }

                Life(cx + dir * (len / 2 - 6), cy + 2, new Color32(255, 255, 255, 255), 255);
                // The fin.
                for (var i = 0; i < 6; i++)
                    Life(cx + dir * (4 - i), cy - 6 - i / 2, back, 240);
            }
            else
            {
                // Jellyfish: five bells pulsing along, trailing tentacles.
                for (var j = 0; j < 5; j++)
                {
                    var jx = cx + (j - 2) * 11 + Mathf.RoundToInt(Mathf.Sin(t * 0.7f + j) * 2f);
                    var jy = cy + Hash(j * 9 + 4) % 9 - 4 + Mathf.RoundToInt(Mathf.Sin(t * 1.6f + j * 1.7f) * 2f);
                    var pulse = Mathf.Sin(t * 2.2f + j * 1.3f) > 0f ? 1 : 0;
                    var bell = j % 2 == 0 ? new Color32(0xf4, 0xa6, 0xc4, 255) : new Color32(0xb4, 0x9a, 0xe6, 255);
                    var glow = new Color32(255, 240, 250, 255);
                    for (var dx = -3 - pulse; dx <= 3 + pulse; dx++)
                    {
                        var h = Mathf.Abs(dx) >= 3 ? 1 : Mathf.Abs(dx) == 2 ? 2 : 3;
                        for (var dy = 0; dy < h; dy++)
                            Life(jx + dx, jy + dy, dy == h - 1 && Mathf.Abs(dx) < 2 ? glow : bell, 225);
                    }
                    for (var s = -2; s <= 2; s += 2)
                        for (var d = 1; d <= 5; d++)
                            Life(jx + s + Mathf.RoundToInt(Mathf.Sin(t * 2f + d * 0.8f + j) * 1.2f), jy - d, bell, (byte)(200 - d * 25));
                }
            }

            // Greeted: a burst of sparkles where it was clicked.
            if (_visitorSpark > 0f)
            {
                var r = Mathf.RoundToInt((1.2f - _visitorSpark) * 16f) + 3;
                var gold = new Color32(255, 232, 140, 255);
                for (var i = 0; i < 10; i++)
                {
                    var a = i / 10f * Mathf.PI * 2f + t;
                    Life(cx + Mathf.RoundToInt(Mathf.Cos(a) * r), cy + Mathf.RoundToInt(Mathf.Sin(a) * r * 0.6f), gold, 255);
                }
            }
        }

        void Bubbles(float t, Color32 c)
        {
            for (var v = 0; v < 3; v++)
            {
                var x = Hash(v * 53 + 7) % Mathf.Max(1, _width);
                for (var b = 0; b < 4; b++)
                {
                    var life = Mathf.Repeat(t * 0.25f + b * 0.25f + v * 0.13f, 1f);
                    var y = Mathf.RoundToInt(_floorH[x] + life * (Depth - _floorH[x]));
                    var wob = Mathf.RoundToInt(Mathf.Sin(t * 4f + b) * 1f);
                    Life(x + wob, y, c, 210);
                    if (b % 2 == 0)
                        Life(x + wob + 1, y, c, 120);
                }
            }
        }

        void Caustics(Color32[] pal, float t)
        {
            if (Darkness > 0.6f)
                return;
            var step = Mathf.FloorToInt(t * 3f);
            for (var x = 0; x < _width; x++)
            {
                var y = _floorH[x] - 1;
                if (y >= 0 && Hash((x + _left) / 2 * 7 + step * 13) % 9 == 0)
                    Life(x, y, pal[0], 170);
            }
        }

        void Drift(Color32[] pal, float t)
        {
            for (var i = 0; i < _width / 10; i++)
            {
                var x = Mathf.RoundToInt(Mathf.Repeat(Hash(i * 3) % _width + t * 1.5f, _width));
                var y = Mathf.RoundToInt(Mathf.Repeat(Hash(i * 7 + 2) % Depth - t * (0.8f + i % 3 * 0.4f), Depth));
                Life(x, y, pal[0], 90);
            }
        }

        // ------------------------------------------------------------------ surface + front

        void DrawStrips(Color32[] pal, bool night, bool dawn)
        {
            Array.Clear(_surf, 0, _surf.Length);
            Array.Clear(_front, 0, _front.Length);
            var t = _clock;
            var w = _width;
            var s = SurfaceY - 1;
            Color32 hi = pal[0], surf = pal[1], mid = pal[2];

            for (var x = 0; x < w; x++)
            {
                var wx = x + _left;
                var edge = EdgeAlpha(x, w);
                if (edge <= 0f)
                    continue;
                var bank = Deep && (x <= _cliffL[s] || x >= _cliffR[s]);
                if (bank)
                {
                    // The cliff tops, standing a little out of the water.
                    var bed = _theme.Bed;
                    for (var y = s - 1; y < s + 4; y++)
                    {
                        if (!InCliff(x, y))
                            continue;
                        var top = !InCliff(x, y + 1);
                        PutS(_surf, x, y, top ? bed[2] : RockBlock(wx, y, bed), 255);
                    }
                    continue;
                }

                for (var y = s - 1; y <= s; y++)
                {
                    if (edge < 1f && Dither(wx, y) > edge)
                        continue;
                    PutS(_surf, x, y, surf, 255);
                }

                var wave = Mathf.Sin(wx * 0.31f + t * 1.7f) + Mathf.Sin(wx * 0.11f - t * 0.9f);
                if (((wx + (int)(t * 6f)) % 19) < 3 || wave > 1.55f)
                    PutS(_surf, x, s, hi, 255);
                if (wave > 1.75f && edge >= 1f)
                    PutS(_surf, x, s + 1, hi, 255);

                // Translucent water over the sunk part of the hull only, in the bed's own colours,
                // so it reads as water in front of the boat rather than a box.
                if (_boatL >= 0f && wx >= _boatL && wx <= _boatR)
                    for (var y = Mathf.Max(_stripY, _hullBottom); y < s - 1; y++)
                        PutS(_front, x, y, WaterAt(pal, x, y), 150);
            }

            Glints(pal, night, t, s);
            if (_weather == WeatherKind.Rain)
                for (var i = 0; i < w / 12; i++)
                {
                    // Raindrops landing: a pale dot that widens into a ring for a few ticks.
                    var beat = Mathf.FloorToInt(t * 4f) + i * 3;
                    var rx = Hash(i * 29 + beat / 4 * 101) % Mathf.Max(1, w);
                    var r = beat % 4;
                    if (EdgeAlpha(rx, w) < 1f || InCliff(rx, s))
                        continue;
                    PutS(_surf, rx - r, s, hi, 255);
                    PutS(_surf, rx + r, s, hi, 255);
                }
            if (_theme.Lilies)
                Lilies(s);
            if (_theme.Reeds)
                Reeds(t, s);
            Shadows(pal[3]);
            SpecksDraw(hi);
            if (dawn)
                Mist(t);

            _surfTex.SetPixels32(_surf);
            _surfTex.Apply(false, false);
            _frontTex.SetPixels32(_front);
            _frontTex.Apply(false, false);
        }

        bool InCliff(int x, int y) =>
            y >= 0 && y < _cliffL.Length && (x <= _cliffL[y] || x >= _cliffR[y]);

        float EdgeAlpha(int x, int w)
        {
            if (_full || Deep)
                return 1f;
            const int fade = 14;
            return Mathf.Clamp01(Mathf.Min(x, w - 1 - x) / (float)fade);
        }

        void Glints(Color32[] pal, bool night, float t, int s)
        {
            var w = _width;
            var rows = Mathf.Min(StripBelow, Depth) - 1;
            for (var i = 0; i < w / 6; i++)
            {
                var x = Hash(i * 7 + _left / 6 * 13) % Mathf.Max(1, w);
                if (EdgeAlpha(x, w) < 1f)
                    continue;
                var y = s - 1 - Hash(i * 31 + 5) % Mathf.Max(1, rows);
                var tw = Mathf.Sin(t * (night ? 1.3f : 3.1f) + Hash(i * 17 + 3) % 100 / 100f * 6.28f);
                if (tw > (night ? 0.55f : 0.86f))
                    PutS(_front, x, y, pal[4], 255);
            }

            if (night)
            {
                var mx = Mathf.RoundToInt(w * 0.72f);
                for (var y = s - rows; y < s; y++)
                    if ((y + (int)(t * 3)) % 3 != 0)
                        PutS(_front, mx + Mathf.RoundToInt(Mathf.Sin(t * 2f + y * 1.3f) * 1.5f), y, pal[0], 220);
            }

            if (_lanternPx >= 0f && Darkness > 0.3f)
            {
                var lx = Mathf.RoundToInt(_lanternPx) - _left;
                var warm = new Color32(255, 196, 96, 255);
                for (var y = s - rows; y < s; y++)
                    if ((y + (int)(t * 4)) % 2 == 0)
                        PutS(_front, lx + Mathf.RoundToInt(Mathf.Sin(t * 3f + y) * 1.2f), y, warm, (byte)(140 * Darkness));
            }
        }

        void Lilies(int s)
        {
            var green = new Color32(0x6a, 0xb8, 0x6a, 255);
            var dark = new Color32(0x3f, 0x8a, 0x5a, 255);
            var pink = new Color32(0xf4, 0xa6, 0xc4, 255);
            for (var i = 0; i < 3; i++)
            {
                var cx = LilyX(i);
                if (EdgeAlpha(cx, _width) < 1f || Mathf.Abs(cx + _left - _catPx) < 26)
                    continue;
                for (var dx = -3; dx <= 3; dx++)
                    PutS(_surf, cx + dx, s, dx == 0 ? dark : green, 255);
                for (var dx = -2; dx <= 2; dx++)
                    PutS(_surf, cx + dx, s + 1, green, 255);
                if (i == 1)
                {
                    PutS(_surf, cx, s + 2, pink, 255);
                    PutS(_surf, cx - 1, s + 2, pink, 255);
                    PutS(_surf, cx, s + 3, pink, 255);
                }
            }
        }

        void Reeds(float t, int s)
        {
            var w = _width;
            var stalk = new Color32(0x5b, 0x8c, 0x4a, 255);
            var stalkDark = new Color32(0x3f, 0x66, 0x3a, 255);
            var head = new Color32(0x8a, 0x52, 0x30, 255);
            var sway = Mathf.Sin(t * 0.8f) + Mathf.Sin(t * 1.9f) * 0.4f;
            var inset = Deep ? Mathf.Max(0, _cliffL[s] + 1) : 0;
            int[] spots = { inset + 7, inset + 11, inset + 15, w - inset - 17, w - inset - 13, w - inset - 9 };
            for (var i = 0; i < spots.Length; i++)
            {
                var h = 8 + Hash(i * 13) % 7;
                for (var y = 0; y < h; y++)
                {
                    var lean = Mathf.RoundToInt(sway * y * y / (float)(h * h) * 2f + Mathf.Sin(i) * 0.4f);
                    var c = y < h - 4 ? (i % 2 == 0 ? stalk : stalkDark) : head;
                    PutS(_surf, spots[i] + lean, s + 1 + y, c, 255);
                    if (y >= h - 4)
                        PutS(_surf, spots[i] + lean + 1, s + 1 + y, head, 255);
                }
            }
        }

        void Shadows(Color32 deep)
        {
            var dark = new Color32((byte)(deep.r * 0.55f), (byte)(deep.g * 0.55f), (byte)(deep.b * 0.6f), 255);
            var s = SurfaceY - 1;
            for (var i = 0; i < _shadows.Length; i++)
            {
                var f = _shadows[i];
                if (!f.Alive)
                    continue;
                var x0 = Mathf.RoundToInt(f.X) - _left;
                var y0 = s - 1 - Mathf.RoundToInt(f.Y);
                for (var dx = 0; dx < 7; dx++)
                {
                    var x = f.Dir > 0 ? x0 - dx : x0 + dx;
                    if (dx >= 1 && dx <= 4)
                    {
                        PutS(_front, x, y0, dark, 210);
                        PutS(_front, x, y0 + 1, dark, 210);
                        if (dx >= 2 && dx <= 3)
                            PutS(_front, x, y0 - 1, dark, 210);
                    }
                    else if (dx == 6)
                    {
                        PutS(_front, x, y0 - 1, dark, 200);
                        PutS(_front, x, y0 + 1, dark, 200);
                    }
                    else if (dx == 5)
                        PutS(_front, x, y0, dark, 210);
                }
            }
        }

        void SpecksDraw(Color32 hi)
        {
            var crumb = new Color32(0xe8, 0xb0, 0x6a, 255);
            var foam = new Color32(0xf4, 0xfb, 0xff, 255);
            for (var i = 0; i < _specks.Length; i++)
            {
                var sp = _specks[i];
                if (sp.Life <= 0f)
                    continue;
                var x = Mathf.RoundToInt(sp.X) - _left;
                var y = Mathf.RoundToInt(sp.Y);
                switch (sp.Kind)
                {
                    case 0:
                        if (y >= SurfaceY)
                            PutS(_surf, x, y, crumb, 255);
                        else
                            PutS(_front, x, y, crumb, (byte)Mathf.Clamp(sp.Life * 120f, 0f, 230f));
                        break;
                    case 1:
                        PutS(_surf, x, y, foam, 255);
                        break;
                    case 2:
                        var r = Mathf.RoundToInt((1.2f - sp.Life) * 8f) + 1;
                        PutS(_surf, x - r, SurfaceY - 1, hi, 255);
                        PutS(_surf, x + r, SurfaceY - 1, hi, 255);
                        PutS(_surf, x - r + 1, SurfaceY, foam, 200);
                        PutS(_surf, x + r - 1, SurfaceY, foam, 200);
                        break;
                }
            }
        }

        void Mist(float t)
        {
            var mist = new Color32(240, 244, 250, 255);
            var top = Mathf.Min(_stripY + _stripH, SurfaceY + 9);
            for (var x = 0; x < _width; x++)
            {
                var wx = x + _left;
                var density = (0.5f + 0.5f * Mathf.Sin(wx * 0.045f + t * 0.25f) * Mathf.Sin(wx * 0.017f - t * 0.11f))
                              * EdgeAlpha(x, _width);
                for (var y = SurfaceY; y < Mathf.Min(top, SurfaceY + 5); y++)
                {
                    // Thin and hugging the water: thickest just above the surface.
                    var fall = 1f - (y - SurfaceY) / 5f;
                    if (Dither(wx + (int)(t * 2f), y) < density * fall * 0.35f)
                        PutS(_front, x, y, mist, 80);
                }
            }
        }

        /// <summary>Plot in a strip texture, using pond (floor-relative) coordinates.</summary>
        void PutS(Color32[] buf, int x, int y, Color32 c, byte a)
        {
            y -= _stripY;
            if (x < 0 || y < 0 || x >= _width || y >= _stripH || a == 0)
                return;
            c.a = a;
            buf[y * _width + x] = c;
        }

        static int Hash(int n)
        {
            unchecked
            {
                var h = (uint)n * 2654435761u;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (int)(h & 0x7fffffff);
            }
        }

        // ------------------------------------------------------------------ time of day

        static readonly float[] Keys = { 0f, 5f, 6.5f, 9f, 17f, 18.5f, 20f, 24f };
        static readonly int[] Phases = { 3, 3, 0, 1, 1, 2, 3, 3 };
        readonly Color32[] _pal = new Color32[5];

        Color32[] Palette(float hour, out bool night, out bool dawn, out float darkness)
        {
            hour = Mathf.Repeat(hour, 24f);
            var i = 0;
            while (i < Keys.Length - 2 && hour >= Keys[i + 1])
                i++;
            var k = Mathf.InverseLerp(Keys[i], Keys[i + 1], hour);
            var a = Of(Phases[i]);
            var b = Of(Phases[i + 1]);
            for (var c = 0; c < 5; c++)
                _pal[c] = Color32.Lerp(a[Mathf.Min(c, a.Length - 1)], b[Mathf.Min(c, b.Length - 1)], k);
            night = hour < 5.5f || hour >= 19.5f;
            dawn = hour >= 5f && hour < 8.5f;
            darkness = hour < 5f || hour >= 20f ? 1f
                : hour < 7f ? Mathf.InverseLerp(7f, 5f, hour)
                : hour > 18f ? Mathf.InverseLerp(18f, 20f, hour) : 0f;
            return _pal;
        }

        Color32[] Of(int phase)
        {
            switch (phase)
            {
                case 0: return _theme.Dawn;
                case 1: return _theme.Day;
                case 2: return _theme.Dusk;
                default: return _theme.Night;
            }
        }
    }
}
