using System.Collections.Generic;
using System.IO;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class CadSceneViewTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        internal static LoadedScene BoltScene()
        {
            var bolt = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture));
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_asm"",""kind"":""assembly"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],
                ""root"":{""name"":""Fake.iam"",""definition_kind"":""assembly"",""children"":[
                  {""name"":""Bolt:1"",""occurrence_id"":""ent_occ_1"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",""children"":[]},
                  {""name"":""Bolt:2"",""occurrence_id"":""ent_occ_2"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",
                   ""matrix_gltf"":[1,0,0,0,0,1,0,0,0,0,1,0,0.03,0,0,1],""children"":[]}]}}"));
            return new LoadedScene(graph,
                new Dictionary<string, GlbModel> { ["doc_bolt"] = bolt },
                new Dictionary<string, string> { ["doc_bolt"] = "a_bolt" },
                new List<string>());
        }

        private CadSceneView View()
        {
            _root = new GameObject("scene");
            var view = _root.AddComponent<CadSceneView>();
            view.BodyMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            return view;
        }

        [Test]
        public void MeshesAreMirroredIntoUnitySpace()
        {
            var mesh = MeshFactory.Build(GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture)).Primitives[0]);
            Assert.AreEqual(24, mesh.vertexCount);
            Assert.AreEqual(36, mesh.GetIndices(0).Length);
            Assert.LessOrEqual(mesh.bounds.max.x, 0.0001f);
            Assert.AreEqual(-0.01f, mesh.bounds.min.x, 1e-5f);
        }

        [Test]
        public void EveryOccurrenceIsPlacedAndInstancesShareTheMesh()
        {
            var view = View();
            view.Show(BoltScene());
            Assert.AreEqual(2, view.Instances.Count);
            Assert.AreEqual(-0.03f, view.Find("ent_occ_2").transform.localPosition.x, 1e-5f);
            Assert.AreSame(view.Find("ent_occ_1").Bodies[0].Mesh, view.Find("ent_occ_2").Bodies[0].Mesh);
        }

        [Test]
        public void ARayOnTheTopFaceFindsTheInventorFace()
        {
            var view = View();
            view.Show(BoltScene());
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(new Vector3(-0.005f, 1f, 0.005f), Vector3.down, out var hit, 5f));
            var body = hit.collider.GetComponent<CadBody>();
            Assert.AreEqual("ent_occ_1", body.Instance.OccurrenceId);
            Assert.AreEqual("ent_doc_bolt_f4", body.Primitive.FaceMap.FaceAtTriangle(hit.triangleIndex).FaceId);
        }

        [Test]
        public void ShowingTheSameAssetAgainReusesItsMesh()
        {
            var view = View();
            view.Show(BoltScene());
            var before = view.Find("ent_occ_1").Bodies[0].Mesh;
            view.Show(BoltScene());
            Assert.AreSame(before, view.Find("ent_occ_1").Bodies[0].Mesh);
            view.Show(null);
            Assert.AreEqual(0, view.Instances.Count);
        }

        [Test]
        public void TheModelLandsInFrontOfTheHeadAtOneToOne()
        {
            var pose = ScenePlacement.InFront(new Bounds(Vector3.zero, Vector3.one * 0.1f), new Vector3(0, 1.6f, 0), new Vector3(0, -1, 1));
            Assert.AreEqual(0f, pose.position.x, 1e-4f);
            Assert.AreEqual(1.4f, pose.position.y, 1e-4f);
            Assert.AreEqual(1.0f, pose.position.z, 1e-4f);
        }
    }
}
