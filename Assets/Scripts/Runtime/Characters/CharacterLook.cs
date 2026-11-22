using CourtSim.Runtime.Courtroom;

namespace CourtSim.Runtime.Characters
{
    /// <summary>What a person in the courtroom is wearing, which decides the colours of their blocks.</summary>
    public enum Outfit
    {
        /// <summary>Black coat over a white shirt, with the white bands advocates wear at the collar.</summary>
        Advocate,

        /// <summary>Khaki shirt and trousers, a brown belt and a peaked cap.</summary>
        Police,

        /// <summary>A plain cloth shirt (colour from <see cref="CharacterLook.Top"/>).</summary>
        Accused,

        Witness,

        /// <summary>The bench clerk who reads out the matter: a formal shirt.</summary>
        Reader,

        /// <summary>Members of the public in the gallery: kurtas, shirts and sarees.</summary>
        Public,
    }

    /// <summary>How much hair a character has. Long hair adds a bun at the back of the head.</summary>
    public enum HairStyle
    {
        None,
        Short,
        Long,
    }

    /// <summary>
    /// A plain description of one character's appearance. <see cref="CharacterFactory"/> reads it to choose
    /// materials and proportions; it holds no Unity objects, so it is cheap to create and easy to test.
    /// </summary>
    public sealed class CharacterLook
    {
        public Outfit Outfit { get; set; } = Outfit.Public;

        /// <summary>One of <see cref="SurfaceKind.SkinLight"/>, <see cref="SurfaceKind.SkinMedium"/> or <see cref="SurfaceKind.SkinDark"/>.</summary>
        public SurfaceKind Skin { get; set; } = SurfaceKind.SkinMedium;

        public HairStyle Hair { get; set; } = HairStyle.Short;

        /// <summary>Multiplies the width of the shoulders and hips (1 = average build).</summary>
        public float BodyWidth { get; set; } = 1f;

        /// <summary>Shirt or kurta colour. Ignored for advocates and police, who wear uniforms.</summary>
        public SurfaceKind Top { get; set; } = SurfaceKind.ClothCream;

        /// <summary>Trouser colour. Ignored for advocates and police, and when a saree is worn.</summary>
        public SurfaceKind Bottom { get; set; } = SurfaceKind.BlackCoat;

        /// <summary>True for a saree or a dupatta: a cloth draped over one shoulder and, for a saree, to the ankles.</summary>
        public bool HasDrape { get; set; }

        /// <summary>Colour of the saree or dupatta when <see cref="HasDrape"/> is true.</summary>
        public SurfaceKind Drape { get; set; } = SurfaceKind.ClothMaroon;
    }
}
