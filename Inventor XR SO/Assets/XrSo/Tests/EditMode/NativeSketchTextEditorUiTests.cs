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
    public sealed class NativeSketchTextEditorUiTests
    {
        private sealed class Backend : INativeSketchBackend
        {
            internal bool Disclose=true;
            internal int Previews,Commits;
            public Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state,string id,CancellationToken ct)
            {
                return Task.FromResult(NativeSketchSnapshot.Parse(new JObject
                {
                    ["schema_version"]=1,["document_id"]=state.DocumentId,["revision"]=state.Revision,["sketch_id"]=id,["sketch_name"]="Text fixture",["partial"]=false,
                    ["supported_variants"]=Disclose ? new JArray("text_create","text_update","text_delete") : new JArray("line"),
                    ["frame"]=new JObject{["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
                    ["entities"]=new JArray(new JObject{["id"]="ent_text",["type"]="text",["supported"]=true,["text"]="Existing parameter value",["text_mm"]=new JArray(10,20),
                        ["text_bounds_mm"]=new JArray(10,20,30,20,30,30,10,30)},
                        new JObject{["id"]="ent_line",["type"]="line",["supported"]=true,["strokes_mm"]=new JArray(100,0,110,0),["start_mm"]=new JArray(100,0),["end_mm"]=new JArray(110,0)}),
                    ["dimensions"]=new JArray(),["constraints"]=new JArray()
                },state,id));
            }
            public Task<DesignPreview> PreviewDesignAsync(DocumentState state,JArray operations,CancellationToken ct)
            {Previews++;throw new NotSupportedException("This UI fixture does not simulate native glyph preview.");}
            public Task<DocumentState> CommitDesignAsync(DesignPreview preview,CancellationToken ct)
            {Commits++;throw new NotSupportedException();}
        }
        private Backend _backend;
        private NativeSketchEditorUi _editor;
        private readonly DocumentState _state=new DocumentState("doc","r1","v1");
        [SetUp] public void Setup()
        {
            _backend=new Backend();
            _editor=new NativeSketchEditorUi(_backend,null,()=>null,()=>true,()=>null,()=>null,()=>{},_=>{},_=>{},()=>{});
            _editor.SetEnvironment(_state,true,true);_editor.OpenAsync("ent_sketch").GetAwaiter().GetResult();
        }
        [TearDown] public void Cleanup()
        {_editor.Dispose();Assert.Zero(_backend.Previews,"UI input must not invoke preview implicitly.");Assert.Zero(_backend.Commits,"UI input must not commit.");}
        private XrAction Action(string id)=>_editor.Actions().Single(a=>a.Id==id);
        private void Do(string id)=>Assert.True(Action(id).TryInvoke(),id);
        private void Text(string action,string value)
        {Do(action);var entry=_editor.ActiveTextEntry;Assert.NotNull(entry);Assert.True(entry.Replace(value));Assert.True(entry.Commit(out _));}
        private void Number(double value)
        {Assert.NotNull(_editor.ActiveEntry);Assert.True(_editor.ActiveEntry.CommitValue(value,out _));}
        private JObject Options=>(JObject)_editor.Session.Draft[0]["arguments"]["options"];

        [Test] public void UndisclosedBackendDoesNotEmitNativeTextActions()
        {
            _backend.Disclose=false;_editor.OpenAsync("ent_sketch").GetAwaiter().GetResult();
            Assert.False(_editor.Actions().Any(a=>a.Id.StartsWith("sketch.text.",StringComparison.Ordinal) || a.Id=="sketch.native.SketchCreateTextCmd"));
        }
        [Test] public void FittedTextKeepsLiteralMarkupAndLocalParameterSeparateUntilExplicitDraft()
        {
            Do("sketch.native.SketchCreateTextCmd");Assert.True(_editor.PromptOpen);Assert.False(_editor.CanCreate);
            Text("sketch.text.literal","<Parameter Name='secret'>&");Text("sketch.text.parameter","Larghezza");Do("sketch.text.newline");
            Assert.False(_editor.Session.HasDraft);Assert.False(Action("sketch.text.finish").Enabled);
            Do("sketch.text.position");Assert.True(_editor.CoordinatePickActive);Assert.True(_editor.Pick(new CadPoint(12,34)));
            Assert.Null(_editor.ActiveEntry);Do("sketch.text.finish");
            Assert.False(_editor.PromptOpen);Assert.False(_editor.CoordinatePickActive);Assert.True(_editor.Session.HasDraft);
            Assert.AreEqual("text_create",(string)_editor.Session.Draft[0]["arguments"]["variant"]);
            var runs=(JArray)Options["content"]["runs"];Assert.AreEqual(1,(int)Options["content"]["schema"]);Assert.AreEqual(3,runs.Count);
            Assert.AreEqual("<Parameter Name='secret'>&",(string)runs[0]["text"]);Assert.AreEqual("Larghezza",(string)runs[1]["parameter_name"]);
            Assert.AreEqual("\n",(string)runs[2]["text"]);Assert.Null(Options["opposite_corner_mm"]);Assert.Null(Options["style_name"]);
            CollectionAssert.AreEqual(new[]{12d,34d},((JArray)Options["position_mm"]).Values<double>());
        }
        [Test] public void RectangleRejectsDegenerateCornersAndAllowsMixedNumericAndPenInput()
        {
            Do("sketch.text.rectangle");Text("sketch.text.literal","Rectangle");Do("sketch.text.position");
            _editor.Pick(new CadPoint(1,2));_editor.Pick(new CadPoint(1,9));Assert.False(Action("sketch.text.finish").Enabled);
            Do("sketch.text.position");Number(1);Number(2);Assert.True(_editor.CoordinatePickActive);_editor.Pick(new CadPoint(30,40));
            Text("sketch.text.style","Native style");Do("sketch.text.finish");
            CollectionAssert.AreEqual(new[]{1d,2d},((JArray)Options["position_mm"]).Values<double>());
            CollectionAssert.AreEqual(new[]{30d,40d},((JArray)Options["opposite_corner_mm"]).Values<double>());
            Assert.AreEqual("Native style",(string)Options["style_name"]);
        }
        [Test] public void DocumentStyleCanBeRestoredWithoutSendingAnInventedName()
        {
            Do("sketch.native.SketchCreateTextCmd");Text("sketch.text.literal","A");Text("sketch.text.style","Native style");
            Do("sketch.text.style.default");Do("sketch.text.position");Number(0);Number(0);Do("sketch.text.finish");Assert.Null(Options["style_name"]);
        }
        [TestCase(false)][TestCase(true)] public void UpdateRetainsWholeVersionedReferenceAndOnlyRequestedChanges(bool moveOnly)
        {
            Assert.True(_editor.Pick(new CadPoint(20,25)));Do("sketch.text.update");Assert.False(Action("sketch.text.finish").Enabled);
            if(moveOnly){Do("sketch.text.position");Number(-5);Number(7);}else Text("sketch.text.parameter","d0");
            Do("sketch.text.finish");Assert.AreEqual("text_update",(string)_editor.Session.Draft[0]["arguments"]["variant"]);
            var reference=Options["references"][0];Assert.AreEqual(6,((JObject)reference).Count);Assert.AreEqual("ent_text",(string)reference["entity_id"]);
            Assert.AreEqual("r1",(string)reference["revision"]);Assert.AreEqual("Entity",(string)reference["subpart"]);
            Assert.AreEqual(moveOnly,Options["content"]==null);Assert.AreEqual(moveOnly,Options["position_mm"]!=null);
            Assert.Null(Options["style_name"]);Assert.Null(Options["opposite_corner_mm"]);
        }
        [Test] public void DeleteRequiresExactlyOneWholeTextAndCreatesOnlyDeleteDraft()
        {
            Assert.False(Action("sketch.text.delete").Enabled);_editor.Pick(new CadPoint(105,0));Assert.False(Action("sketch.text.delete").Enabled);
            _editor.Pick(new CadPoint(20,25),true);Assert.False(Action("sketch.text.delete").Enabled);
            _editor.Pick(new CadPoint(20,25));Do("sketch.text.delete");Assert.AreEqual("text_delete",(string)_editor.Session.Draft[0]["arguments"]["variant"]);
            Assert.AreEqual(1,Options.Count);Assert.AreEqual("ent_text",(string)Options["references"][0]["entity_id"]);
        }
        [Test] public void CancellationRevokesRetainedTextAndNumericCallbacks()
        {
            Do("sketch.native.SketchCreateTextCmd");Do("sketch.text.literal");var oldText=_editor.ActiveTextEntry;
            oldText.Replace("stale");_editor.CancelPrompt();Assert.False(oldText.Commit(out _));Assert.False(_editor.PromptOpen);
            Do("sketch.native.SketchCreateTextCmd");Text("sketch.text.literal","fresh");Do("sketch.text.position");var oldNumber=_editor.ActiveEntry;
            _editor.CancelPrompt();oldNumber.CommitValue(19,out _);Assert.False(_editor.Session.HasDraft);Assert.False(_editor.PromptOpen);
            Assert.True(_editor.CanCreate);
        }
        [TestCase(false)][TestCase(true)] public void ContextChangeRevokesComposerActionsAndFields(bool offline)
        {
            Do("sketch.native.SketchCreateTextCmd");Text("sketch.text.literal","old");var oldAction=Action("sketch.text.position");
            Do("sketch.text.parameter");var oldEntry=_editor.ActiveTextEntry;oldEntry.Replace("d0");
            _editor.SetEnvironment(offline ? _state : new DocumentState("other","r2","v2"),!offline,true);
            Assert.False(oldAction.TryInvoke());Assert.False(oldEntry.Commit(out _));Assert.False(_editor.PromptOpen);Assert.False(_editor.Session.HasDraft);
        }
        [Test] public void RunBudgetAndLiteralBudgetAreEnforcedBeforeDraft()
        {
            Do("sketch.native.SketchCreateTextCmd");for(int i=0;i<64;i++) Do("sketch.text.newline");
            Assert.False(Action("sketch.text.literal").Enabled);Assert.False(Action("sketch.text.parameter").Enabled);Assert.False(Action("sketch.text.newline").Enabled);
            Do("sketch.text.undo");Assert.True(Action("sketch.text.literal").Enabled);Do("sketch.text.cancel");
            Do("sketch.native.SketchCreateTextCmd");Text("sketch.text.literal",new string('x',4096));
            Assert.False(Action("sketch.text.literal").Enabled);Assert.False(Action("sketch.text.newline").Enabled);Assert.True(Action("sketch.text.parameter").Enabled);
            Do("sketch.text.undo");Assert.True(Action("sketch.text.literal").Enabled);Assert.False(_editor.Session.HasDraft);
        }
        [Test] public void InvalidXmlCharacterCannotBecomeStructuredContent()
        {
            Do("sketch.native.SketchCreateTextCmd");Text("sketch.text.literal","\ud800");
            Do("sketch.text.position");_editor.Pick(new CadPoint(0,0));Assert.False(Action("sketch.text.finish").Enabled);
            Text("sketch.text.literal","valid");Do("sketch.text.finish");Assert.AreEqual(1,((JArray)Options["content"]["runs"]).Count);
        }
        [Test] public void RefreshCancelsUnfinishedCompositionWithoutMakingAnExecutableDraft()
        {
            Do("sketch.native.SketchCreateTextCmd");Text("sketch.text.literal","uncommitted");var old=Action("sketch.text.position");
            _editor.OpenAsync("ent_sketch").GetAwaiter().GetResult();Assert.False(old.TryInvoke());Assert.False(_editor.PromptOpen);
            Assert.False(_editor.Session.HasDraft);Assert.True(_editor.CanCreate);
        }
        [Test] public void ReconfirmTextUpdateRequiresOneFreshWholeTextAndPreservesFrozenContent()
        {
            _editor.Pick(new CadPoint(20,25));Do("sketch.text.update");Text("sketch.text.parameter","d0");Do("sketch.text.finish");
            var next=new DocumentState("doc","r2","v2");_editor.SetEnvironment(next,true,true);_editor.OpenAsync("ent_sketch").GetAwaiter().GetResult();
            Assert.True(_editor.Session.NeedsReferenceSelection);Assert.False(Action("sketch.editor.reselect.confirm").Enabled);
            _editor.Pick(new CadPoint(105,0));Assert.False(Action("sketch.editor.reselect.confirm").Enabled);
            _editor.Pick(new CadPoint(20,25));Do("sketch.editor.reselect.confirm");
            Assert.AreEqual("r2",(string)Options["references"][0]["revision"]);Assert.AreEqual("d0",(string)Options["content"]["runs"][0]["parameter_name"]);
        }
    }
}
