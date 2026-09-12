using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Bimwright.Ipt.Shared.Handlers.Core;
using Newtonsoft.Json.Linq;

internal static class Program
{
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object app);

    [STAThread]
    private static int Main(string[] args)
    {
        // Load installed Autodesk interop; do not redistribute it in test output.
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name == "Autodesk.Inventor.Interop"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(@"C:\Program Files\Common Files\Autodesk Shared\Extensions 2027\Framework\Interop\Autodesk.Inventor.Interop.dll") : null;
        return args.Contains("--fixture") ? Fixture() : Probe();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Fixture()
    {
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("Inventor.Application", out var clsid));
        GetActiveObject(ref clsid, IntPtr.Zero, out var com);
        var app = (Inventor.Application)com;
        var active = app.ActiveDocument;
        using var tracker = new Bimwright.Ipt.Shared.Plugin.CadEventTracker(app);
        int count = app.Documents.Count;
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "live-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "reference-roundtrip.ipt");
        Inventor.PartDocument? part = null;
        try
        {
            var template = app.FileManager.GetTemplateFile(Inventor.DocumentTypeEnum.kPartDocumentObject);
            part = (Inventor.PartDocument)app.Documents.Add(Inventor.DocumentTypeEnum.kPartDocumentObject, template, false);
            var comp = part.ComponentDefinition;
            var sketch = comp.Sketches.Add(comp.WorkPlanes[3]);
            sketch.SketchCircles.AddByCenterRadius(app.TransientGeometry.CreatePoint2d(0, 0), 1.0);
            var profile = sketch.Profiles.AddForSolid();
            var definition = comp.Features.ExtrudeFeatures.CreateExtrudeDefinition(profile, Inventor.PartFeatureOperationEnum.kJoinOperation);
            definition.SetDistanceExtent(2.0, Inventor.PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            var feature = comp.Features.ExtrudeFeatures.Add(definition);
            part.SaveAs(path, false);
            var reference = EntityReferences.Describe((Inventor.Document)part, comp.SurfaceBodies[1].Faces[1]);
            var id = (string)reference["id"]!;
            string revisionBefore = tracker.Journal.Revision(EntityReferences.DocumentId((Inventor.Document)part));
            AssertResolved(app, id, "initial");
            var extent = (Inventor.DistanceExtent)feature.Definition.Extent;
            extent.Distance.Expression = "30 mm";
            if (!part.Update2()) throw new InvalidOperationException("Fixture rebuild failed");
            AssertResolved(app, id, "after rebuild");
            if (tracker.Journal.Revision(EntityReferences.DocumentId((Inventor.Document)part)) == revisionBefore)
                throw new InvalidOperationException("No document change event observed for fixture edit");
            part.Save();
            part.Close(true);
            part = null;
            part = (Inventor.PartDocument)app.Documents.Open(path, false);
            AssertResolved(app, id, "after reopen");
            if (!((JArray)tracker.Journal.Read()["events"]!).Any(e => (string?)e["type"] == "document_closed" && (string?)e["document_id"] == (string?)reference["document_id"]))
                throw new InvalidOperationException("Closed document event lost its document identity");
            Console.WriteLine(new JObject { ["probe"] = "face reference lifecycle", ["initial"] = "resolved", ["after_rebuild"] = "resolved",
                ["after_reopen"] = "resolved", ["fixture_path"] = path, ["document_events"] = tracker.Journal.Read() });
        }
        finally
        {
            part?.Close(true);
            if (active != null && app.ActiveDocument?.InternalName != active.InternalName) active.Activate();
        }
        if (app.Documents.Count != count) throw new InvalidOperationException("Document count was not restored");
        return 0;
    }

    private static void AssertResolved(Inventor.Application app, string id, string phase)
    {
        var result = EntityReferences.Resolve(app, id);
        if ((string?)result["status"] != "resolved") throw new InvalidOperationException(phase + ": " + result);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Probe()
    {
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("Inventor.Application", out var clsid));
        GetActiveObject(ref clsid, IntPtr.Zero, out var com);
        var app = (Inventor.Application)com;
        var doc = app.ActiveDocument ?? throw new InvalidOperationException("No active document");
        object entity = doc switch
        {
            Inventor.AssemblyDocument assembly when assembly.ComponentDefinition.Occurrences.Count > 0 => assembly.ComponentDefinition.Occurrences[1],
            Inventor.PartDocument part when part.ComponentDefinition.SurfaceBodies.Count > 0 => part.ComponentDefinition.SurfaceBodies[1].Faces[1],
            _ => throw new InvalidOperationException("Probe needs an existing assembly occurrence or part face")
        };
        int documentCount = app.Documents.Count;
        bool dirty = doc.Dirty;
        string revision = doc.DatabaseRevisionId;
        var descriptor = EntityReferences.Describe(doc, entity);
        var resolved = EntityReferences.Resolve(app, (string)descriptor["id"]!);
        if ((string?)resolved["status"] != "resolved") throw new InvalidOperationException(resolved.ToString());
        if (doc.Dirty != dirty || doc.DatabaseRevisionId != revision || app.Documents.Count != documentCount)
            throw new InvalidOperationException("Read-only probe changed observed document state");
        Console.WriteLine(new JObject { ["probe"] = "read-only persistent reference roundtrip", ["type"] = descriptor["type"],
            ["status"] = resolved["status"], ["document_count_unchanged"] = true, ["dirty_unchanged"] = true,
            ["database_revision_unchanged"] = true, ["reference_length"] = ((string)descriptor["id"]!).Length });
        return 0;
    }
}
