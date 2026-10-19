#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class NativeSketchPatterns
{
    internal static JObject Read(global::Inventor.Document document,object pattern)
    {
        JObject Value(object native,bool count=false,bool angle=false)
        {
            var parameter=native as Parameter;
            double value=parameter!=null ? Convert.ToDouble(parameter.Value) : native is string expression
                ? Convert.ToDouble(document.UnitsOfMeasure.GetValueFromExpression(expression,count ? UnitsTypeEnum.kUnitlessUnits : angle ? UnitsTypeEnum.kDegreeAngleUnits : UnitsTypeEnum.kMillimeterLengthUnits))
                : Convert.ToDouble(native,System.Globalization.CultureInfo.InvariantCulture);
            if(double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("Non-finite native pattern parameter.");
            var item=new JObject {["value"]=count ? value : angle ? value*180/Math.PI : UnitConvert.CmToMm(value)};
            if(parameter!=null){item["name"]=parameter.Name;item["expression"]=parameter.Expression;item["units"]=parameter.get_Units();}
            else if(native is string text) item["expression"]=text;
            return item;
        }
        var result=new JObject();
        if(pattern is SketchCircularPattern circular)
        {
            var d=circular.Definition;
            result["kind"]="circular";result["count"]=Value(d.Count,true);result["angle_deg"]=Value(d.Angle,false,true);
            result["natural_axis"]=d.NaturalAxisDirection;result["symmetric"]=d.Symmetric;result["associative"]=d.Associative;result["fitted"]=d.Fitted;
            result["suppressed_elements"]=new JArray(d.get_SuppressedElements() ?? Array.Empty<int>());result["element_count"]=circular.Elements.Count;
        }
        else
        {
            var rectangular=(SketchRectangularPattern)pattern;var d=rectangular.Definition;
            result["kind"]="rectangular";result["x_count"]=Value(d.XCount,true);result["x_spacing_mm"]=Value(d.XSpacing);
            result["two_directions"]=d.YDirectionEntity!=null;
            if(d.YDirectionEntity!=null) {result["y_count"]=Value(d.YCount,true);result["y_spacing_mm"]=Value(d.YSpacing);}
            result["natural_x"]=d.NaturalXDirection;result["symmetric_x"]=d.XDirectionSymmetric;
            result["natural_y"]=d.NaturalYDirection;result["symmetric_y"]=d.YDirectionSymmetric;result["associative"]=d.Associative;result["fitted"]=d.Fitted;
            result["suppressed_elements"]=new JArray(d.get_SuppressedElements() ?? Array.Empty<int>());result["element_count"]=rectangular.Elements.Count;
        }
        if((int)result["element_count"]!>2000) throw new InvalidOperationException("Native pattern exceeds the snapshot element budget.");
        return result;
    }
    internal static object? Find(InventorCommandContext ctx,PlanarSketch sketch,GeometricConstraint selected)
    {
        if(selected is not PatternConstraint) return null;
        foreach(SketchRectangularPattern pattern in sketch.RectangularPatterns)
        {
            X.Deadline(ctx,"finding the native rectangular sketch pattern");
            foreach(object constraint in pattern.Constraints)
                if(SketchReferences.SameComObject(constraint,selected)) return pattern;
        }
        foreach(SketchCircularPattern pattern in sketch.CircularPatterns)
        {
            X.Deadline(ctx,"finding the native circular sketch pattern");
            foreach(object constraint in pattern.Constraints)
                if(SketchReferences.SameComObject(constraint,selected)) return pattern;
        }
        return null;
    }
    internal static void Apply(InventorCommandContext ctx,Application app,global::Inventor.Document document,
        PlanarSketch sketch,string sketchId,string kind,JObject options)
    {
        bool edit=kind=="update" || kind=="delete",circular=kind=="circular";
        if(!edit && kind!="rectangular" && !circular) throw new ArgumentException("Unsupported native pattern kind.");
        string[] flags={"associative","fitted","natural_x","natural_y","symmetric_x","symmetric_y","natural_axis","symmetric"};
        string[] parameters={"x_count","y_count","x_spacing_mm","y_spacing_mm","count","angle_deg"};
        string[] allowed=kind=="delete" ? new[] {"references"} : flags.Concat(parameters).Concat(new[] {"references","two_directions","suppressed_elements"}).ToArray();
        if(options.Properties().Any(p=>!allowed.Contains(p.Name,StringComparer.Ordinal))) throw new ArgumentException("Unknown sketch pattern option.");
        foreach(string flag in flags.Concat(new[] {"two_directions"}))
            if(options.TryGetValue(flag,out var value) && value.Type!=JTokenType.Boolean) throw new ArgumentException("Pattern flags must be booleans.");
        string documentId=EntityReferences.DocumentId(document),revision=ctx.Events?.Revision(documentId) ?? "";
        if(options["references"] is not JArray refs || refs.Count<1 || refs.Count>2000 || refs.Any(r=>r is not JObject))
            throw new ArgumentException("Select current native sketch references for the pattern.");
        if(kind=="update" && options.Count==1) throw new ArgumentException("Select a native pattern property to update.");
        int sourceCount=edit ? 0 : refs.Count-(circular ? 1 : X.Bool(options,"two_directions",false) ? 2 : 1),ordinal=0;
        var resolved=new List<object>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(JObject reference in refs)
        {
            X.Deadline(ctx,"resolving sketch pattern selections");
            if(X.Str(reference,"document_id")!=documentId || X.Str(reference,"sketch_id")!=sketchId)
                throw new ArgumentException("Pattern selection belongs to another document or sketch.");
            if(X.Str(reference,"revision")!=revision) throw ConcurrencyFailure.StaleRevision((string?)reference["revision"],revision);
            string id=X.Str(reference,"entity_id"),type=X.Str(reference,"type"),part=X.Str(reference,"subpart");
            if((!seen.Add(id) && ordinal<sourceCount) || part!="Entity") throw new ArgumentException("Select distinct whole source geometries and whole native direction objects.");
            ordinal++;
            object native=SketchReferences.Resolve(document,id,edit ? "sketch_constraint" : "sketch_entity");
            if(edit)
            {
                if(refs.Count!=1 || type!="constraint" || native is not PatternConstraint constraint || !SketchReferences.SameComObject(constraint.Parent,sketch))
                    throw new ArgumentException("Pattern editing requires one current native pattern constraint.");
            }
            else if(native is not SketchEntity entity || !SketchReferences.SameComObject(entity.Parent,sketch) || type!=SketchEditorXrHandler.Kind(entity)
                || entity.ContainingSketchBlock!=null) throw new ArgumentException("Select native geometry owned directly by this sketch; block members need their block.");
            resolved.Add(native);
        }
        object? existing=edit ? Find(ctx,sketch,(GeometricConstraint)resolved[0]) ?? throw new ArgumentException("The native pattern definition cannot be resolved.") : null;
        if(kind=="delete")
        {
            if(existing is SketchRectangularPattern rectangular) rectangular.Delete();else ((SketchCircularPattern)existing!).Delete();return;
        }
        circular=existing is SketchCircularPattern || (!edit && circular);
        string[] relevant=circular ? new[] {"associative","fitted","natural_axis","symmetric","count","angle_deg","references","suppressed_elements"}
            : new[] {"associative","fitted","natural_x","natural_y","symmetric_x","symmetric_y","x_count","y_count","x_spacing_mm","y_spacing_mm","references","two_directions","suppressed_elements"};
        if(options.Properties().Any(p=>!relevant.Contains(p.Name,StringComparer.Ordinal))) throw new ArgumentException("Options do not match the selected pattern kind.");
        if(edit && options["two_directions"]!=null) throw new ArgumentException("Changing direction references requires a separate definition selection.");
        object ParameterInput(string name,bool count)
        {
            var token=options[name] ?? throw new ArgumentException("Missing pattern parameter: "+name);
            if(token.Type==JTokenType.String)
            {
                string expression=(string)token!;if(string.IsNullOrWhiteSpace(expression) || expression.Length>2048) throw new ArgumentException("Invalid native pattern expression.");
                double value=Convert.ToDouble(document.UnitsOfMeasure.GetValueFromExpression(expression,count ? UnitsTypeEnum.kUnitlessUnits : name=="angle_deg" ? UnitsTypeEnum.kDegreeAngleUnits : UnitsTypeEnum.kMillimeterLengthUnits));
                if(double.IsNaN(value) || double.IsInfinity(value) || value<=0 || (count && (value!=Math.Truncate(value) || value>2000))) throw new ArgumentException("Invalid evaluated pattern parameter.");
                return expression;
            }
            double number=X.Positive(options,name);if(count && (number!=Math.Truncate(number) || number>2000)) throw new ArgumentException("Pattern count must be an integer in 1–2000.");
            return count ? number : name=="angle_deg" ? UnitConvert.DegToRad(number) : UnitConvert.MmToCm(number);
        }
        int[]? suppression=null;
        if(options["suppressed_elements"]!=null)
        {
            if(options["suppressed_elements"] is not JArray indices || indices.Count>2000 || indices.Any(i=>i.Type!=JTokenType.Integer || (int)i<1 || (int)i>2000)
                || indices.Values<int>().Distinct().Count()!=indices.Count) throw new ArgumentException("Suppressed elements require distinct native indices in 1–2000.");
            suppression=indices.Values<int>().ToArray();
        }
        // Existing associative definitions expose driving Parameter objects. Editing
        // those preserves native dependencies; replacing a copied definition is
        // rejected by Inventor 2027 even for a count-only change.
        if(edit && options.Properties().All(p=>p.Name=="references" || p.Name=="suppressed_elements" || parameters.Contains(p.Name,StringComparer.Ordinal)))
        {
            var values=new Dictionary<string,object>();var native=new Dictionary<string,Parameter>();
            if(existing is SketchCircularPattern cp)
            {
                var d=cp.Definition;
                native["count"]=(Parameter)d.Count;native["angle_deg"]=(Parameter)d.Angle;
                foreach(var name in native.Keys) if(options[name]!=null) values[name]=ParameterInput(name,name=="count");
                ValidateBudget(document,d.Geometries.Count,values.TryGetValue("count",out var count) ? count : d.Count,1);
            }
            else
            {
                var d=((SketchRectangularPattern)existing!).Definition;
                native["x_count"]=(Parameter)d.XCount;native["x_spacing_mm"]=(Parameter)d.XSpacing;
                if(d.YDirectionEntity!=null){native["y_count"]=(Parameter)d.YCount;native["y_spacing_mm"]=(Parameter)d.YSpacing;}
                if(options.Properties().Any(p=>p.Name!="references" && p.Name!="suppressed_elements" && !native.ContainsKey(p.Name))) throw new ArgumentException("Y options require a second direction line.");
                foreach(var name in native.Keys) if(options[name]!=null) values[name]=ParameterInput(name,name.EndsWith("count",StringComparison.Ordinal));
                ValidateBudget(document,d.Geometries.Count,values.TryGetValue("x_count",out var nx) ? nx : d.XCount,
                    values.TryGetValue("y_count",out var ny) ? ny : d.YDirectionEntity==null ? 1 : d.YCount);
            }
            foreach(var value in values)
            {
                if(value.Value is string expression) native[value.Key].Expression=expression;
                else native[value.Key].Value=Convert.ToDouble(value.Value);
            }
            if(suppression!=null)
            {
                sketch.Solve();
                var elements=existing is SketchCircularPattern suppressionPattern ? suppressionPattern.Elements : ((SketchRectangularPattern)existing!).Elements;
                if(elements.Count>2000 || suppression.Any(index=>index>elements.Count)) throw new ArgumentException("Suppression index exceeds the current native pattern element count.");
                // Suppressed is a writable property of each native element;
                // changing it preserves the existing pattern and its parameters.
                var wanted=new HashSet<int>(suppression);
                foreach(SketchPatternElement element in elements)
                {
                    X.Deadline(ctx,"updating native pattern element suppression");
                    bool suppressed=wanted.Contains(element.Index);
                    if(element.Suppressed!=suppressed) element.Suppressed=suppressed;
                }
            }
            return;
        }
        var geometries=app.TransientObjects.CreateObjectCollection();
        if(circular)
        {
            SketchCircularPatternDefinition definition;
            if(existing is SketchCircularPattern pattern) definition=pattern.Definition.Copy();
            else
            {
                if(resolved.Count<2 || resolved[resolved.Count-1] is not (SketchPoint or SketchArc or SketchCircle)) throw new ArgumentException("Select source geometries followed by one whole point, arc or circle as circular axis.");
                foreach(var entity in resolved.Take(resolved.Count-1)) geometries.Add(entity);
                definition=sketch.CircularPatterns.CreateDefinition(geometries,resolved[resolved.Count-1],
                    X.Bool(options,"natural_axis",true),ParameterInput("count",true),ParameterInput("angle_deg",false),
                    X.Bool(options,"symmetric",false),X.Bool(options,"associative",true),X.Bool(options,"fitted",true));
            }
            if(edit)
            {
                if(options["count"]!=null) definition.Count=ParameterInput("count",true);
                if(options["angle_deg"]!=null) definition.Angle=ParameterInput("angle_deg",false);
                if(options["natural_axis"]!=null) definition.NaturalAxisDirection=X.Bool(options,"natural_axis",true);
                if(options["symmetric"]!=null) definition.Symmetric=X.Bool(options,"symmetric",false);
                if(options["associative"]!=null) definition.Associative=X.Bool(options,"associative",true);
                if(options["fitted"]!=null) definition.Fitted=X.Bool(options,"fitted",true);
            }
            if(suppression!=null) definition.set_SuppressedElements(ref suppression);
            ValidateBudget(document,definition.Geometries.Count,definition.Count,1);
            if(existing is SketchCircularPattern target) target.Definition=definition;else sketch.CircularPatterns.Add(definition);
        }
        else
        {
            SketchRectangularPatternDefinition definition;
            if(existing is SketchRectangularPattern pattern) definition=pattern.Definition.Copy();
            else
            {
                bool two=X.Bool(options,"two_directions",false);int axes=two ? 2 : 1;
                if(resolved.Count<=axes || resolved.Skip(resolved.Count-axes).Any(e=>e is not SketchLine)) throw new ArgumentException("Select source geometries followed by one or two whole direction lines.");
                foreach(var entity in resolved.Take(resolved.Count-axes)) geometries.Add(entity);
                definition=sketch.RectangularPatterns.CreateDefinition(geometries,resolved[resolved.Count-axes],ParameterInput("x_count",true),
                    X.Bool(options,"natural_x",true),X.Bool(options,"symmetric_x",false),ParameterInput("x_spacing_mm",false),
                    two ? resolved[resolved.Count-1] : Type.Missing,two ? ParameterInput("y_count",true) : Type.Missing,
                    X.Bool(options,"natural_y",true),X.Bool(options,"symmetric_y",false),two ? ParameterInput("y_spacing_mm",false) : Type.Missing,
                    X.Bool(options,"associative",true),X.Bool(options,"fitted",false));
                if(!two && (options["y_count"]!=null || options["y_spacing_mm"]!=null || options["natural_y"]!=null || options["symmetric_y"]!=null)) throw new ArgumentException("Y options require a second direction line.");
            }
            if(edit)
            {
                if(options["x_count"]!=null) definition.XCount=ParameterInput("x_count",true);
                if(options["x_spacing_mm"]!=null) definition.XSpacing=ParameterInput("x_spacing_mm",false);
                if(options["y_count"]!=null) definition.YCount=ParameterInput("y_count",true);
                if(options["y_spacing_mm"]!=null) definition.YSpacing=ParameterInput("y_spacing_mm",false);
                if(options["natural_x"]!=null) definition.NaturalXDirection=X.Bool(options,"natural_x",true);
                if(options["natural_y"]!=null) definition.NaturalYDirection=X.Bool(options,"natural_y",true);
                if(options["symmetric_x"]!=null) definition.XDirectionSymmetric=X.Bool(options,"symmetric_x",false);
                if(options["symmetric_y"]!=null) definition.YDirectionSymmetric=X.Bool(options,"symmetric_y",false);
                if(options["associative"]!=null) definition.Associative=X.Bool(options,"associative",true);
                if(options["fitted"]!=null) definition.Fitted=X.Bool(options,"fitted",false);
            }
            if(suppression!=null) definition.set_SuppressedElements(ref suppression);
            ValidateBudget(document,definition.Geometries.Count,definition.XCount,definition.YDirectionEntity==null ? 1 : definition.YCount);
            if(existing is SketchRectangularPattern target) target.Definition=definition;else sketch.RectangularPatterns.Add(definition);
        }
    }
    private static double Count(global::Inventor.Document document,object count)=>count is Parameter parameter ? Convert.ToDouble(parameter.Value)
        : count is string expression ? Convert.ToDouble(document.UnitsOfMeasure.GetValueFromExpression(expression,UnitsTypeEnum.kUnitlessUnits)) : Convert.ToDouble(count,System.Globalization.CultureInfo.InvariantCulture);
    private static void ValidateBudget(global::Inventor.Document document,int sources,object x,object y)
    {
        double nx=Count(document,x),ny=Count(document,y);
        if(double.IsNaN(nx) || double.IsInfinity(nx) || nx<1 || nx!=Math.Truncate(nx) || double.IsNaN(ny) || double.IsInfinity(ny)
            || ny<1 || ny!=Math.Truncate(ny) || nx*ny*sources>2000) throw new ArgumentException("Pattern exceeds the native object budget (2000); reduce counts or sources.");
    }
}
#endif
