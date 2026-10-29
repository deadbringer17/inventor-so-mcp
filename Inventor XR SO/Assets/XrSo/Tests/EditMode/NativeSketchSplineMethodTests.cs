using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public partial class DesignWorkspaceTests
    {
        [TestCase(false)][TestCase(true)]
        public void SplinePointEditUsesTypedPointAndCurveThenExplicitPreview(bool pen)
        {
            _backend.NativeCurves=true;_backend.NativeOverlap=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.filter.point");editor.Pick(new CadPoint(20,0));
            Do("sketch.selection.filter.all");editor.Pick(new CadPoint(210,5),true);
            Do("sketch.spline.point");Assert.AreEqual(20,_workspace.ActiveEntry.Value);
            if(pen) editor.Pick(new CadPoint(33,-7));
            else {_workspace.ActiveEntry.CommitValue(33,out _);_workspace.ActiveEntry.CommitValue(-7,out _);}
            Assert.True(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual("ent_spline",(string)options["references"][0]["entity_id"]);
            Assert.AreEqual("ent_point",(string)options["references"][1]["entity_id"]);
            Assert.AreEqual(33,(double)options["position_mm"][0]);Assert.AreEqual(-7,(double)options["position_mm"][1]);
            Assert.Zero(_backend.Commits);
        }

        [TestCase(false, false)][TestCase(true, false)][TestCase(false, true)][TestCase(true, true)]
        public void SplinePointInputCannotSurviveCancellationOrRevision(bool revision, bool afterX)
        {
            _backend.NativeCurves=true;_backend.NativeOverlap=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.filter.point");editor.Pick(new CadPoint(20,0));
            Do("sketch.selection.filter.all");editor.Pick(new CadPoint(210,5),true);
            var action=Act("sketch.spline.point");Do("sketch.spline.point");
            if(afterX) _workspace.ActiveEntry.CommitValue(33,out _);
            var entry=_workspace.ActiveEntry;
            if(revision) {_backend.State=new DocumentState(_backend.State.DocumentId,"r_point_next","v_point_next");_workspace.SetDocumentState(_backend.State);}
            else editor.CancelPrompt();
            entry.CommitValue(99,out _);Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Assert.False(editor.CoordinatePickActive);
            if(revision) Assert.False(action.TryInvoke());
        }
        [Test]
        public void SplinePointDraftRequiresBothFreshReferencesAfterRefresh()
        {
            _backend.NativeCurves=true;_backend.NativeOverlap=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.filter.point");editor.Pick(new CadPoint(20,0));
            Do("sketch.selection.filter.all");editor.Pick(new CadPoint(210,5),true);
            Do("sketch.spline.point");editor.Pick(new CadPoint(33,-7));Do(CommitIds.Preview);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_point_next","v_point_next");_workspace.SetDocumentState(_backend.State);
            var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);_view.Show(scene);_workspace.SetScene(scene);Do("sketch.editor.refresh");
            Assert.False(Enabled(CommitIds.Preview));Assert.False(Enabled("sketch.editor.confirm"));
            editor.Pick(new CadPoint(210,5));Assert.False(Enabled("sketch.editor.reselect.confirm"));
            Do("sketch.selection.filter.point");editor.Pick(new CadPoint(20,0),true);
            Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(33,(double)options["position_mm"][0]);Assert.AreEqual(-7,(double)options["position_mm"][1]);
            Assert.AreEqual("r_point_next",(string)options["references"][1]["revision"]);Assert.Zero(_backend.Commits);
        }

        [TestCase(0)][TestCase(75)][TestCase(100)]
        public void SplineTensionPreparesAnIntegerDraftAndRequiresExplicitPreview(int tension)
        {
            _backend.NativeCurves=true;_backend.NativeTension=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");Assert.True(editor.Pick(new CadPoint(210,5)));
            Do("sketch.spline.tension");var entry=_workspace.ActiveEntry;
            Assert.AreEqual(50,entry.Value);Assert.True(entry.IntegerOnly);
            Assert.False(entry.CommitValue(50.5,out _));Assert.True(entry.Editing);Assert.False(editor.Session.HasDraft);
            Assert.True(entry.CommitValue(tension,out _));Assert.True(editor.Session.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual(NativeSketchSplineTensionRules.Variant,(string)args["variant"]);
            Assert.AreEqual(JTokenType.Integer,args["options"]["tension"].Type);Assert.AreEqual(tension,(int)args["options"]["tension"]);
            Assert.AreEqual("ent_spline",(string)args["options"]["references"][0]["entity_id"]);Assert.Zero(_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void SplineTensionInputIsRevokedByCancellationOrRevision(bool revision)
        {
            _backend.NativeCurves=true;_backend.NativeTension=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(210,5));
            var action=Act("sketch.spline.tension");Do("sketch.spline.tension");var entry=_workspace.ActiveEntry;
            if(revision) {_backend.State=new DocumentState(_backend.State.DocumentId,"r_tension_next","v_tension_next");_workspace.SetDocumentState(_backend.State);}
            else editor.CancelPrompt();
            entry.CommitValue(75,out _);Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            if(revision) Assert.False(action.TryInvoke());
        }
        [Test]
        public void SplineTensionDraftPreservesValueButNeedsFreshReferences()
        {
            _backend.NativeCurves=true;_backend.NativeTension=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(210,5));Do("sketch.spline.tension");
            _workspace.ActiveEntry.CommitValue(75,out _);Do(CommitIds.Preview);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_tension_next","v_tension_next");_workspace.SetDocumentState(_backend.State);
            var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);_view.Show(scene);_workspace.SetScene(scene);Do("sketch.editor.refresh");
            Assert.False(Enabled(CommitIds.Preview));Assert.False(Enabled("sketch.editor.confirm"));
            editor.Pick(new CadPoint(210,5));Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            Assert.AreEqual(75,(int)_backend.LastOperations[0]["arguments"]["options"]["tension"]);
            Assert.AreEqual("r_tension_next",(string)_backend.LastOperations[0]["arguments"]["options"]["references"][0]["revision"]);
            Assert.Zero(_backend.Commits);
        }
        [Test]
        public void SplineTensionRejectsWrongOrMultipleSelectionsAndMissingCapability()
        {
            _backend.NativeCurves=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(210,5));
            Assert.False(Enabled("sketch.spline.tension"));_backend.NativeTension=true;Do("sketch.editor.refresh");editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Pick(new CadPoint(10,0));Assert.False(Enabled("sketch.spline.tension"));
            editor.Pick(new CadPoint(210,5),true);Assert.False(Enabled("sketch.spline.tension"));
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }

        [TestCase(false, false)][TestCase(false, true)][TestCase(true, false)][TestCase(true, true)]
        public void SplineClosurePreservesPointsAndFreezesTheDraftAcrossRefresh(bool control,bool closed)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(control ? "SketchCPSplineCmd" : "SketchSplineCmd"));
            Assert.False(Act("sketch.spline.closed").IsOn);
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(20,10));
            if(closed) Do("sketch.spline.closed");
            Do("sketch.spline.numeric");Assert.False(Act("sketch.spline.closed").TryInvoke());
            Assert.True(_workspace.ActiveEntry.CommitValue(40,out _));
            Assert.True(_workspace.ActiveEntry.CommitValue(0,out _));
            Assert.AreEqual(closed,Act("sketch.spline.closed").IsOn);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(closed,(bool)options["closed"]);
            Assert.AreEqual(3,((JArray)options["points_mm"]).Count);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_closure_next","v_closure_next");
            _workspace.SetDocumentState(_backend.State);
            var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);
            _view.Show(scene);_workspace.SetScene(scene);Do("sketch.editor.refresh");
            Assert.False(Enabled(CommitIds.Preview));Do("sketch.editor.confirm");Do(CommitIds.Preview);
            Assert.True(JToken.DeepEquals(options,_backend.LastOperations[0]["arguments"]["options"]));
            Assert.Zero(_backend.Commits);
        }

        [TestCase(false)][TestCase(true)]
        public void SplineClosureCancellationAndRevisionRevokeTheOldAction(bool revision)
        {
            OpenNative();Do("sketch.native.SketchSplineCmd");Do("sketch.spline.closed");
            var old=Act("sketch.spline.closed");
            if(revision)
            {
                _backend.State=new DocumentState(_backend.State.DocumentId,"r_closure_changed","v_closure_changed");
                _workspace.SetDocumentState(_backend.State);
            }
            else Do("sketch.spline.cancel");
            Assert.False(old.TryInvoke());Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            if(!revision)
            {
                Do("sketch.native.SketchCPSplineCmd");Assert.False(Act("sketch.spline.closed").IsOn);
            }
        }

        [Test]
        public void ExplicitlyOpenControlSplineWithRepeatedFirstPointKeepsInputUntilCorrected()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchCPSplineCmd");
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(20,10));editor.Pick(new CadPoint(0,0));
            Do("sketch.spline.finish");Assert.True(editor.PromptOpen);Assert.False(editor.Session.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.closed");Do("sketch.spline.finish");Do(CommitIds.Preview);
            Assert.True((bool)_backend.LastOperations[0]["arguments"]["options"]["closed"]);
            Assert.AreEqual(3,((JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"]).Count);
        }

        [TestCase(26369)][TestCase(26370)][TestCase(26371)]
        public void SplineMethodStartsFromTheCurrentNativePreference(int method)
        {
            _backend.NativeSplineFitMethod=method;OpenNative();
            Do("sketch.native.SketchSplineCmd");
            foreach(int candidate in new[] {26369,26370,26371})
                Assert.AreEqual(candidate==method,Act("sketch.spline.method."+candidate).IsOn);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }

        [TestCase(26369)][TestCase(26370)][TestCase(26371)]
        public void SplineMethodChoicePreservesInputAndIsFrozenAcrossDraftRefresh(int method)
        {
            _backend.NativeSplineFitMethod=method==26369 ? 26370 : 26369;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchSplineCmd");editor.Pick(new CadPoint(7,9));
            Do("sketch.spline.method."+method);editor.Pick(new CadPoint(23,17));
            Do("sketch.spline.numeric");
            Assert.False(Act("sketch.spline.method."+method).TryInvoke());
            Assert.True(Act("sketch.spline.method."+method).IsOn);
            Assert.True(_workspace.ActiveEntry.CommitValue(31,out _));
            Assert.True(_workspace.ActiveEntry.CommitValue(19,out _));
            Do("sketch.spline.undo");editor.Pick(new CadPoint(40,18));
            Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            Assert.AreEqual(method,(int)_backend.LastOperations[0]["arguments"]["options"]["fit_method"]);
            _backend.NativeSplineFitMethod=method==26371 ? 26369 : 26371;
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_method_next","v_method_next");
            _workspace.SetDocumentState(_backend.State);
            var currentScene=NativeSketchTestData.Scene(_backend.State,_backend.Model);
            _view.Show(currentScene);_workspace.SetScene(currentScene);Do("sketch.editor.refresh");
            Assert.False(Enabled(CommitIds.Preview));Do("sketch.editor.confirm");Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(method,(int)options["fit_method"]);
            var points=(JArray)options["points_mm"];Assert.AreEqual(3,points.Count);
            Assert.AreEqual(7,(double)points[0][0]);Assert.AreEqual(23,(double)points[1][0]);
            Assert.AreEqual(40,(double)points[2][0]);Assert.AreEqual(18,(double)points[2][1]);
            Assert.Zero(_backend.Commits);
        }

        [Test]
        public void SplineMethodCancellationRestartsFromPreferenceAndDoesNotApplyToControlVertices()
        {
            _backend.NativeSplineFitMethod=26370;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchSplineCmd");Do("sketch.spline.method.26371");
            var oldAction=Act("sketch.spline.method.26371");
            Do("sketch.spline.cancel");Assert.False(oldAction.TryInvoke());
            Do("sketch.native.SketchSplineCmd");Assert.True(Act("sketch.spline.method.26370").IsOn);
            Do("sketch.spline.cancel");Do("sketch.native.SketchCPSplineCmd");
            Assert.False(oldAction.TryInvoke());
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(10,10));
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            Assert.Null(_backend.LastOperations[0]["arguments"]["options"]["fit_method"]);
            Assert.Zero(_backend.Commits);
        }

        [Test]
        public void SplineMethodActionCannotChangeInputAfterRevisionInvalidation()
        {
            OpenNative();Do("sketch.native.SketchSplineCmd");
            var oldAction=Act("sketch.spline.method.26370");
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_method_other","v_method_other");
            _workspace.SetDocumentState(_backend.State);
            Assert.False(oldAction.TryInvoke());Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
    }

    public partial class LamieraWorkspaceTests
    {
        [TestCase(false)][TestCase(true)]
        public void SplinePointEditUsesTypedPointAndCurveThenExplicitPreview(bool pen)
        {
            _backend.NativeCurves=true;_backend.NativeOverlap=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.filter.point");editor.Pick(new CadPoint(20,0));
            Do("sketch.selection.filter.all");editor.Pick(new CadPoint(210,5),true);
            Do("sketch.spline.point");Assert.AreEqual(20,_workspace.ActiveEntry.Value);
            if(pen) editor.Pick(new CadPoint(33,-7));
            else {_workspace.ActiveEntry.CommitValue(33,out _);_workspace.ActiveEntry.CommitValue(-7,out _);}
            Assert.True(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual("ent_spline",(string)options["references"][0]["entity_id"]);
            Assert.AreEqual("ent_point",(string)options["references"][1]["entity_id"]);
            Assert.AreEqual(33,(double)options["position_mm"][0]);Assert.AreEqual(-7,(double)options["position_mm"][1]);
            Assert.Zero(_backend.Commits);
        }

        [TestCase(false, false)][TestCase(true, false)][TestCase(false, true)][TestCase(true, true)]
        public void SplinePointInputCannotSurviveCancellationOrRevision(bool revision, bool afterX)
        {
            _backend.NativeCurves=true;_backend.NativeOverlap=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.filter.point");editor.Pick(new CadPoint(20,0));
            Do("sketch.selection.filter.all");editor.Pick(new CadPoint(210,5),true);
            var action=Act("sketch.spline.point");Do("sketch.spline.point");
            if(afterX) _workspace.ActiveEntry.CommitValue(33,out _);
            var entry=_workspace.ActiveEntry;
            if(revision) {_backend.State=new DocumentState(_backend.State.DocumentId,"r_point_next","v_point_next");_workspace.SetDocumentState(_backend.State);}
            else editor.CancelPrompt();
            entry.CommitValue(99,out _);Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Assert.False(editor.CoordinatePickActive);
            if(revision) Assert.False(action.TryInvoke());
        }
        [Test]
        public void SplinePointDraftRequiresBothFreshReferencesAfterRefresh()
        {
            _backend.NativeCurves=true;_backend.NativeOverlap=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.filter.point");editor.Pick(new CadPoint(20,0));
            Do("sketch.selection.filter.all");editor.Pick(new CadPoint(210,5),true);
            Do("sketch.spline.point");editor.Pick(new CadPoint(33,-7));Do(CommitIds.Preview);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_point_next","v_point_next");_workspace.SetDocumentState(_backend.State);
            var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);_view.Show(scene);_workspace.SetScene(scene);Do("sketch.editor.refresh");
            Assert.False(Enabled(CommitIds.Preview));Assert.False(Enabled("sketch.editor.confirm"));
            editor.Pick(new CadPoint(210,5));Assert.False(Enabled("sketch.editor.reselect.confirm"));
            Do("sketch.selection.filter.point");editor.Pick(new CadPoint(20,0),true);
            Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(33,(double)options["position_mm"][0]);Assert.AreEqual(-7,(double)options["position_mm"][1]);
            Assert.AreEqual("r_point_next",(string)options["references"][1]["revision"]);Assert.Zero(_backend.Commits);
        }

        [TestCase(0)][TestCase(75)][TestCase(100)]
        public void SplineTensionPreparesAnIntegerDraftAndRequiresExplicitPreview(int tension)
        {
            _backend.NativeCurves=true;_backend.NativeTension=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");Assert.True(editor.Pick(new CadPoint(210,5)));
            Do("sketch.spline.tension");var entry=_workspace.ActiveEntry;
            Assert.AreEqual(50,entry.Value);Assert.True(entry.IntegerOnly);
            Assert.False(entry.CommitValue(50.5,out _));Assert.True(entry.Editing);Assert.False(editor.Session.HasDraft);
            Assert.True(entry.CommitValue(tension,out _));Assert.True(editor.Session.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual(NativeSketchSplineTensionRules.Variant,(string)args["variant"]);
            Assert.AreEqual(JTokenType.Integer,args["options"]["tension"].Type);Assert.AreEqual(tension,(int)args["options"]["tension"]);
            Assert.AreEqual("ent_spline",(string)args["options"]["references"][0]["entity_id"]);Assert.Zero(_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void SplineTensionInputIsRevokedByCancellationOrRevision(bool revision)
        {
            _backend.NativeCurves=true;_backend.NativeTension=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(210,5));
            var action=Act("sketch.spline.tension");Do("sketch.spline.tension");var entry=_workspace.ActiveEntry;
            if(revision) {_backend.State=new DocumentState(_backend.State.DocumentId,"r_tension_next","v_tension_next");_workspace.SetDocumentState(_backend.State);}
            else editor.CancelPrompt();
            entry.CommitValue(75,out _);Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            if(revision) Assert.False(action.TryInvoke());
        }
        [Test]
        public void SplineTensionDraftPreservesValueButNeedsFreshReferences()
        {
            _backend.NativeCurves=true;_backend.NativeTension=true;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(210,5));Do("sketch.spline.tension");
            _workspace.ActiveEntry.CommitValue(75,out _);Do(CommitIds.Preview);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_tension_next","v_tension_next");_workspace.SetDocumentState(_backend.State);
            var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);_view.Show(scene);_workspace.SetScene(scene);Do("sketch.editor.refresh");
            Assert.False(Enabled(CommitIds.Preview));Assert.False(Enabled("sketch.editor.confirm"));
            editor.Pick(new CadPoint(210,5));Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            Assert.AreEqual(75,(int)_backend.LastOperations[0]["arguments"]["options"]["tension"]);
            Assert.AreEqual("r_tension_next",(string)_backend.LastOperations[0]["arguments"]["options"]["references"][0]["revision"]);
            Assert.Zero(_backend.Commits);
        }
        [Test]
        public void SplineTensionRejectsWrongOrMultipleSelectionsAndMissingCapability()
        {
            _backend.NativeCurves=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(210,5));
            Assert.False(Enabled("sketch.spline.tension"));_backend.NativeTension=true;Do("sketch.editor.refresh");editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Pick(new CadPoint(10,0));Assert.False(Enabled("sketch.spline.tension"));
            editor.Pick(new CadPoint(210,5),true);Assert.False(Enabled("sketch.spline.tension"));
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }

        [TestCase(false, false)][TestCase(false, true)][TestCase(true, false)][TestCase(true, true)]
        public void SplineClosurePreservesPointsAndFreezesTheDraftAcrossRefresh(bool control,bool closed)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(control ? "SketchCPSplineCmd" : "SketchSplineCmd"));
            Assert.False(Act("sketch.spline.closed").IsOn);
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(20,10));
            if(closed) Do("sketch.spline.closed");
            Do("sketch.spline.numeric");Assert.False(Act("sketch.spline.closed").TryInvoke());
            Assert.True(_workspace.ActiveEntry.CommitValue(40,out _));
            Assert.True(_workspace.ActiveEntry.CommitValue(0,out _));
            Assert.AreEqual(closed,Act("sketch.spline.closed").IsOn);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(closed,(bool)options["closed"]);
            Assert.AreEqual(3,((JArray)options["points_mm"]).Count);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_closure_next","v_closure_next");
            _workspace.SetDocumentState(_backend.State);
            var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);
            _view.Show(scene);_workspace.SetScene(scene);Do("sketch.editor.refresh");
            Assert.False(Enabled(CommitIds.Preview));Do("sketch.editor.confirm");Do(CommitIds.Preview);
            Assert.True(JToken.DeepEquals(options,_backend.LastOperations[0]["arguments"]["options"]));
            Assert.Zero(_backend.Commits);
        }

        [TestCase(false)][TestCase(true)]
        public void SplineClosureCancellationAndRevisionRevokeTheOldAction(bool revision)
        {
            OpenNative();Do("sketch.native.SketchSplineCmd");Do("sketch.spline.closed");
            var old=Act("sketch.spline.closed");
            if(revision)
            {
                _backend.State=new DocumentState(_backend.State.DocumentId,"r_closure_changed","v_closure_changed");
                _workspace.SetDocumentState(_backend.State);
            }
            else Do("sketch.spline.cancel");
            Assert.False(old.TryInvoke());Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            if(!revision)
            {
                Do("sketch.native.SketchCPSplineCmd");Assert.False(Act("sketch.spline.closed").IsOn);
            }
        }

        [Test]
        public void ExplicitlyOpenControlSplineWithRepeatedFirstPointKeepsInputUntilCorrected()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchCPSplineCmd");
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(20,10));editor.Pick(new CadPoint(0,0));
            Do("sketch.spline.finish");Assert.True(editor.PromptOpen);Assert.False(editor.Session.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.closed");Do("sketch.spline.finish");Do(CommitIds.Preview);
            Assert.True((bool)_backend.LastOperations[0]["arguments"]["options"]["closed"]);
            Assert.AreEqual(3,((JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"]).Count);
        }


        [TestCase(26369)][TestCase(26370)][TestCase(26371)]
        public void SplineMethodStartsFromTheCurrentNativePreference(int method)
        {
            _backend.NativeSplineFitMethod=method;OpenNative();
            Do("sketch.native.SketchSplineCmd");
            foreach(int candidate in new[] {26369,26370,26371})
                Assert.AreEqual(candidate==method,Act("sketch.spline.method."+candidate).IsOn);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }

        [TestCase(26369)][TestCase(26370)][TestCase(26371)]
        public void SplineMethodChoicePreservesInputAndIsFrozenAcrossDraftRefresh(int method)
        {
            _backend.NativeSplineFitMethod=method==26369 ? 26370 : 26369;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchSplineCmd");editor.Pick(new CadPoint(7,9));
            Do("sketch.spline.method."+method);editor.Pick(new CadPoint(23,17));
            Do("sketch.spline.numeric");
            Assert.False(Act("sketch.spline.method."+method).TryInvoke());
            Assert.True(Act("sketch.spline.method."+method).IsOn);
            Assert.True(_workspace.ActiveEntry.CommitValue(31,out _));
            Assert.True(_workspace.ActiveEntry.CommitValue(19,out _));
            Do("sketch.spline.undo");editor.Pick(new CadPoint(40,18));
            Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            Assert.AreEqual(method,(int)_backend.LastOperations[0]["arguments"]["options"]["fit_method"]);
            _backend.NativeSplineFitMethod=method==26371 ? 26369 : 26371;
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_method_next","v_method_next");
            _workspace.SetDocumentState(_backend.State);
            var currentScene=NativeSketchTestData.Scene(_backend.State,_backend.Model);
            _view.Show(currentScene);_workspace.SetScene(currentScene);Do("sketch.editor.refresh");
            Assert.False(Enabled(CommitIds.Preview));Do("sketch.editor.confirm");Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(method,(int)options["fit_method"]);
            var points=(JArray)options["points_mm"];Assert.AreEqual(3,points.Count);
            Assert.AreEqual(7,(double)points[0][0]);Assert.AreEqual(23,(double)points[1][0]);
            Assert.AreEqual(40,(double)points[2][0]);Assert.AreEqual(18,(double)points[2][1]);
            Assert.Zero(_backend.Commits);
        }

        [Test]
        public void SplineMethodCancellationRestartsFromPreferenceAndDoesNotApplyToControlVertices()
        {
            _backend.NativeSplineFitMethod=26370;OpenNative();
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchSplineCmd");Do("sketch.spline.method.26371");
            var oldAction=Act("sketch.spline.method.26371");
            Do("sketch.spline.cancel");Assert.False(oldAction.TryInvoke());
            Do("sketch.native.SketchSplineCmd");Assert.True(Act("sketch.spline.method.26370").IsOn);
            Do("sketch.spline.cancel");Do("sketch.native.SketchCPSplineCmd");
            Assert.False(oldAction.TryInvoke());
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(10,10));
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            Assert.Null(_backend.LastOperations[0]["arguments"]["options"]["fit_method"]);
            Assert.Zero(_backend.Commits);
        }

        [Test]
        public void SplineMethodActionCannotChangeInputAfterRevisionInvalidation()
        {
            OpenNative();Do("sketch.native.SketchSplineCmd");
            var oldAction=Act("sketch.spline.method.26370");
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_method_other","v_method_other");
            _workspace.SetDocumentState(_backend.State);
            Assert.False(oldAction.TryInvoke());Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
    }
}

