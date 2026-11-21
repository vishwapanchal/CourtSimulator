using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>Spins a ceiling fan's blades. Fans are not static, so they are left out of static batching.</summary>
    [DisallowMultipleComponent]
    public sealed class CeilingFan : MonoBehaviour
    {
        [SerializeField] private float _degreesPerSecond = 220f;

        private void Update()
        {
            transform.Rotate(0f, _degreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
