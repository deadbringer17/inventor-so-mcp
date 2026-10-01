using System;
using System.Linq;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Ui;
using UnityEngine;

namespace InventorXrSo.Xr.Voice
{
    /// <summary>Superficie vocale di un workspace: stessi percorsi dei pulsanti, nessuna via propria.</summary>
    public interface IWorkspaceVoiceSurface
    {
        bool Active { get; }
        bool IsEnabled(string commandId);
        bool Invoke(string commandId);
        DictationField ArmedField { get; }
        bool SetArmedField(string fieldId, double value);
    }

    public interface IWorkspacePanelVoiceSurface
    {
        HomePanel VoicePanel { get; }
    }
    /// <summary>Workspace senza pannello (M6): le etichette vocali vengono dalle azioni del catalogo.</summary>
    public interface IWorkspaceActionVoiceSurface
    {
        System.Collections.Generic.IEnumerable<(string label, bool enabled)> VoiceActions { get; }
        bool InvokeVoiceAction(string label);
    }
    public interface IWorkspaceWristVoiceSurface
    {
        bool IsWristEnabled(string label);
        bool InvokeWrist(string label);
    }

    /// <summary>
    /// Instrada i comandi vocali al workspace attivo (Lamiera, Progettazione, Assieme, altrimenti Ispeziona). Ogni superficie
    /// usa gli stessi percorsi e la stessa abilitazione dei pulsanti; un comando che il workspace attivo non offre
    /// risponde "non disponibile". Fuori sessione (Home) la voce non e accettata: il microfono non si apre.
    /// </summary>
    public sealed class WorkspaceVoiceTarget : IVoiceCommandTarget, IContextVoiceActions
    {
        public const string UnavailableReason = "Comando non disponibile in questa modalità";
        public const string IsolateUnavailableReason = "Isola non è ancora disponibile in Ispeziona";

        private readonly IWorkspaceVoiceSurface[] _surfaces;
        private readonly IWorkspaceWristVoiceSurface _wrist;

        /// <summary>Superfici in ordine di priorita: la prima attiva riceve i comandi (nulle ignorate).</summary>
        public WorkspaceVoiceTarget(IWorkspaceVoiceSurface lamiera, IWorkspaceVoiceSurface design = null,
            IWorkspaceVoiceSurface assembly = null, IWorkspaceVoiceSurface inspect = null)
        {
            _surfaces = new[] { lamiera, design, assembly, inspect };
            _wrist = inspect as IWorkspaceWristVoiceSurface;
        }

        public static WorkspaceVoiceTarget ForLamiera(LamieraWorkspace lamiera) =>
            new WorkspaceVoiceTarget(new LamieraSurface(lamiera));

        public static WorkspaceVoiceTarget ForWorkspaces(LamieraWorkspace lamiera, DesignWorkspace design,
            AssemblyWorkspace assembly, InspectWorkspace inspect) =>
            new WorkspaceVoiceTarget(new LamieraSurface(lamiera), new DesignSurface(design),
                new AssemblySurface(assembly), new InspectSurface(inspect));

        /// <summary>Vero solo con una sessione aperta (fuori dalla Home). Impostato da AppController.</summary>
        public bool InSession { get; set; }

        public bool AcceptsVoice => InSession;

        private IWorkspaceVoiceSurface Current
        {
            get
            {
                if (!InSession) return null;
                foreach (var surface in _surfaces) if (surface != null && surface.Active) return surface;
                return null;
            }
        }

        private const string UiPrefix = "ui:";
        private const string WristPrefix = "wrist:";
        private HomePanel CurrentPanel => (Current as IWorkspacePanelVoiceSurface)?.VoicePanel;
        private IWorkspaceActionVoiceSurface CurrentActions => Current as IWorkspaceActionVoiceSurface;

        /// <summary>Etichette dei comandi a schermo: pannello uGUI se c'e, altrimenti le azioni del catalogo.</summary>
        private System.Collections.Generic.IEnumerable<(string label, bool enabled)> LabelActions(HomePanel panel) =>
            panel != null ? panel.VoiceActions : CurrentActions?.VoiceActions;

        public bool TryResolveAction(string transcript, out ContextVoiceAction action)
        {
            action = null;
            var panel = CurrentPanel;
            string spoken = ItalianTextNormalizer.Normalize(transcript);
            if (spoken.Length == 0) return false;
            if (TryResolveExact(spoken, panel, out action)) return true;
            foreach (var prefix in new[] { "premi ", "apri ", "seleziona ", "mostra " })
                if (spoken.StartsWith(prefix, StringComparison.Ordinal))
                    return TryResolveExact(spoken.Substring(prefix.Length), panel, out action);
            return false;
        }

        private bool TryResolveExact(string spoken, HomePanel panel, out ContextVoiceAction action)
        {
            action = null;
            foreach (var mode in new[] { "Ispeziona", "Esplora", "Progettazione", "Assieme", "Lamiera" })
                if (Matches(spoken, mode) && _wrist?.IsWristEnabled(mode) == true)
                { action = new ContextVoiceAction(WristPrefix + mode, mode, true, false); return true; }
            var labels = LabelActions(panel);
            if (labels == null) return false;
            var matches = labels.Where(item => Matches(spoken, item.label)).ToArray();
            if (matches.Length != 1) return false; // etichette omonime: serve il puntatore
            var chosen = matches[0];
            string normalized = ItalianTextNormalizer.Normalize(chosen.label);
            if (normalized == "applica" || normalized.StartsWith("applica ", StringComparison.Ordinal)) return false;
            bool confirm = normalized.StartsWith("annulla modifica", StringComparison.Ordinal)
                || normalized.StartsWith("ripeti modifica", StringComparison.Ordinal)
                || normalized == "ho controllato il cad" || normalized == "attiva questo assieme";
            action = new ContextVoiceAction(UiPrefix + chosen.label, chosen.label, chosen.enabled, confirm);
            return true;
        }

        private static bool Matches(string spoken, string label)
        {
            string full = ItalianTextNormalizer.Normalize(label);
            if (spoken == full) return true;
            // Sinonimi controllati: si applicano solo ai pulsanti presenti nel pannello.
            // Se due pulsanti coincidono, TryResolveAction rifiuta il comando ambiguo.
            string key = full.StartsWith("estrudi schizzo", StringComparison.Ordinal) ? "estrusione" : full;
            if (key == "estrusione" && (spoken == "estrusione" || spoken == "estrudi" || spoken == "crea estrusione" || spoken == "fai estrusione")) return true;
            if (key == "foro" && (spoken == "fora" || spoken == "crea foro")) return true;
            if (key == "raccordo" && spoken == "raccorda") return true;
            if (key == "smusso" && spoken == "smussa") return true;
            if (key == "misura" && spoken == "misurazione") return true;
            if (key == "sezione" && spoken == "seziona") return true;
            if (key == "crea schizzo" && spoken == "nuovo schizzo") return true;
            if (key == "crea sviluppo" && spoken == "sviluppa") return true;
            if (key == "mostra sviluppo" && spoken == "visualizza sviluppo") return true;
            if (key == "faccia da schizzo" && spoken == "crea faccia da schizzo") return true;
            if (key == "taglio da schizzo" && spoken == "taglia da schizzo") return true;
            if (key == "progettazione" && spoken == "progetta") return true;
            if (key == "assieme" && spoken == "assemblaggio") return true;
            if (key == "sposta componente" && (spoken == "sposta" || spoken == "muovi componente")) return true;
            if (key == "vincolo" && spoken == "vincola") return true;
            if (key == "accoppia" && spoken == "accoppiamento") return true;
            if (key == "allinea" && spoken == "allineamento") return true;
            if (key == "giunto" && spoken == "collega") return true;
            if (key == "esplora" && (spoken == "documenti" || spoken == "browser")) return true;
            if (key == "annulla modifica xr" && spoken == "annulla ultima modifica") return true;
            if (key == "ripeti modifica xr" && spoken == "ripeti ultima modifica") return true;
            int colon = label.IndexOf(':');
            if (colon > 0 && spoken == ItalianTextNormalizer.Normalize(label.Substring(0, colon))) return true;
            int detail = label.IndexOf('•');
            if (detail > 0 && spoken == ItalianTextNormalizer.Normalize(label.Substring(0, detail))) return true;
            int digit = full.IndexOfAny("0123456789".ToCharArray());
            return digit > 0 && spoken == full.Substring(0, digit).TrimEnd();
        }

        public bool IsEnabled(string commandId)
        {
            if (commandId != null && commandId.StartsWith(WristPrefix, StringComparison.Ordinal))
                return _wrist?.IsWristEnabled(commandId.Substring(WristPrefix.Length)) == true;
            if (commandId != null && commandId.StartsWith(UiPrefix, StringComparison.Ordinal))
            {
                var label = commandId.Substring(UiPrefix.Length);
                if (ItalianTextNormalizer.Normalize(label) == "applica") return false;
                return LabelActions(CurrentPanel)?.Any(item => item.label == label && item.enabled) == true;
            }
            return Current != null && Current.IsEnabled(commandId);
        }

        public string DisabledReason(string commandId)
        {
            if (IsEnabled(commandId)) return "";
            return commandId == CommandIds.Isolate && Current != null ? IsolateUnavailableReason : UnavailableReason;
        }

        public bool Invoke(string commandId)
        {
            if (commandId != null && commandId.StartsWith(WristPrefix, StringComparison.Ordinal))
                return _wrist?.InvokeWrist(commandId.Substring(WristPrefix.Length)) == true;
            if (commandId != null && commandId.StartsWith(UiPrefix, StringComparison.Ordinal))
            {
                if (!IsEnabled(commandId)) return false;
                string label = commandId.Substring(UiPrefix.Length);
                var panel = CurrentPanel;
                return panel != null ? panel.InvokeVoiceAction(label) : CurrentActions?.InvokeVoiceAction(label) == true;
            }
            return Current != null && Current.Invoke(commandId);
        }

        private const string UiNumberPrefix = "ui-number:";
        public DictationField ArmedField
        {
            get
            {
                var native = Current?.ArmedField;
                if (native != null) return native;
                var panel = CurrentPanel;
                if (panel?.HasVoiceNumericPrompt != true) return null;
                var unit = panel.VoiceNumericUnit == "deg" ? QuantityUnit.Degrees
                    : panel.VoiceNumericUnit == "m" ? QuantityUnit.Meters : QuantityUnit.Millimeters;
                return new DictationField(UiNumberPrefix + panel.VoiceNumericId, unit,
                    panel.VoiceNumericMin, panel.VoiceNumericMax);
            }
        }

        public void SetField(string fieldId, double value)
        {
            if (fieldId != null && fieldId.StartsWith(UiNumberPrefix, StringComparison.Ordinal))
            {
                var panel = CurrentPanel;
                if (panel?.VoiceNumericId == fieldId.Substring(UiNumberPrefix.Length)) panel.SubmitVoiceNumber(value);
                return;
            }
            Current?.SetArmedField(fieldId, value);
        }

        /// <summary>Mostra il riepilogo del piano nel workspace; il commit resta sul pulsante Applica fisico.</summary>
        public void ShowApplyConfirmation() { Current?.Invoke(CommandIds.Apply); }

        private sealed class DesignSurface : IWorkspaceVoiceSurface, IWorkspacePanelVoiceSurface, IWorkspaceActionVoiceSurface
        {
            private readonly DesignWorkspace _ws;
            public DesignSurface(DesignWorkspace ws) { _ws = ws; }
            public bool Active => _ws != null && _ws.Active;
            public HomePanel VoicePanel => _ws?.VoicePanel;   // null: Progettazione non ha piu il pannello
            public System.Collections.Generic.IEnumerable<(string label, bool enabled)> VoiceActions => _ws.VoiceActions;
            public bool InvokeVoiceAction(string label) => _ws.InvokeVoiceAction(label);
            public bool IsEnabled(string commandId) => _ws.IsEnabled(commandId);
            public bool Invoke(string commandId) => _ws.Invoke(commandId);
            public DictationField ArmedField => _ws.ArmedField;
            public bool SetArmedField(string fieldId, double value) => _ws.SetArmedField(fieldId, value);
        }

        private sealed class AssemblySurface : IWorkspaceVoiceSurface, IWorkspacePanelVoiceSurface
        {
            private readonly AssemblyWorkspace _ws;
            public AssemblySurface(AssemblyWorkspace ws) { _ws = ws; }
            public bool Active => _ws != null && _ws.Active;
            public HomePanel VoicePanel => _ws?.VoicePanel;
            public bool IsEnabled(string commandId) => _ws.IsEnabled(commandId);
            public bool Invoke(string commandId) => _ws.Invoke(commandId);
            public DictationField ArmedField => null;   // Assembly usa solo il tastierino modale
            public bool SetArmedField(string fieldId, double value) => false;
        }

        private sealed class InspectSurface : IWorkspaceVoiceSurface, IWorkspacePanelVoiceSurface, IWorkspaceWristVoiceSurface
        {
            private readonly InspectWorkspace _ws;
            public InspectSurface(InspectWorkspace ws) { _ws = ws; }
            public bool Active => _ws != null && _ws.VoiceActive;
            public HomePanel VoicePanel => _ws?.VoicePanel;
            public bool IsWristEnabled(string label) => _ws != null && _ws.VoiceWristEnabled(label);
            public bool InvokeWrist(string label) => _ws != null && _ws.InvokeVoiceWrist(label);
            public bool IsEnabled(string commandId) => _ws.IsEnabled(commandId);
            public bool Invoke(string commandId) => _ws.Invoke(commandId);
            public DictationField ArmedField => null;
            public bool SetArmedField(string fieldId, double value) => false;
        }

        private sealed class LamieraSurface : IWorkspaceVoiceSurface, IWorkspacePanelVoiceSurface
        {
            private readonly LamieraWorkspace _ws;
            public LamieraSurface(LamieraWorkspace ws) { _ws = ws; }
            public bool Active => _ws != null && _ws.Active;
            public HomePanel VoicePanel => _ws?.VoicePanel;
            public bool IsEnabled(string commandId) => _ws.IsEnabled(commandId);
            public bool Invoke(string commandId) => _ws.Invoke(commandId);
            public bool SetArmedField(string fieldId, double value) => _ws.SetArmedField(fieldId, value);
            public DictationField ArmedField
            {
                get
                {
                    var f = _ws.ArmedField;   // stesso enum QuantityUnit (Core.Voice) su entrambi i lati
                    return f == null ? null : new DictationField(f.Id, f.Unit, f.Min, f.Max);
                }
            }
        }
    }
}
