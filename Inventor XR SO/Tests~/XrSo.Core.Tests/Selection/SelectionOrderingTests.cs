using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Selection;

namespace InventorXrSo.Core.Tests.Selection;

/// <summary>Backend whose Highlight/Pick completions the test releases by hand, to script an out-of-order network.</summary>
internal sealed class ScriptedBackend : IInventorBackend
{
    private readonly object _lock = new();
    public List<string> Log { get; } = new();
    public Dictionary<string, TaskCompletionSource<bool>> Highlights { get; } = new();
    public Dictionary<string, TaskCompletionSource<string>> Picks { get; } = new();

    private void Add(string call) { lock (_lock) Log.Add(call); }
    public List<string> Snapshot() { lock (_lock) return new List<string>(Log); }

    public TaskCompletionSource<bool> HighlightOf(string id)
    {
        lock (_lock)
        {
            if (!Highlights.TryGetValue(id, out var tcs))
                Highlights[id] = tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return tcs;
        }
    }

    public TaskCompletionSource<string> PickOf(string key)
    {
        lock (_lock)
        {
            if (!Picks.TryGetValue(key, out var tcs))
                Picks[key] = tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            return tcs;
        }
    }

    public async Task HighlightAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var id = string.Join(",", ids);
        Add("highlight:" + id);
        await HighlightOf(id).Task;
    }

    public Task ClearHighlightAsync(CancellationToken ct) { Add("clear"); return Task.CompletedTask; }
    public Task<string> PickFaceAsync(string occurrenceId, string faceId, CancellationToken ct)
    {
        Add("pick:" + occurrenceId + ":" + faceId);
        return PickOf(occurrenceId + ":" + faceId).Task;
    }

    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<CapabilitiesInfo> GetCapabilitiesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<SceneGraph> GetSceneGraphAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<DefinitionMesh> GetDefinitionMeshAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    public Task<byte[]> GetAssetAsync(DefinitionMesh mesh, CancellationToken ct) => throw new NotSupportedException();
    public Task RunEventsAsync(Action onChanged, CancellationToken ct) => Task.CompletedTask;
}

public class SelectionOrderingTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AStaleHighlightLandingAfterAClearIsCorrectedWithAnotherClear()
    {
        var backend = new ScriptedBackend();
        var service = new SelectionService(backend);
        var first = service.SelectAsync("part", null, "face_a", None);   // Highlight(face_a) is now in flight
        Assert.Equal(new[] { "highlight:face_a" }, backend.Snapshot());

        await service.ClearAsync(None);                                  // the user cleared meanwhile
        Assert.Equal(SelectionKind.None, service.Current.Kind);

        backend.HighlightOf("face_a").SetResult(true);                   // ...and the old highlight lands late
        await first;
        Assert.Equal(new[] { "highlight:face_a", "clear", "clear" }, backend.Snapshot());   // corrective clear is last
    }

    [Fact]
    public async Task AStaleHighlightLandingAfterANewerSelectionIsCorrectedToTheNewOne()
    {
        var backend = new ScriptedBackend();
        var service = new SelectionService(backend);
        var first = service.SelectAsync("part", null, "face_a", None);
        var second = service.SelectAsync("part", null, "face_b", None);   // clear + highlight(face_b), also in flight

        backend.HighlightOf("face_a").SetResult(true);                    // a's answer arrives after b was requested
        backend.HighlightOf("face_b").SetResult(true);
        await first;
        await second;
        Assert.Equal("face_b", service.Current.EntityId);
        var log = backend.Snapshot();
        Assert.Equal("highlight:face_b", log[^1]);                        // the corrective pass re-asserts b last
        Assert.Equal("clear", log[^2]);
    }

    [Fact]
    public async Task ASupersededSelectionBeforeItsPickReturnsNeverHighlights()
    {
        var backend = new ScriptedBackend();
        var service = new SelectionService(backend);
        var first = service.SelectAsync("assembly", "ent_1", "f1", None);   // occurrence selection, highlight in flight
        backend.HighlightOf("ent_1").SetResult(true);
        await first;
        var second = service.SelectAsync("assembly", "ent_1", "f1", None);   // second click -> face, pick pending
        service.ResetLocal();                                                 // scene replaced while the pick is pending
        backend.PickOf("ent_1:f1").SetResult("proxy:ent_1:f1");
        await second;
        Assert.DoesNotContain("highlight:proxy:ent_1:f1", backend.Snapshot());
    }

    [Fact]
    public async Task ChangedReportsTheResolvedEntityIdOncePickReturns()
    {
        var backend = new ScriptedBackend();
        var service = new SelectionService(backend);
        var seen = new List<InventorXrSo.Core.Selection.Selection>();
        service.Changed += seen.Add;
        backend.HighlightOf("ent_1").SetResult(true);
        await service.SelectAsync("assembly", "ent_1", "f3", None);   // occurrence
        backend.PickOf("ent_1:f3").SetResult("proxy:ent_1:f3");
        backend.HighlightOf("proxy:ent_1:f3").SetResult(true);
        await service.SelectAsync("assembly", "ent_1", "f3", None);   // face
        var last = seen[^1];
        Assert.Equal(SelectionKind.Face, last.Kind);
        Assert.Equal("proxy:ent_1:f3", last.EntityId);
        Assert.Same(service.Current, last);
    }
}
