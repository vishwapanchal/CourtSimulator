using System;
using System.Collections.Generic;
using System.IO;
using CourtSim.Core.Courtroom;
using CourtSim.Runtime.App;
using CourtSim.Runtime.Characters;
using CourtSim.Runtime.Courtroom;
using CourtSim.Runtime.Player;
using CourtSim.Runtime.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CourtSim.EditorTools
{
    /// <summary>
    /// Generates Assets/Scenes/CourtRoom.unity from <see cref="CourtroomLayout.CreateDefault"/>. The scene
    /// is a build artefact of the layout code: regenerate it rather than hand-editing generated objects.
    /// </summary>
    public static class CourtroomSceneGenerator
    {
        public const string ScenePath = "Assets/Scenes/CourtRoom.unity";

        private const StaticEditorFlags StaticFlags =
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

        [MenuItem("CourtSim/Generate CourtRoom Scene", false, 0)]
        public static void GenerateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (File.Exists(ScenePath)
                && !EditorUtility.DisplayDialog("CourtSim", $"Overwrite {ScenePath}?", "Regenerate", "Cancel"))
            {
                return;
            }

            if (Generate())
            {
                EditorSceneManager.OpenScene(ScenePath);
            }
        }

        /// <summary>Entry point for CI: <c>Unity -batchmode -quit -executeMethod CourtSim.EditorTools.CourtroomSceneGenerator.GenerateFromCommandLine</c>.</summary>
        public static void GenerateFromCommandLine()
        {
            bool ok;
            try
            {
                ok = Generate();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ok = false;
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Builds and saves the CourtRoom scene and puts it first in the build settings.</summary>
        public static bool Generate()
        {
            return Generate(ScenePath, true);
        }

        /// <summary>
        /// Builds the scene into a new, single open scene and saves it to <paramref name="scenePath"/>.
        /// Returns false (and logs why) if the layout is invalid or the scene cannot be saved.
        /// </summary>
        public static bool Generate(string scenePath, bool addToBuildSettings)
        {
            CourtroomLayout layout = CourtroomLayout.CreateDefault();
            IReadOnlyList<string> errors = layout.Validate();
            if (errors.Count > 0)
            {
                Debug.LogError("CourtRoom scene not generated; the layout is invalid:\n" + string.Join("\n", errors));
                return false;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var materials = new EditorMaterialProvider();
            CourtroomRoot courtroom = CourtroomBuilder.Build(layout, materials);
            var roomSize = new Vector3(layout.RoomSize.X, layout.RoomSize.Y, layout.RoomSize.Z);
            CourtroomLighting.Build(courtroom.transform, roomSize);
            CourtroomLighting.ApplyAmbient();
            CourtroomAmbience.Build(courtroom.transform, roomSize, materials);
            CourtroomCast.Build(courtroom.transform, materials);
            foreach (MeshRenderer renderer in courtroom.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponentInParent<CeilingFan>() != null || renderer.GetComponentInParent<CharacterAnimator>() != null)
                {
                    continue; // fans spin and people move, so they must not be static
                }

                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticFlags);
            }

            FirstPersonController player = PlayerFactory.Create(courtroom.SpawnPosition, courtroom.SpawnYaw);
            UIManager ui = UIManager.Create();
            var bootstrap = new GameObject("CourtSimBootstrap").AddComponent<CourtSimBootstrap>();
            bootstrap.gameObject.AddComponent<CaseSessionController>();
            bootstrap.Configure(courtroom, player, ui);

            EditorAssetUtil.EnsureFolder(Path.GetDirectoryName(scenePath).Replace('\\', '/'));
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                Debug.LogError($"Failed to save {scenePath}.");
                return false;
            }

            AssetDatabase.SaveAssets();
            if (addToBuildSettings)
            {
                AddToBuildSettings(scenePath);
            }

            Debug.Log($"Generated {scenePath} with {courtroom.Fixtures.Count} fixtures.");
            return true;
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    /// <summary>Offers to generate the CourtRoom scene the first time the project is opened.</summary>
    [InitializeOnLoad]
    internal static class FirstOpenSceneSetup
    {
        private const string PromptedKey = "CourtSim.SceneSetupPrompted";

        static FirstOpenSceneSetup()
        {
            EditorApplication.delayCall += PromptIfSceneMissing;
        }

        private static void PromptIfSceneMissing()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (File.Exists(CourtroomSceneGenerator.ScenePath) || SessionState.GetBool(PromptedKey, false))
            {
                return;
            }

            SessionState.SetBool(PromptedKey, true);
            if (EditorUtility.DisplayDialog(
                "CourtSim",
                "The CourtRoom scene has not been generated yet. Generate it now?\n\nYou can do this later from CourtSim > Generate CourtRoom Scene.",
                "Generate",
                "Later"))
            {
                CourtroomSceneGenerator.GenerateFromMenu();
            }
        }
    }
}
