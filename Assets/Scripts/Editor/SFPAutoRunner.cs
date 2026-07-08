using UnityEditor;

class SFPScriptChangeWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (!SFPAutoRunner.Enabled) return;
        if (!AnyScript(importedAssets) && !AnyScript(deletedAssets) && !AnyScript(movedAssets)) return;
        SessionState.SetBool(SFPAutoRunner.PendingKey, true);
        if (EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();
    }

    static bool AnyScript(string[] paths)
    {
        foreach (var p in paths)
        {
            if (p.StartsWith("Assets/Scripts/") && p.EndsWith(".cs")) return true;
            if (p.StartsWith("Assets/Shaders/") && (p.EndsWith(".shader") || p.EndsWith(".hlsl"))) return true;
        }
        return false;
    }
}

[InitializeOnLoad]
public static class SFPAutoRunner
{
    const string EnabledKey = "SFP_AutoRun_Enabled";
    const string EnterPlayKey = "SFP_AutoRun_EnterPlay";
    public const string PendingKey = "SFP_AutoRun_Pending";

    public static bool Enabled => EditorPrefs.GetBool(EnabledKey, true);
    static bool EnterPlay => EditorPrefs.GetBool(EnterPlayKey, true);

    static double _nextPollTime;
    static long _lastStamp = -1;
    static bool _dirty;
    static double _lastChangeTime;

    static SFPAutoRunner()
    {
        EnsureBackgroundExecution();
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += Poll;
    }

    static void EnsureBackgroundExecution()
    {
        if (EditorPrefs.GetInt("InteractionMode", 0) != 1)
        {
            EditorPrefs.SetInt("InteractionMode", 1);
            var m = typeof(EditorApplication).GetMethod(
                "UpdateInteractionModeSettings",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (m != null) m.Invoke(null, null);
            UnityEngine.Debug.Log("[SFPAutoRunner] Interaction Mode set to No Throttling for background automation.");
        }
        if (!PlayerSettings.runInBackground)
            PlayerSettings.runInBackground = true;
    }

    static void TryRun()
    {
        if (!Enabled) return;
        if (!SessionState.GetBool(PendingKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += TryRun;
            return;
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryRun;
            return;
        }
        if (EditorUtility.scriptCompilationFailed)
            return;
        SessionState.SetBool(PendingKey, false);
        EditorApplication.ExecuteMenuItem("SFP/Build FloodTestShip Scene");
        if (EnterPlay)
            EditorApplication.EnterPlaymode();
    }

    static void Poll()
    {
        if (!Enabled) return;
        if (EditorApplication.timeSinceStartup < _nextPollTime) return;
        _nextPollTime = EditorApplication.timeSinceStartup + 2.0;

        long stamp = ComputeScriptStamp();
        if (_lastStamp < 0) { _lastStamp = stamp; return; }
        if (stamp != _lastStamp)
        {
            _lastStamp = stamp;
            _dirty = true;
            _lastChangeTime = EditorApplication.timeSinceStartup;
        }

        if (_dirty && EditorApplication.timeSinceStartup - _lastChangeTime > 5.0)
        {
            _dirty = false;
            SessionState.SetBool(PendingKey, true);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
                return;
            }
            EditorApplication.delayCall += TryRun;
        }
    }

    static long ComputeScriptStamp()
    {
        long stamp = 17;
        stamp = FoldDir(stamp, System.IO.Path.Combine(UnityEngine.Application.dataPath, "Scripts"), "*.cs");
        string shaders = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Shaders");
        stamp = FoldDir(stamp, shaders, "*.shader");
        stamp = FoldDir(stamp, shaders, "*.hlsl");
        return stamp;
    }

    static long FoldDir(long stamp, string dir, string pattern)
    {
        if (!System.IO.Directory.Exists(dir)) return stamp;
        foreach (var f in System.IO.Directory.EnumerateFiles(dir, pattern, System.IO.SearchOption.AllDirectories))
        {
            stamp = stamp * 31 + System.IO.File.GetLastWriteTimeUtc(f).Ticks;
            stamp = stamp * 31 + f.GetHashCode();
        }
        return stamp;
    }

    [MenuItem("SFP/Auto Rebuild After Compile")]
    static void ToggleEnabled() => EditorPrefs.SetBool(EnabledKey, !Enabled);

    [MenuItem("SFP/Auto Rebuild After Compile", true)]
    static bool ToggleEnabledValidate()
    {
        Menu.SetChecked("SFP/Auto Rebuild After Compile", Enabled);
        return true;
    }

    [MenuItem("SFP/Auto Enter Play Mode")]
    static void ToggleEnterPlay() => EditorPrefs.SetBool(EnterPlayKey, !EnterPlay);

    [MenuItem("SFP/Auto Enter Play Mode", true)]
    static bool ToggleEnterPlayValidate()
    {
        Menu.SetChecked("SFP/Auto Enter Play Mode", EnterPlay);
        return true;
    }
}
