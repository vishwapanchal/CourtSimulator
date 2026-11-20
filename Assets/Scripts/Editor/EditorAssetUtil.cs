using UnityEditor;

namespace CourtSim.EditorTools
{
    internal static class EditorAssetUtil
    {
        /// <summary>Creates every missing folder in an "Assets/..." path.</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
