using System.Reflection;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static JArray ProbePatterns(global::Inventor.Application app,PartDocument fixture,
        IReadOnlyDictionary<string,IInventorCommand> commands,InventorCommandContext context,CadEventJournal journal,JArray rows)
    {
        var g=app.TransientGeometry;string documentId="doc_"+fixture.InternalName;
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Message);return (JObject)result.Data!;}
        var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
        object Resolve(string id)=>resolver.GetMethod("Resolve",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new object[] {fixture,id,"sketch_entity"})!;
        bool expressions=System.Environment.GetEnvironmentVariable("S1_PROBE_PATTERN_EXPRESSIONS")=="1";
        bool independentOptions=System.Environment.GetEnvironmentVariable("S1_PROBE_PATTERN_INDEPENDENT_OPTIONS")=="1";
        bool multipleSuppression=System.Environment.GetEnvironmentVariable("S1_PROBE_PATTERN_MULTIPLE_SUPPRESSION")=="1";
        string? kindFilter=System.Environment.GetEnvironmentVariable("S1_PROBE_PATTERN_KIND");
        if(kindFilter!=null && !new[] {"rectangular","rectangular_two","circular"}.Contains(kindFilter,StringComparer.Ordinal)) throw new ArgumentException("Unknown pattern fixture filter.");
        IEnumerable<(bool associative,bool fitted,bool natural,bool symmetric,bool naturalY,bool symmetricY)> Configurations(string kind)
        {
            if(independentOptions)
            {
                for(int mask=0;mask<(kind=="rectangular_two" ? 16 : 4);mask++)
                    yield return (true,kind=="circular",(mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0);
                yield break;
            }
            foreach(bool associative in new[] {false,true}) foreach(bool fitted in new[] {false,true}) foreach(bool symmetric in new[] {false,true})
                yield return (associative,fitted,!symmetric,symmetric,!symmetric,symmetric);
        }
        foreach(string kind in new[] {"rectangular","rectangular_two","circular"}.Where(k=>kindFilter==null || k==kindFilter))
        foreach(var configuration in Configurations(kind))
        {
            bool circular=kind=="circular",two=kind=="rectangular_two",natural=configuration.natural,symmetric=configuration.symmetric;
            bool associative=configuration.associative,fitted=configuration.fitted,naturalY=configuration.naturalY,symmetricY=configuration.symmetricY;
            var row=new JObject {["kind"]=kind,["associative"]=associative,["fitted"]=fitted,["symmetric"]=symmetric,["natural_direction"]=natural,
                ["natural_y"]=two ? naturalY : true,["symmetric_y"]=two && symmetricY,["independent_options"]=independentOptions,["outcome"]="INCOMPLETE",["stage"]="fixture"};rows.Add(row);
            var seed=app.TransactionManager.StartTransaction((_Document)fixture,"S1 dedicated native sketch pattern");
            PlanarSketch sketch;SketchCircle source;Parameter sourceRadius;object x,y;var originals=new List<object>();
            UserParameter? countSource=null,spacingSource=null,angleSource=null;
            try
            {
                sketch=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Pattern_"+kind+"_"+rows.Count;
                if(expressions)
                {
                    var parameters=fixture.ComponentDefinition.Parameters.UserParameters;string prefix="S1Pattern"+rows.Count+"_";
                    countSource=parameters.AddByValue(prefix+"Count",3,UnitsTypeEnum.kUnitlessUnits);
                    spacingSource=parameters.AddByValue(prefix+"Spacing",1d,UnitsTypeEnum.kMillimeterLengthUnits);
                    angleSource=parameters.AddByValue(prefix+"Angle",Math.PI*2/3,UnitsTypeEnum.kDegreeAngleUnits);
                }
                source=sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(2,0),.2);originals.Add(source);
                sourceRadius=sketch.DimensionConstraints.AddRadius((SketchEntity)source,g.CreatePoint2d(2.5,.5),false).Parameter;
                if(circular) {x=sketch.SketchPoints.Add(g.CreatePoint2d(0,0),false);y=x;}
                else {x=sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(-1,-1),g.CreatePoint2d(1,-1));y=two ? sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(-1,-1),g.CreatePoint2d(-1,1)) : x;}
                originals.Add(x);if(two) originals.Add(y);seed.End();
            }
            catch {seed.Abort();throw;}
            var sketches=(JArray)Call("get_design_context_xr",State())["sketches"]!;
            string sketchId=(string)sketches.Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            JObject Snapshot(){var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();string revision=journal.Revision(documentId);
            if((bool)before["partial"]!) throw new InvalidOperationException("Pattern fixture is partial.");
            var selected=originals.Select(o=>((JArray)before["entities"]!).OfType<JObject>().Single(e=>SameComObject(Resolve((string)e["id"]!),o))).ToArray();
            var refs=new JArray(selected.Select(e=>new JObject {["document_id"]=documentId,["revision"]=revision,["sketch_id"]=sketchId,["entity_id"]=e["id"],["type"]=e["type"],["subpart"]="Entity"}));
            int[] suppression=multipleSuppression && two ? new[] {2,4} : new[] {2};row["suppressed_elements"]=new JArray(suppression);
            var options=new JObject {["references"]=refs,["associative"]=associative,["fitted"]=fitted,["suppressed_elements"]=new JArray(suppression)};
            if(circular) {options["count"]=3;options["angle_deg"]=120;options["natural_axis"]=natural;options["symmetric"]=symmetric;}
            else
            {
                options["two_directions"]=two;options["x_count"]=3;options["x_spacing_mm"]=10;options["natural_x"]=natural;options["symmetric_x"]=symmetric;
                if(two) {options["y_count"]=2;options["y_spacing_mm"]=10;options["natural_y"]=naturalY;options["symmetric_y"]=symmetricY;}
            }
            if(expressions)
            {
                options[circular ? "count" : "x_count"]=countSource!.Name;
                options[circular ? "angle_deg" : "x_spacing_mm"]=circular ? angleSource!.Name : spacingSource!.Name;
                if(two) {options["y_count"]="1 + 1";options["y_spacing_mm"]=spacingSource!.Name+" / 2";}
            }
            JObject Operation(string variant,JObject data)=>new() {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]=variant,["options"]=data}};
            var op=Operation(circular ? "pattern_circular" : "pattern_rectangular",options);
            JObject Batch(JObject operation,bool preview=false)
            {var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            void Unchanged(JObject expected,string expectedRevision)
            {if(journal.Revision(documentId)!=expectedRevision || !JToken.DeepEquals(expected,Snapshot())) throw new InvalidOperationException("Pattern preview/refusal changed the native snapshot or revision.");}
            void Refuse(JObject invalid)
            {if(commands["atomic_batch"].Execute(context,Batch(invalid)).Ok) throw new InvalidOperationException("Invalid pattern accepted.");Unchanged(before,revision);}
            foreach(var bad in new[] {("revision","old"),("document_id","other"),("type","text"),("subpart","Center")})
            {var invalid=(JObject)op.DeepClone();invalid["arguments"]!["options"]!["references"]![0]![bad.Item1]=bad.Item2;Refuse(invalid);}
            foreach(double count in new[] {0d,2.5,2001})
            {var invalid=(JObject)op.DeepClone();invalid["arguments"]!["options"]![circular ? "count" : "x_count"]=count;Refuse(invalid);}
            if(expressions) foreach(string expression in new[] {"2.5","-1","S1MissingParameter","3 mm",new string('a',2049)})
            {var invalid=(JObject)op.DeepClone();invalid["arguments"]!["options"]![circular ? "count" : "x_count"]=expression;Refuse(invalid);}
            var invalidFlag=(JObject)op.DeepClone();invalidFlag["arguments"]!["options"]!["associative"]="yes";Refuse(invalidFlag);
            var extra=(JObject)op.DeepClone();extra["arguments"]!["options"]!["feature_count"]=2;Refuse(extra);
            var oracle=new List<JObject>();var suppressionOracle=new List<JObject>();JObject? deleted=null;
            int[][] suppressionChanges=multipleSuppression && associative ? new[] {two ? new[] {3,5} : new[] {3},Array.Empty<int>(),suppression} : Array.Empty<int[]>();
            row["stage"]="direct_api_creation";
            var direct=app.TransactionManager.StartTransaction((_Document)fixture,"S1 direct sketch pattern API oracle, aborted");
            try
            {
                var geometry=app.TransientObjects.CreateObjectCollection();geometry.Add(source);int[] suppressed=(int[])suppression.Clone();object? pattern;
                if(circular)
                {
                    var d=sketch.CircularPatterns.CreateDefinition(geometry,x,natural,expressions ? countSource!.Name : 3,expressions ? angleSource!.Name : Math.PI*2/3,symmetric,associative,fitted);d.set_SuppressedElements(ref suppressed);
                    pattern=sketch.CircularPatterns.Add(d);
                }
                else
                {
                    var d=sketch.RectangularPatterns.CreateDefinition(geometry,x,expressions ? countSource!.Name : 3,natural,symmetric,expressions ? spacingSource!.Name : 1d,two ? y : Type.Missing,two ? expressions ? "1 + 1" : 2 : Type.Missing,
                        two ? naturalY : true,two && symmetricY,two ? expressions ? spacingSource!.Name+" / 2" : 1d : Type.Missing,associative,fitted);d.set_SuppressedElements(ref suppressed);
                    pattern=sketch.RectangularPatterns.Add(d);
                }
                if((pattern!=null)!=associative) throw new InvalidOperationException("Native associative/static pattern lifecycle differs from its API contract.");
                sketch.Solve();fixture.Update2(true);oracle.Add(OffsetSemanticSnapshot(Snapshot()));
                foreach(var wanted in suppressionChanges)
                {
                    var elements=pattern is SketchCircularPattern circularPattern ? circularPattern.Elements : ((SketchRectangularPattern)pattern!).Elements;
                    sketch.Solve();
                    foreach(SketchPatternElement element in elements)
                        if(element.Suppressed!=wanted.Contains(element.Index)) element.Suppressed=wanted.Contains(element.Index);
                    sketch.Solve();fixture.Update2(true);suppressionOracle.Add(OffsetSemanticSnapshot(Snapshot()));
                }
                for(int i=0;i<2;i++)
                {
                    row["stage"]="direct_api_update_"+i;
                    sourceRadius.Value=.3+i*.1;
                    if(expressions) {countSource!.Value=4+i;spacingSource!.Value=1.2+i*.2;angleSource!.Value=Math.PI/2+i*.1;}
                    // Match the independent source update/solve followed by the
                    // command's parameter edit/solve. The intermediate solve can
                    // normalize native dimension placement at floating precision.
                    sketch.Solve();fixture.Update2(true);
                    if(pattern is SketchCircularPattern cp){if(expressions) ((Parameter)cp.Definition.Count).Expression=countSource!.Name+" + 1";else ((Parameter)cp.Definition.Count).Value=4+i;}
                    if(pattern is SketchRectangularPattern rp){if(expressions) ((Parameter)rp.Definition.XCount).Expression=countSource!.Name+" + 1";else ((Parameter)rp.Definition.XCount).Value=4+i;}
                    sketch.Solve();fixture.Update2(true);oracle.Add(OffsetSemanticSnapshot(Snapshot()));
                }
                if(pattern is SketchCircularPattern cd) cd.Delete();if(pattern is SketchRectangularPattern rd) rd.Delete();sketch.Solve();deleted=OffsetSemanticSnapshot(Snapshot());
            }
            finally {direct.Abort();}
            var restored=Snapshot();var baseline=(JObject)before.DeepClone();restored.Remove("revision");baseline.Remove("revision");
            if(!JToken.DeepEquals(baseline,restored) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Direct pattern API oracle did not roll back exactly.");Unchanged(before,revision);
            var preview=Call("atomic_batch",Batch(op,true));Unchanged(before,revision);
            row["stage"]="handler_creation";
            var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
            if((bool)ghost["partial"]! || ((JArray)ghost["native_entities"]!).Any(e=>e["id"]!=null) || !JToken.DeepEquals(oracle[0]["entities"],ghost["native_entities"])) throw new InvalidOperationException("Pattern preview does not match native API geometry.");
            Call("atomic_batch",Batch(op));var after=Snapshot();
            if((bool)after["partial"]! || journal.Revision(documentId)==revision || !JToken.DeepEquals(oracle[0],OffsetSemanticSnapshot(after)))
            {row["expected"]=oracle[0];row["actual"]=OffsetSemanticSnapshot(after);throw new InvalidOperationException("Pattern creation differs from native API.");}
            string sourceId=(string)selected[0]["id"]!;
            foreach(var original in selected) if(!((JArray)after["entities"]!).Any(e=>JToken.DeepEquals(original,e))) throw new InvalidOperationException("Pattern changed its original selection.");
            JObject PatternReference(JObject snapshot)
            {var constraint=((JArray)snapshot["constraints"]!).First(c=>c["pattern_definition"]!=null);return new JObject {["document_id"]=documentId,["revision"]=journal.Revision(documentId),["sketch_id"]=sketchId,["entity_id"]=constraint["id"],["type"]="constraint",["subpart"]="Entity"};}
            if(multipleSuppression && associative && (!independentOptions || !natural && !symmetric && (!two || !naturalY && !symmetricY)))
            {
                var prior=Snapshot();string rev=journal.Revision(documentId);
                int count=(int)((JArray)prior["constraints"]!).First(c=>c["pattern_definition"]!=null)["pattern_definition"]!["element_count"]!;
                foreach(JToken invalid in new JToken[] {new JArray(2,2),new JArray(count+1),new JValue("2")})
                foreach(bool previewFlag in new[] {false,true})
                {
                    var change=Operation("pattern_update",new JObject {["references"]=new JArray(PatternReference(prior)),["suppressed_elements"]=invalid.DeepClone()});
                    if(commands["atomic_batch"].Execute(context,Batch(change,previewFlag)).Ok) throw new InvalidOperationException("Invalid native suppression edit accepted.");Unchanged(prior,rev);
                }
                foreach(bool previewFlag in new[] {false,true})
                {
                    var change=Operation("pattern_update",new JObject {["references"]=new JArray(PatternReference(prior)),
                        [circular ? "count" : "x_count"]=4,["suppressed_elements"]=new JArray(2000)});
                    if(commands["atomic_batch"].Execute(context,Batch(change,previewFlag)).Ok) throw new InvalidOperationException("Invalid suppression after parameter mutation accepted.");Unchanged(prior,rev);
                }
                row["suppression_refusal_checks"]=8;
            }
            for(int changeIndex=0;changeIndex<suppressionChanges.Length;changeIndex++)
            {
                row["stage"]="handler_suppression_"+changeIndex;var prior=Snapshot();string rev=journal.Revision(documentId);
                var change=Operation("pattern_update",new JObject {["references"]=new JArray(PatternReference(prior)),["suppressed_elements"]=new JArray(suppressionChanges[changeIndex])});
                var ghostPreview=Call("atomic_batch",Batch(change,true));Unchanged(prior,rev);
                var suppressionGhost=((JArray)ghostPreview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
                if(!JToken.DeepEquals(suppressionOracle[changeIndex]["entities"],suppressionGhost["native_entities"])) throw new InvalidOperationException("Suppression preview differs from native element API.");
                Call("atomic_batch",Batch(change));var current=Snapshot();
                if(!JToken.DeepEquals(suppressionOracle[changeIndex],OffsetSemanticSnapshot(current))) throw new InvalidOperationException("Suppression update differs from native element API.");
                foreach(var original in selected) if(!((JArray)current["entities"]!).Any(e=>JToken.DeepEquals(original,e))) throw new InvalidOperationException("Suppression changed a source object.");
            }
            row["suppression_mutations"]=suppressionChanges.Length;
            for(int i=0;i<2;i++)
            {
                row["stage"]="handler_update_"+i;
                var update=app.TransactionManager.StartTransaction((_Document)fixture,"S1 pattern source radius update "+i);
                try {sourceRadius.Value=.3+i*.1;if(expressions) {countSource!.Value=4+i;spacingSource!.Value=1.2+i*.2;angleSource!.Value=Math.PI/2+i*.1;}sketch.Solve();fixture.Update2(true);update.End();}catch {update.Abort();throw;}
                if(associative)
                {
                    var prior=Snapshot();string rev=journal.Revision(documentId);
                    var change=Operation("pattern_update",new JObject {["references"]=new JArray(PatternReference(prior)),[circular ? "count" : "x_count"]=expressions ? countSource!.Name+" + 1" : 4+i});
                    Call("atomic_batch",Batch(change,true));Unchanged(prior,rev);Call("atomic_batch",Batch(change));
                }
                after=Snapshot();if((bool)after["partial"]! || !JToken.DeepEquals(oracle[i+1],OffsetSemanticSnapshot(after))
                    || !((JArray)after["entities"]!).Any(e=>(string?)e["id"]==sourceId && Math.Abs((double)e["radius_mm"]!-(3+i))<1e-6))
                {row["expected"]=oracle[i+1];row["actual"]=OffsetSemanticSnapshot(after);throw new InvalidOperationException("Pattern dependency/definition update differs after source edit "+i);}
            }
            if(associative)
            {
                var prior=Snapshot();string rev=journal.Revision(documentId);var remove=Operation("pattern_delete",new JObject {["references"]=new JArray(PatternReference(prior))});
                Call("atomic_batch",Batch(remove,true));Unchanged(prior,rev);Call("atomic_batch",Batch(remove));
                if(!JToken.DeepEquals(deleted,OffsetSemanticSnapshot(Snapshot()))) throw new InvalidOperationException("Pattern deletion differs from native API.");
            }
            row["natural_direction"]=natural;row["dependency_updates"]=2;row["expression_parameters"]=expressions;
            if(expressions) row["source_parameter_cleanup"]="owned fixture closure without saving";
            row["stage"]="complete";
            row["outcome"]="PASS native API geometry/parameter/suppression oracle, original identity, guarded references/options, exact preview rollback and two source/definition updates; native associative pattern deletion";
            Console.WriteLine("PASS pattern fixture="+fixture.DisplayName+", kind="+kind+", associative="+associative+", fitted="+fitted+", symmetric="+symmetric);
            var cleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 completed pattern cleanup");try {sketch.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
        return rows;
    }
}
