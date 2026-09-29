using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace InventorXrSo.Core.Voice
{
    public enum QuantityUnit { None, Millimeters, Degrees }

    public enum NumberParseError
    {
        None, Empty, NoNumber, NotANumber, BadFraction, MultipleDecimalSeparators, NoIntegerPart,
        AmbiguousUnit, AmbiguousSeparator, OutOfRange, NotFinite, UnitMismatch,
    }

    /// <summary>Esito del parsing di una quantita: valore in mm o gradi piu unita dichiarata.</summary>
    public sealed class QuantityParseResult
    {
        public bool Ok { get; }
        public double Value { get; }
        /// <summary>Unita pronunciata; None se l'utente non ne ha detta nessuna.</summary>
        public QuantityUnit Unit { get; }
        public NumberParseError Error { get; }
        /// <summary>Messaggio italiano per la UI (vuoto se Ok).</summary>
        public string Reason { get; }

        private QuantityParseResult(bool ok, double value, QuantityUnit unit, NumberParseError error, string reason)
        { Ok = ok; Value = value; Unit = unit; Error = error; Reason = reason ?? string.Empty; }

        internal static QuantityParseResult Success(double v, QuantityUnit u) => new QuantityParseResult(true, v, u, NumberParseError.None, "");
        internal static QuantityParseResult Fail(NumberParseError e, string reason) => new QuantityParseResult(false, 0, QuantityUnit.None, e, reason);
    }

    /// <summary>
    /// Parser di numeri italiani (parole o cifre) con segno e unita mm/gradi. Nessuna conversione
    /// silenziosa: unita ambigue (centimetri, pollici, ...) e separatori ambigui sono rifiutati.
    /// </summary>
    public static class ItalianNumberParser
    {
        public const double DefaultMaxAbs = 100000.0;
        private const int MaxTokens = 16, MaxTokenLength = 40, MaxFractionDigits = 9;

        private static readonly Dictionary<string, int> Units = new Dictionary<string, int>
        {
            {"zero",0},{"uno",1},{"un",1},{"due",2},{"tre",3},{"quattro",4},{"cinque",5},{"sei",6},{"sette",7},{"otto",8},{"nove",9},
        };
        private static readonly Dictionary<string, int> Teens = new Dictionary<string, int>
        {
            {"dieci",10},{"undici",11},{"dodici",12},{"tredici",13},{"quattordici",14},{"quindici",15},{"sedici",16},
            {"diciassette",17},{"diciotto",18},{"diciannove",19},
        };
        private static readonly Dictionary<string, int> Tens = new Dictionary<string, int>
        {
            {"venti",20},{"trenta",30},{"quaranta",40},{"cinquanta",50},{"sessanta",60},{"settanta",70},{"ottanta",80},{"novanta",90},
        };
        // Radici elise davanti a "uno"/"otto": ventuno, trentotto, ...
        private static readonly Dictionary<string, int> Stems = new Dictionary<string, int>
        {
            {"vent",20},{"trent",30},{"quarant",40},{"cinquant",50},{"sessant",60},{"settant",70},{"ottant",80},{"novant",90},
        };

        private enum Cat { Unit, Teen, Tens, Stem, Hundred, HStem, Mille, Mila }
        private static readonly Dictionary<string, KeyValuePair<Cat, int>> Atoms = BuildAtoms();
        private static readonly string[] AtomKeys = BuildKeys();

        private static Dictionary<string, KeyValuePair<Cat, int>> BuildAtoms()
        {
            var d = new Dictionary<string, KeyValuePair<Cat, int>>();
            foreach (var kv in Units) d[kv.Key] = new KeyValuePair<Cat, int>(Cat.Unit, kv.Value);
            foreach (var kv in Teens) d[kv.Key] = new KeyValuePair<Cat, int>(Cat.Teen, kv.Value);
            foreach (var kv in Tens) d[kv.Key] = new KeyValuePair<Cat, int>(Cat.Tens, kv.Value);
            foreach (var kv in Stems) d[kv.Key] = new KeyValuePair<Cat, int>(Cat.Stem, kv.Value);
            d["cento"] = new KeyValuePair<Cat, int>(Cat.Hundred, 100);
            d["cent"] = new KeyValuePair<Cat, int>(Cat.HStem, 100);
            d["mille"] = new KeyValuePair<Cat, int>(Cat.Mille, 1000);
            d["mila"] = new KeyValuePair<Cat, int>(Cat.Mila, 1000);
            return d;
        }

        private static string[] BuildKeys()
        {
            var keys = new List<string>(Atoms.Keys);
            keys.Sort((a, b) => b.Length != a.Length ? b.Length.CompareTo(a.Length) : string.CompareOrdinal(a, b));
            return keys.ToArray();
        }

        private static readonly Dictionary<string, QuantityUnit> UnitWords = new Dictionary<string, QuantityUnit>
        {
            {"millimetro",QuantityUnit.Millimeters},{"millimetri",QuantityUnit.Millimeters},{"mm",QuantityUnit.Millimeters},
            {"grado",QuantityUnit.Degrees},{"gradi",QuantityUnit.Degrees},{"deg",QuantityUnit.Degrees},
        };
        private static readonly HashSet<string> AmbiguousUnits = new HashSet<string>
        {
            "centimetro","centimetri","cm","metro","metri","m","pollice","pollici","micron","micrometri","radianti","radiante","rad",
            "decimetri","dm","chilometri","km","piedi","ft","inch",
        };
        private static readonly HashSet<string> BigWords = new HashSet<string> { "milione", "milioni", "miliardo", "miliardi" };

        public static QuantityParseResult Parse(string text) => Parse(text, -DefaultMaxAbs, DefaultMaxAbs);

        /// <summary>Interpreta "meno dodici virgola cinque millimetri" o "-12,5 mm"; il range arriva dal campo armato.</summary>
        public static QuantityParseResult Parse(string text, double min, double max)
        {
            if (double.IsNaN(min) || double.IsNaN(max) || min > max) throw new ArgumentException("range non valido");
            if (string.IsNullOrWhiteSpace(text)) return Fail(NumberParseError.Empty, "Nessun valore pronunciato.");

            string s = ItalianTextNormalizer.StripAccents(text.ToLowerInvariant()).Replace('−', '-').Replace('–', '-');
            if (Regex.IsMatch(s, @"\d\.\d+\.\d") || Regex.IsMatch(s, @"\d,\d+,\d") || Regex.IsMatch(s, @"(?<![\d.,])[1-9]\d{0,2}\.\d{3}(?!\d)"))
                return Fail(NumberParseError.AmbiguousSeparator, "Separatore ambiguo (punto delle migliaia o decimale?): usa la virgola per i decimali.");
            s = Regex.Replace(s, @"(?<=\d)[,.](?=\d)", " virgola ");
            s = Regex.Replace(s, @"^\s*-\s*", "meno ");
            s = Regex.Replace(s, @"(?<=\s)-\s*(?=\d)", "meno ");
            s = Regex.Replace(s, @"^\s*\+\s*", "piu ");
            s = s.Replace("°", " gradi ");
            s = Regex.Replace(s, "(?<=\\d)\\s*[\"″]", " pollici ");
            s = Regex.Replace(s, @"(?<=\d)(?=[a-z])", " ");
            s = Regex.Replace(s, @"[^a-z0-9\s]", " ");
            var tokens = new List<string>(s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            if (tokens.Count == 0) return Fail(NumberParseError.Empty, "Nessun valore pronunciato.");
            if (tokens.Count > MaxTokens) return Fail(NumberParseError.NotANumber, "Non ho capito il numero.");

            int sign = 1;
            if (tokens[0] == "meno") { sign = -1; tokens.RemoveAt(0); }
            else if (tokens[0] == "piu") tokens.RemoveAt(0);

            var unit = QuantityUnit.None;
            if (tokens.Count > 0)
            {
                string last = tokens[tokens.Count - 1];
                if (UnitWords.TryGetValue(last, out var u)) { unit = u; tokens.RemoveAt(tokens.Count - 1); }
                else if (AmbiguousUnits.Contains(last))
                    return Fail(NumberParseError.AmbiguousUnit, "Unita ambigua (" + last + "): dire millimetri o gradi. Nessuna conversione automatica.");
            }
            if (tokens.Count == 0) return Fail(NumberParseError.NoNumber, "Manca il numero.");

            foreach (var t in tokens)
                if (BigWords.Contains(t)) return Fail(NumberParseError.OutOfRange, "Valore fuori intervallo.");

            int comma = 0, commaAt = -1;
            for (int i = 0; i < tokens.Count; i++) if (tokens[i] == "virgola") { comma++; commaAt = i; }
            if (comma > 1) return Fail(NumberParseError.MultipleDecimalSeparators, "Piu di una virgola decimale.");
            List<string> intTokens, fracTokens = new List<string>();
            if (comma == 1) { intTokens = tokens.GetRange(0, commaAt); fracTokens = tokens.GetRange(commaAt + 1, tokens.Count - commaAt - 1); }
            else intTokens = tokens;
            if (intTokens.Count == 0) return Fail(NumberParseError.NoIntegerPart, "Manca la parte intera prima della virgola.");

            long? intVal = ParseIntPart(intTokens, out bool tooBig);
            if (tooBig) return Fail(NumberParseError.OutOfRange, "Valore fuori intervallo.");
            if (intVal == null) return Fail(NumberParseError.NotANumber, "Non ho capito il numero.");
            double value = intVal.Value;
            if (comma == 1)
            {
                string frac = ParseFraction(fracTokens);
                if (frac == null || frac.Length > MaxFractionDigits) return Fail(NumberParseError.BadFraction, "Decimali non validi.");
                value += double.Parse("0." + frac, CultureInfo.InvariantCulture);
            }
            value *= sign;
            if (value == 0) value = 0; // niente -0
            if (double.IsNaN(value) || double.IsInfinity(value)) return Fail(NumberParseError.NotFinite, "Valore non finito.");
            if (value < min || value > max)
                return Fail(NumberParseError.OutOfRange, "Valore fuori intervallo (" + Fmt(min) + " ... " + Fmt(max) + ").");
            return QuantityParseResult.Success(value, unit);
        }

        internal static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static QuantityParseResult Fail(NumberParseError e, string reason) => QuantityParseResult.Fail(e, reason);

        private static long? ParseIntPart(List<string> tokens, out bool tooBig)
        {
            tooBig = false;
            if (tokens.Count == 1 && IsDigits(tokens[0]))
            {
                if (tokens[0].Length > 9) { tooBig = true; return null; }
                return long.Parse(tokens[0], CultureInfo.InvariantCulture);
            }
            return ParseIntegerWords(tokens);
        }

        private static bool IsDigits(string t)
        {
            if (t.Length == 0) return false;
            foreach (char c in t) if (c < '0' || c > '9') return false;
            return true;
        }

        private static List<string> Segment(string word, int depth)
        {
            if (word.Length == 0) return new List<string>();
            if (depth > 12) return null;
            foreach (var k in AtomKeys)
            {
                if (!word.StartsWith(k, StringComparison.Ordinal)) continue;
                var rest = Segment(word.Substring(k.Length), depth + 1);
                if (rest != null) { rest.Insert(0, k); return rest; }
            }
            return null;
        }

        /// <summary>Valore intero da parole normalizzate (una "e" tra i gruppi e ammessa). Null se invalido.</summary>
        public static long? ParseIntegerWords(IList<string> tokens)
        {
            var atoms = new List<string>();
            foreach (var t in tokens)
            {
                if (t == "e") continue;
                if (t.Length > MaxTokenLength) return null;
                var seg = Segment(t, 0);
                if (seg == null || seg.Count == 0) return null;
                atoms.AddRange(seg);
            }
            if (atoms.Count == 0) return null;
            long total = 0, cur = 0;
            Cat? prev = null;
            bool seenThousands = false;
            for (int i = 0; i < atoms.Count; i++)
            {
                var kv = Atoms[atoms[i]];
                Cat cat = kv.Key; int val = kv.Value;
                string nxt = i + 1 < atoms.Count ? atoms[i + 1] : "";
                if (cat == Cat.Stem || cat == Cat.HStem)
                {
                    if (nxt.Length == 0 || (nxt[0] != 'u' && nxt[0] != 'o')) return null;
                    if (cat == Cat.HStem && nxt != "otto" && nxt != "ottanta") return null;
                    if (cat == Cat.Stem && nxt != "uno" && nxt != "un" && nxt != "otto") return null;
                }
                if (cat == Cat.Unit || cat == Cat.Teen || cat == Cat.Tens || cat == Cat.Stem)
                {
                    if (val == 0 && atoms.Count > 1) return null;
                    if (prev == Cat.Unit || prev == Cat.Teen) return null;
                    if (prev == Cat.Tens && cat != Cat.Unit) return null;
                    if (prev == Cat.Tens && val == 0) return null;
                    cur += val;
                }
                else if (cat == Cat.Hundred || cat == Cat.HStem)
                {
                    if (prev == Cat.Tens || prev == Cat.Teen || prev == Cat.Hundred || (prev == Cat.Unit && cur >= 10)) return null;
                    if (cur >= 100) return null;
                    cur = (cur == 0 ? 1 : cur) * 100;
                    if (cur > 900) return null;
                }
                else if (cat == Cat.Mille)
                {
                    if (cur != 0 || seenThousands) return null; // "duemille" non e valido: "duemila"
                    total += 1000; seenThousands = true;
                }
                else // Mila
                {
                    if (cur == 0 || seenThousands || cur >= 1000) return null;
                    total += cur * 1000; cur = 0; seenThousands = true;
                }
                prev = cat == Cat.Stem ? Cat.Tens : cat == Cat.HStem ? Cat.Hundred : cat;
                if (cat == Cat.Mille || cat == Cat.Mila) prev = null;
            }
            return total + cur;
        }

        /// <summary>Cifre decimali: "cinque" -> "5", "zero cinque" -> "05", "venticinque" -> "25", "due cinque" -> "25".</summary>
        private static string ParseFraction(List<string> tokens)
        {
            if (tokens.Count == 0) return null;
            if (tokens.Count == 1 && IsDigits(tokens[0])) return tokens[0];
            var digits = new System.Text.StringBuilder();
            bool allSingle = true;
            foreach (var t in tokens)
            {
                if (Units.TryGetValue(t, out int d) && t != "un") digits.Append(d); else { allSingle = false; break; }
            }
            if (allSingle) return digits.ToString();
            int zeros = 0;
            while (zeros < tokens.Count && tokens[zeros] == "zero") zeros++;
            var rest = tokens.GetRange(zeros, tokens.Count - zeros);
            if (rest.Count == 0) return null;
            long? n = ParseIntegerWords(rest);
            if (n == null) return null;
            return new string('0', zeros) + n.Value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
