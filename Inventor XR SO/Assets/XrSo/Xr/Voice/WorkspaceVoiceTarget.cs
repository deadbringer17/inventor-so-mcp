using InventorXrSo.Core.Voice;
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

    /// <summary>
    /// Instrada i comandi vocali al workspace attivo (Lamiera, Design, Assembly, altrimenti Ispeziona). Ogni superficie
    /// usa gli stessi percorsi e la stessa abilitazione dei pulsanti; un comando che il workspace attivo non offre
    /// risponde "non disponibile". Fuori sessione (Home) la voce non e accettata: il microfono non si apre.
    /// </summary>
    public sealed class WorkspaceVoiceTarget : IVoiceCommandTarget
    {
        public const string UnavailableReason = "Comando non disponibile in questa modalità";
        public const string IsolateUnavailableReason = "Isola non è ancora disponibile in Ispeziona";

        private readonly IWorkspaceVoiceSurface[] _surfaces;

        /// <summary>Superfici in ordine di priorita: la prima attiva riceve i comandi (nulle ignorate).</summary>
        public WorkspaceVoiceTarget(IWorkspaceVoiceSurface lamiera, IWorkspaceVoiceSurface design = null,
            IWorkspaceVoiceSurface assembly = null, IWorkspaceVoiceSurface inspect = null)
        {
            _surfaces = new[] { lamiera, design, assembly, inspect };
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

        public bool IsEnabled(string commandId) => Current != null && Current.IsEnabled(commandId);

        public string DisabledReason(string commandId)
        {
            if (IsEnabled(commandId)) return "";
            return commandId == CommandIds.Isolate && Current != null ? IsolateUnavailableReason : UnavailableReason;
        }

        public bool Invoke(string commandId) => Current != null && Current.Invoke(commandId);

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
            public DictationField ArmedField => null;   // Assembly usa solo il tastierino modale
            public bool SetArmedField(string fieldId, double value) => false;
        }

        private sealed class InspectSurface : IWorkspaceVoiceSurface
        {
            private readonly InspectWorkspace _ws;
            public InspectSurface(InspectWorkspace ws) { _ws = ws; }
            public bool Active => _ws != null && _ws.VoiceActive;
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
