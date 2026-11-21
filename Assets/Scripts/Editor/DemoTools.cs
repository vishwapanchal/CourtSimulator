using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CourtSim.EditorTools
{
    /// <summary>
    /// Command-line helpers for demos: build a Windows player, or open the Editor straight into Play mode.
    /// Called by <c>demo.bat</c> through <c>-executeMethod</c>.
    /// </summary>
    public static class DemoTools
    {
        public const string PlayerPath = "Builds/Windows/CourtSim.exe";

        /// <summary>Builds the CourtRoom scene as a 64-bit Windows player. Exits with 0 on success.</summary>
        public static void BuildWindowsPlayer()
        {
            // Regenerate the scene first so the build always matches the current layout code.
            bool ok = CourtroomSceneGenerator.Generate();
            if (ok)
            {
                // Presentation mode: borderless full screen at the monitor's native resolution.
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                PlayerSettings.defaultIsNativeResolution = true;
                PlayerSettings.resizableWindow = false;
                PlayerSettings.productName = "CourtSim";
                Directory.CreateDirectory(Path.GetDirectoryName(PlayerPath));
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { CourtroomSceneGenerator.ScenePath },
                    locationPathName = PlayerPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                ok = report.summary.result == BuildResult.Succeeded;
                Debug.Log($"[demo] Build {report.summary.result}: {Path.GetFullPath(PlayerPath)} ({report.summary.totalSize / (1024 * 1024)} MB)");
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Opens the CourtRoom scene and presses Play, for demos run inside the Editor.</summary>
        public static void OpenAndPlay()
        {
            if (!File.Exists(CourtroomSceneGenerator.ScenePath))
            {
                CourtroomSceneGenerator.Generate();
            }

            EditorSceneManager.OpenScene(CourtroomSceneGenerator.ScenePath);
            EditorApplication.isPlaying = true;
        }
    }
}
