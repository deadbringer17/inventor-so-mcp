using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public enum SketchSubpart { Entity, Start, End, Center, Point }

    /// <summary>L'identità CAD è opaca; la revisione fa parte del riferimento, mai un indice di raccolta.</summary>
    public sealed class SketchEntityRef
    {
        public SketchEntityRef(string documentId, string revision, string sketchId, string entityId, string type, SketchSubpart subpart = SketchSubpart.Entity)
        {
            if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(type)
                || !Opaque(sketchId) || !Opaque(entityId) || !Enum.IsDefined(typeof(SketchSubpart), subpart))
                throw new FormatException("Riferimento di schizzo nativo incompleto.");
            DocumentId = documentId; Revision = revision; SketchId = sketchId; EntityId = entityId; Type = type; Subpart = subpart;
        }
        internal static bool Opaque(string value) => value != null && value.StartsWith("ent_", StringComparison.Ordinal) && value.Length > 4 && value.Length <= 131072;
        public string DocumentId { get; }
        public string Revision { get; }
        public string SketchId { get; }
        public string EntityId { get; }
        public string Type { get; }
        public SketchSubpart Subpart { get; }
        public bool Matches(DocumentState state, string sketchId) => state != null && state.DocumentId == DocumentId
            && state.Revision == Revision && sketchId == SketchId;
        public SketchEntityRef WithSubpart(SketchSubpart subpart) => new SketchEntityRef(DocumentId, Revision, SketchId, EntityId, Type, subpart);
        public JObject ToJson() => new JObject { ["document_id"]=DocumentId,["revision"]=Revision,["sketch_id"]=SketchId,
            ["entity_id"]=EntityId,["type"]=Type,["subpart"]=Subpart.ToString() };
    }

    /// <summary>Validated native geometry and original-source metadata; does not certify displayed native pixels.</summary>
    public sealed class NativeSketchImage
    {
        private NativeSketchImage() { }
        public IReadOnlyList<CadPoint> CornersMm { get; private set; }
        public double WidthMm { get; private set; }
        public double HeightMm { get; private set; }
        public bool Visible { get; private set; }
        public bool Transparent { get; private set; }
        public string NativeType { get; private set; }
        public string AssetId { get; private set; }
        public string Sha256 { get; private set; }
        public int ByteLength { get; private set; }
        public int PixelWidth { get; private set; }
        public int PixelHeight { get; private set; }
        public string Format => "bmp";
        public string Provenance => "native_corner_reference_keys";
        public string SourceKind => "managed_original";

        internal static NativeSketchImage Parse(JObject data)
        {
            try
            {
                var frame = data["image_frame"] as JObject;
                var source = data["image_source"] as JObject;
                if (frame == null || source == null) throw Invalid();
                if (frame.TryGetValue("corner_order", out var order))
                {
                    Fields(frame, "schema", "provenance", "corners_mm", "corner_order");
                    if (!(order is JArray named) || named.Count != 4 || named.Any(p => p.Type != JTokenType.String)
                        || !named.Values<string>().SequenceEqual(new[] { "top_left", "top_right", "bottom_right", "bottom_left" }, StringComparer.Ordinal)) throw Invalid();
                }
                else Fields(frame, "schema", "provenance", "corners_mm");
                Fields(source, "schema", "kind", "asset_id", "sha256", "byte_length", "format", "pixel_width", "pixel_height");
                if (Integer(frame, "schema", 1, 1) != 1 || Text(frame, "provenance") != "native_corner_reference_keys"
                    || Integer(source, "schema", 1, 1) != 1 || Text(source, "kind") != "managed_original"
                    || Text(source, "format") != "bmp") throw Invalid();
                string sha = Text(source, "sha256"), asset = Text(source, "asset_id"), nativeType = Text(data, "native_type");
                if (sha.Length != 64 || sha.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))
                    || asset != "a_" + sha || string.IsNullOrWhiteSpace(nativeType) || nativeType.Length > 256) throw Invalid();
                int length = Integer(source, "byte_length", 54, 32 * 1024 * 1024);
                int pixelWidth = Integer(source, "pixel_width", 1, int.MaxValue), pixelHeight = Integer(source, "pixel_height", 1, int.MaxValue);
                // Minimum 24-bit row layout must fit the declared source length. Bytes remain unverified here.
                if ((((long)pixelWidth * 24 + 31) / 32) * 4 > (length - 54L) / pixelHeight) throw Invalid();
                if (data["visible"]?.Type != JTokenType.Boolean || data["transparent"]?.Type != JTokenType.Boolean
                    || !(frame["corners_mm"] is JArray points) || points.Count != 4) throw Invalid();
                double width = Positive(data, "width_mm"), height = Positive(data, "height_mm");
                CadPoint[] corners = points.Select(p => CadPoint.Parse(p, 2)).ToArray();
                CadPoint top = corners[1] - corners[0], left = corners[3] - corners[0];
                const double tolerance = 1e-5;
                bool Near(double actual, double expected) => !double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= tolerance;
                if (!(top.Length > 0) || !(left.Length > 0) || !Near(top.Length, width) || !Near(left.Length, height)
                    || !Near((corners[2] - corners[3]).Length, width) || !Near((corners[2] - corners[1]).Length, height)
                    || !Near((corners[2] - corners[1] - left).Length, 0)
                    || !Near(top.Dot(left) / width, 0)) throw Invalid();
                return new NativeSketchImage
                {
                    CornersMm = Array.AsReadOnly(corners), WidthMm = width, HeightMm = height,
                    Visible = (bool)data["visible"], Transparent = (bool)data["transparent"], NativeType = nativeType,
                    AssetId = asset, Sha256 = sha, ByteLength = length, PixelWidth = pixelWidth, PixelHeight = pixelHeight
                };
            }
            catch (ArgumentException) { throw Invalid(); }
            catch (OverflowException) { throw Invalid(); }
        }
        private static void Fields(JObject data, params string[] fields)
        { if (data.Count != fields.Length || data.Properties().Any(p => !fields.Contains(p.Name, StringComparer.Ordinal))) throw Invalid(); }
        private static string Text(JObject data, string name)
        { if (data[name]?.Type != JTokenType.String) throw Invalid(); return (string)data[name]; }
        private static int Integer(JObject data, string name, int minimum, int maximum)
        {
            var token = data[name];
            if (token?.Type != JTokenType.Integer || !int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                || value < minimum || value > maximum) throw Invalid();
            return value;
        }
        private static double Positive(JObject data, string name)
        {
            var token = data[name];
            if (token == null || token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw Invalid();
            double value = (double)token;
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) throw Invalid();
            return value;
        }
        private static FormatException Invalid() => new FormatException("Descrizione immagine nativa non valida.");
    }

    public sealed class NativeSketchObject
    {
        private readonly JObject _data;
        private readonly Dictionary<SketchSubpart,CadPoint> _anchors = new Dictionary<SketchSubpart,CadPoint>();
        private readonly Dictionary<SketchSubpart,string> _structuralSubparts = new Dictionary<SketchSubpart,string>();
        internal NativeSketchObject(JObject data, DocumentState state, string sketchId)
        {
            _data = (JObject)data.DeepClone();
            Type = (string)data["type"];
            if (string.IsNullOrWhiteSpace(Type)) throw new FormatException("Tipo nativo mancante.");
            Supported = (bool?)data["supported"] == true;
            if(data["text_bounds_mm"]!=null)
            {
                if(Type!="text") throw new FormatException("Limiti di testo associati a un altro tipo.");
                SketchTextGlyphMeshContract.ValidateBounds(data["text_bounds_mm"] as JArray);
                var bounds=(JArray)data["text_bounds_mm"];var corners=new CadPoint[4];
                for(int i=0;i<4;i++) corners[i]=new CadPoint((double)bounds[i*2],(double)bounds[i*2+1]);
                TextBoundsMm=Array.AsReadOnly(corners);
            }
            if(data["text_glyphs"]!=null)
            {
                if(Type!="text"||!(data["text_glyphs"] is JObject glyphs)) throw new FormatException("Glifi associati a un altro tipo o incompleti.");
                TextGlyphs=NativeSketchTextGlyphMesh.Parse(glyphs);
            }
            SelectionState = (string)data["selection_state"] ?? "editable";
            if(data["selection_state"]!=null && data["selection_state"].Type!=JTokenType.String
                || !new[]{"editable","image_boundary","read_only_image","read_only_spline_handle","unavailable"}.Contains(SelectionState,StringComparer.Ordinal))
                throw new FormatException("Stato di selezione nativo non valido.");
            if((string)data["native_type"]=="kSketchSplineHandleObject" && Type!="spline_handle"
                || Type=="spline_handle" && ((string)data["native_type"]!="kSketchSplineHandleObject" || SelectionState!="read_only_spline_handle")
                || SelectionState=="read_only_spline_handle" && Type!="spline_handle" && Type!="point")
                throw new FormatException("Maniglia spline priva del contratto strutturale.");
            StructuralOwnerImageId=(string)data["structural_owner_image_id"];
            if(SelectionState=="image_boundary")
            {
                if((Type!="line" && Type!="point") || data["structural_owner_image_id"]?.Type!=JTokenType.String
                    || !SketchEntityRef.Opaque(StructuralOwnerImageId)) throw new FormatException("Proprietario del bordo immagine mancante.");
            }
            else if(data["structural_owner_image_id"]!=null) throw new FormatException("Proprietario strutturale inatteso.");
            if(SelectionState=="read_only_image" && Type!="image") throw new FormatException("Stato immagine associato a un altro tipo.");
            if(data["image_current"]!=null)
            {
                if(Type!="image" || Supported || SelectionState!="read_only_image" || !(data["image_current"] is JObject current)) throw new FormatException("Frame corrente privo del contratto immagine di sola lettura.");
                CurrentImage=NativeSketchImageCurrent.Parse(current);
            }
            if (data["image_frame"] != null || data["image_source"] != null || Type == "image" && Supported)
            {
                if (Type != "image") throw new FormatException("Metadati immagine associati a un altro tipo nativo.");
                Image = NativeSketchImage.Parse(data);
            }
            string id = (string)data["id"];
            if (id != null) Reference = new SketchEntityRef(state.DocumentId, state.Revision, sketchId, id, Type);
            if (Supported && Reference == null) throw new FormatException("Oggetto nativo senza identità opaca.");
            UnavailableReason = (string)data["unavailable_reason"] ?? "";
            if(data["pattern_definition"]!=null)
            {
                if(Type!="constraint" || !(data["pattern_definition"] is JObject definition)
                    || ((string)definition["kind"]!="rectangular" && (string)definition["kind"]!="circular")) throw new FormatException("Definizione serie nativa non valida.");
                bool circular=(string)definition["kind"]=="circular";
                foreach(string flag in new[] {"associative","fitted"}.Concat(circular ? new[] {"natural_axis","symmetric"}
                    : new[] {"two_directions","natural_x","natural_y","symmetric_x","symmetric_y"}))
                    if(definition[flag]?.Type!=JTokenType.Boolean) throw new FormatException("Opzione serie nativa non valida.");
                string[] fields=circular ? new[] {"count","angle_deg"} : (bool)definition["two_directions"] ? new[] {"x_count","x_spacing_mm","y_count","y_spacing_mm"} : new[] {"x_count","x_spacing_mm"};
                foreach(string field in fields)
                {
                    var parameter=definition[field] as JObject;var number=parameter?["value"];
                    if(number==null || (number.Type!=JTokenType.Float && number.Type!=JTokenType.Integer)) throw new FormatException("Parametro serie nativa non valido.");
                    double value=(double)number;
                    if(double.IsNaN(value) || double.IsInfinity(value) || value<=0 || (field.EndsWith("count",StringComparison.Ordinal) && (value!=Math.Truncate(value) || value>2000))) throw new FormatException("Valore serie nativa non valido.");
                    foreach(string name in new[] {"expression","name","units"})
                        if(parameter[name]!=null && (parameter[name].Type!=JTokenType.String || ((string)parameter[name]).Length>2048)) throw new FormatException("Testo parametro serie nativa non valido.");
                }
                if(definition["element_count"]?.Type!=JTokenType.Integer || (int)definition["element_count"]<1 || (int)definition["element_count"]>2000
                    || !(definition["suppressed_elements"] is JArray suppressed) || suppressed.Count>2000
                    || suppressed.Any(e=>e.Type!=JTokenType.Integer || (int)e<1 || (int)e>2000) || suppressed.Values<int>().Distinct().Count()!=suppressed.Count)
                    throw new FormatException("Elementi serie nativa non validi.");
            }
            BindingState=(string)data["binding_state"] ?? "unavailable";
            if(BindingState!="complete" && BindingState!="unavailable") throw new FormatException("Stato associazioni native non valido.");
            var related=new List<SketchEntityRef>();
            if(data["related_entities"]!=null)
            {
                if(Type!="constraint" || BindingState!="complete" || !(data["related_entities"] is JArray bindings) || bindings.Count<1 || bindings.Count>2000)
                    throw new FormatException("Associazioni del vincolo native non valide.");
                foreach(var binding in bindings)
                {
                    if(!(binding is JObject) || !Enum.TryParse((string)binding["subpart"],false,out SketchSubpart part) || !Enum.IsDefined(typeof(SketchSubpart),part))
                        throw new FormatException("Sottoparte associata al vincolo non valida.");
                    related.Add(new SketchEntityRef(state.DocumentId,state.Revision,sketchId,(string)binding["entity_id"],(string)binding["type"],part));
                }
                if(related.Select(r=>(r.EntityId,r.Subpart)).Distinct().Count()!=related.Count) throw new FormatException("Associazione del vincolo duplicata.");
            }
            if(BindingState=="complete" && related.Count==0) throw new FormatException("Vincolo completo senza associazioni.");
            RelatedEntities=related.AsReadOnly();
            if(data["annotation_mm"]!=null) AnnotationMm=CadPoint.Parse(data["annotation_mm"],2);
            Construction = (bool?)data["construction"] ?? false;
            var strokes = new List<CadPoint>();
            if (data["strokes_mm"] is JArray flat)
            {
                if (flat.Count < 4 || flat.Count % 2 != 0 || flat.Count > 16384) throw new FormatException("Tessellazione nativa non valida.");
                for (int i = 0; i < flat.Count; i += 2) strokes.Add(CadPoint.Parse(new JArray(flat[i], flat[i + 1]), 2));
            }
            else if (data["point_mm"] is JArray point) strokes.Add(CadPoint.Parse(point, 2));
            Strokes = strokes.AsReadOnly();
            foreach(var entry in new[] { ("start_mm",SketchSubpart.Start),("end_mm",SketchSubpart.End),
                ("center_mm",SketchSubpart.Center),("point_mm",SketchSubpart.Point) })
                if(data[entry.Item1]!=null) _anchors.Add(entry.Item2,CadPoint.Parse(data[entry.Item1],2));
            if(data["structural_subparts"]!=null)
            {
                if(!(data["structural_subparts"] is JArray structural) || structural.Count<1 || structural.Count>4) throw new FormatException("Sottoparti strutturali non valide.");
                foreach(var token in structural)
                {
                    if(!(token is JObject binding) || binding.Count!=2 || binding["subpart"]?.Type!=JTokenType.String
                        || binding["owner_image_id"]?.Type!=JTokenType.String
                        || !Enum.TryParse((string)binding["subpart"],false,out SketchSubpart part) || part==SketchSubpart.Entity
                        || !_anchors.ContainsKey(part) || _structuralSubparts.ContainsKey(part)
                        || !SketchEntityRef.Opaque((string)binding["owner_image_id"])) throw new FormatException("Sottoparte strutturale incompleta.");
                    _structuralSubparts.Add(part,(string)binding["owner_image_id"]);
                }
            }
        }
        public string Type { get; }
        public bool Supported { get; }
        public NativeSketchTextGlyphMesh TextGlyphs { get; }
        public IReadOnlyList<CadPoint> TextBoundsMm { get; } = Array.Empty<CadPoint>();
        public string SelectionState { get; }
        public string StructuralOwnerImageId { get; }
        public bool Selectable => Supported && SelectionState=="editable";
        public bool CanSelectSubpart(SketchSubpart part)=>Selectable && !_structuralSubparts.ContainsKey(part)
            && (part==SketchSubpart.Entity || _anchors.ContainsKey(part));
        internal IEnumerable<string> StructuralImageOwners=>_structuralSubparts.Values.Concat(StructuralOwnerImageId==null ? Array.Empty<string>() : new[]{StructuralOwnerImageId});
        public NativeSketchImage Image { get; }
        public NativeSketchImageCurrent CurrentImage { get; }
        public bool Construction { get; }
        public string UnavailableReason { get; }
        public string BindingState {get;}
        public IReadOnlyList<SketchEntityRef> RelatedEntities {get;}
        public CadPoint? AnnotationMm {get;}
        public SketchEntityRef Reference { get; }
        public IReadOnlyList<CadPoint> Strokes { get; }
        public JObject Data => (JObject)_data.DeepClone();
        public bool TryGetSubpartPoint(SketchSubpart part,out CadPoint point) => _anchors.TryGetValue(part,out point);
        public double DistanceTo(CadPoint point)
        {
            if(TextBoundsMm.Count==4)
            {
                var origin=TextBoundsMm[0];var x=TextBoundsMm[1]-origin;var y=TextBoundsMm[3]-origin;var d=point-origin;
                double width=x.Length,height=y.Length;double a=d.Dot(x)/width,b=d.Dot(y)/height;
                var closest=origin+x*(Math.Max(0,Math.Min(width,a))/width)+y*(Math.Max(0,Math.Min(height,b))/height);
                return (point-closest).Length;
            }
            double best = Strokes.Count == 1 ? (point - Strokes[0]).Length : double.PositiveInfinity;
            for (int i = 1; i < Strokes.Count; i++)
            {
                var edge = Strokes[i] - Strokes[i - 1]; double square = edge.Dot(edge);
                double t = square == 0 ? 0 : Math.Max(0, Math.Min(1, (point - Strokes[i - 1]).Dot(edge) / square));
                best = Math.Min(best, (point - (Strokes[i - 1] + edge * t)).Length);
            }
            return best;
        }
    }

    public sealed class NativeSketchHit
    {
        internal NativeSketchHit(NativeSketchObject entity,SketchSubpart part,double distance,CadPoint? pickPointMm=null)
        { Entity=entity; Reference=entity.Reference.WithSubpart(part); DistanceMm=distance; PickPointMm=pickPointMm; }
        public NativeSketchObject Entity { get; }
        public SketchEntityRef Reference { get; }
        public double DistanceMm { get; }
        /// <summary>Original pointer position in sketch-local millimetres, without snapping to the curve or anchor.
        /// Window selections have no individual pointer position.</summary>
        public CadPoint? PickPointMm { get; }
    }

    /// <summary>Snapshot incrementale: le spline restano native; i tratti servono solo a visualizzazione/hit test.</summary>
    public sealed class NativeSketchSnapshot
    {
        private NativeSketchSnapshot() { }
        public DocumentState State { get; private set; }
        public string SketchId { get; private set; }
        public string Name { get; private set; }
        public SketchFrame Frame { get; private set; }
        public bool Visible { get; private set; }
        public bool Partial { get; private set; }
        public bool NativeMutationComplete { get; private set; }
        public string ConstraintStatus { get; private set; }
        public IReadOnlyList<NativeSketchObject> Entities { get; private set; }
        public IReadOnlyList<NativeSketchObject> Dimensions { get; private set; }
        public IReadOnlyList<NativeSketchObject> Constraints { get; private set; }
        public IReadOnlyList<string> Warnings { get; private set; }
        public IReadOnlyList<string> SupportedVariants { get; private set; }
        public int? SplineFitMethod { get; private set; }
        public bool SupportsVariant(string variant)=>variant!=null && SupportedVariants.Contains(variant,StringComparer.Ordinal)
            && (variant!="spline_fit" || SplineFitMethod.HasValue);

        public static NativeSketchSnapshot Parse(JObject json, DocumentState expected, string sketchId)
        {
            if (expected == null || (int?)json?["schema_version"] != 1 || !SketchEntityRef.Opaque(sketchId)
                || (string)json["document_id"] != expected.DocumentId || (string)json["revision"] != expected.Revision
                || (string)json["sketch_id"] != sketchId || string.IsNullOrWhiteSpace((string)json["sketch_name"]))
                throw new FormatException("Snapshot diverso dallo schizzo/documento/revisione richiesti.");
            NativeSketchObject[] Collection(string name)
            {
                if (!(json[name] is JArray data) || data.Count > 2000 || data.Any(d => !(d is JObject)))
                    throw new FormatException("Raccolta nativa incompleta: " + name);
                return data.Cast<JObject>().Select(d => new NativeSketchObject(d, expected, sketchId)).ToArray();
            }
            var entities = Collection("entities"); var dimensions = Collection("dimensions"); var constraints = Collection("constraints");
            var all = entities.Concat(dimensions).Concat(constraints).ToArray();
            if(all.Sum(e=>e.RelatedEntities.Count)>100000) throw new FormatException("Budget globale di associazioni dei vincoli superato.");
            if (all.Sum(e => e.Strokes.Count+(e.TextGlyphs?.VerticesMm.Count ?? 0)) > 100000
                ||all.Sum(e=>e.TextGlyphs?.Indices.Count/3 ?? 0)>200000) throw new FormatException("Budget globale di tessellazione superato.");
            var ids = all.Where(e => e.Reference != null).Select(e => e.Reference.EntityId).ToArray();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new FormatException("Identità nativa duplicata nello snapshot.");
            var entityMap=entities.Where(e=>e.Reference!=null).ToDictionary(e=>e.Reference.EntityId,StringComparer.Ordinal);
            foreach(var owner in all.SelectMany(e=>e.StructuralImageOwners))
                if(!entityMap.TryGetValue(owner,out var image) || image.Type!="image") throw new FormatException("Proprietario strutturale immagine assente dallo snapshot.");
            foreach(var binding in all.SelectMany(e=>e.RelatedEntities))
                if(!entityMap.TryGetValue(binding.EntityId,out var target) || !target.Supported || target.Type!=binding.Type
                    || (binding.Subpart!=SketchSubpart.Entity && !target.TryGetSubpartPoint(binding.Subpart,out _)))
                    throw new FormatException("Associazione del vincolo assente dallo snapshot nativo.");
            var variants=Array.Empty<string>();
            if(json.TryGetValue("supported_variants",out var capabilities))
            {
                if(!(capabilities is JArray advertised) || advertised.Count>128 || advertised.Any(v=>v.Type!=JTokenType.String
                    || string.IsNullOrWhiteSpace((string)v) || ((string)v).Length>128)) throw new FormatException("Capacità native non valide.");
                variants=advertised.Values<string>().ToArray();
                if(variants.Distinct(StringComparer.Ordinal).Count()!=variants.Length) throw new FormatException("Capacità native duplicate.");
            }
            int? fitMethod=null;
            if(json.TryGetValue("spline_fit_method",out var fit))
            {
                if(fit.Type!=JTokenType.Integer || (int)fit<26369 || (int)fit>26371)
                    throw new FormatException("Metodo spline nativo non valido.");
                fitMethod=(int)fit;
            }
            bool partial=(bool?)json["partial"]!=false || all.Any(e=>!e.Supported);
            bool mutationComplete=json["mutation_completeness"] is JObject proof
                ? SketchMutationCompleteness.Validate(proof,(JArray)json["entities"],(JArray)json["dimensions"],(JArray)json["constraints"],false,partial)
                : json["mutation_completeness"]==null && !partial;
            if(json["mutation_completeness"]!=null && !(json["mutation_completeness"] is JObject)) throw new FormatException("Stato strutturale dello snapshot non valido.");
            return new NativeSketchSnapshot
            {
                State = expected, SketchId = sketchId, Name = (string)json["sketch_name"], Frame = SketchFrame.Parse(json["frame"]),
                Visible = (bool?)json["visible"] ?? true, Partial = partial, NativeMutationComplete=mutationComplete,
                ConstraintStatus = (string)json["constraint_status"] ?? "unknown",
                Entities = Array.AsReadOnly(entities), Dimensions = Array.AsReadOnly(dimensions), Constraints = Array.AsReadOnly(constraints),
                Warnings = Array.AsReadOnly((json["warnings"] as JArray ?? new JArray()).Values<string>().ToArray()),
                SupportedVariants=Array.AsReadOnly(variants),SplineFitMethod=fitMethod
            };
        }

        public IReadOnlyList<NativeSketchObject> PickAll(CadPoint point, double toleranceMm, string type = null)
        {
            if (!(toleranceMm > 0) || double.IsInfinity(toleranceMm)) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
            return Entities.Where(e => e.Selectable && (type == null || e.Type == type))
                .Select(e => (entity: e, distance: e.DistanceTo(point))).Where(e => e.distance <= toleranceMm)
                .OrderBy(e => e.distance).ThenBy(e => e.entity.Reference.EntityId, StringComparer.Ordinal).Select(e => e.entity).ToArray();
        }
        public IReadOnlyList<NativeSketchHit> PickTargets(CadPoint point,double toleranceMm,string type=null,SketchSubpart? part=null)
        {
            if(point.Z!=0) throw new ArgumentException("Sketch picking requires a planar sketch-local point.",nameof(point));
            if (!(toleranceMm>0) || double.IsInfinity(toleranceMm)) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
            if(part.HasValue && !Enum.IsDefined(typeof(SketchSubpart),part.Value)) throw new ArgumentOutOfRangeException(nameof(part));
            var hits=new List<NativeSketchHit>();
            foreach(var entity in Entities.Where(e=>e.Selectable && (type==null || e.Type==type)))
            {
                if(!part.HasValue || part==SketchSubpart.Entity)
                { double distance=entity.DistanceTo(point); if(distance<=toleranceMm) hits.Add(new NativeSketchHit(entity,SketchSubpart.Entity,distance,point)); }
                foreach(var anchorPart in new[] {SketchSubpart.Start,SketchSubpart.End,SketchSubpart.Center,SketchSubpart.Point})
                    if((!part.HasValue || part==anchorPart) && entity.CanSelectSubpart(anchorPart) && entity.TryGetSubpartPoint(anchorPart,out var anchor))
                    { double distance=(point-anchor).Length; if(distance<=toleranceMm) hits.Add(new NativeSketchHit(entity,anchorPart,distance,point)); }
            }
            return hits.OrderBy(h=>h.DistanceMm).ThenBy(h=>h.Reference.EntityId,StringComparer.Ordinal)
                .ThenBy(h=>h.Reference.Subpart).ToArray();
        }
    }

    public interface INativeSketchBackend : IDesignBackend
    {
        Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state, string sketchId, CancellationToken ct);
    }

    public sealed partial class InventorBackend : INativeSketchBackend
    {
        public async Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state, string sketchId, CancellationToken ct)
            => NativeSketchSnapshot.Parse(await _mcp.CallToolAsync("inventor_get_sketch_editor_xr", new JObject
            { ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision, ["sketch_id"] = sketchId }, ct), state, sketchId);
    }
}
