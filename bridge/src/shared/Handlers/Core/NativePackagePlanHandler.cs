#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Core;

public sealed class NativePackagePlanHandler : HandlerBase, IInventorCommand
{
    public string Name => "plan_native_package";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        var doc = app.ActiveDocument;
        if (doc is not PartDocument && doc is not AssemblyDocument && doc is not DrawingDocument)
            return Fail(ctx, "WRONG_DOCUMENT_TYPE", "Active part, assembly or drawing required.");
        string id = EntityReferences.DocumentId(doc);
        if ((string?)p["document_id"] != id) return Fail(ctx, "INVALID_ARGUMENT", "DOCUMENT_CHANGED");
        string? revision = ctx.Events?.Revision(id);
        if (revision == null || (string?)p["expected_revision"] != revision) return Fail(ctx, "INVALID_ARGUMENT", "STALE_REVISION");
        var project = app.DesignProjectManager.ActiveDesignProject;
        string projectPath = project.FullFileName;
        var documents = doc.AllReferencedDocuments.Cast<global::Inventor.Document>().Prepend(doc).Take(10001).ToArray();
        var nodes = documents.Select(d => new NativePackagePlan.Node(EntityReferences.DocumentId(d), d.FullFileName,
            d.Dirty, d.RequiresUpdate,
            d.File.ReferencedFileDescriptors.Cast<FileDescriptor>().Select(r => r.FullFileName).ToArray())).ToArray();
        var result = NativePackagePlan.Build(project.WorkspacePath, nodes);
        var blockers = (JArray)result["blockers"]!;
        foreach (var d in documents)
        {
            // AllReferencedDocuments alone does not prove closure of suppressed/OLE references.
            foreach (FileDescriptor reference in d.File.ReferencedFileDescriptors)
                if (reference.ReferenceMissing || reference.ReferenceDisabled)
                    blockers.Add(new JObject { ["document_id"] = EntityReferences.DocumentId(d), ["reason"] = "UNRESOLVED_OR_DISABLED_REFERENCE" });
            if (d.ReferencedOLEFileDescriptors.Count != 0)
                blockers.Add(new JObject { ["document_id"] = EntityReferences.DocumentId(d), ["reason"] = "OLE_DEPENDENCIES_REQUIRE_PACKAGING_SUPPORT" });
        }
        for (int i = 0; i < documents.Length; i++)
            if (documents[i].FullFileName != nodes[i].Path || documents[i].Dirty != nodes[i].Dirty || documents[i].RequiresUpdate != nodes[i].RequiresUpdate)
                return Fail(ctx, "INVALID_ARGUMENT", "DOCUMENT_CHANGED_DURING_PLAN");
        if (app.ActiveDocument == null || EntityReferences.DocumentId(app.ActiveDocument) != id ||
            ctx.Events!.Revision(id) != revision || app.DesignProjectManager.ActiveDesignProject.FullFileName != projectPath)
            return Fail(ctx, "INVALID_ARGUMENT", "DOCUMENT_CHANGED_DURING_PLAN");
        result["ready_for_copy"] = blockers.Count == 0;
        result["document_id"] = id; result["revision"] = revision; result["project"] = projectPath;
        return Ok(ctx, result);
    }
}
#endif
