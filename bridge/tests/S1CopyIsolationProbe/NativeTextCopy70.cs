using System.Text.Json;
using System.Text.Json.Nodes;
using Inventor;
using Bimwright.Ipt.Shared.Handlers;

internal static partial class Program
{
    private static void RunText(PlanarSketch sketch)
    {
        GuardDocument();var baseline=CaptureText(sketch);Report["baseline"]=baseline.DeepClone();Transaction? transaction=null;Exception? primary=null;
        Report["text_name_scope"]="TextBox exposes no Name in the installed interface: native keys, document/sketch names, text content, TextStyle.Name and TextStyle.InternalName are captured without an invented TextBox name.";
        Report["text_glyph_frame"]="GetFacetsInfo raw model coordinates, converted cm to mm. Expected translation comes from two native SketchToModelSpace calls; no glyph coordinate mutation or reconstruction.";
        try
        {
            transaction=Native("TransactionManager.StartTransaction.text70",()=>App!.TransactionManager.StartTransaction((_Document)OwnedDocument!,"S1 isolated fitted TextBox copy70, always aborted"));
            Require(!Native("Transaction.HasParentTransaction.text70",()=>transaction.HasParentTransaction),"Text copy research refuses nested transactions.");
            var source=Native("TextBoxes.AddFitted.text70",()=>sketch.TextBoxes.AddFitted(Point(10,20),"Copy70",Type.Missing));
            Native("Sketch.Solve.textSeed",()=>sketch.Solve());Require(Native("Document.Update2.textSeed",()=>OwnedDocument!.Update2(false)),"Fitted source update failed.");
            Require(Native("TextBoxes.Count.seed",()=>sketch.TextBoxes.Count)==1,"Text fixture must contain exactly one original fitted TextBox.");
            var original=ReadFittedText70(source);Report["source_before_copy"]=original.DeepClone();Report["before_copy"]=CaptureText(sketch);
            string originalKey=Key(source);
            var objects=Native("TransientObjects.CreateObjectCollection.textCopy",()=>App!.TransientObjects.CreateObjectCollection());
            Native("ObjectCollection.Add.TextBoxOnly",()=>objects.Add(source));
            var delta=Native("TransientGeometry.CreateVector2d.textCopy",()=>App!.TransientGeometry.CreateVector2d(UnitConvert.MmToCm(12),UnitConvert.MmToCm(-8)));
            GuardDocument();var returned=Native("Sketch.MoveSketchObjects.TextBox.CopyTrue.RemoveFalse",()=>sketch.MoveSketchObjects(objects,delta,true,false));
            int immediateCount=Native("TextBoxes.Count.afterCopyApi",()=>sketch.TextBoxes.Count);Report["text_count_after_copy_api"]=immediateCount;
            Tick("text.copy.native_count",new JsonObject {["count"]=immediateCount});
            Report["returned_enumerator_null"]=returned==null;
            Report["native_returned_objects"]=Native("TextCopyResult.enumerate",()=>returned==null ? new JsonArray() : new JsonArray(returned.Cast<object>().Select(item=>(JsonNode)new JsonObject {["key"]=Key(item),["native_type"]=Native<string>("TextCopyResult.object.Type",()=> (string)((dynamic)item).Type.ToString())}).ToArray()));
            Native("Sketch.Solve.afterTextCopy",()=>sketch.Solve());Require(Native("Document.Update2.afterTextCopy",()=>OwnedDocument!.Update2(false)),"Native text copy update failed.");
            int count=Native("TextBoxes.Count.afterCopyUpdate",()=>sketch.TextBoxes.Count);Report["text_count_after_copy_update"]=count;
            Tick("text.copy.updated_count",new JsonObject {["count"]=count});
            Report["after_copy"]=CaptureText(sketch);Report["original_exact_after_copy"]=JsonNode.DeepEquals(original,ReadFittedText70(source));
            Require(count==2,"Native fitted TextBox copy did not produce exactly two TextBoxes; no alternative creation, context adjustment or substitute copy will be attempted.");
            var copies=Native("TextBoxes.enumerate.newCopy",()=>sketch.TextBoxes.Cast<TextBox>().Where(text=>Key(text)!=originalKey).ToArray());
            Require(copies.Length==1,"Native TextBox copy does not have one distinct new identity.");var copy=copies[0];string copyKey=Key(copy),copyOriginKey=Key(copy.OriginSketchPoint);
            Require(copyOriginKey!=Key(source.OriginSketchPoint),"Copied fitted TextBox shares original origin-point identity.");
            Report["copy_text_key"]=copyKey;Report["copy_origin_key"]=copyOriginKey;
            var copyState=ReadFittedText70(copy);var translation=CheckTextTranslation70(sketch,original,copyState,12,-8);Report["initial_copy_translation"]=translation;
            Require((bool?)Report["original_exact_after_copy"]==true && (bool?)translation["pure_native_translation"]==true,"Native text copy changed the original or is not a pure raw-glyph translation.");
            RequireOriginalConstraints((JsonObject)Report["before_copy"]!,(JsonObject)Report["after_copy"]!);
            var edits=new JsonArray();Report["copy_native_edits"]=edits;double dx=12,dy=-8;
            foreach(var movement in new[]{(x:3d,y:4d),(x:-2d,y:5d)})
            {
                dx+=movement.x;dy+=movement.y;
                var edit=new JsonObject {["delta_mm"]=new JsonArray(movement.x,movement.y),["total_delta_from_original_mm"]=new JsonArray(dx,dy)};edits.Add(edit);
                GuardDocument();var location=Point(10+dx,20+dy);
                Native("TextBox.Origin.set.actualCopy",()=>{copy.Origin=location;});
                Native("Sketch.Solve.afterTextOriginEdit",()=>sketch.Solve());Require(Native("Document.Update2.afterTextOriginEdit",()=>OwnedDocument!.Update2(false)),"Native copied-text origin edit update failed.");
                edit["state"]=CaptureText(sketch);edit["native_copy"]=ReadFittedText70(copy);
                edit["translation"]=CheckTextTranslation70(sketch,original,(JsonObject)edit["native_copy"]!,dx,dy);
                bool originalExact=JsonNode.DeepEquals(original,ReadFittedText70(source));edit["original_exact"]=originalExact;
                bool keys=Key(copy)==copyKey && Key(copy.OriginSketchPoint)==copyOriginKey;edit["copy_and_origin_keys_preserved"]=keys;
                Require(Native("TextBoxes.Count.afterOriginEdit",()=>sketch.TextBoxes.Count)==2 && keys && originalExact && (bool?)edit["translation"]?["pure_native_translation"]==true,"Native copy origin edit changed identities, the original, TextBox count or raw translated glyphs.");
                RequireOriginalConstraints((JsonObject)Report["before_copy"]!,(JsonObject)edit["state"]!);edit["original_constraints_unchanged"]=true;
            }
            Report["copy_and_second_native_edit_verified"]=true;
        }
        catch(Exception error) {primary=error;Report["case_failure"]=Error(error);throw;}
        finally
        {
            if(transaction!=null)
            {
                try
                {
                    GuardDocument();Require(Same(Native("TransactionManager.CurrentTransaction.beforeTextAbort",()=>App!.TransactionManager.CurrentTransaction),transaction),"Owned text-copy transaction is no longer current; refusing unrelated Abort.");
                    Native("Transaction.Abort.text70",()=>transaction.Abort());
                    var restored=CaptureText(sketch);Report["after_abort"]=restored;bool exact=JsonNode.DeepEquals(baseline,restored);Report["exact_abort"]=exact;
                    Require(exact,"Text copy Abort did not restore exact native geometry, styles and parameter baseline.");
                }
                catch(Exception rollbackError) {Report["rollback_failure"]=Error(rollbackError);if(primary==null) throw;}
            }
        }
        Require((bool?)Report["exact_abort"]==true,"Exact native text-copy Abort is required.");
    }

    private static JsonObject CaptureText(PlanarSketch sketch)=>Native("TextNativeReadback.capture",()=>
    {
        GuardDocument();Require(OwnedDocument!.ComponentDefinition.Sketches.Count==1 && Same(OwnedDocument.ComponentDefinition.Sketches[1],sketch),"Owned text sketch collection changed.");
        var texts=new JsonArray(sketch.TextBoxes.Cast<TextBox>().Select(text=>(JsonNode)ReadFittedText70(text)).OrderBy(node=>(string?)node["key"],StringComparer.Ordinal).ToArray());
        var lines=new JsonArray(sketch.SketchLines.Cast<SketchLine>().Select(line=>(JsonNode)LineState(line)).OrderBy(node=>(string?)node["key"],StringComparer.Ordinal).ToArray());
        var points=new JsonArray(sketch.SketchPoints.Cast<SketchPoint>().Select(point=>(JsonNode)new JsonObject {["key"]=Key(point),["point_mm"]=Coordinates(point.Geometry),["hole_center"]=point.HoleCenter}).OrderBy(node=>(string?)node["key"],StringComparer.Ordinal).ToArray());
        var constraints=new JsonArray();foreach(GeometricConstraint constraint in sketch.GeometricConstraints)
        {
            var item=new JsonObject {["key"]=Key(constraint),["type"]=constraint.Type.ToString(),["deletable"]=constraint.Deletable};constraints.Add(item);
            if(constraint is CoincidentConstraint coincidence) {item["entity_one_key"]=Key(coincidence.EntityOne);item["entity_two_key"]=Key(coincidence.EntityTwo);}
            else if(constraint is GroundConstraint ground) item["native_entity_key"]=Key(ground.Entity);
            else {Report["unread_text_constraint"]=item.DeepClone();throw new ResearchFailure("Unexpected constraint type in text-only copy research; native member associations are not inferred.");}
        }
        var parameters=new JsonArray();foreach(Parameter parameter in OwnedDocument.ComponentDefinition.Parameters)
            parameters.Add(new JsonObject {["key"]=Key(parameter),["name"]=parameter.Name,["expression"]=parameter.Expression,["units"]=parameter.get_Units(),["database_scalar"]=Convert.ToDouble(parameter.Value),["health"]=parameter.HealthStatus.ToString()});
        var styles=new JsonArray(OwnedDocument.TextStyles.Cast<TextStyle>().Select(style=>(JsonNode)ReadTextStyle70(style)).OrderBy(node=>(string?)node["internal_name"],StringComparer.Ordinal).ThenBy(node=>(string?)node["name"],StringComparer.Ordinal).ToArray());
        Require(sketch.DimensionConstraints.Count==0 && sketch.SketchArcs.Count==0 && sketch.SketchCircles.Count==0 && sketch.SketchImages.Count==0,"Unexpected arc/circle/image/dimension geometry in text-only fixture.");
        Require(sketch.ConstraintStatus!=ConstraintStatusEnum.kOverConstrainedConstraintStatus,"Native text solver conflict.");
        return new JsonObject {["document_id"]=OwnedDocument.InternalName,["title"]=OwnedDocument.DisplayName,["path"]=OwnedDocument.FullFileName,["sketch_key"]=Key(sketch),["sketch_name"]=sketch.Name,["sketch_count"]=OwnedDocument.ComponentDefinition.Sketches.Count,
            ["entity_count"]=sketch.SketchEntities.Count,["text_count"]=sketch.TextBoxes.Count,["point_count"]=sketch.SketchPoints.Count,["line_count"]=sketch.SketchLines.Count,["dimension_count"]=sketch.DimensionConstraints.Count,["solver_status"]=sketch.ConstraintStatus.ToString(),
            ["texts"]=texts,["points"]=points,["lines"]=lines,["constraints"]=new JsonArray(constraints.OrderBy(node=>(string?)node!["key"],StringComparer.Ordinal).Select(node=>node!.DeepClone()).ToArray()),["parameters"]=parameters,["text_styles"]=styles};
    });

    private static JsonObject ReadFittedText70(TextBox text)=>Native("TextReadback.fitted70",()=>
    {
        Require(text.Fitted,"Research expects an actual fitted native TextBox.");
        int vertices=0,facets=0;double[] raw=Array.Empty<double>();int[] indices=Array.Empty<int>();
        Native("TextBox.GetFacetsInfo.rawModel70",()=>text.GetFacetsInfo(out vertices,out facets,out raw,out indices));
        Require(vertices>0 && vertices<=8192 && raw.Length==vertices*3 && raw.All(double.IsFinite) && facets>0 && facets<=8192 && indices.Length==facets*3 && indices.All(index=>index>=1 && index<=vertices),"Native text glyph mesh exceeds research bounds or has invalid coordinates/indices.");
        var box=text.RangeBox;
        return new JsonObject {["key"]=Key(text),["native_type"]=text.Type.ToString(),["origin_key"]=Key(text.OriginSketchPoint),["origin_mm"]=Coordinates(text.Origin),["rotation_deg"]=UnitConvert.RadToDeg(text.Rotation),["fitted"]=text.Fitted,
            ["text"]=text.Text,["formatted_text"]=text.FormattedText,["width_mm"]=UnitConvert.CmToMm(text.Width),["height_mm"]=UnitConvert.CmToMm(text.Height),["fitted_width_mm"]=UnitConvert.CmToMm(text.FittedTextWidth),["fitted_height_mm"]=UnitConvert.CmToMm(text.FittedTextHeight),
            ["width_scale"]=text.WidthScale,["horizontal"]=text.HorizontalJustification.ToString(),["vertical"]=text.VerticalJustification.ToString(),["single_line"]=text.SingleLineText,["line_spacing"]=text.LineSpacing,["line_spacing_type"]=text.LineSpacingType.ToString(),
            ["style"]=ReadTextStyle70(text.Style),["range_mm"]=new JsonArray(UnitConvert.CmToMm(box.MinPoint.X),UnitConvert.CmToMm(box.MinPoint.Y),UnitConvert.CmToMm(box.MaxPoint.X),UnitConvert.CmToMm(box.MaxPoint.Y)),
            ["vertex_count"]=vertices,["facet_count"]=facets,["raw_model_vertices_mm"]=JsonSerializer.SerializeToNode(raw.Select(UnitConvert.CmToMm).ToArray()),["indices_one_based"]=JsonSerializer.SerializeToNode(indices)};
    });

    private static JsonObject ReadTextStyle70(TextStyle style)=>Native("TextStyle.Readback70",()=>new JsonObject {["key"]=Key(style),["name"]=style.Name,["internal_name"]=style.InternalName,["location"]=style.StyleLocation.ToString(),
        ["font"]=style.Font,["font_size_mm"]=UnitConvert.CmToMm(style.FontSize),["bold"]=style.Bold,["italic"]=style.Italic,["underline"]=style.Underline,["rotation_deg"]=UnitConvert.RadToDeg(style.Rotation),["width_scale"]=style.WidthScale,
        ["horizontal"]=style.HorizontalJustification.ToString(),["vertical"]=style.VerticalJustification.ToString(),["line_spacing"]=style.LineSpacing,["line_spacing_type"]=style.LineSpacingType.ToString()});

    private static JsonObject CheckTextTranslation70(PlanarSketch sketch,JsonObject original,JsonObject actual,double dx,double dy)
    {
        const double tolerance=1e-5;
        var zero=Native("Sketch.SketchToModelSpace.zero70",()=>sketch.SketchToModelSpace(Point(0,0)));
        var shifted=Native("Sketch.SketchToModelSpace.delta70",()=>sketch.SketchToModelSpace(Point(dx,dy)));
        double[] delta={UnitConvert.CmToMm(shifted.X-zero.X),UnitConvert.CmToMm(shifted.Y-zero.Y),UnitConvert.CmToMm(shifted.Z-zero.Z)};
        var before=((JsonArray)original["raw_model_vertices_mm"]!).Select(node=>(double)node!).ToArray();var after=((JsonArray)actual["raw_model_vertices_mm"]!).Select(node=>(double)node!).ToArray();
        var beforeIndices=((JsonArray)original["indices_one_based"]!).Select(node=>(int)node!).ToArray();var afterIndices=((JsonArray)actual["indices_one_based"]!).Select(node=>(int)node!).ToArray();
        bool counts=before.Length==after.Length && beforeIndices.Length==afterIndices.Length;
        var used=new bool[after.Length/3];double maximum=0;int matchedVertices=0;
        if(counts)
        for(int i=0;i<before.Length;i+=3)
        {
            double best=double.PositiveInfinity;int nearest=-1;
            for(int j=0;j<after.Length;j+=3)
            {
                if(used[j/3]) continue;double squared=0;for(int k=0;k<3;k++) squared+=Math.Pow(before[i+k]+delta[k]-after[j+k],2);
                if(squared<best) {best=squared;nearest=j/3;}
            }
            if(nearest<0 || Math.Sqrt(best)>tolerance) break;used[nearest]=true;maximum=Math.Max(maximum,Math.Sqrt(best));matchedVertices++;
        }
        // Compare actual native indexed triangles geometrically, independent of
        // vertex order, duplicated vertices and facet enumeration order, while
        // preserving triangle winding under the requested pure translation.
        var usedTriangles=new bool[afterIndices.Length/3];int matchedTriangles=0;
        int[][] orders={new[]{0,1,2},new[]{1,2,0},new[]{2,0,1}};
        if(counts && matchedVertices==before.Length/3)
        for(int i=0;i<beforeIndices.Length;i+=3)
        {
            int found=-1;
            for(int j=0;j<afterIndices.Length && found<0;j+=3)
            {
                if(usedTriangles[j/3]) continue;
                foreach(var order in orders)
                {
                    bool match=true;for(int vertex=0;vertex<3 && match;vertex++)
                    {
                        double squared=0;for(int k=0;k<3;k++) squared+=Math.Pow(before[(beforeIndices[i+vertex]-1)*3+k]+delta[k]-after[(afterIndices[j+order[vertex]]-1)*3+k],2);
                        match=squared<=tolerance*tolerance;
                    }
                    if(match) {found=j/3;break;}
                }
            }
            if(found<0) break;usedTriangles[found]=true;matchedTriangles++;
        }
        var expectedFormat=(JsonObject)original.DeepClone();var actualFormat=(JsonObject)actual.DeepClone();
        foreach(string field in new[]{"key","origin_key","origin_mm","range_mm","raw_model_vertices_mm","indices_one_based"}) {expectedFormat.Remove(field);actualFormat.Remove(field);}
        ((JsonObject)expectedFormat["style"]!).Remove("key");((JsonObject)actualFormat["style"]!).Remove("key");
        bool lengths=true;foreach(string field in new[]{"width_mm","height_mm","fitted_width_mm","fitted_height_mm"})
        {
            lengths &= Math.Abs((double)expectedFormat[field]!-(double)actualFormat[field]!)<=tolerance;expectedFormat.Remove(field);actualFormat.Remove(field);
        }
        bool format=lengths && JsonNode.DeepEquals(expectedFormat,actualFormat);var oldOrigin=(JsonArray)original["origin_mm"]!;var newOrigin=(JsonArray)actual["origin_mm"]!;
        bool origin=Math.Abs((double)newOrigin[0]!-(double)oldOrigin[0]!-dx)<=tolerance && Math.Abs((double)newOrigin[1]!-(double)oldOrigin[1]!-dy)<=tolerance;
        bool pure=counts && format && origin && matchedVertices==before.Length/3 && matchedTriangles==beforeIndices.Length/3;
        return new JsonObject {["expected_sketch_delta_mm"]=new JsonArray(dx,dy),["expected_model_delta_mm"]=JsonSerializer.SerializeToNode(delta),["distance_tolerance_mm"]=tolerance,["vertex_and_facet_counts_equal"]=counts,["matched_vertices"]=matchedVertices,["expected_vertices"]=before.Length/3,
            ["max_error_of_matched_vertices_mm"]=maximum,["matched_native_triangles"]=matchedTriangles,["expected_native_triangles"]=beforeIndices.Length/3,["format_font_style_and_dimensions_equivalent"]=format,["origin_translated_once"]=origin,["pure_native_translation"]=pure};
    }
}
