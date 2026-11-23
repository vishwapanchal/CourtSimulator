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

        // Along both side walls, clear of the evidence display, the dock and the room's corners.
        private static readonly float[] WindowRows = { -6.5f, -1f, 6.8f };
        private static readonly float[] TubeLightRows = { -3.75f, 3f };

        private static Mesh _cylinderMesh;
        private static Mesh _sphereMesh;

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

        /// <summary>Unity's built-in cylinder: 2 units tall along Y and 1 unit across.</summary>
        internal static Mesh CylinderMesh
        {
            get
            {
                if (_cylinderMesh == null)
                {
                    _cylinderMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                }

                return _cylinderMesh;
            }
        }

        /// <summary>Unity's built-in sphere, 1 unit across.</summary>
        internal static Mesh SphereMesh
        {
            get
            {
                if (_sphereMesh == null)
                {
                    _sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
                }

                return _sphereMesh;
            }
        }

        /// <summary>Like <see cref="AddBlock"/>, but with any built-in mesh and turned by <paramref name="euler"/> degrees.</summary>
        internal static Transform AddMesh(Transform parent, string name, Mesh mesh, Vector3 localCenter, Vector3 scale, Vector3 euler, Material material)
        {
            Transform transform = AddBlock(parent, name, localCenter, scale, material);
            transform.GetComponent<MeshFilter>().sharedMesh = mesh;
            transform.localRotation = Quaternion.Euler(euler);
            return transform;
        }

        /// <summary>A flat round disc facing along Z (a wheel, a clock face or a fan grille on a wall).</summary>
        internal static Transform AddDisc(Transform parent, string name, Vector3 localCenter, float diameter, float thickness, Material material)
        {
            // The cylinder's axis is Y; tipping it 90 degrees about X points it along Z.
            return AddMesh(parent, name, CylinderMesh, localCenter, new Vector3(diameter, thickness * 0.5f, diameter), new Vector3(90f, 0f, 0f), material);
        }

        /// <summary>
        /// Adds a line of painted lettering, readable by someone looking along the parent's +Z after turning by
        /// <paramref name="yawDegrees"/>. See <see cref="PaintedSign"/> for why the letters are drawn at play time.
        /// </summary>
        internal static PaintedSign AddSign(
            Transform parent,
            string name,
            Vector3 localCenter,
            float yawDegrees,
            string text,
            string fallbackText,
            string fontFace,
            float letterHeight,
            float maxWidth,
            Color ink,
            bool bold = false)
        {
            Transform sign = UnityObjectUtil.CreateChild(name, parent);
            sign.localPosition = localCenter;
            sign.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            var painted = sign.gameObject.AddComponent<PaintedSign>();
            painted.Configure(text, fallbackText, fontFace, letterHeight, maxWidth, ink, bold);
            return painted;
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

            // Dark teak dado with a lighter cap rail on every wall.
            const float PanelHeight = 1.3f;
            const float PanelDepth = 0.04f;
            Material wainscot = materials.Get(SurfaceKind.Wainscot);
            Material cap = materials.Get(SurfaceKind.LightWood);
            AddWainscot(shell, "Front", new Vector3(0f, 0f, (d * 0.5f) - (PanelDepth * 0.5f)), new Vector3(w, 0f, PanelDepth), PanelHeight, wainscot, cap);
            AddWainscot(shell, "Back", new Vector3(0f, 0f, -(d * 0.5f) + (PanelDepth * 0.5f)), new Vector3(w, 0f, PanelDepth), PanelHeight, wainscot, cap);
            AddWainscot(shell, "Right", new Vector3((w * 0.5f) - (PanelDepth * 0.5f), 0f, 0f), new Vector3(PanelDepth, 0f, d), PanelHeight, wainscot, cap);
            AddWainscot(shell, "Left", new Vector3(-(w * 0.5f) + (PanelDepth * 0.5f), 0f, 0f), new Vector3(PanelDepth, 0f, d), PanelHeight, wainscot, cap);

            BuildBenchBackdrop(shell, d, materials);

            // Tall arched windows with green wooden shutters along the side walls, and fluorescent tube lights
            // between them, as in most older Karnataka court halls.
            Material tube = materials.Get(SurfaceKind.TubeLight);
            foreach (float side in new[] { -1f, 1f })
            {
                string name = side > 0f ? "Right" : "Left";
                for (int i = 0; i < WindowRows.Length; i++)
                {
                    CourtroomDressing.ArchedWindow(shell, $"Window_{name}_{i + 1}", new Vector3(side * w * 0.5f, 0f, WindowRows[i]), side, materials);
                }

                for (int i = 0; i < TubeLightRows.Length; i++)
                {
                    AddBlock(shell, $"TubeLight_{name}_{i + 1}", new Vector3(side * ((w * 0.5f) - 0.05f), 3.5f, TubeLightRows[i]), new Vector3(0.05f, 0.05f, 1.2f), tube);
                }
            }

            CourtroomDressing.Build(shell, room, materials);

            // Wall clock above the entrance on the back wall.
            AddBlock(shell, "Clock_Frame", new Vector3(0f, 3.6f, -(d * 0.5f) + 0.03f), new Vector3(0.6f, 0.6f, 0.05f), materials.Get(SurfaceKind.DarkWood));
            AddBlock(shell, "Clock_Face", new Vector3(0f, 3.6f, -(d * 0.5f) + 0.06f), new Vector3(0.5f, 0.5f, 0.01f), materials.Get(SurfaceKind.WhiteCloth));
            AddBlock(shell, "Clock_HandHour", new Vector3(0f, 3.66f, -(d * 0.5f) + 0.07f), new Vector3(0.025f, 0.13f, 0.005f), materials.Get(SurfaceKind.Metal));
            AddBlock(shell, "Clock_HandMinute", new Vector3(0.07f, 3.6f, -(d * 0.5f) + 0.07f), new Vector3(0.16f, 0.02f, 0.005f), materials.Get(SurfaceKind.Metal));
        }

        /// <summary>
        /// Panelled wall behind the judge, as in a Karnataka district court: the court's name board in Kannada and
        /// English along the top, and the State Emblem (the Ashoka Lion Capital) with "Satyameva Jayate" beneath it
        /// above the judge's chair. Shown as the court itself would display them, for a realistic training setting.
        /// </summary>
        private static void BuildBenchBackdrop(Transform shell, float depth, IMaterialProvider materials)
        {
            float z = (depth * 0.5f) - 0.04f;
            float face = z - 0.03f; // the room-facing surface of the panel
            AddBlock(shell, "Backdrop_Panel", new Vector3(0f, 2.6f, z), new Vector3(6.4f, 3.6f, 0.06f), materials.Get(SurfaceKind.DarkWood));
            AddBlock(shell, "Backdrop_Cornice", new Vector3(0f, 4.47f, face), new Vector3(6.8f, 0.12f, 0.12f), materials.Get(SurfaceKind.LightWood));

            // Cream name board in a brass frame. The Kannada line comes first, as on Karnataka court boards.
            AddBlock(shell, "NameBoard_Frame", new Vector3(0f, 4.02f, face - 0.015f), new Vector3(5.9f, 0.66f, 0.03f), materials.Get(SurfaceKind.Brass));
            AddBlock(shell, "NameBoard", new Vector3(0f, 4.02f, face - 0.035f), new Vector3(5.76f, 0.56f, 0.01f), materials.Get(SurfaceKind.PaintedBoard));
            AddSign(shell, "NameBoard_Kannada", new Vector3(0f, 4.14f, face - 0.045f), 0f,
                "ಪ್ರಧಾನ ಜಿಲ್ಲಾ ಮತ್ತು ಸತ್ರ ನ್ಯಾಯಾಲಯ", string.Empty, "Nirmala UI", 0.3f, 5.5f, CourtroomDressing.SignRed, true);
            AddSign(shell, "NameBoard_English", new Vector3(0f, 3.86f, face - 0.045f), 0f,
                "PRINCIPAL DISTRICT AND SESSIONS COURT, KESARNAGAR", "PRINCIPAL DISTRICT AND SESSIONS COURT, KESARNAGAR",
                "Arial", 0.17f, 5.5f, CourtroomDressing.SignInk, true);

            // High enough that the top of the judge's chair doesn't hide the motto from the entrance.
            CourtroomDressing.StateEmblem(shell, new Vector3(0f, 3.14f, face), materials);
            AddSign(shell, "Emblem_Motto", new Vector3(0f, 2.83f, face - 0.01f), 0f,
                "सत्यमेव जयते", "SATYAMEVA JAYATE", "Nirmala UI", 0.17f, 0.9f, CourtroomDressing.SignGold, true);
        }

        private static void AddWainscot(Transform parent, string side, Vector3 center, Vector3 span, float height, Material panel, Material cap)
        {
            AddBlock(parent, $"Wainscot_{side}", center + new Vector3(0f, height * 0.5f, 0f), new Vector3(span.x, height, span.z), panel);
            Vector3 capSize = new Vector3(Mathf.Max(span.x, 0.06f), 0.05f, Mathf.Max(span.z, 0.06f));
            AddBlock(parent, $"Wainscot_{side}_Cap", center + new Vector3(0f, height + 0.025f, 0f), capSize, cap);
        }
    }
}
