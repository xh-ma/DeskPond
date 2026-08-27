using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using DeskPond;

/// <summary>
/// Batch-mode checks for the parts of the activity log and pacing model that fail quietly:
/// serialisation that compiles but stores nothing, rollups that silently drop entries, and
/// clamping that lets a target escape its bounds.
///
/// Run with -executeMethod DeskPondSelfTest.Run. Editor only, so none of it ships.
/// </summary>
public static class DeskPondSelfTest
{
    static int _failed;

    static void Check(string name, bool ok, string detail = "")
    {
        if (!ok)
            _failed++;
        Debug.Log($"SELFTEST {(ok ? "PASS" : "FAIL")}  {name}{(detail == "" ? "" : "  " + detail)}");
    }

    public static void Run()
    {
        _failed = 0;
        var path = ActivityLog.FilePath;
        if (File.Exists(path))
            File.Delete(path);

        ActivityRoundTrip();
        EntryCapHoldsTotalTime();
        TitlesCanBeForgotten();
        TitleRetentionStripsButKeepsTime();
        PacingClampsAndVaries();
        ChangePointFindsPlantedShifts();
        PruningMatchesTheExhaustiveOptimum();
        SessionsFollowTheDaysShape();

        Debug.Log($"SELFTEST DONE failures={_failed}");
        EditorApplication.Exit(_failed == 0 ? 0 : 1);
    }

    /// <summary>The parallel-array schema exists because JsonUtility drops a Dictionary silently.</summary>
    static void ActivityRoundTrip()
    {
        var log = new ActivityLog();
        log.AddInputs(140);
        log.AddAppTime("Code", "GameSession.cs - DeskPond", 300f);
        log.AddAppTime("chrome", "docs", 120f);
        log.AddAppTime("Code", "GameSession.cs - DeskPond", 60f);
        log.FlushNow(30, 7);

        var json = File.ReadAllText(ActivityLog.FilePath);
        Check("write produces non-empty buckets", json.Contains("\"apps\"") && json.Contains("Code"),
            json.Length > 200 ? json.Substring(0, 200) : json);

        var reloaded = ActivityLog.Load();
        var totals = reloaded.AppTotals(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        var code = totals.Find(t => t.App == "Code");
        var chrome = totals.Find(t => t.App == "chrome");
        Check("same app and title merge into one entry", code.Seconds == 360,
            $"Code={code.Seconds}s expected 360");
        Check("second app kept separately", chrome.Seconds == 120, $"chrome={chrome.Seconds}s");
        Check("rollup sorted by time descending", totals.Count > 1 && totals[0].App == "Code");

        var byTitle = reloaded.AppTotals(
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), true);
        var titled = byTitle.Find(t => t.Title == "GameSession.cs - DeskPond");
        Check("titles survive the round trip", titled.Seconds == 360, $"titled={titled.Seconds}s");
    }

    /// <summary>
    /// A window that rewrites its title constantly must not lose time or bloat the file.
    /// </summary>
    static void EntryCapHoldsTotalTime()
    {
        File.Delete(ActivityLog.FilePath);
        var log = new ActivityLog();
        for (var i = 0; i < 200; i++)
            log.AddAppTime("vlc", "playing " + i, 3f);
        log.FlushNow(30, 7);

        var reloaded = ActivityLog.Load();
        var totals = reloaded.AppTotals(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        var vlc = totals.Find(t => t.App == "vlc");
        Check("chatty titles keep all their time", vlc.Seconds == 600, $"vlc={vlc.Seconds}s expected 600");

        var byTitle = reloaded.AppTotals(
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), true);
        Check("distinct entries stay capped", byTitle.Count <= 25, $"entries={byTitle.Count}");
    }

    static void TitlesCanBeForgotten()
    {
        File.Delete(ActivityLog.FilePath);
        var log = new ActivityLog();
        log.AddAppTime("Word", "salary review draft", 240f);
        log.FlushNow(30, 7);
        ActivityLog.Load();

        var log2 = ActivityLog.Load();
        log2.ForgetTitles(30);
        var json = File.ReadAllText(ActivityLog.FilePath);
        Check("ForgetTitles removes the text", !json.Contains("salary review draft"));
        Check("ForgetTitles keeps the app time", json.Contains("Word") && json.Contains("240"));
    }

    /// <summary>Titles expire before the slice holding them does.</summary>
    static void TitleRetentionStripsButKeepsTime()
    {
        File.Delete(ActivityLog.FilePath);
        var log = new ActivityLog();
        log.AddAppTime("Notepad", "private notes", 180f);
        log.FlushNow(30, 0);

        var json = File.ReadAllText(ActivityLog.FilePath);
        Check("zero day title retention strips titles", !json.Contains("private notes"), json);

        var reloaded = ActivityLog.Load();
        var totals = reloaded.AppTotals(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        var np = totals.Find(t => t.App == "Notepad");
        Check("stripping titles leaves the totals", np.Seconds == 180, $"Notepad={np.Seconds}s");
    }

    static void PacingClampsAndVaries()
    {
        var model = new RateModel();
        var rng = new System.Random(7);

        Check("cold model refuses to set a target",
            !model.TryGetTarget(5f, 1f, 10, 4000, 0.3f, rng, out _));

        // 30 active minutes at 120 events per minute.
        for (var i = 0; i < 30; i++)
            model.Tick(60f, 120);

        Check("model warms up after enough active minutes", model.Ready, $"samples={model.Samples}");
        Check("median lands near the true rate", Mathf.Abs(model.MedianRate - 120f) < 12f,
            $"median={model.MedianRate:0.#}/min");

        var got = model.TryGetTarget(5f, 1f, 10, 4000, 0.3f, rng, out var target);
        Check("warm model sets a target", got, $"target={target}");
        Check("target is about five minutes of work", target > 380 && target < 820,
            $"target={target} expected near 600");

        // The spread must move the number without ever leaving the configured bounds.
        var min = int.MaxValue;
        var max = int.MinValue;
        for (var i = 0; i < 400; i++)
        {
            model.TryGetTarget(5f, 1f, 700, 900, 0.9f, rng, out var t);
            min = Mathf.Min(min, t);
            max = Mathf.Max(max, t);
        }

        Check("clamped to the configured floor and ceiling", min >= 700 && max <= 900,
            $"observed {min}..{max}");
        Check("spread actually varies the target", max > min, $"observed {min}..{max}");

        var rod = new RateModel();
        for (var i = 0; i < 30; i++)
            rod.Tick(60f, 120);
        rod.TryGetTarget(5f, 1f, 10, 4000, 0f, rng, out var plain);
        rod.TryGetTarget(5f, ShopDefs.RodFactor(3), 10, 4000, 0f, rng, out var upgraded);
        Check("rod upgrades lower the cost", upgraded < plain, $"{plain} -> {upgraded}");
    }

    static string Show(System.Collections.Generic.List<int> v) => string.Join(",", v);

    /// <summary>A series with known shifts should be cut at those shifts and nowhere else.</summary>
    static void ChangePointFindsPlantedShifts()
    {
        var series = new System.Collections.Generic.List<double>();
        for (var i = 0; i < 20; i++) series.Add(5);
        for (var i = 0; i < 20; i++) series.Add(200);
        for (var i = 0; i < 20; i++) series.Add(5);

        var cuts = ChangePoint.Detect(series, ChangePoint.SuggestedPenalty(series), 3);
        Check("finds both planted changepoints", cuts.Count == 2, "cuts at [" + Show(cuts) + "]");
        if (cuts.Count == 2)
        {
            Check("first cut lands on the rise", Mathf.Abs(cuts[0] - 20) <= 1, "at " + cuts[0]);
            Check("second cut lands on the fall", Mathf.Abs(cuts[1] - 40) <= 1, "at " + cuts[1]);
        }

        var flat = new System.Collections.Generic.List<double>();
        for (var i = 0; i < 40; i++) flat.Add(42);
        var none = ChangePoint.Detect(flat, ChangePoint.SuggestedPenalty(flat), 3);
        Check("a flat series is not split", none.Count == 0, "cuts [" + Show(none) + "]");

        var tiny = new System.Collections.Generic.List<double> { 1, 2 };
        Check("too short to split returns nothing",
            ChangePoint.Detect(tiny, 1d, 3).Count == 0);
        Check("empty input does not throw",
            ChangePoint.Detect(new System.Collections.Generic.List<double>(), 1d).Count == 0);
    }

    /// <summary>
    /// PELT claims its pruning is exact, not a heuristic. The only way to know is to run the same
    /// optimisation without pruning and require an identical answer.
    /// </summary>
    static void PruningMatchesTheExhaustiveOptimum()
    {
        var rng = new System.Random(2024);
        var mismatches = 0;
        var trials = 40;

        for (var t = 0; t < trials; t++)
        {
            var n = 30 + rng.Next(0, 40);
            var series = new System.Collections.Generic.List<double>();
            var level = rng.Next(0, 100);
            for (var i = 0; i < n; i++)
            {
                // Shift the level now and then, and add noise, so the search has real work to do.
                if (rng.NextDouble() < 0.08)
                    level = rng.Next(0, 300);
                series.Add(level + rng.NextDouble() * 20 - 10);
            }

            var penalty = ChangePoint.SuggestedPenalty(series);
            var pelt = ChangePoint.Detect(series, penalty, 2);
            var exact = ChangePoint.DetectExhaustive(series, penalty, 2);
            if (Show(pelt) != Show(exact))
            {
                mismatches++;
                if (mismatches == 1)
                    Debug.Log($"SELFTEST   first mismatch n={n} pelt=[{Show(pelt)}] exact=[{Show(exact)}]");
            }
        }

        Check("pruned PELT equals the unpruned optimum", mismatches == 0,
            $"{trials - mismatches}/{trials} identical");
    }

    /// <summary>A day with one clear working block should come back as one session.</summary>
    static void SessionsFollowTheDaysShape()
    {
        File.Delete(ActivityLog.FilePath);
        var log = new ActivityLog();

        // 10:00 to 12:00 busy, then quiet. Written straight into the file so the times are exact.
        var day = DateTime.Now.Date;
        var buckets = new System.Collections.Generic.List<string>();
        for (var slot = 0; slot < 288; slot++)
        {
            var local = day.AddSeconds(slot * ActivityLog.BucketSeconds);
            var busy = local.Hour >= 10 && local.Hour < 12;
            if (!busy)
                continue;
            var unix = new DateTimeOffset(local).ToUnixTimeSeconds();
            buckets.Add($"{{\"startUnix\":{unix},\"inputs\":300,\"apps\":[\"Code\"]," +
                        "\"titles\":[\"\"],\"appSeconds\":[290],\"appInputs\":[300]}");
        }

        File.WriteAllText(ActivityLog.FilePath,
            "{\"version\":1,\"buckets\":[" + string.Join(",", buckets) + "]}");

        var reloaded = ActivityLog.Load();
        var sessions = reloaded.Sessions(day);
        Check("one busy block gives one session", sessions.Count == 1, $"got {sessions.Count}");
        if (sessions.Count == 1)
        {
            var length = sessions[0].End - sessions[0].Start;
            Check("session covers about two hours",
                Mathf.Abs((float)length.TotalMinutes - 120f) <= 20f,
                $"{length.TotalMinutes:0} minutes, {sessions[0].Start:HH:mm} to {sessions[0].End:HH:mm}");
        }

        File.Delete(ActivityLog.FilePath);
    }
}
