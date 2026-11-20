using CourtSim.Core.Courtroom;
using UnityEngine;

namespace CourtSim.Runtime.Common
{
    /// <summary>Conversions between engine-independent core types and Unity types.</summary>
    public static class CoreConversions
    {
        public static Vector3 ToVector3(this Float3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }
    }

    /// <summary>Lazily loaded Unity built-in resources, so the project ships without imported art.</summary>
    public static class BuiltinAssets
    {
        private static Font _defaultFont;
        private static Mesh _cubeMesh;

        /// <summary>Unity's built-in UI font (LegacyRuntime.ttf, Unity 2022.2+).</summary>
        public static Font DefaultFont
        {
            get
            {
                if (_defaultFont == null)
                {
                    _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return _defaultFont;
            }
        }

        /// <summary>Unit cube mesh centred on the origin.</summary>
        public static Mesh CubeMesh
        {
            get
            {
                if (_cubeMesh == null)
                {
                    _cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                }

                return _cubeMesh;
            }
        }
    }

    /// <summary>Helpers for creating and destroying Unity objects safely in both edit and play mode.</summary>
    public static class UnityObjectUtil
    {
        /// <summary>Destroys <paramref name="obj"/> with the call appropriate to the current mode.</summary>
        public static void SafeDestroy(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(obj);
            }
            else
            {
                Object.DestroyImmediate(obj);
            }
        }

        /// <summary>Creates an empty child transform.</summary>
        public static Transform CreateChild(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        /// <summary>Finds the first object of type <typeparamref name="T"/> in loaded scenes, across Unity versions.</summary>
        public static T FindFirst<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }
    }
}
