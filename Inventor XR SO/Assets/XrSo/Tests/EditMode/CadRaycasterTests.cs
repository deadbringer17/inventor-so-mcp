using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class CadRaycasterTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        [Test]
        public void PicksTheBodyAndTriangleAndIgnoresOtherColliders()
        {
            _root = new GameObject("scene");
            var view = _root.AddComponent<CadSceneView>();
            view.BodyMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            view.Show(CadSceneViewTests.BoltScene());
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);   // not CAD: must be skipped
            blocker.transform.SetParent(_root.transform);
            blocker.transform.position = new Vector3(-0.005f, 0.5f, 0.005f);
            blocker.transform.localScale = Vector3.one * 0.01f;
            Physics.SyncTransforms();

            Assert.IsTrue(CadRaycaster.TryPick(new Ray(new Vector3(-0.005f, 1f, 0.005f), Vector3.down), 5f, out var body, out var triangle, out _));
            Assert.AreEqual("ent_doc_bolt_f4", body.Primitive.FaceMap.FaceAtTriangle(triangle).FaceId);
            Assert.IsFalse(CadRaycaster.TryPick(new Ray(new Vector3(5f, 1f, 5f), Vector3.down), 5f, out _, out _, out _));
        }

        [Test]
        public void StillFindsNearestCadWhenMoreThan32CollidersIntersect()
        {
            _root = new GameObject("scene");
            var view = _root.AddComponent<CadSceneView>();
            view.BodyMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            view.Show(CadSceneViewTests.BoltScene());
            for (int i = 0; i < 40; i++)
            {
                var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.transform.SetParent(_root.transform);
                blocker.transform.position = new Vector3(-0.005f, 0.1f + i * 0.02f, 0.005f);
                blocker.transform.localScale = Vector3.one * 0.005f;
            }
            Physics.SyncTransforms();
            Assert.IsTrue(CadRaycaster.TryPick(new Ray(new Vector3(-0.005f, 1f, 0.005f), Vector3.down), 5f, out var body, out _, out _));
            Assert.AreEqual("ent_occ_1", body.Instance.OccurrenceId);
        }
    }
}
