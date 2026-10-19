using System;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Tests
{
    public class NativeSketchConstraintLabelViewTests
    {
        private GameObject _model;
        private readonly SketchFrame _frame = new SketchFrame(new CadPoint(10,20,30),new CadPoint(1,0,0),new CadPoint(0,1,0));
        [SetUp] public void Setup() => _model = new GameObject("constraint model");
        [TearDown] public void Cleanup() { if(_model != null) UnityEngine.Object.DestroyImmediate(_model); }
        private NativeSketchConstraintLabelView Create(string text = "Fissaggio") => NativeSketchConstraintLabelView.Create(
            _model.transform,_model.transform,_frame,new CadPoint(3,4),text);
        private static Vector3[] Corners(NativeSketchConstraintLabelView view)
        { var corners = new Vector3[4]; ((RectTransform)view.transform).GetWorldCorners(corners); return corners; }

        [TestCase(.25f)][TestCase(1f)][TestCase(4f)]
        public void PhysicalRectangleAndCadFootprintRemainCoherentUnderModelScaleAndRotation(float scale)
        {
            _model.transform.SetPositionAndRotation(new Vector3(2,3,4),Quaternion.Euler(25,45,10));
            _model.transform.localScale = Vector3.one*scale;
            var view = Create(); var corners = Corners(view);
            Assert.True(view.IsUsable);
            Assert.AreEqual(.048f,Vector3.Distance(corners[0],corners[3]),.000001f);
            Assert.AreEqual(.012f,Vector3.Distance(corners[0],corners[1]),.000001f);
            var expected = _model.transform.TransformPoint(CadCoordinates.ToLocal(_frame.ToModel(new CadPoint(3,4))));
            Assert.Less(Vector3.Distance(expected,view.transform.position),.000001f);
            Assert.True(view.TryGetSketchFootprint(out var minimum,out var maximum));
            Assert.AreEqual(48/scale,maximum.X-minimum.X,.003);
            Assert.AreEqual(12/scale,maximum.Y-minimum.Y,.003);
            Assert.AreEqual(3,(minimum.X+maximum.X)/2,.003);
            Assert.AreEqual(4,(minimum.Y+maximum.Y)/2,.003);
        }

        [Test]
        public void AlignedNonUniformScaleKeepsPhysicalDimensionsWithDifferentCadAxisFootprints()
        {
            _model.transform.SetPositionAndRotation(new Vector3(1,2,3),Quaternion.Euler(15,30,45));
            _model.transform.localScale = new Vector3(2,3,4);
            var view = Create(); var corners = Corners(view);
            Assert.True(view.IsUsable);
            Assert.AreEqual(.048f,Vector3.Distance(corners[0],corners[3]),.000001f);
            Assert.AreEqual(.012f,Vector3.Distance(corners[0],corners[1]),.000001f);
            Assert.True(view.TryGetSketchFootprint(out var minimum,out var maximum));
            Assert.AreEqual(24,maximum.X-minimum.X,.003);
            Assert.AreEqual(4,maximum.Y-minimum.Y,.003);
            Assert.AreEqual(3,(minimum.X+maximum.X)/2,.003);
            Assert.AreEqual(4,(minimum.Y+maximum.Y)/2,.003);
            var normal = Vector3.Cross(corners[3]-corners[0],corners[1]-corners[0]).normalized;
            Assert.True(view.TryHit(new Ray(view.transform.position+normal,-normal),out float distance,out _));
            Assert.AreEqual(1,distance,.000001f);
        }

        [Test]
        public void SmallUniformScaleDoesNotCollapseValidMillimetreAxesDuringNormalization()
        {
            _model.transform.localScale = Vector3.one*.001f;
            var view = Create(); var corners = Corners(view);
            Assert.True(view.IsUsable);
            Assert.AreEqual(.048f,Vector3.Distance(corners[0],corners[3]),.000001f);
            Assert.AreEqual(.012f,Vector3.Distance(corners[0],corners[1]),.000001f);
            var expectedRight = _model.transform.TransformVector(CadCoordinates.ToLocal(_frame.XAxis));
            var expectedUp = _model.transform.TransformVector(CadCoordinates.ToLocal(_frame.YAxis));
            expectedRight /= Mathf.Sqrt(expectedRight.sqrMagnitude); expectedUp /= Mathf.Sqrt(expectedUp.sqrMagnitude);
            Assert.AreEqual(1,Vector3.Dot((corners[3]-corners[0]).normalized,expectedRight),.000001f);
            Assert.AreEqual(1,Vector3.Dot((corners[1]-corners[0]).normalized,expectedUp),.000001f);
            Assert.True(view.TryGetSketchFootprint(out var minimum,out var maximum));
            Assert.Greater(maximum.X,minimum.X); Assert.Greater(maximum.Y,minimum.Y);
            var normal = Vector3.Cross(expectedRight,expectedUp);
            Assert.True(view.TryHit(new Ray(view.transform.position+normal*.5f,-normal),out float distance,out var hit));
            Assert.AreEqual(.5f,distance,.000001f);
            Assert.Less(Vector3.Distance(hit,view.transform.position),.000001f);
        }

        [Test]
        public void DistinctParentUsesModelCoordinatesAndDisposalLeavesBothOwnersIntact()
        {
            var parent = new GameObject("separate label parent");
            try
            {
                parent.transform.SetPositionAndRotation(new Vector3(-2,3,1),Quaternion.Euler(30,70,10));
                parent.transform.localScale = Vector3.one*.7f;
                _model.transform.SetPositionAndRotation(new Vector3(2,1,3),Quaternion.Euler(20,15,80));
                _model.transform.localScale = Vector3.one*2;
                var view = NativeSketchConstraintLabelView.Create(parent.transform,_model.transform,_frame,new CadPoint(3,4),"Parallelo");
                Assert.AreSame(parent.transform,view.transform.parent);
                Assert.True(view.IsUsable);
                var expected = _model.transform.TransformPoint(CadCoordinates.ToLocal(_frame.ToModel(new CadPoint(3,4))));
                Assert.Less(Vector3.Distance(expected,view.transform.position),.000001f);
                var corners = Corners(view);
                Assert.AreEqual(.048f,Vector3.Distance(corners[0],corners[3]),.000001f);
                Assert.AreEqual(.012f,Vector3.Distance(corners[0],corners[1]),.000001f);
                parent.transform.SetPositionAndRotation(new Vector3(4,-2,1),Quaternion.Euler(70,20,40));
                view.Refresh();
                Assert.Less(Vector3.Distance(expected,view.transform.position),.000001f);
                Assert.True(view.TryGetSketchFootprint(out var minimum,out var maximum));
                Assert.AreEqual(24,maximum.X-minimum.X,.003); Assert.AreEqual(6,maximum.Y-minimum.Y,.003);
                var normal = view.transform.forward;
                Assert.True(view.TryHit(new Ray(expected+normal,-normal),out _,out _));
                view.Dispose();
                Assert.True(view == null); Assert.NotNull(parent); Assert.NotNull(_model);
                Assert.IsEmpty(parent.GetComponentsInChildren<Canvas>());
                Assert.IsEmpty(_model.GetComponentsInChildren<Canvas>());
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); }
        }

        [Test]
        public void ObliqueNonUniformParentShearRefusesRenderingFootprintAndHitThenRecovers()
        {
            var parent = new GameObject("sheared label parent");
            parent.transform.SetParent(_model.transform,false);
            parent.transform.localRotation = Quaternion.Euler(0,0,45);
            parent.transform.localScale = new Vector3(2,1,1);
            var view = NativeSketchConstraintLabelView.Create(parent.transform,_model.transform,_frame,new CadPoint(3,4),"Levigato G2");
            var label = view.GetComponentInChildren<TextMeshProUGUI>();
            Assert.False(view.IsUsable); Assert.False(view.GetComponent<Canvas>().enabled);
            Assert.False(view.TryGetSketchFootprint(out _,out _));
            Assert.False(view.TryHit(new Ray(view.transform.position+Vector3.forward,Vector3.back),out _,out _));
            parent.transform.localScale = Vector3.one;
            view.Refresh();
            Assert.True(view.IsUsable); Assert.True(view.GetComponent<Canvas>().enabled);
            Assert.AreSame(label,view.GetComponentInChildren<TextMeshProUGUI>());
            Assert.True(view.TryGetSketchFootprint(out var minimum,out var maximum));
            Assert.AreEqual(48,maximum.X-minimum.X,.003); Assert.AreEqual(12,maximum.Y-minimum.Y,.003);
            var normal = view.transform.forward;
            Assert.True(view.TryHit(new Ray(view.transform.position+normal,-normal),out _,out _));
        }

        [Test]
        public void HitTestUsesTheActualVisibleRectangleAndRejectsMissBehindParallelAndZeroDirection()
        {
            _model.transform.rotation = Quaternion.Euler(40,10,70);
            var view = Create(); var corners = Corners(view);
            var centre = view.transform.position; var normal = Vector3.Cross(corners[3]-corners[0],corners[1]-corners[0]).normalized;
            Assert.True(view.TryHit(new Ray(centre+normal*.5f,-normal),out float distance,out var hit));
            Assert.AreEqual(.5f,distance,.000001f); Assert.Less(Vector3.Distance(centre,hit),.000001f);
            var outside = centre+(corners[3]-corners[0]).normalized*.025f;
            Assert.False(view.TryHit(new Ray(outside+normal*.5f,-normal),out _,out _));
            Assert.False(view.TryHit(new Ray(centre+normal*.5f,normal),out _,out _));
            Assert.False(view.TryHit(new Ray(centre+normal*.5f,view.transform.right),out _,out _));
            Assert.False(view.TryHit(new Ray(centre,Vector3.zero),out _,out _));
        }

        [Test]
        public void LabelStaysOnSketchPlaneAndNeverFollowsAnUnrelatedHeadPose()
        {
            var frame = new SketchFrame(new CadPoint(1,2,3),new CadPoint(0,1,0),new CadPoint(0,0,1));
            var view = NativeSketchConstraintLabelView.Create(_model.transform,_model.transform,frame,new CadPoint(4,5),"Coincidente");
            var rotation = view.transform.rotation; var corners = Corners(view);
            var head = new GameObject("head");
            try
            {
                head.transform.SetPositionAndRotation(new Vector3(9,8,7),Quaternion.Euler(90,45,30)); view.Refresh();
                Assert.Less(Quaternion.Angle(rotation,view.transform.rotation),.0001f);
                foreach(var corner in corners)
                {
                    var modelPoint = CadCoordinates.FromWorld(_model.transform,corner);
                    Assert.AreEqual(0,(modelPoint-frame.OriginMm).Dot(frame.Normal),.0001);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(head); }
        }

        [Test]
        public void LongMultilineLabelKeepsCompleteTextWithBoundedEllipsisPaddingAndNoInputInterception()
        {
            string full = "Vincolo\n"+new string('W',1000)+"\rFine";
            var view = Create(full); var text = view.GetComponentInChildren<TextMeshProUGUI>();
            Assert.AreEqual(full,view.FullText); Assert.False(text.text.Contains('\n')); Assert.False(text.text.Contains('\r'));
            Assert.AreEqual(TextWrappingModes.NoWrap,text.textWrappingMode);
            Assert.AreEqual(TextOverflowModes.Ellipsis,text.overflowMode);
            Assert.False(text.enableAutoSizing); Assert.False(text.richText);
            Assert.AreEqual(new Vector2(2,2),text.rectTransform.offsetMin);
            Assert.AreEqual(new Vector2(-2,-2),text.rectTransform.offsetMax);
            Assert.AreEqual(7,UiTypography.CapHeight(text),.00001f);
            Assert.NotNull(view.GetComponent<RectMask2D>());
            Assert.True(view.GetComponentsInChildren<Graphic>().All(g=>!g.raycastTarget));
            Assert.IsEmpty(view.GetComponentsInChildren<Collider>());
            Assert.IsEmpty(view.GetComponentsInChildren<GraphicRaycaster>());
        }

        [Test]
        public void ContentAnchorAndScaleUpdatesReuseObjectsAndRefreshHitBounds()
        {
            var view = Create(); var text = view.GetComponentInChildren<TextMeshProUGUI>();
            int childCount = view.GetComponentsInChildren<Transform>().Length;
            view.SetContent("Allineamento orizzontale",new Vector2(60,16),3,8);
            view.SetAnchor(_frame,new CadPoint(100,200));
            _model.transform.localScale = Vector3.one*2; view.Refresh();
            Assert.AreSame(text,view.GetComponentInChildren<TextMeshProUGUI>());
            Assert.AreEqual(childCount,view.GetComponentsInChildren<Transform>().Length);
            Assert.AreEqual("Allineamento orizzontale",view.FullText);
            Assert.True(view.TryGetSketchFootprint(out var minimum,out var maximum));
            Assert.AreEqual(30,maximum.X-minimum.X,.001); Assert.AreEqual(8,maximum.Y-minimum.Y,.001);
            Assert.AreEqual(100,(minimum.X+maximum.X)/2,.001);
            Assert.AreEqual(200,(minimum.Y+maximum.Y)/2,.001);
            var normal = view.transform.forward;
            Assert.True(view.TryHit(new Ray(view.transform.position+normal,-normal),out _,out _));
        }

        [Test]
        public void DegenerateScaleDisablesRenderingAndHitsThenRecoversWithoutRecreation()
        {
            var view = Create(); var text = view.GetComponentInChildren<TextMeshProUGUI>();
            _model.transform.localScale = new Vector3(0,1,1); view.Refresh();
            Assert.False(view.IsUsable); Assert.False(view.GetComponent<Canvas>().enabled);
            Assert.False(view.TryHit(new Ray(Vector3.forward,Vector3.back),out _,out _));
            Assert.False(view.TryGetSketchFootprint(out _,out _));
            _model.transform.localScale = Vector3.one; view.Refresh();
            Assert.True(view.IsUsable); Assert.AreSame(text,view.GetComponentInChildren<TextMeshProUGUI>());
            view.gameObject.SetActive(false);
            Assert.False(view.TryHit(new Ray(view.transform.position+view.transform.forward,-view.transform.forward),out _,out _));
        }

        [Test]
        public void InvalidContentCannotPartiallyChangeAViewAndDisposalRemovesIt()
        {
            var view = Create(); var original = view.FullText;
            Assert.Throws<ArgumentOutOfRangeException>(()=>view.SetContent("invalid",new Vector2(float.NaN,12)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>view.SetContent("invalid",new Vector2(10,10),5));
            Assert.Throws<ArgumentException>(()=>view.SetContent(new string('x',2049),new Vector2(48,12)));
            Assert.AreEqual(original,view.FullText);
            view.Dispose();
            Assert.True(view == null); Assert.IsEmpty(_model.GetComponentsInChildren<Canvas>());
        }
    }
}
