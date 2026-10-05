using System.Linq;
using InventorXrSo.Core.Selection;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace InventorXrSo.Tests
{
    /// <summary>Selection rim, hover rim, face outline and bounds: what makes the selection readable on the headset.</summary>
    public class SelectionHighlightTests
    {
        private GameObject _root;
        private CadSceneView _view;
        private SelectionVisuals _visuals;
        private Material _body, _occurrence, _face;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            _body = new Material(shader); _occurrence = new Material(shader); _face = new Material(shader);
            _root = new GameObject("scene");
            _view = _root.AddComponent<CadSceneView>();
            _view.BodyMaterial = _body;
            _view.Show(CadSceneViewTests.BoltScene());
            _visuals = _root.AddComponent<SelectionVisuals>();
            _visuals.Configure(_view, _occurrence, _face);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_body);
            Object.DestroyImmediate(_occurrence);
            Object.DestroyImmediate(_face);
        }

        private int Count(string name, GameObject within = null)
            => (within ?? _root).GetComponentsInChildren<Transform>(true).Count(t => t.name == name);

        [Test]
        public void ConfigureAppliesTheSemanticSelectionColours()
        {
            var occurrence = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var face = new Material(Shader.Find("XrSo/HighlightOverlay"));
            try
            {
                _visuals.Configure(_view, occurrence, face);
                Assert.AreEqual(UiTheme.Selection.r, occurrence.GetColor("_BaseColor").r, 0.01f);
                Assert.AreEqual(UiTheme.Selection.b, occurrence.GetColor("_BaseColor").b, 0.01f);
                var fill = face.GetColor("_Color");
                Assert.AreEqual(UiTheme.Selection.g, fill.g, 0.01f);
                Assert.AreEqual(UiTheme.SelectionFaceAlpha, fill.a, 0.01f);
            }
            finally { Object.DestroyImmediate(occurrence); Object.DestroyImmediate(face); }
        }

        [Test]
        public void ASelectedOccurrenceGetsARimAroundEachBodyAndNothingElseDoes()
        {
            _visuals.Show(new Selection(SelectionKind.Occurrence, "ent_occ_2", null, "ent_occ_2"));
            Assert.AreEqual(1, _visuals.RimCount);
            var selected = _view.Find("ent_occ_2").Bodies[0];
            var rim = selected.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "SelectionRim");
            Assert.AreSame(_visuals.RimMaterial, rim.sharedMaterial);
            Assert.AreSame(selected.Mesh, rim.GetComponent<MeshFilter>().sharedMesh, "same mesh, no copy");
            Assert.AreEqual(ShadowCastingMode.Off, rim.shadowCastingMode);
            Assert.AreEqual(0, Count("SelectionRim", _view.Find("ent_occ_1").gameObject));
            _visuals.Show(Selection.None);
            Assert.AreEqual(0, _visuals.RimCount);
            Assert.AreEqual(0, Count("SelectionRim"));
        }

        [Test]
        public void TheRimIsAnInvertedHullInTheOutlineColourAndWiderThanTheHoverRim()
        {
            var rim = _visuals.RimMaterial; var hover = _visuals.HoverMaterial;
            Assert.AreEqual("XrSo/HighlightOverlay", rim.shader.name, "reuses the existing Android-safe overlay shader");
            Assert.AreEqual((float)CullMode.Front, rim.GetFloat("_Cull"));
            Assert.AreEqual(UiTheme.SelectionRimWidth, rim.GetFloat("_Expand"), 1e-6f);
            Assert.AreEqual(UiTheme.SelectionOutline.r, rim.GetColor("_Color").r, 0.01f);
            Assert.AreEqual(1f, rim.GetColor("_Color").a, 1e-3f, "the selection rim is opaque");
            Assert.AreEqual(UiTheme.SelectionHoverRimWidth, hover.GetFloat("_Expand"), 1e-6f);
            Assert.Less(hover.GetFloat("_Expand"), rim.GetFloat("_Expand"), "hover is thinner");
            Assert.AreEqual(UiTheme.SelectionHover.g, hover.GetColor("_Color").g, 0.01f, "hover is lighter");
            Assert.Less(hover.GetColor("_Color").a, 1f);
            Assert.AreNotSame(rim, hover);
        }

        [Test]
        public void TheOverlayShaderStillDefaultsToTheOldBehaviour()
        {
            // Ghosts, preview and verify overlays create their material from the same shader: they must keep Cull Back and no expansion.
            var material = new Material(Shader.Find("XrSo/HighlightOverlay"));
            try
            {
                Assert.AreEqual((float)CullMode.Back, material.GetFloat("_Cull"));
                Assert.AreEqual(0f, material.GetFloat("_Expand"));
                Assert.IsTrue(material.shader.isSupported);
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void SelectingAgainReusesTheSharedMaterialsAndPoolsTheHoverObject()
        {
            var rim = _visuals.RimMaterial; var hover = _visuals.HoverMaterial; var line = _visuals.OutlineMaterial;
            for (int i = 0; i < 3; i++)
            {
                _visuals.Show(new Selection(SelectionKind.Occurrence, "ent_occ_1", null, "ent_occ_1"));
                _visuals.Show(new Selection(SelectionKind.Face, "ent_occ_1", "ent_doc_bolt_f4", "proxy"));
                _visuals.Hover(_view.Find("ent_occ_2").Bodies[0]);
                _visuals.Hover(_view.Find("ent_occ_1").Bodies[0]);
            }
            Assert.AreSame(rim, _visuals.RimMaterial);
            Assert.AreSame(hover, _visuals.HoverMaterial);
            Assert.AreSame(line, _visuals.OutlineMaterial);
            Assert.AreEqual(0, _visuals.RimCount, "the face selection replaced the occurrence rim");
            Assert.AreEqual(1, Count("HoverRim"), "one pooled hover object, never one per call");
            Assert.AreEqual(1, Count("FaceHighlight"));
        }

        [Test]
        public void ASelectedFaceGetsAClosedBoundaryLineInTheOutlineColour()
        {
            _visuals.Show(new Selection(SelectionKind.Face, "ent_occ_1", "ent_doc_bolt_f4", "proxy"));
            Assert.AreEqual(1, _visuals.OutlineCount);
            var line = _view.Find("ent_occ_1").GetComponentInChildren<LineRenderer>();
            Assert.AreEqual(5, line.positionCount, "four edges of the square face, first point repeated");
            Assert.AreSame(_visuals.OutlineMaterial, line.sharedMaterial);
            Assert.AreEqual(UiTheme.SelectionLineWidth, line.widthMultiplier, 1e-6f);
            Assert.AreEqual(UiTheme.SelectionOutline.b, _visuals.OutlineMaterial.GetColor("_Color").b, 0.01f);
            Assert.AreEqual((float)CullMode.Off, _visuals.OutlineMaterial.GetFloat("_Cull"));
            Assert.AreEqual(line.GetPosition(0), line.GetPosition(4));
            _visuals.Clear();
            Assert.AreEqual(0, _visuals.OutlineCount);
            Assert.IsNull(_view.Find("ent_occ_1").GetComponentInChildren<LineRenderer>());
        }

        [Test]
        public void FaceBoundaryIgnoresInteriorEdgesAndWeldsDuplicatedVertices()
        {
            var range = _view.Find("ent_occ_1").Bodies[0].Primitive.FaceMap.Find("ent_doc_bolt_f4");
            var chains = MeshFactory.FaceBoundary(_view.Find("ent_occ_1").Bodies[0].Mesh, range, out var normal);
            Assert.AreEqual(1, chains.Count);
            Assert.AreEqual(5, chains[0].Length, "the diagonal shared by the two triangles is not boundary");
            Assert.AreEqual(1f, normal.magnitude, 1e-4f);
            Assert.AreEqual(Mathf.Abs(normal.y), 1f, 1e-4f, "the top face looks along Y");
        }

        [Test]
        public void HoverShowsALighterRimOnAnUnselectedBodyOnly()
        {
            var body1 = _view.Find("ent_occ_1").Bodies[0]; var body2 = _view.Find("ent_occ_2").Bodies[0];
            Assert.IsFalse(_visuals.HoverVisible);
            _visuals.Hover(body1);
            Assert.IsTrue(_visuals.HoverVisible);
            var hover = body1.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "HoverRim");
            Assert.AreSame(_visuals.HoverMaterial, hover.sharedMaterial);
            Assert.AreNotSame(_visuals.RimMaterial, hover.sharedMaterial, "hover and selected must look different");
            _visuals.Show(new Selection(SelectionKind.Occurrence, "ent_occ_1", null, "ent_occ_1"));
            Assert.IsFalse(_visuals.HoverVisible, "a selected body shows the selection rim, not the hover one");
            _visuals.Hover(body2);
            Assert.IsTrue(_visuals.HoverVisible);
            Assert.AreEqual(1, Count("HoverRim", body2.gameObject));
            Assert.AreEqual(0, Count("HoverRim", body1.gameObject), "the pooled object moved");
            _visuals.Hover(null);
            Assert.IsFalse(_visuals.HoverVisible);
            _visuals.Hover(body2);
            _view.Show(CadSceneViewTests.BoltScene());
            Assert.IsFalse(_visuals.HoverVisible, "a rebuilt scene drops the hover");
            _visuals.Hover(_view.Find("ent_occ_1").Bodies[0]);
            Assert.IsTrue(_visuals.HoverVisible, "hover works again after a rebuild");
        }

        [Test]
        public void HoverReturnsWhenTheSelectionIsCleared()
        {
            var body = _view.Find("ent_occ_1").Bodies[0];
            _visuals.Show(new Selection(SelectionKind.Occurrence, "ent_occ_1", null, "ent_occ_1"));
            _visuals.Hover(body);
            Assert.IsFalse(_visuals.HoverVisible);
            _visuals.Show(Selection.None);
            Assert.IsTrue(_visuals.HoverVisible, "the body is no longer selected, the ray is still on it");
        }

        [Test]
        public void TheHighlightBoundsFollowTheSelectedBodyOrFace()
        {
            Assert.IsFalse(_visuals.TryGetBounds(out _));
            _visuals.Show(new Selection(SelectionKind.Occurrence, "ent_occ_2", null, "ent_occ_2"));
            Assert.IsTrue(_visuals.TryGetBounds(out var body));
            Assert.AreEqual(0.01f, body.size.x, 1e-4f);
            Assert.AreEqual(-0.035f, body.center.x, 1e-4f);
            _visuals.Show(new Selection(SelectionKind.Face, "ent_occ_1", "ent_doc_bolt_f4", "proxy"));
            Assert.IsTrue(_visuals.TryGetBounds(out var face));
            Assert.AreEqual(0.01f, face.size.x, 1e-4f);
            Assert.AreEqual(0f, face.size.y, 1e-4f, "a planar face is flat");
            _visuals.Clear();
            Assert.IsFalse(_visuals.TryGetBounds(out _));
        }

        [Test]
        public void CurrentTracksTheDrawnSelection()
        {
            Assert.AreEqual(SelectionKind.None, _visuals.Current.Kind);
            var face = new Selection(SelectionKind.Face, "ent_occ_1", "ent_doc_bolt_f4", "proxy");
            _visuals.Show(face);
            Assert.AreSame(face, _visuals.Current);
            _visuals.Clear();
            Assert.AreEqual(SelectionKind.None, _visuals.Current.Kind);
        }

        [Test]
        public void RimsAreCappedForHugeSelections()
        {
            var ids = Enumerable.Repeat("ent_occ_1", SelectionVisuals.MaxRimBodies + 10).ToList();
            _visuals.ShowOccurrences(ids);
            Assert.AreEqual(SelectionVisuals.MaxRimBodies, _visuals.RimCount);
        }
    }
}
