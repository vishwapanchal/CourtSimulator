using System;
using CourtSim.Core.UI;
using CourtSim.Runtime.Common;
using UnityEngine;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// Generates the few sprites the UI needs (rounded fill, rounded outline, circle) once at startup,
    /// so the project carries no texture assets for UI. Dispose to free the textures.
    /// </summary>
    public sealed class UISpriteCache : IDisposable
    {
        /// <summary>Corner radius baked into <see cref="Rounded"/> and <see cref="Outline"/>, in texels.</summary>
        public const float BakedRadius = 24f;

        private const int RoundedSize = 64;
        private const int CircleSize = 48;
        private const float OutlineThickness = 2f;

        public UISpriteCache()
        {
            Rounded = CreateSprite("UI_Rounded", RoundedRectMask.BuildFill(RoundedSize, RoundedSize, BakedRadius), RoundedSize, RoundedSize, BakedRadius);
            Outline = CreateSprite("UI_Outline", RoundedRectMask.BuildOutline(RoundedSize, RoundedSize, BakedRadius, OutlineThickness), RoundedSize, RoundedSize, BakedRadius);
            Circle = CreateSprite("UI_Circle", RoundedRectMask.BuildFill(CircleSize, CircleSize, CircleSize * 0.5f), CircleSize, CircleSize, 0f);
        }

        /// <summary>9-sliced rounded rectangle.</summary>
        public Sprite Rounded { get; private set; }

        /// <summary>9-sliced rounded outline, drawn over a <see cref="Rounded"/> panel.</summary>
        public Sprite Outline { get; private set; }

        /// <summary>Anti-aliased disc for dots and badges.</summary>
        public Sprite Circle { get; private set; }

        public void Dispose()
        {
            Release(Rounded);
            Release(Outline);
            Release(Circle);
            Rounded = null;
            Outline = null;
            Circle = null;
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
