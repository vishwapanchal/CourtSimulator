using CourtSim.Core.Courtroom;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>Tags a generated fixture with its layout id and role.</summary>
    [DisallowMultipleComponent]
    public sealed class CourtroomFixture : MonoBehaviour
    {
        [SerializeField] private string _id;
        [SerializeField] private FixtureKind _kind;

        public string Id => _id;

        public FixtureKind Kind => _kind;

        internal void Configure(string id, FixtureKind kind)
        {
            _id = id;
            _kind = kind;
        }
    }
}
