using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    internal static class NativeConstraintVisibilityFixture
    {
        internal const string Ground = "sketch.constraint.filter.kGroundConstraintObject";
        internal const string Parallel = "sketch.constraint.filter.kParallelConstraintObject";
        private const BindingFlags Flags = BindingFlags.Instance|BindingFlags.NonPublic;
        private sealed class Backend : INativeSketchBackend
        {
            private readonly IDesignBackend _original;
            private readonly int _count;
            private readonly bool _missing;
            internal Backend(IDesignBackend original,int count,bool missing) { _original=original;_count=count;_missing=missing; }
            public Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state,string id,CancellationToken ct)
                => Task.FromResult(Snapshot(state,id,_count,_missing));
            public Task<DesignPreview> PreviewDesignAsync(DocumentState state,JArray operations,CancellationToken ct)
                => _original.PreviewDesignAsync(state,operations,ct);
            public Task<DocumentState> CommitDesignAsync(DesignPreview preview,CancellationToken ct) => _original.CommitDesignAsync(preview,ct);
        }
        private static NativeSketchSnapshot Snapshot(DocumentState state,string id,int count,bool missing)
        {
            var original=NativeSketchTestData.Snapshot(state,id);
            var constraints=new JArray();
            for(int i=0;i<count;i++) constraints.Add(new JObject
            {
                ["id"]="ent_v"+i.ToString("D3"),["type"]="constraint",["supported"]=true,["deletable"]=true,
                ["native_type"]=i==count-1 ? "kParallelConstraintObject" : "kGroundConstraintObject",
                ["annotation_mm"]=new JArray(0,0),["binding_state"]="unavailable",["binding_reason"]="Fixture association not available."
            });
            if(missing) constraints.Add(new JObject {["id"]="ent_missing",["type"]="constraint",["supported"]=true,
                ["native_type"]="kGroundConstraintObject",["binding_state"]="unavailable"});
            return NativeSketchSnapshot.Parse(new JObject
            {
                ["schema_version"]=1,["document_id"]=state.DocumentId,["revision"]=state.Revision,["sketch_id"]=id,["sketch_name"]="Existing",
                ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
                ["entities"]=new JArray(original.Entities.Select(e=>e.Data)),["dimensions"]=new JArray(),["constraints"]=constraints,
                ["supported_variants"]=new JArray(original.SupportedVariants),["partial"]=false
            },state,id);
        }
        // Replace the test fixture's editor, not a private snapshot or a CAD object. All input/actions remain product code.
        internal static NativeSketchEditorUi Install(MonoBehaviour workspace,IDesignBackend original,int count,bool missing=false)
        {
            var type=workspace.GetType();var editorField=type.GetField("_nativeEditor",Flags);
            var old=(NativeSketchEditorUi)editorField.GetValue(workspace);
            var state=old.Session.Snapshot.State;string id=old.Session.SketchId;var mutation=old.Session.Mutation;
            var geometry=(DesignGeometryView)type.GetField("_geometry",Flags).GetValue(workspace);
            var shell=(UiShell)type.GetField("_shell",Flags).GetValue(workspace);
            var head=(Transform)type.GetField("_head",Flags).GetValue(workspace);
            var refresh=type.GetMethod("Refresh",Flags);
            old.Dispose();
            var editor=new NativeSketchEditorUi(new Backend(original,count,missing),mutation,()=>shell.Palette,
                ()=>mutation.CanEdit && (bool)type.GetProperty("Active").GetValue(workspace),()=>geometry,()=>head,
                ()=>refresh.Invoke(workspace,null),_=>{},_=>{},()=>{});
            editorField.SetValue(workspace,editor);editor.SetEnvironment(state,true,true);editor.OpenAsync(id).GetAwaiter().GetResult();
            return editor;
        }
        internal static NativeSketchConstraintLayer Layer(DesignGeometryView geometry)
        { var layer=geometry.GetComponentInChildren<NativeSketchConstraintLayer>();Assert.NotNull(layer);return layer; }
        internal static void Filters(NativeSketchEditorUi editor,DesignGeometryView geometry,Action<string> invoke,Func<string,XrAction> action)
        {
            var layer=Layer(geometry);Assert.AreEqual(3,layer.VisibleCount);
            invoke("sketch.constraint.filters");
            Assert.AreEqual(NativeSketchEditorUi.ConstraintVisibilityTab,action(Ground).Tab);
            Assert.True(action(Ground).IsOn);Assert.True(action(Parallel).IsOn);Assert.True(action("sketch.constraint.filter.all").IsOn);
            invoke(Ground);Assert.False(action(Ground).IsOn);Assert.True(action(Parallel).IsOn);Assert.AreEqual(1,layer.VisibleCount);
            invoke(Parallel);Assert.Zero(layer.VisibleCount);Assert.False(action("sketch.constraint.filter.all").IsOn);
            invoke(Ground);Assert.AreEqual(2,layer.VisibleCount);Assert.True(action(Ground).IsOn);
            invoke("sketch.constraint.filter.all");Assert.AreEqual(3,layer.VisibleCount);Assert.True(action("sketch.constraint.filter.all").IsOn);
            invoke("sketch.constraint.pick.ent_v000");invoke(Ground);
            Assert.True(action("sketch.constraint.pick.ent_v000").IsOn);
            Assert.AreEqual(2,layer.VisibleCount);Assert.AreEqual(1,layer.Layout.Entries.Count(e=>e.State==NativeSketchConstraintGlyphState.HiddenByFilter));
            Assert.True(layer.TryGetLabel("ent_v000",out var label));Assert.True(label.IsUsable);
            Assert.False(editor.Session.HasDraft);
        }
        internal static XrAction[] Candidates(Func<XrAction[]> actions) => actions().Where(a=>a.Id.StartsWith("sketch.constraint.candidate.",StringComparison.Ordinal)).ToArray();
        internal static void Overlap(NativeSketchEditorUi editor,DesignGeometryView geometry,Transform hand,Action pen,
            Action<string> invoke,Func<string,XrAction> action,Func<XrAction[]> actions,Action revision)
        {
            void AimFirst() { Assert.True(Layer(geometry).TryGetLabel("ent_v000",out var label));NativeConstraintInputAim.Aim(hand,label); }
            AimFirst();pen();var candidates=Candidates(actions);Assert.AreEqual(2,candidates.Length);
            var expiredFilterCandidate=candidates[0];invoke(Ground);
            Assert.IsEmpty(Candidates(actions));Assert.False(expiredFilterCandidate.Enabled);Assert.False(expiredFilterCandidate.TryInvoke());
            invoke("sketch.constraint.filter.all");AimFirst();pen();candidates=Candidates(actions);Assert.AreEqual(2,candidates.Length);
            Assert.True(candidates[1].TryInvoke());Assert.True(action("sketch.constraint.pick.ent_v033").IsOn);
            // Drop the selected symbol via the normal geometry-pick path, then get another overlap choice.
            editor.Pick(new CadPoint(100000,100000));AimFirst();pen();candidates=Candidates(actions);Assert.AreEqual(2,candidates.Length);
            var expiredRevisionCandidate=candidates[0];var expiredFilterAction=action(Ground);
            revision();
            Assert.False(expiredRevisionCandidate.Enabled);Assert.False(expiredRevisionCandidate.TryInvoke());
            Assert.False(expiredFilterAction.Enabled);Assert.False(expiredFilterAction.TryInvoke());
            Assert.IsEmpty(Candidates(actions));Assert.False(editor.Session.HasDraft);
        }
        internal static void Summary(NativeSketchEditorUi editor,DesignGeometryView geometry,Action<string> invoke)
        {
            var layer=Layer(geometry);Assert.AreEqual(256,layer.VisibleCount);
            StringAssert.Contains("mostrate 256",editor.ConstraintVisibilitySummary);
            StringAssert.Contains("filtrate 0",editor.ConstraintVisibilitySummary);
            StringAssert.Contains("oltre limite 4",editor.ConstraintVisibilitySummary);
            StringAssert.Contains("senza posizione 1",editor.ConstraintVisibilitySummary);
            StringAssert.Contains("associazioni non disponibili 261",editor.ConstraintVisibilitySummary);
            invoke(Ground);
            StringAssert.Contains("mostrate 1",editor.ConstraintVisibilitySummary);
            StringAssert.Contains("filtrate 260",editor.ConstraintVisibilitySummary);
            StringAssert.Contains("oltre limite 0",editor.ConstraintVisibilitySummary);
            StringAssert.Contains("senza posizione 0",editor.ConstraintVisibilitySummary);
            Assert.False(editor.Session.HasDraft);
        }
    }

    public partial class DesignWorkspaceTests
    {
        [Test] public void ConstraintVisibilityFiltersRoundTripPreservesSelectionWithoutCadMutation()
        {
            OpenNative();var editor=NativeConstraintVisibilityFixture.Install(_workspace,_backend,3);
            NativeConstraintVisibilityFixture.Filters(editor,Field<DesignGeometryView>("_geometry"),Do,Act);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
        [Test] public void ConstraintVisibilityRevokesOverlappingPenCandidatesAndOldFilterActionsAfterRevision()
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=NativeConstraintVisibilityFixture.Install(_workspace,_backend,34);
            _workspace.UiHitOverride=()=>false;
            NativeConstraintVisibilityFixture.Overlap(editor,Field<DesignGeometryView>("_geometry"),Hand,()=>{Frame();PressTrigger();ReleaseTrigger();},Do,Act,
                ()=>_workspace.Actions.ToArray(),()=>
                {
                    _backend.State=new DocumentState(_backend.State.DocumentId,"r_visibility_next","v_visibility_next");_workspace.SetDocumentState(_backend.State);
                    var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);_view.Show(scene);_workspace.SetScene(scene);Do("sketch.editor.refresh");
                });
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
        [Test] public void ConstraintVisibilitySummarySeparatesBudgetFilterMissingAnchorAndUnavailableBindings()
        {
            OpenNative();var editor=NativeConstraintVisibilityFixture.Install(_workspace,_backend,260,true);
            NativeConstraintVisibilityFixture.Summary(editor,Field<DesignGeometryView>("_geometry"),Do);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
    }
    public partial class LamieraWorkspaceTests
    {
        [Test] public void ConstraintVisibilityFiltersRoundTripPreservesSelectionWithoutCadMutation()
        {
            OpenNative();var editor=NativeConstraintVisibilityFixture.Install(_workspace,_backend,3);
            NativeConstraintVisibilityFixture.Filters(editor,Field<DesignGeometryView>("_geometry"),Do,Act);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
        [Test] public void ConstraintVisibilityRevokesOverlappingPenCandidatesAndOldFilterActionsAfterRevision()
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=NativeConstraintVisibilityFixture.Install(_workspace,_backend,34);
            _workspace.UiHitOverride=()=>false;
            NativeConstraintVisibilityFixture.Overlap(editor,Field<DesignGeometryView>("_geometry"),_ray.Origin,()=>{Frame();PressTrigger();ReleaseTrigger();},Do,Act,
                ()=>_workspace.Actions.ToArray(),()=>
                {
                    _backend.State=new DocumentState(_backend.State.DocumentId,"r_visibility_next","v_visibility_next");_workspace.SetDocumentState(_backend.State);
                    var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);_view.Show(scene);_workspace.SetScene(scene);Do("sketch.editor.refresh");
                });
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
        [Test] public void ConstraintVisibilitySummarySeparatesBudgetFilterMissingAnchorAndUnavailableBindings()
        {
            OpenNative();var editor=NativeConstraintVisibilityFixture.Install(_workspace,_backend,260,true);
            NativeConstraintVisibilityFixture.Summary(editor,Field<DesignGeometryView>("_geometry"),Do);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
    }
}
