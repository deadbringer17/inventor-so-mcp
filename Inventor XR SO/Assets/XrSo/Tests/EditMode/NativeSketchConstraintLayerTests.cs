using System;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class NativeSketchConstraintLayerTests
    {
        private GameObject _model;
        [SetUp] public void Setup() => _model = new GameObject("constraint layer model");
        [TearDown] public void Cleanup() { if(_model != null) UnityEngine.Object.DestroyImmediate(_model); }
        private static JObject Constraint(string id,string nativeType = "kGroundConstraintObject") => new JObject
        {
            ["id"] = id, ["type"] = "constraint", ["native_type"] = nativeType, ["supported"] = true,
            ["annotation_mm"] = new JArray(0,0), ["binding_state"] = "unavailable"
        };
        private static NativeSketchSnapshot Snapshot(JObject[] constraints,string revision = "r1",string document = "part",string sketch = "ent_sketch")
            => NativeSketchSnapshot.Parse(new JObject
            {
                ["schema_version"] = 1, ["document_id"] = document, ["revision"] = revision,
                ["sketch_id"] = sketch, ["sketch_name"] = "Native",
                ["frame"] = new JObject { ["origin_mm"] = new JArray(0,0,0), ["x_axis"] = new JArray(1,0,0), ["y_axis"] = new JArray(0,1,0) },
                ["entities"] = new JArray(), ["dimensions"] = new JArray(), ["constraints"] = new JArray(constraints), ["partial"] = false
            },new DocumentState(document,revision,"visual"),sketch);
        private NativeSketchConstraintLayer Create() => NativeSketchConstraintLayer.Create(_model.transform,_model.transform);
        private static Ray RayAt(NativeSketchConstraintLabelView label) => new Ray(label.transform.position+label.transform.forward,-label.transform.forward);

        [Test]
        public void SynchronizationReusesLabelsAndRefreshesReferenceRevisionWithoutAcceptingOldSnapshot()
        {
            var layer = Create(); var first = Snapshot(new[] {Constraint("ent_a")});
            layer.Synchronize(first);
            Assert.True(layer.TryGetLabel("ent_a",out var label)); Assert.AreEqual(1,layer.VisibleCount);
            int created = layer.CreatedLabelCount;
            layer.Synchronize(first);
            Assert.AreEqual(created,layer.CreatedLabelCount);
            var second = Snapshot(new[] {Constraint("ent_a")},"r2"); layer.Synchronize(second);
            Assert.True(layer.TryGetLabel("ent_a",out var reused)); Assert.AreSame(label,reused);
            Assert.AreEqual(created,layer.CreatedLabelCount);
            Assert.AreEqual("r2",layer.PickTargets(second,RayAt(label)).Single().Revision);
            Assert.IsEmpty(layer.PickTargets(first,RayAt(label)));
            Assert.IsEmpty(layer.PickTargets(Snapshot(new[] {Constraint("ent_a")},"r2"),RayAt(label)));
        }

        [TestCase(.5f)][TestCase(2f)]
        public void ActualPhysicalFootprintSeparatesSharedAnchorLabelsAndTracksModelScale(float scale)
        {
            _model.transform.localScale = Vector3.one*scale;
            _model.transform.rotation = Quaternion.Euler(20,30,40);
            var layer = Create(); var snapshot = Snapshot(new[] {Constraint("ent_a"),Constraint("ent_b"),Constraint("ent_c")});
            layer.Synchronize(snapshot);
            Assert.AreEqual(3,layer.VisibleCount);
            Assert.AreEqual(48/scale,layer.Layout.Options.WidthMm,.001);
            Assert.AreEqual(12/scale,layer.Layout.Options.HeightMm,.001);
            foreach(var glyph in layer.Layout.Entries)
            {
                Assert.False(glyph.MayOverlap); Assert.True(layer.TryGetLabel(glyph.Reference.EntityId,out var label));
                Assert.True(label.TryGetSketchFootprint(out var minimum,out var maximum));
                Assert.AreEqual(layer.Layout.Options.WidthMm,maximum.X-minimum.X,.001);
                Assert.AreEqual(layer.Layout.Options.HeightMm,maximum.Y-minimum.Y,.001);
                Assert.AreEqual(glyph.Reference.EntityId,layer.PickTargets(snapshot,RayAt(label)).Single().EntityId);
            }
            int created = layer.CreatedLabelCount;
            _model.transform.localScale = Vector3.one*(scale*2);
            // Picking invalidates transforms even before the next LateUpdate.
            Assert.True(layer.TryGetLabel("ent_a",out var selected)); layer.PickTargets(snapshot,RayAt(selected));
            Assert.AreEqual(24/scale,layer.Layout.Options.WidthMm,.001);
            Assert.AreEqual(created,layer.CreatedLabelCount);
        }

        [Test]
        public void FiltersBudgetAndSelectedConstraintControlOnlyActuallyVisibleHits()
        {
            var layer = Create(); var snapshot = Snapshot(new[] {Constraint("ent_a"),Constraint("ent_b","kParallelConstraintObject"),Constraint("ent_c")});
            layer.Synchronize(snapshot,maxVisible:1);
            Assert.AreEqual(1,layer.VisibleCount);
            Assert.AreEqual(2,layer.Layout.Entries.Count(e=>e.State == NativeSketchConstraintGlyphState.OmittedByBudget));
            layer.Synchronize(snapshot,selected:snapshot.Constraints[1].Reference,showAll:false,nativeTypes:Array.Empty<string>(),maxVisible:1);
            Assert.True(layer.Layout.SelectedReferenceFound); Assert.AreEqual(1,layer.VisibleCount);
            Assert.True(layer.TryGetLabel("ent_b",out var chosen));
            Assert.AreEqual("ent_b",layer.PickTargets(snapshot,RayAt(chosen)).Single().EntityId);
            Assert.True(layer.TryGetLabel("ent_a",out var hidden)); Assert.False(hidden.gameObject.activeSelf);
            var selectedPosition = chosen.transform.position;
            layer.Synchronize(snapshot,showAll:false);
            Assert.Zero(layer.VisibleCount);
            Assert.IsEmpty(layer.PickTargets(snapshot,new Ray(selectedPosition+Vector3.forward,Vector3.back)));
            layer.Synchronize(snapshot,nativeTypes:new[] {"kParallelConstraintObject"});
            Assert.AreEqual(1,layer.VisibleCount); Assert.AreEqual("ent_b",layer.Layout.Entries.Single(e=>e.State == NativeSketchConstraintGlyphState.Visible).Reference.EntityId);
        }

        [Test]
        public void OverlappingBoundedFallbackReturnsAllRayCandidatesAndMissReturnsNone()
        {
            var layer = Create(); var snapshot = Snapshot(Enumerable.Range(0,34).Select(i=>Constraint("ent_"+i.ToString("D2"))).ToArray());
            layer.Synchronize(snapshot);
            Assert.True(layer.Layout.Entries.Last().MayOverlap);
            Assert.True(layer.TryGetLabel("ent_00",out var first));
            var hits = layer.PickTargets(snapshot,RayAt(first));
            Assert.AreEqual(new[] {"ent_00","ent_33"},hits.Select(h=>h.EntityId).ToArray());
            Assert.True(layer.TryPick(snapshot,RayAt(first),out var reference)); Assert.AreEqual("ent_00",reference.EntityId);
            var miss = new Ray(first.transform.position+first.transform.right*10+first.transform.forward,-first.transform.forward);
            Assert.IsEmpty(layer.PickTargets(snapshot,miss));
        }

        [Test]
        public void ForeignSnapshotsAndSelectionsCannotReuseAHitAndDocumentOrSketchSwitchDestroysOldLabels()
        {
            var layer = Create(); var original = Snapshot(new[] {Constraint("ent_a")}); layer.Synchronize(original);
            Assert.True(layer.TryGetLabel("ent_a",out var old)); var ray = RayAt(old);
            var foreign = Snapshot(new[] {Constraint("ent_a")},document:"other");
            Assert.IsEmpty(layer.PickTargets(foreign,ray));
            layer.Synchronize(original,new SketchEntityRef("other","r1","ent_sketch","ent_a","constraint"),showAll:false);
            Assert.False(layer.Layout.SelectedReferenceFound); Assert.Zero(layer.VisibleCount);
            layer.Synchronize(foreign); Assert.True(old == null);
            Assert.True(layer.TryGetLabel("ent_a",out var foreignLabel));
            Assert.AreEqual("other",layer.PickTargets(foreign,RayAt(foreignLabel)).Single().DocumentId);
            var anotherSketch = Snapshot(new[] {Constraint("ent_a")},document:"other",sketch:"ent_second");
            layer.Synchronize(anotherSketch); Assert.True(foreignLabel == null);
        }

        [Test]
        public void DegenerateFootprintDisablesAllHitsAndRecoversWithTheExistingPool()
        {
            var layer = Create(); var snapshot = Snapshot(new[] {Constraint("ent_a")}); layer.Synchronize(snapshot);
            Assert.True(layer.TryGetLabel("ent_a",out var label)); int created = layer.CreatedLabelCount;
            _model.transform.localScale = new Vector3(0,1,1);
            Assert.IsEmpty(layer.PickTargets(snapshot,new Ray(Vector3.forward,Vector3.back)));
            Assert.Null(layer.Layout); Assert.IsNotEmpty(layer.UnavailableReason); Assert.Zero(layer.VisibleCount);
            _model.transform.localScale = Vector3.one; layer.Synchronize(snapshot);
            Assert.AreSame(label,GetLabel(layer,"ent_a")); Assert.AreEqual(created,layer.CreatedLabelCount);
            Assert.True(layer.TryPick(snapshot,RayAt(label),out _)); Assert.AreEqual("",layer.UnavailableReason);
        }
        private static NativeSketchConstraintLabelView GetLabel(NativeSketchConstraintLayer layer,string id)
        { Assert.True(layer.TryGetLabel(id,out var label)); return label; }

        [Test]
        public void ClearAndDisposeRemoveActivePooledAndMeasurementObjects()
        {
            var layer = Create(); var snapshot = Snapshot(new[] {Constraint("ent_a")}); layer.Synchronize(snapshot);
            Assert.AreEqual(2,layer.GetComponentsInChildren<NativeSketchConstraintLabelView>(true).Length);
            layer.Clear(); Assert.Zero(layer.PooledCount); Assert.Zero(layer.VisibleCount); Assert.Null(layer.Layout);
            Assert.IsEmpty(layer.GetComponentsInChildren<NativeSketchConstraintLabelView>(true));
            Assert.IsEmpty(layer.PickTargets(snapshot,new Ray(Vector3.forward,Vector3.back)));
            layer.Synchronize(snapshot); Assert.AreEqual(1,layer.VisibleCount);
            layer.Dispose(); Assert.True(layer == null);
            Assert.IsEmpty(_model.GetComponentsInChildren<NativeSketchConstraintLabelView>(true));
        }

        [Test]
        public void HardPoolBudgetEvictsAnInactiveLabelWhenAnotherFilterNeedsANewIdentity()
        {
            var constraints = Enumerable.Range(0,512).Select(i=>Constraint("ent_"+i.ToString("D3")))
                .Concat(new[] {Constraint("ent_z","kParallelConstraintObject")}).ToArray();
            var snapshot = Snapshot(constraints); var layer = Create();
            layer.Synchronize(snapshot,maxVisible:512);
            Assert.AreEqual(512,layer.VisibleCount); Assert.AreEqual(512,layer.PooledCount);
            Assert.True(layer.TryGetLabel("ent_000",out var evicted));
            layer.Synchronize(snapshot,nativeTypes:new[] {"kParallelConstraintObject"},maxVisible:512);
            Assert.AreEqual(512,layer.PooledCount); Assert.AreEqual(513,layer.CreatedLabelCount);
            Assert.AreEqual(1,layer.VisibleCount); Assert.True(evicted == null);
            Assert.False(layer.TryGetLabel("ent_000",out _));
            Assert.True(layer.TryGetLabel("ent_z",out var selected));
            Assert.AreEqual("ent_z",layer.PickTargets(snapshot,RayAt(selected)).Single().EntityId);
            Assert.Throws<ArgumentOutOfRangeException>(()=>layer.Synchronize(snapshot,maxVisible:513));
            Assert.AreEqual(1,layer.VisibleCount); Assert.AreEqual(512,layer.PooledCount);
        }

        [Test]
        public void BorrowedMaterialLeadersReuseObjectsAndFollowLayoutAfterZoomAndModelMovement()
        {
            var material = new Material(Shader.Find("XrSo/CadSurface"));
            try
            {
                var layer = NativeSketchConstraintLayer.Create(_model.transform,_model.transform,material);
                var snapshot = Snapshot(new[] {Constraint("ent_a"),Constraint("ent_b")}); layer.Synchronize(snapshot);
                Assert.AreEqual(1,layer.VisibleLeaderCount); Assert.False(layer.TryGetLeader("ent_a",out _));
                Assert.True(layer.TryGetLeader("ent_b",out var line)); Assert.True(line.useWorldSpace);
                Assert.AreSame(material,line.sharedMaterial); AssertLeaderMatchesLayout(layer,line,"ent_b");
                var firstEnd = line.GetPosition(1);
                _model.transform.localScale = Vector3.one*2;
                layer.PickTargets(snapshot,RayAt(GetLabel(layer,"ent_b")));
                Assert.AreSame(line,GetLeader(layer,"ent_b")); AssertLeaderMatchesLayout(layer,line,"ent_b");
                Assert.Greater(Vector3.Distance(firstEnd,line.GetPosition(1)),.0001f);
                _model.transform.SetPositionAndRotation(new Vector3(1,2,3),Quaternion.Euler(20,30,40));
                layer.Synchronize(snapshot);
                Assert.AreSame(line,GetLeader(layer,"ent_b")); AssertLeaderMatchesLayout(layer,line,"ent_b");
                layer.Dispose(); Assert.True(line == null); Assert.True(material != null);
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }

        [Test]
        public void LeadersHideWhenFilteredOrNoLongerDisplacedAndClearDoesNotOwnTheMaterial()
        {
            var material = new Material(Shader.Find("XrSo/CadSurface"));
            try
            {
                var layer = NativeSketchConstraintLayer.Create(_model.transform,_model.transform,material);
                var snapshot = Snapshot(new[] {Constraint("ent_a"),Constraint("ent_b")}); layer.Synchronize(snapshot);
                var line = GetLeader(layer,"ent_b");
                layer.Synchronize(snapshot,showAll:false); Assert.Zero(layer.VisibleLeaderCount); Assert.False(line.gameObject.activeSelf);
                layer.Synchronize(snapshot,selected:snapshot.Constraints[1].Reference,showAll:false);
                Assert.AreEqual(1,layer.VisibleCount); Assert.Zero(layer.VisibleLeaderCount);
                layer.Synchronize(snapshot); Assert.AreSame(line,GetLeader(layer,"ent_b")); Assert.AreEqual(1,layer.VisibleLeaderCount);
                layer.Clear(); Assert.True(line == null); Assert.Zero(layer.VisibleLeaderCount);
                Assert.IsEmpty(layer.GetComponentsInChildren<LineRenderer>(true)); Assert.True(material != null);
                var withoutMaterial = Create(); withoutMaterial.Synchronize(snapshot);
                Assert.Zero(withoutMaterial.VisibleLeaderCount); Assert.IsEmpty(withoutMaterial.GetComponentsInChildren<LineRenderer>(true));
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }

        private static LineRenderer GetLeader(NativeSketchConstraintLayer layer,string id)
        { Assert.True(layer.TryGetLeader(id,out var line)); return line; }
        private void AssertLeaderMatchesLayout(NativeSketchConstraintLayer layer,LineRenderer line,string id)
        {
            var glyph = layer.Layout.Entries.Single(e=>e.Reference.EntityId == id);
            Assert.AreEqual(2,line.positionCount);
            var start = _model.transform.TransformPoint(CadCoordinates.ToLocal(new CadPoint(glyph.AnchorMm.Value.X,glyph.AnchorMm.Value.Y)));
            var end = _model.transform.TransformPoint(CadCoordinates.ToLocal(new CadPoint(glyph.PositionMm.Value.X,glyph.PositionMm.Value.Y)));
            Assert.Less(Vector3.Distance(start,line.GetPosition(0)),.000001f);
            Assert.Less(Vector3.Distance(end,line.GetPosition(1)),.000001f);
        }
    }
}
