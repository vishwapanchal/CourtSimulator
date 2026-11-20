using System.Collections.Generic;
using CourtSim.Core.Cases;
using UnityEngine;

namespace CourtSim.Runtime.Cases
{
    /// <summary>Loads and validates every case file under <c>Resources/Cases</c>. Invalid files are reported and skipped.</summary>
    public static class CaseRepository
    {
        public const string ResourceFolder = "Cases";

        public static IReadOnlyList<CaseFile> LoadAll()
        {
            var cases = new List<CaseFile>();
            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourceFolder);
            foreach (TextAsset asset in assets)
            {
                CaseReadResult result = CaseFileReader.Read(asset.text);
                if (result.Success)
                {
                    cases.Add(result.Case);
                }
                else
                {
                    Debug.LogError($"Case file '{asset.name}' is invalid and was skipped:\n- " + string.Join("\n- ", result.Errors));
                }
            }

            cases.Sort((a, b) => a.Difficulty != b.Difficulty ? a.Difficulty.CompareTo(b.Difficulty) : string.CompareOrdinal(a.Id, b.Id));
            return cases;
        }
    }
}
