using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public enum SheetMetalAvailability { NoDocument, NotAPart, NoRead, NotSheetMetal, Stale, Offline, Unreadable, Ready }
    public enum SheetMetalCommand { None, Rule, Face, Flange, Cut, FlatPattern }
    public enum FlatPatternCreation { Allowed, AlreadyExists, MultiBody, Unavailable }

    /// <summary>
    /// Decides whether Lamiera is the primary mode and arms sheet-metal writes. It never owns a preview:
    /// every write goes through the shared <see cref="DesignSession"/> (draft, preview, Apply, Undo), whose
    /// default checks (rebuild, feature_health) stay in force. No sheet-metal specific server rule is requested:
    /// the validator vocabulary is closed, so bend/thickness/flat-pattern evidence stays a native probe item.
    /// </summary>
    public sealed class SheetMetalMode
    {
        private DocumentState _current;
        private SheetMetalCommand _armed;
        public SheetMetalContext Context { get; private set; }
        public SheetMetalAvailability Availability { get; private set; } = SheetMetalAvailability.NoDocument;
        public SheetMetalCommand Armed => _armed;
        public string LastError { get; private set; }
        public event Action Changed;
        /// <summary>Document or revision moved: edge ids, drafts and armed commands are no longer meaningful.</summary>
        public event Action ContextInvalidated;

        /// <summary>Lamiera leads only for a part whose read said is_sheet_metal=true; Design, Sketch and Inspect are unaffected.</summary>
        public bool IsPrimary => Context != null && Context.IsSheetMetal && Availability != SheetMetalAvailability.NoDocument
            && Availability != SheetMetalAvailability.NotAPart && Availability != SheetMetalAvailability.NoRead;
        public bool CanWrite => Availability == SheetMetalAvailability.Ready;
        public string Reason { get; private set; }

        public void Update(DocumentState current, bool online, bool isPart, SheetMetalContext context)
        {
            bool moved = _current?.DocumentId != current?.DocumentId || _current?.Revision != current?.Revision;
            _current = current; Context = context;
            var before = Availability; string reasonBefore = Reason;
            (Availability, Reason) = Evaluate(current, online, isPart, context);
            var armedBefore = _armed;
            if (moved || Availability != SheetMetalAvailability.Ready) _armed = SheetMetalCommand.None;
            LastError = null;
            if (moved) ContextInvalidated?.Invoke();
            if (moved || before != Availability || reasonBefore != Reason || armedBefore != _armed) Changed?.Invoke();
        }

        private static (SheetMetalAvailability, string) Evaluate(DocumentState current, bool online, bool isPart, SheetMetalContext context)
        {
            if (current == null) return (SheetMetalAvailability.NoDocument, "Nessun documento attivo.");
            if (!isPart) return (SheetMetalAvailability.NotAPart, "Lamiera richiede una parte.");
            if (context == null || context.State?.DocumentId != current.DocumentId)
                return (SheetMetalAvailability.NoRead, "Lettura lamiera non ancora disponibile per questo documento.");
            if (context.IsComplete && !context.IsSheetMetal) return (SheetMetalAvailability.NotSheetMetal, "La parte non è in lamiera.");
            if (!context.IsSheetMetal) return (SheetMetalAvailability.NoRead, context.Reason);
            if (!context.IsCurrentFor(current)) return (SheetMetalAvailability.Stale, "Lettura lamiera obsoleta: rileggi il documento.");
            if (!online) return (SheetMetalAvailability.Offline, "PC non raggiungibile: Lamiera è in sola lettura.");
            if (!context.CanArmWrite) return (SheetMetalAvailability.Unreadable, context.Reason);
            return (SheetMetalAvailability.Ready, null);
        }

        public bool Arm(SheetMetalCommand command, out string reason)
        {
            reason = null;
            if (!CanWrite) { reason = Reason ?? "Lamiera non disponibile."; return false; }
            if (command == SheetMetalCommand.FlatPattern && CheckFlatPattern(out reason) != FlatPatternCreation.Allowed) return false;
            if (_armed == command) return true;
            _armed = command; Changed?.Invoke(); return true;
        }

        public void Disarm()
        {
            if (_armed == SheetMetalCommand.None) return;
            _armed = SheetMetalCommand.None; Changed?.Invoke();
        }

        /// <summary>Distinct outcomes: an existing pattern is reported, never rebuilt; several bodies cannot be unfolded.</summary>
        public FlatPatternCreation CheckFlatPattern(out string message)
        {
            message = null;
            if (!CanWrite) { message = Reason ?? "Lamiera non disponibile."; return FlatPatternCreation.Unavailable; }
            if (Context.FlatPattern.Exists) { message = "Lo sviluppo esiste già: non viene ricostruito."; return FlatPatternCreation.AlreadyExists; }
            if (!Context.IsSingleBody) { message = "Lo sviluppo richiede una parte con un solo body."; return FlatPatternCreation.MultiBody; }
            return FlatPatternCreation.Allowed;
        }

        public bool SubmitRule(DesignSession session, string rule, string unfoldRule, double? thicknessMm) =>
            Submit(session, SheetMetalCommand.Rule, () =>
            {
                if (!string.IsNullOrWhiteSpace(rule) && !Context.AvailableRules.Contains(rule))
                    throw new ArgumentException("La regola non è presente nel documento.");
                return SheetMetalOperations.SetRule(rule, unfoldRule, thicknessMm);
            });
        public bool SubmitFace(DesignSession session, string sketch) =>
            Submit(session, SheetMetalCommand.Face, () => SheetMetalOperations.Face(sketch));
        public bool SubmitCut(DesignSession session, string sketch, string extent, string direction, bool acrossBends) =>
            Submit(session, SheetMetalCommand.Cut, () => SheetMetalOperations.Cut(sketch, extent, direction, acrossBends));
        public bool SubmitFlange(DesignSession session, FlangeDraft draft) =>
            Submit(session, SheetMetalCommand.Flange, () =>
            {
                if (!draft.TryBuild(out var operation, out var error)) throw new ArgumentException(error);
                return operation;
            });
        public bool SubmitFlatPattern(DesignSession session, string alignToEdgeId = null, string alignment = null, bool reversed = false) =>
            Submit(session, SheetMetalCommand.FlatPattern, () =>
            {
                if (CheckFlatPattern(out var message) != FlatPatternCreation.Allowed) throw new ArgumentException(message);
                return SheetMetalOperations.CreateFlatPattern(alignToEdgeId, alignment, reversed);
            });

        /// <summary>Local validation failure keeps the ghost but drops the executable draft; nothing is written to CAD.</summary>
        private bool Submit(DesignSession session, SheetMetalCommand command, Func<JObject> build)
        {
            LastError = null;
            if (!CanWrite) { LastError = Reason ?? "Lamiera non disponibile."; return false; }
            if (_armed != command) { LastError = "Arma prima il comando."; return false; }
            if (session == null || !session.CanEdit) { LastError = "Design non disponibile finché il documento non è online e aggiornato."; return false; }
            JObject operation;
            try { operation = build(); }
            catch (ArgumentException ex) { LastError = ex.Message; session.RejectDraft(ex.Message); return false; }
            session.SetDraft(new JArray(operation));
            return true;
        }

        /// <summary>
        /// Keeps the session in step with the flange draft: every edit replaces the session draft (or rejects it),
        /// so a preview of an older version can never be applied. Dispose to unbind.
        /// </summary>
        public IDisposable BindFlange(DesignSession session, FlangeDraft draft)
        {
            void OnDraft() { if (_armed == SheetMetalCommand.Flange && session.CanEdit) SubmitFlange(session, draft); }
            void OnInvalidated() => draft.Clear();
            draft.Changed += OnDraft; ContextInvalidated += OnInvalidated;
            return new Unbind(() => { draft.Changed -= OnDraft; ContextInvalidated -= OnInvalidated; });
        }

        /// <summary>Release of the manipulator asks for a preview of the current draft version; it never commits.</summary>
        public async Task<bool> PreviewPendingAsync(DesignSession session, FlangeDraft draft)
        {
            if (!draft.PreviewRequested) return false;
            draft.AcknowledgePreviewRequest();
            if (session.Status != DesignStatus.Draft) return false;
            await session.PreviewAsync();
            return true;
        }

        private sealed class Unbind : IDisposable
        {
            private Action _action;
            public Unbind(Action action) { _action = action; }
            public void Dispose() { _action?.Invoke(); _action = null; }
        }
    }
}
