using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public sealed class NativeSketchParameterSessionTests
{
    private static readonly DocumentState First = new("doc", "r1", "v1"), Next = new("doc", "r2", "v2");
    private sealed class Backend : INativeSketchBackend
    {
        public int Plans, Commits;
        public JArray Operations;
        public Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state, string id, CancellationToken ct)
            => Task.FromResult(NativeSketchSnapshot.Parse(new JObject
            {
                ["schema_version"]=1, ["document_id"]=state.DocumentId, ["revision"]=state.Revision,
                ["sketch_id"]=id, ["sketch_name"]="Parameters", ["partial"]=false,
                ["supported_variants"]=new JArray("line"),
                ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0), ["x_axis"]=new JArray(1,0,0), ["y_axis"]=new JArray(0,1,0)},
                ["entities"]=new JArray(), ["dimensions"]=new JArray(), ["constraints"]=new JArray()
            }, state, id));
        public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct)
        {
            ++Plans; Operations = (JArray)operations.DeepClone();
            return Task.FromResult(new DesignPreview("plan", state.DocumentId, state.Revision, DateTimeOffset.UtcNow.AddMinutes(2),
                new GlbModel(state.DocumentId, Array.Empty<GlbPrimitive>())));
        }
        public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct) { ++Commits; return Task.FromResult(Next); }
    }
    private static async Task<NativeSketchSession> Open(Backend backend)
    { var session = new NativeSketchSession(backend); session.SetEnvironment(First,true,true); await session.OpenAsync("ent_sketch"); return session; }

    [Theory] [InlineData(0,"set_parameter")] [InlineData(1,"create_parameter")]
    [InlineData(2,"rename_parameter")] [InlineData(3,"delete_parameter")]
    public async Task TypedParameterDraftUsesExistingWireAndRenderedApplyRefresh(int kind,string command)
    {
        var backend=new Backend(); using var session=await Open(backend);
        switch(kind)
        {
            case 0: session.SetParameterExpression("Length","Base * 2 + 3 mm"); break;
            case 1: session.CreateParameter("New","Base / 2","mm"); break;
            case 2: session.RenameParameter("Length","Width"); break;
            case 3: session.DeleteParameter("Unused"); break;
        }
        Assert.True(session.HasDraft); Assert.True(session.CanConfirmDraft);
        Assert.Equal(DesignStatus.Draft,session.Mutation.Status); Assert.Equal(0,backend.Plans); Assert.Equal(0,backend.Commits);
        Assert.Throws<InvalidOperationException>(()=>session.Close());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.ApplyAsync());
        // Public snapshots are clones and cannot change the frozen operation.
        session.Draft[0]["command"]="arbitrary";
        await session.PreviewAsync(); Assert.Equal(command,(string)backend.Operations[0]["command"]);
        Assert.Null(backend.Operations[0]["arguments"]!["sketch_id"]);
        if(kind==0) Assert.Equal("Base * 2 + 3 mm",(string)backend.Operations[0]["arguments"]!["value"]);
        Assert.False(session.Mutation.CanApply);
        session.Mutation.ConfirmRendered("plan"); await session.ApplyAsync();
        Assert.Equal(1,backend.Commits); Assert.False(session.HasDraft); Assert.True(session.NeedsRefresh);
        Assert.Equal("ent_sketch",session.SketchId);
        session.SetEnvironment(Next,true,true); session.AcknowledgeMutationRefresh(Next); await session.OpenAsync("ent_sketch");
        Assert.True(session.CanEdit);
    }
    [Fact] public async Task RevisionChangeAndCheckpointCannotSilentlyRetargetParameterName()
    {
        var backend=new Backend(); using var session=await Open(backend);
        session.SetParameterExpression("Length","42 mm"); var checkpoint=session.Checkpoint();
        using var restored=new NativeSketchSession(backend); restored.Restore(checkpoint);
        restored.SetEnvironment(Next,true,true); await restored.OpenAsync("ent_sketch");
        Assert.True(restored.HasDraft); Assert.False(restored.CanConfirmDraft); Assert.False(restored.NeedsReferenceSelection);
        Assert.Throws<InvalidOperationException>(()=>restored.ConfirmDraftOnCurrentRevision());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>restored.PreviewAsync());
        Assert.Contains("revisione precedente",restored.Error);
        Assert.Equal(0,backend.Plans); Assert.Equal(0,backend.Commits);
        restored.DiscardOperation(); restored.SetParameterExpression("Length","17 mm");
        Assert.True(restored.CanConfirmDraft);
    }
    [Fact] public async Task CancellationAndOfflineCannotMutateOrOverwriteAnotherDraft()
    {
        var backend=new Backend(); using var session=await Open(backend);
        session.CreateParameter("Label","\"A & B\"","Text");
        Assert.Throws<InvalidOperationException>(()=>session.DeleteParameter("Length"));
        session.SetEnvironment(First,false,true); Assert.False(session.CanConfirmDraft);
        Assert.Throws<InvalidOperationException>(()=>session.SetParameterExpression("Length","1 mm"));
        session.DiscardOperation(); Assert.False(session.HasDraft); Assert.Equal(0,backend.Commits); Assert.Equal(0,backend.Plans);
    }
}
