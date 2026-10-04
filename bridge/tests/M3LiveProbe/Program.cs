using System;
using System.Runtime.InteropServices;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using InventorXrSo.Core.Backend;

internal static class Program
{
    [DllImport("oleaut32.dll",PreserveSig=false)]
    private static extern void GetActiveObject(ref Guid clsid,IntPtr reserved,[MarshalAs(UnmanagedType.IUnknown)] out object app);
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Contains("--http")) return HttpProbe.Run(args.Contains("--consolidated"),args.Contains("--face-selection"));
        PartDocument? part=null; global::Inventor.Document? original=null; IDisposable? tracker=null;
        try
        {
            var clsid=Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            GetActiveObject(ref clsid,IntPtr.Zero,out var running); var app=(global::Inventor.Application)running;
            original=app.ActiveDocument;
            var trackerType=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker",true)!;
            tracker=(IDisposable)Activator.CreateInstance(trackerType,app)!;
            var journal=(CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
            var commands=InventorCommandRegistry.Build(new PluginOptions(2027,false,false,0));
            var context=new InventorCommandContext { Application=app,Events=journal,Commands=commands,AllowExperimental=true,InventorYear=2027 };
            if(args.Contains("--face-feature")) return FaceFeatureProbe.Run(app,commands,context,journal);
            if(args.Contains("--face-selection"))
            {
                var current=app.ActiveDocument;
                string currentId="doc_"+current.InternalName;
                var faceRequest=new JObject { ["document_id"]=currentId,["expected_revision"]=journal.Revision(currentId) };
                var meshResponse=commands["get_display_mesh"].Execute(context,faceRequest);
                var designResponse=commands["get_design_context_xr"].Execute(context,faceRequest);
                if(!meshResponse.Ok || !designResponse.Ok) throw new InvalidOperationException("Face selection probe read failed.");
                var mesh=(JObject)meshResponse.Data!; var design=(JObject)designResponse.Data!;
                int matched=0,differentIds=0;
                foreach(var face in (JArray)design["faces"]!)
                {
                    var body=((JArray)mesh["bodies"]!).Single(b=>(int)b["index"]! == (int)face["body_index"]!);
                    var range=((JArray)body["faces"]!).Single(f=>(int)f["ordinal"]! == (int)face["face_ordinal"]!);
                    // Resolve both opaque references in Inventor; ordinal mapping must identify
                    // exactly the same native face, even when serialized contexts differ.
                    var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Core.EntityReferences",true)!;
                    var resolve=resolver.GetMethod("ResolvePartEntityFace")!;
                    var first=resolve.Invoke(null,new object[]{current,(string)range["face_id"]!})!;
                    var second=resolve.Invoke(null,new object[]{current,(string)face["id"]!})!;
                    var a=Marshal.GetIUnknownForObject(first); var b=Marshal.GetIUnknownForObject(second);
                    try { if(a!=b) throw new InvalidOperationException("Topology mapped to a different native face."); }
                    finally { Marshal.Release(a); Marshal.Release(b); }
                    matched++; if((string)range["face_id"]! != (string)face["id"]!) differentIds++;
                }
                if(matched==0) throw new InvalidOperationException("No planar faces tested.");
                if(journal.Revision(currentId)!=(string)faceRequest["expected_revision"]!) throw new InvalidOperationException("Read changed revision.");
                Console.WriteLine($"PASS read-only face selection: {matched} native identities matched, {differentIds} differing opaque references; revision unchanged.");
                return 0;
            }
            part=(PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject,app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject),true);
            string document="doc_"+((global::Inventor.Document)part).InternalName;
            JObject Call(string command,JObject args)
            {
                var response=commands[command].Execute(context,args);
                if(!response.Ok) throw new InvalidOperationException(command+": "+JsonConvert.SerializeObject(response.Error));
                return (JObject)response.Data!;
            }
            JObject StateArgs() => new() { ["document_id"]=document,["expected_revision"]=journal.Revision(document) };
            if(args.Contains("--constraints"))
            {
                var frame=new SketchFrame(default,new CadPoint(1,0,0),new CadPoint(0,1,0));
                var lineA=new SketchElement(SketchShape.Line,new CadPoint(0,0),new CadPoint(20,10));
                var lineB=new SketchElement(SketchShape.Line,new CadPoint(30,0),new CadPoint(45,8));
                var circleA=new SketchElement(SketchShape.Circle,new CadPoint(10,5),default,5);
                var circleB=new SketchElement(SketchShape.Circle,new CadPoint(30,12),default,3);
                var cases=new[] {
                    (name:"automatic_hv",kind:"",shapes:new[]{new SketchElement(SketchShape.Line,default,new CadPoint(20,0)),new SketchElement(SketchShape.Line,new CadPoint(20,0),new CadPoint(20,15))},native:"kHorizontalConstraintObject"),
                    (name:"equal_lines",kind:"equal",shapes:new[]{lineA,lineB},native:"kEqualLengthConstraintObject"),
                    (name:"rectangle_side",kind:"equal",shapes:new[]{new SketchElement(SketchShape.Rectangle,new CadPoint(10,20),new CadPoint(40,60)),lineB},native:"kEqualLengthConstraintObject"),
                    (name:"equal_circles",kind:"equal",shapes:new[]{circleA,circleB},native:"kEqualRadiusConstraintObject"),
                    (name:"tangent",kind:"tangent",shapes:new[]{new SketchElement(SketchShape.Line,default,new CadPoint(20,0)),circleA},native:"kTangentSketchConstraintObject"),
                    (name:"parallel",kind:"parallel",shapes:new[]{lineA,lineB},native:"kParallelConstraintObject"),
                    (name:"perpendicular",kind:"perpendicular",shapes:new[]{lineA,lineB},native:"kPerpendicularConstraintObject"),
                    (name:"concentric",kind:"concentric",shapes:new[]{circleA,circleB},native:"kConcentricConstraintObject"),
                    (name:"symmetric",kind:"symmetric",shapes:new[]{
                        new SketchElement(SketchShape.Line,new CadPoint(-20,0),new CadPoint(-10,10)),
                        new SketchElement(SketchShape.Line,new CadPoint(10,10),new CadPoint(20,0)),
                        new SketchElement(SketchShape.Line,new CadPoint(0,-10),new CadPoint(0,20))},native:"kSymmetryConstraintObject")
                };
                foreach(var test in cases)
                {
                    var constrained=new SketchDraft("3",frame,"XR_"+test.name);
                    foreach(var shape in test.shapes) constrained.Add(shape);
                    if(test.name=="rectangle_side") constrained.AddEntityConstraint("equal",1,4);
                    else if(test.kind.Length>0) constrained.AddConstraint(test.kind,0,1,test.kind=="symmetric" ? 2 : -1);
                    var batch=StateArgs(); string revision=journal.Revision(document);
                    int count=part.ComponentDefinition.Sketches.Count;
                    batch["operations"]=constrained.Operations(); batch["preview"]=true; batch["include_preview_mesh"]=true;
                    batch["validate"]=new JArray("rebuild","feature_health");
                    var ghost=Call("atomic_batch",batch);
                    Check(part.ComponentDefinition.Sketches.Count==count && journal.Revision(document)==revision,test.name+" clean rollback");
                    var nativeConstraints=(JArray)ghost["preview_mesh"]!["sketches"]!.Last!["geometric_constraints"]!;
                    Check(nativeConstraints.Any(c=>(string?)c["type"]==test.native),test.name+" native preview constraint");
                    batch["preview"]=false; batch["include_preview_mesh"]=false;
                    Call("atomic_batch",batch);
                    Check(part.ComponentDefinition.Sketches.Count==count+1,test.name+" committed sketch");
                    bool persisted=false;
                    foreach(GeometricConstraint constraint in part.ComponentDefinition.Sketches[count+1].GeometricConstraints)
                        persisted |= constraint.Type.ToString()==test.native;
                    Check(persisted,test.name+" persistent native relation");
                    if(test.name=="automatic_hv")
                    {
                        var sketch=part.ComponentDefinition.Sketches[count+1];
                        bool vertical=false;
                        foreach(GeometricConstraint relation in sketch.GeometricConstraints)
                            vertical |= relation.Type.ToString()=="kVerticalConstraintObject";
                        Check(vertical,"Automatic vertical relation persists");
                        var first=Marshal.GetIUnknownForObject(sketch.SketchLines[1].EndSketchPoint);
                        var second=Marshal.GetIUnknownForObject(sketch.SketchLines[2].StartSketchPoint);
                        try { Check(first==second,"Connected endpoints share native sketch point identity"); }
                        finally { Marshal.Release(first); Marshal.Release(second); }
                    }
                    if(test.name=="rectangle_side")
                    {
                        var lines=part.ComponentDefinition.Sketches[count+1].SketchLines;
                        double Length(int n)=>lines[n].StartSketchPoint.Geometry.DistanceTo(lines[n].EndSketchPoint.Geometry);
                        Check(Math.Abs(Length(2)-Length(5))<1e-6,"Selected rectangle side has equal length to standalone line");
                    }
                    Console.WriteLine("Native constraint preview / rollback / commit: "+test.name+" PASS");
                }
                return 0;
            }
            var draft=new SketchDraft("3",new SketchFrame(default,new CadPoint(1,0,0),new CadPoint(0,1,0)),"XR_LiveProbe");
            draft.Add(new SketchElement(SketchShape.Rectangle,default,new CadPoint(20,30),dimensioned:true));
            var operations=draft.Operations(DesignOperations.Extrude(draft.Name,10,"new_body","positive"));
            string before=journal.Revision(document);
            var request=StateArgs(); request["operations"]=operations; request["preview"]=true;
            request["include_preview_mesh"]=true; request["validate"]=new JArray("rebuild","feature_health");
            var preview=Call("atomic_batch",request);
            Check((string?)preview["status"]=="preview_rolled_back","Preview rollback status");
            Check(part.ComponentDefinition.SurfaceBodies.Count==0 && part.ComponentDefinition.Sketches.Count==0,"No residual preview geometry");
            Check(journal.Revision(document)==before,"Preview revision restored");
            Check((preview["preview_mesh"]?["bodies"] as JArray)?.Count>0,"Tentative mesh captured");
            Console.WriteLine(new JObject{["stage"]="preview",["bodies"]=(preview["preview_mesh"]?["bodies"] as JArray)?.Count,
                ["sketches"]=(preview["preview_mesh"]?["sketches"] as JArray)?.Count,["rollback_clean"]=true}.ToString(Formatting.None));
            request["preview"]=false; request["include_preview_mesh"]=false; request["history_owner"]="m3-live-probe";
            var committed=Call("atomic_batch",request);
            Check(part.ComponentDefinition.SurfaceBodies.Count==1,"Committed solid");
            Check(part.ComponentDefinition.Sketches[1].DimensionConstraints.Count==2,"Persistent rectangle dimensions");
            Check((string?)committed["history_ticket"]!=null,"History receipt");
            var authoring=Call("get_design_context_xr",StateArgs());
            Check((authoring["edges"] as JArray)?.Count>0,"Native edge context");
            Check((authoring["sketch_snapshots"] as JArray)?.Count==1,"Current sketch snapshot available before any further preview");
            Check((authoring["sketch_snapshots"]![0]!["dimensions"] as JArray)?.Count==2,"Context snapshot preserves dimension attribution");
            JObject History(string action,string? ticket=null)
            { var args=StateArgs(); args["action"]=action;args["owner"]="m3-live-probe";args["ticket"]=ticket; return Call("history_xr",args); }
            var history=History("status"); Check((bool?)history["can_undo"]==true,"Undo offered after commit");
            history=History("undo",(string?)history["ticket"]);
            Check(part.ComponentDefinition.SurfaceBodies.Count==0,"Undo removed solid");
            Check((bool?)history["can_redo"]==true,"Redo offered after undo");
            history=History("redo",(string?)history["ticket"]);
            Check(part.ComponentDefinition.SurfaceBodies.Count==1,"Redo restored solid");
            Console.WriteLine(new JObject{["stage"]="commit_undo_redo",["passed"]=true,["edge_count"]=(authoring["edges"] as JArray)?.Count,
                ["dimensions"]=part.ComponentDefinition.Sketches[1].DimensionConstraints.Count}.ToString(Formatting.None));
            void FeatureCheck(JObject operation)
            {
                string command=(string)operation["command"]!;
                double volume=part.ComponentDefinition.MassProperties.Volume;
                var feature=StateArgs(); feature["operations"]=new JArray(operation);
                feature["preview"]=true; feature["include_preview_mesh"]=true;
                feature["validate"]=new JArray("rebuild","feature_health");
                var ghost=Call("atomic_batch",feature);
                Check((ghost["preview_mesh"]?["bodies"] as JArray)?.Count>0,command+" preview mesh");
                Check(Math.Abs(part.ComponentDefinition.MassProperties.Volume-volume)<1e-8,command+" preview preserved volume");
                feature["preview"]=false; feature["include_preview_mesh"]=false; feature["history_owner"]="m3-live-probe";
                Call("atomic_batch",feature);
                Check(Math.Abs(part.ComponentDefinition.MassProperties.Volume-volume)>1e-6,command+" changed volume");
                var h=History("status"); History("undo",(string?)h["ticket"]);
                Check(Math.Abs(part.ComponentDefinition.MassProperties.Volume-volume)<1e-8,command+" undo restored volume");
                Console.WriteLine(new JObject{["stage"]=command,["preview_commit_undo"]=true}.ToString(Formatting.None));
            }
            authoring=Call("get_design_context_xr",StateArgs());
            var top=((JArray)authoring["faces"]!).First(f=>(double)f["normal"]![2]!>0.9 && Math.Abs((double)f["point_mm"]![2]!-10)<0.001);
            FeatureCheck(DesignOperations.Hole((string)top["id"]!,10,15,10,4,null));
            authoring=Call("get_design_context_xr",StateArgs());
            FeatureCheck(DesignOperations.Fillet(new[]{(string)authoring["edges"]![0]!["id"]!},1));
            authoring=Call("get_design_context_xr",StateArgs());
            var blockEdges=((JArray)authoring["edges"]!).Where(e=>(string?)e!["kind"]=="kLineSegmentCurve")
                .Select(e=>(string)e!["id"]!).ToArray();
            Check(blockEdges.Length>=8,"Straight block edges for the oversized-fillet regression");
            var limitRequest=StateArgs();
            limitRequest["preview"]=true; limitRequest["include_preview_mesh"]=true;
            limitRequest["validate"]=new JArray("rebuild","feature_health");
            var limitRevision=journal.Revision(document);
            var limitVolume=part.ComponentDefinition.MassProperties.Volume;
            var limitFeatureCount=part.ComponentDefinition.Features.FilletFeatures.Count;
            limitRequest["operations"]=new JArray(DesignOperations.Fillet(blockEdges,1));
            Check(commands["atomic_batch"].Execute(context,limitRequest).Ok,"1 mm block-edge fillet previews");
            limitRequest["operations"]=new JArray(DesignOperations.Fillet(blockEdges,100));
            var rejected=commands["atomic_batch"].Execute(context,limitRequest);
            Check(!rejected.Ok && rejected.Error?.Code==InventorErrorCodes.ROLLED_BACK,"100 mm block-edge fillet is rejected and rolled back");
            Check(journal.Revision(document)==limitRevision && Math.Abs(part.ComponentDefinition.MassProperties.Volume-limitVolume)<1e-8
                && part.ComponentDefinition.Features.FilletFeatures.Count==limitFeatureCount,"Oversized fillet leaves no CAD changes");
            Console.WriteLine("Fillet oversized-radius regression: valid 1 mm, rejected 100 mm, preview rollback preserved.");
            FeatureCheck(DesignOperations.Chamfer(new[]{(string)authoring["edges"]![0]!["id"]!},1));
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            try { if(part!=null) ((global::Inventor.Document)part).Close(true); }
            finally { try { original?.Activate(); } finally { tracker?.Dispose(); } }
        }
    }
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException("Assertion failed: "+message); }
}
