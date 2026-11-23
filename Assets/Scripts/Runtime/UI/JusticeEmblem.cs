using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// Draws CourtSim's emblem, the scales of justice inside a gold seal, out of plain UI shapes (discs, bars and
    /// thin lines), so no image file is needed. Used by the opening titles and the home page.
    /// </summary>
    /// <remarks>
    /// Every size and position is a fraction of <c>size</c>, so the emblem looks the same at any scale. The seal is
    /// a gold disc with a dark disc on top (leaving a gold rim), then a thinner inner ring made the same way.
    /// </remarks>
    public static class JusticeEmblem
    {
        /// <summary>Builds the emblem, <paramref name="size"/> pixels across, centred in a new rect under <paramref name="parent"/>.</summary>
        public static RectTransform Build(UIFactory factory, Transform parent, float size)
        {
            UITheme theme = factory.Theme;
            RectTransform root = factory.CreateRect("Emblem", parent);
            root.sizeDelta = new Vector2(size, size);

            // The seal: outer rim, dark face, thin inner ring.
            Disc(factory, root, "Rim", theme.Accent, size, Vector2.zero, size);
            Disc(factory, root, "Face", theme.Backdrop, size, Vector2.zero, size * 0.93f);
            Disc(factory, root, "InnerRing", theme.Accent, size, Vector2.zero, size * 0.86f);
            Disc(factory, root, "InnerFace", theme.Surface, size, Vector2.zero, size * 0.835f);

            Color gold = theme.AccentBright;

            // The stand: a central post on a two-step base, with a round knob on top.
            Bar(factory, root, "Post", gold, size, new Vector2(0f, -0.02f), new Vector2(0.03f, 0.44f));
            Bar(factory, root, "Foot", gold, size, new Vector2(0f, -0.225f), new Vector2(0.11f, 0.04f));
            Bar(factory, root, "Base", gold, size, new Vector2(0f, -0.255f), new Vector2(0.24f, 0.028f));
            Disc(factory, root, "Knob", gold, size, new Vector2(0f, 0.215f), size * 0.075f);

            // The beam across the top, and a pan hanging from each end on two cords.
            Bar(factory, root, "Beam", gold, size, new Vector2(0f, 0.165f), new Vector2(0.5f, 0.024f));
            for (int side = -1; side <= 1; side += 2)
            {
                var hook = new Vector2(0.22f * side, 0.165f);
                Cord(factory, root, gold, size, hook, new Vector2(hook.x - 0.075f, -0.03f));
                Cord(factory, root, gold, size, hook, new Vector2(hook.x + 0.075f, -0.03f));
                Image pan = Disc(factory, root, side < 0 ? "LeftPan" : "RightPan", gold, size, new Vector2(hook.x, -0.04f), size * 0.19f);
                pan.preserveAspect = false;
                pan.rectTransform.sizeDelta = new Vector2(size * 0.19f, size * 0.045f); // a flat ellipse seen from the side
            }

            return root;
        }

        private static Image Disc(UIFactory factory, RectTransform root, string name, Color color, float size, Vector2 centre, float diameter)
        {
            Image disc = factory.CreatePanel(name, root, color, PanelShape.LargeCircle);
            UIFactory.Place(disc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), centre * size, new Vector2(diameter, diameter));
            return disc;
        }

        private static void Bar(UIFactory factory, RectTransform root, string name, Color color, float size, Vector2 centre, Vector2 extent)
        {
            Image bar = factory.CreatePanel(name, root, color, PanelShape.Square);
            UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), centre * size, extent * size);
        }

        /// <summary>A thin straight line from <paramref name="from"/> to <paramref name="to"/>: a bar pivoted at its top end and turned.</summary>
        private static void Cord(UIFactory factory, RectTransform root, Color color, float size, Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            Image cord = factory.CreatePanel("Cord", root, color, PanelShape.Square);
            UIFactory.Place(
                cord.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 1f),
                from * size,
                new Vector2(Mathf.Max(1.5f, size * 0.008f), delta.magnitude * size));

            // A bar hangs straight down; turning it by this angle points its bottom end at the target.
            float degrees = Mathf.Atan2(delta.x, -delta.y) * Mathf.Rad2Deg;
            cord.rectTransform.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }
    }
}
