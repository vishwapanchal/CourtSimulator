using CourtSim.Core.Courtroom;
using CourtSim.Runtime.Common;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>
    /// Placeholder visuals for each fixture kind, built from scaled cubes.
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
                    Enclosure(visual, size, materials, panelHeight: size.y * 0.8f, openBack: false);
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
            CourtroomBuilder.AddBlock(v, "Trim", new Vector3(0f, top - 0.25f, front + 0.01f), new Vector3(s.x - 0.4f, 0.05f, 0.02f), m.Get(SurfaceKind.Brass));

            float seat = bottom + DaisHeight + 0.45f;
            CourtroomBuilder.AddBlock(v, "Chair_Seat", new Vector3(0f, seat, -0.3f), new Vector3(0.7f, 0.1f, 0.65f), m.Get(SurfaceKind.Leather));
            CourtroomBuilder.AddBlock(v, "Chair_Back", new Vector3(0f, seat + 0.55f, -0.62f), new Vector3(0.75f, 1.1f, 0.1f), m.Get(SurfaceKind.Leather));
        }

        private static void ClerkDesk(Transform v, Vector3 s, IMaterialProvider m)
        {
            CourtroomBuilder.AddBlock(v, "Body", Vector3.zero, s, m.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(v, "Top", new Vector3(0f, (s.y * 0.5f) + 0.02f, 0f), new Vector3(s.x + 0.06f, 0.04f, s.z + 0.06f), m.Get(SurfaceKind.LightWood));
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
            CourtroomBuilder.AddBlock(v, "Nameplate", new Vector3(0f, top + 0.04f, lz - 0.1f), new Vector3(0.5f, 0.08f, 0.02f), m.Get(SurfaceKind.Brass));
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
