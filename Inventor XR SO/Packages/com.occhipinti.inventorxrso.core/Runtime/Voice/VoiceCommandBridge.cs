using System;
using System.Collections.Generic;

namespace InventorXrSo.Core.Voice
{
    public enum VoiceOutcomeKind
    {
        None,
        /// <summary>Comando eseguito con lo stesso percorso del pulsante.</summary>
        Executed,
        /// <summary>Riconosciuto ma non eseguibile (disabilitato o rifiutato dal pulsante).</summary>
        NotExecuted,
        /// <summary>Frase non riconosciuta.</summary>
        Rejected,
        /// <summary>Mostrata la conferma del piano: serve Applica fisico.</summary>
        ApplyConfirmationShown,
        /// <summary>In attesa di una conferma fisica (es. Undo).</summary>
        AwaitingConfirmation,
        /// <summary>Valore dettato proposto: serve conferma fisica.</summary>
        DictationProposed,
    }

    /// <summary>
    /// Collega controller vocale e UI. Non dipende da Unity. Il controller notifica da thread di pool, quindi
    /// qui non si reagisce a Changed: ogni effetto sulla UI (Invoke, ShowApplyConfirmation, SetField) avviene
    /// in <see cref="Pump"/>, chiamato dal thread principale a ogni frame. La voce non fa mai commit:
    /// "applica" mostra la conferma, Undo e dettatura richiedono una conferma fisica.
    /// </summary>
    public sealed class VoiceCommandBridge : ICommandAvailability, IDisposable
    {
        /// <summary>Testo mostrato quando si preme il push-to-talk fuori sessione (Home).</summary>
        public const string NotInSessionText = "Voce disponibile solo in sessione";

        private readonly IVoiceCommandTarget _target;
        private readonly VoiceCommandRouter _router = new VoiceCommandRouter();
        private readonly DictationTarget _dictation;
        private readonly Func<DateTimeOffset> _clock;
        private DateTimeOffset _pendingSince;
        private VoiceRouteResult _handledRoute;
        private DictationProposal _handledDictation;
        private DictationField _armed;
        private string _lastContextId;
        private string _lastContextLabel;
        private Dictionary<string, CommandAvailability> _availabilitySnapshot = new Dictionary<string, CommandAvailability>();

        /// <summary>Una conferma fisica non presa in tempo scade: nessuna azione resta armata a lungo.</summary>
        public TimeSpan PendingTimeout { get; set; } = TimeSpan.FromSeconds(15);

        public PushToTalkController Controller { get; }
        public DictationTarget Dictation => _dictation;

        public VoiceOutcomeKind Outcome { get; private set; }
        /// <summary>Testo italiano per il pannello.</summary>
        public string OutcomeText { get; private set; } = "";
        /// <summary>Comando in attesa di conferma fisica (Undo), o null.</summary>
        public string PendingConfirmationCommandId { get; private set; }
        public string PendingConfirmationText { get; private set; } = "";
        public string LastContextCommandLabel => _lastContextLabel;
        public event Action OutcomeChanged;

        public VoiceCommandBridge(IVoiceCommandTarget target, ISpeechRecognizer recognizer,
            Func<DateTimeOffset> clock = null, TimeSpan? armingThreshold = null, TimeSpan? resultDisplayTime = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            if (recognizer == null) throw new ArgumentNullException(nameof(recognizer));
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _dictation = new DictationTarget((id, v) => _target.SetField(id, v));
            Controller = new PushToTalkController(recognizer, _router, this, _dictation, clock, armingThreshold, resultDisplayTime, () => _target.AcceptsVoice);
            RefreshAvailabilitySnapshot();
        }

        public CommandAvailability GetAvailability(string commandId)
        {
            var snapshot = _availabilitySnapshot;
            return snapshot.TryGetValue(commandId, out var availability)
                ? availability : CommandAvailability.Disabled("Comando non disponibile ora.");
        }

        private void RefreshAvailabilitySnapshot()
        {
            var snapshot = new Dictionary<string, CommandAvailability>();
            foreach (var id in CommandIds.All)
                snapshot[id] = _target.IsEnabled(id) ? CommandAvailability.Available
                    : CommandAvailability.Disabled(_target.DisabledReason(id));
            _availabilitySnapshot = snapshot;
        }

        /// <summary>Dal thread principale, a ogni frame: sincronizza il campo armato ed esegue il risultato nuovo.</summary>
        public void Pump()
        {
            RefreshAvailabilitySnapshot();
            SyncDictation();
            var state = Controller.State;
            if (HasPending && _pendingSince != default(DateTimeOffset) && _clock() - _pendingSince >= PendingTimeout) CancelPending();
            if (state != PushToTalkState.Result)
            {
                if (state == PushToTalkState.Pressed || state == PushToTalkState.Listening || state == PushToTalkState.Processing)
                {
                    _handledRoute = null; _handledDictation = null;
                    if (HasPending) CancelPending();     // sta parlando di nuovo: la richiesta precedente decade
                }
                return;
            }
            var route = Controller.ProposedCommand;
            if (route != null && !ReferenceEquals(route, _handledRoute))
            {
                _handledRoute = route;
                if (route.Kind == VoiceRouteKind.Rejected && _target is IContextVoiceActions contextual
                    && contextual.TryResolveAction(Controller.Transcript, out var action)) HandleContextAction(action);
                else Handle(route);
                return;
            }
            var dictation = Controller.ProposedDictation;
            if (dictation != null && !ReferenceEquals(dictation, _handledDictation)) { _handledDictation = dictation; HandleDictation(dictation); }
        }

        /// <summary>Allinea DictationTarget al campo armato dalla UI. Va chiamato prima di ogni pressione (Pump lo fa).</summary>
        public void SyncDictation()
        {
            var field = _target.ArmedField;
            if (field == null)
            {
                if (_armed != null || _dictation.IsArmed) { _armed = null; _dictation.Disarm(); }
                return;
            }
            if (_armed != null && _armed.SameAs(field) && _dictation.IsArmed) return;
            _armed = field;
            _dictation.Arm(field.Id, field.Unit, field.Min, field.Max);
        }

        private void Handle(VoiceRouteResult route)
        {
            _lastContextId = null; _lastContextLabel = null;
            ClearPending();
            switch (route.Kind)
            {
                case VoiceRouteKind.Recognized:
                    if (!route.Enabled || !_target.IsEnabled(route.CommandId))   // stato riletto: puo essere cambiato
                    {
                        string why = string.IsNullOrEmpty(route.Reason) ? "Comando non disponibile ora." : route.Reason;
                        Set(VoiceOutcomeKind.NotExecuted, why);
                    }
                    else if (_target.Invoke(route.CommandId)) Set(VoiceOutcomeKind.Executed, "Eseguito.");
                    else Set(VoiceOutcomeKind.NotExecuted, "Comando non eseguito.");
                    break;
                case VoiceRouteKind.ShowApplyConfirmation:
                    _target.ShowApplyConfirmation();
                    Set(VoiceOutcomeKind.ApplyConfirmationShown, route.Reason);
                    break;
                case VoiceRouteKind.NeedsConfirmation:
                    PendingConfirmationCommandId = route.CommandId;
                    PendingConfirmationText = route.Reason;
                    _pendingSince = _clock();
                    Set(VoiceOutcomeKind.AwaitingConfirmation, route.Reason);
                    break;
                default:
                    Set(VoiceOutcomeKind.Rejected, route.Reason);
                    break;
            }
        }

        private void HandleContextAction(ContextVoiceAction action)
        {
            ClearPending();
            _lastContextId = action.Id; _lastContextLabel = action.Label;
            if (!action.Enabled || !_target.IsEnabled(action.Id))
            { Set(VoiceOutcomeKind.NotExecuted, string.IsNullOrEmpty(action.Reply) ? "Comando non disponibile ora." : action.Reply); return; }
            if (action.RequiresConfirmation)
            {
                PendingConfirmationCommandId = action.Id;
                PendingConfirmationText = "Confermare " + action.Label + "?";
                _pendingSince = _clock();
                Set(VoiceOutcomeKind.AwaitingConfirmation, PendingConfirmationText);
                return;
            }
            bool ok = _target.Invoke(action.Id);
            Set(ok ? VoiceOutcomeKind.Executed : VoiceOutcomeKind.NotExecuted, ok ? "Eseguito: " + action.Label + "." : "Comando non eseguito.");
        }

        /// <summary>Vero se serve una conferma fisica (comando in attesa o valore dettato proposto).</summary>
        public bool HasPending => PendingConfirmationCommandId != null || _dictation.Pending != null;

        /// <summary>Etichetta italiana del comando (il nome del pulsante), o l'ID se sconosciuto.</summary>
        public string CommandLabel(string commandId)
        {
            if (commandId == _lastContextId) return _lastContextLabel;
            var aliases = _router.AliasesOf(commandId);
            return aliases.Count > 0 ? aliases[0] : commandId ?? "";
        }

        /// <summary>Pulsante fisico Conferma: comando in attesa (Undo) oppure valore dettato.</summary>
        public bool ConfirmPendingAny() => PendingConfirmationCommandId != null ? ConfirmPending() : ConfirmDictation();

        /// <summary>Pulsante fisico Annulla: scarta qualunque conferma in attesa.</summary>
        public void CancelPendingAny() { CancelPending(); }

        private void CancelPending()
        {
            if (PendingConfirmationCommandId != null) DismissPending();
            if (_dictation.Pending != null) CancelDictation();
        }

        private void HandleDictation(DictationProposal p)
        {
            ClearPending();
            _pendingSince = _clock();
            Set(VoiceOutcomeKind.DictationProposed, p.Accepted ? p.DisplayText + " Conferma per applicare." : p.Reason);
        }

        /// <summary>Conferma fisica (pulsante) del comando in attesa. Rilegge l'abilitazione.</summary>
        public bool ConfirmPending()
        {
            string id = PendingConfirmationCommandId;
            if (id == null) return false;
            ClearPending();
            if (!_target.IsEnabled(id)) { Set(VoiceOutcomeKind.NotExecuted, "Comando non piu disponibile."); return false; }
            bool ok = _target.Invoke(id);
            Set(ok ? VoiceOutcomeKind.Executed : VoiceOutcomeKind.NotExecuted, ok ? "Eseguito." : "Comando non eseguito.");
            return ok;
        }

        public void DismissPending()
        {
            if (PendingConfirmationCommandId == null) return;
            ClearPending();
            Set(VoiceOutcomeKind.None, "");
        }

        /// <summary>Conferma fisica del valore dettato: aggiorna solo il campo armato.</summary>
        public bool ConfirmDictation()
        {
            bool ok = _dictation.Confirm();
            Set(ok ? VoiceOutcomeKind.Executed : VoiceOutcomeKind.NotExecuted, ok ? "Valore applicato." : "Nessun valore da confermare.");
            if (ok) Controller.Dismiss();
            return ok;
        }

        public void CancelDictation()
        {
            _dictation.Cancel();
            Controller.Dismiss();
            Set(VoiceOutcomeKind.None, "");
        }

        /// <summary>Cambio modalita/workspace: chiude il microfono e scarta proposte e conferme.</summary>
        public void NotifyModeChanged() { Reset(VoiceInterruption.ModeChanged); }

        /// <summary>Disconnessione dal PC: come sopra.</summary>
        public void NotifyDisconnected() { Reset(VoiceInterruption.Disconnected); }

        private void Reset(VoiceInterruption reason)
        {
            Controller.Interrupt(reason);
            _dictation.Disarm();
            _armed = null; _handledRoute = null; _handledDictation = null;
            ClearPending();
            Set(VoiceOutcomeKind.None, "");
        }

        private void ClearPending() { PendingConfirmationCommandId = null; PendingConfirmationText = ""; _pendingSince = default(DateTimeOffset); }

        private void Set(VoiceOutcomeKind kind, string text)
        {
            Outcome = kind; OutcomeText = text ?? "";
            OutcomeChanged?.Invoke();
        }

        public void Dispose()
        {
            Controller.Dispose();
        }
    }
}
