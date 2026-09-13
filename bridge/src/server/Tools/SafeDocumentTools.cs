using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Host-owned document lifecycle: create, save in place and close documents inside the managed
/// InventorSO workspace only. Documents the user opened from anywhere else are never written or
/// closed here; they keep going through <c>inventor_save_artifact</c> copies.
/// </summary>
[McpServerToolType]
public sealed class SafeDocumentTools
{
    private readonly PluginClient _client;
    public SafeDocumentTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_new_document_safe"), Description("Create a new part or assembly from the host default template and save it once into the managed InventorSO workspace, leaving it open and active. kind is 'part' or 'assembly'. name accepts 1-60 letters, digits, space, underscore or hyphen and never overwrites an existing workspace document. Only the new file is written: no user document, project setting or template is modified. in_active_project=false means Inventor may fail to resolve this file after reopening from another project. Drawings are created by inventor_create_drawing_safe instead.")]
    public Task<string> NewDocument(string name, string kind, CancellationToken ct = default)
        => Call("workspace_new_document", new JObject { ["name"] = name, ["kind"] = kind }, ct);

    [McpServerTool(Name = "inventor_open_document_safe"), Description("Open one existing workspace document by file name with extension (.ipt, .iam, .idw or .dwg), so work from an earlier session continues. Only the managed InventorSO workspace is searched; arbitrary paths and user directories are refused. A document already open is reported as already_open without reopening. Opens silently to avoid unattended reference-resolution dialogs and reports dirty, requires_update and any missing_references rather than repairing them. Opening a CAD file is not a rollbackable transaction.")]
    public Task<string> OpenDocument(string file, CancellationToken ct = default)
        => Call("workspace_open_document", new JObject { ["file"] = file }, ct);

    [McpServerTool(Name = "inventor_activate_document_safe"), Description("Bring one open workspace document to the front, so the modelling commands act on it. Needed whenever work moves between documents, for example to carry a dimension read from one part into another. Refuses documents outside the managed workspace and reports already_active when nothing changes. Activation alone modifies nothing.")]
    public Task<string> ActivateDocument(string document_id, CancellationToken ct = default)
        => Call("workspace_activate_document", new JObject { ["document_id"] = document_id }, ct);

    [McpServerTool(Name = "inventor_save_document_safe"), Description("Save the active document in place, only when its file lives inside the managed InventorSO workspace; anything else fails with OUTSIDE_WORKSPACE. Requires document_id and expected_revision. Dependents are NEVER saved automatically: dirty or unsaved references fail with REFERENCE_NOT_SAVED so each one is saved explicitly first. Assemblies and drawings must be updated with no missing references. Returns the written size and SHA-256. Saving a file is not a rollbackable CAD transaction. A document that has never been saved, such as a draft from inventor_create_drawing_safe, needs name: it is written once into the workspace as a new file and the document is bound to it. name is refused for a document already on disk; it never renames, copies or relocates an existing file.")]
    public Task<string> SaveDocument(string document_id, string expected_revision, string? name = null, CancellationToken ct = default)
        => Call("workspace_save_document", new JObject { ["document_id"] = document_id,
            ["expected_revision"] = expected_revision, ["name"] = name }, ct);

    [McpServerTool(Name = "inventor_close_document_safe"), Description("Close one open workspace document by document_id without ever saving it. Refuses documents outside the managed workspace, documents still referenced by another open document, and unsaved changes unless discard_changes=true (which loses them). The file on disk is retained as last saved.")]
    public Task<string> CloseDocument(string document_id, bool discard_changes = false, CancellationToken ct = default)
        => Call("workspace_close_document", new JObject { ["document_id"] = document_id, ["discard_changes"] = discard_changes }, ct);

    private async Task<string> Call(string command, JObject arguments, CancellationToken ct)
    {
        try { return (await _client.SendAsync(command, arguments, ct)).ToString(); }
        catch (InventorGatewayException ex)
        { return ex.ToErrorJson().ToString(Formatting.None); }
    }
}
