using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Support;

/// <summary>Scriptable in-memory backend for session and selection logic.</summary>
public sealed class FakeBackend : IInventorBackend
{
    private readonly object _callsLock = new();
    private readonly List<string> _calls = new();

    /// <summary>A snapshot taken under the lock: safe to enumerate while the session keeps calling in the background.</summary>
    public List<string> Calls { get { lock (_callsLock) return new List<string>(_calls); } }

    public CapabilitiesInfo Capabilities { get; set; } = CapabilitiesInfo.FromJson(JObject.Parse(
        @"{""target"":{""reachable"":true},""capabilities"":{""xr_mesh"":true,""scene_graph"":true,""highlight"":true}}"));
    public DocumentState State { get; set; } = new DocumentState("doc", "r1", "v1");
    public Func<SceneGraph> Scene { get; set; }
    public Dictionary<string, byte[]> Meshes { get; } = new();
    public HashSet<string> TooLarge { get; } = new();
    public Exception FailNext { get; set; }
    public Func<string, string, string> Pick { get; set; } = (occ, face) => "proxy:" + occ + ":" + face;
    public Action RaiseChanged { get; private set; }

    private Task Step(string call)
    {
        lock (_callsLock) { _calls.Add(call); }
        if (FailNext is { } failure) { FailNext = null; throw failure; }
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(CancellationToken ct) => await Step("connect");
    public async Task<CapabilitiesInfo> GetCapabilitiesAsync(CancellationToken ct) { await Step("capabilities"); return Capabilities; }
    public async Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) { await Step("state"); return State; }
    public async Task<SceneGraph> GetSceneGraphAsync(CancellationToken ct) { await Step("scene"); return Scene(); }

    public async Task<DefinitionMesh> GetDefinitionMeshAsync(string id, CancellationToken ct)
    {
        await Step("mesh:" + id);
        if (TooLarge.Contains(id)) throw new McpToolException("inventor_get_display_mesh", "MESH_TOO_LARGE", "too large", null);
        return DefinitionMesh.FromJson(id, new JObject { ["asset"] = new JObject { ["asset_id"] = "a_" + id, ["asset_url"] = "/assets/a_" + id } });
    }

    public async Task<byte[]> GetAssetAsync(DefinitionMesh mesh, CancellationToken ct) { await Step("asset:" + mesh.DefinitionDocumentId); return Meshes[mesh.DefinitionDocumentId]; }
    public async Task<string> PickFaceAsync(string occ, string face, CancellationToken ct) { await Step("pick:" + occ + ":" + face); return Pick(occ, face); }
    public async Task HighlightAsync(IReadOnlyList<string> ids, CancellationToken ct) => await Step("highlight:" + string.Join(",", ids));
    public async Task ClearHighlightAsync(CancellationToken ct) => await Step("clear");

    public async Task RunEventsAsync(Action onChanged, CancellationToken ct)
    {
        await Step("events");
        RaiseChanged = onChanged;
        // A fresh stream per connection, open until the session cancels it.
        var open = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() => open.TrySetCanceled()))
            await open.Task;
    }

    /// <summary>Two-part assembly scene over definitions p and q.</summary>
    public static SceneGraph Assembly(string visual = "v1", string documentId = "doc") => SceneGraph.FromJson(JObject.Parse(
        @"{""document_id"":""" + documentId + @""",""kind"":""assembly"",""revision"":""r1"",""visual_revision"":""" + visual + @""",
          ""definition_document_ids"":[""p"",""q""],
          ""root"":{""name"":""Top.iam"",""definition_kind"":""assembly"",""children"":[
            {""name"":""P:1"",""occurrence_id"":""ent_1"",""definition_document_id"":""p"",""definition_kind"":""part"",""children"":[]},
            {""name"":""Q:1"",""occurrence_id"":""ent_2"",""definition_document_id"":""q"",""definition_kind"":""part"",""children"":[]}]}}"));
}
