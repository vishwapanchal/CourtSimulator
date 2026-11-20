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
                    albedo = new Color(0.24f, 0.16f, 0.11f);
                    smoothness = 0.45f;
                    break;
                case SurfaceKind.Wall:
                    albedo = new Color(0.86f, 0.82f, 0.74f);
                    smoothness = 0.08f;
                    break;
                case SurfaceKind.Wainscot:
                    albedo = new Color(0.36f, 0.24f, 0.15f);
                    smoothness = 0.35f;
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
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown surface kind.");
            }
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
