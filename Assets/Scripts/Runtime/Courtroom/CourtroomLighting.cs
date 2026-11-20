using CourtSim.Runtime.Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>
    /// Real-time lighting rig for the courtroom: warm ceiling fills plus one shadowed key spot over the
    /// well. Only the key light casts shadows, which keeps the rig cheap enough for VR frame budgets.
    /// </summary>
    public static class CourtroomLighting
    {
        private static readonly Color WarmLight = new Color(1f, 0.93f, 0.82f);

        /// <summary>Creates the light objects under <paramref name="parent"/>.</summary>
        public static Transform Build(Transform parent, Vector3 roomSize)
        {
            Transform rig = UnityObjectUtil.CreateChild("Lighting", parent);
            float ceiling = roomSize.y - 0.4f;

            // A shadowless directional fill stops vertical surfaces going flat-black.
            Light fill = CreateLight(rig, "Fill_Directional", LightType.Directional, new Color(0.9f, 0.92f, 1f), 0.25f);
            fill.transform.localRotation = Quaternion.Euler(55f, -30f, 0f);

            float[] fillRows = { roomSize.z * 0.35f, 0f, -roomSize.z * 0.32f };
            for (int i = 0; i < fillRows.Length; i++)
            {
                Light point = CreateLight(rig, $"Fill_Point_{i}", LightType.Point, WarmLight, 1.1f);
                point.transform.localPosition = new Vector3(0f, ceiling, fillRows[i]);
                point.range = roomSize.x * 0.9f;
            }

            Light key = CreateLight(rig, "Key_Spot", LightType.Spot, WarmLight, 1.6f);
            key.transform.localPosition = new Vector3(0f, ceiling, roomSize.z * 0.15f);
            key.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            key.spotAngle = 115f;
            key.range = roomSize.y * 2.5f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.55f;

            return rig;
        }

        /// <summary>Sets scene-wide ambient light. Saved with the scene when called in the editor.</summary>
        public static void ApplyAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.5f, 0.47f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.32f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.13f, 0.1f);
            RenderSettings.fog = false;
        }

        private static Light CreateLight(Transform parent, string name, LightType type, Color color, float intensity)
        {
            Transform t = UnityObjectUtil.CreateChild(name, parent);
            var light = t.gameObject.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }
    }
}
