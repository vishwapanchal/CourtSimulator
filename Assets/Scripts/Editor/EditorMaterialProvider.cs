using System.Collections.Generic;
using CourtSim.Runtime.Courtroom;
using UnityEditor;
using UnityEngine;

namespace CourtSim.EditorTools
{
    /// <summary>
    /// Material provider backed by assets in <see cref="Folder"/>, so baked scenes reference real
    /// materials that ship in builds. Existing assets are reused untouched, so artist tweaks survive
    /// scene regeneration; delete an asset to reset it to the palette.
    /// </summary>
    internal sealed class EditorMaterialProvider : IMaterialProvider
    {
        public const string Folder = "Assets/Materials/Courtroom";

        private readonly Dictionary<SurfaceKind, Material> _cache = new Dictionary<SurfaceKind, Material>();

        public Material Get(SurfaceKind kind)
        {
            if (_cache.TryGetValue(kind, out Material cached) && cached != null)
            {
                return cached;
            }

            string path = $"{Folder}/Courtroom_{kind}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EditorAssetUtil.EnsureFolder(Folder);
                material = new Material(Shader.Find("Standard")) { name = $"Courtroom_{kind}" };
                CourtroomPalette.Configure(material, kind);
                AssetDatabase.CreateAsset(material, path);

                // A patterned surface (the floor tiles) keeps its texture inside the material asset.
                Texture2D pattern = CourtroomPalette.CreatePattern(kind);
                if (pattern != null)
                {
                    AssetDatabase.AddObjectToAsset(pattern, material);
                    material.mainTexture = pattern;
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssets();
                }
            }

            _cache[kind] = material;
            return material;
        }
    }
}
