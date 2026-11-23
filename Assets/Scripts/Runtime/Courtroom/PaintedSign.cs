using CourtSim.Runtime.Common;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>
    /// One line of painted lettering on a courtroom sign, in Kannada, Hindi or English. The lettering faces local -Z
    /// (towards a viewer looking along +Z), centred on this object.
    /// </summary>
    /// <remarks>
    /// The letters are drawn when play starts, with Windows' text engine (see <see cref="WindowsTextRenderer"/>), because
    /// Indian scripts need proper shaping and a texture made from an OS font can't be saved in the scene. Where that
    /// isn't available the English fallback text is shown with Unity's built-in font instead.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PaintedSign : MonoBehaviour
    {
        private const int PixelHeight = 80;

        [SerializeField] private string _text = string.Empty;
        [SerializeField] private string _fallbackText = string.Empty;
        [SerializeField] private string _fontFace = "Nirmala UI";
        [SerializeField] private float _letterHeight = 0.2f;
        [SerializeField] private float _maxWidth = 2f;
        [SerializeField] private Color _ink = Color.black;
        [SerializeField] private bool _bold;

        private Texture2D _texture;
        private Material _material;

        /// <summary>True once the shaped lettering (not the English fallback) is showing.</summary>
        public bool ShowsShapedText { get; private set; }

        /// <summary>
        /// Sets what the sign says. <paramref name="letterHeight"/> is the height of the line of lettering in metres,
        /// including vowel signs above and below; the width follows the text, shrunk to fit <paramref name="maxWidth"/>.
        /// </summary>
        public void Configure(string text, string fallbackText, string fontFace, float letterHeight, float maxWidth, Color ink, bool bold)
        {
            _text = text;
            _fallbackText = fallbackText;
            _fontFace = fontFace;
            _letterHeight = letterHeight;
            _maxWidth = maxWidth;
            _ink = ink;
            _bold = bold;
        }

        private void Start()
        {
            // Sprites/Default is always included in builds, is unlit and blends the letters' soft edges.
            Shader shader = Shader.Find("Sprites/Default");
            _texture = WindowsTextRenderer.Render(_text, _fontFace, PixelHeight, _bold);
            if (_texture != null && shader != null)
            {
                ShowShapedText(shader);
            }
            else
            {
                ShowFallbackText();
            }
        }

        private void OnDestroy()
        {
            UnityObjectUtil.SafeDestroy(_material);
            UnityObjectUtil.SafeDestroy(_texture);
        }

        private void ShowShapedText(Shader shader)
        {
            _material = new Material(shader) { name = "SignText", hideFlags = HideFlags.DontSave, mainTexture = _texture, color = _ink };

            // The texture has a margin round the letters; scale so the letters themselves are about _letterHeight tall.
            float height = _letterHeight * _texture.height / (_texture.height - (2f * PixelHeight / 5f));
            float width = height * _texture.width / _texture.height;
            if (width > _maxWidth)
            {
                height *= _maxWidth / width;
                width = _maxWidth;
            }

            var face = new GameObject("Lettering");
            face.transform.SetParent(transform, false);
            face.transform.localScale = new Vector3(width, height, 1f);
            face.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var meshRenderer = face.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ShowsShapedText = true;
        }

        private void ShowFallbackText()
        {
            if (string.IsNullOrEmpty(_fallbackText))
            {
                return;
            }

            var label = new GameObject("Lettering");
            label.transform.SetParent(transform, false);
            var text = label.AddComponent<TextMesh>();
            text.text = _fallbackText;
            text.font = BuiltinAssets.DefaultFont;
            text.fontSize = 64;
            text.characterSize = _letterHeight / 6.4f; // a 64-point line is about 6.4 character sizes tall
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = _bold ? FontStyle.Bold : FontStyle.Normal;
            text.color = _ink;
            label.GetComponent<MeshRenderer>().sharedMaterial = BuiltinAssets.DefaultFont.material;
        }
    }
}
