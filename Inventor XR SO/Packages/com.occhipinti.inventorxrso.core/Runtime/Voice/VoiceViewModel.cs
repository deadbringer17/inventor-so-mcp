namespace InventorXrSo.Core.Voice
{
    /// <summary>Cosa mostrare nel pannello vocale, calcolato dallo stato (senza Unity). Testo italiano.</summary>
    public sealed class VoiceViewModel
    {
        public bool Visible { get; private set; }
        public string Title { get; private set; } = "";
        public string Transcript { get; private set; } = "";
        public string Command { get; private set; } = "";
        public string Detail { get; private set; } = "";
        public string Error { get; private set; } = "";
        /// <summary>True se il pannello deve offrire i pulsanti fisici Conferma / Annulla.</summary>
        public bool ShowConfirm { get; private set; }
        public string ConfirmLabel { get; private set; } = "Conferma";
        public bool Listening { get; private set; }

        public static VoiceViewModel Build(VoiceCommandBridge bridge)
        {
            var c = bridge.Controller;
            var m = new VoiceViewModel();
            var state = c.State;
            m.ShowConfirm = bridge.HasPending;
            if (state == PushToTalkState.Listening) { m.Visible = true; m.Listening = true; m.Title = "Ascolto…"; return m; }
            if (state == PushToTalkState.Processing) { m.Visible = true; m.Title = "Elaborazione…"; return m; }
            if (state == PushToTalkState.Error)
            {
                m.Visible = true; m.Title = "Voce non disponibile"; m.Error = c.ErrorMessage ?? "";
                return m;
            }
            if (state == PushToTalkState.Result)
            {
                m.Visible = true;
                string text = c.Transcript;
                if (!string.IsNullOrEmpty(text)) m.Transcript = "“" + text + "”";
                var route = c.ProposedCommand;
                if (route != null && !string.IsNullOrEmpty(route.CommandId) && route.Kind != VoiceRouteKind.Rejected)
                    m.Command = "Comando: " + bridge.CommandLabel(route.CommandId);
                m.Title = TitleOf(bridge.Outcome, c.ProposedDictation != null);
                m.Detail = bridge.OutcomeText;
                if (bridge.PendingConfirmationCommandId != null) m.ConfirmLabel = "Conferma " + bridge.CommandLabel(bridge.PendingConfirmationCommandId);
                else if (bridge.Dictation.Pending != null) m.ConfirmLabel = "Applica valore";
                return m;
            }
            // Idle/Pressed: visibile solo se resta una conferma fisica in attesa (es. Undo dopo la fine del risultato)
            if (bridge.HasPending)
            {
                m.Visible = true;
                m.Title = bridge.PendingConfirmationCommandId != null ? "Confermare?" : "Valore proposto";
                m.Detail = bridge.OutcomeText;
                if (bridge.PendingConfirmationCommandId != null) m.ConfirmLabel = "Conferma " + bridge.CommandLabel(bridge.PendingConfirmationCommandId);
                else m.ConfirmLabel = "Applica valore";
            }
            return m;
        }

        private static string TitleOf(VoiceOutcomeKind kind, bool dictation)
        {
            switch (kind)
            {
                case VoiceOutcomeKind.Executed: return "Eseguito";
                case VoiceOutcomeKind.NotExecuted: return "Non eseguito";
                case VoiceOutcomeKind.Rejected: return "Non riconosciuto";
                case VoiceOutcomeKind.ApplyConfirmationShown: return "Conferma il piano";
                case VoiceOutcomeKind.AwaitingConfirmation: return "Confermare?";
                case VoiceOutcomeKind.DictationProposed: return "Valore proposto";
                default: return dictation ? "Valore proposto" : "";
            }
        }
    }
}
