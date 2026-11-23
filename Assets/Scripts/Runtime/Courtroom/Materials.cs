using System;
using System.Collections.Generic;
using CourtSim.Runtime.Common;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>Surface finishes used by the procedural courtroom.</summary>
    public enum SurfaceKind
    {
        Floor,
        Wall,
        Wainscot,
        Ceiling,
        DarkWood,
        LightWood,
        Brass,
        Leather,
        Screen,
        AssistantScreen,
        WindowGlow,

        // Indian courtroom details.
        GreenBaize,   // green cloth on the judge, reader and advocates' tables
        WhiteCloth,   // towel draped over the judge's chair
        Curtain,      // maroon window curtains
        FileCover,    // pink-red cloth-bound case files ("basta")
        TubeLight,    // wall-mounted fluorescent tubes
        Metal,        // ceiling fans and fittings

        // People (see CourtSim.Runtime.Characters). Skin and cloth are matte so the low-poly figures read softly.
        SkinLight,    // lighter South Asian skin tone
        SkinMedium,   // mid warm brown
        SkinDark,     // deep warm brown
        Hair,         // near-black hair, eyes and shoes
        BlackCoat,    // advocates' black coat and trousers
        WhiteShirt,   // shirts and the white advocates' bands at the collar
        Khaki,        // police uniform
        ClothBlue,    // kurtas, sarees and shirts worn by the public
        ClothOlive,
        ClothMaroon,
        ClothCream,

        // Indian court furnishings (added after the people so existing values keep their numbers).
        SteelGrey,    // Godrej-style steel almirahs, the desert cooler and the steel water jug
        RedCloth,     // red cloth wrapped round case bundles
        ShutterGreen, // green-painted wooden window shutters and frames
        FlagSaffron,  // national flag: top band
        FlagGreen,    // national flag: bottom band
        ChakraNavy,   // Ashoka Chakra on the flag; also a navy suit in a portrait
        PaintedBoard, // cream-painted signboards and notice headers
    }

    /// <summary>Supplies one shared material per surface kind.</summary>
    /// <remarks>Implemented at runtime by <see cref="RuntimeMaterialProvider"/> and in the editor by an
    /// asset-backed provider, so baked scenes reference real material assets.</remarks>
    public interface IMaterialProvider
    {
        Material Get(SurfaceKind kind);
    }

    /// <summary>The single source of truth for courtroom colours and finishes.</summary>
    public static class CourtroomPalette
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Width of one square floor tile, in metres.</summary>
        private const float FloorTileMetres = 0.6f;

        /// <summary>The floor slab is the room (14 m x 20 m) plus the wall thickness on each side.</summary>
        private static readonly Vector2 FloorSlabMetres = new Vector2(14.4f, 20.4f);

        /// <summary>Applies the finish for <paramref name="kind"/>. Works with Built-in Standard and URP Lit.</summary>
        public static void Configure(Material material, SurfaceKind kind)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            GetFinish(kind, out Color albedo, out float smoothness, out float metallic, out Color emission);

            SetColorIfPresent(material, ColorId, albedo);
            SetColorIfPresent(material, BaseColorId, albedo);
            SetFloatIfPresent(material, GlossinessId, smoothness);
            SetFloatIfPresent(material, SmoothnessId, smoothness);
            SetFloatIfPresent(material, MetallicId, metallic);

            if (kind == SurfaceKind.Floor)
            {
                // The tile texture holds two tiles each way, so it repeats every two tiles.
                material.mainTextureScale = FloorSlabMetres / (2f * FloorTileMetres);
            }

            bool emissive = emission.maxColorComponent > 0f;
            if (emissive && material.HasProperty(EmissionColorId))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }
        }

        private static void GetFinish(SurfaceKind kind, out Color albedo, out float smoothness, out float metallic, out Color emission)
        {
            metallic = 0f;
            emission = Color.black;
            switch (kind)
            {
                case SurfaceKind.Floor:
                    // White, so the red-oxide and cream tile texture shows in its own colours.
                    albedo = Color.white;
                    smoothness = 0.45f;
                    break;
                case SurfaceKind.Wall:
                    // Warm ochre lime-wash, as in old public-works court buildings.
                    albedo = new Color(0.89f, 0.77f, 0.54f);
                    smoothness = 0.08f;
                    break;
                case SurfaceKind.Wainscot:
                    // Dark polished teak dado.
                    albedo = new Color(0.27f, 0.17f, 0.10f);
                    smoothness = 0.4f;
                    break;
                case SurfaceKind.Ceiling:
                    albedo = new Color(0.93f, 0.92f, 0.89f);
                    smoothness = 0.05f;
                    break;
                case SurfaceKind.DarkWood:
                    albedo = new Color(0.30f, 0.19f, 0.12f);
                    smoothness = 0.5f;
                    break;
                case SurfaceKind.LightWood:
                    albedo = new Color(0.56f, 0.39f, 0.24f);
                    smoothness = 0.45f;
                    break;
                case SurfaceKind.Brass:
                    albedo = new Color(0.79f, 0.63f, 0.29f);
                    smoothness = 0.65f;
                    metallic = 0.85f;
                    break;
                case SurfaceKind.Leather:
                    albedo = new Color(0.36f, 0.09f, 0.08f);
                    smoothness = 0.35f;
                    break;
                case SurfaceKind.Screen:
                    albedo = new Color(0.04f, 0.06f, 0.09f);
                    smoothness = 0.8f;
                    emission = new Color(0.16f, 0.28f, 0.45f);
                    break;
                case SurfaceKind.AssistantScreen:
                    albedo = new Color(0.04f, 0.07f, 0.07f);
                    smoothness = 0.8f;
                    emission = new Color(0.15f, 0.55f, 0.5f);
                    break;
                case SurfaceKind.WindowGlow:
                    albedo = new Color(0.82f, 0.88f, 0.95f);
                    smoothness = 0.9f;
                    emission = new Color(0.55f, 0.6f, 0.68f);
                    break;
                case SurfaceKind.GreenBaize:
                    albedo = new Color(0.10f, 0.32f, 0.20f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.WhiteCloth:
                    albedo = new Color(0.95f, 0.95f, 0.92f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.Curtain:
                    albedo = new Color(0.45f, 0.08f, 0.11f);
                    smoothness = 0.15f;
                    break;
                case SurfaceKind.FileCover:
                    albedo = new Color(0.80f, 0.36f, 0.40f);
                    smoothness = 0.2f;
                    break;
                case SurfaceKind.TubeLight:
                    albedo = new Color(0.95f, 0.97f, 1f);
                    smoothness = 0.6f;
                    emission = new Color(0.9f, 0.93f, 0.95f);
                    break;
                case SurfaceKind.Metal:
                    albedo = new Color(0.33f, 0.33f, 0.35f);
                    smoothness = 0.5f;
                    metallic = 0.6f;
                    break;
                case SurfaceKind.SkinLight:
                    albedo = new Color(0.80f, 0.60f, 0.46f);
                    smoothness = 0.2f;
                    break;
                case SurfaceKind.SkinMedium:
                    albedo = new Color(0.62f, 0.43f, 0.30f);
                    smoothness = 0.2f;
                    break;
                case SurfaceKind.SkinDark:
                    albedo = new Color(0.42f, 0.27f, 0.18f);
                    smoothness = 0.2f;
                    break;
                case SurfaceKind.Hair:
                    albedo = new Color(0.06f, 0.05f, 0.05f);
                    smoothness = 0.25f;
                    break;
                case SurfaceKind.BlackCoat:
                    albedo = new Color(0.08f, 0.08f, 0.09f);
                    smoothness = 0.15f;
                    break;
                case SurfaceKind.WhiteShirt:
                    albedo = new Color(0.93f, 0.93f, 0.90f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.Khaki:
                    albedo = new Color(0.62f, 0.53f, 0.34f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.ClothBlue:
                    albedo = new Color(0.20f, 0.32f, 0.55f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.ClothOlive:
                    albedo = new Color(0.40f, 0.42f, 0.22f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.ClothMaroon:
                    albedo = new Color(0.50f, 0.12f, 0.16f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.ClothCream:
                    albedo = new Color(0.88f, 0.82f, 0.68f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.SteelGrey:
                    albedo = new Color(0.55f, 0.58f, 0.60f);
                    smoothness = 0.55f;
                    metallic = 0.35f;
                    break;
                case SurfaceKind.RedCloth:
                    albedo = new Color(0.68f, 0.09f, 0.08f);
                    smoothness = 0.15f;
                    break;
                case SurfaceKind.ShutterGreen:
                    albedo = new Color(0.14f, 0.40f, 0.26f);
                    smoothness = 0.35f;
                    break;
                case SurfaceKind.FlagSaffron:
                    albedo = new Color(1f, 0.50f, 0.13f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.FlagGreen:
                    albedo = new Color(0.07f, 0.45f, 0.18f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.ChakraNavy:
                    albedo = new Color(0.03f, 0.05f, 0.40f);
                    smoothness = 0.1f;
                    break;
                case SurfaceKind.PaintedBoard:
                    albedo = new Color(0.93f, 0.90f, 0.80f);
                    smoothness = 0.2f;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown surface kind.");
            }
        }

        /// <summary>
        /// Returns a small tiling texture for surfaces that have a pattern, or null. Only the floor has one: red-oxide
        /// and cream square tiles, as laid in many older Karnataka court halls. One texture is far cheaper than
        /// hundreds of tile blocks.
        /// </summary>
        public static Texture2D CreatePattern(SurfaceKind kind)
        {
            if (kind != SurfaceKind.Floor)
            {
                return null;
            }

            const int TilePixels = 32;
            const int Size = TilePixels * 2;
            var redOxide = new Color32(128, 50, 36, 255);
            var cream = new Color32(204, 188, 158, 255);
            var grout = new Color32(70, 58, 48, 255);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool groutLine = x % TilePixels == 0 || y % TilePixels == 0;
                    bool redTile = ((x / TilePixels) + (y / TilePixels)) % 2 == 0;
                    pixels[(y * Size) + x] = groutLine ? grout : redTile ? redOxide : cream;
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "Courtroom_FloorTiles",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        private static void SetColorIfPresent(Material material, int id, Color value)
        {
            if (material.HasProperty(id))
            {
                material.SetColor(id, value);
            }
        }

        private static void SetFloatIfPresent(Material material, int id, float value)
        {
            if (material.HasProperty(id))
            {
                material.SetFloat(id, value);
            }
        }
    }

    /// <summary>Creates throwaway materials at runtime. Dispose to release them.</summary>
    public sealed class RuntimeMaterialProvider : IMaterialProvider, IDisposable
    {
        private readonly Dictionary<SurfaceKind, Material> _cache = new Dictionary<SurfaceKind, Material>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private Material _template;

        public Material Get(SurfaceKind kind)
        {
            if (_cache.TryGetValue(kind, out Material cached) && cached != null)
            {
                return cached;
            }

            Material material = CreateFromTemplate();
            material.name = $"Courtroom_{kind} (Runtime)";
            material.hideFlags = HideFlags.DontSave;
            CourtroomPalette.Configure(material, kind);
            Texture2D pattern = CourtroomPalette.CreatePattern(kind);
            if (pattern != null)
            {
                pattern.hideFlags = HideFlags.DontSave;
                material.mainTexture = pattern;
                _textures.Add(pattern);
            }

            _cache[kind] = material;
            return material;
        }

        public void Dispose()
        {
            foreach (Material material in _cache.Values)
            {
                UnityObjectUtil.SafeDestroy(material);
            }

            _cache.Clear();
            foreach (Texture2D texture in _textures)
            {
                UnityObjectUtil.SafeDestroy(texture);
            }

            _textures.Clear();
            UnityObjectUtil.SafeDestroy(_template);
            _template = null;
        }

        private Material CreateFromTemplate()
        {
            if (_template == null)
            {
                _template = CreateTemplate();
            }

            return new Material(_template);
        }

        private static Material CreateTemplate()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader != null)
            {
                return new Material(shader) { hideFlags = HideFlags.DontSave };
            }

            // Shader.Find can fail in players where the shader was stripped; the primitive's
            // default material is always available.
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var fallback = new Material(probe.GetComponent<Renderer>().sharedMaterial) { hideFlags = HideFlags.DontSave };
            UnityObjectUtil.SafeDestroy(probe);
            return fallback;
        }
    }
}
