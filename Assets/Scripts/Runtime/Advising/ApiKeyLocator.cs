using System;
using System.IO;
using UnityEngine;

namespace CourtSim.Runtime.Advising
{
    /// <summary>
    /// Finds the Anthropic API key without it ever entering the project or a build: the
    /// <c>ANTHROPIC_API_KEY</c> environment variable first, then a one-line file in the user's
    /// persistent data folder. Keys must never be committed or placed under <c>Assets/</c>.
    /// </summary>
    public static class ApiKeyLocator
    {
        public const string EnvironmentVariable = "ANTHROPIC_API_KEY";
        public const string KeyFileName = "anthropic_api_key.txt";

        /// <summary>Where the optional key file is read from on this machine.</summary>
        public static string KeyFilePath => Path.Combine(Application.persistentDataPath, KeyFileName);

        /// <summary>The key, or null if none is configured. Never log the return value.</summary>
        public static string Find()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment.Trim();
            }

            try
            {
                if (File.Exists(KeyFilePath))
                {
                    string fromFile = File.ReadAllText(KeyFilePath).Trim();
                    return fromFile.Length > 0 ? fromFile : null;
                }
            }
            catch (IOException)
            {
                // Unreadable key file: behave as if none is configured.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }

            return null;
        }
    }
}
