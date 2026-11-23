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

            // A pedestal fan beside the dais, turned towards the judge's chair (behind the judge's seat view).
            BuildPedestalFan(root, new Vector3(3.2f, 0f, 8.7f), 80f, materials);
            return root;
        }

        /// <summary>A pedestal fan whose blades spin to face local -Z (turned by <paramref name="yaw"/> degrees).</summary>
        private static void BuildPedestalFan(Transform parent, Vector3 floorPoint, float yaw, IMaterialProvider materials)
        {
            Material metal = materials.Get(SurfaceKind.Metal);
            Mesh cylinder = CourtroomBuilder.CylinderMesh;
            Transform fan = UnityObjectUtil.CreateChild("PedestalFan", parent);
            fan.localPosition = floorPoint;
            fan.localRotation = Quaternion.Euler(0f, yaw, 0f);
            CourtroomBuilder.AddMesh(fan, "Base", cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.4f, 0.02f, 0.4f), Vector3.zero, metal);
            CourtroomBuilder.AddMesh(fan, "Pole", cylinder, new Vector3(0f, 0.66f, 0f), new Vector3(0.04f, 0.63f, 0.04f), Vector3.zero, metal);
            CourtroomBuilder.AddBlock(fan, "Motor", new Vector3(0f, 1.35f, 0.08f), new Vector3(0.14f, 0.14f, 0.2f), metal);
            CourtroomBuilder.AddDisc(fan, "Guard_Back", new Vector3(0f, 1.35f, -0.02f), 0.52f, 0.01f, materials.Get(SurfaceKind.SteelGrey));

            // The rotor's spin axis (its local Y) is tipped forward to point along the fan's -Z.
            Transform rotor = UnityObjectUtil.CreateChild("Rotor", fan);
            rotor.localPosition = new Vector3(0f, 1.35f, -0.06f);
            rotor.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            CourtroomBuilder.AddBlock(rotor, "Hub", Vector3.zero, new Vector3(0.09f, 0.05f, 0.09f), metal);
            for (int b = 0; b < 3; b++)
            {
                Transform arm = UnityObjectUtil.CreateChild($"Blade_{b + 1}", rotor);
                arm.localRotation = Quaternion.Euler(0f, b * 120f, 0f);
                CourtroomBuilder.AddBlock(arm, "Blade", new Vector3(0f, 0f, 0.13f), new Vector3(0.11f, 0.01f, 0.19f), metal);
            }

            rotor.gameObject.AddComponent<CeilingFan>();
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
