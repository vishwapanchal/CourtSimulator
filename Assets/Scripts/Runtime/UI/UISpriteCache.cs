using System;
using CourtSim.Core.UI;
using CourtSim.Runtime.Common;
using UnityEngine;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// Generates the few sprites the UI needs (rounded fill, rounded outline, circles, soft gradients) once at startup,
    /// so the project carries no texture assets for UI. Dispose to free the textures.
    /// </summary>
    public sealed class UISpriteCache : IDisposable
    {
        /// <summary>Corner radius baked into <see cref="Rounded"/> and <see cref="Outline"/>, in texels.</summary>
        public const float BakedRadius = 24f;

        private const int RoundedSize = 64;
        private const int CircleSize = 48;
        private const int LargeCircleSize = 256;
        private const int GradientSize = 128;
        private const float OutlineThickness = 2f;

        public UISpriteCache()
        {
            Rounded = CreateSprite("UI_Rounded", RoundedRectMask.BuildFill(RoundedSize, RoundedSize, BakedRadius), RoundedSize, RoundedSize, BakedRadius);
            Outline = CreateSprite("UI_Outline", RoundedRectMask.BuildOutline(RoundedSize, RoundedSize, BakedRadius, OutlineThickness), RoundedSize, RoundedSize, BakedRadius);
            Circle = CreateSprite("UI_Circle", RoundedRectMask.BuildFill(CircleSize, CircleSize, CircleSize * 0.5f), CircleSize, CircleSize, 0f);
            LargeCircle = CreateSprite("UI_LargeCircle", RoundedRectMask.BuildFill(LargeCircleSize, LargeCircleSize, LargeCircleSize * 0.5f), LargeCircleSize, LargeCircleSize, 0f);
            FadeRight = CreateSprite("UI_FadeRight", BuildGradient(GradientSize, 4, (u, v) => 1f - Smooth(u)), GradientSize, 4, 0f);
            FadeUp = CreateSprite("UI_FadeUp", BuildGradient(4, GradientSize, (u, v) => 1f - Smooth(v)), 4, GradientSize, 0f);
            Glow = CreateSprite("UI_Glow", BuildGradient(GradientSize, GradientSize, (u, v) => 1f - Smooth(Distance(u, v))), GradientSize, GradientSize, 0f);
            Vignette = CreateSprite(
                "UI_Vignette",
                BuildGradient(GradientSize, GradientSize, (u, v) => Smooth(Mathf.InverseLerp(0.35f, 1.3f, Distance(u, v)))),
                GradientSize,
                GradientSize,
                0f);
        }

        /// <summary>9-sliced rounded rectangle.</summary>
        public Sprite Rounded { get; private set; }

        /// <summary>9-sliced rounded outline, drawn over a <see cref="Rounded"/> panel.</summary>
        public Sprite Outline { get; private set; }

        /// <summary>Anti-aliased disc for dots and badges.</summary>
        public Sprite Circle { get; private set; }

        /// <summary>A bigger disc that stays crisp at a few hundred pixels, e.g. for the emblem's seal.</summary>
        public Sprite LargeCircle { get; private set; }

        /// <summary>Opaque at the left edge, fading smoothly to clear at the right.</summary>
        public Sprite FadeRight { get; private set; }

        /// <summary>Opaque at the bottom edge, fading smoothly to clear at the top.</summary>
        public Sprite FadeUp { get; private set; }

        /// <summary>A soft round light: opaque in the middle, clear at the edges. Stretch it for a beam.</summary>
        public Sprite Glow { get; private set; }

        /// <summary>The opposite of <see cref="Glow"/>: clear in the middle, darkening towards the corners.</summary>
        public Sprite Vignette { get; private set; }

        public void Dispose()
        {
            Release(Rounded);
            Release(Outline);
            Release(Circle);
            Release(LargeCircle);
            Release(FadeRight);
            Release(FadeUp);
            Release(Glow);
            Release(Vignette);
            Rounded = null;
            Outline = null;
            Circle = null;
            LargeCircle = null;
            FadeRight = null;
            FadeUp = null;
            Glow = null;
            Vignette = null;
        }

        /// <summary>Fills a width × height coverage map from <paramref name="alpha"/>(u, v), with u and v running 0..1.</summary>
        private static float[] BuildGradient(int width, int height, Func<float, float, float> alpha)
        {
            var coverage = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = width > 1 ? x / (width - 1f) : 0f;
                    float v = height > 1 ? y / (height - 1f) : 0f;
                    coverage[(y * width) + x] = Mathf.Clamp01(alpha(u, v));
                }
            }

            return coverage;
        }

        /// <summary>Distance from the centre of the unit square, scaled so the middle of each edge is 1.</summary>
        private static float Distance(float u, float v)
        {
            float dx = (u - 0.5f) * 2f;
            float dy = (v - 0.5f) * 2f;
            return Mathf.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>An ease-in-out curve: a gradient through it has no visible "edge" where it starts or stops.</summary>
        private static float Smooth(float t)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        }

        private static Sprite CreateSprite(string name, float[] coverage, int width, int height, float border)
        {
            var pixels = new Color32[coverage.Length];
            for (int i = 0; i < coverage.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage[i] * 255f));
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static void Release(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            UnityObjectUtil.SafeDestroy(sprite.texture);
            UnityObjectUtil.SafeDestroy(sprite);
        }
    }
}
