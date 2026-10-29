using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Tests
{
    internal static class NativeSketchTestData
    {
        public static IEnumerable<object[]> PenCreations
        {
            get
            {
                foreach(var item in new[] {("SketchLineCmd",2),("SketchMidpointLineWrapperCmd",2),("SketchTwoPointRectangleCmd",2),
                    ("SketchThreePointRectangleCmd",3),("SketchTwoPointCenRectangleCmd",2),("SketchThreePointCenRectangleCmd",3),
                    ("SketchPolygonCmd",2),("SketchCenterPointCircleCmd",1),("SketchHoleCenterPointCmd",1),("SketchThreePointArcCmd",3),
                    ("SketchCenterPointArcCmd",1),("SketchEllipseCmd",1),("SketchSplineCmd",3),("SketchCPSplineCmd",3),
                    ("SketchCentertoCenterSlotCmd",2),("SketchOverallLengthSlotCmd",2),("SketchCenterPointSlotCmd",2),
                    ("SketchThreePointArcSlotCmd",3),("SketchCenterPointArcSlotCmd",2)}) yield return new object[] {item.Item1,item.Item2};
            }
        }
        public static void AimWindow(Transform hand,Transform model,SketchFrame frame,CadPoint point)
        {
            var target=model.TransformPoint(CadCoordinates.ToLocal(frame.ToModel(point)));
            var normal=model.TransformDirection(CadCoordinates.ToLocal(frame.Normal)).normalized;
            hand.SetPositionAndRotation(target+normal*0.3f,Quaternion.LookRotation(-normal));
        }
        public static NativeSketchSnapshot Snapshot(DocumentState state,string id,bool partial=false,bool overlap=false,bool capabilities=true,bool curves=false,bool dimensions=false,bool angular=false,bool constraints=false,bool patterns=false,bool copy=true,int splineFitMethod=26369,int? splineTension=null)
        {
            var json=new JObject
            {
            ["schema_version"]=1,["document_id"]=state.DocumentId,["revision"]=state.Revision,["sketch_id"]=id,
            ["sketch_name"]="Existing",["partial"]=partial,["spline_fit_method"]=splineFitMethod,
            ["supported_variants"]=new JArray("line","line_midpoint","rectangle_two_points","rectangle_three_points","rectangle_center_two_points","rectangle_center_three_points","polygon",
                "spline_point_update","circle_center","point","arc_three_points","arc_center","ellipse","spline_fit","spline_control",
                "slot_center_to_center","slot_overall","slot_center","slot_three_point_arc","slot_center_arc",
                "format_construction","format_centerline","format_hole_center","transform_move","transform_copy","transform_rotate","transform_stretch","offset_distance","offset_point","constraint_ground","constraint_horizontal","constraint_vertical","constraint_coincident","constraint_tangent","constraint_smooth",
                "pattern_rectangular","pattern_circular","pattern_update","pattern_delete",
                "dimension_aligned","dimension_horizontal","dimension_vertical","dimension_radius","dimension_diameter","dimension_tangent_distance","dimension_update","dimension_delete","constraint_delete"),
            ["frame"]=new JObject { ["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0) },
            ["entities"]=new JArray(new JObject { ["type"]="line",["native_type"]="kSketchLineObject",["id"]="ent_line",
                ["supported"]=true,["construction"]=false,["centerline"]=false,["strokes_mm"]=new JArray(0,0,40,0),["start_mm"]=new JArray(0,0),["end_mm"]=new JArray(40,0) }),["dimensions"]=new JArray(),["constraints"]=new JArray()
            };
            if(constraints) ((JArray)json["constraints"]).Add(new JObject {["id"]="ent_constraint",["type"]="constraint",["native_type"]="kGroundConstraintObject",["supported"]=true,["deletable"]=true,
                ["binding_state"]="complete",["annotation_mm"]=new JArray(0,0),["related_entities"]=new JArray(new JObject {["entity_id"]="ent_line",["type"]="line",["subpart"]="Entity"})});
            if(constraints) ((JArray)json["constraints"]).Add(new JObject {["id"]="ent_locked",["type"]="constraint",["native_type"]="kGroundConstraintObject",["supported"]=true,["deletable"]=false});
            if(patterns) ((JArray)json["constraints"]).Add(new JObject {["id"]="ent_pattern",["type"]="constraint",["native_type"]="kPatternConstraintObject",["supported"]=true,["deletable"]=false,
                ["pattern_definition"]=new JObject {["kind"]="circular",["count"]=new JObject {["value"]=4,["expression"]="N_copie"},["angle_deg"]=new JObject {["value"]=360,["expression"]="360 deg"},
                    ["associative"]=true,["fitted"]=true,["natural_axis"]=true,["symmetric"]=false,["element_count"]=4,["suppressed_elements"]=new JArray()}});
            if(patterns) ((JArray)json["entities"]).Add(new JObject {["id"]="ent_yaxis",["type"]="line",["supported"]=true,["strokes_mm"]=new JArray(300,0,300,40)});
            if(overlap) ((JArray)json["entities"]).Add(new JObject { ["type"]="point",["native_type"]="kSketchPointObject",
                ["id"]="ent_point",["supported"]=true,["construction"]=false,["hole_center"]=false,["point_mm"]=new JArray(20,0) });
            if(!copy) ((JArray)json["supported_variants"]).Single(v=>(string)v=="transform_copy").Remove();
            if(!capabilities) json.Remove("supported_variants");
            if(dimensions) ((JArray)json["dimensions"]).Add(new JObject {["id"]="ent_dimension",["type"]="dimension",["supported"]=true,
                ["name"]="d0",["expression"]=angular ? "30 deg" : "10 mm",[angular ? "value_deg" : "value_mm"]=angular ? 30 : 10,["driven"]=false,["text_mm"]=new JArray(5,6)});
            if(curves)
            {
                ((JArray)json["entities"]).Add(new JObject {["id"]="ent_circle",["type"]="circle",["supported"]=true,
                    ["strokes_mm"]=new JArray(100,0,110,10),["center_mm"]=new JArray(100,10)});
                ((JArray)json["entities"]).Add(new JObject {["id"]="ent_spline",["type"]="spline",["supported"]=true,
                    ["strokes_mm"]=new JArray(200,0,220,10),["start_mm"]=new JArray(200,0),["end_mm"]=new JArray(220,10)});
            }
            if(splineTension.HasValue && curves)
            {
                ((JObject)((JArray)json["entities"]).Single(e=>(string)e["type"]=="spline"))["tension"]=splineTension.Value;
                if(capabilities) ((JArray)json["supported_variants"]).Add(NativeSketchSplineTensionRules.Variant);
            }
            return NativeSketchSnapshot.Parse(json,state,id);
        }
        public static LoadedScene Scene(DocumentState state,GlbModel model)
        {
            var graph=SceneGraph.FromJson(new JObject { ["document_id"]=state.DocumentId,["revision"]=state.Revision,
                ["visual_revision"]=state.VisualRevision,["kind"]="part",["definition_document_ids"]=new JArray(state.DocumentId),
                ["root"]=new JObject { ["name"]="Bolt",["definition_kind"]="part",["definition_document_id"]=state.DocumentId,["children"]=new JArray() } });
            return new LoadedScene(graph,new Dictionary<string,GlbModel>{{state.DocumentId,model}},new Dictionary<string,string>{{state.DocumentId,"a_test"}},new List<string>());
        }
    }
    public partial class DesignWorkspaceTests
    {
        [Test] public void NativePreviewSurvivesSameRevisionSceneRefreshButNotARevisionChange()
        {
            OpenNative(); Do("sketch.native.SketchLineCmd"); Commit(0,0,10,10); Do(CommitIds.Preview);
            var preview = _view.GetComponent<DesignPreviewView>();
            var plan = preview.PlanId;
            Assert.True(Enabled(CommitIds.Apply));
            _view.Show(_scene); _workspace.SetScene(_scene);
            Assert.True(_workspace.NativeEditor.HasDraft);
            Assert.True(preview.IsShowing); Assert.AreEqual(plan, preview.PlanId);
            Assert.True(Enabled(CommitIds.Apply), "same-revision scene delivery must not cancel a validated native preview");
            _workspace.SetDocumentState(new DocumentState(_backend.State.DocumentId,"r_new","v_new"));
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(preview.IsShowing);
            Assert.Zero(_backend.Commits);
        }


        [TestCase(false)][TestCase(true)]
        public void SplineFinishesAtTwoPointsAndUndoRestoresTheMinimumGuard(bool control)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(control ? "SketchCPSplineCmd" : "SketchSplineCmd"));
            editor.Pick(new CadPoint(7,9));Assert.False(Enabled("sketch.spline.finish"));
            editor.Pick(new CadPoint(23,17));Assert.True(Enabled("sketch.spline.finish"));
            Do("sketch.spline.undo");Assert.False(Enabled("sketch.spline.finish"));
            Do("sketch.spline.numeric");Assert.True(_workspace.ActiveEntry.CommitValue(31,out _));
            Assert.True(_workspace.ActiveEntry.CommitValue(19,out _));
            Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            var points=(JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"];
            Assert.AreEqual(2,points.Count);Assert.AreEqual(7,(double)points[0][0]);
            Assert.AreEqual(31,(double)points[1][0]);Assert.AreEqual(19,(double)points[1][1]);
        }
        [TestCase(false)][TestCase(true)]
        public void SplineCollectsVariablePointsAndFinishesOnlyExplicitly(bool control)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(control ? "SketchCPSplineCmd" : "SketchSplineCmd"));
            Assert.True(editor.CoordinatePickActive);Assert.False(Enabled("sketch.spline.finish"));
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(10,10));
            Assert.True(Enabled("sketch.spline.finish"));editor.Pick(new CadPoint(20,0));
            Assert.True(Enabled("sketch.spline.finish"));Assert.False(editor.Session.HasDraft);
            Do("sketch.spline.numeric");var old=_workspace.ActiveEntry;
            Assert.True(old.CommitValue(30,out _));Assert.True(_workspace.ActiveEntry.CommitValue(15,out _));
            Assert.Null(_workspace.ActiveEntry);old.CommitValue(999,out _);
            editor.Pick(new CadPoint(40,0));Do("sketch.spline.undo");editor.Pick(new CadPoint(45,5));
            Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.finish");Assert.False(editor.PromptOpen);Assert.False(editor.CoordinatePickActive);
            Assert.True(editor.Session.HasDraft);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];var points=(JArray)args["options"]["points_mm"];
            Assert.AreEqual(control ? "spline_control" : "spline_fit",(string)args["variant"]);
            Assert.AreEqual(5,points.Count);Assert.AreEqual(30,(double)points[3][0]);Assert.AreEqual(15,(double)points[3][1]);
            Assert.AreEqual(45,(double)points[4][0]);
        }
        [TestCase(false)][TestCase(true)]
        public void SplinePointBudgetRequiresExplicitFinishAndCancellationRevokesCallbacks(bool control)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            string command="sketch.native."+(control ? "SketchCPSplineCmd" : "SketchSplineCmd");Do(command);
            Do("sketch.spline.numeric");var stale=_workspace.ActiveEntry;editor.CancelPrompt();stale.CommitValue(10,out _);
            Assert.False(editor.PromptOpen);Assert.False(editor.Session.HasDraft);Do(command);
            for(int i=0;i<65;i++) editor.Pick(new CadPoint(i,10*(i%2)));
            Assert.False(editor.Session.HasDraft);Assert.False(Enabled("sketch.spline.numeric"));
            Do("sketch.spline.undo");Assert.True(Enabled("sketch.spline.numeric"));editor.Pick(new CadPoint(80,5));
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            var points=(JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"];
            Assert.AreEqual(64,points.Count);Assert.AreEqual(80,(double)points[63][0]);
        }
        [TestCase(false)][TestCase(true)]
        public void SplineNumericCancellationAndRevisionChangeRevokeThePendingField(bool revision)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchSplineCmd");
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(10,10));Do("sketch.spline.numeric");
            var stale=_workspace.ActiveEntry;
            if(revision)
            {
                _backend.State=new DocumentState(_backend.State.DocumentId,"r_spline_next","v_spline_next");
                _workspace.SetDocumentState(_backend.State);
                Assert.False(editor.PromptOpen);Assert.False(editor.CoordinatePickActive);
            }
            else Field<UiShell>("_shell").Palette.HideKeypad();
            stale.CommitValue(99,out _);Assert.Null(_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            if(!revision)
            {
                Assert.True(editor.CoordinatePickActive);Assert.True(Enabled("sketch.spline.finish"));
                editor.Pick(new CadPoint(20,0));Do("sketch.spline.finish");Do(CommitIds.Preview);
                Assert.AreEqual(3,((JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"]).Count);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void ThreePointArcCollectsStartEndThroughAndRejectsCollinearPenPoint(bool numericThrough)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchThreePointArcCmd");
            Assert.True(editor.Pick(new CadPoint(10,15)));Assert.True(editor.Pick(new CadPoint(30,15)));
            var field=_workspace.ActiveEntry;editor.Pick(new CadPoint(20,15));editor.Pick(new CadPoint(10,15));
            Assert.AreSame(field,_workspace.ActiveEntry);Assert.True(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            if(numericThrough) {Assert.True(field.CommitValue(20,out _));Assert.True(_workspace.ActiveEntry.CommitValue(25,out _));}
            else
            {NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(20,25));Frame();PressTrigger();ReleaseTrigger();}
            Assert.True(editor.Session.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);field.CommitValue(99,out _);Do(CommitIds.Preview);
            var points=_backend.LastOperations[0]["arguments"]["options"]["points_mm"];
            Assert.AreEqual(10,(double)points[0][0],.0001);Assert.AreEqual(30,(double)points[1][0],.0001);
            Assert.AreEqual(20,(double)points[2][0],.0001);Assert.AreEqual(25,(double)points[2][1],.0001);
        }
        [TestCase(false,90,false)][TestCase(false,-90,false)][TestCase(false,270,false)][TestCase(false,-270,false)]
        [TestCase(true,90,false)][TestCase(true,-90,false)][TestCase(true,270,false)][TestCase(true,-270,false)]
        [TestCase(false,90,true)][TestCase(false,-270,true)][TestCase(true,90,true)][TestCase(true,-270,true)]
        public void ArcSlotPenCollectsNativePointsSweepAndWidth(bool threePoint,double sweep,bool numericWidth)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(threePoint ? "SketchThreePointArcSlotCmd" : "SketchCenterPointArcSlotCmd"));
            var center=new CadPoint(10,15);var start=new CadPoint(30,15);double radians=sweep*System.Math.PI/180;
            CadPoint Point(double angle)=>center+new CadPoint(20*System.Math.Cos(angle),20*System.Math.Sin(angle));
            Assert.True(editor.Pick(threePoint ? start : center));
            Assert.True(editor.Pick(threePoint ? Point(radians) : start));
            if(threePoint) Assert.True(editor.Pick(Point(radians/2)));
            else
            {
                for(int degrees=90;degrees<System.Math.Abs(sweep);degrees+=90)
                {NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,Point(System.Math.Sign(sweep)*degrees*System.Math.PI/180));Frame();}
                NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,Point(radians));
                Frame();PressTrigger();ReleaseTrigger();
            }
            var widthField=_workspace.ActiveEntry;Assert.NotNull(widthField);Assert.True(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            if(numericWidth) Assert.True(widthField.CommitValue(7,out _));
            else
            {
                NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(34,15-3*System.Math.Sign(sweep)));
                Frame();PressTrigger();ReleaseTrigger();
            }
            Assert.True(editor.Session.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);widthField.CommitValue(99,out _);
            Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];var options=args["options"];
            Assert.AreEqual(threePoint ? "slot_three_point_arc" : "slot_center_arc",(string)args["variant"]);
            Assert.AreEqual(numericWidth ? 7 : 10,(double)options["width_mm"],.0001);
            Assert.AreEqual(threePoint ? 30 : 10,(double)options["points_mm"][0][0],.0001);
            if(!threePoint) Assert.AreEqual(sweep,(double)options["sweep_deg"],.0001);
            else Assert.AreEqual(Point(radians/2).Y,(double)options["points_mm"][2][1],.0001);
        }
        [TestCase(false)][TestCase(true)]
        public void ArcSlotWidthRejectsZeroAndUiPressThenRevokesCancelledField(bool threePoint)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(threePoint ? "SketchThreePointArcSlotCmd" : "SketchCenterPointArcSlotCmd"));
            if(threePoint) {editor.Pick(new CadPoint(30,15));editor.Pick(new CadPoint(10,35));editor.Pick(new CadPoint(10+System.Math.Sqrt(200),15+System.Math.Sqrt(200)));}
            else {editor.Pick(new CadPoint(10,15));editor.Pick(new CadPoint(30,15));Assert.True(_workspace.ActiveEntry.CommitValue(90,out _));}
            var field=_workspace.ActiveEntry;Assert.True(editor.CoordinatePickActive);editor.Pick(new CadPoint(30,15));editor.Pick(new CadPoint(-99999,99999));
            Assert.AreSame(field,_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            _workspace.UiHitOverride=()=>true;NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(34,12));
            Frame();PressTrigger();ReleaseTrigger();Assert.AreSame(field,_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            editor.CancelPrompt();field.CommitValue(7,out _);Assert.False(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase("SketchCentertoCenterSlotCmd","slot_center_to_center",0)][TestCase("SketchCentertoCenterSlotCmd","slot_center_to_center",1)][TestCase("SketchCentertoCenterSlotCmd","slot_center_to_center",2)]
        [TestCase("SketchOverallLengthSlotCmd","slot_overall",0)][TestCase("SketchOverallLengthSlotCmd","slot_overall",1)][TestCase("SketchOverallLengthSlotCmd","slot_overall",2)]
        [TestCase("SketchCenterPointSlotCmd","slot_center",0)][TestCase("SketchCenterPointSlotCmd","slot_center",1)][TestCase("SketchCenterPointSlotCmd","slot_center",2)]
        public void StraightSlotWidthUsesTheThirdPenPointOrNumericAlternative(string command,string variant,int mode)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native."+command);
            Assert.True(editor.Pick(new CadPoint(10,15)));Assert.True(editor.Pick(new CadPoint(34,47)));
            var widthField=_workspace.ActiveEntry;Assert.True(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            double expected=mode==2 ? 7 : mode==1 && variant!="slot_overall" ? 10 : 6;
            if(mode==2) Commit(expected);
            else
            {
                var point=mode==0 ? new CadPoint(19.6,32.8) : new CadPoint(34,52);
                NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,point);
                Frame();PressTrigger();ReleaseTrigger();
            }
            Assert.True(editor.Session.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            widthField.CommitValue(99,out _);Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(variant,(string)_backend.LastOperations[0]["arguments"]["variant"]);
            Assert.AreEqual(expected,(double)options["width_mm"],.0001);
            Assert.AreEqual(34,(double)options["points_mm"][1][0],.0001);
            Assert.AreEqual(47,(double)options["points_mm"][1][1],.0001);
        }
        [TestCase("SketchCentertoCenterSlotCmd")][TestCase("SketchOverallLengthSlotCmd")][TestCase("SketchCenterPointSlotCmd")]
        public void StraightSlotInvalidWidthAndUiPressKeepThePromptUntilCancel(string command)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native."+command);
            Assert.True(editor.Pick(new CadPoint(10,15)));Assert.True(editor.Pick(new CadPoint(34,47)));var field=_workspace.ActiveEntry;
            Assert.True(editor.Pick(new CadPoint(22,31)));Assert.True(editor.Pick(new CadPoint(-99999,99999)));
            Assert.AreSame(field,_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            _workspace.UiHitOverride=()=>true;NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(19.6,32.8));
            Frame();PressTrigger();ReleaseTrigger();Assert.AreSame(field,_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            editor.CancelPrompt();field.CommitValue(7,out _);Assert.False(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        private void OpenNative()
        { AttachShell(out _,out _); Do("sketch.editor.open"); Pick("Existing"); Assert.NotNull(_workspace.NativeEditor.Snapshot); }
        [TestCase(90,false)][TestCase(-90,false)][TestCase(270,false)][TestCase(-270,false)][TestCase(90,true)][TestCase(-270,true)]
        public void CenterArcPenFollowsTheSignedPathAndKeepsItsInitialRadius(double sweep,bool numericEnd)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchCenterPointArcCmd");
            Assert.True(editor.Pick(new CadPoint(10,15)));var radiusField=_workspace.ActiveEntry;
            Assert.True(editor.Pick(new CadPoint(20,15)));radiusField.CommitValue(99,out _);var endField=_workspace.ActiveEntry;
            for(int degrees=90;degrees<System.Math.Abs(sweep);degrees+=90)
            {
                double angle=System.Math.Sign(sweep)*degrees*System.Math.PI/180;
                NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10+10*System.Math.Cos(angle),15+10*System.Math.Sin(angle)));Frame();
                Assert.True(editor.CoordinatePickActive);Assert.False(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);
            }
            if(numericEnd) Assert.True(endField.CommitValue(sweep,out _));
            else
            {
                double angle=sweep*System.Math.PI/180;
                NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10+3*System.Math.Cos(angle),15+3*System.Math.Sin(angle)));
                Frame();PressTrigger();ReleaseTrigger();
            }
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);endField.CommitValue(99,out _);Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(10,(double)options["radius_mm"],.0001);Assert.AreEqual(0,(double)options["start_deg"],.0001);Assert.AreEqual(sweep,(double)options["sweep_deg"],.0001);
        }
        [Test]
        public void CenterArcCanUseNumericRadiusAndStartThenAPenEndpoint()
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchCenterPointArcCmd");
            Assert.True(editor.Pick(new CadPoint(10,15)));var radius=_workspace.ActiveEntry;Assert.True(radius.CommitValue(10,out _));
            var start=_workspace.ActiveEntry;Assert.True(start.CommitValue(30,out _));Assert.True(editor.CoordinatePickActive);
            double end=120*System.Math.PI/180;Assert.True(editor.Pick(new CadPoint(10+3*System.Math.Cos(end),15+3*System.Math.Sin(end))));
            Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(10,(double)options["radius_mm"],.0001);Assert.AreEqual(30,(double)options["start_deg"],.0001);Assert.AreEqual(90,(double)options["sweep_deg"],.0001);
        }
        [Test]
        public void CenterArcRejectsInvalidStartAndEndpointWithoutLosingThePrompt()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchCenterPointArcCmd");editor.Pick(new CadPoint(10,15));
            var radius=_workspace.ActiveEntry;editor.Pick(new CadPoint(10,15));editor.Pick(new CadPoint(-99999,-99999));Assert.AreSame(radius,_workspace.ActiveEntry);
            editor.Pick(new CadPoint(20,15));var end=_workspace.ActiveEntry;editor.Pick(new CadPoint(10,15));editor.Pick(new CadPoint(20,15));
            Assert.AreSame(end,_workspace.ActiveEntry);Assert.True(editor.CoordinatePickActive);Assert.False(_workspace.NativeEditor.HasDraft);
            editor.CancelPrompt();end.CommitValue(99,out _);Assert.False(editor.CoordinatePickActive);Assert.False(_workspace.NativeEditor.HasDraft);
        }
        [Test]
        public void CenterArcSkipsMotionOverUiAndResumesTheChosenDirection()
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchCenterPointArcCmd");
            editor.Pick(new CadPoint(10,15));editor.Pick(new CadPoint(20,15));
            NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10,25));Frame();
            _workspace.UiHitOverride=()=>true;NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10,5));Frame();PressTrigger();ReleaseTrigger();
            Assert.False(_workspace.NativeEditor.HasDraft);Assert.True(editor.CoordinatePickActive);
            _workspace.UiHitOverride=()=>false;Frame();PressTrigger();ReleaseTrigger();Do(CommitIds.Preview);
            Assert.AreEqual(270,(double)_backend.LastOperations[0]["arguments"]["options"]["sweep_deg"],.0001);
        }
        [TestCase(false,0)][TestCase(true,0)][TestCase(false,1)][TestCase(true,1)][TestCase(false,2)][TestCase(true,2)]
        public void EllipsePenUsesAPointOnTheCurveAndPreservesAxisOrder(bool secondLarger,int mode)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchEllipseCmd");Assert.True(editor.Pick(new CadPoint(10,15)));
            var firstField=_workspace.ActiveEntry;
            if(mode==1) {Assert.True(editor.Pick(new CadPoint(10,15)));Assert.AreSame(firstField,_workspace.ActiveEntry);Assert.False(_workspace.NativeEditor.HasDraft);}
            Assert.True(editor.Pick(new CadPoint(22,31))); // length 20, unit direction (.6,.8)
            firstField.CommitValue(99,out _);var secondField=_workspace.ActiveEntry;Assert.True(editor.CoordinatePickActive);
            if(mode==1)
            {
                Assert.True(editor.Pick(new CadPoint(16,23))); // on the first axis
                Assert.True(editor.Pick(new CadPoint(40,55))); // beyond first-axis extent
                Assert.AreSame(secondField,_workspace.ActiveEntry);Assert.False(_workspace.NativeEditor.HasDraft);
            }
            double second=secondLarger ? 40 : 10;
            if(mode==2) {Assert.True(secondField.CommitValue(second,out var reason),reason);}
            else
            {
                // Along = 10, across = b*sqrt(3)/2: the click lies on the ellipse.
                double across=second*System.Math.Sqrt(3)/2;
                NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(16-.8*across,23+.6*across));
                Frame();PressTrigger();ReleaseTrigger();
            }
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);secondField.CommitValue(99,out _);
            Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(20,(double)options["major_radius_mm"],.0001);Assert.AreEqual(second,(double)options["minor_radius_mm"],.0001);
            Assert.AreEqual(System.Math.Atan2(.8,.6)*180/System.Math.PI,(double)options["rotation_deg"],.0001);
        }
        [TestCase(false)][TestCase(true)]
        public void CircleRadiusAcceptsPenAndRejectsDegenerateOrExcessiveRadius(bool rejectFirst)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchCenterPointCircleCmd");Assert.True(editor.Pick(new CadPoint(10,15)));
            var radiusField=_workspace.ActiveEntry;Assert.True(editor.CoordinatePickActive);
            if(rejectFirst)
            {
                Assert.True(editor.Pick(new CadPoint(10,15)));Assert.True(editor.Pick(new CadPoint(-99999,-99999)));
                Assert.AreSame(radiusField,_workspace.ActiveEntry);Assert.True(editor.CoordinatePickActive);Assert.False(_workspace.NativeEditor.HasDraft);
            }
            NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(13,19));
            Frame();PressTrigger();ReleaseTrigger();Assert.True(_workspace.NativeEditor.HasDraft);Assert.False(editor.CoordinatePickActive);
            Assert.Null(_workspace.ActiveEntry);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            radiusField.CommitValue(99,out _);Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(5,(double)options["radius_mm"],.0001);Assert.AreEqual(10,(double)options["points_mm"][0][0],.0001);
        }
        [TestCaseSource(typeof(NativeSketchTestData),nameof(NativeSketchTestData.PenCreations))]
        public void NativeCreationAcceptsSyntheticPenPointsAndKeepsScalarOptionsNumeric(string command,int count)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native."+command);
            if(command=="SketchPolygonCmd") Do("sketch.polygon.inscribed");
            for(int i=0;i<count;i++)
            {
                Assert.True(editor.CoordinatePickActive,command+" point "+i);
                NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10+10*i,i%2==0 ? 10 : 15));
                Frame();PressTrigger();ReleaseTrigger();Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            }
            if(command=="SketchSplineCmd" || command=="SketchCPSplineCmd") Do("sketch.spline.finish");
            Assert.AreEqual(command=="SketchCenterPointCircleCmd" || command=="SketchEllipseCmd" || command=="SketchCenterPointArcCmd" || command=="SketchCentertoCenterSlotCmd" || command=="SketchOverallLengthSlotCmd" || command=="SketchCenterPointSlotCmd" || command=="SketchThreePointArcSlotCmd" || command=="SketchCenterPointArcSlotCmd",editor.CoordinatePickActive);while(_workspace.ActiveEntry!=null) {var entry=_workspace.ActiveEntry;Assert.True(entry.CommitValue(entry.Value,out var reason),reason);}
            Assert.True(_workspace.NativeEditor.HasDraft);Do(CommitIds.Preview);var points=(JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"];
            Assert.AreEqual(count,points.Count);for(int i=0;i<count;i++) {Assert.AreEqual(10+10*i,(double)points[i][0],.0001);Assert.AreEqual(i%2==0 ? 10 : 15,(double)points[i][1],.0001);}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void NativePointInputIsRevokedOnCancelRevisionAndDoesNotCaptureUiPresses(int interruption)
        {
            OpenNative();UseInput();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchLineCmd");
            var old=_workspace.ActiveEntry;
            if(interruption==0) _workspace.Back();
            if(interruption==1) {_backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);}
            if(interruption==2) {_workspace.UiHitOverride=()=>true;NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10,15));PressTrigger();ReleaseTrigger();Assert.True(editor.CoordinatePickActive);Assert.AreSame(old,_workspace.ActiveEntry);editor.CancelPrompt();}
            Assert.False(editor.CoordinatePickActive);old.CommitValue(99,out _);Assert.False(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);
        }
        [TestCase(false)][TestCase(true)]
        public void NativePointInputKeepsAcceptedPointsAcrossTrackingLossAndRequiresRelease(bool paletteLost)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchLineCmd");
            NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10,10));PressTrigger();ReleaseTrigger();
            var next=_workspace.ActiveEntry;_workspace.UiHitOverride=()=>true;PressTrigger();
            if(paletteLost) _frame.PaletteTracked=false;else _frame.PenTracked=false;Poll();
            _workspace.UiHitOverride=()=>false;_frame.PaletteTracked=true;_frame.PenTracked=true;Poll();
            Assert.AreSame(next,_workspace.ActiveEntry);Assert.True(editor.CoordinatePickActive);Assert.False(_workspace.NativeEditor.HasDraft);
            ReleaseTrigger();NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(20,15));PressTrigger();ReleaseTrigger();
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);
            var points=_backend.LastOperations[0]["arguments"]["options"]["points_mm"];Assert.AreEqual(10,(double)points[0][0],.0001);Assert.AreEqual(20,(double)points[1][0],.0001);
        }
        [Test]
        public void PenPointReplacesTheEntireCoordinatePairAndCannotReuseAClosedNumericField()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchLineCmd");var old=_workspace.ActiveEntry;Commit(99);
            Assert.True(editor.Pick(new CadPoint(10,15)));old.CommitValue(77,out _);Assert.True(editor.Pick(new CadPoint(20,25)));
            Assert.False(editor.CoordinatePickActive);Do(CommitIds.Preview);var points=_backend.LastOperations[0]["arguments"]["options"]["points_mm"];
            Assert.AreEqual(10,(double)points[0][0]);Assert.AreEqual(15,(double)points[0][1]);Assert.AreEqual(20,(double)points[1][0]);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)]
        public void MidpointLineUsesOnlyTheChosenPointRolesAndPromptsOnlyNumericInputs(int mode)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            if((mode&1)!=0){Do("sketch.selection.part.Start");editor.Pick(new CadPoint(0,0));}
            if((mode&2)!=0){Do("sketch.selection.part.End");editor.Pick(new CadPoint(40,0),mode==3);}
            Do("sketch.line.midpoint."+new[] {"coordinates","midpoint","end","both"}[mode]);Do("sketch.native.SketchMidpointLineWrapperCmd");
            if(mode==0) Commit(5,6,20,8);else if(mode!=3) Commit(10,12);
            Assert.IsNull(_workspace.ActiveEntry);Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("line_midpoint",(string)args["variant"]);
            var options=args["options"];Assert.AreEqual(mode==0 ? 0 : mode==3 ? 2 : 1,(options["references"] as JArray)?.Count??0);
            if(mode==1) {Assert.AreEqual("midpoint",(string)options["reference_roles"][0]);Assert.AreEqual(10,(double)options["points_mm"][1][0]);}
            if(mode==2) {Assert.AreEqual("end",(string)options["reference_roles"][0]);Assert.AreEqual(10,(double)options["points_mm"][0][0]);}
        }
        [Test]
        public void MidpointLineRequiresExplicitReselectionInTheSameRoleOrderAfterRefresh()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.selection.part.Start");editor.Pick(new CadPoint(0,0));
            Do("sketch.selection.part.End");editor.Pick(new CadPoint(40,0),true);Do("sketch.line.midpoint.both");Do("sketch.native.SketchMidpointLineWrapperCmd");
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.editor.reselect.confirm"));Do("sketch.selection.part.Start");editor.Pick(new CadPoint(0,0));
            Do("sketch.selection.part.End");editor.Pick(new CadPoint(40,0),true);Do("sketch.editor.reselect.confirm");
            Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual("midpoint",(string)options["reference_roles"][0]);Assert.AreEqual("end",(string)options["reference_roles"][1]);
            Assert.AreEqual("r_next",(string)options["references"][0]["revision"]);Assert.AreEqual("Start",(string)options["references"][0]["subpart"]);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)]
        public void WindowTriggerDragFinishesOrCancelsThroughSyntheticInput(int interruption)
        {
            OpenNative();UseInput();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.window");
            NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(41,-1));
            PressTrigger();Assert.True(editor.WindowDragging);
            NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(-1,1));
            Frame();Assert.Zero(editor.Selected.Count);
            if(interruption==1) _workspace.UiHitOverride=()=>true;
            if(interruption==2) {_frame.PenTracked=false;Poll();}
            if(interruption==3) {_frame.PaletteTracked=false;Poll();}
            if(interruption==4) _workspace.SetOnline(false);
            ReleaseTrigger();Assert.False(editor.WindowActive);Assert.AreEqual(interruption==0 ? 1 : 0,editor.Selected.Count);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void WindowDirectionUsesViewerRightDespiteCadXReflection(bool crossing)
        {
            OpenNative();UseInput();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.selection.window");
            NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(crossing ? -1 : 1,-1));PressTrigger();
            NativeSketchTestData.AimWindow(Hand,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(crossing ? 1 : -1,1));ReleaseTrigger();
            Assert.False(editor.WindowActive);Assert.AreEqual(crossing ? 1 : 0,editor.Selected.Count);
        }
        [Test]
        public void WindowSelectionIsLocalUntilReleaseAndCanAddOrInvertWithoutCadCalls()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.window");Assert.False(Enabled("sketch.native.SketchLineCmd"));
            editor.Pick(new CadPoint(-1,-1));editor.MoveWindow(new CadPoint(41,1),false);
            Assert.Zero(editor.Selected.Count);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            Assert.True(editor.MoveWindow(new CadPoint(41,1),false,true));Assert.AreEqual(1,editor.Selected.Count);
            Do("sketch.selection.window.Add");Do("sketch.selection.window");editor.Pick(new CadPoint(-1,-1));editor.MoveWindow(new CadPoint(41,1),false,true);
            Assert.AreEqual(1,editor.Selected.Count,"Add must not toggle an existing selection.");
            Do("sketch.selection.window.Toggle");Do("sketch.selection.window");editor.Pick(new CadPoint(-1,-1));editor.MoveWindow(new CadPoint(41,1),false,true);
            Assert.Zero(editor.Selected.Count);Assert.False(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void WindowCancellationAndRevisionChangeNeverApplyThePendingRegion()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));
            Do("sketch.selection.window");editor.Pick(new CadPoint(100,100));_workspace.Back();
            Assert.False(editor.WindowActive);Assert.AreEqual(1,editor.Selected.Count);
            Do("sketch.selection.window");editor.Pick(new CadPoint(-1,-1));
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);
            Assert.False(editor.WindowActive);Assert.False(editor.MoveWindow(new CadPoint(41,1),false,true));Assert.Zero(editor.Selected.Count);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void StretchAcceptsEndpointSelectionAndRequiresExplicitPreview()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.selection.part.End");editor.Pick(new CadPoint(40,0));
            Assert.False(Enabled("sketch.native.SketchMoveCmd"));Assert.True(Enabled("sketch.native.SketchStretchCmd"));
            Do("sketch.native.SketchStretchCmd");Commit(12,-8);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("transform_stretch",(string)args["variant"]);
            Assert.AreEqual("End",(string)args["options"]["references"][0]["subpart"]);Assert.AreEqual(-8,(double)args["options"]["delta_mm"][1]);
        }
        [Test]
        public void StretchReselectionPreservesBothEndpointsOfOneCurveAndNativeOptions()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.part.Start");editor.Pick(new CadPoint(0,0));Do("sketch.selection.part.End");editor.Pick(new CadPoint(40,0),true);
            Do("sketch.transform.remove_constraints");Do("sketch.native.SketchStretchCmd");Commit(12,-8);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.editor.reselect.confirm"));Do("sketch.selection.part.Start");editor.Pick(new CadPoint(0,0));
            Do("sketch.selection.part.End");editor.Pick(new CadPoint(40,0),true);Do("sketch.editor.reselect.confirm");
            Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(2,((JArray)options["references"]).Count);Assert.AreEqual("Start",(string)options["references"][0]["subpart"]);
            Assert.AreEqual("End",(string)options["references"][1]["subpart"]);Assert.AreEqual("r_next",(string)options["references"][1]["revision"]);
            Assert.True((bool)options["remove_constraints"]);Assert.AreEqual(-8,(double)options["delta_mm"][1]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void MissingCopyCapabilityDisablesCopyAndRotationCopyButKeepsMoveAndRotate()
        {
            _backend.NativeNoCopy=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));
            Assert.False(Enabled("sketch.native.SketchCopyCmd"));Assert.False(Enabled("sketch.transform.rotate_copy"));
            Assert.True(Enabled("sketch.native.SketchMoveCmd"));Assert.True(Enabled("sketch.native.SketchRotateCmd"));
            Do("sketch.native.SketchRotateCmd");Commit(5,6,-45);Do(CommitIds.Preview);
            Assert.False((bool)_backend.LastOperations[0]["arguments"]["options"]["copy"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void PreservedRotationCopyCannotBeReselectedAfterCapabilityLoss()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));
            Do("sketch.transform.rotate_copy");Do("sketch.native.SketchRotateCmd");Commit(5,6,-45);
            _backend.NativeNoCopy=true;_backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");
            _workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");editor.Pick(new CadPoint(20,0));
            Assert.False(Enabled("sketch.editor.reselect.confirm"));Assert.False(Enabled(CommitIds.Preview));
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void SketchPatternFreezesOrderedSourcesAndNativeOptions(bool circular)
        {
            _backend.NativeCurves=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Pick(circular ? new CadPoint(20,0) : new CadPoint(105,5));editor.Pick(circular ? new CadPoint(105,5) : new CadPoint(20,0),true);
            Do("sketch.pattern.associative");Do("sketch.pattern.natural");Do("sketch.pattern.symmetric");
            Do(circular ? "sketch.native.SketchCircularPatternCmd" : "sketch.native.SketchRectangularPatternCmd");Commit(4,circular ? 180 : 12);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual(circular ? "pattern_circular" : "pattern_rectangular",(string)args["variant"]);
            var options=args["options"];Assert.False((bool)options["associative"]);Assert.False((bool)options[circular ? "natural_axis" : "natural_x"]);
            Assert.True((bool)options[circular ? "symmetric" : "symmetric_x"]);Assert.AreEqual(circular,(bool)options["fitted"]);
            Assert.AreEqual(circular ? "ent_circle" : "ent_line",(string)options["references"][1]["entity_id"]);
        }
        [Test]
        public void RectangularPatternRequiresTwoDirectionLinesWhenRequested()
        {
            _backend.NativeCurves=true;_backend.NativePatterns=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Pick(new CadPoint(105,5));editor.Pick(new CadPoint(20,0),true);Do("sketch.pattern.two");
            Assert.False(Enabled("sketch.native.SketchRectangularPatternCmd"));editor.Pick(new CadPoint(300,20),true);
            Do("sketch.native.SketchRectangularPatternCmd");Commit(3,12,2,8);Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];Assert.True((bool)options["two_directions"]);Assert.AreEqual(8,(double)options["y_spacing_mm"]);
            Assert.AreEqual("ent_yaxis",(string)options["references"][2]["entity_id"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void RectangularPatternFreezesAllSixteenIndependentDirectionAndSymmetryCombinations()
        {
            _backend.NativeCurves=true;_backend.NativePatterns=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Assert.False(Enabled("sketch.pattern.natural_y"));Assert.False(Enabled("sketch.pattern.symmetric_y"));Do("sketch.pattern.two");
            editor.Pick(new CadPoint(105,5));editor.Pick(new CadPoint(20,0),true);editor.Pick(new CadPoint(300,20),true);
            var names=new[] {"natural","symmetric","natural_y","symmetric_y"};
            for(int mask=0;mask<16;mask++)
            {
                for(int bit=0;bit<4;bit++) if(_workspace.Actions.Single(a=>a.Id=="sketch.pattern."+names[bit]).IsOn!=((mask&(1<<bit))!=0)) Do("sketch.pattern."+names[bit]);
                Do("sketch.native.SketchRectangularPatternCmd");Commit(3,12,2,8);
                Assert.False(Enabled("sketch.pattern.natural_y"));Assert.AreEqual(mask,_backend.Previews);Do(CommitIds.Preview);
                var options=_backend.LastOperations[0]["arguments"]["options"];
                Assert.AreEqual((mask&1)!=0,(bool)options["natural_x"]);Assert.AreEqual((mask&2)!=0,(bool)options["symmetric_x"]);
                Assert.AreEqual((mask&4)!=0,(bool)options["natural_y"]);Assert.AreEqual((mask&8)!=0,(bool)options["symmetric_y"]);
                Assert.AreEqual(0,_backend.Commits);Do(CommitIds.Cancel);
            }
        }
        [Test]
        public void IndependentYOptionsAndSuppressionSurviveExplicitReselectionButDoNotLeakIntoOneDirection()
        {
            _backend.NativeCurves=true;_backend.NativePatterns=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.pattern.two");Do("sketch.pattern.natural_y");Do("sketch.pattern.symmetric_y");Do("sketch.pattern.suppressed");
            Assert.True(editor.ActiveTextEntry.Replace("2, 4"));Assert.True(editor.ActiveTextEntry.Commit(out _));
            Assert.False(_workspace.NativeEditor.HasDraft);editor.Pick(new CadPoint(105,5));editor.Pick(new CadPoint(20,0),true);editor.Pick(new CadPoint(300,20),true);
            Do("sketch.native.SketchRectangularPatternCmd");Commit(3,12,2,8);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            editor.Pick(new CadPoint(105,5));editor.Pick(new CadPoint(20,0),true);editor.Pick(new CadPoint(300,20),true);Do("sketch.editor.reselect.confirm");
            Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.False((bool)options["natural_y"]);Assert.True((bool)options["symmetric_y"]);CollectionAssert.AreEqual(new[] {2,4},((JArray)options["suppressed_elements"]).Values<int>().ToArray());
            Do(CommitIds.Cancel);Do("sketch.pattern.two");Do("sketch.pattern.suppressed.clear");editor.Pick(new CadPoint(105,5));editor.Pick(new CadPoint(20,0),true);
            Do("sketch.native.SketchRectangularPatternCmd");Commit(3,12);Do(CommitIds.Preview);options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.Null(options["natural_y"]);Assert.Null(options["symmetric_y"]);Assert.Null(options["suppressed_elements"]);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase("2,2")][TestCase("0")][TestCase("2001")][TestCase("-2")][TestCase("2.5")][TestCase("due")]
        public void InvalidSuppressionListPreservesPreviousOptionsAndMakesNoCadRequest(string invalid)
        {
            _backend.NativeCurves=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.pattern.suppressed");
            Assert.True(editor.ActiveTextEntry.Replace("2"));Assert.True(editor.ActiveTextEntry.Commit(out _));Do("sketch.pattern.suppressed");
            Assert.True(editor.ActiveTextEntry.Replace(invalid));Assert.True(editor.ActiveTextEntry.Commit(out _));Assert.False(_workspace.NativeEditor.HasDraft);
            editor.Pick(new CadPoint(20,0));editor.Pick(new CadPoint(105,5),true);Do("sketch.native.SketchCircularPatternCmd");Commit(4,180);
            Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);CollectionAssert.AreEqual(new[] {2},((JArray)_backend.LastOperations[0]["arguments"]["options"]["suppressed_elements"]).Values<int>().ToArray());
            Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void PendingSuppressionInputCannotCommitAfterCancelOrRevisionChange(bool revision)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.pattern.suppressed");var entry=editor.ActiveTextEntry;
            Assert.True(entry.Replace("2,4"));
            if(revision){_backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);}
            else _workspace.Back();
            Assert.False(entry.Commit(out _));Assert.Null(editor.ActiveTextEntry);Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void ExistingPatternSuppressionChangesAreDraftsWithExplicitCurrentConstraintReselection(bool clear)
        {
            _backend.NativePatterns=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.constraint.pick.ent_pattern");
            Do(clear ? "sketch.pattern.edit.unsuppress" : "sketch.pattern.edit.suppressed");
            if(!clear){Assert.True(editor.ActiveTextEntry.Replace("2,3"));Assert.True(editor.ActiveTextEntry.Commit(out _));}
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.editor.reselect.confirm"));Do("sketch.constraint.pick.ent_pattern");Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("pattern_update",(string)args["variant"]);
            Assert.AreEqual("ent_pattern",(string)args["options"]["references"][0]["entity_id"]);Assert.AreEqual("r_next",(string)args["options"]["references"][0]["revision"]);
            CollectionAssert.AreEqual(clear ? new int[0] : new[] {2,3},((JArray)args["options"]["suppressed_elements"]).Values<int>().ToArray());
            Assert.Null(args["options"]["count"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativePatternCanBeEditedWithoutDeletingItsManagedConstraint()
        {
            _backend.NativePatterns=true;OpenNative();Do("sketch.constraint.pick.ent_pattern");Assert.False(Enabled("sketch.constraint.edit.delete"));
            Assert.True(Enabled("sketch.pattern.edit.delete"));Do("sketch.pattern.edit.parameters");Commit(6,180);
            Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual("pattern_update",(string)args["variant"]);Assert.AreEqual(6,(double)args["options"]["count"]);
            Assert.Null(args["options"]["associative"]);Assert.AreEqual("ent_pattern",(string)args["options"]["references"][0]["entity_id"]);
        }
        [Test]
        public void FractionalPatternCountCreatesNoDraftOrCadRequest()
        {
            _backend.NativeCurves=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));editor.Pick(new CadPoint(105,5),true);
            Do("sketch.native.SketchCircularPatternCmd");Commit(2.5);Assert.False(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void NativePatternExpressionsAreLiteralRevisionBoundDrafts(bool edit)
        {
            _backend.NativeCurves=true;_backend.NativePatterns=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            if(edit){Do("sketch.constraint.pick.ent_pattern");Do("sketch.pattern.edit.expressions");Assert.AreEqual("N_copie",editor.ActiveTextEntry.Value);}
            else {editor.Pick(new CadPoint(20,0));editor.Pick(new CadPoint(105,5),true);Do("sketch.pattern.expressions");Do("sketch.native.SketchCircularPatternCmd");}
            Assert.Null(editor.ActiveEntry);Assert.True(editor.ActiveTextEntry.Replace("N_copie + 1"));Assert.True(editor.ActiveTextEntry.Commit(out _));
            Assert.False(_workspace.NativeEditor.HasDraft);Assert.True(editor.ActiveTextEntry.Replace("Angolo_base / 2"));Assert.True(editor.ActiveTextEntry.Commit(out _));
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            if(edit) Do("sketch.constraint.pick.ent_pattern");else {editor.Pick(new CadPoint(20,0));editor.Pick(new CadPoint(105,5),true);}
            Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual(edit ? "pattern_update" : "pattern_circular",(string)args["variant"]);
            Assert.AreEqual("N_copie + 1",(string)args["options"]["count"]);Assert.AreEqual("Angolo_base / 2",(string)args["options"]["angle_deg"]);
            Assert.AreEqual("r_next",(string)args["options"]["references"][0]["revision"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void RectangularPatternExpressionsKeepBothDirectionsAndExplicitUnits()
        {
            _backend.NativeCurves=true;_backend.NativePatterns=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Pick(new CadPoint(105,5));editor.Pick(new CadPoint(20,0),true);editor.Pick(new CadPoint(300,20),true);
            Do("sketch.pattern.two");Do("sketch.pattern.expressions");Do("sketch.native.SketchRectangularPatternCmd");
            foreach(string value in new[] {"N_x","2 * 6 mm","N_y","1 in"}) {Assert.True(editor.ActiveTextEntry.Replace(value));Assert.True(editor.ActiveTextEntry.Commit(out _));}
            Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.True((bool)options["two_directions"]);Assert.AreEqual("2 * 6 mm",(string)options["x_spacing_mm"]);Assert.AreEqual("1 in",(string)options["y_spacing_mm"]);
            Assert.AreEqual("N_y",(string)options["y_count"]);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void PendingPatternExpressionCannotContinueAfterCancelOrRevisionChange(bool revision)
        {
            _backend.NativePatterns=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.constraint.pick.ent_pattern");Do("sketch.pattern.edit.expressions");var first=editor.ActiveTextEntry;
            Assert.True(first.Replace("N_copie"));Assert.True(first.Commit(out _));var second=editor.ActiveTextEntry;
            Assert.True(second.Replace("45 deg"));
            if(revision){_backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);}
            else _workspace.Back();
            Assert.False(second.Commit(out _));Assert.False(_workspace.NativeEditor.HasDraft);Assert.Null(editor.ActiveTextEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void NativeOffsetFreezesOrderedCurveOptionsBeforePreview(bool point)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));
            Do("sketch.native.SketchOffsetCmd");Do("sketch.offset.connected");Do("sketch.offset.constraints");Do("sketch.offset.natural");
            Do(point ? "sketch.offset_point" : "sketch.offset_distance");
            Assert.False(Enabled("sketch.offset.connected"));if(point) Commit(5,-7);else Commit(7);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual(point ? "offset_point" : "offset_distance",(string)args["variant"]);
            var options=args["options"];Assert.True((bool)options["include_connected"]);Assert.False((bool)options["create_constraints"]);
            Assert.AreEqual("ent_line",(string)options["references"][0]["entity_id"]);
            if(point) {Assert.AreEqual(-7,(double)options["point_mm"][1]);Assert.Null(options["natural_direction"]);Assert.Null(options["distance_mm"]);}
            else {Assert.AreEqual(7,(double)options["distance_mm"]);Assert.False((bool)options["natural_direction"]);}
        }
        [Test]
        public void OffsetChainRejectsMultipleBasesAndPointSubparts()
        {
            _backend.NativeCurves=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));editor.Pick(new CadPoint(105,5),true);
            Assert.True(Enabled("sketch.offset_distance"));Do("sketch.offset.connected");Assert.False(Enabled("sketch.offset_distance"));
            Do("sketch.offset.connected");Do("sketch.selection.part.Start");editor.Pick(new CadPoint(0,0));Assert.False(Enabled("sketch.offset_point"));
            Assert.False(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);
        }
        [Test]
        public void OffsetReselectionRetainsItsOriginalChainAndConstraintOptions()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));Do("sketch.offset.connected");Do("sketch.offset_distance");Commit(7);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.editor.reselect.confirm"));editor.Pick(new CadPoint(20,0));Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];Assert.True((bool)options["include_connected"]);Assert.True((bool)options["create_constraints"]);
            Assert.AreEqual(7,(double)options["distance_mm"]);Assert.AreEqual("r_next",(string)options["references"][0]["revision"]);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase("SketchMoveCmd","transform_move")] [TestCase("SketchCopyCmd","transform_copy")] [TestCase("SketchRotateCmd","transform_rotate")]
        public void NativeTransformFreezesSelectionsAndOptionsUntilExplicitPreview(string command,string variant)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));
            Do("sketch.transform.remove_constraints");Do("sketch.transform.rotate_copy");Do("sketch.native."+command);
            if(variant=="transform_rotate") Commit(5,6,-45);else Commit(12,-8);
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual(variant,(string)args["variant"]);
            var options=args["options"];Assert.True((bool)options["remove_constraints"]);Assert.AreEqual("ent_line",(string)options["references"][0]["entity_id"]);
            if(variant=="transform_rotate") {Assert.True((bool)options["copy"]);Assert.AreEqual(-45,(double)options["angle_deg"]);}
            else {Assert.Null(options["copy"]);Assert.AreEqual(-8,(double)options["delta_mm"][1]);}
        }
        [Test]
        public void NativeTransformReselectionKeepsTheOriginalDeltaAndConstraintChoice()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));Do("sketch.native.SketchMoveCmd");Commit(12,-8);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.editor.reselect.confirm"));editor.Pick(new CadPoint(20,0));Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];Assert.AreEqual(-8,(double)options["delta_mm"][1]);Assert.False((bool)options["remove_constraints"]);
            Assert.AreEqual("r_next",(string)options["references"][0]["revision"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeConstraintVisibilityAndSelectionAreViewStateOnly()
        {
            _backend.NativeConstraints=true;OpenNative();var geometry=Field<InventorXrSo.Unity.Scene.DesignGeometryView>("_geometry");
            Assert.True(geometry.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text=="Fissaggio"));
            Do("sketch.native.SketchShowAllConstraintsCmd");Assert.False(geometry.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text=="Fissaggio"));
            Do("sketch.constraint.pick.ent_constraint");Assert.True(geometry.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text=="Fissaggio"));
            Assert.AreEqual(.003f,geometry.GetComponentsInChildren<UnityEngine.LineRenderer>().Single(l=>l.name=="line").widthMultiplier);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);Assert.False(_workspace.NativeEditor.HasDraft);
        }
        [TestCase(false)] [TestCase(true)]
        public void NativeConstraintDeletionRefusesNonDeletableOrUndeclaredOperations(bool missingCapabilities)
        {
            _backend.NativeConstraints=true;_backend.NativeNoCapabilities=missingCapabilities;OpenNative();
            Do("sketch.constraint.pick."+(missingCapabilities ? "ent_constraint" : "ent_locked"));
            Assert.False(Enabled("sketch.constraint.edit.delete"));Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeConstraintDeletionIsADraftAndRequiresCurrentSelectionAfterRefresh()
        {
            _backend.NativeConstraints=true;OpenNative();Do("sketch.constraint.pick.ent_constraint");Do("sketch.constraint.edit.delete");
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.editor.reselect.confirm"));Do("sketch.constraint.pick.ent_constraint");Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("constraint_delete",(string)args["variant"]);
            Assert.AreEqual("r_next",(string)args["options"]["references"][0]["revision"]);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)] [TestCase(true)]
        public void NativeNewDimensionFreezesExpressionAndNameWithoutANumericSubstitute(bool driven)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.dimension.text.expression");Assert.True(editor.ActiveTextEntry.Replace("2 * 6 mm"));Assert.True(editor.ActiveTextEntry.Commit(out _));
            Do("sketch.dimension.text.name");Assert.True(editor.ActiveTextEntry.Replace("Larghezza_guida"));Assert.True(editor.ActiveTextEntry.Commit(out _));
            Assert.False(_workspace.NativeEditor.HasDraft);if(driven) Do("sketch.dimension.driven");
            editor.Pick(new CadPoint(20,0));Do("sketch.dimension.aligned");Commit(18,25);
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];Assert.AreEqual("Larghezza_guida",(string)options["parameter_name"]);
            if(driven) Assert.Null(options["expression"]);else Assert.AreEqual("2 * 6 mm",(string)options["expression"]);
            Assert.Null(options["value_mm"]);Assert.Null(options["value_deg"]);Assert.AreEqual(18,(double)options["text_mm"][0]);
        }
        [TestCase("expression","2 * 6 mm")] [TestCase("name","Larghezza_guida")]
        public void NativeDimensionTextIsLiteralAndOnlyChangesTheDraft(string field,string value)
        {
            _backend.NativeDimensions=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.dimension.pick.ent_dimension");Do("sketch.dimension.edit."+field);
            Assert.True(_workspace.KeypadOpen);Assert.NotNull(editor.ActiveTextEntry);Assert.Null(editor.ActiveEntry);
            var entry=editor.ActiveTextEntry;Assert.True(entry.Replace(value));Assert.True(entry.Commit(out var reason),reason);
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(value,(string)options[field=="name" ? "parameter_name" : "expression"]);Assert.Null(options["value_mm"]);
            Assert.AreEqual("ent_dimension",(string)options["references"][0]["entity_id"]);
        }
        [TestCase(false)] [TestCase(true)]
        public void NativeCancelledInputCannotBecomeActiveWhenTheOtherKeyboardOpens(bool numericFirst)
        {
            _backend.NativeDimensions=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.dimension.pick.ent_dimension");Do("sketch.dimension.edit."+(numericFirst ? "value" : "expression"));
            Field<UiShell>("_shell").Palette.HideKeypad();Assert.False(editor.PromptOpen);
            Do("sketch.dimension.edit."+(numericFirst ? "expression" : "value"));
            if(numericFirst)
            {
                Assert.Null(editor.ActiveEntry);Assert.NotNull(editor.ActiveTextEntry);Assert.Null(_workspace.ArmedField);
                Assert.True(editor.ActiveTextEntry.Replace("2 * 6 mm"));Assert.True(editor.ActiveTextEntry.Commit(out _));
            }
            else
            {
                Assert.Null(editor.ActiveTextEntry);Assert.NotNull(editor.ActiveEntry);Assert.NotNull(_workspace.ArmedField);
                Assert.True(_workspace.SetArmedField(editor.ActiveEntry.Id,14));
            }
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeDimensionTextCannotCommitAfterItsRevisionChanges()
        {
            _backend.NativeDimensions=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.dimension.pick.ent_dimension");Do("sketch.dimension.edit.expression");var entry=editor.ActiveTextEntry;
            entry.Replace("2 * 6 mm");_backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);
            Assert.False(entry.Commit(out _));Assert.False(_workspace.NativeEditor.HasDraft);Assert.False(editor.PromptOpen);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeExistingDimensionRemainsReadableButCannotMutateWithoutCapabilities()
        {
            _backend.NativeDimensions=true;_backend.NativeNoCapabilities=true;OpenNative();Do("sketch.dimension.pick.ent_dimension");
            foreach(string operation in new[] {"value","position","driven","delete"})
            {var action=_workspace.Actions.Single(a=>a.Id=="sketch.dimension.edit."+operation);Assert.False(action.TryInvoke());StringAssert.Contains("backend",action.DisabledReason);}
            Assert.False(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)] [TestCase(true)]
        public void NativeExistingDimensionValueUsesItsNativeUnitAndExplicitPreview(bool angular)
        {
            _backend.NativeDimensions=true;_backend.NativeAngular=angular;OpenNative();Do("sketch.dimension.pick.ent_dimension");Do("sketch.dimension.edit.value");
            Assert.AreEqual(angular ? QuantityUnit.Degrees : QuantityUnit.Millimeters,_workspace.ActiveEntry.Unit);
            Commit(25);Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("dimension_update",(string)args["variant"]);
            Assert.AreEqual(25,(double)args["options"][angular ? "value_deg" : "value_mm"]);Assert.AreEqual("ent_dimension",(string)args["options"]["references"][0]["entity_id"]);
        }
        [TestCase("position")] [TestCase("driven")] [TestCase("delete")]
        public void NativeExistingDimensionEditsRemainDraftsUntilExplicitPreview(string edit)
        {
            _backend.NativeDimensions=true;OpenNative();Do("sketch.dimension.pick.ent_dimension");Do("sketch.dimension.edit."+edit);
            if(edit=="position") Commit(22,34);
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual(edit=="delete" ? "dimension_delete" : "dimension_update",(string)args["variant"]);
            if(edit=="driven") Assert.True((bool)args["options"]["driven"]);
            if(edit=="position") Assert.AreEqual(34,(double)args["options"]["text_mm"][1]);
        }
        [Test]
        public void NativeExistingDimensionReselectionPreservesEditedPosition()
        {
            _backend.NativeDimensions=true;OpenNative();Do("sketch.dimension.pick.ent_dimension");Do("sketch.dimension.edit.position");Commit(22,34);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.editor.reselect.confirm"));Do("sketch.dimension.pick.ent_dimension");Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            Assert.AreEqual(34,(double)_backend.LastOperations[0]["arguments"]["options"]["text_mm"][1]);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)] [TestCase(true)]
        public void NativeDimensionCapturesPlacementModeAndValueWithoutImplicitPreview(bool driven)
        {
            OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Pick(new CadPoint(20,0));
            Assert.True(Enabled("sketch.native.SketchGeneralDimensionCmd"));
            if(driven) Do("sketch.dimension.driven");
            Do("sketch.dimension.aligned");
            if(driven) Commit(18,25); else Commit(18,25,42);
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual("dimension_aligned",(string)args["variant"]);
            var options=args["options"];
            Assert.AreEqual(driven,(bool)options["driven"]);Assert.AreEqual(18,(double)options["text_mm"][0]);Assert.AreEqual(25,(double)options["text_mm"][1]);
            if(driven) Assert.Null(options["value_mm"]);else Assert.AreEqual(42,(double)options["value_mm"]);
            Assert.AreEqual("ent_line",(string)options["references"][0]["entity_id"]);
        }
        [Test]
        public void NativeDimensionReselectionPreservesUnitsAndOptionsAfterRevisionChange()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");editor.Pick(new CadPoint(20,0));
            Do("sketch.dimension.horizontal");Commit(13,29,35);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);
            Do("sketch.editor.refresh");editor.Pick(new CadPoint(20,0));Do("sketch.editor.reselect.confirm");Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(35,(double)options["value_mm"]);Assert.AreEqual(13,(double)options["text_mm"][0]);
            Assert.AreEqual("r_next",(string)options["references"][0]["revision"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeMissingCapabilitiesShowGeometryButRefuseCreationFormatAndConstraints()
        {
            _backend.NativeNoCapabilities=true; OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Assert.False(_workspace.NativeEditor.Snapshot.Partial); Assert.True(editor.Pick(new CadPoint(20,0)));
            foreach(string id in new[] {"SketchLineCmd","SketchConstructionCmd","SketchFixConstraintCmd"})
            {
                var action=_workspace.Actions.Single(a=>a.Id=="sketch.native."+id);
                Assert.False(action.TryInvoke()); StringAssert.Contains("backend",action.DisabledReason);
            }
            Assert.AreEqual(0,_backend.Previews); Assert.AreEqual(0,_backend.Commits); Assert.False(_workspace.NativeEditor.HasDraft);
        }
        [Test]
        public void NativeConstraintOptionsUseTheSharedKeypadAndSurviveDraftRefresh()
        {
            OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.constraint.axis.1"); Do("sketch.constraint.axis.2");
            Do("sketch.constraint.proximity.point"); Commit(12,34); Assert.False(_workspace.NativeEditor.HasDraft);
            editor.Pick(new CadPoint(20,0)); Do("sketch.native.SketchHorizontalConstraintCmd");
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");
            _workspace.SetDocumentState(_backend.State); Do("sketch.editor.refresh");
            editor.Pick(new CadPoint(20,0)); Do("sketch.editor.reselect.confirm"); Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.False((bool)options["ellipse_one_major_axis"]); Assert.False((bool)options["ellipse_two_major_axis"]);
            Assert.Null(options["proximity_mm"]); Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase("SketchTangentConstraintCmd","tangent",100,0)]
        [TestCase("SketchCurvatureConstraintCmd","smooth",210,5)]
        public void NativeProximityChoiceIsCapturedOnlyByTheChosenTangentOrG2Operation(string id,string kind,double x,double y)
        {
            _backend.NativeCurves=true; OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.constraint.proximity.point"); Commit(12,34);
            editor.Pick(new CadPoint(20,0)); editor.Pick(new CadPoint(x,y),true);
            Do("sketch.native."+id); Assert.AreEqual(0,_backend.Previews); Do(CommitIds.Preview);
            var op=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual("constraint_"+kind,(string)op["variant"]);
            CollectionAssert.AreEqual(new[] {12d,34d},op["options"]["proximity_mm"].Values<double>().ToArray());
            Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase("SketchFixConstraintCmd","ground")]
        [TestCase("SketchHorizontalConstraintCmd","horizontal")]
        [TestCase("SketchVerticalConstraintCmd","vertical")]
        public void NativeDirectionalConstraintRequiresSelectionAndExplicitPreview(string id,string kind)
        {
            OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Assert.False(Enabled("sketch.native."+id)); editor.Pick(new CadPoint(20,0));
            Do("sketch.native."+id); Assert.AreEqual(0,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Preview); var op=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual("constraint_"+kind,(string)op["variant"]);
            Assert.AreEqual("ent_line",(string)op["options"]["references"][0]["entity_id"]);
            Assert.True(Enabled(CommitIds.Apply)); Do(CommitIds.Cancel); Assert.False(_workspace.NativeEditor.HasDraft);
        }
        [Test]
        public void NativeCoincidencePreservesEndpointSelectionOrder()
        {
            OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Actions().Single(a=>a.Id=="sketch.selection.part.Start").TryInvoke(); editor.Pick(new CadPoint(0,0));
            editor.Actions().Single(a=>a.Id=="sketch.selection.part.End").TryInvoke(); editor.Pick(new CadPoint(40,0),true);
            Assert.False(Enabled("sketch.native.SketchParallelConstraintCmd")); Do("sketch.native.SketchCoincidentConstraintCmd"); Do(CommitIds.Preview);
            var refs=_backend.LastOperations[0]["arguments"]["options"]["references"];
            CollectionAssert.AreEqual(new[] {"Start","End"},refs.Select(r=>(string)r["subpart"]).ToArray());
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeCoincidenceCannotSelectTheSamePointThroughTwoFilters()
        {
            _backend.NativeOverlap=true; OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Actions().Single(a=>a.Id=="sketch.selection.filter.point").TryInvoke(); editor.Pick(new CadPoint(20,0));
            editor.Actions().Single(a=>a.Id=="sketch.selection.part.Point").TryInvoke(); editor.Pick(new CadPoint(20,0),true);
            Assert.AreEqual(2,editor.Selected.Count); Assert.False(Enabled("sketch.native.SketchCoincidentConstraintCmd"));
            Assert.AreEqual(0,_backend.Previews); Assert.False(_workspace.NativeEditor.HasDraft);
        }
        [Test]
        public void PreservedNativeFormatRequiresNewSelectionsAfterRevisionChangeAndKeepsItsValue()
        {
            OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Pick(new CadPoint(20,0)); Do("sketch.native.SketchConstructionCmd");
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");
            _workspace.SetDocumentState(_backend.State); Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.editor.confirm")); Assert.True(_workspace.NativeEditor.HasDraft);
            Assert.True(editor.Pick(new CadPoint(20,0))); Do("sketch.editor.reselect.confirm"); Do(CommitIds.Preview);
            Assert.True((bool)_backend.LastOperations[0]["arguments"]["options"]["value"]);
            Assert.AreEqual("r_next",(string)_backend.LastOperations[0]["arguments"]["options"]["references"][0]["revision"]);
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeConstructionAndCenterlineAreDraftsOnRevisionBoundWholeEntities()
        {
            OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Assert.False(Enabled("sketch.native.SketchConstructionCmd")); editor.Pick(new CadPoint(20,0));
            Do("sketch.native.SketchConstructionCmd"); Assert.AreEqual(0,_backend.Previews); Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.True((bool)options["value"]); Assert.AreEqual(_backend.State.Revision,(string)options["references"][0]["revision"]);
            Assert.AreEqual("Entity",(string)options["references"][0]["subpart"]);
            Do(CommitIds.Cancel); Do("sketch.native.SketchCenterlineCmd"); Do(CommitIds.Preview);
            Assert.AreEqual("format_centerline",(string)_backend.LastOperations[0]["arguments"]["variant"]);
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeHoleCenterAcceptsPointsAndRejectsLineEndpoints()
        {
            _backend.NativeOverlap=true; OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Actions().Single(a=>a.Id=="sketch.selection.part.Start").TryInvoke(); editor.Pick(new CadPoint(0,0));
            Assert.False(Enabled("sketch.native.SketchCenterlineCmd")); Assert.False(Enabled("sketch.native.SketchConstructionCmd"));
            Assert.False(Enabled("sketch.native.SketchHoleCenterCmd"));
            editor.Actions().Single(a=>a.Id=="sketch.selection.part.Point").TryInvoke(); editor.Pick(new CadPoint(20,0));
            Do("sketch.native.SketchHoleCenterCmd"); Do(CommitIds.Preview);
            Assert.AreEqual("format_hole_center",(string)_backend.LastOperations[0]["arguments"]["variant"]);
            Assert.AreEqual("Point",(string)_backend.LastOperations[0]["arguments"]["options"]["references"][0]["subpart"]);
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeEndpointFilterKeepsSubpartDistinctFromWholeCurve()
        {
            OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            editor.Actions().Single(a=>a.Id=="sketch.selection.part.Start").TryInvoke();
            Assert.True(editor.Pick(new CadPoint(0,0,0))); Assert.AreEqual(SketchSubpart.Start,editor.Selected.Single().Subpart);
            editor.Actions().Single(a=>a.Id=="sketch.selection.part.Entity").TryInvoke();
            Assert.True(editor.Pick(new CadPoint(0,0,0),true)); Assert.AreEqual(2,editor.Selected.Count);
            Assert.AreEqual(0,_backend.Commits); Assert.AreEqual(0,_backend.Previews);
        }
        [Test]
        public void NativeOverlapRequiresExplicitChoiceAndKeepsAdditiveSelection()
        {
            _backend.NativeOverlap=true; OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Assert.True(editor.Pick(new CadPoint(20,0,0))); Assert.IsEmpty(editor.Selected);
            var choices=editor.Actions().Where(a=>a.Tab==NativeSketchEditorUi.SelectionTab).ToArray();
            Assert.AreEqual(2,choices.Length); Assert.True(choices[1].TryInvoke());
            Assert.AreEqual("ent_point",editor.Selected.Single().EntityId); Assert.False(choices[0].TryInvoke());
            editor.Pick(new CadPoint(20,0,0),true);
            Assert.True(editor.Actions().Single(a=>a.Tab==NativeSketchEditorUi.SelectionTab && a.Label.Contains("linea")).TryInvoke());
            CollectionAssert.AreEqual(new[] {"ent_point","ent_line"},editor.Selected.Select(r=>r.EntityId).ToArray());
            Assert.AreEqual(0,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeTypeFilterAndRevisionChangeInvalidateOverlapChoices()
        {
            _backend.NativeOverlap=true; OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Assert.True(editor.Actions().Single(a=>a.Id=="sketch.selection.filter.point").TryInvoke());
            editor.Pick(new CadPoint(20,0,0)); Assert.AreEqual("point",editor.Selected.Single().Type);
            Assert.IsEmpty(editor.Actions().Where(a=>a.Tab==NativeSketchEditorUi.SelectionTab));
            editor.Actions().Single(a=>a.Id=="sketch.selection.filter.all").TryInvoke(); editor.Pick(new CadPoint(20,0,0));
            var stale=editor.Actions().First(a=>a.Tab==NativeSketchEditorUi.SelectionTab);
            _workspace.SetOnline(false); Assert.False(stale.TryInvoke()); Assert.IsEmpty(editor.Selected);
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeSketchInputContextFollowsWorkspaceVisibilityAndFinish()
        {
            OpenNative(); Assert.True(_workspace.SketchOpen); Assert.True(_workspace.TabState.SketchOpen);
            Assert.AreEqual(InventorXrSo.Core.Input.InputState.SketchOpen,
                InventorXrSo.Core.Input.InputMap.Resolve(_workspace.KeypadOpen,_workspace.InputArmed,_workspace.SketchOpen,false));
            _workspace.Close(); Assert.False(_workspace.SketchOpen); Assert.False(_workspace.TabState.SketchOpen);
            _workspace.Open(); Assert.True(_workspace.SketchOpen);
            Do("sketch.editor.finish"); Assert.False(_workspace.SketchOpen); Assert.False(_workspace.TabState.SketchOpen);
        }
        [Test]
        public void NativeCreationUsesSharedKeypadAndCommitBarThenKeepsSketchOpen()
        {
            OpenNative(); Assert.AreSame(Field<DesignSession>("_session"),_workspace.NativeEditor.Mutation);
            Assert.False(Enabled(Create));
            Do("sketch.native.SketchLineCmd"); Commit(1,2,30,40);
            Assert.False(Enabled(CommitIds.Apply)); Assert.AreEqual(0,_backend.Previews);
            Do(CommitIds.Preview); Assert.True(Enabled(CommitIds.Apply));
            Assert.AreEqual("edit_sketch",(string)_backend.LastOperations[0]["command"]);
            Assert.AreEqual("ent_existing",(string)_backend.LastOperations[0]["arguments"]["sketch_id"]);
            Assert.AreEqual(1,_backend.LastOperations.Count);
            Do(CommitIds.Apply); Assert.AreEqual(1,_backend.Commits);
            Assert.AreEqual("ent_existing",_workspace.NativeEditor.SketchId);
            Assert.False(_workspace.NativeEditor.HasDraft); Assert.AreEqual("r2",_workspace.NativeEditor.Snapshot.State.Revision);
            Assert.True(Enabled("sketch.native.SketchLineCmd"));
            Do("sketch.editor.finish"); Assert.Null(_workspace.NativeEditor.SketchId); Assert.True(Enabled(Create));
        }
        [Test]
        public void NativeReconnectionPreservesOperationAndRequiresReloadAndReconfirmation()
        {
            OpenNative(); Do("sketch.native.SketchLineCmd"); Commit(0,0,10,10); Do(CommitIds.Preview);
            _workspace.Bind(null); Assert.False(Enabled(CommitIds.Apply));
            _workspace.Bind(_backend); _workspace.SetScene(_scene); _workspace.SetOnline(true);
            Assert.True(_workspace.NativeEditor.HasDraft); Assert.False(Enabled(CommitIds.Apply)); Assert.False(Enabled(CommitIds.Preview));
            Do("sketch.editor.refresh"); Assert.False(Enabled(CommitIds.Preview));
            Do("sketch.editor.confirm"); Do(CommitIds.Preview); Assert.True(Enabled(CommitIds.Apply));
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeSceneRebuildWithDifferentRevisionRevokesRenderingProofWithoutDiscardingOperation()
        {
            OpenNative(); Do("sketch.native.SketchLineCmd"); Commit(0,0,10,10); Do(CommitIds.Preview);
            _backend.State = new DocumentState(_backend.State.DocumentId,"r_new","v_new");
            var changed = NativeSketchTestData.Scene(_backend.State,_backend.Model);
            _view.Show(changed); _workspace.SetScene(changed);
            Assert.False(Enabled(CommitIds.Apply)); Assert.True(_workspace.NativeEditor.HasDraft);
            Do("sketch.editor.refresh"); Do("sketch.editor.confirm"); Do(CommitIds.Preview); Assert.True(Enabled(CommitIds.Apply));
        }
        [Test]
        public void NativeAnglePromptUsesDegreesAndPartialSnapshotRefusesCreation()
        {
            OpenNative(); Do("sketch.native.SketchCenterPointArcCmd"); Commit(0,0,10);
            Assert.AreEqual(QuantityUnit.Degrees,_workspace.ActiveEntry.Unit); Commit(0,90);
            Do(CommitIds.Cancel); Assert.False(_workspace.NativeEditor.HasDraft);
            _backend.NativePartial=true; Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.native.SketchLineCmd")); Assert.True(_workspace.NativeEditor.Snapshot.Partial);
            Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase("SketchThreePointRectangleCmd","rectangle_three_points")]
        [TestCase("SketchTwoPointCenRectangleCmd","rectangle_center_two_points")]
        [TestCase("SketchThreePointCenRectangleCmd","rectangle_center_three_points")]
        public void NativeRectangleVariantsKeepTheirExactCommandThroughTheKeypad(string id,string variant)
        {
            OpenNative(); Do("sketch.native."+id);
            while (_workspace.ActiveEntry!=null) { var entry=_workspace.ActiveEntry; Assert.True(entry.CommitValue(entry.Value,out var reason),reason); }
            Do(CommitIds.Preview); Assert.AreEqual(variant,(string)_backend.LastOperations[0]["arguments"]["variant"]);
            Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(true,3)][TestCase(true,65)][TestCase(true,120)]
        [TestCase(false,3)][TestCase(false,65)][TestCase(false,120)]
        public void NativePolygonAcceptsDesktopSideRangeAndKeepsInvalidRangePending(bool inscribed,int sides)
        {
            OpenNative(); Do("sketch.native.SketchPolygonCmd"); Do("sketch.polygon."+(inscribed ? "inscribed" : "circumscribed"));
            Commit(13,21,25,37); var field=_workspace.ActiveEntry;
            Assert.False(field.CommitValue(2,out _)); Assert.AreSame(field,_workspace.ActiveEntry);
            Assert.False(field.CommitValue(121,out _)); Assert.AreSame(field,_workspace.ActiveEntry);
            Assert.False(_workspace.NativeEditor.HasDraft); Assert.AreEqual(0,_backend.Commits);
            Assert.True(field.CommitValue(sides,out var reason),reason); Assert.True(_workspace.NativeEditor.HasDraft);
            Do(CommitIds.Preview); var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(JTokenType.Integer,options["sides"].Type); Assert.AreEqual(sides,(int)options["sides"]);
            Assert.AreEqual(inscribed,(bool)options["inscribed"]); Assert.AreEqual(0,_backend.Commits);
            Assert.AreEqual(25d,(double)options["points_mm"][1][0]); Assert.AreEqual(37d,(double)options["points_mm"][1][1]);
        }
        [TestCase(true)]
        [TestCase(false)]
        public void NativePolygonPreservesChosenModeAndIntegerSideCount(bool inscribed)
        {
            OpenNative(); Do("sketch.native.SketchPolygonCmd"); Do("sketch.polygon."+(inscribed ? "inscribed" : "circumscribed"));
            Commit(0,0,20,0); Assert.AreEqual(QuantityUnit.None,_workspace.ActiveEntry.Unit); Commit(5);
            Do(CommitIds.Preview); var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(JTokenType.Integer,options["sides"].Type); Assert.AreEqual(5,(int)options["sides"]);
            Assert.AreEqual(inscribed,(bool)options["inscribed"]); Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void ClosedNativeWorkspaceDoesNotRedrawGeometryOnReconnect()
        {
            OpenNative(); _workspace.Close(); _workspace.SetOnline(false); _workspace.SetOnline(true);
            Assert.IsEmpty(_workspace.Geometry.GetComponentsInChildren<UnityEngine.LineRenderer>().Where(l=>l.name=="line"));
            _workspace.Open(); Assert.IsNotEmpty(_workspace.Geometry.GetComponentsInChildren<UnityEngine.LineRenderer>().Where(l=>l.name=="line"));
        }
        [Test]
        public void NativeLostCommitResponseRequiresCadReviewAndNeverReplaysTheOperation()
        {
            OpenNative(); Do("sketch.native.SketchLineCmd"); Commit(0,0,10,10); Do(CommitIds.Preview);
            _backend.CommitError=new System.IO.IOException("Lost response"); Do(CommitIds.Apply);
            Assert.True(_workspace.NativeEditor.Mutation.CommitOutcomeUnknown); Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.False(Enabled("sketch.editor.finish")); Assert.False(Enabled(CommitIds.Apply));
            Do(CommitIds.Recover); Assert.AreEqual(1,_backend.Commits); Assert.True(Enabled("sketch.native.SketchLineCmd"));
        }
    }
    public partial class LamieraWorkspaceTests
    {
        [Test] public void NativePreviewSurvivesSameRevisionSceneRefreshButNotARevisionChange()
        {
            OpenNative(); Do("sketch.native.SketchLineCmd"); CommitNative(0,0,10,10); Do(CommitIds.Preview);
            var preview = PreviewView;
            var plan = preview.PlanId;
            Assert.True(Enabled(CommitIds.Apply));
            _view.Show(_scene); _workspace.SetScene(_scene);
            Assert.True(_workspace.NativeEditor.HasDraft);
            Assert.True(preview.IsShowing); Assert.AreEqual(plan, preview.PlanId);
            Assert.True(Enabled(CommitIds.Apply), "same-revision scene delivery must not cancel a validated native preview");
            _workspace.SetDocumentState(new DocumentState(_backend.State.DocumentId,"r_new","v_new"));
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(preview.IsShowing);
            Assert.Zero(_backend.Commits);
        }


        [TestCase(false)][TestCase(true)]
        public void SplineFinishesAtTwoPointsAndUndoRestoresTheMinimumGuard(bool control)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(control ? "SketchCPSplineCmd" : "SketchSplineCmd"));
            editor.Pick(new CadPoint(7,9));Assert.False(Enabled("sketch.spline.finish"));
            editor.Pick(new CadPoint(23,17));Assert.True(Enabled("sketch.spline.finish"));
            Do("sketch.spline.undo");Assert.False(Enabled("sketch.spline.finish"));
            Do("sketch.spline.numeric");Assert.True(_workspace.ActiveEntry.CommitValue(31,out _));
            Assert.True(_workspace.ActiveEntry.CommitValue(19,out _));
            Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            var points=(JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"];
            Assert.AreEqual(2,points.Count);Assert.AreEqual(7,(double)points[0][0]);
            Assert.AreEqual(31,(double)points[1][0]);Assert.AreEqual(19,(double)points[1][1]);
        }
        [TestCase(false)][TestCase(true)]
        public void SplineCollectsVariablePointsAndFinishesOnlyExplicitly(bool control)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(control ? "SketchCPSplineCmd" : "SketchSplineCmd"));
            Assert.True(editor.CoordinatePickActive);Assert.False(Enabled("sketch.spline.finish"));
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(10,10));
            Assert.True(Enabled("sketch.spline.finish"));editor.Pick(new CadPoint(20,0));
            Assert.True(Enabled("sketch.spline.finish"));Assert.False(editor.Session.HasDraft);
            Do("sketch.spline.numeric");var old=_workspace.ActiveEntry;
            Assert.True(old.CommitValue(30,out _));Assert.True(_workspace.ActiveEntry.CommitValue(15,out _));
            Assert.Null(_workspace.ActiveEntry);old.CommitValue(999,out _);
            editor.Pick(new CadPoint(40,0));Do("sketch.spline.undo");editor.Pick(new CadPoint(45,5));
            Assert.False(editor.Session.HasDraft);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("sketch.spline.finish");Assert.False(editor.PromptOpen);Assert.False(editor.CoordinatePickActive);
            Assert.True(editor.Session.HasDraft);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];var points=(JArray)args["options"]["points_mm"];
            Assert.AreEqual(control ? "spline_control" : "spline_fit",(string)args["variant"]);
            Assert.AreEqual(5,points.Count);Assert.AreEqual(30,(double)points[3][0]);Assert.AreEqual(15,(double)points[3][1]);
            Assert.AreEqual(45,(double)points[4][0]);
        }
        [TestCase(false)][TestCase(true)]
        public void SplinePointBudgetRequiresExplicitFinishAndCancellationRevokesCallbacks(bool control)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            string command="sketch.native."+(control ? "SketchCPSplineCmd" : "SketchSplineCmd");Do(command);
            Do("sketch.spline.numeric");var stale=_workspace.ActiveEntry;editor.CancelPrompt();stale.CommitValue(10,out _);
            Assert.False(editor.PromptOpen);Assert.False(editor.Session.HasDraft);Do(command);
            for(int i=0;i<65;i++) editor.Pick(new CadPoint(i,10*(i%2)));
            Assert.False(editor.Session.HasDraft);Assert.False(Enabled("sketch.spline.numeric"));
            Do("sketch.spline.undo");Assert.True(Enabled("sketch.spline.numeric"));editor.Pick(new CadPoint(80,5));
            Do("sketch.spline.finish");Do(CommitIds.Preview);
            var points=(JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"];
            Assert.AreEqual(64,points.Count);Assert.AreEqual(80,(double)points[63][0]);
        }
        [TestCase(false)][TestCase(true)]
        public void SplineNumericCancellationAndRevisionChangeRevokeThePendingField(bool revision)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchSplineCmd");
            editor.Pick(new CadPoint(0,0));editor.Pick(new CadPoint(10,10));Do("sketch.spline.numeric");
            var stale=_workspace.ActiveEntry;
            if(revision)
            {
                _backend.State=new DocumentState(_backend.State.DocumentId,"r_spline_next","v_spline_next");
                _workspace.SetDocumentState(_backend.State);
                Assert.False(editor.PromptOpen);Assert.False(editor.CoordinatePickActive);
            }
            else Field<UiShell>("_shell").Palette.HideKeypad();
            stale.CommitValue(99,out _);Assert.Null(_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            if(!revision)
            {
                Assert.True(editor.CoordinatePickActive);Assert.True(Enabled("sketch.spline.finish"));
                editor.Pick(new CadPoint(20,0));Do("sketch.spline.finish");Do(CommitIds.Preview);
                Assert.AreEqual(3,((JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"]).Count);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void ThreePointArcCollectsStartEndThroughAndRejectsCollinearPenPoint(bool numericThrough)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchThreePointArcCmd");
            Assert.True(editor.Pick(new CadPoint(10,15)));Assert.True(editor.Pick(new CadPoint(30,15)));
            var field=_workspace.ActiveEntry;editor.Pick(new CadPoint(20,15));editor.Pick(new CadPoint(10,15));
            Assert.AreSame(field,_workspace.ActiveEntry);Assert.True(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            if(numericThrough) {Assert.True(field.CommitValue(20,out _));Assert.True(_workspace.ActiveEntry.CommitValue(25,out _));}
            else
            {NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(20,25));Frame();PressTrigger();ReleaseTrigger();}
            Assert.True(editor.Session.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);field.CommitValue(99,out _);Do(CommitIds.Preview);
            var points=_backend.LastOperations[0]["arguments"]["options"]["points_mm"];
            Assert.AreEqual(10,(double)points[0][0],.0001);Assert.AreEqual(30,(double)points[1][0],.0001);
            Assert.AreEqual(20,(double)points[2][0],.0001);Assert.AreEqual(25,(double)points[2][1],.0001);
        }
        [TestCase(false,90,false)][TestCase(false,-90,false)][TestCase(false,270,false)][TestCase(false,-270,false)]
        [TestCase(true,90,false)][TestCase(true,-90,false)][TestCase(true,270,false)][TestCase(true,-270,false)]
        [TestCase(false,90,true)][TestCase(false,-270,true)][TestCase(true,90,true)][TestCase(true,-270,true)]
        public void ArcSlotPenCollectsNativePointsSweepAndWidth(bool threePoint,double sweep,bool numericWidth)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(threePoint ? "SketchThreePointArcSlotCmd" : "SketchCenterPointArcSlotCmd"));
            var center=new CadPoint(10,15);var start=new CadPoint(30,15);double radians=sweep*System.Math.PI/180;
            CadPoint Point(double angle)=>center+new CadPoint(20*System.Math.Cos(angle),20*System.Math.Sin(angle));
            Assert.True(editor.Pick(threePoint ? start : center));
            Assert.True(editor.Pick(threePoint ? Point(radians) : start));
            if(threePoint) Assert.True(editor.Pick(Point(radians/2)));
            else
            {
                for(int degrees=90;degrees<System.Math.Abs(sweep);degrees+=90)
                {NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,Point(System.Math.Sign(sweep)*degrees*System.Math.PI/180));Frame();}
                NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,Point(radians));
                Frame();PressTrigger();ReleaseTrigger();
            }
            var widthField=_workspace.ActiveEntry;Assert.NotNull(widthField);Assert.True(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            if(numericWidth) Assert.True(widthField.CommitValue(7,out _));
            else
            {
                NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(34,15-3*System.Math.Sign(sweep)));
                Frame();PressTrigger();ReleaseTrigger();
            }
            Assert.True(editor.Session.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);widthField.CommitValue(99,out _);
            Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];var options=args["options"];
            Assert.AreEqual(threePoint ? "slot_three_point_arc" : "slot_center_arc",(string)args["variant"]);
            Assert.AreEqual(numericWidth ? 7 : 10,(double)options["width_mm"],.0001);
            Assert.AreEqual(threePoint ? 30 : 10,(double)options["points_mm"][0][0],.0001);
            if(!threePoint) Assert.AreEqual(sweep,(double)options["sweep_deg"],.0001);
            else Assert.AreEqual(Point(radians/2).Y,(double)options["points_mm"][2][1],.0001);
        }
        [TestCase(false)][TestCase(true)]
        public void ArcSlotWidthRejectsZeroAndUiPressThenRevokesCancelledField(bool threePoint)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native."+(threePoint ? "SketchThreePointArcSlotCmd" : "SketchCenterPointArcSlotCmd"));
            if(threePoint) {editor.Pick(new CadPoint(30,15));editor.Pick(new CadPoint(10,35));editor.Pick(new CadPoint(10+System.Math.Sqrt(200),15+System.Math.Sqrt(200)));}
            else {editor.Pick(new CadPoint(10,15));editor.Pick(new CadPoint(30,15));Assert.True(_workspace.ActiveEntry.CommitValue(90,out _));}
            var field=_workspace.ActiveEntry;Assert.True(editor.CoordinatePickActive);editor.Pick(new CadPoint(30,15));editor.Pick(new CadPoint(-99999,99999));
            Assert.AreSame(field,_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            _workspace.UiHitOverride=()=>true;NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(34,12));
            Frame();PressTrigger();ReleaseTrigger();Assert.AreSame(field,_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            editor.CancelPrompt();field.CommitValue(7,out _);Assert.False(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase("SketchCentertoCenterSlotCmd","slot_center_to_center",0)][TestCase("SketchCentertoCenterSlotCmd","slot_center_to_center",1)][TestCase("SketchCentertoCenterSlotCmd","slot_center_to_center",2)]
        [TestCase("SketchOverallLengthSlotCmd","slot_overall",0)][TestCase("SketchOverallLengthSlotCmd","slot_overall",1)][TestCase("SketchOverallLengthSlotCmd","slot_overall",2)]
        [TestCase("SketchCenterPointSlotCmd","slot_center",0)][TestCase("SketchCenterPointSlotCmd","slot_center",1)][TestCase("SketchCenterPointSlotCmd","slot_center",2)]
        public void StraightSlotWidthUsesTheThirdPenPointOrNumericAlternative(string command,string variant,int mode)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native."+command);
            Assert.True(editor.Pick(new CadPoint(10,15)));Assert.True(editor.Pick(new CadPoint(34,47)));
            var widthField=_workspace.ActiveEntry;Assert.True(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            double expected=mode==2 ? 7 : mode==1 && variant!="slot_overall" ? 10 : 6;
            if(mode==2) Commit(expected);
            else
            {
                var point=mode==0 ? new CadPoint(19.6,32.8) : new CadPoint(34,52);
                NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,point);
                Frame();PressTrigger();ReleaseTrigger();
            }
            Assert.True(editor.Session.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            widthField.CommitValue(99,out _);Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(variant,(string)_backend.LastOperations[0]["arguments"]["variant"]);
            Assert.AreEqual(expected,(double)options["width_mm"],.0001);
            Assert.AreEqual(34,(double)options["points_mm"][1][0],.0001);
            Assert.AreEqual(47,(double)options["points_mm"][1][1],.0001);
        }
        [TestCase("SketchCentertoCenterSlotCmd")][TestCase("SketchOverallLengthSlotCmd")][TestCase("SketchCenterPointSlotCmd")]
        public void StraightSlotInvalidWidthAndUiPressKeepThePromptUntilCancel(string command)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native."+command);
            Assert.True(editor.Pick(new CadPoint(10,15)));Assert.True(editor.Pick(new CadPoint(34,47)));var field=_workspace.ActiveEntry;
            Assert.True(editor.Pick(new CadPoint(22,31)));Assert.True(editor.Pick(new CadPoint(-99999,99999)));
            Assert.AreSame(field,_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            _workspace.UiHitOverride=()=>true;NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(19.6,32.8));
            Frame();PressTrigger();ReleaseTrigger();Assert.AreSame(field,_workspace.ActiveEntry);Assert.False(editor.Session.HasDraft);
            editor.CancelPrompt();field.CommitValue(7,out _);Assert.False(editor.CoordinatePickActive);Assert.False(editor.Session.HasDraft);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(90,false)][TestCase(-90,false)][TestCase(270,false)][TestCase(-270,false)][TestCase(90,true)][TestCase(-270,true)]
        public void CenterArcPenFollowsTheSignedPathAndKeepsItsInitialRadius(double sweep,bool numericEnd)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchCenterPointArcCmd");
            Assert.True(editor.Pick(new CadPoint(10,15)));var radiusField=_workspace.ActiveEntry;
            Assert.True(editor.Pick(new CadPoint(20,15)));radiusField.CommitValue(99,out _);var endField=_workspace.ActiveEntry;
            for(int degrees=90;degrees<System.Math.Abs(sweep);degrees+=90)
            {
                double angle=System.Math.Sign(sweep)*degrees*System.Math.PI/180;
                NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10+10*System.Math.Cos(angle),15+10*System.Math.Sin(angle)));Frame();
                Assert.True(editor.CoordinatePickActive);Assert.False(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);
            }
            if(numericEnd) Assert.True(endField.CommitValue(sweep,out _));
            else
            {
                double angle=sweep*System.Math.PI/180;
                NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10+3*System.Math.Cos(angle),15+3*System.Math.Sin(angle)));
                Frame();PressTrigger();ReleaseTrigger();
            }
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);endField.CommitValue(99,out _);Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(10,(double)options["radius_mm"],.0001);Assert.AreEqual(0,(double)options["start_deg"],.0001);Assert.AreEqual(sweep,(double)options["sweep_deg"],.0001);
        }
        [Test]
        public void CenterArcCanUseNumericRadiusAndStartThenAPenEndpoint()
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchCenterPointArcCmd");
            Assert.True(editor.Pick(new CadPoint(10,15)));var radius=_workspace.ActiveEntry;Assert.True(radius.CommitValue(10,out _));
            var start=_workspace.ActiveEntry;Assert.True(start.CommitValue(30,out _));Assert.True(editor.CoordinatePickActive);
            double end=120*System.Math.PI/180;Assert.True(editor.Pick(new CadPoint(10+3*System.Math.Cos(end),15+3*System.Math.Sin(end))));
            Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(10,(double)options["radius_mm"],.0001);Assert.AreEqual(30,(double)options["start_deg"],.0001);Assert.AreEqual(90,(double)options["sweep_deg"],.0001);
        }
        [Test]
        public void CenterArcRejectsInvalidStartAndEndpointWithoutLosingThePrompt()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchCenterPointArcCmd");editor.Pick(new CadPoint(10,15));
            var radius=_workspace.ActiveEntry;editor.Pick(new CadPoint(10,15));editor.Pick(new CadPoint(-99999,-99999));Assert.AreSame(radius,_workspace.ActiveEntry);
            editor.Pick(new CadPoint(20,15));var end=_workspace.ActiveEntry;editor.Pick(new CadPoint(10,15));editor.Pick(new CadPoint(20,15));
            Assert.AreSame(end,_workspace.ActiveEntry);Assert.True(editor.CoordinatePickActive);Assert.False(_workspace.NativeEditor.HasDraft);
            editor.CancelPrompt();end.CommitValue(99,out _);Assert.False(editor.CoordinatePickActive);Assert.False(_workspace.NativeEditor.HasDraft);
        }
        [Test]
        public void CenterArcSkipsMotionOverUiAndResumesTheChosenDirection()
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native.SketchCenterPointArcCmd");
            editor.Pick(new CadPoint(10,15));editor.Pick(new CadPoint(20,15));
            NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10,25));Frame();
            _workspace.UiHitOverride=()=>true;NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10,5));Frame();PressTrigger();ReleaseTrigger();
            Assert.False(_workspace.NativeEditor.HasDraft);Assert.True(editor.CoordinatePickActive);
            _workspace.UiHitOverride=()=>false;Frame();PressTrigger();ReleaseTrigger();Do(CommitIds.Preview);
            Assert.AreEqual(270,(double)_backend.LastOperations[0]["arguments"]["options"]["sweep_deg"],.0001);
        }
        [TestCase(false,0)][TestCase(true,0)][TestCase(false,1)][TestCase(true,1)][TestCase(false,2)][TestCase(true,2)]
        public void EllipsePenUsesAPointOnTheCurveAndPreservesAxisOrder(bool secondLarger,int mode)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchEllipseCmd");Assert.True(editor.Pick(new CadPoint(10,15)));
            var firstField=_workspace.ActiveEntry;
            if(mode==1) {Assert.True(editor.Pick(new CadPoint(10,15)));Assert.AreSame(firstField,_workspace.ActiveEntry);Assert.False(_workspace.NativeEditor.HasDraft);}
            Assert.True(editor.Pick(new CadPoint(22,31))); // length 20, unit direction (.6,.8)
            firstField.CommitValue(99,out _);var secondField=_workspace.ActiveEntry;Assert.True(editor.CoordinatePickActive);
            if(mode==1)
            {
                Assert.True(editor.Pick(new CadPoint(16,23))); // on the first axis
                Assert.True(editor.Pick(new CadPoint(40,55))); // beyond first-axis extent
                Assert.AreSame(secondField,_workspace.ActiveEntry);Assert.False(_workspace.NativeEditor.HasDraft);
            }
            double second=secondLarger ? 40 : 10;
            if(mode==2) {Assert.True(secondField.CommitValue(second,out var reason),reason);}
            else
            {
                // Along = 10, across = b*sqrt(3)/2: the click lies on the ellipse.
                double across=second*System.Math.Sqrt(3)/2;
                NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(16-.8*across,23+.6*across));
                Frame();PressTrigger();ReleaseTrigger();
            }
            Assert.True(_workspace.NativeEditor.HasDraft);Assert.False(editor.CoordinatePickActive);Assert.Null(_workspace.ActiveEntry);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);secondField.CommitValue(99,out _);
            Do(CommitIds.Preview);var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(20,(double)options["major_radius_mm"],.0001);Assert.AreEqual(second,(double)options["minor_radius_mm"],.0001);
            Assert.AreEqual(System.Math.Atan2(.8,.6)*180/System.Math.PI,(double)options["rotation_deg"],.0001);
        }
        [TestCase(false)][TestCase(true)]
        public void CircleRadiusAcceptsPenAndRejectsDegenerateOrExcessiveRadius(bool rejectFirst)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.native.SketchCenterPointCircleCmd");Assert.True(editor.Pick(new CadPoint(10,15)));
            var radiusField=_workspace.ActiveEntry;Assert.True(editor.CoordinatePickActive);
            if(rejectFirst)
            {
                Assert.True(editor.Pick(new CadPoint(10,15)));Assert.True(editor.Pick(new CadPoint(-99999,-99999)));
                Assert.AreSame(radiusField,_workspace.ActiveEntry);Assert.True(editor.CoordinatePickActive);Assert.False(_workspace.NativeEditor.HasDraft);
            }
            NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(13,19));
            Frame();PressTrigger();ReleaseTrigger();Assert.True(_workspace.NativeEditor.HasDraft);Assert.False(editor.CoordinatePickActive);
            Assert.Null(_workspace.ActiveEntry);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            radiusField.CommitValue(99,out _);Do(CommitIds.Preview);
            var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(5,(double)options["radius_mm"],.0001);Assert.AreEqual(10,(double)options["points_mm"][0][0],.0001);
        }
        [TestCaseSource(typeof(NativeSketchTestData),nameof(NativeSketchTestData.PenCreations))]
        public void NativeCreationAcceptsSyntheticPenPointsAndKeepsScalarOptionsNumeric(string command,int count)
        {
            OpenNative();UseInput(Field<UiShell>("_shell"));var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.native."+command);
            if(command=="SketchPolygonCmd") Do("sketch.polygon.inscribed");
            for(int i=0;i<count;i++)
            {
                Assert.True(editor.CoordinatePickActive,command+" point "+i);
                NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(10+10*i,i%2==0 ? 10 : 15));
                Frame();PressTrigger();ReleaseTrigger();Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            }
            if(command=="SketchSplineCmd" || command=="SketchCPSplineCmd") Do("sketch.spline.finish");
            Assert.AreEqual(command=="SketchCenterPointCircleCmd" || command=="SketchEllipseCmd" || command=="SketchCenterPointArcCmd" || command=="SketchCentertoCenterSlotCmd" || command=="SketchOverallLengthSlotCmd" || command=="SketchCenterPointSlotCmd" || command=="SketchThreePointArcSlotCmd" || command=="SketchCenterPointArcSlotCmd",editor.CoordinatePickActive);while(_workspace.ActiveEntry!=null) {var entry=_workspace.ActiveEntry;Assert.True(entry.CommitValue(entry.Value,out var reason),reason);}
            Assert.True(_workspace.NativeEditor.HasDraft);Do(CommitIds.Preview);var points=(JArray)_backend.LastOperations[0]["arguments"]["options"]["points_mm"];
            Assert.AreEqual(count,points.Count);for(int i=0;i<count;i++) Assert.AreEqual(10+10*i,(double)points[i][0],.0001);
        }
        [Test]
        public void MidpointLineRevokesThePendingNumericPromptOnRevisionChange()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.selection.part.Start");editor.Pick(new CadPoint(0,0));
            Do("sketch.line.midpoint.midpoint");Do("sketch.native.SketchMidpointLineWrapperCmd");var old=_workspace.ActiveEntry;Assert.NotNull(old);
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v_next");_workspace.SetDocumentState(_backend.State);
            Assert.IsNull(_workspace.ActiveEntry);old.CommitValue(99,out _);Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)]
        public void MidpointLineUsesOnlyTheChosenPointRolesAndPromptsOnlyNumericInputs(int mode)
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            if((mode&1)!=0){Do("sketch.selection.part.Start");editor.Pick(new CadPoint(0,0));}
            if((mode&2)!=0){Do("sketch.selection.part.End");editor.Pick(new CadPoint(40,0),mode==3);}
            Do("sketch.line.midpoint."+new[] {"coordinates","midpoint","end","both"}[mode]);Do("sketch.native.SketchMidpointLineWrapperCmd");
            if(mode==0) CommitNative(5,6,20,8);else if(mode!=3) CommitNative(10,12);
            Assert.IsNull(_workspace.ActiveEntry);Assert.True(_workspace.NativeEditor.HasDraft);Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("line_midpoint",(string)args["variant"]);
            var options=args["options"];Assert.AreEqual(mode==0 ? 0 : mode==3 ? 2 : 1,(options["references"] as JArray)?.Count??0);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)]
        public void WindowTriggerDragFinishesOrCancelsThroughSyntheticInput(int interruption)
        {
            OpenNative();UseInput();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.selection.window");
            NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(41,-1));
            PressTrigger();Assert.True(editor.WindowDragging);
            NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(-1,1));
            Frame();Assert.Zero(editor.Selected.Count);
            if(interruption==1) _workspace.UiHitOverride=()=>true;
            if(interruption==2) {_frame.PenTracked=false;Poll();}
            if(interruption==3) {_frame.PaletteTracked=false;Poll();}
            if(interruption==4) _workspace.SetOnline(false);
            ReleaseTrigger();Assert.False(editor.WindowActive);Assert.AreEqual(interruption==0 ? 1 : 0,editor.Selected.Count);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);
        }
        [TestCase(false)][TestCase(true)]
        public void WindowDirectionUsesViewerRightDespiteCadXReflection(bool crossing)
        {
            OpenNative();UseInput();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.selection.window");
            NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(crossing ? -1 : 1,-1));PressTrigger();
            NativeSketchTestData.AimWindow(_ray.Origin,_view.transform,editor.Session.Snapshot.Frame,new CadPoint(crossing ? 1 : -1,1));ReleaseTrigger();
            Assert.False(editor.WindowActive);Assert.AreEqual(crossing ? 1 : 0,editor.Selected.Count);
        }
        private void OpenNative()
        { AttachShell(out _); Do("sketch.editor.open"); Pick("Contour"); Assert.NotNull(_workspace.NativeEditor.Snapshot); }
        [Test]
        public void LamieraNativePatternDeletionUsesTheSharedCommitBar()
        {
            _backend.NativePatterns=true;OpenNative();Do("sketch.constraint.pick.ent_pattern");Do("sketch.pattern.edit.delete");
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("pattern_delete",(string)args["variant"]);Assert.AreEqual("ent_contour",(string)args["sketch_id"]);
            Assert.True(Enabled(CommitIds.Apply));
        }
        [Test]
        public void LamieraPatternDeletionRequiresCurrentConstraintReselectionAfterRefresh()
        {
            _backend.NativePatterns=true;OpenNative();Do("sketch.constraint.pick.ent_pattern");Do("sketch.pattern.edit.delete");
            _backend.State=new DocumentState(_backend.State.DocumentId,"r_next","v");_workspace.SetDocumentState(_backend.State);Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.editor.reselect.confirm"));Do("sketch.constraint.pick.ent_pattern");Do("sketch.editor.reselect.confirm");
            Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual("pattern_delete",(string)args["variant"]);Assert.AreEqual("r_next",(string)args["options"]["references"][0]["revision"]);
            Assert.AreEqual("ent_pattern",(string)args["options"]["references"][0]["entity_id"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void LamieraPatternExpressionsUseSharedTextEntryAndExplicitPreview()
        {
            _backend.NativePatterns=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.constraint.pick.ent_pattern");Do("sketch.pattern.edit.expressions");Assert.AreEqual("N_copie",editor.ActiveTextEntry.Value);
            foreach(string value in new[] {"N_copie + 2","90 deg"}) {Assert.True(editor.ActiveTextEntry.Replace(value));Assert.True(editor.ActiveTextEntry.Commit(out _));}
            Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual("ent_contour",(string)args["sketch_id"]);Assert.AreEqual("N_copie + 2",(string)args["options"]["count"]);
            Assert.AreEqual("90 deg",(string)args["options"]["angle_deg"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void LamieraPatternCreationFreezesIndependentYOptionsAndSuppressedElements()
        {
            _backend.NativePatterns=true;_backend.NativeCurves=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.pattern.two");Do("sketch.pattern.natural_y");Do("sketch.pattern.symmetric_y");Do("sketch.pattern.suppressed");
            Assert.True(editor.ActiveTextEntry.Replace("2;4"));Assert.True(editor.ActiveTextEntry.Commit(out _));
            editor.Pick(new CadPoint(105,5));editor.Pick(new CadPoint(20,0),true);editor.Pick(new CadPoint(300,20),true);
            Do("sketch.native.SketchRectangularPatternCmd");CommitNative(3,12,2,8);Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("ent_contour",(string)args["sketch_id"]);
            Assert.False((bool)args["options"]["natural_y"]);Assert.True((bool)args["options"]["symmetric_y"]);Assert.True((bool)args["options"]["natural_x"]);
            CollectionAssert.AreEqual(new[] {2,4},((JArray)args["options"]["suppressed_elements"]).Values<int>().ToArray());Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void LamieraPatternSuppressionEditingAndReactivationUseTheSharedDraft()
        {
            _backend.NativePatterns=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");Do("sketch.constraint.pick.ent_pattern");
            Do("sketch.pattern.edit.suppressed");Assert.True(editor.ActiveTextEntry.Replace("2,3"));Assert.True(editor.ActiveTextEntry.Commit(out _));
            Do(CommitIds.Preview);CollectionAssert.AreEqual(new[] {2,3},((JArray)_backend.LastOperations[0]["arguments"]["options"]["suppressed_elements"]).Values<int>().ToArray());
            Do(CommitIds.Cancel);Do("sketch.pattern.edit.unsuppress");Assert.AreEqual(1,_backend.Previews);Do(CommitIds.Preview);
            Assert.AreEqual(0,((JArray)_backend.LastOperations[0]["arguments"]["options"]["suppressed_elements"]).Count);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeLamieraOffsetUsesTheSharedDraftWithNativeDefaults()
        {
            OpenNative();Field<NativeSketchEditorUi>("_nativeEditor").Pick(new CadPoint(20,0));Do("sketch.offset_distance");CommitNative(8);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("offset_distance",(string)args["variant"]);Assert.AreEqual("ent_contour",(string)args["sketch_id"]);
            Assert.AreEqual(8,(double)args["options"]["distance_mm"]);Assert.True((bool)args["options"]["create_constraints"]);Assert.False((bool)args["options"]["include_connected"]);
        }
        [Test]
        public void NativeLamieraRotateUsesTheSameNativeDraftAndDegreeInput()
        {
            OpenNative();Field<NativeSketchEditorUi>("_nativeEditor").Pick(new CadPoint(20,0));Do("sketch.native.SketchRotateCmd");CommitNative(0,0,90);
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("transform_rotate",(string)args["variant"]);Assert.AreEqual("ent_contour",(string)args["sketch_id"]);
            Assert.AreEqual(90,(double)args["options"]["angle_deg"]);Assert.False((bool)args["options"]["copy"]);
        }
        [Test]
        public void NativeLamieraConstraintDeletionUsesTheSharedDraftAndCommitBar()
        {
            _backend.NativeConstraints=true;OpenNative();Do("sketch.constraint.pick.ent_constraint");Do("sketch.constraint.edit.delete");
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("constraint_delete",(string)args["variant"]);
            Assert.AreEqual("ent_contour",(string)args["sketch_id"]);Assert.True(Enabled(CommitIds.Apply));
        }
        [Test]
        public void NativeLamieraExpressionUsesTheSharedTextEntryAndDraft()
        {
            _backend.NativeDimensions=true;OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Do("sketch.dimension.pick.ent_dimension");Do("sketch.dimension.edit.expression");Assert.Null(_workspace.ArmedField);
            Assert.True(editor.ActiveTextEntry.Replace("2 * 6 mm"));Assert.True(editor.ActiveTextEntry.Commit(out _));
            Assert.AreEqual(0,_backend.Previews);Do(CommitIds.Preview);
            Assert.AreEqual("2 * 6 mm",(string)_backend.LastOperations[0]["arguments"]["options"]["expression"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeLamieraExistingDimensionDeletionUsesTheSharedCommitBar()
        {
            _backend.NativeDimensions=true;OpenNative();Do("sketch.dimension.pick.ent_dimension");Do("sketch.dimension.edit.delete");
            Assert.AreEqual(0,_backend.Previews);Assert.AreEqual(0,_backend.Commits);Do(CommitIds.Preview);
            var args=_backend.LastOperations[0]["arguments"];Assert.AreEqual("ent_contour",(string)args["sketch_id"]);
            Assert.AreEqual("dimension_delete",(string)args["variant"]);Assert.AreEqual("ent_dimension",(string)args["options"]["references"][0]["entity_id"]);
            Assert.True(Enabled(CommitIds.Apply));
        }
        [Test]
        public void NativeLamieraDimensionUsesSameOrderedDraftAndExplicitPreview()
        {
            OpenNative();Field<NativeSketchEditorUi>("_nativeEditor").Pick(new CadPoint(20,0));
            Do("sketch.dimension.vertical");CommitNative(12,18,20);Assert.AreEqual(0,_backend.Previews);
            Do(CommitIds.Preview);var args=_backend.LastOperations[0]["arguments"];
            Assert.AreEqual("ent_contour",(string)args["sketch_id"]);Assert.AreEqual("dimension_vertical",(string)args["variant"]);
            Assert.AreEqual(20,(double)args["options"]["value_mm"]);Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeLamieraGroundUsesTheSameConstraintDraftAndGuard()
        {
            OpenNative(); Field<NativeSketchEditorUi>("_nativeEditor").Pick(new CadPoint(20,0));
            Do("sketch.native.SketchFixConstraintCmd"); Assert.AreEqual(0,_backend.Previews); Do(CommitIds.Preview);
            Assert.AreEqual("constraint_ground",(string)_backend.LastOperations[0]["arguments"]["variant"]);
            Assert.AreEqual("ent_contour",(string)_backend.LastOperations[0]["arguments"]["sketch_id"]);
            Assert.AreEqual(0,_backend.Commits); Assert.True(Enabled(CommitIds.Apply));
        }
        [Test]
        public void NativeLamieraSketchInputContextFollowsWorkspaceVisibilityAndFinish()
        {
            OpenNative(); Assert.True(_workspace.SketchOpen); Assert.True(_workspace.TabState.SketchOpen);
            Assert.AreEqual(InventorXrSo.Core.Input.InputState.SketchOpen,
                InventorXrSo.Core.Input.InputMap.Resolve(_workspace.KeypadOpen,_workspace.InputArmed,_workspace.SketchOpen,false));
            _workspace.Close(); Assert.False(_workspace.SketchOpen); Assert.False(_workspace.TabState.SketchOpen);
            _workspace.Open(); Assert.True(_workspace.SketchOpen);
            Do("sketch.editor.finish"); Assert.False(_workspace.SketchOpen); Assert.False(_workspace.TabState.SketchOpen);
        }
        [TestCase(true,3)][TestCase(true,65)][TestCase(true,120)]
        [TestCase(false,3)][TestCase(false,65)][TestCase(false,120)]
        public void NativePolygonAcceptsDesktopSideRangeAndKeepsInvalidRangePending(bool inscribed,int sides)
        {
            OpenNative(); Do("sketch.native.SketchPolygonCmd"); Do("sketch.polygon."+(inscribed ? "inscribed" : "circumscribed"));
            CommitNative(13,21,25,37); var field=_workspace.ActiveEntry;
            Assert.False(field.CommitValue(2,out _)); Assert.AreSame(field,_workspace.ActiveEntry);
            Assert.False(field.CommitValue(121,out _)); Assert.AreSame(field,_workspace.ActiveEntry);
            Assert.False(_workspace.NativeEditor.HasDraft); Assert.AreEqual(0,_backend.Commits);
            Assert.True(field.CommitValue(sides,out var reason),reason); Assert.True(_workspace.NativeEditor.HasDraft);
            Do(CommitIds.Preview); var options=_backend.LastOperations[0]["arguments"]["options"];
            Assert.AreEqual(JTokenType.Integer,options["sides"].Type); Assert.AreEqual(sides,(int)options["sides"]);
            Assert.AreEqual(inscribed,(bool)options["inscribed"]); Assert.AreEqual(0,_backend.Commits);
            Assert.AreEqual(25d,(double)options["points_mm"][1][0]); Assert.AreEqual(37d,(double)options["points_mm"][1][1]);
        }
        private void CommitNative(params double[] values)
        {
            foreach (var value in values)
            { var entry=_workspace.ActiveEntry; Assert.NotNull(entry); Assert.True(entry.CommitValue(value,out var reason),reason); }
        }
        [Test]
        public void NativeLamieraFormatUsesTheSharedSelectionAndCommitBar()
        {
            OpenNative(); var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            Assert.True(editor.Pick(new CadPoint(20,0))); Do("sketch.native.SketchConstructionCmd");
            Assert.AreEqual(0,_backend.Commits); Do(CommitIds.Preview);
            Assert.AreEqual("format_construction",(string)_backend.LastOperations[0]["arguments"]["variant"]);
            Assert.AreEqual("ent_contour",(string)_backend.LastOperations[0]["arguments"]["options"]["references"][0]["sketch_id"]);
            Assert.True(Enabled(CommitIds.Apply)); Do(CommitIds.Cancel); Assert.False(_workspace.NativeEditor.HasDraft);
        }
        [Test]
        public void NativeLamieraCreationUsesTheSameMutationGuardAndRemainsOpenAfterApply()
        {
            OpenNative(); Assert.AreSame(_workspace.Session,_workspace.NativeEditor.Mutation); Assert.False(Enabled(FlangeAction));
            Do("sketch.native.SketchLineCmd"); CommitNative(0,0,10,10); Do(CommitIds.Preview);
            Assert.True(Enabled(CommitIds.Apply)); Assert.AreEqual("edit_sketch",(string)_backend.LastOperations[0]["command"]);
            Do(CommitIds.Apply); Assert.AreEqual(1,_backend.Commits); Assert.AreEqual("ent_contour",_workspace.NativeEditor.SketchId);
            Assert.AreEqual("r2",_workspace.NativeEditor.Snapshot.State.Revision); Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.False(Enabled("sketch.native.SketchLineCmd"));
            var current=NativeSketchTestData.Scene(_backend.State,_backend.Model); _view.Show(current); _workspace.SetScene(current);
            Assert.True(Enabled("sketch.native.SketchLineCmd"));
            Do("sketch.editor.finish"); Assert.True(Enabled(FlangeAction));
        }
        [Test]
        public void NativeLamieraReconnectionPreservesOperationWithoutReplayingPlan()
        {
            OpenNative(); Do("sketch.native.SketchLineCmd"); CommitNative(0,0,10,10); Do(CommitIds.Preview);
            _workspace.Bind(null,null); _workspace.Bind(_backend,_backend); _workspace.SetScene(_scene); _workspace.SetOnline(true);
            Assert.True(_workspace.NativeEditor.HasDraft); Assert.False(Enabled(CommitIds.Apply));
            Do("sketch.editor.refresh"); Do("sketch.editor.confirm"); Do(CommitIds.Preview);
            Assert.True(Enabled(CommitIds.Apply)); Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeLamieraPartialSnapshotAndBackFromCoordinatesCannotMutate()
        {
            OpenNative(); Do("sketch.native.SketchLineCmd"); CommitNative(0);
            _workspace.Back(); Assert.Null(_workspace.ActiveEntry); Assert.False(_workspace.NativeEditor.HasDraft);
            _backend.NativePartial=true; Do("sketch.editor.refresh");
            Assert.False(Enabled("sketch.native.SketchLineCmd")); Assert.AreEqual(0,_backend.Commits);
        }
        [Test]
        public void NativeLamieraLostCommitResponseRequiresReviewBeforeFurtherCommands()
        {
            OpenNative(); Do("sketch.native.SketchLineCmd"); CommitNative(0,0,10,10); Do(CommitIds.Preview);
            _backend.CommitError=new System.IO.IOException("Lost response"); Do(CommitIds.Apply);
            Assert.True(_workspace.NativeEditor.Mutation.CommitOutcomeUnknown); Assert.False(_workspace.NativeEditor.HasDraft);
            Assert.False(Enabled("sketch.native.SketchLineCmd")); Do(CommitIds.Recover);
            Assert.AreEqual(1,_backend.Commits); Assert.True(Enabled("sketch.native.SketchLineCmd"));
        }
    }
}
