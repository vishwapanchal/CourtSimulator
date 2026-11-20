using System;
using System.Collections.Generic;

namespace CourtSim.Core.Decisions
{
    /// <summary>A ruling the trainee made, in the order it was made.</summary>
    public readonly struct DecisionRecord
    {
        public DecisionRecord(int sequenceIndex, DecisionPointKind kind, string optionId)
        {
            SequenceIndex = sequenceIndex;
            Kind = kind;
            OptionId = optionId;
        }

        /// <summary>Zero-based position of this decision within the session.</summary>
        public int SequenceIndex { get; }

        public DecisionPointKind Kind { get; }

        public string OptionId { get; }
    }

    /// <summary>
    /// Walks a trainee through an ordered list of decision points and records each ruling.
    /// </summary>
    public sealed class DecisionSequence
    {
        private readonly DecisionPointDefinition[] _points;
        private readonly List<DecisionRecord> _records = new List<DecisionRecord>();

        public DecisionSequence(IEnumerable<DecisionPointDefinition> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            _points = new List<DecisionPointDefinition>(points).ToArray();
            if (_points.Length == 0)
            {
                throw new ArgumentException("A sequence needs at least one decision point.", nameof(points));
            }

            foreach (DecisionPointDefinition point in _points)
            {
                if (point == null)
                {
                    throw new ArgumentException("Decision points must not contain null.", nameof(points));
                }
            }
        }

        /// <summary>Total number of decision points.</summary>
        public int Count => _points.Length;

        /// <summary>Index of the pending decision point; equals <see cref="Count"/> once complete.</summary>
        public int CurrentIndex => _records.Count;

        public bool IsComplete => _records.Count >= _points.Length;

        /// <summary>The pending decision point, or null once every ruling has been recorded.</summary>
        public DecisionPointDefinition Current => IsComplete ? null : _points[_records.Count];

        public IReadOnlyList<DecisionRecord> Records => _records;

        /// <summary>Bail, then evidence admissibility, then sentencing, using placeholder content.</summary>
        public static DecisionSequence CreateStandardPlaceholder()
        {
            return new DecisionSequence(new[]
            {
                DecisionPointCatalog.GetPlaceholder(DecisionPointKind.Bail),
                DecisionPointCatalog.GetPlaceholder(DecisionPointKind.EvidenceAdmissibility),
                DecisionPointCatalog.GetPlaceholder(DecisionPointKind.Sentencing),
            });
        }

        /// <summary>Records <paramref name="optionId"/> for the current point and advances.</summary>
        /// <exception cref="InvalidOperationException">The sequence is already complete.</exception>
        /// <exception cref="ArgumentException"><paramref name="optionId"/> is not an option of the current point.</exception>
        public DecisionRecord Record(string optionId)
        {
            DecisionPointDefinition current = Current;
            if (current == null)
            {
                throw new InvalidOperationException("Every decision point has already been ruled on.");
            }

            if (current.FindOption(optionId) == null)
            {
                throw new ArgumentException($"'{optionId}' is not an option for {current.Kind}.", nameof(optionId));
            }

            var record = new DecisionRecord(_records.Count, current.Kind, optionId);
            _records.Add(record);
            return record;
        }

        /// <summary>Discards every recorded ruling so the session can be replayed.</summary>
        public void Reset()
        {
            _records.Clear();
        }
    }
}
