using System;
using System.Linq;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;

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
        /// <summary>M9 «apri &lt;componente&gt;»: ricerca per nome senza eseguire. Solo l'Assieme la offre.</summary>
        VoiceOpenResult FindOccurrence(string spokenName) => VoiceOpenResult.UnavailableHere;
        /// <summary>M9 «apri &lt;componente&gt;»: seleziona ed entra come il doppio Trigger.</summary>
        VoiceOpenResult OpenOccurrence(string spokenName) => VoiceOpenResult.UnavailableHere;
    }

    /// <summary>
    /// Instrada i comandi vocali al workspace attivo (Lamiera, Progettazione, Assieme, altrimenti Ispeziona). Il vocabolario
    /// fisso passa dalle superfici (stessa abilitazione dei pulsanti); le etichette a schermo (premi X) si risolvono dal
    /// <see cref="ActionCatalog"/> (<see cref="ActionCatalog.ResolveVoice"/>): nessun workspace ha piu un pannello da scandire.
    /// Un comando che il workspace attivo non offre risponde "non disponibile". Fuori sessione (Home) la voce non e accettata.
    /// </summary>
    public sealed class WorkspaceVoiceTarget : IVoiceCommandTarget, IContextVoiceActions
    {
        public const string UnavailableReason = "Comando non disponibile in questa modalità";
        public const string IsolateUnavailableReason = "Isola non è ancora disponibile in Ispeziona";

        private readonly IWorkspaceVoiceSurface[] _surfaces;

        /// <summary>Superfici in ordine di priorita: la prima attiva riceve i comandi (nulle ignorate).</summary>
        public WorkspaceVoiceTarget(IWorkspaceVoiceSurface lamiera, IWorkspaceVoiceSurface design = null,
            IWorkspaceVoiceSurface assembly = null, IWorkspaceVoiceSurface inspect = null, ActionCatalog catalog = null)
        {
            _surfaces = new[] { lamiera, design, assembly, inspect };
            Catalog = catalog;
        }

        public static WorkspaceVoiceTarget ForLamiera(LamieraWorkspace lamiera, ActionCatalog catalog = null) =>
            new WorkspaceVoiceTarget(new LamieraSurface(lamiera), catalog: catalog);

        public static WorkspaceVoiceTarget ForWorkspaces(LamieraWorkspace lamiera, DesignWorkspace design,
            AssemblyWorkspace assembly, InspectWorkspace inspect, ActionCatalog catalog = null) =>
            new WorkspaceVoiceTarget(new LamieraSurface(lamiera), new DesignSurface(design),
                new AssemblySurface(assembly), new InspectSurface(inspect), catalog);

        /// <summary>Catalogo delle azioni: palette, anello, voce e runner passano tutti da qui.</summary>
        public ActionCatalog Catalog { get; set; }

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

        private const string ActionPrefix = "act:";
        private const string OpenPrefix = "open:";
        private const string ReplyPrefix = "reply:";

        public bool TryResolveAction(string transcript, out ContextVoiceAction action)
        {
            action = null;
            string spoken = ItalianTextNormalizer.Normalize(transcript);
            if (spoken.Length == 0 || Catalog == null) return false;
            if (TryResolveExact(spoken, out action)) return true;
            foreach (var prefix in new[] { "premi ", "seleziona ", "mostra " })
                if (spoken.StartsWith(prefix, StringComparison.Ordinal))
                    return TryResolveExact(spoken.Substring(prefix.Length), out action);
            if (spoken.StartsWith("apri ", StringComparison.Ordinal))
            {
                string name = spoken.Substring(5).Trim();
                if (TryResolveExact(name, out action)) return true;       // «apri contesto»: azione dichiarata
                return TryResolveOpen(name, out action);
            }
            return false;
        }

        /// <summary>«apri &lt;componente&gt;» (M9): solo in Assieme; altrove, ignoto o ambiguo risponde senza eseguire.</summary>
        private bool TryResolveOpen(string name, out ContextVoiceAction action)
        {
            var found = Current?.FindOccurrence(name) ?? VoiceOpenResult.UnavailableHere;
            action = found.Found
                ? new ContextVoiceAction(OpenPrefix + name, "Apri " + name, true, false)
                : new ContextVoiceAction(ReplyPrefix + name, "Apri " + name, false, false, found.Message);
            return true;
        }

        private bool TryResolveExact(string spoken, out ContextVoiceAction action)
        {
            action = null;
            var match = Catalog.ResolveVoice(spoken);
            if (match.Kind != VoiceMatchKind.Ok && match.Kind != VoiceMatchKind.Disabled) return false;   // ambigua o ignota: serve il puntatore
            var chosen = match.Action;
            if (!chosen.VoiceInvokes) return false;                                                         // Applica solo fisico (M5-11)
            string normalized = ItalianTextNormalizer.Normalize(chosen.Label);
            bool confirm = normalized.StartsWith("annulla modifica", StringComparison.Ordinal)
                || normalized.StartsWith("ripeti modifica", StringComparison.Ordinal)
                || normalized == "ho controllato il cad" || normalized == "attiva questo assieme";
            action = new ContextVoiceAction(ActionPrefix + chosen.Id, chosen.Label, match.Kind == VoiceMatchKind.Ok, confirm);
            return true;
        }

        private XrAction CatalogAction(string commandId)
        {
            if (!InSession || Catalog == null || commandId == null || !commandId.StartsWith(ActionPrefix, StringComparison.Ordinal)) return null;
            return Catalog.Find(commandId.Substring(ActionPrefix.Length));
        }

        public bool IsEnabled(string commandId)
        {
            if (commandId != null && commandId.StartsWith(OpenPrefix, StringComparison.Ordinal))
                return InSession && Current != null && Current.FindOccurrence(commandId.Substring(OpenPrefix.Length)).Found;
            if (commandId != null && commandId.StartsWith(ReplyPrefix, StringComparison.Ordinal)) return false;
            if (commandId != null && commandId.StartsWith(ActionPrefix, StringComparison.Ordinal))
            {
                var action = CatalogAction(commandId);
                return action != null && action.VoiceInvokes && action.Enabled;
            }
            return Current != null && Current.IsEnabled(commandId);
        }

        public string DisabledReason(string commandId)
        {
            if (IsEnabled(commandId)) return "";
            if (commandId != null && commandId.StartsWith(OpenPrefix, StringComparison.Ordinal))
                return (Current?.FindOccurrence(commandId.Substring(OpenPrefix.Length)) ?? VoiceOpenResult.UnavailableHere).Message;
            var action = CatalogAction(commandId);
            if (action != null) return string.IsNullOrEmpty(action.DisabledReason) ? UnavailableReason : action.DisabledReason;
            return commandId == CommandIds.Isolate && Current != null ? IsolateUnavailableReason : UnavailableReason;
        }

        public bool Invoke(string commandId)
        {
            if (commandId != null && commandId.StartsWith(OpenPrefix, StringComparison.Ordinal))
                return InSession && Current != null && Current.OpenOccurrence(commandId.Substring(OpenPrefix.Length)).Found;
            if (commandId != null && commandId.StartsWith(ReplyPrefix, StringComparison.Ordinal)) return false;
            if (commandId != null && commandId.StartsWith(ActionPrefix, StringComparison.Ordinal))
            {
                var action = CatalogAction(commandId);
                return action != null && action.VoiceInvokes && action.TryInvoke();
            }
            return Current != null && Current.Invoke(commandId);
        }

        public DictationField ArmedField => Current?.ArmedField;

        public void SetField(string fieldId, double value) { Current?.SetArmedField(fieldId, value); }

        /// <summary>Mostra il riepilogo del piano nel workspace; il commit resta sul pulsante Applica fisico.</summary>
        public void ShowApplyConfirmation() { Current?.Invoke(CommandIds.Apply); }

        private sealed class DesignSurface : IWorkspaceVoiceSurface
        {
            private readonly DesignWorkspace _ws;
            public DesignSurface(DesignWorkspace ws) { _ws = ws; }
            public bool Active => _ws != null && _ws.Active;
            public bool IsEnabled(string commandId) => _ws.IsEnabled(commandId);
            public bool Invoke(string commandId) => _ws.Invoke(commandId);
            public DictationField ArmedField => _ws.ArmedField;
            public bool SetArmedField(string fieldId, double value) => _ws.SetArmedField(fieldId, value);
        }

        private sealed class AssemblySurface : IWorkspaceVoiceSurface
        {
            private readonly AssemblyWorkspace _ws;
            public AssemblySurface(AssemblyWorkspace ws) { _ws = ws; }
            public bool Active => _ws != null && _ws.Active;
            public bool IsEnabled(string commandId) => _ws.IsEnabled(commandId);
            public bool Invoke(string commandId) => _ws.Invoke(commandId);
            public bool SetArmedField(string fieldId, double value) => _ws.SetArmedField(fieldId, value);
            public VoiceOpenResult FindOccurrence(string spokenName) => _ws.FindOccurrenceByName(spokenName);
            public VoiceOpenResult OpenOccurrence(string spokenName) => _ws.OpenByName(spokenName);
            public DictationField ArmedField
            {
                get
                {
                    var f = _ws.ArmedField;
                    return f == null ? null : new DictationField(f.Id, f.Unit, f.Min, f.Max);
                }
            }
        }

        private sealed class InspectSurface : IWorkspaceVoiceSurface
        {
            private readonly InspectWorkspace _ws;
            public InspectSurface(InspectWorkspace ws) { _ws = ws; }
            public bool Active => _ws != null && _ws.Active;
            public bool IsEnabled(string commandId) => _ws.IsEnabled(commandId);
            public bool Invoke(string commandId) => _ws.Invoke(commandId);
            public DictationField ArmedField => null;
            public bool SetArmedField(string fieldId, double value) => false;
        }

        private sealed class LamieraSurface : IWorkspaceVoiceSurface
        {
            private readonly LamieraWorkspace _ws;
            public LamieraSurface(LamieraWorkspace ws) { _ws = ws; }
            public bool Active => _ws != null && _ws.Active;
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
