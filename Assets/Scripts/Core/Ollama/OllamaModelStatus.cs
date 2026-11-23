using System;
using CourtSim.Core.Json;

namespace CourtSim.Core.Ollama
{
    /// <summary>Where Ollama has put a model's weights.</summary>
    public enum ModelPlacement
    {
        /// <summary>The model is not loaded right now (nothing to check yet).</summary>
        NotLoaded,

        /// <summary>Entirely in graphics memory: the fast case, a few seconds per advice.</summary>
        Gpu,

        /// <summary>Split between graphics memory and system memory: noticeably slower.</summary>
        PartlyCpu,

        /// <summary>Entirely in system memory: ten or more times slower; advice can take over 30 seconds.</summary>
        Cpu,
    }

    /// <summary>
    /// One model from Ollama's <c>GET /api/ps</c> ("which models are loaded"). Ollama reports the model's
    /// total size and how much of it sits in graphics memory (<c>size_vram</c>); comparing the two tells us
    /// whether the GPU is being used. Ollama sometimes falls back to the CPU (for example when the GPU was
    /// busy when it started) and stays there until it is restarted, which made advice very slow.
    /// </summary>
    public sealed class OllamaModelStatus
    {
        /// <summary>Below this share of the model in graphics memory, it counts as partly on the CPU.</summary>
        public const double GpuShareThreshold = 0.9;

        public OllamaModelStatus(string name, long sizeBytes, long vramBytes)
        {
            Name = name ?? string.Empty;
            SizeBytes = Math.Max(0, sizeBytes);
            VramBytes = Math.Max(0, vramBytes);
        }

        public string Name { get; }

        public long SizeBytes { get; }

        public long VramBytes { get; }

        public bool IsLoaded => Name.Length > 0;

        public ModelPlacement Placement
        {
            get
            {
                if (!IsLoaded)
                {
                    return ModelPlacement.NotLoaded;
                }

                if (VramBytes == 0)
                {
                    return ModelPlacement.Cpu;
                }

                return SizeBytes > 0 && VramBytes < SizeBytes * GpuShareThreshold ? ModelPlacement.PartlyCpu : ModelPlacement.Gpu;
            }
        }

        /// <summary>A plain warning for the log when the model is not fully on the GPU, otherwise null.</summary>
        public string Warning
        {
            get
            {
                switch (Placement)
                {
                    case ModelPlacement.Cpu:
                        return $"Ollama is running the model on the CPU; restart Ollama to use the GPU. ({Name}: 0 of {Megabytes(SizeBytes)} MB in GPU memory, so advice will be very slow.)";
                    case ModelPlacement.PartlyCpu:
                        return $"Ollama is running part of the model on the CPU; restart Ollama to use the GPU. ({Name}: {Megabytes(VramBytes)} of {Megabytes(SizeBytes)} MB in GPU memory.)";
                    default:
                        return null;
                }
            }
        }

        /// <summary>A model that is not loaded.</summary>
        public static OllamaModelStatus NotLoadedModel()
        {
            return new OllamaModelStatus(string.Empty, 0, 0);
        }

        /// <summary>
        /// Finds <paramref name="model"/> in a <c>/api/ps</c> reply. A tag-less name such as "llama3" also matches
        /// "llama3:latest", as Ollama does. Returns a not-loaded status if the model is absent or the reply is unreadable.
        /// </summary>
        public static OllamaModelStatus FromPsReply(string psJson, string model)
        {
            if (string.IsNullOrWhiteSpace(model) || !JsonReader.TryParse(psJson, out JsonValue root, out _) || root.Kind != JsonKind.Object)
            {
                return NotLoadedModel();
            }

            JsonValue models = root.Get("models");
            if (models == null)
            {
                return NotLoadedModel();
            }

            foreach (JsonValue entry in models.Items)
            {
                string name = entry.GetString("name") ?? entry.GetString("model");
                if (Matches(name, model) || Matches(entry.GetString("model"), model))
                {
                    return new OllamaModelStatus(name, (long)(entry.GetNumber("size") ?? 0), (long)(entry.GetNumber("size_vram") ?? 0));
                }
            }

            return NotLoadedModel();
        }

        private static bool Matches(string name, string model)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string wanted = model.Trim();
            return string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)
                || (!wanted.Contains(":") && string.Equals(name, wanted + ":latest", StringComparison.OrdinalIgnoreCase));
        }

        private static long Megabytes(long bytes)
        {
            return bytes / (1024 * 1024);
        }
    }
}
