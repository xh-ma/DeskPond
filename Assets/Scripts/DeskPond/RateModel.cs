using System;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Estimates a quantile of a stream using the P-square algorithm (Jain and Chlamtac, 1985).
    ///
    /// Five markers track the quantile and are nudged toward their ideal positions as samples
    /// arrive, so the estimate needs constant memory instead of the whole history. That matters
    /// here because the alternative, keeping every sample to sort later, grows without bound in a
    /// program that runs all day.
    /// </summary>
    [Serializable]
    public class P2Quantile
    {
        [SerializeField] double p = 0.5;

        // Marker heights, their actual positions, and the positions they are aiming for.
        // While count < 5 the heights double as an unsorted buffer of the first samples.
        [SerializeField] double[] q = new double[5];
        [SerializeField] double[] n = new double[5];
        [SerializeField] double[] np = new double[5];
        [SerializeField] int count;

        public P2Quantile() { }

        public P2Quantile(double quantile) => p = Clamp01(quantile);

        public int Count => count;

        /// <summary>Samples needed before the estimate means anything.</summary>
        public bool Ready => count >= 5;

        /// <summary>The current estimate, or the midpoint of the buffer while still warming up.</summary>
        public double Value
        {
            get
            {
                if (count == 0)
                    return 0d;
                if (count < 5)
                {
                    var sorted = new double[count];
                    Array.Copy(q, sorted, count);
                    Array.Sort(sorted);
                    return sorted[count / 2];
                }

                return q[2];
            }
        }

        public void Add(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
                return;

            if (count < 5)
            {
                q[count] = x;
                count++;
                if (count == 5)
                {
                    Array.Sort(q);
                    for (var i = 0; i < 5; i++)
                        n[i] = i + 1;
                    np[0] = 1d;
                    np[1] = 1d + 2d * p;
                    np[2] = 1d + 4d * p;
                    np[3] = 3d + 2d * p;
                    np[4] = 5d;
                }

                return;
            }

            var k = FindCell(x);

            for (var i = k + 1; i < 5; i++)
                n[i] += 1d;

            // Desired positions advance by a fixed share of each new sample.
            np[0] += 0d;
            np[1] += p / 2d;
            np[2] += p;
            np[3] += (1d + p) / 2d;
            np[4] += 1d;

            for (var i = 1; i <= 3; i++)
                Adjust(i);

            count++;
        }

        /// <summary>
        /// Locates the cell holding x, stretching the outer markers when x falls beyond them.
        /// </summary>
        int FindCell(double x)
        {
            if (x < q[0])
            {
                q[0] = x;
                return 0;
            }

            if (x >= q[4])
            {
                q[4] = x;
                return 3;
            }

            for (var i = 1; i < 4; i++)
            {
                if (x < q[i])
                    return i - 1;
            }

            return 3;
        }

        /// <summary>Moves one interior marker a single step when it has drifted a full place out.</summary>
        void Adjust(int i)
        {
            var d = np[i] - n[i];
            var canRise = d >= 1d && n[i + 1] - n[i] > 1d;
            var canFall = d <= -1d && n[i - 1] - n[i] < -1d;
            if (!canRise && !canFall)
                return;

            var s = Math.Sign(d);
            var candidate = Parabolic(i, s);

            // The parabolic prediction can overshoot a neighbour and break the ordering the whole
            // algorithm rests on. Linear interpolation is the documented fallback.
            q[i] = q[i - 1] < candidate && candidate < q[i + 1]
                ? candidate
                : Linear(i, s);
            n[i] += s;
        }

        double Parabolic(int i, int d)
        {
            var span = n[i + 1] - n[i - 1];
            var upper = n[i + 1] - n[i];
            var lower = n[i] - n[i - 1];
            if (span == 0d || upper == 0d || lower == 0d)
                return q[i];

            return q[i] + d / span * ((lower + d) * (q[i + 1] - q[i]) / upper
                                      + (upper - d) * (q[i] - q[i - 1]) / lower);
        }

        double Linear(int i, int d)
        {
            var span = n[i + d] - n[i];
            return span == 0d ? q[i] : q[i] + d * (q[i + d] - q[i]) / span;
        }

        static double Clamp01(double v) => v < 0.001d ? 0.001d : v > 0.999d ? 0.999d : v;
    }

    /// <summary>
    /// Turns "how fast does this user actually work" into "how many inputs should the next fish
    /// cost", so a fast typist and a slow one wait a comparable number of minutes.
    ///
    /// The fixed 10 to 1000 range it replaces meant the same numbers played very differently:
    /// someone at 300 events a minute got a fish every couple of minutes, someone at 30 waited
    /// half an hour. Sampling the user's own rate and multiplying by a target wait fixes that.
    ///
    /// Rate samples are taken once a minute and empty minutes are skipped, otherwise stepping away
    /// from the desk would drag the median toward zero and make fish nearly free on return.
    /// </summary>
    public class RateModel
    {
        /// <summary>Active minutes needed before the estimate replaces the fixed roll.</summary>
        public const int WarmupSamples = 12;

        const float SampleSeconds = 60f;

        readonly P2Quantile _median;
        float _windowElapsed;
        int _windowEvents;

        public RateModel(P2Quantile restored = null) =>
            _median = restored ?? new P2Quantile(0.5d);

        /// <summary>Serialisable state, so the model is not relearned from scratch every launch.</summary>
        public P2Quantile State => _median;

        public int Samples => _median.Count;

        public bool Ready => _median.Count >= WarmupSamples;

        /// <summary>Median input events per minute across the user's active minutes.</summary>
        public float MedianRate => (float)_median.Value;

        /// <summary>Feeds one frame of counted input; closes a sample every minute of wall clock.</summary>
        public void Tick(float dt, int events)
        {
            _windowElapsed += dt;
            _windowEvents += events;

            if (_windowElapsed < SampleSeconds)
                return;

            // An idle minute says nothing about how fast the user types, so it is not a sample.
            if (_windowEvents > 0)
                _median.Add(_windowEvents * 60f / _windowElapsed);

            _windowElapsed = 0f;
            _windowEvents = 0;
        }

        /// <summary>
        /// Inputs the next fish should cost. Returns false until warmed up, so the caller can keep
        /// using the fixed range on a fresh save rather than pacing off two minutes of data.
        /// </summary>
        public bool TryGetTarget(float targetMinutes, float rodFactor, int minInputs, int maxInputs,
            float spread, System.Random rng, out int target)
        {
            target = 0;
            if (!Ready)
                return false;

            var rate = MedianRate;
            if (rate <= 0.01f)
                return false;

            // Keep some roll in it: a perfectly predictable timer stops feeling like fishing.
            var jitter = 1f + ((float)rng.NextDouble() * 2f - 1f) * Mathf.Clamp01(spread);
            var raw = rate * Mathf.Max(0.1f, targetMinutes) * Mathf.Max(0.01f, rodFactor) * jitter;

            var low = Mathf.Max(1, Mathf.RoundToInt(minInputs * rodFactor));
            var high = Mathf.Max(low + 1, Mathf.RoundToInt(maxInputs * rodFactor));
            target = Mathf.Clamp(Mathf.RoundToInt(raw), low, high);
            return true;
        }
    }
}
