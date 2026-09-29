using System;

namespace InventorXrSo.Core.Ui
{
    public enum CommitBarPhase { Empty, Draft, Previewing, Ready, Stale, Uncertain, Error, Offline, Applied }

    /// <summary>Fatti del workspace da cui si ricava lo stato della barra; le regole restano quelle di M3–M5.</summary>
    public readonly struct CommitBarInputs
    {
        public CommitBarInputs(bool online, bool outcomeUnknown, bool refreshRequired, bool busy, bool hasPreview, bool hasDraft, string error)
        {
            Online = online; OutcomeUnknown = outcomeUnknown; RefreshRequired = refreshRequired; Busy = busy;
            HasPreview = hasPreview; HasDraft = hasDraft; Error = error ?? "";
        }
        public bool Online { get; }
        public bool OutcomeUnknown { get; }
        public bool RefreshRequired { get; }
        public bool Busy { get; }
        public bool HasPreview { get; }
        public bool HasDraft { get; }
        public string Error { get; }
    }

    /// <summary>Barra di conferma unica: l'unico posto da cui una modifica entra nel CAD.</summary>
    public sealed class CommitBarState
    {
        public const double AppliedSeconds = 1.5;
        private double _appliedAt = double.NegativeInfinity;

        public CommitBarPhase Phase { get; private set; } = CommitBarPhase.Empty;
        public string Message { get; private set; } = "";
        public event Action Changed;

        public bool CanPreview => Phase == CommitBarPhase.Draft;
        public bool CanApply => Phase == CommitBarPhase.Ready;
        public bool CanCancel => Phase == CommitBarPhase.Draft || Phase == CommitBarPhase.Ready || Phase == CommitBarPhase.Error;
        public string RecoveryLabel => Phase == CommitBarPhase.Stale ? "Aggiorna documento"
            : Phase == CommitBarPhase.Uncertain ? "Ho controllato il CAD" : null;

        public static CommitBarPhase Derive(CommitBarInputs i)
        {
            if (!i.Online) return CommitBarPhase.Offline;
            if (i.OutcomeUnknown) return CommitBarPhase.Uncertain;
            if (i.RefreshRequired) return CommitBarPhase.Stale;
            if (i.Busy) return CommitBarPhase.Previewing;
            if (i.Error.Length > 0) return CommitBarPhase.Error;
            if (i.HasPreview) return CommitBarPhase.Ready;
            if (i.HasDraft) return CommitBarPhase.Draft;
            return CommitBarPhase.Empty;
        }

        public void Update(CommitBarInputs inputs, double nowSeconds)
        {
            var phase = Derive(inputs);
            if (phase == CommitBarPhase.Empty && nowSeconds - _appliedAt < AppliedSeconds) phase = CommitBarPhase.Applied;
            Set(phase, phase == CommitBarPhase.Error ? inputs.Error : "");
        }

        public void MarkApplied(double nowSeconds)
        {
            _appliedAt = nowSeconds;
            Set(CommitBarPhase.Applied, "");
        }

        public void Tick(double nowSeconds)
        {
            if (Phase == CommitBarPhase.Applied && nowSeconds - _appliedAt >= AppliedSeconds) Set(CommitBarPhase.Empty, "");
        }

        private void Set(CommitBarPhase phase, string message)
        {
            if (phase == Phase && message == Message) return;
            Phase = phase; Message = message;
            Changed?.Invoke();
        }
    }
}
