using System;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    internal static class NativeConstraintInputAim
    {
        internal static void Aim(Transform hand,NativeSketchConstraintLabelView label)
        {
            var corners=new Vector3[4];((RectTransform)label.transform).GetWorldCorners(corners);
            var right=corners[3]-corners[0];var up=corners[1]-corners[0];
            var normal=Vector3.Cross(right.normalized,up.normalized).normalized;
            var point=label.transform.position+right*.3f+up*.25f;
            hand.SetPositionAndRotation(point+normal*.3f,Quaternion.LookRotation(-normal));
        }
        internal static NativeSketchConstraintLabelView Label(DesignGeometryView geometry)
        {
            var layer=geometry.GetComponentInChildren<NativeSketchConstraintLayer>();
            Assert.NotNull(layer);Assert.True(layer.TryGetLabel("ent_constraint",out var label));
            Assert.True(label.IsUsable);return label;
        }
    }
    public partial class DesignWorkspaceTests
    {
        [TestCase(false)][TestCase(true)]
        public void ConstraintRectangleIsPickedThroughTheProductPenAndUiHasPriority(bool overUi)
        {
            _backend.NativeConstraints=true;OpenNative();UseInput(Field<UiShell>("_shell"));
            var geometry=Field<DesignGeometryView>("_geometry");
            var label=NativeConstraintInputAim.Label(geometry);
            NativeConstraintInputAim.Aim(Hand,label);Frame();
            _workspace.UiHitOverride=()=>overUi;PressTrigger();ReleaseTrigger();
            Assert.AreEqual(!overUi,Act("sketch.constraint.pick.ent_constraint").IsOn);
            if(overUi) { _workspace.UiHitOverride=()=>false;Frame();PressTrigger();ReleaseTrigger(); }
            Assert.True(Act("sketch.constraint.pick.ent_constraint").IsOn);
            Assert.True(Enabled("sketch.constraint.edit.delete"));
            Assert.AreSame(label,NativeConstraintInputAim.Label(geometry));
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);Assert.False(_workspace.NativeEditor.HasDraft);
        }

        [Test]
        public void HiddenLabelsAndGeometrySubpartFiltersCannotSelectAConstraint()
        {
            _backend.NativeConstraints=true;OpenNative();UseInput(Field<UiShell>("_shell"));
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");var geometry=Field<DesignGeometryView>("_geometry");
            NativeConstraintInputAim.Aim(Hand,NativeConstraintInputAim.Label(geometry));
            var ray=new Ray(Hand.position,Hand.forward);
            Do("sketch.selection.filter.line");Assert.False(editor.PickConstraint(ray));
            Do("sketch.selection.filter.constraint");Do("sketch.selection.part.Start");Assert.False(editor.PickConstraint(ray));
            Do("sketch.selection.part.Entity");Do("sketch.native.SketchShowAllConstraintsCmd");Assert.False(editor.PickConstraint(ray));
            Assert.False(Act("sketch.constraint.pick.ent_constraint").IsOn);
            Do("sketch.native.SketchShowAllConstraintsCmd");Assert.True(editor.PickConstraint(ray));
            Assert.True(Act("sketch.constraint.pick.ent_constraint").IsOn);
            Do("sketch.native.SketchShowAllConstraintsCmd");
            Assert.True(NativeConstraintInputAim.Label(geometry).IsUsable);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }

        [Test]
        public void CoordinatePromptAndWindowSelectionTakePriorityOverConstraintLabels()
        {
            _backend.NativeConstraints=true;OpenNative();UseInput(Field<UiShell>("_shell"));
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");var geometry=Field<DesignGeometryView>("_geometry");
            NativeConstraintInputAim.Aim(Hand,NativeConstraintInputAim.Label(geometry));
            var ray=new Ray(Hand.position,Hand.forward);
            Do("sketch.native.SketchLineCmd");Assert.True(editor.CoordinatePickActive);
            Assert.False(editor.PickConstraint(ray));
            PressTrigger();ReleaseTrigger();Assert.False(Act("sketch.constraint.pick.ent_constraint").IsOn);
            editor.CancelPrompt();
            Do("sketch.selection.window");Assert.True(editor.WindowActive);Assert.False(editor.PickConstraint(ray));
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);Assert.False(editor.Session.HasDraft);
        }

        [Test]
        public void StaleRevisionRejectsDisplayedConstraintAndRefreshUpdatesTheReference()
        {
            _backend.NativeConstraints=true;OpenNative();UseInput(Field<UiShell>("_shell"));
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");var geometry=Field<DesignGeometryView>("_geometry");
            NativeConstraintInputAim.Aim(Hand,NativeConstraintInputAim.Label(geometry));
            var ray=new Ray(Hand.position,Hand.forward);Assert.True(editor.PickConstraint(ray));
            Do("sketch.constraint.edit.delete");
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_constraint_next","v_constraint_next");
            _workspace.SetDocumentState(_backend.State);Assert.False(editor.PickConstraint(ray));
            var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);_view.Show(scene);_workspace.SetScene(scene);
            Do("sketch.editor.refresh");Assert.False(Enabled("sketch.editor.reselect.confirm"));
            NativeConstraintInputAim.Aim(Hand,NativeConstraintInputAim.Label(geometry));
            Frame();PressTrigger();ReleaseTrigger();Assert.True(Enabled("sketch.editor.reselect.confirm"));
            Do("sketch.editor.reselect.confirm");Do(InventorXrSo.Core.Ui.CommitIds.Preview);
            Assert.AreEqual("r_constraint_next",(string)_backend.LastOperations[0]["arguments"]["options"]["references"][0]["revision"]);
            Assert.Zero(_backend.Commits);
        }
    }
    public partial class LamieraWorkspaceTests
    {
        [TestCase(false)][TestCase(true)]
        public void ConstraintRectangleIsPickedThroughTheProductPenAndUiHasPriority(bool overUi)
        {
            _backend.NativeConstraints=true;OpenNative();UseInput(Field<UiShell>("_shell"));
            var geometry=Field<DesignGeometryView>("_geometry");
            var label=NativeConstraintInputAim.Label(geometry);
            NativeConstraintInputAim.Aim(_ray.Origin,label);Frame();
            _workspace.UiHitOverride=()=>overUi;PressTrigger();ReleaseTrigger();
            Assert.AreEqual(!overUi,Act("sketch.constraint.pick.ent_constraint").IsOn);
            if(overUi) { _workspace.UiHitOverride=()=>false;Frame();PressTrigger();ReleaseTrigger(); }
            Assert.True(Act("sketch.constraint.pick.ent_constraint").IsOn);
            Assert.True(Enabled("sketch.constraint.edit.delete"));
            Assert.AreSame(label,NativeConstraintInputAim.Label(geometry));
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);Assert.False(_workspace.NativeEditor.HasDraft);
        }

        [Test]
        public void HiddenLabelsAndGeometrySubpartFiltersCannotSelectAConstraint()
        {
            _backend.NativeConstraints=true;OpenNative();UseInput(Field<UiShell>("_shell"));
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");var geometry=Field<DesignGeometryView>("_geometry");
            NativeConstraintInputAim.Aim(_ray.Origin,NativeConstraintInputAim.Label(geometry));
            var ray=new Ray(_ray.Origin.position,_ray.Origin.forward);
            Do("sketch.selection.filter.line");Assert.False(editor.PickConstraint(ray));
            Do("sketch.selection.filter.constraint");Do("sketch.selection.part.Start");Assert.False(editor.PickConstraint(ray));
            Do("sketch.selection.part.Entity");Do("sketch.native.SketchShowAllConstraintsCmd");Assert.False(editor.PickConstraint(ray));
            Assert.False(Act("sketch.constraint.pick.ent_constraint").IsOn);
            Do("sketch.native.SketchShowAllConstraintsCmd");Assert.True(editor.PickConstraint(ray));
            Assert.True(Act("sketch.constraint.pick.ent_constraint").IsOn);
            Do("sketch.native.SketchShowAllConstraintsCmd");
            Assert.True(NativeConstraintInputAim.Label(geometry).IsUsable);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }

        [Test]
        public void CoordinatePromptAndWindowSelectionTakePriorityOverConstraintLabels()
        {
            _backend.NativeConstraints=true;OpenNative();UseInput(Field<UiShell>("_shell"));
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");var geometry=Field<DesignGeometryView>("_geometry");
            NativeConstraintInputAim.Aim(_ray.Origin,NativeConstraintInputAim.Label(geometry));
            var ray=new Ray(_ray.Origin.position,_ray.Origin.forward);
            Do("sketch.native.SketchLineCmd");Assert.True(editor.CoordinatePickActive);
            Assert.False(editor.PickConstraint(ray));
            PressTrigger();ReleaseTrigger();Assert.False(Act("sketch.constraint.pick.ent_constraint").IsOn);
            editor.CancelPrompt();
            Do("sketch.selection.window");Assert.True(editor.WindowActive);Assert.False(editor.PickConstraint(ray));
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);Assert.False(editor.Session.HasDraft);
        }

        [Test]
        public void StaleRevisionRejectsDisplayedConstraintAndRefreshUpdatesTheReference()
        {
            _backend.NativeConstraints=true;OpenNative();UseInput(Field<UiShell>("_shell"));
            var editor=Field<NativeSketchEditorUi>("_nativeEditor");var geometry=Field<DesignGeometryView>("_geometry");
            NativeConstraintInputAim.Aim(_ray.Origin,NativeConstraintInputAim.Label(geometry));
            var ray=new Ray(_ray.Origin.position,_ray.Origin.forward);Assert.True(editor.PickConstraint(ray));
            Do("sketch.constraint.edit.delete");
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_constraint_next","v_constraint_next");
            _workspace.SetDocumentState(_backend.State);Assert.False(editor.PickConstraint(ray));
            var scene=NativeSketchTestData.Scene(_backend.State,_backend.Model);_view.Show(scene);_workspace.SetScene(scene);
            Do("sketch.editor.refresh");Assert.False(Enabled("sketch.editor.reselect.confirm"));
            NativeConstraintInputAim.Aim(_ray.Origin,NativeConstraintInputAim.Label(geometry));
            Frame();PressTrigger();ReleaseTrigger();Assert.True(Enabled("sketch.editor.reselect.confirm"));
            Do("sketch.editor.reselect.confirm");Do(InventorXrSo.Core.Ui.CommitIds.Preview);
            Assert.AreEqual("r_constraint_next",(string)_backend.LastOperations[0]["arguments"]["options"]["references"][0]["revision"]);
            Assert.Zero(_backend.Commits);
        }
    }
}
