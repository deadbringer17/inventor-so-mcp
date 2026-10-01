using System;
using System.Globalization;
using InventorXrSo.Core.Voice;

namespace InventorXrSo.Core.Ui
{
    /// <summary>
    /// Il valore di un chip: passi del thumbstick, buffer del tastierino in tavolozza e dettatura.
    /// Cambia solo la bozza del workspace; nel CAD entra con Anteprima e Applica.
    /// </summary>
    public sealed class NumericEntry
    {
        public static readonly double[] Steps = { 0.1, 1, 10 };
        private int _step = 1;

        public NumericEntry(string id, QuantityUnit unit, double value, double min, double max)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id campo vuoto");
            if (double.IsNaN(min) || double.IsNaN(max) || min > max) throw new ArgumentException("intervallo non valido");
            Id = id; Unit = unit; Min = min; Max = max;
            Value = Math.Max(min, Math.Min(max, value));
        }

        public string Id { get; }
        public QuantityUnit Unit { get; }
        public double Min { get; }
        public double Max { get; }
        public double Value { get; private set; }
        public double Step => Steps[_step];
        public bool Editing => Buffer != null;
        public string Buffer { get; private set; }
        public event Action Changed;
        /// <summary>Il tastierino ha confermato un valore (non per trascinamento, passo o dettatura senza tastierino).</summary>
        public event Action Committed;

        public string Display => Editing ? Buffer : Format(Value) + (Unit == QuantityUnit.Degrees ? "°" : Unit == QuantityUnit.Millimeters ? " mm" : "");

        public static string Format(double v) => v.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',');

        public void CycleStep(int direction)
        {
            int next = Math.Max(0, Math.Min(Steps.Length - 1, _step + Math.Sign(direction)));
            if (next == _step) return;
            _step = next;
            Changed?.Invoke();
        }

        /// <summary>Un passo nella direzione data; false se il valore era gia al limite.</summary>
        public bool Nudge(int direction)
        {
            double next = Math.Round(Value + Math.Sign(direction) * Step, 6);
            next = Math.Max(Min, Math.Min(Max, next));
            if (next == Value) return false;
            Value = next;
            Changed?.Invoke();
            return true;
        }

        public void BeginEdit() { Buffer = ""; Changed?.Invoke(); }

        public void Type(char c)
        {
            if (!Editing) return;
            if (c == '-') Buffer = Buffer.StartsWith("-", StringComparison.Ordinal) ? Buffer.Substring(1) : "-" + Buffer;
            else if (c == ',' || c == '.') { if (Buffer.IndexOf(',') >= 0) return; Buffer += ","; }
            else if (c >= '0' && c <= '9') Buffer += c;
            else return;
            Changed?.Invoke();
        }

        public void Backspace()
        {
            if (!Editing || Buffer.Length == 0) return;
            Buffer = Buffer.Substring(0, Buffer.Length - 1);
            Changed?.Invoke();
        }

        public bool Commit(out string reason)
        {
            reason = "";
            if (!Editing) return false;
            var parsed = ItalianNumberParser.Parse(Buffer, Min, Max);
            if (!parsed.Ok) { reason = parsed.Reason; return false; }
            Value = parsed.Value;
            Buffer = null;
            Changed?.Invoke();
            Committed?.Invoke();
            return true;
        }

        /// <summary>Dettatura su un tastierino aperto: valida come SetValue e conferma al posto del tasto OK.</summary>
        public bool CommitValue(double value, out string reason)
        {
            if (!SetValue(value, out reason)) return false;
            Buffer = null;
            Changed?.Invoke();
            Committed?.Invoke();
            return true;
        }

        public void CancelEdit()
        {
            if (!Editing) return;
            Buffer = null;
            Changed?.Invoke();
        }

        /// <summary>Stesso controllo del tastierino, per dettatura e trascinamento.</summary>
        public bool SetValue(double value, out string reason)
        {
            reason = "";
            if (double.IsNaN(value) || double.IsInfinity(value) || value < Min || value > Max)
            {
                reason = $"Fuori intervallo {Format(Min)}–{Format(Max)}.";
                return false;
            }
            if (value == Value) return true;
            Value = value;
            Changed?.Invoke();
            return true;
        }
    }
}
