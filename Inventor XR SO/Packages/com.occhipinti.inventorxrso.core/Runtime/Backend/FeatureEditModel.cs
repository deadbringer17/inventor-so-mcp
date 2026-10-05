using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>One chip of the «Feature: nome» tab: a driving parameter with its local draft value (M9 §4).</summary>
    public sealed class FeatureChip
    {
        public FeatureChip(FeatureParameter source, bool editable, string readOnlyReason, string sourceParameter)
        {
            Name = source.Name; Role = source.Role; Unit = FeatureEditModel.UnitCode(source.Unit);
            Expression = source.Expression; Original = source.Value; Value = source.Value;
            Editable = editable; ReadOnlyReason = readOnlyReason; SourceParameter = sourceParameter;
        }

        public string Name { get; }
        public string Role { get; }
        /// <summary>Wire unit of <c>set_parameter</c>: mm, deg or ul.</summary>
        public string Unit { get; }
        public string Expression { get; }
        public double Original { get; }
        public double Value { get; internal set; }
        /// <summary>False for an expression-driven parameter, a suppressed or unhealthy feature: never written.</summary>
        public bool Editable { get; }
        public string ReadOnlyReason { get; }
        /// <summary>The parameter an expression plainly refers to (expression == a parameter name), else null.</summary>
        public string SourceParameter { get; }
        public bool Modified => Editable && Value != Original;
    }

    /// <summary>
    /// Local state of the feature edit: chips and draft values. It never talks to CAD; <see cref="BuildOperations"/> produces the
    /// operations of ONE atomic batch (N <c>set_parameter</c>) that the shared Design session previews and applies.
    /// </summary>
    public sealed class FeatureEditModel
    {
        private static readonly Regex PlainName = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
        private readonly List<FeatureChip> _chips = new List<FeatureChip>();
        private readonly List<(string name, double value, string unit)> _sources = new List<(string, double, string)>();

        public FeatureEditModel(FaceFeatureInfo info, string faceId, IEnumerable<string> knownParameterNames)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            FaceId = faceId;
            var known = new HashSet<string>(knownParameterNames ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            BlockReason = !info.Supported ? "Tipo di feature non supportato in XR: modifica dal desktop."
                : info.Suppressed ? "Feature soppressa: modifica bloccata."
                : !info.Healthy ? "Feature in errore: modifica bloccata." : null;
            foreach (var p in info.Parameters)
            {
                string source = SourceOf(p, known);
                string reason = BlockReason != null ? BlockReason
                    : p.Editable ? null
                    : "Guidato dall'espressione «" + p.Expression + "»: non si scrive un numero sopra un'espressione.";
                _chips.Add(new FeatureChip(p, BlockReason == null && p.Editable, reason, source));
            }
        }

        public FaceFeatureInfo Info { get; }
        public string FaceId { get; }
        public string Name => Info.FeatureName;
        public IReadOnlyList<FeatureChip> Chips => _chips;
        /// <summary>Why the whole feature cannot be edited (unsupported, suppressed, in error), else null.</summary>
        public string BlockReason { get; }
        public bool IsDirty => _sources.Count > 0 || _chips.Any(c => c.Modified);

        private static string SourceOf(FeatureParameter p, HashSet<string> known)
        {
            if (p.Editable || string.IsNullOrWhiteSpace(p.Expression)) return null;
            var text = p.Expression.Trim();
            return PlainName.IsMatch(text) && text != p.Name && known.Contains(text) ? text : null;
        }

        public FeatureChip Chip(string name) => _chips.FirstOrDefault(c => c.Name == name);

        /// <summary>Sets the draft value of an editable chip. A read-only chip never accepts a number.</summary>
        public bool TrySetValue(string name, double value, out string reason)
        {
            reason = null;
            var chip = Chip(name);
            if (chip == null) { reason = "Parametro sconosciuto."; return false; }
            if (!chip.Editable) { reason = chip.ReadOnlyReason ?? "Parametro in sola lettura."; return false; }
            if (double.IsNaN(value) || double.IsInfinity(value)) { reason = "Valore non valido."; return false; }
            chip.Value = value;
            return true;
        }

        /// <summary>
        /// «Modifica sorgente»: the value goes to the parameter an expression plainly refers to, never over the expression itself.
        /// Only a name that is the SourceParameter of one of the chips is accepted.
        /// </summary>
        public bool TrySetSource(string name, double value, string unit, out string reason)
        {
            reason = null;
            if (!_chips.Any(c => c.SourceParameter == name)) { reason = "«" + name + "» non è la sorgente di un parametro di questa feature."; return false; }
            if (double.IsNaN(value) || double.IsInfinity(value)) { reason = "Valore non valido."; return false; }
            _sources.RemoveAll(x => x.name == name);
            _sources.Add((name, value, UnitCode(unit)));
            return true;
        }

        /// <summary>One <c>set_parameter</c> per modified chip (chip order), then one per edited source; empty when nothing changed.</summary>
        public JArray BuildOperations()
        {
            var operations = new JArray();
            foreach (var chip in _chips.Where(c => c.Modified))
                operations.Add(DesignOperations.Parameter(chip.Name, chip.Value, chip.Unit));
            foreach (var source in _sources) operations.Add(DesignOperations.Parameter(source.name, source.value, source.unit));
            return operations;
        }

        public static string UnitCode(string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "mm": return "mm";
                case "deg": case "°": case "gradi": return "deg";
                default: return "ul";
            }
        }
    }
}
