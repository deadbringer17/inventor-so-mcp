#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>S1 native snapshot, with opaque keys scoped to document, sketch and revision. Never recreates geometry.</summary>
public sealed class SketchEditorXrHandler : ExperimentalHandler
{
    public override string Name => "get_sketch_editor_xr";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var part = X.ActivePart(app, Name);
        var doc = (global::Inventor.Document)part;
        string documentId = EntityReferences.DocumentId(doc), revision = X.Str(p, "expected_revision");
        if (X.Str(p, "document_id") != documentId) throw ConcurrencyFailure.DocumentChanged((string?)p["document_id"], documentId);
        if (ctx.Events == null || ctx.Events.Revision(documentId) != revision)
            throw ConcurrencyFailure.StaleRevision(revision, ctx.Events?.Revision(documentId));
        string sketchId = X.Str(p, "sketch_id");
        var sketch = SketchReferences.Resolve(doc, sketchId, "sketch") as PlanarSketch
            ?? throw new ArgumentException("Reference does not resolve to a native 2D sketch.");
        var snapshot = Capture(ctx, app, doc, sketch, sketchId, revision, true);
        if (ctx.Events.Revision(documentId) != revision) throw ConcurrencyFailure.StaleRevision(revision, ctx.Events.Revision(documentId));
        return snapshot;
    }

    internal static JObject Capture(InventorCommandContext ctx, Application app, global::Inventor.Document doc,
        PlanarSketch sketch, string? sketchId, string revision, bool includeReferences)
    {
        string documentId = EntityReferences.DocumentId(doc);
        var entities = new JArray(); var dimensions = new JArray(); var constraints = new JArray(); var warnings = new JArray();
        int points = 0,relatedCount=0,patternLinks=0,textTriangles=0;
        int readErrors=0,omitted=0;
        var representedEntities=new System.Collections.Generic.List<object>();
        var nativeBindings=new System.Collections.Generic.Dictionary<string,JArray>(StringComparer.Ordinal);
        var patternMembers=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<SketchEntity>>(StringComparer.Ordinal);
        void RegisterBinding(SketchEntity native,JObject item,string subpart)
        {
            string key=SketchReferences.Describe(doc,native,"sketch_entity");
            if(!nativeBindings.TryGetValue(key,out var bindings)) nativeBindings[key]=bindings=new JArray();
            bindings.Add(new JObject {["entity_id"]=item["id"],["type"]=item["type"],["subpart"]=subpart});
        }
        bool partial = false;
        NativeSketchSplineHandleStructure? splineHandles=null;
        try { splineHandles=NativeSketchSplineHandleStructure.Capture(sketch,()=>X.Deadline(ctx,"reading spline handle ownership")); }
        catch { readErrors++;partial=true;warnings.Add("Native spline handle ownership is unavailable."); }
        NativeSketchImageStructure? imageStructure = null;
        try { imageStructure = NativeSketchImageStructure.Capture(sketch, () => X.Deadline(ctx, "reading native image structural ownership")); }
        catch
        {
            readErrors++;
            partial = true;
            warnings.Add("Native image structural ownership is unavailable; geometry selection is disabled.");
        }
        bool patternMembershipUnavailable = false;
        // Reverse lookup is needed only for native PatternConstraint members.
        // Some unconstrained CV spline entities reject Constraints rather than
        // returning an empty enumerator. The sketch collection is authoritative
        // for whether any pattern relation needs to be resolved at all.
        bool hasPatternConstraints=sketch.GeometricConstraints.Cast<GeometricConstraint>().Any(c=>c is PatternConstraint);
        void Add(object native, string kind, JArray destination)
        {
            X.Deadline(ctx, "reading the S1 sketch editor snapshot");
            if (destination.Count >= 2000) { partial = true; omitted++; return; }
            var item = new JObject { ["type"] = kind };
            try
            {
                if (includeReferences) item["id"] = SketchReferences.Describe(doc, native, kind.StartsWith("constraint", StringComparison.Ordinal) ? "sketch_constraint"
                    : kind == "dimension" ? "sketch_dimension" : kind == "text" ? "sketch_text" : kind == "image" ? "sketch_image" : "sketch_entity");
                dynamic value = native;
                if (native is SketchEntity entity)
                {
                    if(splineHandles==null) item["selection_state"]="unavailable";
                    else if(splineHandles.Owner(entity)!=null) item["selection_state"]="read_only_spline_handle";
                    if (imageStructure == null) item["selection_state"] = "unavailable";
                    else if (imageStructure.Owner(entity) is SketchImage ownerImage)
                    {
                        item["selection_state"] = "image_boundary";
                        if (includeReferences) item["structural_owner_image_id"] = SketchReferences.Describe(doc, ownerImage, "sketch_image");
                    }
                    if(entity is not (SketchLine or SketchCircle or SketchArc or SketchEllipse or SketchEllipticalArc or SketchSpline or SketchControlPointSpline or SketchPoint or SketchOffsetSpline))
                        throw new InvalidOperationException("Native sketch geometry type is not yet supported.");
                    item["native_type"] = entity.Type.ToString();
                    item["construction"] = entity.Construction;
                    if (entity is SketchPoint point)
                    { if(points+1>100000) throw new InvalidOperationException("Point exceeds the snapshot budget.");
                        points++; item["point_mm"] = X.Mm2(point.Geometry); item["hole_center"] = point.HoleCenter; }
                    else
                    {
                        dynamic evaluator = value.Geometry.Evaluator;
                        double min = 0, max = 0; evaluator.GetParamExtents(out min, out max);
                        int count = 0; double[] coordinates = Array.Empty<double>();
                        evaluator.GetStrokes(min, max, 0.0001, out count, out coordinates);
                        if (count < 2 || count > 8192 || coordinates.Length != count * 2 || points + count > 100000)
                            throw new InvalidOperationException("Curve tessellation exceeds the snapshot budget.");
                        var strokes = new JArray();
                        foreach (double coordinate in coordinates)
                        {
                            if (double.IsNaN(coordinate) || double.IsInfinity(coordinate)) throw new InvalidOperationException("Non-finite native stroke.");
                            strokes.Add(UnitConvert.CmToMm(coordinate));
                        }
                        points += count; item["strokes_mm"] = strokes;
                    }
                    switch (entity)
                    {
                        case SketchSplineHandle handle:
                            item["start_mm"]=X.Mm2(handle.StartSketchPoint.Geometry);
                            item["end_mm"]=X.Mm2(handle.EndSketchPoint.Geometry);break;
                        case SketchLine line: item["start_mm"] = X.Mm2(line.StartSketchPoint.Geometry); item["end_mm"] = X.Mm2(line.EndSketchPoint.Geometry); item["centerline"]=line.Centerline; break;
                        case SketchCircle circle: item["center_mm"] = X.Mm2(circle.CenterSketchPoint.Geometry); item["radius_mm"] = UnitConvert.CmToMm(circle.Radius); break;
                        case SketchArc arc: item["center_mm"] = X.Mm2(arc.CenterSketchPoint.Geometry); item["radius_mm"] = UnitConvert.CmToMm(arc.Radius);
                            item["start_mm"]=X.Mm2(arc.StartSketchPoint.Geometry); item["end_mm"]=X.Mm2(arc.EndSketchPoint.Geometry); break;
                        case SketchEllipse ellipse: item["center_mm"] = X.Mm2(ellipse.CenterSketchPoint.Geometry); item["major_radius_mm"] = UnitConvert.CmToMm(ellipse.MajorRadius); item["minor_radius_mm"] = UnitConvert.CmToMm(ellipse.MinorRadius); break;
                        case SketchEllipticalArc ellipticalArc:
                            item["start_mm"]=X.Mm2(ellipticalArc.StartSketchPoint.Geometry); item["end_mm"]=X.Mm2(ellipticalArc.EndSketchPoint.Geometry); break;
                        case SketchSpline spline:
                            if(spline.StartSketchPoint!=null) item["start_mm"]=X.Mm2(spline.StartSketchPoint.Geometry);
                            if(spline.EndSketchPoint!=null) item["end_mm"]=X.Mm2(spline.EndSketchPoint.Geometry); break;
                        case SketchControlPointSpline spline:
                            if(spline.StartSketchPoint!=null) item["start_mm"]=X.Mm2(spline.StartSketchPoint.Geometry);
                            if(spline.EndSketchPoint!=null) item["end_mm"]=X.Mm2(spline.EndSketchPoint.Geometry); break;
                    }
                    // Polygon constraints also have native type PatternConstraint, but are
                    // not owned by RectangularPatterns/CircularPatterns. Read their actual
                    // members from SketchEntity.Constraints instead of inventing a definition.
                    if(hasPatternConstraints) try
                    {
                        foreach(object attached in entity.Constraints)
                        {
                            X.Deadline(ctx,"reading native pattern constraint members");
                            if(attached is not PatternConstraint) continue;
                            if(++patternLinks>100000) throw new InvalidOperationException("Native pattern association budget exceeded.");
                            string key=SketchReferences.Describe(doc,attached,"sketch_constraint");
                            if(!patternMembers.TryGetValue(key,out var members)) patternMembers[key]=members=new System.Collections.Generic.List<SketchEntity>();
                            if(members.Count>=2000) throw new InvalidOperationException("Native pattern association budget exceeded.");
                            members.Add(entity);
                        }
                    }
                    catch(COMException ex) when(ex.ErrorCode==unchecked((int)0x8000FFFF))
                    {
                        // This says nothing about whether the entity has constraints.
                        // Keep its valid geometry and invalidate the reverse index.
                        partial=true;patternMembershipUnavailable=true;
                        readErrors++;
                        item["pattern_membership_state"]="unavailable";
                        item["pattern_membership_reason"]="Inventor could not enumerate this entity's pattern associations (E_UNEXPECTED).";
                        warnings.Add((string)item["pattern_membership_reason"]!);
                    }
                    if(includeReferences)
                    {
                        RegisterBinding(entity,item,"Entity");
                        var structuralSubparts = new JArray();
                        foreach(var anchor in NativeSketchConstraints.Anchors(entity))
                        {
                            RegisterBinding((SketchEntity)anchor.point,item,anchor.subpart);
                            if(imageStructure?.Owner((SketchEntity)anchor.point) is SketchImage anchorImage)
                                structuralSubparts.Add(new JObject { ["subpart"] = anchor.subpart,
                                    ["owner_image_id"] = SketchReferences.Describe(doc, anchorImage, "sketch_image") });
                        }
                        if(structuralSubparts.Count>0) item["structural_subparts"] = structuralSubparts;
                    }
                }
                else if (native is DimensionConstraint dimension)
                {
                    item["native_type"] = dimension.Type.ToString(); item["driven"] = dimension.Driven;
                    item["name"] = dimension.Parameter.Name; item["expression"] = dimension.Parameter.Expression;
                    item["text_mm"] = X.Mm2(dimension.TextPoint);
                    bool angular=dimension is TwoLineAngleDimConstraint or ThreePointAngleDimConstraint;
                    double measured=Convert.ToDouble(dimension.Parameter.Value,System.Globalization.CultureInfo.InvariantCulture);
                    item[angular ? "value_deg" : "value_mm"]=angular ? measured*180/Math.PI : UnitConvert.CmToMm(measured);
                    item["parameter_units"]=dimension.Parameter.get_Units();
                }
                else if (native is GeometricConstraint constraint)
                {
                    item["native_type"] = constraint.Type.ToString(); item["deletable"] = constraint.Deletable;
                    var involved=NativeSketchConstraints.Entities(constraint);
                    if(constraint is PatternConstraint)
                    {
                        var pattern=NativeSketchPatterns.Find(ctx,sketch,constraint);
                        if(pattern!=null) item["pattern_definition"]=NativeSketchPatterns.Read(doc,pattern);
                        else
                        {
                            item["pattern_definition_state"]="unavailable";
                            item["pattern_definition_reason"]="Native constraint has no exposed rectangular or circular pattern definition.";
                        }
                        if(patternMembershipUnavailable)
                        {
                            // A successful lookup on other entities is only a subset.
                            // Do not publish that subset as a complete association.
                            involved=null;partial=true;
                            readErrors++;
                            item["binding_reason"]="Native pattern associations are incomplete because an entity's membership could not be read.";
                        }
                        else if(patternMembers.TryGetValue(SketchReferences.Describe(doc,constraint,"sketch_constraint"),out var members)) involved=members.ToArray();
                        else throw new InvalidOperationException("Native pattern constraint members are unavailable.");
                    }
                    item["binding_state"]="unavailable";
                    if(involved!=null && involved.Length>0)
                    {
                        var anchor=NativeSketchConstraints.Anchors(involved[0]).FirstOrDefault();
                        if(anchor.point!=null) item["annotation_mm"]=X.Mm2(anchor.point.Geometry);
                        if(includeReferences)
                        {
                            var bindings=new JArray();bool complete=true;
                            foreach(var member in involved)
                            {
                                X.Deadline(ctx,"reading native constraint associations");
                                string key=SketchReferences.Describe(doc,member,"sketch_entity");
                                if(!nativeBindings.TryGetValue(key,out var related)) {complete=false;break;}
                                foreach(JObject binding in related)
                                    if(!bindings.Any(b=>JToken.DeepEquals(b,binding))) bindings.Add(binding.DeepClone());
                            }
                            if(complete)
                            {
                                if(bindings.Count>2000 || relatedCount+bindings.Count>100000) throw new InvalidOperationException("Native constraint association budget exceeded.");
                                relatedCount+=bindings.Count;item["related_entities"]=bindings;item["binding_state"]="complete";
                            }
                            else { readErrors++;partial=true;item["binding_reason"]="Native constraint members are not completely represented."; }
                        }
                        else item["binding_state"]="preview_annotation_only";
                    }
                }
                else if (native is SketchImage image)
                {
                    item["native_type"] = image.Type.ToString();
                    item["width_mm"] = image.Width * 10; item["height_mm"] = image.Height * 10;
                    item["visible"] = image.Visible; item["transparent"] = image.Transparent;
                    item["linked_to_file"] = image.LinkedToFile;
                    item["selection_state"] = "read_only_image";
                    if (!image.LinkedToFile && !image.Transparent)
                    {
                        var current = NativeSketchImageLiveFrame.CaptureCurrent(image);
                        current["schema"] = 1;
                        current["source_kind"] = "native_bitmap";
                        current["asset_state"] = "deferred";
                        current["visible"] = image.Visible;
                        current["transparent"] = false;
                        item["image_current"] = current;
                    }
                    // Original-source metadata has its own frame and dimensions. A missing or
                    // stale persistent pair must not discard the independent current frame.
                    item["managed_original_state"] = "absent";
                    try
                    {
                        var imageFrame = NativeSketchImageFrame.Read(doc, image);
                        var imageSource = NativeSketchImagePayload.Describe(image);
                        if (imageFrame != null && imageSource != null)
                        {
                            item["image_frame"] = imageFrame; item["image_source"] = imageSource;
                            item["representation_state"] = "geometry_and_original_source";
                            item["managed_original_state"] = "available";
                        }
                    }
                    catch { item["managed_original_state"] = "unavailable"; }
                    if(item["image_current"]==null) throw new InvalidOperationException("Linked or transparent image geometry is unsupported.");
                }
                else if (native is TextBox text)
                { item["text_glyphs"]=NativeSketchTextGlyphs.Capture(ctx,doc,text,ref points,ref textTriangles);
                    item["text_bounds_mm"]=NativeSketchTextGlyphs.Bounds(text);
                    item["text"] = text.Text; item["formatted_text"] = text.FormattedText; item["rotation_deg"] = text.Rotation * 180 / Math.PI;
                    item["text_mm"] = X.Mm2(text.RangeBox.MinPoint); }
                else
                    throw new InvalidOperationException("Native data for this sketch object is not yet supported.");
                item["supported"] = native is not SketchImage;
                if(native is SketchImage)
                {
                    partial=true;
                    item["unavailable_reason"]="Image editing is unavailable; the opaque bitmap is a read-only background.";
                }
            }
            catch (Exception ex)
            {
                readErrors++;
                partial = true; item["supported"] = false;
                item["unavailable_reason"] = "Native object could not be represented completely: " + ex.GetType().Name;
                warnings.Add((string)item["unavailable_reason"]!);
            }
            destination.Add(item);
            if(ReferenceEquals(destination,entities)) representedEntities.Add(native);
        }
        foreach (SketchEntity entity in sketch.SketchEntities) Add(entity, Kind(entity), entities);
        foreach (TextBox text in sketch.TextBoxes) Add(text, "text", entities);
        foreach (SketchImage image in sketch.SketchImages) Add(image, "image", entities);
        try
        {
            if(splineHandles==null) throw new InvalidOperationException();
            int Slot(object value)
            {
                int found=-1;
                for(int i=0;i<representedEntities.Count;i++)
                    if(SketchReferences.SameComObject(representedEntities[i],value))
                    { if(found>=0) throw new InvalidOperationException("Duplicate spline structural entity.");found=i; }
                if(found<0) throw new InvalidOperationException("Spline structural entity is omitted.");
                return found;
            }
            foreach(var handle in splineHandles.Handles)
            {
                var owner=NativeSketchSplineHandleStructure.DescribeOwner(handle,sketch);
                entities[Slot(handle)]["spline_handle"]=new JObject {
                    ["schema"]=1,["owner_slot"]=Slot(owner.Spline),["fit_point_slot"]=Slot(owner.FitPoint),
                    ["start_slot"]=Slot(handle.StartSketchPoint),["end_slot"]=Slot(handle.EndSketchPoint),["fit_point_role"]=owner.Role };
            }
            SketchMutationCompleteness.ValidateSplineHandles(entities);
        }
        catch { readErrors++;partial=true;warnings.Add("Spline handle ownership is not completely represented."); }
        if(includeReferences)
        {
            // Collection truncation or an image-key failure must not publish a dangling
            // ownership assertion. Preserve the geometry, disable selection, and keep partial.
            var imageIds = new System.Collections.Generic.HashSet<string>(entities.OfType<JObject>()
                .Where(e => (string?)e["type"] == "image" && e["id"]?.Type == JTokenType.String)
                .Select(e => (string)e["id"]!), StringComparer.Ordinal);
            foreach(JObject item in entities)
            {
                bool missingOwner = item["structural_owner_image_id"] is JValue owner && !imageIds.Contains((string)owner!);
                bool missingSubpartOwner = item["structural_subparts"] is JArray subparts
                    && subparts.Any(s => !imageIds.Contains((string)s["owner_image_id"]!));
                if(!missingOwner && !missingSubpartOwner) continue;
                item["selection_state"] = "unavailable";
                item.Remove("structural_owner_image_id"); item.Remove("structural_subparts");
                partial = true;
                readErrors++;
                warnings.Add("Image structural ownership was omitted by the snapshot budget or reference failure.");
            }
        }
        foreach (DimensionConstraint dimension in sketch.DimensionConstraints) Add(dimension, "dimension", dimensions);
        foreach (GeometricConstraint constraint in sketch.GeometricConstraints) Add(constraint, "constraint", constraints);
        var snapshot = new JObject
        {
            ["schema_version"] = 1, ["document_id"] = documentId, ["revision"] = revision, ["sketch_id"] = sketchId,
            ["sketch_name"] = sketch.Name, ["frame"] = DesignContextXrHandler.SketchFrame(app, sketch), ["visible"] = sketch.Visible,
            ["entities"] = entities, ["dimensions"] = dimensions, ["constraints"] = constraints,
            ["constraint_status"] = sketch.ConstraintStatus.ToString(), ["partial"] = partial, ["warnings"] = warnings,
            ["supported_variants"] = new JArray(EditSketchHandler.SupportedVariants),
            ["spline_fit_method"] = (int)app.SketchOptions.SplineFitMethod,
            ["limits"] = new JObject { ["entities_per_collection"] = 2000, ["stroke_points"] = 100000, ["points_per_curve"] = 8192,
                ["related_per_constraint"]=2000,["related_per_snapshot"]=100000 }
        };
        JArray boundarySlots=new JArray();
        try { if(imageStructure==null) throw new InvalidOperationException(); boundarySlots=imageStructure.BoundarySlots(representedEntities,()=>X.Deadline(ctx,"mapping native image ownership")); }
        catch { readErrors++; }
        JObject CollectionCount(int nativeCount,int representedCount)=>new JObject { ["native_count"]=nativeCount,["represented_count"]=representedCount };
        snapshot["mutation_completeness"]=new JObject
        {
            ["schema"]=1,["state"]=readErrors==0 && omitted==0 ? (boundarySlots.Count==0 ? "complete" : "read_only_image_backgrounds") : "incomplete",
            ["omitted_count"]=omitted,["read_error_count"]=readErrors,
            ["collections"]=new JObject
            {
                ["entities"]=CollectionCount(sketch.SketchEntities.Count+sketch.TextBoxes.Count+sketch.SketchImages.Count,entities.Count),
                ["dimensions"]=CollectionCount(sketch.DimensionConstraints.Count,dimensions.Count),
                ["constraints"]=CollectionCount(sketch.GeometricConstraints.Count,constraints.Count)
            },
            ["image_boundaries"]=boundarySlots
        };
        return snapshot;
    }

    internal static JObject PreviewData(JObject native)
    {
        var entities = new JArray();
        foreach (var entity in (JArray)native["entities"]!)
        { var lite = (JObject)entity.DeepClone(); lite.Remove("strokes_mm"); lite.Remove("id"); entities.Add(lite); }
        return new JObject { ["sketch_name"] = native["sketch_name"], ["frame"] = native["frame"]!.DeepClone(),
            ["visible"] = native["visible"], ["entities"] = entities, ["native_entities"] = native["entities"]!.DeepClone(),
            ["dimensions"] = native["dimensions"]!.DeepClone(), ["constraint_status"] = native["constraint_status"],
            ["native_constraints"] = native["constraints"]!.DeepClone(),
            ["geometric_constraints"] = new JArray(((JArray)native["constraints"]!).Select(c =>
            {
                var annotation=new JObject {["type"]=c["native_type"]};
                if(c["annotation_mm"] is JArray point) annotation["annotation_mm"]=point.DeepClone();
                return annotation;
            })),
            ["partial"] = native["partial"], ["mutation_completeness"]=native["mutation_completeness"]?.DeepClone() };
    }

    internal static string Kind(SketchEntity entity) => entity switch
    {
        SketchSplineHandle => "spline_handle", SketchLine => "line", SketchCircle => "circle", SketchArc => "arc", SketchEllipse => "ellipse",
        SketchEllipticalArc => "elliptical_arc", SketchSpline => "spline", SketchControlPointSpline => "control_point_spline",
        SketchPoint => "point", SketchOffsetSpline => "offset_spline", _ => entity.Type.ToString()
    };
}

internal static class SketchReferences
{
    public static bool SameComObject(object left,object right)
    {
        IntPtr a=IntPtr.Zero,b=IntPtr.Zero;
        try
        { a=System.Runtime.InteropServices.Marshal.GetIUnknownForObject(left); b=System.Runtime.InteropServices.Marshal.GetIUnknownForObject(right); return a==b; }
        finally
        { if(b!=IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(b); if(a!=IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(a); }
    }
    public static string Describe(global::Inventor.Document doc, object entity, string kind)
    {
        if(!PersistentEntityReference.SupportsContextFreeKey(kind)) throw new ArgumentException("Not a context-free sketch reference type.");
        // Inventor 2027 GetReferenceKey: non-B-Rep objects ignore KeyContext.
        // Creating/saving an empty B-Rep table is unnecessary and can fail in SaveContextToArray.
        byte[] key=Array.Empty<byte>(); ((dynamic)entity).GetReferenceKey(ref key,0);
        return new PersistentEntityReference { DocumentId=EntityReferences.DocumentId(doc),EntityType=kind,
            Key=key,Context=Array.Empty<byte>() }.Encode();
    }

    public static object Resolve(global::Inventor.Document doc, string id, string kind)
    {
        var reference = PersistentEntityReference.Decode(id);
        if (reference.DocumentId != EntityReferences.DocumentId(doc) || reference.EntityType != kind)
            throw new ArgumentException("Sketch reference belongs to a different document or entity type.");
        byte[] data = reference.Context; var manager = doc.ReferenceKeyManager;
        int context = data.Length==0 ? 0 : manager.LoadContextFromArray(ref data);
        try
        {
            byte[] key = reference.Key;
            if (!manager.CanBindKeyToObject(ref key, context, out object entity, out object details) || entity is ObjectCollection)
                throw new ArgumentException("Sketch reference is unresolved or ambiguous: refresh the snapshot.");
            if (kind == "sketch_entity" && entity is SketchEntity sketchEntity)
                NativeSketchImageStructure.RefuseMutation(sketchEntity);
            return entity;
        }
        finally { if(data.Length>0) manager.ReleaseKeyContext(context); }
    }
}
#endif
