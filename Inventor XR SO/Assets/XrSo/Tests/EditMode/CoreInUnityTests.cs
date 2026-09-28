using System.IO;
using InventorXrSo.Core.Glb;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public class CoreInUnityTests
    {
        public const string BoltFixture = "Assets/XrSo/Tests/EditMode/Fixtures/bolt-1cm.glb.bytes";

        [Test]
        public void TheCorePackageParsesTheServerGlbInsideUnity()
        {
            var model = GlbModel.Parse(File.ReadAllBytes(BoltFixture));
            Assert.AreEqual(1, model.Primitives.Count);
            Assert.AreEqual(12, model.Primitives[0].TriangleCount);
            Assert.AreEqual("ent_doc_bolt_f4", model.Primitives[0].FaceMap.FaceAtTriangle(6).FaceId);
        }
    }
}
