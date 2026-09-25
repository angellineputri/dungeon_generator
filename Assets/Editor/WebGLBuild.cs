using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLBuild
{
    const string OutputDir = "Builds/WebGL";
    static readonly string[] Scenes = { "Assets/Scenes/Main.unity" };

    [MenuItem("Build/WebGL Playtest Build")]
    public static void BuildPlaytest()
    {

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

        var options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = OutputDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
            Debug.Log($"[WebGLBuild] Succeeded — {summary.totalSize} bytes in {summary.totalTime}. Output: {OutputDir}");
        else
            Debug.LogError($"[WebGLBuild] Build {summary.result} with {summary.totalErrors} error(s).");
    }
}
