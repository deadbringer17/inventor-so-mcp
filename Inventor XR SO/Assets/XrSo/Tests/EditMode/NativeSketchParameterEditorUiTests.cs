using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public sealed class NativeSketchParameterEditorUiTests
    {
        private sealed class Backend : INativeSketchBackend, IDesignWorkspaceBackend
        {
            public DocumentState State = new DocumentState("doc","r1","v1");
            public int Plans, Commits;
            public TaskCompletionSource<DesignContext> Pending;
            public JArray Parameters = new JArray(
                new JObject {["name"]="Length",["expression"]="10 mm",["unit"]="mm",["value_mm"]=10},
                new JObject {["name"]="Flag",["expression"]="False",["unit"]="Boolean",["value_bool"]=false},
                new JObject {["name"]="Label",["expression"]="\"Quest\"",["unit"]="Text",["value_text"]="Quest"},
                new JObject {["name"]="Missing",["expression"]="Broken",["unit"]="ul"});
            public Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) => Task.FromResult(State);
            public DesignContext Context(DocumentState state) => DesignContext.Parse(new JObject
                {["document_id"]=state.DocumentId,["revision"]=state.Revision,["kind"]="part",["parameters"]=Parameters},state);
            public Task<DesignContext> GetDesignContextAsync(DocumentState state,CancellationToken ct)
                => Pending?.Task ?? Task.FromResult(Context(state));
            public Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state,string id,CancellationToken ct)
                => Task.FromResult(NativeSketchSnapshot.Parse(new JObject
                {
                    ["schema_version"]=1,["document_id"]=state.DocumentId,["revision"]=state.Revision,
                    ["sketch_id"]=id,["sketch_name"]="Parameters",["partial"]=false,["supported_variants"]=new JArray("line"),
                    ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
                    ["entities"]=new JArray(),["dimensions"]=new JArray(),["constraints"]=new JArray()
                },state,id));
            public Task<DesignPreview> PreviewDesignAsync(DocumentState state,JArray operations,CancellationToken ct)
            { ++Plans; throw new NotSupportedException("This input fixture does not claim native preview."); }
            public Task<DocumentState> CommitDesignAsync(DesignPreview preview,CancellationToken ct)
            { ++Commits; throw new NotSupportedException(); }
        }
        private Backend _backend;
        private NativeSketchEditorUi _editor;
        private bool _ready;
        [SetUp] public void Setup()
        {
            _backend=new Backend(); _ready=true;
            _editor=new NativeSketchEditorUi(_backend,null,()=>null,()=>_ready,()=>null,()=>null,()=>{},_=>{},_=>{},()=>{});
            _editor.SetEnvironment(_backend.State,true,true);
            _editor.OpenAsync("ent_sketch").GetAwaiter().GetResult();
            _editor.OpenParametersAsync().GetAwaiter().GetResult();
        }
        [TearDown] public void Cleanup()
        { _editor.Dispose(); Assert.Zero(_backend.Plans); Assert.Zero(_backend.Commits); }
        private XrAction Act(string id) => _editor.Actions().Single(a=>a.Id==id);
        private void Do(string id) => Assert.True(Act(id).TryInvoke(),id);
        private void Pick(string name) => Assert.True(_editor.Actions().Single(a=>a.Id.StartsWith("sketch.parameter.pick.")
            && a.Label.StartsWith(name+" = ")).TryInvoke());
        private void Text(string value)
        {
            var entry=_editor.ActiveParameterTextEntry; Assert.NotNull(entry);
            Assert.True(entry.Replace(value)); Assert.True(entry.Commit(out _));
        }
        private JObject Arguments => (JObject)_editor.Session.Draft[0]["arguments"];

        [Test] public void NativePickerKeepsEveryTypedValueAndUsesExistingCompactPagination()
        {
            for(int i=0;i<23;i++) _backend.Parameters.Add(new JObject {["name"]="Extra"+i,["expression"]="1 ul",["unit"]="ul",["value_unitless"]=1});
            _editor.OpenParametersAsync().GetAwaiter().GetResult();
            var choices=_editor.Actions().Where(a=>a.Id.StartsWith("sketch.parameter.pick.")).ToArray();
            Assert.AreEqual(27,choices.Length);
            StringAssert.Contains("False",choices.Single(a=>a.Label.StartsWith("Flag = ")).Label);
            StringAssert.Contains("\"Quest\"",choices.Single(a=>a.Label.StartsWith("Label = ")).Label);
            StringAssert.Contains("valore non disponibile",choices.Single(a=>a.Label.StartsWith("Missing = ")).Label);
            Assert.AreEqual(PaletteProfile.Compact,SketchRibbon.Tabs.Single(t=>t.Id==NativeSketchEditorUi.ParametersTab).Profile);
        }
        [TestCase("Length","Base * 2 + 3 mm")]
        [TestCase("Flag","True")]
        [TestCase("Label","\"Native & text\"")]
        public void NativeExpressionsPrepareOnlySharedSessionDraft(string name,string value)
        {
            Pick(name); Do("sketch.parameter.expression"); Assert.True(_editor.PromptOpen);
            Assert.False(Act("sketch.editor.finish").Enabled); Text(value);
            Assert.Null(_editor.ActiveParameterTextEntry); Assert.False(_editor.PromptOpen);
            Assert.True(_editor.Session.HasDraft); Assert.True(_editor.Session.CanConfirmDraft);
            Assert.AreEqual("set_parameter",(string)_editor.Session.Draft[0]["command"]);
            Assert.AreEqual(name,(string)Arguments["name"]); Assert.AreEqual(value,(string)Arguments["value"]);
            Assert.False(_editor.Session.Mutation.CanApply);
        }
        [Test] public void CreateRequiresAllFieldsAndRenameDeleteUseNativeBuilders()
        {
            Do("sketch.parameter.create"); Text("Unused"); Assert.False(_editor.Session.HasDraft);
            Text("Length / 2"); Assert.False(_editor.Session.HasDraft); Text("mm");
            Assert.AreEqual("create_parameter",(string)_editor.Session.Draft[0]["command"]);
            Assert.AreEqual("mm",(string)Arguments["unit"]);
            _editor.DiscardOperation(); _editor.OpenParametersAsync().GetAwaiter().GetResult();
            Pick("Length"); Do("sketch.parameter.rename"); Text("Width");
            Assert.AreEqual("rename_parameter",(string)_editor.Session.Draft[0]["command"]);
            _editor.DiscardOperation(); _editor.OpenParametersAsync().GetAwaiter().GetResult();
            Pick("Length"); Do("sketch.parameter.delete");
            Assert.AreEqual("delete_parameter",(string)_editor.Session.Draft[0]["command"]);
        }
        [TestCase("offline")][TestCase("revision")][TestCase("refresh")][TestCase("cancel")][TestCase("hidden")]
        public void RevokedEntryAndSelectionCannotPrepareMutation(string interruption)
        {
            Pick("Length"); var oldDelete=Act("sketch.parameter.delete"); Do("sketch.parameter.expression");
            var entry=_editor.ActiveParameterTextEntry; entry.Replace("42 mm");
            switch(interruption)
            {
                case "offline":_editor.SetEnvironment(_backend.State,false,true);break;
                case "revision":_editor.SetEnvironment(new DocumentState("doc","r2","v2"),true,true);break;
                case "refresh":_editor.OpenAsync("ent_sketch").GetAwaiter().GetResult();break;
                case "cancel":_editor.CancelPrompt();break;
                case "hidden":_ready=false;_editor.Render();break;
            }
            Assert.False(entry.Commit(out _)); Assert.False(oldDelete.TryInvoke());
            Assert.False(_editor.Session.HasDraft); Assert.Null(_editor.ActiveParameterTextEntry);
        }
        [Test] public async Task LateParameterReadCannotReplaceNewDocumentContext()
        {
            _backend.Pending=new TaskCompletionSource<DesignContext>();
            var first=_backend.State; var request=_editor.OpenParametersAsync();
            _editor.SetEnvironment(new DocumentState("other","r2","v2"),true,true);
            _backend.Pending.SetResult(_backend.Context(first)); await request;
            Assert.False(_editor.Actions().Any(a=>a.Id.StartsWith("sketch.parameter.pick.")));
            Assert.False(_editor.Session.HasDraft);
        }
        [Test] public void MultilineExpressionRequiresExplicitReplacementAndCancelCreatesNoDraft()
        {
            _backend.Parameters[0]["expression"]="Length\n+ 2 mm";
            _editor.OpenParametersAsync().GetAwaiter().GetResult(); Pick("Length"); Do("sketch.parameter.expression");
            Assert.AreEqual("",_editor.ActiveParameterTextEntry.Value);
            Assert.False(_editor.ActiveParameterTextEntry.Commit(out _));
            _editor.CancelPrompt(); Assert.False(_editor.Session.HasDraft);
        }
    }
}
