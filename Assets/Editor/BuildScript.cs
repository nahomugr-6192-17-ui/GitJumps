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

            // Ensure synchronization across filename formats
            string buildDir = Path.Combine("GitJumps - Web", "Build");
            if (Directory.Exists(buildDir))
            {
                CopyIfExists(Path.Combine(buildDir, "GitJumps.data.br"), Path.Combine(buildDir, "GitJumps - Web.data.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps.framework.js.br"), Path.Combine(buildDir, "GitJumps - Web.framework.js.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps.wasm.br"), Path.Combine(buildDir, "GitJumps - Web.wasm.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps.loader.js"), Path.Combine(buildDir, "GitJumps - Web.loader.js"));

                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.data.br"), Path.Combine(buildDir, "GitJumps_v4.data.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.framework.js.br"), Path.Combine(buildDir, "GitJumps_v4.framework.js.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.wasm.br"), Path.Combine(buildDir, "GitJumps_v4.wasm.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.loader.js"), Path.Combine(buildDir, "GitJumps_v4.loader.js"));

                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.data.br"), Path.Combine(buildDir, "GitJumps_v5.data.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.framework.js.br"), Path.Combine(buildDir, "GitJumps_v5.framework.js.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.wasm.br"), Path.Combine(buildDir, "GitJumps_v5.wasm.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.loader.js"), Path.Combine(buildDir, "GitJumps_v5.loader.js"));

                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.data.br"), Path.Combine(buildDir, "GitJumps_v8.data.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.framework.js.br"), Path.Combine(buildDir, "GitJumps_v8.framework.js.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.wasm.br"), Path.Combine(buildDir, "GitJumps_v8.wasm.br"));
                CopyIfExists(Path.Combine(buildDir, "GitJumps - Web.loader.js"), Path.Combine(buildDir, "GitJumps_v8.loader.js"));
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        else
        {
            Debug.LogError("WebGL Build failed: " + summary.result + " with " + summary.totalErrors + " errors");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
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
