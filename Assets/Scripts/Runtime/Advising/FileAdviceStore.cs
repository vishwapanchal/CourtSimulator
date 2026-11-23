using System;
using System.IO;
using CourtSim.Core.Advising;
using UnityEngine;

namespace CourtSim.Runtime.Advising
{
    /// <summary>
    /// Keeps generated advice as one small JSON file per cache key, by default in
    /// <c>persistentDataPath/advice-cache</c> (on Windows: %USERPROFILE%\AppData\LocalLow\&lt;company&gt;\&lt;product&gt;).
    /// A case seen before then shows its advice instantly, even with Ollama switched off.
    /// Delete the folder to force fresh advice.
    /// </summary>
    public sealed class FileAdviceStore : IAdviceStore
    {
        public FileAdviceStore(string directory)
        {
            Directory = string.IsNullOrEmpty(directory) ? DefaultDirectory : directory;
        }

        public static string DefaultDirectory => Path.Combine(Application.persistentDataPath, "advice-cache");

        public string Directory { get; }

        public string Load(string key)
        {
            string path = PathFor(key);
            try
            {
                return path != null && File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Save(string key, string json)
        {
            string path = PathFor(key);
            if (path == null)
            {
                return;
            }

            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                // Write to a temporary file first, so a crash mid-write never leaves half a file behind.
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, json);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
            }
            catch (IOException ex)
            {
                Debug.LogWarning("Could not save advice to the cache. " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogWarning("Could not save advice to the cache. " + ex.Message);
            }
        }

        // Keys are built from safe characters; anything else is refused rather than escaping the folder.
        private string PathFor(string key)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || key.Contains(".."))
            {
                return null;
            }

            return Path.Combine(Directory, key + ".json");
        }
    }
}
