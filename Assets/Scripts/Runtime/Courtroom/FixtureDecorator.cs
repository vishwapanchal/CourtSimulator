using CourtSim.Core.Courtroom;
using CourtSim.Runtime.Common;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>
    /// Visuals for each fixture kind, built from scaled cubes and styled on an Indian district/sessions courtroom:
    /// green baize on the tables, a high-backed chair with a white towel for the judge, stacks of cloth-bound case
    /// files, a railed witness box and a barred accused box (kathghara).
    /// </summary>
    /// <remarks>
    /// Every routine works in the fixture's local frame: the box spans ±size/2 around the origin and the
    /// fixture's front faces local +Z. Swap these routines for imported models without touching layout,
    /// colliders or interaction, which are owned by <see cref="CourtroomBuilder"/>.
    /// </remarks>
    internal static class FixtureDecorator
    {
        public static void Decorate(FixtureKind kind, Transform visual, Vector3 size, IMaterialProvider materials)
        {
            switch (kind)
            {
                case FixtureKind.JudgeBench:
                    JudgeBench(visual, size, materials);
                    break;
                case FixtureKind.ClerkDesk:
                    ClerkDesk(visual, size, materials);
                    break;
                case FixtureKind.WitnessStand:
                    Enclosure(visual, size, materials, panelHeight: size.y, openBack: true);
                    break;
                case FixtureKind.AccusedDock:
                    Enclosure(visual, size, materials, panelHeight: size.y * 0.55f, openBack: false);
                    Balusters(visual, size, materials, fromHeight: size.y * 0.55f);
                    break;
                case FixtureKind.ProsecutionTable:
                case FixtureKind.DefenseTable:
                    CounselTable(visual, size, materials);
                    break;
                case FixtureKind.EvidenceDisplay:
                    EvidenceDisplay(visual, size, materials);
                    break;
                case FixtureKind.AiAssistantTerminal:
                    AssistantTerminal(visual, size, materials);
                    break;
                case FixtureKind.Bar:
                    Bar(visual, size, materials);
                    break;
                case FixtureKind.GalleryBench:
                    GalleryBench(visual, size, materials);
                    break;
                default:
                    CourtroomBuilder.AddBlock(visual, "Block", Vector3.zero, size, materials.Get(SurfaceKind.DarkWood));
                    break;
            }
        }

        private static void JudgeBench(Transform v, Vector3 s, IMaterialProvider m)
        {
            float bottom = -s.y * 0.5f;
            float top = s.y * 0.5f;
            float front = s.z * 0.5f;
            const float DaisHeight = 0.4f;

            CourtroomBuilder.AddBlock(v, "Dais", new Vector3(0f, bottom + (DaisHeight * 0.5f), 0f), new Vector3(s.x, DaisHeight, s.z), m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(v, "FrontPanel", new Vector3(0f, 0f, front - 0.15f), new Vector3(s.x, s.y, 0.3f), m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(v, "Desktop", new Vector3(0f, top - 0.03f, front - 0.4f), new Vector3(s.x + 0.1f, 0.06f, 0.8f), m.Get(SurfaceKind.LightWood));
            CourtroomBuilder.AddBlock(v, "Baize", new Vector3(0f, top + 0.005f, front - 0.4f), new Vector3(s.x - 0.2f, 0.01f, 0.7f), m.Get(SurfaceKind.GreenBaize));
            CourtroomBuilder.AddBlock(v, "Trim", new Vector3(0f, top - 0.25f, front + 0.01f), new Vector3(s.x - 0.4f, 0.05f, 0.02f), m.Get(SurfaceKind.Brass));
            CourtroomBuilder.AddBlock(v, "Step", new Vector3(0f, bottom + 0.1f, front + 0.25f), new Vector3(1.4f, 0.2f, 0.5f), m.Get(SurfaceKind.DarkWood));

            // Indian courts seat the judge in a tall high-backed chair with a white towel over the backrest.
            float seat = bottom + DaisHeight + 0.45f;
            CourtroomBuilder.AddBlock(v, "Chair_Seat", new Vector3(0f, seat, -0.3f), new Vector3(0.75f, 0.12f, 0.7f), m.Get(SurfaceKind.Leather));
            CourtroomBuilder.AddBlock(v, "Chair_Back", new Vector3(0f, seat + 0.85f, -0.64f), new Vector3(0.85f, 1.7f, 0.12f), m.Get(SurfaceKind.Leather));
            CourtroomBuilder.AddBlock(v, "Chair_Crown", new Vector3(0f, seat + 1.73f, -0.64f), new Vector3(0.95f, 0.1f, 0.16f), m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(v, "Chair_Towel", new Vector3(0f, seat + 1.35f, -0.57f), new Vector3(0.6f, 0.7f, 0.02f), m.Get(SurfaceKind.WhiteCloth));
            CourtroomBuilder.AddBlock(v, "Armrest_L", new Vector3(-0.42f, seat + 0.25f, -0.3f), new Vector3(0.08f, 0.08f, 0.6f), m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(v, "Armrest_R", new Vector3(0.42f, seat + 0.25f, -0.3f), new Vector3(0.08f, 0.08f, 0.6f), m.Get(SurfaceKind.DarkWood));

            FileStack(v, m, new Vector3(-1.6f, top + 0.01f, front - 0.45f), 5);
            FileStack(v, m, new Vector3(1.5f, top + 0.01f, front - 0.5f), 3);

            // A steel water jug and tumbler at the judge's right hand and a brass calling bell at the left, kept low
            // and near the chair so they never hide the advocates from the judge's seat.
            float desk = top + 0.01f;
            Material steel = m.Get(SurfaceKind.SteelGrey);
            Material brass = m.Get(SurfaceKind.Brass);
            Mesh cylinder = CourtroomBuilder.CylinderMesh;
            CourtroomBuilder.AddMesh(v, "Jug", cylinder, new Vector3(-1f, desk + 0.11f, 0.7f), new Vector3(0.12f, 0.11f, 0.12f), Vector3.zero, steel);
            CourtroomBuilder.AddMesh(v, "Jug_Neck", cylinder, new Vector3(-1f, desk + 0.24f, 0.7f), new Vector3(0.09f, 0.02f, 0.09f), Vector3.zero, steel);
            CourtroomBuilder.AddBlock(v, "Jug_Handle", new Vector3(-1.07f, desk + 0.13f, 0.7f), new Vector3(0.02f, 0.12f, 0.025f), steel);
            CourtroomBuilder.AddMesh(v, "Tumbler", cylinder, new Vector3(-0.82f, desk + 0.05f, 0.8f), new Vector3(0.065f, 0.05f, 0.065f), Vector3.zero, steel);
            CourtroomBuilder.AddMesh(v, "Bell_Base", cylinder, new Vector3(0.95f, desk + 0.01f, 0.8f), new Vector3(0.11f, 0.01f, 0.11f), Vector3.zero, m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddMesh(v, "Bell_Dome", CourtroomBuilder.SphereMesh, new Vector3(0.95f, desk + 0.03f, 0.8f), new Vector3(0.09f, 0.07f, 0.09f), Vector3.zero, brass);
            CourtroomBuilder.AddMesh(v, "Bell_Knob", CourtroomBuilder.SphereMesh, new Vector3(0.95f, desk + 0.075f, 0.8f), new Vector3(0.02f, 0.02f, 0.02f), Vector3.zero, brass);

            // Nameplate at the front edge of the desk, lettered towards the room.
            CourtroomBuilder.AddBlock(v, "Nameplate_Block", new Vector3(0f, desk + 0.045f, front - 0.08f), new Vector3(0.75f, 0.09f, 0.05f), m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(v, "Nameplate_Plate", new Vector3(0f, desk + 0.045f, front - 0.053f), new Vector3(0.71f, 0.07f, 0.004f), brass);
            CourtroomBuilder.AddSign(v, "Nameplate_Text", new Vector3(0f, desk + 0.045f, front - 0.049f), 180f,
                "Hon'ble Sessions Judge", "Hon'ble Sessions Judge", "Times New Roman", 0.055f, 0.66f, CourtroomDressing.SignInk, true);
        }

        // The reader / stenographer sits below the dais with the day's cause list and case files.
        private static void ClerkDesk(Transform v, Vector3 s, IMaterialProvider m)
        {
            float top = s.y * 0.5f;
            CourtroomBuilder.AddBlock(v, "Body", Vector3.zero, s, m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(v, "Top", new Vector3(0f, top + 0.02f, 0f), new Vector3(s.x + 0.06f, 0.04f, s.z + 0.06f), m.Get(SurfaceKind.LightWood));
            CourtroomBuilder.AddBlock(v, "Baize", new Vector3(0f, top + 0.045f, 0f), new Vector3(s.x - 0.1f, 0.01f, s.z - 0.1f), m.Get(SurfaceKind.GreenBaize));
            CourtroomBuilder.AddBlock(v, "Monitor", new Vector3(0.7f, top + 0.27f, -0.15f), new Vector3(0.55f, 0.36f, 0.04f), m.Get(SurfaceKind.Screen));
            CourtroomBuilder.AddBlock(v, "Monitor_Stand", new Vector3(0.7f, top + 0.08f, -0.15f), new Vector3(0.08f, 0.1f, 0.08f), m.Get(SurfaceKind.Metal));
            // The bench clerk sits behind local x = -1.1, so the bundles stand beside them rather than in front.
            CaseBundle(v, m, new Vector3(-0.45f, top + 0.05f, 0.05f), new Vector3(0.42f, 0.15f, 0.3f));
            CaseBundle(v, m, new Vector3(-0.42f, top + 0.2f, 0.03f), new Vector3(0.38f, 0.12f, 0.27f));
            FileStack(v, m, new Vector3(0.1f, top + 0.05f, 0.1f), 4);
        }

        /// <summary>
        /// A case bundle: files wrapped in red cloth and tied with white tape, crossed on the front and over the top,
        /// as Indian court records are kept. <paramref name="bottom"/> is the centre of its base.
        /// </summary>
        internal static void CaseBundle(Transform v, IMaterialProvider m, Vector3 bottom, Vector3 size)
        {
            const float Tape = 0.022f;
            const float Wrap = 0.006f; // the tape sits just proud of the cloth
            Vector3 centre = bottom + new Vector3(0f, size.y * 0.5f, 0f);
            Material tape = m.Get(SurfaceKind.WhiteCloth);
            CourtroomBuilder.AddBlock(v, "Bundle", centre, size, m.Get(SurfaceKind.RedCloth));
            CourtroomBuilder.AddBlock(v, "Bundle_TapeUpright", centre, new Vector3(Tape, size.y + Wrap, size.z + Wrap), tape);
            CourtroomBuilder.AddBlock(v, "Bundle_TapeAround", centre, new Vector3(size.x + Wrap, Tape, size.z + Wrap), tape);
        }

        /// <summary>A pile of cloth-bound case files, each slightly offset so the stack looks hand-placed.</summary>
        internal static void FileStack(Transform v, IMaterialProvider m, Vector3 bottom, int count)
        {
            const float Thickness = 0.045f;
            for (int i = 0; i < count; i++)
            {
                float jitterX = (((i * 37) % 5) - 2) * 0.012f;
                float jitterZ = (((i * 53) % 5) - 2) * 0.01f;
                Material cover = m.Get(i % 3 == 2 ? SurfaceKind.LightWood : SurfaceKind.FileCover);
                CourtroomBuilder.AddBlock(v, $"File_{i}", bottom + new Vector3(jitterX, (i + 0.5f) * Thickness, jitterZ), new Vector3(0.34f, Thickness * 0.9f, 0.25f), cover);
            }
        }

        /// <summary>Vertical bars above the panels of the accused box, as in an Indian kathghara.</summary>
        private static void Balusters(Transform v, Vector3 s, IMaterialProvider m, float fromHeight)
        {
            const float Bar = 0.04f;
            const float Spacing = 0.18f;
            float bottom = -s.y * 0.5f;
            float barHeight = s.y - fromHeight;
            float y = bottom + fromHeight + (barHeight * 0.5f);
            Material wood = m.Get(SurfaceKind.LightWood);

            int frontBars = Mathf.Max(2, Mathf.RoundToInt(s.x / Spacing));
            for (int i = 0; i <= frontBars; i++)
            {
                float x = (-s.x * 0.5f) + (i * s.x / frontBars);
                CourtroomBuilder.AddBlock(v, $"Bar_F{i}", new Vector3(x, y, (s.z * 0.5f) - Bar), new Vector3(Bar, barHeight, Bar), wood);
            }

            int sideBars = Mathf.Max(2, Mathf.RoundToInt(s.z / Spacing));
            for (int i = 1; i < sideBars; i++)
            {
                float z = (-s.z * 0.5f) + (i * s.z / sideBars);
                CourtroomBuilder.AddBlock(v, $"Bar_L{i}", new Vector3((-s.x * 0.5f) + Bar, y, z), new Vector3(Bar, barHeight, Bar), wood);
                CourtroomBuilder.AddBlock(v, $"Bar_R{i}", new Vector3((s.x * 0.5f) - Bar, y, z), new Vector3(Bar, barHeight, Bar), wood);
            }

            CourtroomBuilder.AddBlock(v, "TopRail", new Vector3(0f, bottom + s.y, (s.z * 0.5f) - Bar), new Vector3(s.x + 0.04f, 0.05f, 0.08f), m.Get(SurfaceKind.DarkWood));
        }

        /// <summary>A simple wooden chair whose front faces local +Z.</summary>
        private static void Chair(Transform v, IMaterialProvider m, string name, Vector3 floorCenter)
        {
            Material wood = m.Get(SurfaceKind.DarkWood);
            CourtroomBuilder.AddBlock(v, name + "_Seat", floorCenter + new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.05f, 0.45f), wood);
            CourtroomBuilder.AddBlock(v, name + "_Back", floorCenter + new Vector3(0f, 0.75f, -0.2f), new Vector3(0.45f, 0.55f, 0.04f), wood);
            CourtroomBuilder.AddBlock(v, name + "_Base", floorCenter + new Vector3(0f, 0.21f, 0f), new Vector3(0.38f, 0.42f, 0.38f), m.Get(SurfaceKind.Leather));
        }

        private static void Enclosure(Transform v, Vector3 s, IMaterialProvider m, float panelHeight, bool openBack)
        {
            const float Plinth = 0.2f;
            const float Panel = 0.08f;
            float bottom = -s.y * 0.5f;
            float panelCenter = bottom + Plinth + ((panelHeight - Plinth) * 0.5f);
            float panelSpan = panelHeight - Plinth;
            Material wood = m.Get(SurfaceKind.DarkWood);

            CourtroomBuilder.AddBlock(v, "Plinth", new Vector3(0f, bottom + (Plinth * 0.5f), 0f), new Vector3(s.x, Plinth, s.z), wood);
            CourtroomBuilder.AddBlock(v, "Panel_Front", new Vector3(0f, panelCenter, (s.z - Panel) * 0.5f), new Vector3(s.x, panelSpan, Panel), wood);
            CourtroomBuilder.AddBlock(v, "Panel_Left", new Vector3(-(s.x - Panel) * 0.5f, panelCenter, 0f), new Vector3(Panel, panelSpan, s.z), wood);
            CourtroomBuilder.AddBlock(v, "Panel_Right", new Vector3((s.x - Panel) * 0.5f, panelCenter, 0f), new Vector3(Panel, panelSpan, s.z), wood);
            if (!openBack)
            {
                CourtroomBuilder.AddBlock(v, "Panel_Back", new Vector3(0f, panelCenter, -(s.z - Panel) * 0.5f), new Vector3(s.x, panelSpan, Panel), wood);
            }

            float railY = bottom + panelHeight + 0.02f;
            CourtroomBuilder.AddBlock(v, "Rail", new Vector3(0f, railY, (s.z - Panel) * 0.5f), new Vector3(s.x + 0.04f, 0.04f, Panel + 0.06f), m.Get(SurfaceKind.LightWood));
        }

        private static void CounselTable(Transform v, Vector3 s, IMaterialProvider m)
        {
            const float TopThickness = 0.06f;
            const float Leg = 0.08f;
            float top = s.y * 0.5f;
            Material dark = m.Get(SurfaceKind.DarkWood);

            CourtroomBuilder.AddBlock(v, "Top", new Vector3(0f, top - (TopThickness * 0.5f), 0f), new Vector3(s.x, TopThickness, s.z), m.Get(SurfaceKind.LightWood));
            float legHeight = s.y - TopThickness;
            float legY = (-s.y * 0.5f) + (legHeight * 0.5f);
            float lx = (s.x * 0.5f) - 0.1f;
            float lz = (s.z * 0.5f) - 0.1f;
            CourtroomBuilder.AddBlock(v, "Leg_FL", new Vector3(-lx, legY, lz), new Vector3(Leg, legHeight, Leg), dark);
            CourtroomBuilder.AddBlock(v, "Leg_FR", new Vector3(lx, legY, lz), new Vector3(Leg, legHeight, Leg), dark);
            CourtroomBuilder.AddBlock(v, "Leg_BL", new Vector3(-lx, legY, -lz), new Vector3(Leg, legHeight, Leg), dark);
            CourtroomBuilder.AddBlock(v, "Leg_BR", new Vector3(lx, legY, -lz), new Vector3(Leg, legHeight, Leg), dark);
            CourtroomBuilder.AddBlock(v, "ModestyPanel", new Vector3(0f, top - 0.3f, lz), new Vector3(s.x - 0.3f, 0.4f, 0.03f), dark);
            CourtroomBuilder.AddBlock(v, "Baize", new Vector3(0f, top + 0.005f, 0f), new Vector3(s.x - 0.1f, 0.01f, s.z - 0.1f), m.Get(SurfaceKind.GreenBaize));
            CourtroomBuilder.AddBlock(v, "Nameplate", new Vector3(0f, top + 0.04f, lz - 0.1f), new Vector3(0.5f, 0.08f, 0.02f), m.Get(SurfaceKind.Brass));
            FileStack(v, m, new Vector3(-0.8f, top + 0.01f, -0.1f), 4);
            FileStack(v, m, new Vector3(0.9f, top + 0.01f, 0f), 2);

            // Advocates sit behind their table (local -Z side), facing the bench.
            float floor = -s.y * 0.5f;
            Chair(v, m, "Chair_A", new Vector3(-0.7f, floor, -(s.z * 0.5f) - 0.45f));
            Chair(v, m, "Chair_B", new Vector3(0.7f, floor, -(s.z * 0.5f) - 0.45f));
        }

        private static void EvidenceDisplay(Transform v, Vector3 s, IMaterialProvider m)
        {
            CourtroomBuilder.AddBlock(v, "Frame", Vector3.zero, s, m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(v, "Screen", new Vector3(0f, 0f, (s.z * 0.5f) + 0.005f), new Vector3(s.x - 0.16f, s.y - 0.16f, 0.01f), m.Get(SurfaceKind.Screen));
            CourtroomBuilder.AddBlock(v, "Label", new Vector3(0f, (-s.y * 0.5f) - 0.12f, s.z * 0.5f), new Vector3(0.9f, 0.1f, 0.02f), m.Get(SurfaceKind.Brass));
        }

        private static void AssistantTerminal(Transform v, Vector3 s, IMaterialProvider m)
        {
            float bottom = -s.y * 0.5f;
            CourtroomBuilder.AddBlock(v, "Base", new Vector3(0f, bottom + 0.03f, 0f), new Vector3(0.7f, 0.06f, 0.7f), m.Get(SurfaceKind.Brass));
            CourtroomBuilder.AddBlock(v, "Pedestal", new Vector3(0f, bottom + 0.5f, 0f), new Vector3(0.32f, 0.9f, 0.32f), m.Get(SurfaceKind.DarkWood));

            // Console head tilted back toward the user standing in front of it.
            Transform head = UnityObjectUtil.CreateChild("Head", v);
            head.localPosition = new Vector3(0f, bottom + 1.02f, 0f);
            head.localRotation = Quaternion.Euler(-30f, 0f, 0f);
            CourtroomBuilder.AddBlock(head, "Housing", Vector3.zero, new Vector3(Mathf.Min(s.x, 0.75f), 0.5f, 0.06f), m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(head, "Screen", new Vector3(0f, 0f, 0.035f), new Vector3(Mathf.Min(s.x, 0.75f) - 0.07f, 0.43f, 0.01f), m.Get(SurfaceKind.AssistantScreen));
        }

        private static void Bar(Transform v, Vector3 s, IMaterialProvider m)
        {
            const float PostSpacing = 0.5f;
            const float Post = 0.07f;
            Material dark = m.Get(SurfaceKind.DarkWood);

            CourtroomBuilder.AddBlock(v, "TopRail", new Vector3(0f, (s.y * 0.5f) - 0.03f, 0f), new Vector3(s.x, 0.06f, s.z), m.Get(SurfaceKind.LightWood));
            CourtroomBuilder.AddBlock(v, "BottomRail", new Vector3(0f, (-s.y * 0.5f) + 0.1f, 0f), new Vector3(s.x, 0.05f, s.z * 0.6f), dark);

            int posts = Mathf.Max(2, Mathf.RoundToInt(s.x / PostSpacing) + 1);
            float step = (s.x - Post) / (posts - 1);
            float postHeight = s.y - 0.06f;
            for (int i = 0; i < posts; i++)
            {
                float x = (-(s.x - Post) * 0.5f) + (i * step);
                CourtroomBuilder.AddBlock(v, $"Post_{i}", new Vector3(x, (-s.y * 0.5f) + (postHeight * 0.5f), 0f), new Vector3(Post, postHeight, Post), dark);
            }
        }

        private static void GalleryBench(Transform v, Vector3 s, IMaterialProvider m)
        {
            Material dark = m.Get(SurfaceKind.DarkWood);
            float top = s.y * 0.5f;

            CourtroomBuilder.AddBlock(v, "Seat", new Vector3(0f, top - 0.03f, 0.05f), new Vector3(s.x, 0.06f, s.z - 0.1f), m.Get(SurfaceKind.LightWood));
            CourtroomBuilder.AddBlock(v, "End_Left", new Vector3(-(s.x * 0.5f) + 0.03f, -0.03f, 0f), new Vector3(0.06f, s.y - 0.06f, s.z), dark);
            CourtroomBuilder.AddBlock(v, "End_Right", new Vector3((s.x * 0.5f) - 0.03f, -0.03f, 0f), new Vector3(0.06f, s.y - 0.06f, s.z), dark);
            CourtroomBuilder.AddBlock(v, "Backrest", new Vector3(0f, top + 0.3f, (-s.z * 0.5f) + 0.03f), new Vector3(s.x, 0.5f, 0.05f), dark);
        }
    }
}
