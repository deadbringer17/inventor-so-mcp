using System;

namespace InventorXrSo.Core.Voice
{
    /// <summary>Valore proposto dalla dettatura, da mostrare con il testo originale prima della conferma.</summary>
    public sealed class DictationProposal
    {
        public bool Accepted { get; }
        public string FieldId { get; }
        public double Value { get; }
        public QuantityUnit Unit { get; }
        /// <summary>Trascrizione originale, mostrata all'utente accanto al valore.</summary>
        public string OriginalText { get; }
        public string Reason { get; }
        public NumberParseError Error { get; }
        /// <summary>True se l'utente non ha pronunciato l'unita e si assume quella del campo.</summary>
        public bool UnitAssumed { get; }
        internal int ArmVersion { get; }

        internal DictationProposal(bool accepted, string fieldId, double value, QuantityUnit unit, string text, string reason,
            NumberParseError error, bool assumed, int armVersion)
        { Accepted = accepted; FieldId = fieldId; Value = value; Unit = unit; OriginalText = text ?? ""; Reason = reason ?? "";
          Error = error; UnitAssumed = assumed; ArmVersion = armVersion; }

        /// <summary>Testo per la UI, es. "12,5 mm (da: dodici virgola cinque millimetri)".</summary>
        public string DisplayText
        {
            get
            {
                if (!Accepted) return Reason;
                string u = Unit == QuantityUnit.Millimeters ? " mm" : Unit == QuantityUnit.Degrees ? " °" : "";
                return Value.ToString("0.######", System.Globalization.CultureInfo.GetCultureInfo("it-IT")) + u + " (da: " + OriginalText + ")";
            }
        }
    }

    /// <summary>
    /// Dettatura numerica: attiva solo con un campo armato (id, unita, min, max). La conferma aggiorna
    /// esclusivamente quel campo, tramite il setter fornito; qualunque altro campo resta intatto.
    /// </summary>
    public sealed class DictationTarget
    {
        private readonly Action<string, double> _setField;
        private int _armVersion;
        private QuantityUnit _unit;
        private double _min, _max;

        public string FieldId { get; private set; }
        public bool IsArmed => FieldId != null;
        public DictationProposal Pending { get; private set; }
        public event Action Changed;

        /// <param name="setField">Applica il valore confermato alla bozza (stesso percorso della tastiera).</param>
        public DictationTarget(Action<string, double> setField)
        { _setField = setField ?? throw new ArgumentNullException(nameof(setField)); }

        public void Arm(string fieldId, QuantityUnit unit, double min, double max)
        {
            if (string.IsNullOrEmpty(fieldId)) throw new ArgumentException("fieldId richiesto", nameof(fieldId));
            if (double.IsNaN(min) || double.IsNaN(max) || min > max) throw new ArgumentException("range non valido");
            FieldId = fieldId; _unit = unit; _min = min; _max = max; _armVersion++; Pending = null;
            Changed?.Invoke();
        }

        public void Disarm()
        {
            if (FieldId == null && Pending == null) return;
            FieldId = null; _armVersion++; Pending = null;
            Changed?.Invoke();
        }

        /// <summary>Interpreta la trascrizione come valore per il campo armato. Non modifica nulla.</summary>
        public DictationProposal Propose(string transcript)
        {
            if (!IsArmed)
            {
                Pending = null;
                return new DictationProposal(false, null, 0, QuantityUnit.None, transcript, "Nessun campo selezionato per la dettatura.",
                    NumberParseError.None, false, _armVersion);
            }
            var q = ItalianNumberParser.Parse(transcript, _min, _max);
            DictationProposal p;
            if (!q.Ok)
                p = new DictationProposal(false, FieldId, 0, _unit, transcript, q.Reason, q.Error, false, _armVersion);
            else if (q.Unit != QuantityUnit.None && q.Unit != _unit)
                p = new DictationProposal(false, FieldId, 0, _unit, transcript,
                    "Unita non compatibile con il campo (" + UnitName(_unit) + ").", NumberParseError.UnitMismatch, false, _armVersion);
            else
                p = new DictationProposal(true, FieldId, q.Value, _unit, transcript, "", NumberParseError.None, q.Unit == QuantityUnit.None, _armVersion);
            Pending = p.Accepted ? p : null;
            Changed?.Invoke();
            return p;
        }

        /// <summary>Conferma la proposta pendente: aggiorna solo il campo armato. Una sola volta.</summary>
        public bool Confirm()
        {
            var p = Pending;
            if (p == null) return false;
            return Confirm(p);
        }

        public bool Confirm(DictationProposal proposal)
        {
            if (proposal == null || !proposal.Accepted || !IsArmed) return false;
            if (!ReferenceEquals(proposal, Pending) || proposal.FieldId != FieldId || proposal.ArmVersion != _armVersion) return false;
            Pending = null;
            _setField(proposal.FieldId, proposal.Value);
            Changed?.Invoke();
            return true;
        }

        public void Cancel()
        {
            if (Pending == null) return;
            Pending = null;
            Changed?.Invoke();
        }

        private static string UnitName(QuantityUnit u) => u == QuantityUnit.Millimeters ? "mm" : u == QuantityUnit.Degrees ? "gradi" : "senza unita";
    }
}
