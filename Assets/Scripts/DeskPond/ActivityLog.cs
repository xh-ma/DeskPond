using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// One slice of the day: how much input happened, and which windows were in front.
    ///
    /// Entries are parallel arrays rather than a dictionary because JsonUtility cannot serialise
    /// Dictionary. Written as a dictionary this compiles, runs, and silently saves nothing.
    /// </summary>
    [Serializable]
    public class ActivityBucket
    {
        public long startUnix;
        public int inputs;

        // One entry per distinct window seen in this slice. When title recording is off, entries
        // are per process and titles stays empty.
        public string[] apps = Array.Empty<string>();
        public string[] titles = Array.Empty<string>();
        public int[] appSeconds = Array.Empty<int>();

        // Clicks and keystrokes attributed to whatever was in front when they happened.
        public int[] appInputs = Array.Empty<int>();
    }

    [Serializable]
    class ActivityFile
    {
        public int version = 1;
        public List<ActivityBucket> buckets = new List<ActivityBucket>();
    }

    /// <summary>Time spent in one app or window, produced by the rollup helpers.</summary>
    public struct AppTotal
    {
        public string App;
        public string Title;
        public int Seconds;
        public int Inputs;
    }

    /// <summary>
    /// A local record of when the computer was busy and what was on screen, kept in five minute
    /// buckets in activity.json.
    ///
    /// It lives in its own file rather than in save.json on purpose. The save is small and
    /// critical; losing it loses the game. This log is large and disposable; deleting it loses
    /// only statistics. Sharing a file would also mean rewriting megabytes on every autosave.
    ///
    /// Window titles are only recorded when GameConfig.recordWindowTitles is on, which is off by
    /// default. A title carries far more than a process name does: document names, page titles and
    /// chat partners all live there. That is fine to keep about yourself and not fine to collect
    /// silently from whoever else runs a build, so it is a decision the person running it makes.
    /// Titles also expire sooner than the buckets they sit in; see Prune.
    /// </summary>
    public class ActivityLog
    {
        public const int BucketSeconds = 300;

        /// <summary>Distinct windows tracked per bucket before extra ones collapse to app only.</summary>
        const int MaxEntriesPerBucket = 24;

        const string FileName = "activity.json";
        const float FlushIntervalSeconds = 60f;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        readonly ActivityFile _file;
        readonly List<string> _apps = new List<string>();
        readonly List<string> _titles = new List<string>();
        readonly List<float> _seconds = new List<float>();
        readonly List<int> _inputs = new List<int>();

        long _bucketStart;
        int _bucketInputs;
        float _flushCooldown = FlushIntervalSeconds;
        bool _dirty;

        public ActivityLog() : this(null) { }

        ActivityLog(ActivityFile file)
        {
            _file = file ?? new ActivityFile();
            _bucketStart = BucketStartFor(DateTime.UtcNow);
        }

        public IReadOnlyList<ActivityBucket> Buckets => _file.buckets;

        public static ActivityLog Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var parsed = JsonUtility.FromJson<ActivityFile>(File.ReadAllText(FilePath));
                    if (parsed?.buckets != null)
                        return new ActivityLog(parsed);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not load activity log. " + ex.Message);
            }

            return new ActivityLog();
        }

        /// <summary>
        /// Counted clicks and keystrokes for the current slice. Passing the app that was in front
        /// also credits them to it, which is what turns a bare daily total into a per application
        /// breakdown.
        /// </summary>
        public void AddInputs(int events, string foregroundApp = null, string foregroundTitle = null)
        {
            if (events <= 0)
                return;
            _bucketInputs += events;

            if (!string.IsNullOrEmpty(foregroundApp))
            {
                var index = Find(foregroundApp, foregroundTitle);
                if (index < 0)
                    index = AddEntry(foregroundApp, foregroundTitle);
                if (index >= 0)
                    _inputs[index] += events;
            }

            _dirty = true;
        }

        /// <summary>
        /// Attributes foreground time. Pass a null title when title recording is off; entries then
        /// merge per process.
        /// </summary>
        public void AddAppTime(string app, string title, float seconds)
        {
            if (string.IsNullOrEmpty(app) || seconds <= 0f)
                return;

            var index = Find(app, title);
            if (index < 0)
                index = AddEntry(app, title);
            if (index < 0)
                return;

            _seconds[index] += seconds;
            _dirty = true;
        }

        /// <summary>
        /// Adds a slot for one window. A media player or browser can rewrite its title every
        /// second, so past the cap the title is dropped and the time folds into an app only entry
        /// rather than letting one chatty window bloat the file.
        /// </summary>
        int AddEntry(string app, string title)
        {
            if (_apps.Count >= MaxEntriesPerBucket)
            {
                var plain = Find(app, null);
                if (plain >= 0)
                    return plain;
                title = null;
            }

            _apps.Add(app);
            _titles.Add(title ?? string.Empty);
            _seconds.Add(0f);
            _inputs.Add(0);
            return _apps.Count - 1;
        }

        int Find(string app, string title)
        {
            var wanted = title ?? string.Empty;
            for (var i = 0; i < _apps.Count; i++)
            {
                if (_apps[i] == app && _titles[i] == wanted)
                    return i;
            }

            return -1;
        }

        /// <summary>Rolls the slice over when it expires and flushes on a timer.</summary>
        public void Tick(float dt, int retentionDays, int titleRetentionDays)
        {
            var nowStart = BucketStartFor(DateTime.UtcNow);
            if (nowStart != _bucketStart)
            {
                CloseBucket();
                _bucketStart = nowStart;
            }

            _flushCooldown -= dt;
            if (_flushCooldown <= 0f)
            {
                Flush(retentionDays, titleRetentionDays);
                _flushCooldown = FlushIntervalSeconds;
            }
        }

        void CloseBucket()
        {
            if (_bucketInputs > 0 || _apps.Count > 0)
            {
                var bucket = new ActivityBucket
                {
                    startUnix = _bucketStart,
                    inputs = _bucketInputs,
                    apps = _apps.ToArray(),
                    titles = _titles.ToArray(),
                    appSeconds = new int[_seconds.Count],
                    appInputs = _inputs.ToArray()
                };

                for (var i = 0; i < _seconds.Count; i++)
                    bucket.appSeconds[i] = Mathf.RoundToInt(_seconds[i]);

                _file.buckets.Add(bucket);
                _dirty = true;
            }

            _bucketInputs = 0;
            _apps.Clear();
            _titles.Clear();
            _seconds.Clear();
            _inputs.Clear();
        }

        public void Flush(int retentionDays, int titleRetentionDays)
        {
            if (!_dirty)
                return;

            Prune(retentionDays, titleRetentionDays);

            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(_file));
                _dirty = false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not write activity log. " + ex.Message);
            }
        }

        /// <summary>Writes the open slice too, for quit.</summary>
        public void FlushNow(int retentionDays, int titleRetentionDays)
        {
            CloseBucket();
            _dirty = true;
            Flush(retentionDays, titleRetentionDays);
        }

        /// <summary>
        /// Two expiry windows, because the two kinds of data are useful for different lengths of
        /// time. Rhythm statistics want months of history; nobody needs to know which file was open
        /// on a Tuesday afternoon last month, so titles are stripped well before their bucket goes.
        /// </summary>
        void Prune(int retentionDays, int titleRetentionDays)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var bucketCutoff = now - (long)Mathf.Max(1, retentionDays) * 86400L;
            var titleCutoff = now - (long)Mathf.Max(0, titleRetentionDays) * 86400L;

            _file.buckets.RemoveAll(b => b.startUnix < bucketCutoff);

            foreach (var bucket in _file.buckets)
            {
                if (bucket.startUnix >= titleCutoff || bucket.titles.Length == 0)
                    continue;
                bucket.titles = Array.Empty<string>();
            }
        }

        /// <summary>Forgets everything on disk and in memory.</summary>
        public void Clear()
        {
            _file.buckets.Clear();
            _bucketInputs = 0;
            _apps.Clear();
            _titles.Clear();
            _seconds.Clear();
            _inputs.Clear();
            _dirty = false;

            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not delete activity log. " + ex.Message);
            }
        }

        /// <summary>Drops every recorded title but keeps the time totals.</summary>
        public void ForgetTitles(int retentionDays)
        {
            foreach (var bucket in _file.buckets)
                bucket.titles = Array.Empty<string>();
            for (var i = 0; i < _titles.Count; i++)
                _titles[i] = string.Empty;

            _dirty = true;
            Flush(retentionDays, 0);
        }

        static long BucketStartFor(DateTime utc)
        {
            var seconds = new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
            return seconds - seconds % BucketSeconds;
        }

        /// <summary>Seconds per app between two instants, largest first.</summary>
        public List<AppTotal> AppTotals(DateTime fromUtc, DateTime toUtc, bool byTitle = false)
        {
            var from = new DateTimeOffset(fromUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            var to = new DateTimeOffset(toUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            var totals = new List<AppTotal>();

            void Fold(string app, string title, int seconds, int inputs)
            {
                var key = byTitle ? title ?? string.Empty : string.Empty;
                var slot = totals.FindIndex(t => t.App == app && t.Title == key);
                if (slot < 0)
                {
                    totals.Add(new AppTotal
                    {
                        App = app, Title = key, Seconds = seconds, Inputs = inputs
                    });
                    return;
                }

                var entry = totals[slot];
                entry.Seconds += seconds;
                entry.Inputs += inputs;
                totals[slot] = entry;
            }

            foreach (var bucket in _file.buckets)
            {
                if (bucket.startUnix < from || bucket.startUnix >= to)
                    continue;

                for (var i = 0; i < bucket.apps.Length; i++)
                {
                    Fold(bucket.apps[i],
                        i < bucket.titles.Length ? bucket.titles[i] : string.Empty,
                        i < bucket.appSeconds.Length ? bucket.appSeconds[i] : 0,
                        i < bucket.appInputs.Length ? bucket.appInputs[i] : 0);
                }
            }

            // The slice being filled right now is where today's most useful numbers live.
            if (_bucketStart >= from && _bucketStart < to)
            {
                for (var i = 0; i < _apps.Count; i++)
                    Fold(_apps[i], _titles[i], Mathf.RoundToInt(_seconds[i]), _inputs[i]);
            }

            totals.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
            return totals;
        }

        /// <summary>
        /// Clicks and keystrokes counted between two instants. Includes the slice still being
        /// filled, so the panel reflects the key you just pressed rather than lagging five minutes.
        /// </summary>
        public int InputTotal(DateTime fromUtc, DateTime toUtc)
        {
            var from = new DateTimeOffset(fromUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            var to = new DateTimeOffset(toUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            var total = 0;

            foreach (var bucket in _file.buckets)
            {
                if (bucket.startUnix >= from && bucket.startUnix < to)
                    total += bucket.inputs;
            }

            if (_bucketStart >= from && _bucketStart < to)
                total += _bucketInputs;

            return total;
        }

        /// <summary>Input events per hour of one local day, for the rhythm chart.</summary>
        public int[] InputsByHour(DateTime localDay)
        {
            var hours = new int[24];
            var dayStart = localDay.Date;

            foreach (var bucket in _file.buckets)
            {
                var local = DateTimeOffset.FromUnixTimeSeconds(bucket.startUnix).LocalDateTime;
                if (local.Date != dayStart)
                    continue;
                hours[local.Hour] += bucket.inputs;
            }

            var open = DateTimeOffset.FromUnixTimeSeconds(_bucketStart).LocalDateTime;
            if (open.Date == dayStart)
                hours[open.Hour] += _bucketInputs;

            return hours;
        }

        /// <summary>
        /// Splits the day into stretches of work. A bucket counts as active above a threshold, and
        /// short gaps are absorbed rather than ending a stretch, otherwise pausing to read
        /// something would chop one afternoon into a dozen fragments.
        /// </summary>
        public List<(DateTime Start, DateTime End)> Sessions(DateTime localDay,
            int activeInputs = 20, int gapBuckets = 2)
        {
            var sessions = new List<(DateTime, DateTime)>();
            var dayStart = localDay.Date;
            var active = new List<DateTime>();

            foreach (var bucket in _file.buckets)
            {
                var local = DateTimeOffset.FromUnixTimeSeconds(bucket.startUnix).LocalDateTime;
                if (local.Date == dayStart && bucket.inputs >= activeInputs)
                    active.Add(local);
            }

            active.Sort();
            if (active.Count == 0)
                return sessions;

            var start = active[0];
            var last = active[0];
            var slack = TimeSpan.FromSeconds((gapBuckets + 1) * BucketSeconds);

            for (var i = 1; i < active.Count; i++)
            {
                if (active[i] - last <= slack)
                {
                    last = active[i];
                    continue;
                }

                sessions.Add((start, last.AddSeconds(BucketSeconds)));
                start = active[i];
                last = active[i];
            }

            sessions.Add((start, last.AddSeconds(BucketSeconds)));
            return sessions;
        }
    }
}
