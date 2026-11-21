using System;
using CourtSim.Core.Courtroom;
using CourtSim.Runtime.Common;
using CourtSim.Runtime.Interaction;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>
    /// Turns a <see cref="CourtroomLayout"/> into scene geometry. Used both at runtime and by the
    /// editor scene generator, so a baked scene and a runtime-built one are identical.
    /// </summary>
    /// <remarks>
    /// Each fixture gets one root object carrying its <see cref="BoxCollider"/> (matching the validated
    /// layout footprint exactly) and, if interactive, an <see cref="Interactable"/>. Decorative meshes
    /// live under a "Visual" child and have no colliders, which keeps physics predictable and cheap.
    /// </remarks>
    public static class CourtroomBuilder
    {
        public const string RootName = "Courtroom";

        private const float WallThickness = 0.2f;

        public static CourtroomRoot Build(CourtroomLayout layout, IMaterialProvider materials, Transform parent = null)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (materials == null)
            {
                throw new ArgumentNullException(nameof(materials));
            }

            var rootObject = new GameObject(RootName);
            rootObject.transform.SetParent(parent, false);
            var root = rootObject.AddComponent<CourtroomRoot>();

            BuildShell(layout.RoomSize.ToVector3(), UnityObjectUtil.CreateChild("Shell", rootObject.transform), materials);

            Transform fixturesParent = UnityObjectUtil.CreateChild("Fixtures", rootObject.transform);
            foreach (FixtureSpec spec in layout.Fixtures)
            {
                if (spec.Kind == FixtureKind.PlayerSpawn)
                {
                    Transform marker = UnityObjectUtil.CreateChild("PlayerSpawn", rootObject.transform);
                    marker.localPosition = spec.Center.ToVector3();
                    marker.localRotation = Quaternion.Euler(0f, spec.FacingYawDegrees, 0f);
                    root.SetSpawnPoint(marker);
                    continue;
                }

                root.Register(BuildFixture(spec, fixturesParent, materials));
            }

            if (Application.isPlaying)
            {
                // Hundreds of small static meshes collapse into a handful of draw calls.
                StaticBatchingUtility.Combine(rootObject);
            }

            return root;
        }

        /// <summary>Adds a unit-cube mesh scaled to <paramref name="size"/>. Shared by every decoration routine.</summary>
        internal static Transform AddBlock(
            Transform parent,
            string name,
            Vector3 localCenter,
            Vector3 size,
            Material material,
            bool withCollider = false)
        {
            var block = new GameObject(name);
            Transform transform = block.transform;
            transform.SetParent(parent, false);
            transform.localPosition = localCenter;
            transform.localScale = size;

            block.AddComponent<MeshFilter>().sharedMesh = BuiltinAssets.CubeMesh;
            block.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (withCollider)
            {
                block.AddComponent<BoxCollider>();
            }

            return transform;
        }

        private static CourtroomFixture BuildFixture(FixtureSpec spec, Transform parent, IMaterialProvider materials)
        {
            var fixtureObject = new GameObject($"{spec.Kind}_{spec.Id}");
            Transform transform = fixtureObject.transform;
            transform.SetParent(parent, false);
            transform.localPosition = spec.Center.ToVector3();

            var fixture = fixtureObject.AddComponent<CourtroomFixture>();
            fixture.Configure(spec.Id, spec.Kind);

            if (spec.IsSolid)
            {
                fixtureObject.AddComponent<BoxCollider>().size = spec.Size.ToVector3();
            }

            if (spec.IsInteractive)
            {
                fixtureObject.AddComponent<Interactable>().Configure(spec.Action, spec.Prompt);
            }

            float yaw = SnapYaw(spec);
            Transform visual = UnityObjectUtil.CreateChild("Visual", transform);
            visual.localRotation = Quaternion.Euler(0f, yaw, 0f);
            FixtureDecorator.Decorate(spec.Kind, visual, LocalExtents(spec.Size.ToVector3(), yaw), materials);

            return fixture;
        }

        /// <summary>Footprints are axis-aligned, so facings are limited to quarter turns.</summary>
        private static float SnapYaw(FixtureSpec spec)
        {
            float snapped = Mathf.Repeat(Mathf.Round(spec.FacingYawDegrees / 90f) * 90f, 360f);
            if (!Mathf.Approximately(Mathf.DeltaAngle(snapped, spec.FacingYawDegrees), 0f))
            {
                Debug.LogWarning($"Fixture '{spec.Id}' facing {spec.FacingYawDegrees}° was snapped to {snapped}°.");
            }

            return snapped;
        }

        /// <summary>World-axis size expressed in the rotated visual's local axes (width, height, depth).</summary>
        private static Vector3 LocalExtents(Vector3 worldSize, float snappedYaw)
        {
            bool quarterTurn = Mathf.Approximately(snappedYaw, 90f) || Mathf.Approximately(snappedYaw, 270f);
            return quarterTurn ? new Vector3(worldSize.z, worldSize.y, worldSize.x) : worldSize;
        }

        private static void BuildShell(Vector3 room, Transform shell, IMaterialProvider materials)
        {
            float w = room.x;
            float h = room.y;
            float d = room.z;
            float t = WallThickness;

            Material wall = materials.Get(SurfaceKind.Wall);
            AddBlock(shell, "Floor", new Vector3(0f, -t * 0.5f, 0f), new Vector3(w + (2f * t), t, d + (2f * t)), materials.Get(SurfaceKind.Floor), true);
            AddBlock(shell, "Ceiling", new Vector3(0f, h + (t * 0.5f), 0f), new Vector3(w + (2f * t), t, d + (2f * t)), materials.Get(SurfaceKind.Ceiling), true);
            AddBlock(shell, "Wall_Front", new Vector3(0f, h * 0.5f, (d + t) * 0.5f), new Vector3(w + (2f * t), h, t), wall, true);
            AddBlock(shell, "Wall_Back", new Vector3(0f, h * 0.5f, -(d + t) * 0.5f), new Vector3(w + (2f * t), h, t), wall, true);
            AddBlock(shell, "Wall_Right", new Vector3((w + t) * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), wall, true);
            AddBlock(shell, "Wall_Left", new Vector3(-(w + t) * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), wall, true);

            // Timber wainscot with a lighter cap rail on every wall.
            const float PanelHeight = 1.1f;
            const float PanelDepth = 0.04f;
            Material wainscot = materials.Get(SurfaceKind.Wainscot);
            Material cap = materials.Get(SurfaceKind.LightWood);
            AddWainscot(shell, "Front", new Vector3(0f, 0f, (d * 0.5f) - (PanelDepth * 0.5f)), new Vector3(w, 0f, PanelDepth), PanelHeight, wainscot, cap);
            AddWainscot(shell, "Back", new Vector3(0f, 0f, -(d * 0.5f) + (PanelDepth * 0.5f)), new Vector3(w, 0f, PanelDepth), PanelHeight, wainscot, cap);
            AddWainscot(shell, "Right", new Vector3((w * 0.5f) - (PanelDepth * 0.5f), 0f, 0f), new Vector3(PanelDepth, 0f, d), PanelHeight, wainscot, cap);
            AddWainscot(shell, "Left", new Vector3(-(w * 0.5f) + (PanelDepth * 0.5f), 0f, 0f), new Vector3(PanelDepth, 0f, d), PanelHeight, wainscot, cap);

            BuildBenchBackdrop(shell, d, materials);

            // High windows on the side walls with maroon curtains, and fluorescent tube lights between them,
            // as in most Indian district court halls.
            Material glow = materials.Get(SurfaceKind.WindowGlow);
            Material curtain = materials.Get(SurfaceKind.Curtain);
            Material tube = materials.Get(SurfaceKind.TubeLight);
            for (int i = -1; i <= 1; i++)
            {
                float z = i * (d * 0.25f);
                foreach (float side in new[] { -1f, 1f })
                {
                    string name = side > 0f ? "Right" : "Left";
                    float x = side * ((w * 0.5f) - 0.01f);
                    AddBlock(shell, $"Window_{name}_{i + 1}", new Vector3(x, 3.5f, z), new Vector3(0.02f, 1.6f, 1.3f), glow);
                    float inner = side * ((w * 0.5f) - 0.06f);
                    AddBlock(shell, $"Curtain_{name}_{i + 1}_A", new Vector3(inner, 3.45f, z - 0.8f), new Vector3(0.06f, 1.9f, 0.35f), curtain);
                    AddBlock(shell, $"Curtain_{name}_{i + 1}_B", new Vector3(inner, 3.45f, z + 0.8f), new Vector3(0.06f, 1.9f, 0.35f), curtain);
                    AddBlock(shell, $"CurtainRod_{name}_{i + 1}", new Vector3(inner, 4.42f, z), new Vector3(0.03f, 0.03f, 2.2f), materials.Get(SurfaceKind.Brass));
                    if (i < 1)
                    {
                        AddBlock(shell, $"TubeLight_{name}_{i + 1}", new Vector3(side * ((w * 0.5f) - 0.05f), 3.1f, z + (d * 0.125f)), new Vector3(0.05f, 0.05f, 1.2f), tube);
                    }
                }
            }

            // Wall clock above the entrance on the back wall.
            AddBlock(shell, "Clock_Frame", new Vector3(0f, 3.6f, -(d * 0.5f) + 0.03f), new Vector3(0.6f, 0.6f, 0.05f), materials.Get(SurfaceKind.DarkWood));
            AddBlock(shell, "Clock_Face", new Vector3(0f, 3.6f, -(d * 0.5f) + 0.06f), new Vector3(0.5f, 0.5f, 0.01f), materials.Get(SurfaceKind.WhiteCloth));
            AddBlock(shell, "Clock_HandHour", new Vector3(0f, 3.66f, -(d * 0.5f) + 0.07f), new Vector3(0.025f, 0.13f, 0.005f), materials.Get(SurfaceKind.Metal));
            AddBlock(shell, "Clock_HandMinute", new Vector3(0.07f, 3.6f, -(d * 0.5f) + 0.07f), new Vector3(0.16f, 0.02f, 0.005f), materials.Get(SurfaceKind.Metal));
        }

        /// <summary>
        /// Panelled wall behind the judge with a scales-of-justice motif and a "SESSIONS COURT" nameplate.
        /// The State Emblem and national flag are left out on purpose: their use is restricted by law.
        /// </summary>
        private static void BuildBenchBackdrop(Transform shell, float depth, IMaterialProvider materials)
        {
            float z = (depth * 0.5f) - 0.04f;
            Material wood = materials.Get(SurfaceKind.DarkWood);
            Material brass = materials.Get(SurfaceKind.Brass);
            AddBlock(shell, "Backdrop_Panel", new Vector3(0f, 2.5f, z), new Vector3(6.4f, 3.2f, 0.06f), wood);
            AddBlock(shell, "Backdrop_Cornice", new Vector3(0f, 4.15f, z - 0.03f), new Vector3(6.8f, 0.12f, 0.12f), materials.Get(SurfaceKind.LightWood));

            // Scales of justice: a post, a beam and two hanging pans.
            float sz = z - 0.05f;
            AddBlock(shell, "Scales_Post", new Vector3(0f, 2.95f, sz), new Vector3(0.05f, 0.9f, 0.03f), brass);
            AddBlock(shell, "Scales_Beam", new Vector3(0f, 3.38f, sz), new Vector3(0.9f, 0.04f, 0.03f), brass);
            AddBlock(shell, "Scales_Base", new Vector3(0f, 2.5f, sz), new Vector3(0.35f, 0.05f, 0.03f), brass);
            foreach (float side in new[] { -0.42f, 0.42f })
            {
                AddBlock(shell, $"Scales_Chain_{side}", new Vector3(side, 3.18f, sz), new Vector3(0.015f, 0.38f, 0.015f), brass);
                AddBlock(shell, $"Scales_Pan_{side}", new Vector3(side, 2.98f, sz), new Vector3(0.28f, 0.03f, 0.03f), brass);
            }

            // Nameplate board with 3D lettering.
            AddBlock(shell, "Nameplate_Board", new Vector3(0f, 3.8f, sz), new Vector3(3.2f, 0.42f, 0.03f), brass);
            var label = new GameObject("Nameplate_Text");
            label.transform.SetParent(shell, false);
            label.transform.localPosition = new Vector3(0f, 3.8f, sz - 0.03f); // readable from the room (camera looks +Z)
            var text = label.AddComponent<TextMesh>();
            text.text = "SESSIONS COURT";
            text.font = BuiltinAssets.DefaultFont;
            text.fontSize = 64;
            text.characterSize = 0.045f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(0.16f, 0.1f, 0.06f);
            label.GetComponent<MeshRenderer>().sharedMaterial = BuiltinAssets.DefaultFont.material;
        }

        private static void AddWainscot(Transform parent, string side, Vector3 center, Vector3 span, float height, Material panel, Material cap)
        {
            AddBlock(parent, $"Wainscot_{side}", center + new Vector3(0f, height * 0.5f, 0f), new Vector3(span.x, height, span.z), panel);
            Vector3 capSize = new Vector3(Mathf.Max(span.x, 0.06f), 0.05f, Mathf.Max(span.z, 0.06f));
            AddBlock(parent, $"Wainscot_{side}_Cap", center + new Vector3(0f, height + 0.025f, 0f), capSize, cap);
        }
    }
}
