using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InventorXrSo.Tests
{
    public sealed class NativeSketchImageLayerTests
    {
        private GameObject _model;
        [SetUp] public void Setup() => _model = new GameObject("native bitmap test model");
        [TearDown] public void Cleanup() { if (_model != null) UnityEngine.Object.DestroyImmediate(_model); }
        private sealed class Backend : INativeSketchImageBackend
        {
            public int Requests;
            public CancellationToken LastToken;
            public Func<DocumentState, string, string, CancellationToken, Task<NativeSketchImageAsset>> Handler;
            public Task<NativeSketchImageAsset> GetNativeSketchImageAsync(DocumentState state, string sketch, string image, CancellationToken ct)
            { Requests++; LastToken = ct; return Handler(state, sketch, image, ct); }
        }
        private sealed class Transport : IHttpTransport
        {
            public byte[] Bytes;
            public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct) => Task.FromResult(
                new TransportResponse(200, new Dictionary<string, string> { ["Content-Type"] = "image/png" }, (byte[])Bytes.Clone()));
            public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct) => throw new NotSupportedException();
        }
        private static byte[] Png(int width = 2, int height = 1, int color = 0)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                if (width == 2 && height == 1) texture.SetPixels(new[] { Color.red, Color.blue });
                else { texture.SetPixels32(new Color32[width * height]); texture.SetPixel(0, 0, color == 0 ? Color.red : color == 1 ? Color.green : Color.blue); }
                texture.Apply(); return ImageConversion.EncodeToPNG(texture);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static async Task<NativeSketchImageAsset> Asset(DocumentState state, string sketch, string image, bool visible = true, byte[] png = null, int declaredWidth = 2, int declaredHeight = 1)
        {
            png = png ?? Png(); string sha;
            using (var digest = SHA256.Create()) sha = BitConverter.ToString(digest.ComputeHash(png)).Replace("-", "").ToLowerInvariant();
            var data = new JObject {
                ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["sketch_id"] = sketch, ["image_id"] = image,
                ["source_kind"] = "native_bitmap", ["pixel_width"] = declaredWidth, ["pixel_height"] = declaredHeight, ["width_mm"] = 30, ["height_mm"] = 20,
                ["visible"] = visible, ["transparent"] = false,
                ["image_frame"] = new JObject { ["schema"] = 1, ["provenance"] = "native_boundary_roles",
                    ["corner_order"] = new JArray("top_left", "top_right", "bottom_right", "bottom_left"),
                    ["corners_mm"] = new JArray(new JArray(13, 21), new JArray(43, 21), new JArray(43, 1), new JArray(13, 1)) },
                ["asset"] = new JObject { ["asset_id"] = "a_" + sha, ["sha256"] = sha, ["mime_type"] = "image/png",
                    ["size_bytes"] = png.Length, ["asset_url"] = "/assets/a_" + sha } };
            return await new NativeSketchImageClient(new Transport { Bytes = png }, new PairedServer("127.0.0.1", 7443, new string('a', 64), "test", "secret"))
                .DownloadAsync(data, state, sketch, image, CancellationToken.None);
        }
        private static NativeSketchSnapshot Snapshot(string revision = "r1", int count = 1, bool visible = true)
        {
            var entities = new JArray();
            for (int i = 0; i < count; i++) entities.Add(new JObject { ["id"] = "ent_image" + i, ["type"] = "image", ["supported"] = false,
                ["native_type"] = "kSketchImageObject", ["visible"] = visible });
            return NativeSketchSnapshot.Parse(new JObject {
                ["schema_version"] = 1, ["document_id"] = "part", ["revision"] = revision, ["sketch_id"] = "ent_sketch", ["sketch_name"] = "Native",
                ["frame"] = new JObject { ["origin_mm"] = new JArray(100, 200, 300), ["x_axis"] = new JArray(0, 1, 0), ["y_axis"] = new JArray(0, 0, 1) },
                ["entities"] = entities, ["dimensions"] = new JArray(), ["constraints"] = new JArray(), ["partial"] = true
            }, new DocumentState("part", revision, "v"), "ent_sketch");
        }
        private static IEnumerator Finished(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.True(task.IsCompleted, "Image load did not complete within 30 seconds.");
            Assert.False(task.IsFaulted, task.Exception?.ToString());
        }
        private static Backend Available() => new Backend { Handler = (s, k, i, _) => Asset(s, k, i) };

        [UnityTest]
        public IEnumerator ActualDecodedTextureUsesOrderedUvsAndCurrentCadPlaneWithNoCollider()
        {
            _model.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(10, 20, 30));
            var backend = Available(); var layer = NativeSketchImageLayer.Create(_model.transform, backend); var snapshot = Snapshot();
            layer.Synchronize(snapshot); yield return Finished(layer.CurrentLoad);
            Assert.AreEqual("", layer.UnavailableReason); Assert.AreEqual(1, layer.VisibleCount); Assert.AreEqual(1, layer.TextureCount); Assert.AreEqual(2, layer.AllocatedPixels);
            var filter = layer.GetComponentInChildren<MeshFilter>(); var mesh = filter.sharedMesh;
            CollectionAssert.AreEqual(new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) }, mesh.uv);
            var corners = new[] { new CadPoint(13, 21), new CadPoint(43, 21), new CadPoint(43, 1), new CadPoint(13, 1) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 expected = _model.transform.TransformPoint(CadCoordinates.ToLocal(snapshot.Frame.ToModel(corners[i])));
                Assert.Less(Vector3.Distance(expected, filter.transform.TransformPoint(mesh.vertices[i])), 1e-6f);
            }
            Assert.IsEmpty(layer.GetComponentsInChildren<Collider>());
            var texture = (Texture2D)filter.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_BaseMap");
            Assert.AreEqual(2, texture.width); Assert.AreEqual(1, texture.height); Assert.False(texture.isReadable);
            var target = RenderTexture.GetTemporary(2, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var readable = new Texture2D(2, 1, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(texture, target, filter.GetComponent<MeshRenderer>().sharedMaterial); RenderTexture.active = target;
                readable.ReadPixels(new Rect(0, 0, 2, 1), 0, 0); readable.Apply();
                CollectionAssert.AreEqual(new[] { new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255) }, readable.GetPixels32());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(readable); }
            layer.Clear(); Assert.AreEqual(0, layer.VisibleCount); Assert.AreEqual(0, layer.TextureCount); Assert.AreEqual(0, layer.AllocatedPixels);
            Assert.True(mesh == null); Assert.True(texture == null);
        }

        [UnityTest]
        public IEnumerator SameIdentityRefreshPreservesPendingLoadAndTexturesAndSharesSameBitmap()
        {
            var backend = Available(); var layer = NativeSketchImageLayer.Create(_model.transform, backend);
            layer.Synchronize(Snapshot(count: 2)); var load = layer.CurrentLoad; layer.Synchronize(Snapshot(count: 2));
            Assert.AreSame(load, layer.CurrentLoad); yield return Finished(load);
            Assert.AreEqual(2, backend.Requests); Assert.AreEqual(2, layer.VisibleCount); Assert.AreEqual(1, layer.TextureCount); Assert.AreEqual(2, layer.AllocatedPixels);
            var texture = layer.GetComponentInChildren<MeshRenderer>().sharedMaterial.GetTexture("_BaseMap");
            layer.Synchronize(Snapshot(count: 2)); Assert.AreEqual(2, backend.Requests);
            Assert.AreSame(texture, layer.GetComponentInChildren<MeshRenderer>().sharedMaterial.GetTexture("_BaseMap"));
        }

        [UnityTest]
        public IEnumerator RevisionChangeCancelsOldRequestAndDisposesIgnoredLateResult()
        {
            var late = new TaskCompletionSource<NativeSketchImageAsset>(); var backend = Available();
            backend.Handler = (s, k, i, _) => s.Revision == "r1" ? late.Task : Asset(s, k, i);
            var layer = NativeSketchImageLayer.Create(_model.transform, backend); var old = Snapshot();
            layer.Synchronize(old); Task oldLoad = layer.CurrentLoad; CancellationToken oldToken = backend.LastToken;
            layer.Synchronize(Snapshot("r2")); yield return Finished(layer.CurrentLoad); Assert.True(oldToken.IsCancellationRequested);
            var lateAssetTask = Asset(old.State, old.SketchId, "ent_image0"); yield return Finished(lateAssetTask);
            var lateAsset = lateAssetTask.Result; late.SetResult(lateAsset); yield return Finished(oldLoad);
            Assert.Throws<ObjectDisposedException>(() => lateAsset.CopyPngBytes()); Assert.AreEqual(1, layer.VisibleCount); Assert.AreEqual(1, layer.TextureCount);
        }

        [UnityTest]
        public IEnumerator HiddenImagesAndImageCountBudgetDoNotFetchAndInvisibleReplyDoesNotDecode()
        {
            var backend = Available(); var layer = NativeSketchImageLayer.Create(_model.transform, backend);
            layer.Synchronize(Snapshot(visible: false)); Assert.AreEqual(0, backend.Requests);
            layer.Synchronize(Snapshot(count: 33)); Assert.AreEqual(0, backend.Requests); StringAssert.Contains("budget", layer.UnavailableReason);
            backend.Handler = (s, k, i, _) => Asset(s, k, i, false);
            layer.Synchronize(Snapshot()); yield return Finished(layer.CurrentLoad);
            Assert.AreEqual(1, backend.Requests); Assert.AreEqual(0, layer.VisibleCount); Assert.AreEqual(0, layer.TextureCount);
        }

        [UnityTest]
        public IEnumerator AggregateDecodedPixelBudgetRefusesThirdDistinctBitmapBeforeTextureAllocation()
        {
            var backend = new Backend { Handler = (s, k, i, _) => Asset(s, k, i,
                png: Png(2048, 2048, i.EndsWith("0", StringComparison.Ordinal) ? 0 : i.EndsWith("1", StringComparison.Ordinal) ? 1 : 2),
                declaredWidth: 2048, declaredHeight: 2048) };
            var layer = NativeSketchImageLayer.Create(_model.transform, backend); layer.Synchronize(Snapshot(count: 3));
            yield return Finished(layer.CurrentLoad);
            Assert.AreEqual(3, backend.Requests); Assert.AreEqual(2, layer.VisibleCount); Assert.AreEqual(2, layer.TextureCount);
            Assert.AreEqual(8 * 1024 * 1024, layer.AllocatedPixels); StringAssert.Contains("decoded-pixel budget", layer.UnavailableReason);
            layer.Clear(); Assert.AreEqual(0, layer.AllocatedPixels); Assert.AreEqual(0, layer.TextureCount);
        }

        [UnityTest]
        public IEnumerator ClearCancelsPendingLoadAndReleasesOnlyOwnedResources()
        {
            var late = new TaskCompletionSource<NativeSketchImageAsset>(); var backend = Available(); backend.Handler = (_, __, ___, ____) => late.Task;
            var layer = NativeSketchImageLayer.Create(_model.transform, backend); var snapshot = Snapshot(); layer.Synchronize(snapshot);
            Task pending = layer.CurrentLoad; CancellationToken token = backend.LastToken;
            layer.Clear(); Assert.True(token.IsCancellationRequested); Assert.False(layer.Loading);
            var assetTask = Asset(snapshot.State, snapshot.SketchId, "ent_image0"); yield return Finished(assetTask);
            var asset = assetTask.Result; late.SetResult(asset); yield return Finished(pending);
            Assert.Throws<ObjectDisposedException>(() => asset.CopyPngBytes()); Assert.AreEqual(0, layer.VisibleCount); Assert.AreEqual(0, layer.TextureCount);
            layer.Dispose(); layer.Dispose(); Assert.NotNull(_model); Assert.Throws<ObjectDisposedException>(() => layer.Synchronize(snapshot));
        }

        [UnityTest]
        public IEnumerator MalformedBitmapOrStaleBindingIsUnavailableAndDoesNotPublishQuad()
        {
            byte[] corrupted = Png(); corrupted[corrupted.Length - 1] ^= 1;
            var backend = Available(); backend.Handler = (s, k, i, _) => Asset(s, k, i, png: corrupted);
            var layer = NativeSketchImageLayer.Create(_model.transform, backend); layer.Synchronize(Snapshot()); yield return Finished(layer.CurrentLoad);
            Assert.AreEqual(0, layer.VisibleCount); StringAssert.Contains("FormatException", layer.UnavailableReason);
            layer.BindBackend(new Backend { Handler = (s, k, i, _) => Asset(s, k, i, declaredWidth: 3) });
            layer.Synchronize(Snapshot()); yield return Finished(layer.CurrentLoad);
            Assert.AreEqual(0, layer.VisibleCount); Assert.AreEqual(0, layer.TextureCount); StringAssert.Contains("FormatException", layer.UnavailableReason);
            var other = Snapshot("other"); backend.Handler = (_, k, i, __) => Asset(other.State, k, i);
            layer.BindBackend(new Backend { Handler = backend.Handler }); layer.Synchronize(Snapshot()); yield return Finished(layer.CurrentLoad);
            Assert.AreEqual(0, layer.VisibleCount); Assert.AreEqual(0, layer.TextureCount); StringAssert.Contains("FormatException", layer.UnavailableReason);
        }
    }
}
