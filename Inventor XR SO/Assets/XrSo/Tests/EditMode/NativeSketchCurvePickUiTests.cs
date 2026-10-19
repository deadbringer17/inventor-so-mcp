using System;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    internal static class NativeSketchCurvePickAssertions
    {
        internal static void PointerAndToggle(NativeSketchEditorUi editor)
        {
            Assert.True(editor.Pick(new CadPoint(12,0.75)));
            Assert.AreEqual(1,editor.Selected.Count);
            Assert.AreEqual(12,editor.SelectedPickPoints[0].Value.X);
            Assert.AreEqual(0.75,editor.SelectedPickPoints[0].Value.Y);
            Assert.True(editor.Pick(new CadPoint(28,-0.5),true));
            Assert.Zero(editor.Selected.Count);
            Assert.Zero(editor.SelectedPickPoints.Count);
            Assert.True(editor.Pick(new CadPoint(28,-0.5)));
            Assert.AreEqual(28,editor.SelectedPickPoints[0].Value.X);
            Assert.AreEqual(-0.5,editor.SelectedPickPoints[0].Value.Y);
            Assert.Throws<ArgumentException>(()=>editor.FreezeSelectedCurveOptions(new JObject()));
            Assert.False(editor.Session.HasDraft);
        }
        internal static void WindowClearsSolution(NativeSketchEditorUi editor,Action arm)
        {
            Assert.True(editor.Pick(new CadPoint(12,0.75)));
            arm();Assert.True(editor.Pick(new CadPoint(-1,-1)));
            Assert.True(editor.MoveWindow(new CadPoint(41,1),false,true));
            Assert.AreEqual(1,editor.Selected.Count);
            Assert.False(editor.SelectedPickPoints[0].HasValue);
            Assert.Throws<InvalidOperationException>(()=>editor.FreezeSelectedCurveOptions(new JObject()));
            Assert.False(editor.Session.HasDraft);
        }
        internal static void RefreshClearsSolution(NativeSketchEditorUi editor)
        {
            Assert.True(editor.Pick(new CadPoint(12,0.75)));
            editor.OpenAsync(editor.Session.SketchId).GetAwaiter().GetResult();
            Assert.Zero(editor.Selected.Count);
            Assert.Zero(editor.SelectedPickPoints.Count);
            Assert.Throws<InvalidOperationException>(()=>editor.FreezeSelectedCurveOptions(new JObject()));
        }
    }
    public partial class DesignWorkspaceTests
    {
        [Test] public void NativePointerSelectionRetainsOriginalPointAndToggleDropsIt()
        { OpenNative();NativeSketchCurvePickAssertions.PointerAndToggle(Field<NativeSketchEditorUi>("_nativeEditor")); }
        [Test] public void NativeWindowSelectionDoesNotInventCornerSolution()
        { OpenNative();NativeSketchCurvePickAssertions.WindowClearsSolution(Field<NativeSketchEditorUi>("_nativeEditor"),()=>Do("sketch.selection.window")); }
        [Test] public void NativeRefreshRequiresFreshCornerPicks()
        { OpenNative();NativeSketchCurvePickAssertions.RefreshClearsSolution(Field<NativeSketchEditorUi>("_nativeEditor")); }
    }
    public partial class LamieraWorkspaceTests
    {
        [Test] public void NativePointerSelectionRetainsOriginalPointAndToggleDropsIt()
        { OpenNative();NativeSketchCurvePickAssertions.PointerAndToggle(Field<NativeSketchEditorUi>("_nativeEditor")); }
        [Test] public void NativeWindowSelectionDoesNotInventCornerSolution()
        { OpenNative();NativeSketchCurvePickAssertions.WindowClearsSolution(Field<NativeSketchEditorUi>("_nativeEditor"),()=>Do("sketch.selection.window")); }
        [Test] public void NativeRefreshRequiresFreshCornerPicks()
        { OpenNative();NativeSketchCurvePickAssertions.RefreshClearsSolution(Field<NativeSketchEditorUi>("_nativeEditor")); }
    }
}
