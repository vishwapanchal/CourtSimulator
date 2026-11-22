using System.IO;
using CourtSim.Core.Courtroom;
using CourtSim.Runtime.App;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CourtSim.EditorTools.Tests
{
    public sealed class CourtroomSceneGeneratorTests
    {
        private const string TempFolder = "Assets/CourtSimTestTemp";
        private const string TempScene = TempFolder + "/GeneratedCourtRoom.unity";

        private SceneSetup[] _sceneSetup;
        private EditorBuildSettingsScene[] _buildScenes;

        [SetUp]
        public void SetUp()
        {
            _sceneSetup = EditorSceneManager.GetSceneManagerSetup();
            _buildScenes = EditorBuildSettings.scenes;
        }

        [TearDown]
        public void TearDown()
        {
            EditorBuildSettings.scenes = _buildScenes;
            if (_sceneSetup.Length > 0 && !string.IsNullOrEmpty(_sceneSetup[0].path))
            {
                EditorSceneManager.RestoreSceneManagerSetup(_sceneSetup);
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            AssetDatabase.DeleteAsset(TempFolder);
        }

        [Test]
        public void Generate_SavesSceneWithWiredBootstrap()
        {
            bool ok = CourtroomSceneGenerator.Generate(TempScene, false);

            Assert.That(ok, Is.True);
            Assert.That(File.Exists(TempScene), Is.True);

            Scene scene = SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.EqualTo(TempScene));
            var bootstrap = Object.FindObjectOfType<CourtSimBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.Courtroom, Is.Not.Null);
            Assert.That(bootstrap.Player, Is.Not.Null);
            Assert.That(bootstrap.UI, Is.Not.Null);
            Assert.That(bootstrap.Courtroom.Find(FixtureKind.JudgeBench), Is.Not.Null);
            Assert.That(bootstrap.GetComponent<CaseSessionController>(), Is.Not.Null);
        }

        [Test]
        public void Generate_MarksCourtroomRenderersStatic_ButNotThePlayer()
        {
            CourtroomSceneGenerator.Generate(TempScene, false);
            var bootstrap = Object.FindObjectOfType<CourtSimBootstrap>();

            foreach (MeshRenderer renderer in bootstrap.Courtroom.GetComponentsInChildren<MeshRenderer>(true))
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                bool moves = renderer.GetComponentInParent<CourtSim.Runtime.Courtroom.CeilingFan>() != null
                    || renderer.GetComponentInParent<CourtSim.Runtime.Characters.CharacterAnimator>() != null;

                // Fan rotors spin and people move, so they stay dynamic; everything else is batched as static.
                Assert.That((flags & StaticEditorFlags.BatchingStatic) != 0, Is.EqualTo(!moves), renderer.name);
            }

            Assert.That(GameObjectUtility.GetStaticEditorFlags(bootstrap.Player.gameObject), Is.EqualTo((StaticEditorFlags)0));
        }

        [Test]
        public void Generate_UsesMaterialAssets_SoTheSceneSurvivesReload()
        {
            CourtroomSceneGenerator.Generate(TempScene, false);
            var bootstrap = Object.FindObjectOfType<CourtSimBootstrap>();

            foreach (MeshRenderer renderer in bootstrap.Courtroom.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.That(AssetDatabase.Contains(renderer.sharedMaterial), Is.True, renderer.name);
            }
        }

        [Test]
        public void Generate_WithBuildSettingsFlag_PutsSceneFirst()
        {
            CourtroomSceneGenerator.Generate(TempScene, true);

            Assert.That(EditorBuildSettings.scenes.Length, Is.GreaterThan(0));
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(TempScene));
        }

        [Test]
        public void Generate_WithoutBuildSettingsFlag_LeavesBuildSettingsAlone()
        {
            CourtroomSceneGenerator.Generate(TempScene, false);

            Assert.That(Paths(EditorBuildSettings.scenes), Is.EqualTo(Paths(_buildScenes)));
        }

        private static string[] Paths(EditorBuildSettingsScene[] scenes)
        {
            var paths = new string[scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
            {
                paths[i] = scenes[i].path;
            }

            return paths;
        }
    }
}
