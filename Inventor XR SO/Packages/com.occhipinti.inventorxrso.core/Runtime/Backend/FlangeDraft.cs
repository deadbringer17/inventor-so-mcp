using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>
    /// The one flange draft shared by the spatial manipulator and the numeric field. Any real change bumps
    /// <see cref="Version"/> and raises <see cref="Changed"/>, so whoever holds a preview can drop it.
    /// Edges are persistent CAD ids (ent_...), never GLB mesh ids. The bend angle is only ever set
    /// explicitly; dragging moves the height alone.
    /// </summary>
    public sealed class FlangeDraft
    {
        private readonly List<string> _edges = new List<string>();
        public IReadOnlyList<string> EdgeIds => _edges;
        public double HeightMm { get; private set; } = 10;
        public double AngleDegrees { get; private set; } = 90;
        public string Datum { get; private set; } = "outer";
        public int Version { get; private set; }
        /// <summary>Set when a manipulator was released: the UI should ask for a preview (never a commit).</summary>
        public bool PreviewRequested { get; private set; }
        public event Action Changed;
        public event Action PreviewWanted;

        public bool SetEdges(IEnumerable<string> edgeIds)
        {
            var next = (edgeIds ?? Enumerable.Empty<string>()).Distinct().ToList();
            if (next.SequenceEqual(_edges)) return false;
            _edges.Clear(); _edges.AddRange(next); return Bump();
        }

        public bool ToggleEdge(string edgeId)
        {
            if (string.IsNullOrEmpty(edgeId)) return false;
            if (!_edges.Remove(edgeId)) _edges.Add(edgeId);
            return Bump();
        }

        /// <summary>Numeric field.</summary>
        public bool SetHeight(double mm)
        {
            if (HeightMm.Equals(mm)) return false; // NaN equals itself here: no spurious bump on repeated bad input
            HeightMm = mm; return Bump();
        }
        /// <summary>Spatial manipulator: same setter as the field, so both edit one draft. Touches nothing else.</summary>
        public bool ApplyManipulatorHeight(double mm) => SetHeight(mm);
        /// <summary>Numeric field only; no gesture ever infers a bend angle.</summary>
        public bool SetAngle(double degrees)
        {
            if (AngleDegrees.Equals(degrees)) return false;
            AngleDegrees = degrees; return Bump();
        }
        public bool SetDatum(string datum)
        {
            if (datum == Datum) return false;
            Datum = datum; return Bump();
        }

        /// <summary>Manipulator release: request a preview of the current version, leave Apply to the session.</summary>
        public void ReleaseManipulator()
        {
            PreviewRequested = true;
            PreviewWanted?.Invoke();
        }
        public void AcknowledgePreviewRequest() => PreviewRequested = false;

        public void Clear()
        {
            if (_edges.Count == 0) return;
            _edges.Clear(); Bump();
        }

        /// <summary>Local validation through the shared builder; the message is Italian and user-facing.</summary>
        public bool TryBuild(out JObject operation, out string error)
        {
            try
            {
                operation = SheetMetalOperations.Flange(_edges.ToArray(), HeightMm, AngleDegrees, Datum);
                error = null; return true;
            }
            catch (ArgumentException ex) { operation = null; error = ex.Message; return false; }
        }

        private bool Bump()
        {
            Version++; Changed?.Invoke(); return true;
        }
    }
}
