using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class AssemblyOccurrence
    {
        public string Id { get; }
        public string Name { get; }
        public string DefinitionId { get; }
        public string Kind { get; }
        public bool Editable { get; }
        public bool Grounded { get; }
        public bool DofComplete { get; }
        public int? TranslationCount { get; }
        public int? RotationCount { get; }
        public int? TotalDof => TranslationCount.HasValue && RotationCount.HasValue ? TranslationCount + RotationCount : null;
        public IReadOnlyList<CadPoint> TranslationAxes { get; }
        public IReadOnlyList<CadPoint> RotationAxes { get; }
        public CadPoint? Center { get; }
        public string UnavailableReason { get; }
        public bool CanMove => Editable && !Grounded && DofComplete && TotalDof > 0;
        internal AssemblyOccurrence(JObject json)
        {
            Id = AssemblyOperations.Entity((string)json["occurrence_id"]); Name = (string)json["name"] ?? Id;
            DefinitionId = (string)json["definition_document_id"]; Kind = (string)json["definition_kind"];
            Editable = (bool?)json["editable"] == true && (bool?)json["suppressed"] == false && (bool?)json["adaptive"] == false && (bool?)json["flexible"] != true;
            Grounded = (bool?)json["grounded"] != false;
            UnavailableReason = (string)json["unavailable_reason"];
            TranslationCount = Count(json["dof_translation"]); RotationCount = Count(json["dof_rotation"]);
            TranslationAxes = Axes(json["translation_axes"]); RotationAxes = Axes(json["rotation_axes"]);
            Center = json["rotation_center_mm"] is JArray center ? CadPoint.Parse(center) : (CadPoint?)null;
            DofComplete = (bool?)json["dof_complete"] == true && TranslationCount == TranslationAxes.Count
                && RotationCount == RotationAxes.Count && (RotationCount == 0 || Center.HasValue);
        }
        private static int? Count(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.Integer || (int)token < 0 || (int)token > 3) throw new FormatException("Invalid DOF count.");
            return (int)token;
        }
        private static CadPoint[] Axes(JToken token)
        {
            if (!(token is JArray array) || array.Count > 3) throw new FormatException("Invalid DOF axes.");
            var result = array.Select(t => CadPoint.Parse(t)).ToArray();
            if (result.Any(v => Math.Abs(v.Length - 1) > 1e-5)) throw new FormatException("DOF axes must be normalized.");
            return result;
        }
    }

    public sealed class AssemblyReference
    {
        public string Id { get; }
        public string OccurrenceId { get; }
        public string Name { get; }
        public string Kind { get; }
        public string Geometry { get; }
        public int BodyIndex { get; }
        public int FaceOrdinal { get; }
        public CadPoint? Point { get; }
        public CadPoint? Axis { get; }
        public IReadOnlyList<CadPoint> Polyline { get; }
        public bool IsPlane => Kind == "face" && Geometry == "kPlaneSurface";
        public bool IsAxial => Kind == "face" && (Geometry == "kCylinderSurface" || Geometry == "kConeSurface");
        public bool IsCircle => Kind == "edge" && (Geometry == "kCircleCurve" || Geometry == "kCircularArcCurve");
        public bool Available { get; }
        internal AssemblyReference(JObject json)
        {
            Id = AssemblyOperations.Entity((string)json["id"]); OccurrenceId = AssemblyOperations.Entity((string)json["occurrence_id"]);
            Name = (string)json["name"] ?? Id; Kind = (string)json["kind"]; Geometry = (string)json["geometry"];
            BodyIndex = (int?)json["body_index"] ?? 0; FaceOrdinal = (int?)json["face_ordinal"] ?? 0;
            Point = json["point_mm"] is JArray point ? CadPoint.Parse(point) : (CadPoint?)null;
            Axis = json["axis"] is JArray axis ? CadPoint.Parse(axis) : (CadPoint?)null;
            Polyline = (json["polyline_mm"] as JArray ?? new JArray()).Select(p => CadPoint.Parse(p)).ToArray();
            if (Polyline.Count > 4096 || (Kind != "face" && Kind != "edge")) throw new FormatException("Invalid assembly geometry.");
            Available = string.IsNullOrEmpty((string)json["unavailable_reason"]) && Point.HasValue;
        }
    }

    public sealed class AssemblyContext
    {
        public DocumentState State { get; }
        public IReadOnlyList<AssemblyOccurrence> Occurrences { get; }
        public IReadOnlyList<AssemblyReference> References { get; }
        public bool Truncated { get; }
        private AssemblyContext(JObject json, DocumentState expected)
        {
            if ((string)json["document_id"] != expected.DocumentId || (string)json["revision"] != expected.Revision || (string)json["kind"] != "assembly")
                throw new FormatException("Assembly context differs from the active document.");
            State = expected; Truncated = (bool?)json["truncated"] ?? true;
            if (!(json["occurrences"] is JArray occurrences) || occurrences.Count > 2000 || !(json["references"] is JArray references) || references.Count > 5000)
                throw new FormatException("Invalid assembly context collections.");
            Occurrences = occurrences.Select(o => new AssemblyOccurrence((JObject)o)).ToArray();
            References = references.Select(r => new AssemblyReference((JObject)r)).ToArray();
            if (Occurrences.Select(o => o.Id).Distinct().Count() != Occurrences.Count
                || References.Select(r => r.Id).Distinct().Count() != References.Count
                || References.Any(r => !Occurrences.Any(o => o.Id == r.OccurrenceId)))
                throw new FormatException("Invalid assembly reference ownership.");
        }
        public static AssemblyContext Parse(JObject json, DocumentState expected) => new AssemblyContext(json, expected);
    }
}
