using System;
using System.Collections.Generic;

namespace CourtSim.Core.Decisions
{
    /// <summary>One selectable ruling at a decision point.</summary>
    public sealed class DecisionOption
    {
        public DecisionOption(string id, string label)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Option id must not be empty.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("Option label must not be empty.", nameof(label));
            }

            Id = id;
            Label = label;
        }

        /// <summary>Stable machine identifier, used in decision logs.</summary>
        public string Id { get; }

        /// <summary>Text shown to the trainee.</summary>
        public string Label { get; }
    }

    /// <summary>The question posed at a decision point and the rulings available.</summary>
    public sealed class DecisionPointDefinition
    {
        private readonly DecisionOption[] _options;

        public DecisionPointDefinition(DecisionPointKind kind, string title, string prompt, IEnumerable<DecisionOption> options)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("Title must not be empty.", nameof(title));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = new List<DecisionOption>(options).ToArray();
            if (_options.Length < 2)
            {
                throw new ArgumentException("A decision point needs at least two options.", nameof(options));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DecisionOption option in _options)
            {
                if (option == null)
                {
                    throw new ArgumentException("Options must not contain null.", nameof(options));
                }

                if (!seen.Add(option.Id))
                {
                    throw new ArgumentException($"Duplicate option id '{option.Id}'.", nameof(options));
                }
            }

            Kind = kind;
            Title = title;
            Prompt = prompt ?? string.Empty;
        }

        public DecisionPointKind Kind { get; }

        public string Title { get; }

        public string Prompt { get; }

        public IReadOnlyList<DecisionOption> Options => _options;

        /// <summary>Returns the option with <paramref name="optionId"/>, or null.</summary>
        public DecisionOption FindOption(string optionId)
        {
            foreach (DecisionOption option in _options)
            {
                if (string.Equals(option.Id, optionId, StringComparison.Ordinal))
                {
                    return option;
                }
            }

            return null;
        }
    }
}
