using System.Runtime.InteropServices;
using Path = System.IO.Path;
using File = System.IO.File;
using System.Security.Cryptography.X509Certificates;
using Inventor;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;

internal static class HttpProbe
{
    [DllImport("oleaut32.dll", PreserveSig=false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object app);

    internal static int Run(bool consolidated=false, bool faceSelection=false)
    {
        PartDocument? part = null;
        global::Inventor.Document? original = null;
        try
        {
            var clsid = Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            GetActiveObject(ref clsid, IntPtr.Zero, out var running);
            var app = (global::Inventor.Application)running;
            original = app.ActiveDocument;
            var directory = Path.Combine(Path.GetTempPath(), "xrso-quest-live");
            var entry = File.ReadLines(Path.Combine(directory,"tokens-revocation.txt"))
                .Select(l=>l.Trim()).First(l=>l.Length>0 && !l.StartsWith('#')).Split(':',2);
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(directory,"server.pfx"),null);
            string pin = CertificatePin.Sha256Hex(cert.RawData);
            using var transport = new SystemHttpTransport(ServerTrust.Pinned(pin));
            var backend = new InventorBackend(transport,
                new PairedServer("localhost",8443,pin,entry[0],entry[1].Trim()),new MemoryAssetCache());
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var ct = timeout.Token;
            backend.ConnectAsync(ct).GetAwaiter().GetResult();
            if(faceSelection)
            {
                var faceState=backend.GetDocumentStateAsync(ct).GetAwaiter().GetResult();
                var faceContext=backend.GetDesignContextAsync(faceState,ct).GetAwaiter().GetResult();
                var mesh=backend.GetDefinitionMeshAsync(faceState.DocumentId,ct).GetAwaiter().GetResult();
                var model=InventorXrSo.Core.Glb.GlbModel.Parse(backend.GetAssetAsync(mesh,ct).GetAwaiter().GetResult());
                Check(faceContext.Faces.Count>0,"Planar faces available");
                foreach(var face in faceContext.Faces)
                {
                    Check(face.BodyIndex>0 && face.FaceOrdinal>0,"Deployed topology fields present");
                    var primitive=model.Primitives.Single(p=>p.BodyIndex==face.BodyIndex);
                    Check(primitive.Faces.Any(f=>f.Ordinal==face.FaceOrdinal),"Mesh face mapped by native body/face ordinal");
                }
                Check(backend.GetDocumentStateAsync(ct).GetAwaiter().GetResult().Revision==faceState.Revision,"Read preserves revision");
                Console.WriteLine($"HTTP read-only face mapping: PASS ({faceContext.Faces.Count} planar faces)");
                return 0;
            }
            part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject,
                app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject),true);
            var state = backend.GetDocumentStateAsync(ct).GetAwaiter().GetResult();
            Check(state.DocumentId=="doc_"+((global::Inventor.Document)part).InternalName,"Temporary document selected");
            backend.GetDesignContextAsync(state,ct).GetAwaiter().GetResult();
            var draft = new SketchDraft("3",new SketchFrame(default,new CadPoint(1,0,0),new CadPoint(0,1,0)),"XR_HttpProbe");
            draft.Add(new SketchElement(SketchShape.Rectangle,default,new CadPoint(20,30),dimensioned:true));
            var ops = draft.Operations(DesignOperations.Extrude(draft.Name,10,"new_body","positive"));
            var preview = backend.PreviewDesignAsync(state,ops,ct).GetAwaiter().GetResult();
            Check(preview.Model!=null && preview.Sketches.Count>0,"Downloaded and parsed owned GLB and sketches");
            Check(part.ComponentDefinition.SurfaceBodies.Count==0 && part.ComponentDefinition.Sketches.Count==0,"Preview rollback clean");
            Check(backend.GetDocumentStateAsync(ct).GetAwaiter().GetResult().Revision==state.Revision,"Preview preserves revision");
            Console.WriteLine("HTTP preview / GLB download / rollback: PASS");
            state = backend.CommitDesignAsync(preview,ct).GetAwaiter().GetResult();
            Check(part.ComponentDefinition.SurfaceBodies.Count==1,"Committed one solid");
            Check(part.ComponentDefinition.Sketches[1].DimensionConstraints.Count==2,"Persistent dimensions");
            var context = backend.GetDesignContextAsync(state,ct).GetAwaiter().GetResult();
            Check(context.Edges.Count==12,"Context matches committed solid");
            if(consolidated) Check(context.SketchSnapshots.Count==1 && context.SketchSnapshots[0].Dimensions.Count==2,"Deployed current sketch/dimension context");
            var history = backend.GetHistoryAsync(state,ct).GetAwaiter().GetResult();
            Check(history.CanUndo,"Undo offered");
            state = backend.ApplyHistoryAsync(history,false,ct).GetAwaiter().GetResult();
            Check(part.ComponentDefinition.SurfaceBodies.Count==0,"Undo removes solid");
            history = backend.GetHistoryAsync(state,ct).GetAwaiter().GetResult();
            Check(history.CanRedo,"Redo offered");
            state = backend.ApplyHistoryAsync(history,true,ct).GetAwaiter().GetResult();
            Check(part.ComponentDefinition.SurfaceBodies.Count==1,"Redo restores solid");
            Console.WriteLine("HTTP commit / persistent dimensions / context / undo / redo: PASS");
            void Feature(JObject operation) => FeatureBatch((string)operation["command"]!,new JArray(operation));
            void FeatureBatch(string label,JArray operations,Action? verify=null)
            {
                double volume = part.ComponentDefinition.MassProperties.Volume;
                var ghost = backend.PreviewDesignAsync(state,operations,ct).GetAwaiter().GetResult();
                Check(Math.Abs(part.ComponentDefinition.MassProperties.Volume-volume)<1e-8,"Feature preview preserves volume");
                state = backend.CommitDesignAsync(ghost,ct).GetAwaiter().GetResult();
                Check(Math.Abs(part.ComponentDefinition.MassProperties.Volume-volume)>1e-6,"Feature changes volume");
                verify?.Invoke();
                var receipt = backend.GetHistoryAsync(state,ct).GetAwaiter().GetResult();
                state = backend.ApplyHistoryAsync(receipt,false,ct).GetAwaiter().GetResult();
                Check(Math.Abs(part.ComponentDefinition.MassProperties.Volume-volume)<1e-8,"Feature undo restores volume");
                Console.WriteLine("HTTP "+label+" preview / commit / undo: PASS");
            }
            foreach(var operation in new[]{"join","cut","intersect"})
            {
                var extrusion=new SketchDraft("3",new SketchFrame(default,new CadPoint(1,0,0),new CadPoint(0,1,0)),"XR_"+operation);
                extrusion.Add(operation=="join"
                    ? new SketchElement(SketchShape.Rectangle,new CadPoint(15,10),new CadPoint(25,20))
                    : new SketchElement(SketchShape.Rectangle,new CadPoint(5,5),new CadPoint(15,15)));
                double distance=operation=="intersect" ? 20 : 5;
                double expectedVolume=operation=="join" ? 6.25 : operation=="cut" ? 5.5 : 1;
                FeatureBatch("extrude "+operation,extrusion.Operations(DesignOperations.Extrude(extrusion.Name,distance,operation,"positive")),()=>{
                    Check(Math.Abs(part.ComponentDefinition.MassProperties.Volume-expectedVolume)<1e-6,"Exact "+operation+" volume");
                    Check(part.ComponentDefinition.SurfaceBodies.Count==1,"One body after "+operation);
                });
            }
            foreach(var direction in new[]{"negative","symmetric"})
            {
                var extrusion=new SketchDraft("3",new SketchFrame(default,new CadPoint(1,0,0),new CadPoint(0,1,0)),"XR_"+direction);
                extrusion.Add(new SketchElement(SketchShape.Rectangle,new CadPoint(40,0),new CadPoint(45,5)));
                FeatureBatch("extrude "+direction,extrusion.Operations(DesignOperations.Extrude(extrusion.Name,10,"new_body",direction)),()=>{
                    Check(part.ComponentDefinition.SurfaceBodies.Count==2,"New separate body");
                    var box=part.ComponentDefinition.SurfaceBodies[2].RangeBox;
                    Console.WriteLine("Native "+direction+" Z extent mm: "+(box.MinPoint.Z*10)+" .. "+(box.MaxPoint.Z*10));
                    if(direction=="negative") Check(Math.Abs(box.MinPoint.Z+1)<1e-6 && Math.Abs(box.MaxPoint.Z)<1e-6,"Negative extent");
                    else Check(box.MinPoint.Z<0 && Math.Abs(box.MinPoint.Z+box.MaxPoint.Z)<1e-6,"Symmetric extent");
                });
            }
            context = backend.GetDesignContextAsync(state,ct).GetAwaiter().GetResult();
            var top = context.Faces.First(f=>f.Normal.Z>0.9);
            Feature(DesignOperations.Hole(top.Id,10,15,10,4,null));
            context = backend.GetDesignContextAsync(state,ct).GetAwaiter().GetResult();
            Feature(DesignOperations.Hole(context.Faces.First(f=>f.Normal.Z>0.9).Id,10,15,10,4,5));
            context = backend.GetDesignContextAsync(state,ct).GetAwaiter().GetResult();
            Feature(DesignOperations.Fillet(new[]{context.Edges[0].Id},1));
            context = backend.GetDesignContextAsync(state,ct).GetAwaiter().GetResult();
            Feature(DesignOperations.Chamfer(new[]{context.Edges[0].Id},1));
            var parameter = part.ComponentDefinition.Sketches[1].DimensionConstraints[1].Parameter;
            Feature(DesignOperations.Parameter(parameter.Name,25,"mm"));
            if(consolidated)
            {
                foreach(bool circles in new[]{false,true})
                {
                    var relation=new SketchDraft("3",new SketchFrame(default,new CadPoint(1,0,0),new CadPoint(0,1,0)),circles ? "XR_EqualCircles" : "XR_RectangleSide");
                    if(circles)
                    {
                        relation.Add(new SketchElement(SketchShape.Circle,new CadPoint(50,50),default,5));
                        relation.Add(new SketchElement(SketchShape.Circle,new CadPoint(70,50),default,3));
                        relation.AddConstraint("equal",0,1);
                    }
                    else
                    {
                        relation.Add(new SketchElement(SketchShape.Rectangle,new CadPoint(40,40),new CadPoint(60,70)));
                        relation.Add(new SketchElement(SketchShape.Line,new CadPoint(80,40),new CadPoint(90,60)));
                        relation.AddEntityConstraint("equal",1,4);
                    }
                    int count=part.ComponentDefinition.Sketches.Count;
                    var ghost=backend.PreviewDesignAsync(state,relation.Operations(),ct).GetAwaiter().GetResult();
                    Check(part.ComponentDefinition.Sketches.Count==count,"Constraint preview rolls back new sketch");
                    string nativeType=circles ? "kEqualRadiusConstraintObject" : "kEqualLengthConstraintObject";
                    Check(ghost.Sketches.Single(s=>s.Name==relation.Name).Constraints.Contains(nativeType),"Downloaded native constraint snapshot");
                    state=backend.CommitDesignAsync(ghost,ct).GetAwaiter().GetResult();
                    Check(part.ComponentDefinition.Sketches.Count==count+1,"Constraint sketch committed");
                    context=backend.GetDesignContextAsync(state,ct).GetAwaiter().GetResult();
                    Check(context.SketchSnapshots.Single(s=>s.Name==relation.Name).Constraints.Contains(nativeType),"Committed constraint in deployed context");
                    history=backend.GetHistoryAsync(state,ct).GetAwaiter().GetResult();
                    state=backend.ApplyHistoryAsync(history,false,ct).GetAwaiter().GetResult();
                    Check(part.ComponentDefinition.Sketches.Count==count,"Undo removes constrained sketch");
                    Console.WriteLine("HTTP "+relation.Name+" preview / snapshot / commit / undo: PASS");
                }
            }
            // An ordinary native transaction must invalidate the XR history chain.
            var native = app.TransactionManager.StartTransaction((global::Inventor._Document)part,"Desktop guard probe");
            part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]);
            native.End();
            state = backend.GetDocumentStateAsync(ct).GetAwaiter().GetResult();
            history = backend.GetHistoryAsync(state,ct).GetAwaiter().GetResult();
            Check(!history.CanUndo && !history.CanRedo,"Desktop transaction cannot be undone through XR history");
            Console.WriteLine("HTTP history guard after desktop edit: PASS");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            try { if(part!=null) ((global::Inventor.Document)part).Close(true); }
            finally { original?.Activate(); }
        }
    }
    private static void Check(bool condition,string message)
    { if(!condition) throw new InvalidOperationException("Assertion failed: "+message); }
}
