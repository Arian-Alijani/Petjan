using Animora.Contracts.Visits;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Shared, in-memory encounter store backing both <see cref="MockVisitsTriageService"/>
/// and <see cref="MockVisitEncounterService"/> — the single source of truth for
/// the wizard's mutable encounter state (the reference keeps this on the shared
/// <c>S.enc</c> object in Animora-UI/assets/visit.js). Re-derived from what the
/// wizard visually needs; not copied from the reference's seed data. A future
/// backend replaces both mocks with implementations over the same contracts
/// (R-18), so nothing here leaks past the mock layer.
/// </summary>
public sealed class VisitEncounterStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EncounterState> _encounters = new();

    /// <summary>
    /// Registers a fresh encounter for the draft handed over from the triage
    /// gate and returns its id.
    /// </summary>
    public string Create(EncounterDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        lock (_gate)
        {
            _encounters[draft.Id] = new EncounterState(draft);
            return draft.Id;
        }
    }

    public EncounterState Get(string encounterId)
    {
        ArgumentNullException.ThrowIfNull(encounterId);
        lock (_gate)
        {
            return _encounters.TryGetValue(encounterId, out var state)
                ? state
                : throw new KeyNotFoundException($"Encounter '{encounterId}' was not found.");
        }
    }

    /// <summary>
    /// Mutable encounter state — the context draft plus every field the wizard
    /// edits across its five steps (vitals, history/exam, diagnosis/plan, the
    /// billable lines and completion). Mirrors the reference's <c>S.enc</c>.
    /// </summary>
    public sealed class EncounterState
    {
        private readonly List<RxLine> _lines = [];

        public EncounterState(EncounterDraft draft)
        {
            Draft = draft;
            History = draft.Note;
        }

        public EncounterDraft Draft { get; }

        public Vitals Vitals { get; set; } = Vitals.Empty;

        public string History { get; set; }

        public string Exam { get; set; } = string.Empty;

        public string Diagnosis { get; set; } = string.Empty;

        public string Plan { get; set; } = string.Empty;

        public bool IsCompleted { get; set; }

        public DateTime? CompletedAt { get; set; }

        public IReadOnlyList<RxLine> Lines
        {
            get
            {
                lock (_lines)
                {
                    return _lines.ToArray();
                }
            }
        }

        public void AddLine(RxLine line)
        {
            lock (_lines)
            {
                _lines.Add(line);
            }
        }

        public bool RemoveLine(string rxLineId)
        {
            lock (_lines)
            {
                var index = _lines.FindIndex(line => line.Id == rxLineId);
                if (index < 0)
                {
                    return false;
                }

                _lines.RemoveAt(index);
                return true;
            }
        }
    }
}
