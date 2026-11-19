using System;
using System.IO;
using UnityEditor.Build;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the Windows player configured as a desktop overlay. Run with
/// -executeMethod DeskPondBuild.Run and DP_BUILD_DIR pointing at the output folder.
/// </summary>
public static class DeskPondBuild
{
    public static void Run()
    {
        var dir = Environment.GetEnvironmentVariable("DP_BUILD_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            Debug.LogError("BUILD: DP_BUILD_DIR not set");
            EditorApplication.Exit(2);
            return;
        }

        Configure();

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/DeskPond.unity" },
            locationPathName = Path.Combine(dir, "DeskPond.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
            ShrinkAllocators(Path.Combine(dir, "DeskPond_Data", "boot.config"));
        Debug.Log("BUILD: result=" + summary.result + " errors=" + summary.totalErrors +
                  " size=" + summary.totalSize + " out=" + options.locationPathName);

        foreach (var step in report.steps)
        foreach (var msg in step.messages)
        {
            if (msg.type == LogType.Error || msg.type == LogType.Exception)
                Debug.Log("BUILD ERR: " + msg.content);
        }

        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>
    /// Feeds Assets/Icon/icon_NN.png into the player icon slots.
    ///
    /// Unity asks for a fixed set of sizes; whichever ones exist on disk are used directly, and any
    /// missing size is scaled from the nearest available source with point filtering so pixel art
    /// stays crisp instead of being smoothed into mush.
    /// </summary>
    static void ApplyIcons()
    {
        var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
        if (sizes == null || sizes.Length == 0)
        {
            Debug.Log("ICONS: platform reports no icon slots");
            return;
        }

        // Load every icon_NN.png we have, keyed by its pixel size.
        var available = new SortedDictionary<int, Texture2D>();
        foreach (var path in Directory.GetFiles("Assets/Icon", "icon_*.png"))
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace('\\', '/'));
            if (tex != null)
                available[tex.width] = tex;
        }

        if (available.Count == 0)
        {
            Debug.LogWarning("ICONS: no icon_*.png found in Assets/Icon");
            return;
        }

        var icons = new Texture2D[sizes.Length];
        for (var i = 0; i < sizes.Length; i++)
            icons[i] = PickOrScale(available, sizes[i]);

        PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Any);
        Debug.Log("ICONS: filled " + sizes.Length + " slots (" + string.Join(",", sizes) +
                  ") from " + available.Count + " source files");
    }

    static Texture2D PickOrScale(SortedDictionary<int, Texture2D> available, int wanted)
    {
        if (available.TryGetValue(wanted, out var exact))
            return exact;

        // Prefer scaling down from a larger source; only scale up as a last resort.
        Texture2D source = null;
        foreach (var pair in available)
        {
            source = pair.Value;
            if (pair.Key >= wanted)
                break;
        }

        if (source == null)
            return null;

        var scaled = new Texture2D(wanted, wanted, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point
        };
        for (var y = 0; y < wanted; y++)
        for (var x = 0; x < wanted; x++)
        {
            var sx = x * source.width / wanted;
            var sy = y * source.height / wanted;
            scaled.SetPixel(x, y, source.GetPixel(sx, sy));
        }

        scaled.Apply(false, false);
        return scaled;
    }

    /// <summary>
    /// Unity's default allocator blocks are sized for games that stream gigabytes: 16-32 MB
    /// reserved per allocator and per thread. A pet drawing a few sprites needs a fraction of
    /// that, and these blocks are most of its resident memory. Overrides go in boot.config,
    /// which the player reads at startup.
    /// </summary>
    static void ShrinkAllocators(string bootConfig)
    {
        var lines = new List<string>(File.Exists(bootConfig) ? File.ReadAllLines(bootConfig) : new string[0]);
        lines.RemoveAll(l => l.StartsWith("memorysetup-") || l.StartsWith("gfx-enable-gfx-jobs") ||
                             l.StartsWith("gfx-threading-mode") || l.StartsWith("job-worker-count"));
        lines.AddRange(new[]
        {
            "memorysetup-main-allocator-block-size=2097152",
            "memorysetup-thread-allocator-block-size=1048576",
            "memorysetup-gfx-main-allocator-block-size=1048576",
            "memorysetup-gfx-thread-allocator-block-size=1048576",
            "memorysetup-cache-allocator-block-size=1048576",
            "memorysetup-typetree-allocator-block-size=524288",
            "memorysetup-bucket-allocator-block-size=4194304",
            "memorysetup-bucket-allocator-block-count=1",
            "memorysetup-temp-allocator-size-main=2097152",
            "memorysetup-temp-allocator-size-gfx=131072",
            "memorysetup-temp-allocator-size-job-worker=131072",
            "memorysetup-temp-allocator-size-preload-manager=1048576",
            "memorysetup-job-temp-allocator-block-size=524288",
            "memorysetup-profiler-allocator-block-size=262144",
            "memorysetup-profiler-bucket-allocator-block-size=262144",
            "memorysetup-profiler-bucket-allocator-block-count=1",
            "gfx-enable-gfx-jobs=0",
            "job-worker-count=1",
        });
        File.WriteAllLines(bootConfig, lines);
        Debug.Log("BUILD: shrank allocators in " + bootConfig);
    }

    static void Configure()
    {
        ApplyIcons();

        // A desktop pet must keep fishing while the user works in other windows.
        PlayerSettings.runInBackground = true;
        PlayerSettings.visibleInBackground = true;
        PlayerSettings.forceSingleInstance = true;

        // Borderless window sized to the desktop; DesktopWindow re-styles it at runtime.
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.resizableWindow = false;
        // Start small: DesktopWindow sizes the window to a band along the taskbar at runtime, and
        // a full-screen first frame would allocate a full-screen swapchain for nothing.
        PlayerSettings.defaultScreenWidth = 640;
        PlayerSettings.defaultScreenHeight = 240;

        // Footprint: strip unused engine and managed code.
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.High);
        PlayerSettings.gcIncremental = true;
        PlayerSettings.usePlayerLog = true;

        // Rendering a few sprites on its own thread costs a thread and its allocators for nothing.
        PlayerSettings.MTRendering = false;
        PlayerSettings.graphicsJobs = false;

        // Unity 6 makes the "Made with Unity" splash optional on every licence tier. A desktop pet
        // that shows a full-screen logo on launch is jarring, so it is turned off.
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;

        // Per-pixel window transparency needs the legacy (non-flip) swapchain on D3D11.
        PlayerSettings.useFlipModelSwapchain = false;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });

        Debug.Log("BUILD: configured " + PlayerSettings.defaultScreenWidth + "x" +
                  PlayerSettings.defaultScreenHeight + " flipModel=" + PlayerSettings.useFlipModelSwapchain);
    }
}
