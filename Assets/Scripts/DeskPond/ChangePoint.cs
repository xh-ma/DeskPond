using System;
using System.Collections.Generic;

namespace DeskPond
{
    /// <summary>
    /// Segments a series where its mean shifts, using PELT (Killick, Fearnhead and Eckley, 2012).
    ///
    /// The day is a run of five minute input counts. A fixed threshold answers "was this slice
    /// busy", which is a different and worse question than "did the pace change here": someone
    /// working steadily but slowly never crosses a global threshold, and a burst of clicking in a
    /// browser crosses it without being work. Changepoints locate the boundaries from the series
    /// itself, so each person is measured against their own baseline.
    ///
    /// PELT minimises the total segment cost plus a penalty per changepoint, and prunes candidates
    /// that can never be optimal. The pruning is exact, not a heuristic: the result matches the
    /// unpruned dynamic program, which DeskPondSelfTest checks directly.
    /// </summary>
    public static class ChangePoint
    {
        /// <summary>
        /// Cost of fitting one mean to values[a..b], as the sum of squared deviations. Prefix sums
        /// make it O(1), which is what keeps the whole search cheap.
        /// </summary>
        struct Cost
        {
            readonly double[] _sum;
            readonly double[] _sumSquares;

            public Cost(IReadOnlyList<double> values)
            {
                _sum = new double[values.Count + 1];
                _sumSquares = new double[values.Count + 1];
                for (var i = 0; i < values.Count; i++)
                {
                    _sum[i + 1] = _sum[i] + values[i];
                    _sumSquares[i + 1] = _sumSquares[i] + values[i] * values[i];
                }
            }

            /// <summary>Inclusive range, zero based.</summary>
            public double Of(int a, int b)
            {
                var length = b - a + 1;
                if (length <= 0)
                    return 0d;

                var total = _sum[b + 1] - _sum[a];
                var squares = _sumSquares[b + 1] - _sumSquares[a];
                var sse = squares - total * total / length;
                // Floating point can push an exactly-constant segment a hair below zero.
                return sse > 0d ? sse : 0d;
            }
        }

        /// <summary>
        /// A penalty in the spirit of BIC: charge more for a changepoint when the series is long
        /// or noisy, so a jittery day is not chopped into dozens of segments.
        /// </summary>
        public static double SuggestedPenalty(IReadOnlyList<double> values, double strength = 2d)
        {
            if (values == null || values.Count < 2)
                return 0d;

            var mean = 0d;
            foreach (var v in values)
                mean += v;
            mean /= values.Count;

            var variance = 0d;
            foreach (var v in values)
                variance += (v - mean) * (v - mean);
            variance /= values.Count;

            // A flat series has no scale of its own; any positive penalty then means "no splits".
            if (variance <= 0d)
                return 1d;

            return strength * variance * Math.Log(values.Count);
        }

        /// <summary>
        /// Indices where the mean changes. Each returned index is the first element of a new
        /// segment, so segment boundaries are 0, r[0], r[1], ..., values.Count.
        /// </summary>
        public static List<int> Detect(IReadOnlyList<double> values, double penalty,
            int minSegment = 1)
        {
            var result = new List<int>();
            if (values == null || values.Count == 0)
                return result;

            var n = values.Count;
            minSegment = Math.Max(1, minSegment);
            if (n < 2 * minSegment)
                return result;

            var cost = new Cost(values);
            var f = new double[n + 1];
            var previous = new int[n + 1];

            // Starting at -penalty makes the single-segment case cost exactly Cost(0, n-1).
            f[0] = -penalty;
            previous[0] = 0;

            var candidates = new List<int> { 0 };
            var keep = new List<int>();

            for (var t = minSegment; t <= n; t++)
            {
                var best = double.PositiveInfinity;
                var bestStart = 0;

                foreach (var s in candidates)
                {
                    if (t - s < minSegment)
                        continue;

                    var value = f[s] + cost.Of(s, t - 1) + penalty;
                    if (value < best)
                    {
                        best = value;
                        bestStart = s;
                    }
                }

                if (double.IsPositiveInfinity(best))
                    continue;

                f[t] = best;
                previous[t] = bestStart;

                // Prune: a start that is already worse than the best total here can never win
                // later, because extending it only adds cost. Candidates too recent to have
                // formed a segment are kept, since they have not had their chance yet.
                keep.Clear();
                foreach (var s in candidates)
                {
                    if (t - s < minSegment || f[s] + cost.Of(s, t - 1) <= f[t])
                        keep.Add(s);
                }

                keep.Add(t);
                candidates.Clear();
                candidates.AddRange(keep);
            }

            // Walk the chain of starts back from the end.
            for (var t = n; t > 0; t = previous[t])
            {
                if (previous[t] > 0)
                    result.Add(previous[t]);
                if (previous[t] == t)
                    break;
            }

            result.Reverse();
            return result;
        }

        /// <summary>
        /// The same optimisation without pruning. Kept because it is the reference PELT is checked
        /// against: both must return the same segmentation, which is what makes the pruning safe.
        /// </summary>
        public static List<int> DetectExhaustive(IReadOnlyList<double> values, double penalty,
            int minSegment = 1)
        {
            var result = new List<int>();
            if (values == null || values.Count == 0)
                return result;

            var n = values.Count;
            minSegment = Math.Max(1, minSegment);
            if (n < 2 * minSegment)
                return result;

            var cost = new Cost(values);
            var f = new double[n + 1];
            var previous = new int[n + 1];
            for (var i = 1; i <= n; i++)
                f[i] = double.PositiveInfinity;

            f[0] = -penalty;

            for (var t = minSegment; t <= n; t++)
            {
                for (var s = 0; s <= t - minSegment; s++)
                {
                    if (double.IsPositiveInfinity(f[s]))
                        continue;

                    var value = f[s] + cost.Of(s, t - 1) + penalty;
                    if (value < f[t])
                    {
                        f[t] = value;
                        previous[t] = s;
                    }
                }
            }

            for (var t = n; t > 0; t = previous[t])
            {
                if (previous[t] > 0)
                    result.Add(previous[t]);
                if (previous[t] == t)
                    break;
            }

            result.Reverse();
            return result;
        }
    }
}
