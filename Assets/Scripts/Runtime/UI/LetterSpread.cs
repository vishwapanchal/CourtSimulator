using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// Spreads the letters of a centred, single-line <see cref="Text"/> apart (or squeezes them together) without
    /// changing their size. Unity's legacy Text has no letter-spacing setting, so this moves each letter after the
    /// text is laid out: a letter's offset is its distance from the centre times <see cref="Spread"/>.
    /// </summary>
    /// <remarks>
    /// Text draws every letter as one quad (four corners), so the mesh is walked four vertices at a time and each
    /// quad is shifted as a whole. Animating <see cref="Spread"/> from, say, 0.4 down to 0 makes a title "gather"
    /// into place, a common film-title effect; a small fixed value gives spaced-out capitals.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class LetterSpread : BaseMeshEffect
    {
        [SerializeField] private float _spread;

        /// <summary>0 leaves the text as laid out; 0.2 moves each letter 20% further from the centre.</summary>
        public float Spread
        {
            get => _spread;
            set
            {
                if (Mathf.Approximately(value, _spread))
                {
                    return;
                }

                _spread = value;
                if (graphic != null)
                {
                    graphic.SetVerticesDirty();
                }
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Approximately(_spread, 0f))
            {
                return;
            }

            float centre = graphic.rectTransform.rect.center.x;
            var vertex = new UIVertex();
            int count = vh.currentVertCount;
            for (int quad = 0; quad + 3 < count; quad += 4)
            {
                float middle = 0f;
                for (int i = 0; i < 4; i++)
                {
                    vh.PopulateUIVertex(ref vertex, quad + i);
                    middle += vertex.position.x * 0.25f;
                }

                float offset = (middle - centre) * _spread;
                for (int i = 0; i < 4; i++)
                {
                    vh.PopulateUIVertex(ref vertex, quad + i);
                    vertex.position.x += offset;
                    vh.SetUIVertex(vertex, quad + i);
                }
            }
        }
    }
}
