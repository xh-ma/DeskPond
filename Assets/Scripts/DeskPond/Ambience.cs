using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// White-noise companion and sound effects, all synthesised at startup: no audio files ship.
    ///
    /// Two quiet loops (water lapping, wind in the reeds) and a handful of one-shots. The mixer runs
    /// at 22 kHz with a large buffer, which is plenty for soft noise and costs far less CPU than
    /// the 48 kHz default. Muting pauses the listener entirely, so a muted pet does no audio work.
    /// </summary>
    public class Ambience : MonoBehaviour
    {
        public enum Sfx
        {
            Plop,
            Splash,
            Reel,
            Meow,
            Caw,
            Coin
        }

        const int Rate = 22050;

        static Ambience _instance;

        AudioSource _water, _wind, _fx;
        AudioClip[] _clips;
        bool _on = true;
        float _volume = 0.45f;
        float _meowTimer = 300f;
        float _windSwell;
        bool _suspended;
        readonly System.Random _rng = new System.Random(3);

        public static Ambience Create()
        {
            if (_instance != null)
                return _instance;

            // Must happen before any clip exists: Reset tears the audio system down.
            var cfg = AudioSettings.GetConfiguration();
            cfg.sampleRate = Rate;
            cfg.dspBufferSize = 1024;
            cfg.speakerMode = AudioSpeakerMode.Mono;
            AudioSettings.Reset(cfg);

            var go = new GameObject("Ambience");
            _instance = go.AddComponent<Ambience>();
            if (FindFirstObjectByType<AudioListener>() == null)
                go.AddComponent<AudioListener>();
            _instance.Build();
            return _instance;
        }

        public static void Play(Sfx sfx, float gain = 1f)
        {
            if (_instance == null || !_instance._on || _instance._suspended)
                return;
            var clip = _instance._clips[(int)sfx];
            _instance._fx.PlayOneShot(clip, _instance._volume * gain * 0.7f);
        }

        public void SetEnabled(bool on, float volume)
        {
            _on = on;
            _volume = Mathf.Clamp01(volume);
            Apply();
        }

        /// <summary>Silences everything while a fullscreen app is in front.</summary>
        public void Suspend(bool suspended)
        {
            if (_suspended == suspended)
                return;
            _suspended = suspended;
            Apply();
        }

        void Apply()
        {
            var audible = _on && !_suspended && _volume > 0.001f;
            AudioListener.pause = !audible;
            AudioListener.volume = audible ? 1f : 0f;
            if (_water != null)
                _water.volume = _volume * 0.32f;
        }

        void Build()
        {
            _water = Loop("Water", WaterLoop());
            _wind = Loop("Wind", WindLoop());
            _fx = gameObject.AddComponent<AudioSource>();
            _fx.playOnAwake = false;
            _clips = new[]
            {
                Make("plop", 0.25f, Plop), Make("splash", 0.6f, Splash), Make("reel", 0.7f, Reel),
                Make("meow", 0.65f, Meow), Make("caw", 0.7f, Caw), Make("coin", 0.4f, Coin)
            };
            Apply();
        }

        AudioSource Loop(string name, AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            s.playOnAwake = false;
            s.Play();
            return s;
        }

        void Update()
        {
            if (!_on || _suspended)
                return;

            var dt = Time.unscaledDeltaTime;
            _water.volume = _volume * 0.32f;
            _windSwell += dt * 0.07f;
            _wind.volume = _volume * (0.10f + 0.08f * Mathf.Sin(_windSwell) * Mathf.Sin(_windSwell * 0.37f + 1f));

            // An occasional meow, every few minutes, so it is company rather than noise.
            _meowTimer -= dt;
            if (_meowTimer <= 0f)
            {
                _meowTimer = 240f + (float)_rng.NextDouble() * 600f;
                Play(Sfx.Meow, 0.55f);
            }
        }

        // ------------------------------------------------------------------ synthesis

        delegate float Synth(float t, float dur, System.Random rng);

        static AudioClip Make(string name, float seconds, Synth synth)
        {
            var n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            for (var i = 0; i < n; i++)
                data[i] = synth((float)i / Rate, seconds, rng);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Makes a loop seamless by cross-fading the tail into the head.</summary>
        static AudioClip MakeLoop(string name, float seconds, float[] raw)
        {
            var n = Mathf.CeilToInt(seconds * Rate);
            var fade = raw.Length - n;
            var data = new float[n];
            for (var i = 0; i < n; i++)
                data[i] = raw[i];
            for (var i = 0; i < fade; i++)
            {
                var k = (float)i / fade;
                data[i] = raw[i] * k + raw[n + i] * (1f - k);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip WaterLoop()
        {
            const float seconds = 8f;
            var n = Mathf.CeilToInt((seconds + 1f) * Rate);
            var raw = new float[n];
            var rng = new System.Random(11);
            float brown = 0f, lp = 0f;
            var nextBubble = 0.3f;
            var bubbleT = -1f;
            var bubbleF = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                brown = brown * 0.995f + (float)(rng.NextDouble() * 2 - 1) * 0.06f;
                lp += (brown - lp) * 0.08f;
                var lap = 0.55f + 0.45f * Mathf.Sin(t * 1.3f) * Mathf.Sin(t * 0.47f + 2f);
                var s = lp * lap * 1.4f;

                // Small bubbles: short rising blips.
                if (t > nextBubble)
                {
                    bubbleT = 0f;
                    bubbleF = 380f + (float)rng.NextDouble() * 500f;
                    nextBubble = t + 0.2f + (float)rng.NextDouble() * 1.4f;
                }

                if (bubbleT >= 0f)
                {
                    bubbleT += 1f / Rate;
                    var env = Mathf.Exp(-bubbleT * 45f);
                    s += Mathf.Sin(2f * Mathf.PI * bubbleF * bubbleT * (1f + bubbleT * 6f)) * env * 0.08f;
                    if (bubbleT > 0.12f)
                        bubbleT = -1f;
                }

                raw[i] = s;
            }

            return MakeLoop("water", seconds, raw);
        }

        static AudioClip WindLoop()
        {
            const float seconds = 10f;
            var n = Mathf.CeilToInt((seconds + 1f) * Rate);
            var raw = new float[n];
            var rng = new System.Random(23);
            float lp1 = 0f, lp2 = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var white = (float)(rng.NextDouble() * 2 - 1);
                // A crude band-pass: two one-pole low-passes, one subtracted from the other.
                lp1 += (white - lp1) * 0.12f;
                lp2 += (white - lp2) * 0.02f;
                var band = lp1 - lp2;
                var gust = 0.5f + 0.5f * Mathf.Sin(t * 0.63f) * Mathf.Sin(t * 0.21f + 1f);
                raw[i] = band * gust * 0.5f;
            }

            return MakeLoop("wind", seconds, raw);
        }

        static float Plop(float t, float dur, System.Random rng)
        {
            var f = Mathf.Lerp(520f, 160f, t / dur);
            return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 22f) * 0.6f;
        }

        static float _splashLp;

        static float Splash(float t, float dur, System.Random rng)
        {
            var white = (float)(rng.NextDouble() * 2 - 1);
            _splashLp += (white - _splashLp) * Mathf.Lerp(0.6f, 0.08f, t / dur);
            var env = t < 0.02f ? t / 0.02f : Mathf.Exp(-(t - 0.02f) * 7f);
            return _splashLp * env * 0.7f;
        }

        static float Reel(float t, float dur, System.Random rng)
        {
            // Ratchet clicks speeding up.
            var rate = Mathf.Lerp(18f, 34f, t / dur);
            var phase = Mathf.Repeat(t * rate, 1f);
            var click = phase < 0.08f ? (float)(rng.NextDouble() * 2 - 1) * (1f - phase / 0.08f) : 0f;
            return click * 0.35f * (1f - t / dur * 0.5f);
        }

        static float Meow(float t, float dur, System.Random rng)
        {
            var k = t / dur;
            // Pitch rises then falls: "mi-aow".
            var f0 = k < 0.3f ? Mathf.Lerp(560f, 820f, k / 0.3f) : Mathf.Lerp(820f, 480f, (k - 0.3f) / 0.7f);
            f0 *= 1f + Mathf.Sin(t * 38f) * 0.012f;
            // Formants slide from an "i" through "a" to "u".
            var f1 = k < 0.4f ? Mathf.Lerp(350f, 850f, k / 0.4f) : Mathf.Lerp(850f, 420f, (k - 0.4f) / 0.6f);
            var f2 = k < 0.4f ? Mathf.Lerp(2300f, 1300f, k / 0.4f) : Mathf.Lerp(1300f, 900f, (k - 0.4f) / 0.6f);
            var phase = 2f * Mathf.PI * f0 * t;
            var s = 0f;
            for (var h = 1; h <= 10; h++)
            {
                var fh = f0 * h;
                var amp = Mathf.Exp(-Mathf.Pow((fh - f1) / 260f, 2f)) + 0.6f * Mathf.Exp(-Mathf.Pow((fh - f2) / 380f, 2f));
                s += Mathf.Sin(phase * h) * amp / Mathf.Sqrt(h);
            }

            var env = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((dur - t) / 0.18f);
            return s * env * 0.22f;
        }

        static float Caw(float t, float dur, System.Random rng)
        {
            var on = (t < 0.28f) || (t > 0.36f && t < 0.62f);
            if (!on)
                return 0f;
            var local = t < 0.3f ? t : t - 0.36f;
            var f = 420f - local * 300f;
            var saw = 2f * Mathf.Repeat(f * t, 1f) - 1f;
            var noise = (float)(rng.NextDouble() * 2 - 1) * 0.35f;
            var env = Mathf.Clamp01(local / 0.02f) * Mathf.Clamp01((0.26f - local) / 0.08f);
            return (saw * 0.6f + noise) * env * 0.25f;
        }

        static float Coin(float t, float dur, System.Random rng)
        {
            var f = t < 0.08f ? 1320f : 1760f;
            var env = Mathf.Exp(-(t < 0.08f ? t : t - 0.08f) * 14f);
            return Mathf.Sin(2f * Mathf.PI * f * t) * env * 0.3f;
        }
    }
}
