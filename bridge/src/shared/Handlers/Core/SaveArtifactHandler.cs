#if INVENTOR2027
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Export;

namespace Bimwright.Ipt.Shared.Handlers.Core;

public sealed class SaveArtifactHandler : HandlerBase, IInventorCommand
{
    public string Name => "save_artifact";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly) return Fail(ctx, InventorErrorCodes.READ_ONLY, "Artifact writes require write permission.");
        var app = (Application)ctx.Application!;
        var doc = app.ActiveDocument;
        if (doc == null) return Fail(ctx, "NO_DOCUMENT", "No active document.");
        if (doc is not PartDocument && doc is not AssemblyDocument && doc is not DrawingDocument) return Fail(ctx, "WRONG_DOCUMENT_TYPE", "Part, assembly or drawing required.");
        string id = EntityReferences.DocumentId(doc);
        if ((string?)p["document_id"] != id) return Fail(ctx, "INVALID_ARGUMENT", "DOCUMENT_CHANGED");
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id)) return Fail(ctx, "INVALID_ARGUMENT", "STALE_REVISION");
        string format = (string?)p["format"] ?? "";
        string extension = ArtifactFormatPolicy.Extension(doc is PartDocument ? "part" : doc is AssemblyDocument ? "assembly" : "drawing",
            format, doc is DrawingDocument dwg && dwg.IsInventorDWG);
        SheetMetalComponentDefinition? sheetMetal = null;
        string dxfOptions = "";
        if (format == "dxf")
        {
            Dictionary<string, string>? layers = null;
            if (p["dxf_layers"] is JObject requestedLayers)
            {
                layers = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var layer in requestedLayers.Properties()) layers[layer.Name] = layer.Value.ToString();
            }
            try { dxfOptions = ArtifactFormatPolicy.FlatPatternDxfOptions((string?)p["dxf_version"], layers); }
            catch (ArgumentException ex) { return Fail(ctx, "INVALID_ARGUMENT", ex.Message); }
            if (doc is not PartDocument dxfPart || dxfPart.ComponentDefinition is not SheetMetalComponentDefinition dxfDef)
                return Fail(ctx, "WRONG_DOCUMENT_TYPE", "DXF output covers the flat pattern of an active sheet-metal part only.");
            if (!dxfDef.HasFlatPattern)
                return Fail(ctx, "INVALID_ARGUMENT", "NO_FLAT_PATTERN: create the flat pattern first; DXF is not inferred from the folded model.");
            sheetMetal = dxfDef;
        }
        string Geometry(global::Inventor.Document d) => d switch
        {
            PartDocument part => part.ComponentDefinition.ModelGeometryVersion,
            AssemblyDocument assembly => assembly.ComponentDefinition.ModelGeometryVersion,
            DrawingDocument drawing => drawing.DatabaseRevisionId,
            _ => throw new InvalidOperationException("Unexpected reference document type.")
        };
        var referenced = doc.AllReferencedDocuments.Cast<global::Inventor.Document>().ToArray();
        if (doc is AssemblyDocument || doc is DrawingDocument)
        {
            if (doc.RequiresUpdate) return Fail(ctx, "INVALID_ARGUMENT", "Document requires an update before export.");
            foreach (var d in referenced.Append(doc))
                foreach (DocumentDescriptor descriptor in d.ReferencedDocumentDescriptors)
                    if (descriptor.ReferenceMissing) return Fail(ctx, "INVALID_ARGUMENT", "Document has missing references.");
        }
        if (doc is DrawingDocument && format == "native" && referenced.Any(d => d.Dirty || string.IsNullOrWhiteSpace(d.FullFileName) || !System.IO.File.Exists(d.FullFileName)))
            return Fail(ctx, "INVALID_ARGUMENT", "SOURCE_REFERENCE_NOT_SAVED: native drawing copy requires saved clean model references; no referenced file will be saved automatically.");
        if (doc is DrawingDocument && format == "native")
            foreach (var reference in referenced)
            {
                if (!app.DesignProjectManager.IsFileInActiveProject(reference.FullFileName, out _, out _))
                    return Fail(ctx, "INVALID_ARGUMENT", "REFERENCE_OUTSIDE_PROJECT: native drawing references must resolve in the active Inventor project. No project settings were changed.");
            }
        var referenceStates = referenced.Select(d => new { Document = d, Path = d.FullFileName, Dirty = d.Dirty, Geometry = Geometry(d) }).ToArray();
        var transaction = app.TransactionManager.StartTransaction((Inventor._Document)doc, "Inventor SO export ownership check");
        bool nested = transaction.HasParentTransaction;
        transaction.Abort(); // our empty transaction only
        if (nested) return Fail(ctx, "INVALID_ARGUMENT", "TRANSACTION_BUSY");
        string originalPath = doc.FullFileName;
        bool dirty = doc.Dirty;
        string geometryVersion = Geometry(doc);
        string root = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "InventorSO", "artifacts");
        string path;
        bool priorSilent = app.SilentOperation;
        try
        {
        app.SilentOperation = true;
        path = SafeArtifactWriter.Write(root, extension, output =>
        {
            if (format == "native")
            {
                if (doc is DrawingDocument nativeDrawing && nativeDrawing.IsInventorDWG)
                    nativeDrawing.SaveAsInventorDWG(output, true);
                else
                {
                    var options = app.TransientObjects.CreateNameValueMap();
                    options.Add("SaveDependents", false);
                    doc.SaveAs2(output, true, options); // copy only, explicitly never save dependent documents
                }
            }
            else if (format == "dxf")
                // Inventor's own flat-pattern translator, not a generic 2D export of the folded model.
                sheetMetal!.FlatPattern.DataIO.WriteDataToFile(dxfOptions, output);
            else if (format == "pdf") ExportSupport.SavePdf(app, doc, output);
            else ExportSupport.SaveCopyAs(app, ExportSupport.GetTranslator(app, ExportSupport.StepTranslatorId, "STEP"), doc, output);
            // Save-copy itself emits save events. Compare observed source state, not the event cursor.
            if (doc.FullFileName != originalPath || doc.Dirty != dirty || Geometry(doc) != geometryVersion ||
                app.ActiveDocument == null || EntityReferences.DocumentId(app.ActiveDocument) != id)
                throw new InvalidOperationException("Source path, dirty flag, geometry or active document changed during export; inspect before continuing.");
            foreach (var state in referenceStates)
                if (state.Document.FullFileName != state.Path || state.Document.Dirty != state.Dirty || Geometry(state.Document) != state.Geometry)
                    throw new InvalidOperationException("Referenced source changed during export; inspect before continuing.");
        }, ctx.IsDeadlineExceeded);
        }
        finally { app.SilentOperation = priorSilent; }
        return Ok(ctx, new JObject { ["path"] = path, ["format"] = format, ["bytes"] = new FileInfo(path).Length,
            ["document_id"] = id, ["revision"] = ctx.Events.Revision(id), ["source_saved_in_place"] = false, ["overwritten"] = false,
            ["native_dependencies_packaged"] = false,
            ["flat_pattern_source"] = format == "dxf",
            ["dxf_version"] = format == "dxf" ? ((string?)p["dxf_version"] ?? ArtifactFormatPolicy.DefaultDxfVersion) : null,
            ["dxf_options"] = format == "dxf" ? dxfOptions : null,
            ["required_project"] = doc is DrawingDocument && format == "native" ? app.DesignProjectManager.ActiveDesignProject.FullFileName : null,
            ["portable_package"] = false,
            ["required_references"] = format == "native" ? new JArray(referenceStates.Select(s => new JObject
                { ["document_id"] = EntityReferences.DocumentId(s.Document), ["path"] = s.Path })) : new JArray() });
    }
}
#endif
