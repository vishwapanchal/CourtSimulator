using CourtSim.Runtime.Common;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>
    /// Moving details that make the room feel lived in: ceiling fans, a familiar sight in Indian court halls.
    /// Built after the courtroom so it is never folded into the static batch (static meshes can't rotate).
    /// </summary>
    public static class CourtroomAmbience
    {
        public static Transform Build(Transform parent, Vector3 roomSize, IMaterialProvider materials)
        {
            Transform root = UnityObjectUtil.CreateChild("Ambience", parent);
            float ceiling = roomSize.y;
            Vector3[] spots =
            {
                new Vector3(-3f, 0f, 3.5f), new Vector3(3f, 0f, 3.5f),
                new Vector3(-3f, 0f, -3.5f), new Vector3(3f, 0f, -3.5f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                BuildFan(root, $"CeilingFan_{i + 1}", new Vector3(spots[i].x, ceiling, spots[i].z), materials, i * 47f);
            }

            return root;
        }

        private static void BuildFan(Transform parent, string name, Vector3 ceilingPoint, IMaterialProvider materials, float startAngle)
        {
            Material metal = materials.Get(SurfaceKind.Metal);
            Transform fan = UnityObjectUtil.CreateChild(name, parent);
            fan.localPosition = ceilingPoint;

            // Downrod from the ceiling, then a motor housing.
            CourtroomBuilder.AddBlock(fan, "Downrod", new Vector3(0f, -0.3f, 0f), new Vector3(0.04f, 0.6f, 0.04f), metal);
            CourtroomBuilder.AddBlock(fan, "Canopy", new Vector3(0f, -0.03f, 0f), new Vector3(0.16f, 0.06f, 0.16f), metal);

            // Only the rotor spins; it carries the motor and three blades 120 degrees apart.
            Transform rotor = UnityObjectUtil.CreateChild("Rotor", fan);
            rotor.localPosition = new Vector3(0f, -0.65f, 0f);
            rotor.localRotation = Quaternion.Euler(0f, startAngle, 0f);
            CourtroomBuilder.AddBlock(rotor, "Motor", Vector3.zero, new Vector3(0.26f, 0.12f, 0.26f), metal);
            for (int b = 0; b < 3; b++)
            {
                Transform arm = UnityObjectUtil.CreateChild($"Blade_{b + 1}", rotor);
                arm.localRotation = Quaternion.Euler(0f, b * 120f, 0f);
                CourtroomBuilder.AddBlock(arm, "Blade", new Vector3(0f, -0.01f, 0.62f), new Vector3(0.14f, 0.015f, 0.95f), metal);
            }

            rotor.gameObject.AddComponent<CeilingFan>();
        }
    }
}
