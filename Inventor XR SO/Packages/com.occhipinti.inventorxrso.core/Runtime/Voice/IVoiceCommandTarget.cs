namespace InventorXrSo.Core.Voice
{
    /// <summary>Campo numerico armato per la dettatura: id, unita e intervallo ammesso.</summary>
    public sealed class DictationField
    {
        public DictationField(string id, QuantityUnit unit, double min, double max)
        { Id = id; Unit = unit; Min = min; Max = max; }

        public string Id { get; }
        public QuantityUnit Unit { get; }
        public double Min { get; }
        public double Max { get; }

        public bool SameAs(DictationField other) =>
            other != null && other.Id == Id && other.Unit == Unit && other.Min == Min && other.Max == Max;
    }

    /// <summary>
    /// La UI (workspace) implementa questa interfaccia: la voce usa esattamente gli stessi percorsi dei
    /// pulsanti. Tutti i metodi sono chiamati dal thread principale (dal Pump del bridge).
    /// </summary>
    public interface IVoiceCommandTarget
    {
        /// <summary>Stessa abilitazione del pulsante manuale.</summary>
        bool IsEnabled(string commandId);

        /// <summary>Motivo italiano per cui il comando non e disponibile; vuoto se non noto.</summary>
        string DisabledReason(string commandId);

        /// <summary>Equivale a premere il pulsante. False se non e stato eseguito.</summary>
        bool Invoke(string commandId);

        /// <summary>Campo numerico attualmente armato per la dettatura, o null.</summary>
        DictationField ArmedField { get; }

        /// <summary>Applica il valore confermato al campo (stesso percorso della tastiera).</summary>
        void SetField(string fieldId, double value);

        /// <summary>Mostra la conferma del piano: il commit resta sul pulsante Applica fisico.</summary>
        void ShowApplyConfirmation();
    }
}
