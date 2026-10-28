using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CsAim.EditorTools
{
    /// <summary>
    /// WebGL build for GitHub Pages, used by .github/workflows/pages.yml (GameCI passes
    /// -customBuildPath) and by Tools > CS Aim > Build WebGL locally.
    /// </summary>
    public static class WebGLBuild
    {
        [MenuItem("Tools/CS Aim/Build WebGL (GitHub Pages)")]
        public static void BuildFromMenu() => Build();

        public static void Build()
        {
            string path = GetArg("-customBuildPath") ?? "build/WebGL/WebGL";

            PlayerSettings.productName = "CS Aim Trainer";
            PlayerSettings.companyName = "CsAim";
            PlayerSettings.runInBackground = true;
            // GitHub Pages can't send Content-Encoding for Unity's .br/.gz files, so ship uncompressed.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.template = "PROJECT:FullWindow";
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.dataCaching = true;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = path,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });

            Debug.Log($"[CS Aim] WebGL build {report.summary.result}: {path} ({report.summary.totalSize / (1024 * 1024)} MB)");
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
