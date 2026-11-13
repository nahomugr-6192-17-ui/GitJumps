using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    public static void BuildWebGL()
    {
        string[] scenes = new string[]
        {
            "Assets/Scenes/HomeScene.unity",
            "Assets/Scenes/HowToPlayScene.unity",
            "Assets/Scenes/GamePlayScene.unity"
        };

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = scenes;
        buildPlayerOptions.locationPathName = "GitJumps - Web";
        buildPlayerOptions.target = BuildTarget.WebGL;
        buildPlayerOptions.options = BuildOptions.None;

        Debug.Log("Starting WebGL build to: " + buildPlayerOptions.locationPathName);
        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log("WebGL Build succeeded: " + summary.totalSize + " bytes in " + summary.totalTime.TotalSeconds + " s");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError("WebGL Build failed: " + summary.result + " with " + summary.totalErrors + " errors");
            EditorApplication.Exit(1);
        }
    }
}

