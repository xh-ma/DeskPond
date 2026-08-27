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
}
