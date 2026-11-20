using System.Collections.Generic;
using CourtSim.Core.Courtroom;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>Root of a generated courtroom. Gives systems typed access to fixtures and the spawn point.</summary>
    [DisallowMultipleComponent]
    public sealed class CourtroomRoot : MonoBehaviour
    {
        [SerializeField] private List<CourtroomFixture> _fixtures = new List<CourtroomFixture>();
        [SerializeField] private Transform _spawnPoint;

        public IReadOnlyList<CourtroomFixture> Fixtures => _fixtures;

        public Transform SpawnPoint => _spawnPoint;

        public Vector3 SpawnPosition => _spawnPoint != null ? _spawnPoint.position : transform.position;

        public float SpawnYaw => _spawnPoint != null ? _spawnPoint.eulerAngles.y : transform.eulerAngles.y;

        /// <summary>Returns the first fixture of <paramref name="kind"/>, or null.</summary>
        public CourtroomFixture Find(FixtureKind kind)
        {
            foreach (CourtroomFixture fixture in _fixtures)
            {
                if (fixture != null && fixture.Kind == kind)
                {
                    return fixture;
                }
            }

            return null;
        }

        internal void Register(CourtroomFixture fixture)
        {
            _fixtures.Add(fixture);
        }

        internal void SetSpawnPoint(Transform spawnPoint)
        {
            _spawnPoint = spawnPoint;
        }
    }
}
