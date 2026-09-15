#if INVENTOR2027
using System;
using System.IO;
using File = System.IO.File;
using Path = System.IO.Path;
using Directory = System.IO.Directory;
using System.Linq;
using System.Security.Cryptography;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.SheetMetal;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// Document lifecycle restricted to the host-owned workspace: create, save in place, close and list.
/// Files the user opened from anywhere else are never saved or closed here; they keep using
/// <c>save_artifact</c> copies. Dependents are never saved automatically, so an assembly save reports
/// its dirty references as blockers instead of silently writing them.
/// </summary>
public sealed class WorkspaceDocumentHandler : HandlerBase, IInventorCommand
{
    private readonly string _action;
    public WorkspaceDocumentHandler(string action) => _action = action;
    public string Name => "workspace_" + _action + (_action == "list" ? "_documents" : "_document");
    public bool IsReadOnly => _action == "list";

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!IsReadOnly && ctx.ReadOnly)
            return Fail(ctx, InventorErrorCodes.READ_ONLY, "Workspace document writes require write permission.");
        var app = (Application)ctx.Application!;
        string root;
        try { root = WorkspaceDocumentPolicy.Root(); }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "WORKSPACE_NOT_CONFIGURED: " + ex.Message); }
        return _action switch
        {
            "list" => List(ctx, app, root),
            "new" => Create(ctx, app, root, p),
            "open" => Open(ctx, app, root, p),
            "activate" => Activate(ctx, app, root, p),
            "save" => Save(ctx, app, root, p),
            "close" => Close(ctx, app, root, p),
            _ => throw new InvalidOperationException("Unknown workspace action.")
        };
    }

    private InventorCommandResult List(InventorCommandContext ctx, Application app, string root)
    {
        var files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root).Where(WorkspaceDocumentPolicy.IsManagedExtension)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(501).ToArray()
            : Array.Empty<string>();
        bool truncated = files.Length > 500;
        var open = app.Documents.Cast<global::Inventor.Document>()
            .Where(d => !string.IsNullOrWhiteSpace(d.FullFileName)).ToArray();
        var documents = new JArray();
        foreach (var file in files.Take(500))
        {
            var opened = open.FirstOrDefault(d => string.Equals(d.FullFileName, file, StringComparison.OrdinalIgnoreCase));
            var info = new FileInfo(file);
            documents.Add(new JObject
            {
                ["name"] = Path.GetFileNameWithoutExtension(file),
                ["path"] = file,
                ["bytes"] = info.Length,
                ["modified_utc"] = info.LastWriteTimeUtc.ToString("O"),
                ["open"] = opened != null,
                ["document_id"] = opened == null ? null : EntityReferences.DocumentId(opened),
                ["dirty"] = opened?.Dirty,
            });
        }
        return Ok(ctx, new JObject { ["workspace_root"] = root, ["documents"] = documents, ["truncated"] = truncated });
    }

    private InventorCommandResult Create(InventorCommandContext ctx, Application app, string root, JObject p)
    {
        string kind = (string?)p["kind"] ?? "";
        string path;
        try { path = WorkspaceDocumentPolicy.NewPath(root, (string?)p["name"], kind); }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException)
        { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }

        if (app.ActiveDocument != null)
        {
            var probe = app.TransactionManager.StartTransaction((Inventor._Document)app.ActiveDocument, "Inventor SO workspace ownership check");
            bool nested = probe.HasParentTransaction;
            probe.Abort();
            if (nested) return Fail(ctx, ConcurrencyFailure.TransactionBusy());
        }
        if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before document creation.");
        Directory.CreateDirectory(root);
        SafeArtifactWriter.CheckAncestors(root);
        bool sheetMetal = kind == "sheet_metal";
        var type = kind == "assembly" ? DocumentTypeEnum.kAssemblyDocumentObject : DocumentTypeEnum.kPartDocumentObject;
        string template;
        try
        {
            // The sheet-metal sub-type selects Inventor's own sheet-metal template; a plain part
            // template would produce a document where no sheet-metal feature can be created.
            template = sheetMetal
                ? app.FileManager.GetTemplateFile(type, SystemOfMeasureEnum.kDefaultSystemOfMeasure,
                    DraftingStandardEnum.kDefault_DraftingStandard, SheetMetalSupport.SheetMetalSubType)
                : app.FileManager.GetTemplateFile(type);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR,
                "TEMPLATE_UNAVAILABLE: Inventor did not supply a" + (sheetMetal ? " sheet-metal" : "") + " template: " + ex.Message);
        }
        bool priorSilent = app.SilentOperation;
        global::Inventor.Document doc;
        try
        {
            app.SilentOperation = true;
            doc = app.Documents.Add(type, template, true);
            try
            {
                if (File.Exists(path)) throw new IOException("A workspace document with that name appeared during creation.");
                doc.SaveAs(path, false); // real SaveAs: the new document is bound to the workspace file
            }
            catch
            {
                try { doc.Close(true); } catch { /* leave inspection to the caller */ }
                throw;
            }
        }
        finally { app.SilentOperation = priorSilent; }

        if (!string.Equals(doc.FullFileName, path, StringComparison.OrdinalIgnoreCase) || doc.Dirty || !File.Exists(path))
            throw new InvalidOperationException("NEW_DOCUMENT_NOT_PERSISTED: inspect the workspace; no other document was touched.");
        bool isSheetMetal = doc is PartDocument created && created.ComponentDefinition is SheetMetalComponentDefinition;
        if (sheetMetal && !isSheetMetal)
        {
            // Never hand back a plain part pretending to be sheet metal.
            try { doc.Close(true); } catch { /* leave inspection to the caller */ }
            try { File.Delete(path); } catch { /* the file is reported below if it survives */ }
            return Fail(ctx, InventorErrorCodes.API_ERROR,
                "SHEET_METAL_TEMPLATE_MISMATCH: the resolved template did not produce a sheet-metal part; check the Inventor template configuration.");
        }
        string id = EntityReferences.DocumentId(doc);
        bool inProject = app.DesignProjectManager.IsFileInActiveProject(path, out _, out _);
        return Ok(ctx, new JObject
        {
            ["status"] = "created",
            ["document_id"] = id,
            ["revision"] = ctx.Events?.Revision(id),
            ["path"] = path,
            ["kind"] = kind,
            ["sheet_metal"] = isSheetMetal,
            ["bytes"] = new FileInfo(path).Length,
            ["sha256"] = Hash(path),
            ["active"] = app.ActiveDocument != null && EntityReferences.DocumentId(app.ActiveDocument) == id,
            ["workspace_managed"] = true,
            ["workspace_root"] = root,
            ["in_active_project"] = inProject,
            ["active_project"] = app.DesignProjectManager.ActiveDesignProject.FullFileName,
            ["reopen_outside_project_may_fail"] = !inProject,
        });
    }

    private InventorCommandResult Open(InventorCommandContext ctx, Application app, string root, JObject p)
    {
        string path;
        try { path = WorkspaceDocumentPolicy.ExistingPath(root, (string?)p["file"]); }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException)
        { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        SafeArtifactWriter.CheckAncestors(path);
        var already = app.Documents.Cast<global::Inventor.Document>()
            .FirstOrDefault(d => string.Equals(d.FullFileName, path, StringComparison.OrdinalIgnoreCase));
        if (already != null)
        {
            string openId = EntityReferences.DocumentId(already);
            return Ok(ctx, new JObject { ["status"] = "already_open", ["document_id"] = openId,
                ["revision"] = ctx.Events?.Revision(openId), ["path"] = path, ["dirty"] = already.Dirty,
                ["workspace_root"] = root, ["activated"] = false });
        }
        if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before open.");
        bool priorSilent = app.SilentOperation;
        global::Inventor.Document opened;
        try
        {
            // Silent operation keeps an unattended reference-resolution dialog from blocking the STA thread.
            app.SilentOperation = true;
            opened = app.Documents.Open(path, true);
        }
        finally { app.SilentOperation = priorSilent; }
        if (!string.Equals(opened.FullFileName, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OPENED_UNEXPECTED_DOCUMENT: inspect open documents before continuing.");
        var missing = opened.AllReferencedDocuments.Cast<global::Inventor.Document>().Append(opened)
            .SelectMany(d => d.ReferencedDocumentDescriptors.Cast<DocumentDescriptor>())
            .Where(descriptor => descriptor.ReferenceMissing)
            .Select(descriptor => (JToken)(descriptor.FullDocumentName ?? "unknown")).ToArray();
        string id = EntityReferences.DocumentId(opened);
        return Ok(ctx, new JObject
        {
            ["status"] = "opened",
            ["document_id"] = id,
            ["revision"] = ctx.Events?.Revision(id),
            ["path"] = path,
            ["dirty"] = opened.Dirty,
            ["requires_update"] = opened.RequiresUpdate,
            ["missing_references"] = new JArray(missing),
            ["workspace_root"] = root,
            ["activated"] = app.ActiveDocument != null && EntityReferences.DocumentId(app.ActiveDocument) == id,
        });
    }

    /// <summary>
    /// Brings one open workspace document to the front. Modelling commands act on the active document,
    /// so carrying a value from one part into another needs this rather than a user clicking a tab.
    /// Documents outside the workspace are left alone, as everywhere else.
    /// </summary>
    private InventorCommandResult Activate(InventorCommandContext ctx, Application app, string root, JObject p)
    {
        string documentId = (string?)p["document_id"] ?? "";
        var matches = app.Documents.Cast<global::Inventor.Document>()
            .Where(d => EntityReferences.DocumentId(d) == documentId).ToArray();
        if (matches.Length != 1)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                matches.Length == 0 ? "DOCUMENT_NOT_OPEN" : "AMBIGUOUS_DOCUMENT");
        var doc = matches[0];
        string path = doc.FullFileName;
        if (!WorkspaceDocumentPolicy.IsInside(root, path))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "OUTSIDE_WORKSPACE: only host-owned workspace documents are activated here.");
        if (app.ActiveDocument != null && EntityReferences.DocumentId(app.ActiveDocument) == documentId)
            return Ok(ctx, new JObject { ["status"] = "already_active", ["document_id"] = documentId,
                ["path"] = path, ["revision"] = ctx.Events?.Revision(documentId) });
        if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before activation.");
        doc.Activate();
        if (app.ActiveDocument == null || EntityReferences.DocumentId(app.ActiveDocument) != documentId)
            throw new InvalidOperationException("ACTIVATE_NOT_CONFIRMED: another document is in front; inspect before continuing.");
        return Ok(ctx, new JObject
        {
            ["status"] = "activated",
            ["document_id"] = documentId,
            ["path"] = path,
            ["dirty"] = doc.Dirty,
            ["revision"] = ctx.Events?.Revision(documentId),
        });
    }

    private InventorCommandResult Save(InventorCommandContext ctx, Application app, string root, JObject p)
    {
        var doc = app.ActiveDocument;
        if (doc == null) return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "No active document.");
        string id = EntityReferences.DocumentId(doc);
        if ((string?)p["document_id"] != id) return Fail(ctx, ConcurrencyFailure.DocumentChanged((string?)p["document_id"], id));
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id))
            return Fail(ctx, ConcurrencyFailure.StaleRevision((string?)p["expected_revision"], ctx.Events?.Revision(id)));
        string path = doc.FullFileName;
        string? name = (string?)p["name"];
        if (string.IsNullOrWhiteSpace(path)) return SaveNew(ctx, app, root, doc, id, name);
        if (!string.IsNullOrWhiteSpace(name))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "ALREADY_ON_DISK: name only applies to a document that has never been saved; renaming or copying an existing document is not supported here.");
        if (!WorkspaceDocumentPolicy.IsInside(root, path))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "OUTSIDE_WORKSPACE: this document is not host-owned and is never written in place. Export a copy with inventor_save_artifact.");
        if (!File.Exists(path))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "WORKSPACE_FILE_MISSING: the workspace file backing this document no longer exists.");
        SafeArtifactWriter.CheckAncestors(path);

        var referenced = doc.AllReferencedDocuments.Cast<global::Inventor.Document>().ToArray();
        if (doc is AssemblyDocument || doc is DrawingDocument)
        {
            if (doc.RequiresUpdate) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "Document requires an update before saving.");
            foreach (var d in referenced.Append(doc))
                foreach (DocumentDescriptor descriptor in d.ReferencedDocumentDescriptors)
                    if (descriptor.ReferenceMissing) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "Document has missing references.");
        }
        var unsaved = referenced.Where(d => d.Dirty || string.IsNullOrWhiteSpace(d.FullFileName) || !File.Exists(d.FullFileName)).ToArray();
        if (unsaved.Length > 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "REFERENCE_NOT_SAVED: dependents are never saved automatically. Save each referenced workspace document first: " +
                string.Join(", ", unsaved.Select(d => string.IsNullOrWhiteSpace(d.FullFileName) ? d.DisplayName : d.FullFileName)));

        var transaction = app.TransactionManager.StartTransaction((Inventor._Document)doc, "Inventor SO workspace save ownership check");
        bool nested = transaction.HasParentTransaction;
        transaction.Abort();
        if (nested) return Fail(ctx, ConcurrencyFailure.TransactionBusy());

        var referenceStates = referenced.Select(d => new { Document = d, Path = d.FullFileName, Hash = Fingerprint(d.FullFileName) }).ToArray();
        if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before save.");
        bool priorSilent = app.SilentOperation;
        try
        {
            app.SilentOperation = true;
            doc.Save2(false); // explicitly never save dependent documents
        }
        finally { app.SilentOperation = priorSilent; }

        if (!string.Equals(doc.FullFileName, path, StringComparison.OrdinalIgnoreCase) || doc.Dirty)
            throw new InvalidOperationException("SAVE_NOT_CONFIRMED: the document path or dirty flag is unexpected; inspect before continuing.");
        foreach (var state in referenceStates)
            if (state.Document.FullFileName != state.Path || Fingerprint(state.Document.FullFileName) != state.Hash)
                throw new InvalidOperationException("REFERENCE_WRITTEN_DURING_SAVE: a referenced file changed; inspect before continuing.");
        return Ok(ctx, new JObject
        {
            ["status"] = "saved_in_place",
            ["document_id"] = id,
            ["revision"] = ctx.Events.Revision(id),
            ["path"] = path,
            ["bytes"] = new FileInfo(path).Length,
            ["sha256"] = Hash(path),
            ["source_saved_in_place"] = true,
            ["dependents_saved"] = false,
            ["workspace_managed"] = true,
        });
    }

    private InventorCommandResult Close(InventorCommandContext ctx, Application app, string root, JObject p)
    {
        string documentId = (string?)p["document_id"] ?? "";
        var matches = app.Documents.Cast<global::Inventor.Document>()
            .Where(d => EntityReferences.DocumentId(d) == documentId).ToArray();
        if (matches.Length != 1)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                matches.Length == 0 ? "DOCUMENT_NOT_OPEN" : "AMBIGUOUS_DOCUMENT");
        var doc = matches[0];
        string path = doc.FullFileName;
        if (!WorkspaceDocumentPolicy.IsInside(root, path))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "OUTSIDE_WORKSPACE: only host-owned workspace documents are closed here; user documents are left untouched.");
        bool discard = p["discard_changes"]?.Type == JTokenType.Boolean && (bool)p["discard_changes"]!;
        if (doc.Dirty && !discard)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "UNSAVED_CHANGES: save the document first, or repeat with discard_changes=true to lose them.");
        foreach (var other in app.Documents.Cast<global::Inventor.Document>())
        {
            if (ReferenceEquals(other, doc)) continue;
            if (other.AllReferencedDocuments.Cast<global::Inventor.Document>().Any(r => ReferenceEquals(r, doc)))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    "REFERENCED_BY_OPEN_DOCUMENT: " + other.DisplayName + " still references this document; close that first.");
        }
        string hash = Fingerprint(path);
        if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before close.");
        bool priorSilent = app.SilentOperation;
        try
        {
            app.SilentOperation = true;
            doc.Close(true); // SkipSave: closing never writes
        }
        finally { app.SilentOperation = priorSilent; }
        if (app.Documents.Cast<global::Inventor.Document>().Any(d => EntityReferences.DocumentId(d) == documentId))
            throw new InvalidOperationException("CLOSE_NOT_CONFIRMED: the document is still open; inspect before continuing.");
        if (File.Exists(path) && Fingerprint(path) != hash)
            throw new InvalidOperationException("FILE_CHANGED_DURING_CLOSE: the workspace file was written while closing; inspect before continuing.");
        return Ok(ctx, new JObject
        {
            ["status"] = "closed",
            ["document_id"] = documentId,
            ["path"] = path,
            ["saved_on_close"] = false,
            ["discarded_changes"] = discard,
            ["file_retained"] = File.Exists(path),
        });
    }

    /// <summary>
    /// First save of a document that has never been on disk, such as a draft from create_drawing_safe.
    /// It writes one new workspace file and never rebinds a document already saved somewhere else.
    /// </summary>
    private InventorCommandResult SaveNew(InventorCommandContext ctx, Application app, string root,
        global::Inventor.Document doc, string id, string? name)
    {
        string extension = doc switch
        {
            PartDocument => ".ipt",
            AssemblyDocument => ".iam",
            DrawingDocument drawing => drawing.IsInventorDWG ? ".dwg" : ".idw",
            _ => ""
        };
        if (extension.Length == 0)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "Only parts, assemblies and drawings are saved into the workspace.");
        string path;
        try { path = WorkspaceDocumentPolicy.NewPathWithExtension(root, name, extension); }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException)
        { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }

        var referenced = doc.AllReferencedDocuments.Cast<global::Inventor.Document>().ToArray();
        if (doc.RequiresUpdate) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "Document requires an update before saving.");
        var unsaved = referenced.Where(d => d.Dirty || string.IsNullOrWhiteSpace(d.FullFileName) || !File.Exists(d.FullFileName)).ToArray();
        if (unsaved.Length > 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "REFERENCE_NOT_SAVED: dependents are never saved automatically. Save each referenced workspace document first: " +
                string.Join(", ", unsaved.Select(d => string.IsNullOrWhiteSpace(d.FullFileName) ? d.DisplayName : d.FullFileName)));
        var referenceStates = referenced.Select(d => new { Document = d, Path = d.FullFileName, Hash = Fingerprint(d.FullFileName) }).ToArray();
        var transaction = app.TransactionManager.StartTransaction((Inventor._Document)doc, "Inventor SO workspace first save ownership check");
        bool nested = transaction.HasParentTransaction;
        transaction.Abort();
        if (nested) return Fail(ctx, ConcurrencyFailure.TransactionBusy());
        if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before first save.");
        Directory.CreateDirectory(root);
        SafeArtifactWriter.CheckAncestors(root);
        bool priorSilent = app.SilentOperation;
        try
        {
            app.SilentOperation = true;
            if (File.Exists(path)) throw new IOException("A workspace document with that name appeared during the save.");
            doc.SaveAs(path, false); // binds this document to the workspace file; SaveCopyAs=false
        }
        finally { app.SilentOperation = priorSilent; }
        if (!string.Equals(doc.FullFileName, path, StringComparison.OrdinalIgnoreCase) || doc.Dirty || !File.Exists(path))
            throw new InvalidOperationException("SAVE_NOT_CONFIRMED: inspect the workspace before continuing.");
        foreach (var state in referenceStates)
            if (state.Document.FullFileName != state.Path || Fingerprint(state.Document.FullFileName) != state.Hash)
                throw new InvalidOperationException("REFERENCE_WRITTEN_DURING_SAVE: a referenced file changed; inspect before continuing.");
        return Ok(ctx, new JObject
        {
            ["status"] = "saved_into_workspace",
            ["document_id"] = id,
            ["revision"] = ctx.Events?.Revision(id),
            ["path"] = path,
            ["bytes"] = new FileInfo(path).Length,
            ["sha256"] = Hash(path),
            ["source_saved_in_place"] = true,
            ["dependents_saved"] = false,
            ["workspace_managed"] = true,
            ["workspace_root"] = root,
            ["required_references"] = new JArray(referenceStates.Select(state => (JToken)state.Path)),
        });
    }

    /// <summary>
    /// SHA-256 of a file Inventor is not holding open, or null. Inventor keeps a handle on some saved
    /// documents (Inventor DWG among them), so a locked file is reported as "no digest", never as a
    /// failed save.
    /// </summary>
    private static string? Hash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(stream));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Change detector for the untouched-source guards. Falls back to size and write time when the
    /// digest is unavailable, so a locked file still fails loudly if it is written behind our back.
    /// </summary>
    private static string Fingerprint(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "absent";
        string? digest = Hash(path);
        if (digest != null) return "sha256:" + digest;
        var info = new FileInfo(path);
        return "size:" + info.Length + ":written:" + info.LastWriteTimeUtc.Ticks;
    }
}
#endif
