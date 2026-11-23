using CourtSim.Runtime.Common;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>
    /// The things that make the hall read as a Karnataka district and sessions court rather than a generic one: the
    /// State Emblem and national flag by the bench, steel almirahs full of red-tied case bundles, portraits of Gandhi
    /// and Ambedkar, the cause-list board and "Silence" sign by the entrance, green-shuttered arched windows and a
    /// desert cooler.
    /// </summary>
    /// <remarks>
    /// Wall pieces are built in a small local frame: the wall surface is at z = 0, the room is towards -Z and +X runs
    /// along the wall to the right of someone facing it. Each piece is then turned to face into the room. Everything is
    /// static and gets folded into the static batch, so the many small blocks cost only a few draw calls.
    /// </remarks>
    internal static class CourtroomDressing
    {
        public static readonly Color SignInk = new Color(0.07f, 0.06f, 0.05f);
        public static readonly Color SignRed = new Color(0.55f, 0.05f, 0.04f);
        public static readonly Color SignGold = new Color(0.95f, 0.78f, 0.38f);

        private const float FacingFrontWall = 0f;    // pieces on the wall behind the judge
        private const float FacingBackWall = 180f;   // pieces on the entrance wall
        private const float FacingLeftWall = -90f;   // pieces on the -X wall
        private const float FacingRightWall = 90f;   // pieces on the +X wall

        public static void Build(Transform shell, Vector3 room, IMaterialProvider m)
        {
            float front = room.z * 0.5f;
            float back = -room.z * 0.5f;

            // Beside the dais, clear of the judge's chair, the bench and the witness box.
            NationalFlag(shell, new Vector3(-2.95f, 0f, front - 0.45f), m);
            Almirah(shell, "Almirah_Right_Open", new Vector3(4.25f, 0f, front - 0.27f), true, m);
            Almirah(shell, "Almirah_Right", new Vector3(5.3f, 0f, front - 0.27f), false, m);
            Almirah(shell, "Almirah_Left_Open", new Vector3(-5.2f, 0f, front - 0.27f), true, m);

            // The entrance wall faces the judge's seat, so most of what the judge sees across the room is here.
            EntranceDoor(shell, back, m);
            Portrait(shell, "Portrait_Gandhi", new Vector3(2.7f, 3.05f, back), true, m);
            Portrait(shell, "Portrait_Ambedkar", new Vector3(-2.7f, 3.05f, back), false, m);
            CauseListBoard(shell, new Vector3(-4.8f, 1.75f, back), m);
            SilenceSign(shell, new Vector3(4.8f, 2.55f, back), m);

            // A desert cooler against the left wall between the bar and the public benches.
            AirCooler(shell, new Vector3(-room.x * 0.5f, 0f, -4.1f), m);
        }

        /// <summary>
        /// A tall window with a round arch, green frame and glazing bars, and two green wooden shutters folded back
        /// against the wall. <paramref name="side"/> is +1 for the +X wall and -1 for the -X wall.
        /// </summary>
        public static void ArchedWindow(Transform shell, string name, Vector3 wallPoint, float side, IMaterialProvider m)
        {
            Transform w = Pivot(shell, name, wallPoint, side > 0f ? FacingRightWall : FacingLeftWall);
            Material green = m.Get(SurfaceKind.ShutterGreen);
            Material glow = m.Get(SurfaceKind.WindowGlow);
            const float SpringLine = 4.1f; // where the straight sides meet the arch

            CourtroomBuilder.AddBlock(w, "Frame", new Vector3(0f, 2.85f, -0.015f), new Vector3(1.5f, 2.5f, 0.03f), green);
            CourtroomBuilder.AddDisc(w, "Frame_Arch", new Vector3(0f, SpringLine, -0.015f), 1.5f, 0.03f, green);
            CourtroomBuilder.AddBlock(w, "Glass", new Vector3(0f, 2.9f, -0.035f), new Vector3(1.3f, 2.4f, 0.01f), glow);
            CourtroomBuilder.AddDisc(w, "Glass_Arch", new Vector3(0f, SpringLine, -0.035f), 1.3f, 0.01f, glow);

            CourtroomBuilder.AddBlock(w, "Bar_Upright", new Vector3(0f, 3.2f, -0.045f), new Vector3(0.04f, 3.0f, 0.02f), green);
            foreach (float y in new[] { 2.5f, 3.3f, SpringLine })
            {
                CourtroomBuilder.AddBlock(w, $"Bar_{y}", new Vector3(0f, y, -0.045f), new Vector3(1.3f, 0.04f, 0.02f), green);
            }

            // Two radiating bars give the arch a fanlight.
            foreach (float s in new[] { -1f, 1f })
            {
                CourtroomBuilder.AddMesh(w, $"Bar_Fan_{s}", BuiltinAssets.CubeMesh, new Vector3(s * 0.23f, SpringLine + 0.23f, -0.045f),
                    new Vector3(0.04f, 0.65f, 0.02f), new Vector3(0f, 0f, -s * 45f), green);
            }

            CourtroomBuilder.AddBlock(w, "Sill", new Vector3(0f, 1.57f, -0.08f), new Vector3(1.7f, 0.06f, 0.16f), m.Get(SurfaceKind.LightWood));

            // Shutters folded flat against the wall either side, each with two darker recessed panels.
            Material panel = m.Get(SurfaceKind.GreenBaize);
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * 1.08f;
                CourtroomBuilder.AddBlock(w, $"Shutter_{s}", new Vector3(x, 2.9f, -0.03f), new Vector3(0.64f, 2.4f, 0.04f), green);
                CourtroomBuilder.AddBlock(w, $"Shutter_{s}_PanelTop", new Vector3(x, 3.5f, -0.051f), new Vector3(0.48f, 0.95f, 0.004f), panel);
                CourtroomBuilder.AddBlock(w, $"Shutter_{s}_PanelLow", new Vector3(x, 2.3f, -0.051f), new Vector3(0.48f, 0.95f, 0.004f), panel);
            }
        }

        /// <summary>
        /// The State Emblem: the Lion Capital of Ashoka as a gilded wall relief. Three of the four lions show (one facing
        /// out, two in profile) on a round abacus carrying the 24-spoke Dharma Chakra, with a bull and a horse either
        /// side. <paramref name="abacusTop"/> is the top centre of the abacus on the wall surface.
        /// </summary>
        public static void StateEmblem(Transform shell, Vector3 abacusTop, IMaterialProvider m)
        {
            Transform e = Pivot(shell, "StateEmblem", abacusTop, FacingFrontWall);
            Material gold = m.Get(SurfaceKind.Brass);
            Material shadow = m.Get(SurfaceKind.DarkWood);

            CourtroomBuilder.AddBlock(e, "Abacus", new Vector3(0f, -0.1f, -0.04f), new Vector3(0.62f, 0.17f, 0.08f), gold);
            CourtroomBuilder.AddBlock(e, "Abacus_Lip", new Vector3(0f, -0.005f, -0.045f), new Vector3(0.66f, 0.03f, 0.09f), gold);
            CourtroomBuilder.AddBlock(e, "Abacus_Foot", new Vector3(0f, -0.2f, -0.045f), new Vector3(0.66f, 0.03f, 0.09f), gold);
            Chakra(e, "Abacus_Chakra", new Vector3(0f, -0.1f, -0.082f), 0.13f, gold, shadow, gold);
            foreach (float s in new[] { -1f, 1f })
            {
                // Bull (left) and horse (right), as small dark reliefs on the abacus.
                CourtroomBuilder.AddBlock(e, $"Abacus_Animal_{s}", new Vector3(s * 0.19f, -0.115f, -0.082f), new Vector3(0.11f, 0.04f, 0.004f), shadow);
                CourtroomBuilder.AddBlock(e, $"Abacus_AnimalHead_{s}", new Vector3(s * 0.255f, -0.09f, -0.082f), new Vector3(0.03f, 0.04f, 0.004f), shadow);
            }

            // The lion facing the room.
            CourtroomBuilder.AddBlock(e, "Lion_Front_Body", new Vector3(0f, 0.16f, -0.06f), new Vector3(0.2f, 0.3f, 0.12f), gold);
            CourtroomBuilder.AddBlock(e, "Lion_Front_Mane", new Vector3(0f, 0.36f, -0.07f), new Vector3(0.27f, 0.22f, 0.12f), gold);
            CourtroomBuilder.AddBlock(e, "Lion_Front_Face", new Vector3(0f, 0.35f, -0.135f), new Vector3(0.12f, 0.12f, 0.03f), gold);
            foreach (float s in new[] { -1f, 1f })
            {
                CourtroomBuilder.AddBlock(e, $"Lion_Front_Ear_{s}", new Vector3(s * 0.07f, 0.485f, -0.07f), new Vector3(0.05f, 0.05f, 0.05f), gold);
                CourtroomBuilder.AddBlock(e, $"Lion_Front_Leg_{s}", new Vector3(s * 0.055f, 0.06f, -0.12f), new Vector3(0.05f, 0.12f, 0.05f), gold);

                // A seated lion in profile, looking outwards.
                CourtroomBuilder.AddBlock(e, $"Lion_Side_{s}_Haunch", new Vector3(s * 0.2f, 0.11f, -0.05f), new Vector3(0.2f, 0.2f, 0.1f), gold);
                CourtroomBuilder.AddBlock(e, $"Lion_Side_{s}_Chest", new Vector3(s * 0.27f, 0.2f, -0.05f), new Vector3(0.11f, 0.28f, 0.1f), gold);
                CourtroomBuilder.AddBlock(e, $"Lion_Side_{s}_Mane", new Vector3(s * 0.3f, 0.36f, -0.05f), new Vector3(0.17f, 0.2f, 0.1f), gold);
                CourtroomBuilder.AddBlock(e, $"Lion_Side_{s}_Muzzle", new Vector3(s * 0.4f, 0.34f, -0.05f), new Vector3(0.08f, 0.08f, 0.08f), gold);
                CourtroomBuilder.AddBlock(e, $"Lion_Side_{s}_Leg", new Vector3(s * 0.33f, 0.07f, -0.05f), new Vector3(0.04f, 0.14f, 0.04f), gold);
            }
        }

        /// <summary>A free-standing national flag on a brass pole, flying away from the bench (towards -X).</summary>
        private static void NationalFlag(Transform shell, Vector3 floorPoint, IMaterialProvider m)
        {
            Transform f = Pivot(shell, "NationalFlag", floorPoint, FacingFrontWall);
            Material brass = m.Get(SurfaceKind.Brass);
            Mesh cylinder = CourtroomBuilder.CylinderMesh;
            CourtroomBuilder.AddMesh(f, "Stand_Base", cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(0.42f, 0.03f, 0.42f), Vector3.zero, m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddMesh(f, "Stand_Collar", cylinder, new Vector3(0f, 0.16f, 0f), new Vector3(0.12f, 0.1f, 0.12f), Vector3.zero, brass);
            CourtroomBuilder.AddMesh(f, "Pole", cylinder, new Vector3(0f, 1.3f, 0f), new Vector3(0.04f, 1.3f, 0.04f), Vector3.zero, brass);
            CourtroomBuilder.AddMesh(f, "Finial", CourtroomBuilder.SphereMesh, new Vector3(0f, 2.65f, 0f), new Vector3(0.09f, 0.09f, 0.09f), Vector3.zero, brass);

            // Saffron, white and green bands in the 3:2 proportion, with the navy Ashoka Chakra on the white.
            const float Width = 0.9f;
            const float Band = 0.2f;
            const float Top = 2.5f;
            float x = -0.02f - (Width * 0.5f);
            CourtroomBuilder.AddBlock(f, "Flag_Saffron", new Vector3(x, Top - (Band * 0.5f), 0f), new Vector3(Width, Band, 0.012f), m.Get(SurfaceKind.FlagSaffron));
            CourtroomBuilder.AddBlock(f, "Flag_White", new Vector3(x, Top - (Band * 1.5f), 0f), new Vector3(Width, Band, 0.012f), m.Get(SurfaceKind.WhiteCloth));
            CourtroomBuilder.AddBlock(f, "Flag_Green", new Vector3(x, Top - (Band * 2.5f), 0f), new Vector3(Width, Band, 0.012f), m.Get(SurfaceKind.FlagGreen));
            Material navy = m.Get(SurfaceKind.ChakraNavy);
            Chakra(f, "Flag_Chakra", new Vector3(x, Top - (Band * 1.5f), -0.008f), 0.18f, navy, m.Get(SurfaceKind.WhiteCloth), navy);
        }

        /// <summary>
        /// A wheel with 24 spokes facing -Z: a rim disc, an inner disc of <paramref name="fill"/>, a hub and twelve bars
        /// through the centre (each bar makes two spokes).
        /// </summary>
        private static void Chakra(Transform parent, string name, Vector3 center, float diameter, Material rim, Material fill, Material spokes)
        {
            float inner = diameter * 0.84f;
            CourtroomBuilder.AddDisc(parent, name + "_Rim", center, diameter, 0.004f, rim);
            CourtroomBuilder.AddDisc(parent, name + "_Fill", center + new Vector3(0f, 0f, -0.003f), inner, 0.004f, fill);
            CourtroomBuilder.AddDisc(parent, name + "_Hub", center + new Vector3(0f, 0f, -0.006f), diameter * 0.18f, 0.004f, spokes);
            for (int i = 0; i < 12; i++)
            {
                CourtroomBuilder.AddMesh(parent, $"{name}_Spoke_{i}", BuiltinAssets.CubeMesh, center + new Vector3(0f, 0f, -0.006f),
                    new Vector3(diameter * 0.035f, inner, 0.003f), new Vector3(0f, 0f, i * 15f), spokes);
            }
        }

        /// <summary>
        /// A grey steel almirah (the Godrej-style cupboard found in every Indian court) standing with its back to the
        /// wall behind the judge. An open one shows shelves of red-tied case bundles.
        /// </summary>
        private static void Almirah(Transform shell, string name, Vector3 floorPoint, bool open, IMaterialProvider m)
        {
            const float W = 0.9f;
            const float H = 1.95f;
            const float D = 0.5f;
            const float Plinth = 0.04f;
            Transform a = Pivot(shell, name, floorPoint, FacingFrontWall);
            Material grey = m.Get(SurfaceKind.SteelGrey);
            Material metal = m.Get(SurfaceKind.Metal);

            // One collider for the whole cupboard, so the player can't walk into it.
            var solid = a.gameObject.AddComponent<BoxCollider>();
            solid.center = new Vector3(0f, (H * 0.5f) + Plinth, 0f);
            solid.size = new Vector3(W, H + Plinth, D);

            float mid = Plinth + (H * 0.5f);
            CourtroomBuilder.AddBlock(a, "Plinth", new Vector3(0f, Plinth * 0.5f, 0f), new Vector3(W - 0.04f, Plinth, D - 0.04f), metal);
            if (!open)
            {
                CourtroomBuilder.AddBlock(a, "Body", new Vector3(0f, mid, 0f), new Vector3(W, H, D), grey);
                CourtroomBuilder.AddBlock(a, "DoorSeam", new Vector3(0f, mid, -(D * 0.5f) - 0.002f), new Vector3(0.008f, H - 0.1f, 0.004f), metal);
                CourtroomBuilder.AddBlock(a, "Handle", new Vector3(0.06f, 1.05f, -(D * 0.5f) - 0.015f), new Vector3(0.025f, 0.16f, 0.03f), metal);
                return;
            }

            CourtroomBuilder.AddBlock(a, "Back", new Vector3(0f, mid, (D * 0.5f) - 0.01f), new Vector3(W, H, 0.02f), grey);
            CourtroomBuilder.AddBlock(a, "Side_L", new Vector3(-(W * 0.5f) + 0.01f, mid, 0f), new Vector3(0.02f, H, D), grey);
            CourtroomBuilder.AddBlock(a, "Side_R", new Vector3((W * 0.5f) - 0.01f, mid, 0f), new Vector3(0.02f, H, D), grey);
            CourtroomBuilder.AddBlock(a, "Top", new Vector3(0f, Plinth + H - 0.01f, 0f), new Vector3(W, 0.02f, D), grey);
            CourtroomBuilder.AddBlock(a, "Bottom", new Vector3(0f, Plinth + 0.01f, 0f), new Vector3(W, 0.02f, D), grey);

            // Doors swung fully open, standing out into the room from the hinges.
            foreach (float s in new[] { -1f, 1f })
            {
                CourtroomBuilder.AddBlock(a, $"Door_{s}", new Vector3(s * ((W * 0.5f) + 0.01f), mid, -(D * 0.5f) - (W * 0.25f)), new Vector3(0.02f, H - 0.02f, W * 0.5f), grey);
            }

            // Shelves loaded with bundles; one shelf holds a loose stack of files for variety.
            float[] levels = { Plinth + 0.02f, Plinth + 0.5f, Plinth + 0.97f, Plinth + 1.44f };
            for (int i = 0; i < levels.Length; i++)
            {
                if (i > 0)
                {
                    CourtroomBuilder.AddBlock(a, $"Shelf_{i}", new Vector3(0f, levels[i] - 0.01f, 0f), new Vector3(W - 0.04f, 0.02f, D - 0.04f), grey);
                }

                if (i == 2)
                {
                    FixtureDecorator.FileStack(a, m, new Vector3(-0.18f, levels[i], -0.03f), 5);
                    FixtureDecorator.CaseBundle(a, m, new Vector3(0.2f, levels[i], -0.02f), new Vector3(0.34f, 0.15f, 0.3f));
                    continue;
                }

                FixtureDecorator.CaseBundle(a, m, new Vector3(-0.2f, levels[i], -0.02f), new Vector3(0.36f, 0.16f, 0.3f));
                FixtureDecorator.CaseBundle(a, m, new Vector3(0.2f, levels[i], -0.04f), new Vector3(0.33f, 0.13f, 0.28f));
                if (i == 0)
                {
                    FixtureDecorator.CaseBundle(a, m, new Vector3(-0.19f, levels[i] + 0.16f, -0.03f), new Vector3(0.32f, 0.12f, 0.27f));
                }
            }
        }

        /// <summary>Panelled double doors in the middle of the entrance wall, under the clock.</summary>
        private static void EntranceDoor(Transform shell, float backWall, IMaterialProvider m)
        {
            Transform d = Pivot(shell, "EntranceDoor", new Vector3(0f, 0f, backWall), FacingBackWall);
            CourtroomBuilder.AddBlock(d, "Frame", new Vector3(0f, 1.4f, -0.03f), new Vector3(1.9f, 2.8f, 0.06f), m.Get(SurfaceKind.DarkWood));
            Material leaf = m.Get(SurfaceKind.LightWood);
            Material inset = m.Get(SurfaceKind.Wainscot);
            foreach (float s in new[] { -1f, 1f })
            {
                CourtroomBuilder.AddBlock(d, $"Leaf_{s}", new Vector3(s * 0.43f, 1.33f, -0.07f), new Vector3(0.82f, 2.56f, 0.03f), leaf);
                CourtroomBuilder.AddBlock(d, $"Leaf_{s}_PanelTop", new Vector3(s * 0.43f, 1.85f, -0.087f), new Vector3(0.6f, 0.9f, 0.005f), inset);
                CourtroomBuilder.AddBlock(d, $"Leaf_{s}_PanelLow", new Vector3(s * 0.43f, 0.72f, -0.087f), new Vector3(0.6f, 0.8f, 0.005f), inset);
                CourtroomBuilder.AddBlock(d, $"Leaf_{s}_Handle", new Vector3(s * 0.08f, 1.1f, -0.1f), new Vector3(0.03f, 0.2f, 0.03f), m.Get(SurfaceKind.Brass));
            }
        }

        /// <summary>
        /// A framed painted portrait with a brass name plate below. The likenesses are kept simple and respectful: a
        /// bald head, round spectacles and a white shawl for Gandhi; dark hair, spectacles and a blue suit for Ambedkar.
        /// </summary>
        private static void Portrait(Transform shell, string name, Vector3 wallPoint, bool gandhi, IMaterialProvider m)
        {
            Transform p = Pivot(shell, name, wallPoint, FacingBackWall);
            Material skin = m.Get(SurfaceKind.SkinMedium);
            Material dark = m.Get(SurfaceKind.Hair);

            CourtroomBuilder.AddBlock(p, "Frame", new Vector3(0f, 0f, -0.02f), new Vector3(0.8f, 1.0f, 0.04f), m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(p, "Gilt", new Vector3(0f, 0f, -0.042f), new Vector3(0.72f, 0.92f, 0.004f), m.Get(SurfaceKind.Brass));
            CourtroomBuilder.AddBlock(p, "Canvas", new Vector3(0f, 0f, -0.046f), new Vector3(0.64f, 0.84f, 0.004f), m.Get(gandhi ? SurfaceKind.Khaki : SurfaceKind.ClothBlue));

            // Shoulders and clothing.
            Material clothes = m.Get(gandhi ? SurfaceKind.WhiteCloth : SurfaceKind.ChakraNavy);
            CourtroomBuilder.AddBlock(p, "Body", new Vector3(0f, -0.28f, -0.051f), new Vector3(0.56f, 0.28f, 0.004f), clothes);
            CourtroomBuilder.AddBlock(p, "Shoulders", new Vector3(0f, -0.125f, -0.051f), new Vector3(gandhi ? 0.36f : 0.42f, 0.09f, 0.004f), clothes);
            if (!gandhi)
            {
                CourtroomBuilder.AddBlock(p, "Shirt", new Vector3(0f, -0.2f, -0.054f), new Vector3(0.09f, 0.2f, 0.003f), m.Get(SurfaceKind.WhiteShirt));
                CourtroomBuilder.AddBlock(p, "Tie", new Vector3(0f, -0.21f, -0.057f), new Vector3(0.03f, 0.18f, 0.002f), m.Get(SurfaceKind.ClothMaroon));
            }

            // Head, ears and round spectacles.
            CourtroomBuilder.AddBlock(p, "Neck", new Vector3(0f, -0.06f, -0.052f), new Vector3(0.08f, 0.06f, 0.004f), skin);
            CourtroomBuilder.AddMesh(p, "Head", CourtroomBuilder.SphereMesh, new Vector3(0f, 0.07f, -0.05f), new Vector3(0.21f, 0.26f, 0.012f), Vector3.zero, skin);
            foreach (float s in new[] { -1f, 1f })
            {
                CourtroomBuilder.AddBlock(p, $"Ear_{s}", new Vector3(s * (gandhi ? 0.118f : 0.108f), 0.06f, -0.05f), new Vector3(gandhi ? 0.045f : 0.03f, 0.06f, 0.004f), skin);
                CourtroomBuilder.AddDisc(p, $"Lens_{s}", new Vector3(s * 0.045f, 0.08f, -0.058f), 0.055f, 0.002f, dark);
                CourtroomBuilder.AddDisc(p, $"Lens_{s}_Glass", new Vector3(s * 0.045f, 0.08f, -0.06f), 0.04f, 0.002f, skin);
            }

            if (gandhi)
            {
                CourtroomBuilder.AddBlock(p, "Moustache", new Vector3(0f, 0.005f, -0.058f), new Vector3(0.06f, 0.015f, 0.002f), m.Get(SurfaceKind.ClothCream));
            }
            else
            {
                CourtroomBuilder.AddBlock(p, "Hair", new Vector3(0f, 0.175f, -0.059f), new Vector3(0.19f, 0.07f, 0.004f), dark);
            }

            CourtroomBuilder.AddBlock(p, "NamePlate", new Vector3(0f, -0.6f, -0.012f), new Vector3(0.62f, 0.1f, 0.02f), m.Get(SurfaceKind.Brass));
            string label = gandhi ? "Mahatma Gandhi" : "Dr. B. R. Ambedkar";
            CourtroomBuilder.AddSign(p, "NamePlate_Text", new Vector3(0f, -0.6f, -0.024f), 0f, label, label, "Times New Roman", 0.075f, 0.56f, SignInk, true);
        }

        /// <summary>The day's cause list: printed sheets pinned to a green baize board near the entrance.</summary>
        private static void CauseListBoard(Transform shell, Vector3 wallPoint, IMaterialProvider m)
        {
            Transform b = Pivot(shell, "CauseListBoard", wallPoint, FacingBackWall);
            CourtroomBuilder.AddBlock(b, "Frame", new Vector3(0f, 0f, -0.02f), new Vector3(1.3f, 1.0f, 0.04f), m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(b, "Baize", new Vector3(0f, -0.05f, -0.042f), new Vector3(1.2f, 0.82f, 0.004f), m.Get(SurfaceKind.GreenBaize));
            CourtroomBuilder.AddBlock(b, "Header", new Vector3(0f, 0.43f, -0.043f), new Vector3(1.2f, 0.11f, 0.006f), m.Get(SurfaceKind.PaintedBoard));
            CourtroomBuilder.AddSign(b, "Header_Text", new Vector3(0f, 0.43f, -0.05f), 0f, "CAUSE LIST", "CAUSE LIST", "Arial", 0.085f, 1.1f, SignRed, true);

            Material paper = m.Get(SurfaceKind.WhiteCloth);
            Material print = m.Get(SurfaceKind.Metal);
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int i = (row * 3) + col;
                    float tilt = ((i * 7) % 5 - 2) * 1.5f; // pinned up by hand, so a little crooked
                    var centre = new Vector3((col - 1) * 0.38f, row == 0 ? 0.15f : -0.26f, -0.046f);
                    Transform sheet = CourtroomBuilder.AddMesh(b, $"Sheet_{i}", BuiltinAssets.CubeMesh, centre, new Vector3(0.2f, 0.28f, 0.003f), new Vector3(0f, 0f, tilt), paper);

                    // Two grey lines of "print" on each sheet, placed in the sheet's own (scaled) frame so they tilt with it.
                    for (int line = 0; line < 2; line++)
                    {
                        CourtroomBuilder.AddBlock(sheet, $"Print_{line}", new Vector3(0f, 0.3f - (line * 0.15f), -0.8f), new Vector3(0.75f, 0.05f, 0.5f), print);
                    }
                }
            }
        }

        /// <summary>A red-bordered "Silence" sign in Kannada and English.</summary>
        private static void SilenceSign(Transform shell, Vector3 wallPoint, IMaterialProvider m)
        {
            Transform s = Pivot(shell, "SilenceSign", wallPoint, FacingBackWall);
            CourtroomBuilder.AddBlock(s, "Border", new Vector3(0f, 0f, -0.015f), new Vector3(1.2f, 0.5f, 0.03f), m.Get(SurfaceKind.RedCloth));
            CourtroomBuilder.AddBlock(s, "Board", new Vector3(0f, 0f, -0.032f), new Vector3(1.1f, 0.42f, 0.004f), m.Get(SurfaceKind.PaintedBoard));
            CourtroomBuilder.AddSign(s, "Text_Kannada", new Vector3(0f, 0.075f, -0.04f), 0f, "ಮೌನ ಕಾಪಾಡಿ", string.Empty, "Nirmala UI", 0.2f, 1f, SignRed, true);
            CourtroomBuilder.AddSign(s, "Text_English", new Vector3(0f, -0.11f, -0.04f), 0f, "SILENCE", "SILENCE", "Arial", 0.12f, 1f, SignInk, true);
        }

        /// <summary>A floor-standing desert cooler with a fan grille, louvres and wood-wool side pads.</summary>
        private static void AirCooler(Transform shell, Vector3 wallPoint, IMaterialProvider m)
        {
            Transform c = Pivot(shell, "AirCooler", wallPoint, FacingLeftWall);
            Material body = m.Get(SurfaceKind.PaintedBoard);
            Material metal = m.Get(SurfaceKind.Metal);
            CourtroomBuilder.AddBlock(c, "Wheels", new Vector3(0f, 0.05f, -0.3f), new Vector3(0.7f, 0.1f, 0.5f), metal);
            CourtroomBuilder.AddBlock(c, "Body", new Vector3(0f, 0.66f, -0.3f), new Vector3(0.75f, 1.1f, 0.55f), body, true);
            CourtroomBuilder.AddBlock(c, "Lid", new Vector3(0f, 1.24f, -0.3f), new Vector3(0.79f, 0.06f, 0.59f), body);
            CourtroomBuilder.AddDisc(c, "FanGrille", new Vector3(0f, 0.88f, -0.58f), 0.46f, 0.01f, metal);
            CourtroomBuilder.AddDisc(c, "FanHub", new Vector3(0f, 0.88f, -0.59f), 0.1f, 0.01f, m.Get(SurfaceKind.SteelGrey));
            for (int i = 0; i < 3; i++)
            {
                CourtroomBuilder.AddBlock(c, $"Louvre_{i}", new Vector3(0f, 0.3f + (i * 0.1f), -0.58f), new Vector3(0.6f, 0.025f, 0.01f), metal);
            }

            foreach (float s in new[] { -1f, 1f })
            {
                CourtroomBuilder.AddBlock(c, $"Pad_{s}", new Vector3(s * 0.38f, 0.66f, -0.3f), new Vector3(0.01f, 0.8f, 0.45f), m.Get(SurfaceKind.LightWood));
            }
        }

        /// <summary>An empty parent at <paramref name="point"/>, turned so its -Z faces into the room.</summary>
        private static Transform Pivot(Transform parent, string name, Vector3 point, float yawDegrees)
        {
            Transform pivot = UnityObjectUtil.CreateChild(name, parent);
            pivot.localPosition = point;
            pivot.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            return pivot;
        }
    }
}
