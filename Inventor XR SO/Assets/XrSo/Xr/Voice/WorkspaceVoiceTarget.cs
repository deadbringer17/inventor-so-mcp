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
    /// Instrada i comandi vocali al workspace attivo. In M5 solo Lamiera espone la superficie completa;
    /// Design, Assembly e Inspect restano fuori scope (nessun comando vocale): rispondono "non disponibile".
    /// </summary>
    public sealed class WorkspaceVoiceTarget : IVoiceCommandTarget
    {
        public const string UnavailableReason = "Comando non disponibile in questa modalità";

        private readonly IWorkspaceVoiceSurface _lamiera;

        public WorkspaceVoiceTarget(IWorkspaceVoiceSurface lamiera) { _lamiera = lamiera; }

        public static WorkspaceVoiceTarget ForLamiera(LamieraWorkspace lamiera) =>
            new WorkspaceVoiceTarget(new LamieraSurface(lamiera));

        private IWorkspaceVoiceSurface Current => _lamiera != null && _lamiera.Active ? _lamiera : null;

        public bool IsEnabled(string commandId) => Current != null && Current.IsEnabled(commandId);

        public string DisabledReason(string commandId) => IsEnabled(commandId) ? "" : UnavailableReason;

        public bool Invoke(string commandId) => Current != null && Current.Invoke(commandId);

        public DictationField ArmedField => Current?.ArmedField;

        public void SetField(string fieldId, double value) { Current?.SetArmedField(fieldId, value); }

        /// <summary>Mostra il riepilogo del piano nel workspace; il commit resta sul pulsante Applica fisico.</summary>
        public void ShowApplyConfirmation() { Current?.Invoke(CommandIds.Apply); }

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
