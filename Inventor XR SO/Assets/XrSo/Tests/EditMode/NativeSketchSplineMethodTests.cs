using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public partial class DesignWorkspaceTests
    {
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

