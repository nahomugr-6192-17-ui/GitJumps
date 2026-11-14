using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

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

            // Ensure synchronization across both filename formats (GitJumps.* and GitJumps - Web.*)
            string buildDir = Path.Combine("GitJumps - Web", "Build");
            if (Directory.Exists(buildDir))
            {
                CopyIfExists(Path.Combine(buildDir, "GitJumps.data.br"), Path.Combine(buildDir, "GitJumps - Web.data.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps.framework.js.br"), Path.Combine(buildDir, "GitJumps - Web.framework.js.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps.wasm.br"), Path.Combine(buildDir, "GitJumps - Web.wasm.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps.loader.js"), Path.Combine(buildDir, "GitJumps - Web.loader.js"));
            }

            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError("WebGL Build failed: " + summary.result + " with " + summary.totalErrors + " errors");
            EditorApplication.Exit(1);
        }
    }

    private static void CopyIfExists(string src, string dst)
    {
        if (File.Exists(src))
        {
            File.Copy(src, dst, true);
        }
    }
}
