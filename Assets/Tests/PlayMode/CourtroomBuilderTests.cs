using System.Collections;
using CourtSim.Core.Courtroom;
using CourtSim.Runtime.Courtroom;
using CourtSim.Runtime.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CourtSim.Runtime.Tests
{
    public sealed class CourtroomBuilderTests
    {
        private RuntimeMaterialProvider _materials;
        private CourtroomRoot _root;

        [SetUp]
        public void SetUp()
        {
            _materials = new RuntimeMaterialProvider();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
            }

            _materials.Dispose();
        }

        [UnityTest]
        public IEnumerator Build_CreatesOneFixturePerSolidLayoutEntry()
        {
            CourtroomLayout layout = CourtroomLayout.CreateDefault();

            _root = CourtroomBuilder.Build(layout, _materials);
            yield return null;

            int solid = 0;
            foreach (FixtureSpec spec in layout.Fixtures)
            {
                if (spec.IsSolid)
                {
                    solid++;
                }
            }

            Assert.That(_root.Fixtures.Count, Is.EqualTo(solid));
            foreach (FixtureKind kind in CourtroomLayout.RequiredKinds)
            {
                if (kind != FixtureKind.PlayerSpawn)
                {
                    Assert.That(_root.Find(kind), Is.Not.Null, kind.ToString());
                }
            }
        }

        [UnityTest]
        public IEnumerator Build_ColliderMatchesLayoutFootprint()
        {
            CourtroomLayout layout = CourtroomLayout.CreateDefault();
            FixtureSpec bench = layout.Find(FixtureKind.JudgeBench);

            _root = CourtroomBuilder.Build(layout, _materials);
            yield return null;

            var collider = _root.Find(FixtureKind.JudgeBench).GetComponent<BoxCollider>();
            Assert.That(collider.bounds.center.z, Is.EqualTo(bench.Center.Z).Within(1e-3f));
            Assert.That(collider.bounds.size.x, Is.EqualTo(bench.Size.X).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator Build_AddsInteractablesWithLayoutActions()
        {
            CourtroomLayout layout = CourtroomLayout.CreateDefault();

            _root = CourtroomBuilder.Build(layout, _materials);
            yield return null;

            foreach (FixtureSpec spec in layout.Fixtures)
            {
                if (!spec.IsSolid)
                {
                    continue;
                }

                CourtroomFixture fixture = FindById(spec.Id);
                var interactable = fixture.GetComponent<Interactable>();
                if (spec.IsInteractive)
                {
                    Assert.That(interactable, Is.Not.Null, spec.Id);
                    Assert.That(interactable.Action, Is.EqualTo(spec.Action), spec.Id);
                }
                else
                {
                    Assert.That(interactable == null, Is.True, spec.Id);
                }
            }
        }

        [UnityTest]
        public IEnumerator Build_PlacesSpawnPointFromLayout()
        {
            CourtroomLayout layout = CourtroomLayout.CreateDefault();
            FixtureSpec spawn = layout.Find(FixtureKind.PlayerSpawn);

            _root = CourtroomBuilder.Build(layout, _materials);
            yield return null;

            Assert.That(_root.SpawnPoint, Is.Not.Null);
            Assert.That(Vector3.Distance(_root.SpawnPosition, new Vector3(spawn.Center.X, spawn.Center.Y, spawn.Center.Z)), Is.LessThan(1e-3f));
        }

        [UnityTest]
        public IEnumerator RaycastFromWell_HitsJudgeBench()
        {
            _root = CourtroomBuilder.Build(CourtroomLayout.CreateDefault(), _materials);
            yield return new WaitForFixedUpdate();

            bool hit = Physics.Raycast(new Vector3(0f, 1f, 6.5f), Vector3.forward, out RaycastHit info, 3f);

            Assert.That(hit, Is.True);
            Assert.That(info.collider.GetComponentInParent<Interactable>(), Is.Not.Null);
        }

        private CourtroomFixture FindById(string id)
        {
            foreach (CourtroomFixture fixture in _root.Fixtures)
            {
                if (fixture.Id == id)
                {
                    return fixture;
                }
            }

            Assert.Fail($"No fixture with id '{id}'.");
            return null;
        }
    }
}
