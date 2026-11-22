using System.Collections.Generic;
using CourtSim.Core.Courtroom;
using CourtSim.Runtime.Common;
using CourtSim.Runtime.Courtroom;
using UnityEngine;

namespace CourtSim.Runtime.Characters
{
    /// <summary>
    /// The people in an Indian sessions-court hearing: the Additional Public Prosecutor and the defence advocate at
    /// their tables, the accused in the dock (kathghara) with a police constable beside it, the bench clerk below
    /// the dais, a witness (shown during the evidence ruling) and members of the public in the gallery.
    /// </summary>
    /// <remarks>Positions come from the courtroom layout, so the cast follows any change to it. Characters have no
    /// colliders, so they never block the player, and they are kept out of the walkways.</remarks>
    public sealed class CourtroomCast : MonoBehaviour
    {
        // Saved with the scene, so a cast baked by the scene generator still knows who is who.
        [SerializeField] private List<CharacterAnimator> _all = new List<CharacterAnimator>();
        [SerializeField] private List<CharacterAnimator> _spectators = new List<CharacterAnimator>();
        [SerializeField] private CharacterAnimator _prosecutor;
        [SerializeField] private CharacterAnimator _defence;
        [SerializeField] private CharacterAnimator _defenceMale;
        [SerializeField] private CharacterAnimator _accused;
        [SerializeField] private CharacterAnimator _witness;
        [SerializeField] private CharacterAnimator _reader;
        [SerializeField] private CharacterAnimator _constable;

        public CharacterAnimator Prosecutor { get => _prosecutor; private set => _prosecutor = value; }

        /// <summary>The defence advocate currently in court (a woman or a man, chosen per case).</summary>
        public CharacterAnimator Defence => _defenceMale != null && _defenceMale.gameObject.activeSelf ? _defenceMale : _defence;

        public CharacterAnimator Accused { get => _accused; private set => _accused = value; }

        public CharacterAnimator Witness { get => _witness; private set => _witness = value; }

        public CharacterAnimator Reader { get => _reader; private set => _reader = value; }

        public CharacterAnimator Constable { get => _constable; private set => _constable = value; }

        public IReadOnlyList<CharacterAnimator> Spectators => _spectators;

        /// <summary>Everyone currently in the room (the witness only while shown).</summary>
        public IReadOnlyList<CharacterAnimator> AllCharacters => _all;

        /// <summary>
        /// Seats the female or the male defence advocate, so the person on screen matches the voice the case uses.
        /// </summary>
        public void UseFemaleDefence(bool female)
        {
            if (_defence == null || _defenceMale == null)
            {
                return;
            }

            _defence.gameObject.SetActive(female);
            _defenceMale.gameObject.SetActive(!female);
            _all.Remove(_defence);
            _all.Remove(_defenceMale);
            _all.Add(female ? _defence : _defenceMale);
        }

        /// <summary>Shows or hides the witness in the witness box.</summary>
        public void ShowWitness(bool show)
        {
            if (Witness == null)
            {
                return;
            }

            Witness.gameObject.SetActive(show);
            if (show && !_all.Contains(Witness))
            {
                _all.Add(Witness);
            }
            else if (!show)
            {
                _all.Remove(Witness);
            }
        }

        public static CourtroomCast Build(Transform parent, IMaterialProvider materials)
        {
            Transform root = UnityObjectUtil.CreateChild("Cast", parent);
            CourtroomCast cast = root.gameObject.AddComponent<CourtroomCast>();
            CourtroomLayout layout = CourtroomLayout.CreateDefault();

            // Advocates sit on the aisle-side chair behind their tables (chairs are 0.45 m behind the table's back edge).
            FixtureSpec prosecution = layout.Find(FixtureKind.ProsecutionTable);
            FixtureSpec defence = layout.Find(FixtureKind.DefenseTable);
            cast.Prosecutor = cast.Add(root, materials, "Prosecutor", "Additional Public Prosecutor",
                new CharacterLook { Outfit = Outfit.Advocate, Skin = SurfaceKind.SkinMedium, Hair = HairStyle.Short },
                BehindTable(prosecution, -0.7f), 0f, true, 0.45f);
            cast._defence = cast.Add(root, materials, "Defence", "Defence Advocate",
                new CharacterLook { Outfit = Outfit.Advocate, Skin = SurfaceKind.SkinLight, Hair = HairStyle.Long, BodyWidth = 0.9f },
                BehindTable(defence, 0.7f), 0f, true, 0.45f);
            cast._defenceMale = cast.Add(root, materials, "DefenceMale", "Defence Advocate",
                new CharacterLook { Outfit = Outfit.Advocate, Skin = SurfaceKind.SkinDark, Hair = HairStyle.Short, BodyWidth = 1.05f },
                BehindTable(defence, 0.7f), 0f, true, 0.45f);
            cast.UseFemaleDefence(true);

            // The accused stands in the dock on its plinth, facing the centre aisle; a constable stands guard beside it.
            FixtureSpec dock = layout.Find(FixtureKind.AccusedDock);
            cast.Accused = cast.Add(root, materials, "Accused", "Accused",
                new CharacterLook { Outfit = Outfit.Accused, Skin = SurfaceKind.SkinDark, Top = SurfaceKind.ClothOlive, Bottom = SurfaceKind.ClothBlue },
                new Vector3(dock.Center.X, 0.2f, dock.Center.Z), dock.FacingYawDegrees, false, 0f);
            cast.Constable = cast.Add(root, materials, "Constable", "Police Constable",
                new CharacterLook { Outfit = Outfit.Police, Skin = SurfaceKind.SkinMedium, BodyWidth = 1.1f },
                new Vector3(dock.Center.X + (dock.Size.X * 0.5f) + 0.45f, 0f, dock.Center.Z), dock.FacingYawDegrees, false, 0f);

            FixtureSpec witnessBox = layout.Find(FixtureKind.WitnessStand);
            cast.Witness = cast.Add(root, materials, "Witness", "Investigating Officer",
                new CharacterLook { Outfit = Outfit.Witness, Skin = SurfaceKind.SkinMedium, Top = SurfaceKind.Khaki, Bottom = SurfaceKind.Khaki },
                new Vector3(witnessBox.Center.X, 0.2f, witnessBox.Center.Z), witnessBox.FacingYawDegrees, false, 0f);
            cast.ShowWitness(false);

            // The bench clerk sits on the dais side of the desk, facing the room, on a simple chair.
            FixtureSpec desk = layout.Find(FixtureKind.ClerkDesk);
            var readerSeat = new Vector3(desk.Center.X + 1.1f, 0f, desk.Center.Z + (desk.Size.Z * 0.5f) + 0.45f);
            CourtroomBuilder.AddBlock(root, "ClerkChair_Seat", readerSeat + new Vector3(0f, 0.43f, 0f), new Vector3(0.45f, 0.05f, 0.45f), materials.Get(SurfaceKind.DarkWood));
            CourtroomBuilder.AddBlock(root, "ClerkChair_Base", readerSeat + new Vector3(0f, 0.2f, 0f), new Vector3(0.36f, 0.4f, 0.36f), materials.Get(SurfaceKind.Leather));
            cast.Reader = cast.Add(root, materials, "Reader", "Bench Clerk",
                new CharacterLook { Outfit = Outfit.Reader, Skin = SurfaceKind.SkinMedium, Bottom = SurfaceKind.BlackCoat },
                readerSeat, 180f, true, 0.45f);

            // Members of the public on the gallery benches, clear of the centre aisle.
            AddSpectator(cast, root, materials, -2.4f, -5f, SurfaceKind.SkinMedium, SurfaceKind.ClothCream, false);
            AddSpectator(cast, root, materials, -4.6f, -5f, SurfaceKind.SkinDark, SurfaceKind.ClothBlue, false);
            AddSpectator(cast, root, materials, 2.6f, -5f, SurfaceKind.SkinLight, SurfaceKind.ClothMaroon, true);
            AddSpectator(cast, root, materials, 4.9f, -5f, SurfaceKind.SkinMedium, SurfaceKind.ClothOlive, false);
            AddSpectator(cast, root, materials, -3.5f, -6.6f, SurfaceKind.SkinLight, SurfaceKind.ClothBlue, true);
            AddSpectator(cast, root, materials, 3.3f, -6.6f, SurfaceKind.SkinDark, SurfaceKind.ClothCream, false);
            AddSpectator(cast, root, materials, 5.3f, -6.6f, SurfaceKind.SkinMedium, SurfaceKind.ClothMaroon, true);

            return cast;
        }

        private static void AddSpectator(CourtroomCast cast, Transform root, IMaterialProvider materials, float x, float z, SurfaceKind skin, SurfaceKind cloth, bool woman)
        {
            var look = new CharacterLook
            {
                Outfit = Outfit.Public,
                Skin = skin,
                Top = cloth,
                Bottom = woman ? cloth : SurfaceKind.BlackCoat,
                Hair = woman ? HairStyle.Long : HairStyle.Short,
                HasDrape = woman,
                Drape = cloth == SurfaceKind.ClothMaroon ? SurfaceKind.ClothCream : SurfaceKind.ClothMaroon,
                BodyWidth = woman ? 0.9f : 1f,
            };
            CharacterAnimator person = cast.Add(root, materials, $"Public_{cast._spectators.Count + 1}", "Member of the public", look, new Vector3(x, 0f, z + 0.05f), 0f, true, 0.5f);
            cast._spectators.Add(person);
        }

        /// <summary>The chair position behind a table, offset sideways from its centre (negative = towards -X).</summary>
        private static Vector3 BehindTable(FixtureSpec table, float sideways)
        {
            return new Vector3(table.Center.X + sideways, 0f, table.Center.Z - (table.Size.Z * 0.5f) - 0.45f);
        }

        private CharacterAnimator Add(Transform root, IMaterialProvider materials, string name, string displayName, CharacterLook look, Vector3 feet, float yaw, bool seated, float seatHeight)
        {
            CharacterAnimator person = CharacterFactory.Build(root, name, displayName, look, seated, seatHeight, materials);
            person.transform.localPosition = feet;
            person.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            _all.Add(person);
            return person;
        }
    }
}
