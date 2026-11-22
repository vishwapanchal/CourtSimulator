using CourtSim.Runtime.Common;
using CourtSim.Runtime.Courtroom;
using UnityEngine;

namespace CourtSim.Runtime.Characters
{
    /// <summary>
    /// Builds a stylised low-poly person from scaled blocks and a sphere head, matching the courtroom's style.
    /// The figure stands about 1.7 m tall with its feet at the root and faces local +Z.
    /// </summary>
    /// <remarks>
    /// Joints are empty "pivot" objects (hips, waist, neck, shoulders, hips and knees) so
    /// <see cref="CharacterAnimator"/> can bend them: sit, stand, breathe, gesture and turn the head.
    /// </remarks>
    public static class CharacterFactory
    {
        public const float StandingHipHeight = 0.86f;
        private const float ThighLength = 0.44f;
        private const float ShinLength = 0.42f;
        private const float TorsoLength = 0.56f;

        public static CharacterAnimator Build(Transform parent, string name, string displayName, CharacterLook look, bool seated, float seatHeight, IMaterialProvider m)
        {
            Transform root = UnityObjectUtil.CreateChild(name, parent);
            float w = look.BodyWidth;
            Material skin = m.Get(look.Skin);
            Material top = m.Get(TopKind(look));
            Material bottom = m.Get(BottomKind(look));
            Material shoes = m.Get(SurfaceKind.Hair);

            // Hips: everything above the legs hangs from here.
            Transform hips = UnityObjectUtil.CreateChild("Hips", root);
            hips.localPosition = new Vector3(0f, StandingHipHeight, 0f);
            CourtroomBuilder.AddBlock(hips, "Pelvis", new Vector3(0f, 0.04f, 0f), new Vector3(0.34f * w, 0.14f, 0.2f), bottom);

            // Legs: a thigh pivot at the hip joint and a shin pivot at the knee.
            Transform thighL = Leg(hips, "L", -0.09f * w, bottom, shoes, out Transform shinL);
            Transform thighR = Leg(hips, "R", 0.09f * w, bottom, shoes, out Transform shinR);

            // Torso pivots at the waist so the upper body can lean and breathe.
            Transform torso = UnityObjectUtil.CreateChild("Torso", hips);
            torso.localPosition = new Vector3(0f, 0.08f, 0f);
            CourtroomBuilder.AddBlock(torso, "Chest", new Vector3(0f, TorsoLength * 0.5f, 0f), new Vector3(0.4f * w, TorsoLength, 0.22f), top);
            DressTorso(torso, look, w, m);

            CourtroomBuilder.AddBlock(torso, "Neck", new Vector3(0f, TorsoLength + 0.04f, 0f), new Vector3(0.09f, 0.08f, 0.09f), skin);
            Transform head = UnityObjectUtil.CreateChild("Head", torso);
            head.localPosition = new Vector3(0f, TorsoLength + 0.08f, 0f);
            BuildHead(head, look, skin, m);

            Transform armL = Arm(torso, "L", -(0.22f * w), look, top, skin, m);
            Transform armR = Arm(torso, "R", 0.22f * w, look, top, skin, m);

            CharacterAnimator animator = root.gameObject.AddComponent<CharacterAnimator>();
            animator.Configure(displayName, hips, torso, head, armL, armR, thighL, thighR, shinL, shinR, seated, seatHeight);
            return animator;
        }

        private static Transform Leg(Transform hips, string side, float x, Material trousers, Material shoes, out Transform shin)
        {
            Transform thigh = UnityObjectUtil.CreateChild("Thigh_" + side, hips);
            thigh.localPosition = new Vector3(x, 0f, 0f);
            CourtroomBuilder.AddBlock(thigh, "Upper", new Vector3(0f, -ThighLength * 0.5f, 0f), new Vector3(0.15f, ThighLength, 0.16f), trousers);

            shin = UnityObjectUtil.CreateChild("Shin_" + side, thigh);
            shin.localPosition = new Vector3(0f, -ThighLength, 0f);
            CourtroomBuilder.AddBlock(shin, "Lower", new Vector3(0f, -ShinLength * 0.5f, 0f), new Vector3(0.13f, ShinLength, 0.14f), trousers);
            CourtroomBuilder.AddBlock(shin, "Shoe", new Vector3(0f, -ShinLength + 0.03f, 0.05f), new Vector3(0.12f, 0.06f, 0.26f), shoes);
            return thigh;
        }

        private static Transform Arm(Transform torso, string side, float x, CharacterLook look, Material sleeve, Material skin, IMaterialProvider m)
        {
            Transform shoulder = UnityObjectUtil.CreateChild("Arm_" + side, torso);
            shoulder.localPosition = new Vector3(x, TorsoLength - 0.05f, 0f);
            CourtroomBuilder.AddBlock(shoulder, "Sleeve", new Vector3(0f, -0.27f, 0f), new Vector3(0.11f, 0.54f, 0.12f), sleeve);
            CourtroomBuilder.AddBlock(shoulder, "Hand", new Vector3(0f, -0.59f, 0f), new Vector3(0.09f, 0.1f, 0.09f), skin);
            if (look.Outfit == Outfit.Advocate)
            {
                // White shirt cuff showing below the black coat sleeve.
                CourtroomBuilder.AddBlock(shoulder, "Cuff", new Vector3(0f, -0.53f, 0f), new Vector3(0.115f, 0.03f, 0.125f), m.Get(SurfaceKind.WhiteShirt));
            }

            return shoulder;
        }

        private static void BuildHead(Transform head, CharacterLook look, Material skin, IMaterialProvider m)
        {
            Material hair = m.Get(SurfaceKind.Hair);
            var skull = new GameObject("Skull", typeof(MeshFilter), typeof(MeshRenderer));
            skull.transform.SetParent(head, false);
            skull.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            skull.transform.localScale = new Vector3(0.2f, 0.24f, 0.21f);
            skull.GetComponent<MeshFilter>().sharedMesh = SphereMesh;
            skull.GetComponent<MeshRenderer>().sharedMaterial = skin;

            // Simple face hints so it is clear which way a person is looking.
            CourtroomBuilder.AddBlock(head, "Eye_L", new Vector3(-0.04f, 0.14f, 0.1f), new Vector3(0.025f, 0.02f, 0.01f), hair);
            CourtroomBuilder.AddBlock(head, "Eye_R", new Vector3(0.04f, 0.14f, 0.1f), new Vector3(0.025f, 0.02f, 0.01f), hair);
            CourtroomBuilder.AddBlock(head, "Nose", new Vector3(0f, 0.105f, 0.105f), new Vector3(0.025f, 0.04f, 0.025f), skin);

            if (look.Hair != HairStyle.None)
            {
                // Slightly larger than the head so the hair shows from every side, with a fringe above the eyes.
                CourtroomBuilder.AddBlock(head, "Hair", new Vector3(0f, 0.23f, -0.01f), new Vector3(0.23f, 0.1f, 0.24f), hair);
                CourtroomBuilder.AddBlock(head, "Hair_Fringe", new Vector3(0f, 0.205f, 0.1f), new Vector3(0.2f, 0.05f, 0.05f), hair);
                CourtroomBuilder.AddBlock(head, "Hair_Back", new Vector3(0f, 0.14f, -0.095f), new Vector3(0.205f, 0.15f, 0.05f), hair);
            }

            if (look.Hair == HairStyle.Long)
            {
                CourtroomBuilder.AddBlock(head, "Bun", new Vector3(0f, 0.1f, -0.13f), new Vector3(0.1f, 0.1f, 0.08f), hair);
            }

            if (look.Outfit == Outfit.Police)
            {
                Material khaki = m.Get(SurfaceKind.Khaki);
                CourtroomBuilder.AddBlock(head, "Cap", new Vector3(0f, 0.25f, 0f), new Vector3(0.23f, 0.07f, 0.23f), khaki);
                CourtroomBuilder.AddBlock(head, "Cap_Brim", new Vector3(0f, 0.22f, 0.12f), new Vector3(0.2f, 0.015f, 0.08f), hair);
            }
        }

        private static void DressTorso(Transform torso, CharacterLook look, float w, IMaterialProvider m)
        {
            switch (look.Outfit)
            {
                case Outfit.Advocate:
                    // White shirt front between the coat lapels, and the two white bands at the collar.
                    Material white = m.Get(SurfaceKind.WhiteShirt);
                    CourtroomBuilder.AddBlock(torso, "ShirtFront", new Vector3(0f, TorsoLength - 0.12f, 0.112f), new Vector3(0.12f, 0.22f, 0.01f), white);
                    CourtroomBuilder.AddBlock(torso, "Band_L", new Vector3(-0.025f, TorsoLength - 0.1f, 0.12f), new Vector3(0.035f, 0.11f, 0.01f), white);
                    CourtroomBuilder.AddBlock(torso, "Band_R", new Vector3(0.025f, TorsoLength - 0.1f, 0.12f), new Vector3(0.035f, 0.11f, 0.01f), white);
                    break;
                case Outfit.Police:
                    CourtroomBuilder.AddBlock(torso, "Belt", new Vector3(0f, 0.04f, 0f), new Vector3((0.41f * w) + 0.01f, 0.05f, 0.23f), m.Get(SurfaceKind.DarkWood));
                    CourtroomBuilder.AddBlock(torso, "Badge", new Vector3(-0.09f, TorsoLength - 0.15f, 0.112f), new Vector3(0.05f, 0.05f, 0.01f), m.Get(SurfaceKind.Brass));
                    break;
                default:
                    if (look.HasDrape)
                    {
                        // A saree pallu or dupatta draped from the left shoulder across the chest.
                        Transform drape = UnityObjectUtil.CreateChild("Drape", torso);
                        drape.localPosition = new Vector3(0f, TorsoLength * 0.5f, 0.115f);
                        drape.localRotation = Quaternion.Euler(0f, 0f, 35f);
                        CourtroomBuilder.AddBlock(drape, "Cloth", Vector3.zero, new Vector3(0.14f, TorsoLength * 1.15f, 0.015f), m.Get(look.Drape));
                    }

                    break;
            }
        }

        private static SurfaceKind TopKind(CharacterLook look)
        {
            switch (look.Outfit)
            {
                case Outfit.Advocate:
                    return SurfaceKind.BlackCoat;
                case Outfit.Police:
                    return SurfaceKind.Khaki;
                case Outfit.Reader:
                    return SurfaceKind.WhiteShirt;
                default:
                    return look.Top;
            }
        }

        private static SurfaceKind BottomKind(CharacterLook look)
        {
            switch (look.Outfit)
            {
                case Outfit.Advocate:
                    return SurfaceKind.BlackCoat;
                case Outfit.Police:
                    return SurfaceKind.Khaki;
                default:
                    // A saree covers the legs in the drape colour.
                    return look.HasDrape && look.Hair == HairStyle.Long ? look.Drape : look.Bottom;
            }
        }

        private static Mesh _sphere;

        private static Mesh SphereMesh
        {
            get
            {
                if (_sphere == null)
                {
                    _sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
                }

                return _sphere;
            }
        }
    }
}
