using System;

namespace CourtSim.Core.UI
{
    /// <summary>
    /// Anti-aliased coverage masks for rounded rectangles, used to generate UI sprites
    /// procedurally so the project ships without hand-authored texture assets.
    /// </summary>
    public static class RoundedRectMask
    {
        /// <summary>
        /// Coverage in [0, 1] of the pixel at (<paramref name="x"/>, <paramref name="y"/>) by a rounded
        /// rectangle that fills a <paramref name="width"/> × <paramref name="height"/> grid, shrunk
        /// inward by <paramref name="inset"/> pixels on every side.
        /// </summary>
        /// <remarks>Uses the signed distance to a rounded box sampled at the pixel center, with a one-pixel
        /// linear falloff for anti-aliasing. The radius is clamped to half the smaller dimension.</remarks>
        public static float Coverage(int x, int y, int width, int height, float radius, float inset = 0f)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(width <= 0 ? nameof(width) : nameof(height), "Dimensions must be positive.");
            }

            float halfWidth = (width * 0.5f) - inset;
            float halfHeight = (height * 0.5f) - inset;
            if (halfWidth <= 0f || halfHeight <= 0f)
            {
                return 0f;
            }

            float r = Math.Clamp(radius - inset, 0f, Math.Min(halfWidth, halfHeight));
            float px = Math.Abs((x + 0.5f) - (width * 0.5f));
            float py = Math.Abs((y + 0.5f) - (height * 0.5f));

            float qx = px - (halfWidth - r);
            float qy = py - (halfHeight - r);
            float outside = MathF.Sqrt((Math.Max(qx, 0f) * Math.Max(qx, 0f)) + (Math.Max(qy, 0f) * Math.Max(qy, 0f)));
            float inside = Math.Min(Math.Max(qx, qy), 0f);
            float distance = outside + inside - r;

            return Math.Clamp(0.5f - distance, 0f, 1f);
        }

        /// <summary>Coverage of a rounded outline of the given <paramref name="thickness"/> along the edge.</summary>
        public static float OutlineCoverage(int x, int y, int width, int height, float radius, float thickness)
        {
            float outer = Coverage(x, y, width, height, radius);
            float inner = Coverage(x, y, width, height, radius, thickness);
            return Math.Clamp(outer - inner, 0f, 1f);
        }

        /// <summary>Row-major coverage values (y = 0 is the bottom row, matching Unity texture layout).</summary>
        public static float[] BuildFill(int width, int height, float radius)
        {
            var result = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    result[(y * width) + x] = Coverage(x, y, width, height, radius);
                }
            }

            return result;
        }

        /// <summary>Row-major outline coverage values (y = 0 is the bottom row).</summary>
        public static float[] BuildOutline(int width, int height, float radius, float thickness)
        {
            var result = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    result[(y * width) + x] = OutlineCoverage(x, y, width, height, radius, thickness);
                }
            }

            return result;
        }
    }
}
