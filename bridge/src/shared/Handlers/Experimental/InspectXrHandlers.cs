#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

public sealed class InspectXrHandler : ExperimentalHandler
{
    public override string Name => "inspect_xr";
    public override bool IsReadOnly => true;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var active = X.Active(app);
        string id = EntityReferences.DocumentId(active);
        if (X.Str(p, "document_id") != id) throw ConcurrencyFailure.DocumentChanged((string?)p["document_id"], id);
        if (ctx.Events == null || X.Str(p, "expected_revision") != ctx.Events.Revision(id))
            throw ConcurrencyFailure.StaleRevision((string?)p["expected_revision"], ctx.Events?.Revision(id));
        var doc = active;
        dynamic? occurrence = null;
        if (!string.IsNullOrEmpty((string?)p["occurrence_id"]))
        {
            object entity = X.Resolve(active, (string?)p["occurrence_id"], out _);
            if (entity is not ComponentOccurrence && entity is not ComponentOccurrenceProxy)
                throw new ArgumentException("occurrence_id must resolve to an occurrence.");
            occurrence = entity;
            doc = (global::Inventor.Document)occurrence.Definition.Document;
        }
        var result = new JObject { ["document_id"] = id, ["revision"] = ctx.Events.Revision(id),
            ["name"] = occurrence == null ? doc.DisplayName : (string)occurrence.Name };
        // Optional Inventor properties may be unavailable (suppressed/virtual components).
        void Read(string key, Func<object?> read)
        {
            try { var value = read(); result[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value); }
            catch { result[key] = JValue.CreateNull(); }
        }
        MassProperties? mass = null;
        try
        {
            mass = occurrence != null ? (MassProperties)occurrence.MassProperties : doc switch
            { PartDocument part => part.ComponentDefinition.MassProperties, AssemblyDocument assembly => assembly.ComponentDefinition.MassProperties, _ => null };
        }
        catch { }
        Read("mass_kg", () => mass?.Mass);
        Read("volume_mm3", () => mass == null ? null : (object)(mass.Volume * 1000));
        Read("area_mm2", () => mass == null ? null : (object)(mass.Area * 100));
        Read("material", () => doc is PartDocument part ? part.ActiveMaterial.DisplayName : null);
        Read("constraints", () => occurrence == null ? null : (object)occurrence.Constraints.Count);
        result["dof_translation"] = null; result["dof_rotation"] = null;
        if (occurrence != null)
        {
            try
            {
                int translations, rotations; ObjectsEnumerator translationAxes, rotationAxes; Point center;
                occurrence.GetDegreesOfFreedom(out translations, out translationAxes, out rotations, out rotationAxes, out center);
                result["dof_translation"] = translations; result["dof_rotation"] = rotations;
            }
            catch { /* null, never report unknown as constrained */ }
        }
        return result;
    }
}

public sealed class ActivateOpenDocumentXrHandler : ExperimentalHandler
{
    public override string Name => "activate_open_document_xr";
    public override bool IsReadOnly => true; // same classification as highlight/camera: view state only
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var id = X.Str(p, "document_id");
        var doc = X.Document(app, id);
        if (doc is not PartDocument && doc is not AssemblyDocument)
            throw new ArgumentException("XR requires an open part or assembly.");
        // Inventor 2027 exposes an unidentified CurrentTransaction while idle.
        // HasParentTransaction returns E_FAIL for that sentinel, but succeeds for
        // both root and nested identified transactions. An empty StartTransaction
        // probe would erase Redo, even when aborted, so activation must be read-only.
        if (app.CommandManager.ActiveCommand != "AppSelectNorthwestArrowCmd")
            throw ConcurrencyFailure.TransactionBusy();
        var current = app.TransactionManager.CurrentTransaction;
        if (current != null)
        {
            bool identified = true;
            try { _ = current.HasParentTransaction; }
            catch (COMException ex) when (ex.ErrorCode == unchecked((int)0x80004005)) { identified = false; }
            if (identified) throw ConcurrencyFailure.TransactionBusy();
        }
        X.Deadline(ctx, "before activation");
        // A loaded document without a window (typical for assembly definitions) makes Activate() fail
        // with E_FAIL. Showing its window is a view operation on an already-loaded document: no load
        // from disk, no save, no model change. Documents.Open(path, true) on a loaded document returns
        // the same Document and creates its window.
        Exception? activateError = null;
        try { doc.Activate(); }
        catch (COMException ex) { activateError = ex; }
        string? activeId = null;
        if (activateError == null) { try { activeId = EntityReferences.DocumentId(X.Active(app)); } catch { activeId = null; } }
        if (DocumentActivationPolicy.ShouldShowWindowAndRetry(activateError, id, activeId))
        {
            string fileName = string.Empty;
            try { fileName = doc.FullFileName; } catch { fileName = string.Empty; }
            if (!DocumentActivationPolicy.HasFileName(fileName))
                throw new InvalidOperationException(DocumentActivationPolicy.NoWindowNoFileMessage, activateError);
            X.Deadline(ctx, "before showing the document window");
            app.Documents.Open(fileName, true);
            doc.Activate();
        }
        if (EntityReferences.DocumentId(X.Active(app)) != id) throw new InvalidOperationException("ACTIVATE_NOT_CONFIRMED");
        return new JObject { ["document_id"] = id, ["status"] = "activated" };
    }
}
#endif
