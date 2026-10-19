using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace InventorXrSo.Tests
{
    public sealed class NativeSketchImageLifecycleTests
    {
        private GameObject _root;
        private sealed class Transport : IHttpTransport
        {
            public byte[] Png;
            public Task<TransportResponse> SendAsync(TransportRequest request,CancellationToken ct) => Task.FromResult(
                new TransportResponse(200,new Dictionary<string,string>{{"Content-Type","image/png"}},(byte[])Png.Clone()));
            public Task<int> StreamLinesAsync(TransportRequest request,Action<string> line,CancellationToken ct) => throw new NotSupportedException();
        }
        private sealed class Backend : INativeSketchImageBackend
        {
            public bool Deferred;
            public CancellationToken LastToken;
            public TaskCompletionSource<NativeSketchImageAsset> Pending;
            public Task<NativeSketchImageAsset> GetNativeSketchImageAsync(DocumentState state,string sketch,string image,CancellationToken ct)
            {
                LastToken=ct;
                if(Deferred) { Pending=new TaskCompletionSource<NativeSketchImageAsset>();return Pending.Task; }
                return Asset(state,sketch,image);
            }
        }
        private static NativeSketchSnapshot Snapshot(string revision) => NativeSketchSnapshot.Parse(new JObject
        {
            ["schema_version"]=1,["document_id"]="part",["revision"]=revision,["sketch_id"]="ent_sketch",["sketch_name"]="Lifecycle",
            ["frame"]=new JObject { ["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0) },
            ["entities"]=new JArray(new JObject { ["id"]="ent_image",["type"]="image",["supported"]=false,["visible"]=true }),
            ["dimensions"]=new JArray(),["constraints"]=new JArray(),["partial"]=true
        },new DocumentState("part",revision,"v"),"ent_sketch");
        private static async Task<NativeSketchImageAsset> Asset(DocumentState state,string sketch,string image)
        {
            var source=new Texture2D(2,1,TextureFormat.RGBA32,false,false);
            byte[] png;
            try { source.SetPixels(new[]{Color.red,Color.blue});source.Apply();png=ImageConversion.EncodeToPNG(source); }
            finally { UnityEngine.Object.DestroyImmediate(source); }
            try
            {
                string sha;
                using(var digest=SHA256.Create()) sha=BitConverter.ToString(digest.ComputeHash(png)).Replace("-","").ToLowerInvariant();
                return await new NativeSketchImageClient(new Transport{Png=png},new PairedServer("127.0.0.1",7443,new string('a',64),"test","secret"))
                    .DownloadAsync(new JObject
                    {
                        ["document_id"]=state.DocumentId,["revision"]=state.Revision,["sketch_id"]=sketch,["image_id"]=image,
                        ["source_kind"]="native_bitmap",["pixel_width"]=2,["pixel_height"]=1,["width_mm"]=30,["height_mm"]=20,
                        ["visible"]=true,["transparent"]=false,
                        ["image_frame"]=new JObject { ["schema"]=1,["provenance"]="native_boundary_roles",
                            ["corner_order"]=new JArray("top_left","top_right","bottom_right","bottom_left"),
                            ["corners_mm"]=new JArray(new JArray(0,20),new JArray(30,20),new JArray(30,0),new JArray(0,0)) },
                        ["asset"]=new JObject { ["asset_id"]="a_"+sha,["sha256"]=sha,["mime_type"]="image/png",
                            ["size_bytes"]=png.Length,["asset_url"]="/assets/a_"+sha }
                    },state,sketch,image,CancellationToken.None);
            }
            finally { Array.Clear(png,0,png.Length); }
        }
        private static IEnumerator Finished(Task task)
        {
            var deadline=DateTime.UtcNow.AddSeconds(30);
            while(!task.IsCompleted && DateTime.UtcNow<deadline) yield return null;
            Assert.True(task.IsCompleted,"Runtime image request timed out.");Assert.False(task.IsFaulted,task.Exception?.ToString());
        }
        // Keep the assertion closure outside the coroutine: its display class would be
        // allocated before EnterPlayMode and cannot survive the test runner's domain reload.
        private static void AssertDisposed(NativeSketchImageAsset asset)
            => Assert.Throws<ObjectDisposedException>(()=>asset.CopyPngBytes());
        [UnityTest]
        public IEnumerator RuntimeDisableCancelsLateDownloadAndDestroyReleasesOwnedGpuResources()
        {
            yield return new EnterPlayMode();
            Assert.True(Application.isPlaying);
            _root=new GameObject("image lifecycle owned fixture");
            var backend=new Backend();var layer=NativeSketchImageLayer.Create(_root.transform,backend);
            layer.Synchronize(Snapshot("r1"));yield return Finished(layer.CurrentLoad);
            Assert.AreEqual(1,layer.VisibleCount);Assert.AreEqual(1,layer.TextureCount);
            var filter=layer.GetComponentInChildren<MeshFilter>();var mesh=filter.sharedMesh;
            var material=filter.GetComponent<MeshRenderer>().sharedMaterial;var texture=material.GetTexture("_BaseMap");
            layer.gameObject.SetActive(false);
            Assert.Zero(layer.VisibleCount);Assert.Zero(layer.TextureCount);Assert.Zero(layer.AllocatedPixels);
            yield return null;
            Assert.True(mesh==null);Assert.True(material==null);Assert.True(texture==null);

            backend.Deferred=true;layer.gameObject.SetActive(true);var pendingSnapshot=Snapshot("r2");
            layer.Synchronize(pendingSnapshot);var pending=layer.CurrentLoad;var token=backend.LastToken;
            Assert.NotNull(backend.Pending,"Deferred backend must own the in-flight request before disabling the layer.");
            var completion=backend.Pending;
            Assert.True(layer.Loading);layer.gameObject.SetActive(false);
            Assert.True(token.IsCancellationRequested);Assert.False(layer.Loading);
            var late=Asset(pendingSnapshot.State,pendingSnapshot.SketchId,"ent_image");yield return Finished(late);
            Assert.NotNull(late,"Late asset task must remain available.");
            var asset=late.GetAwaiter().GetResult();
            Assert.NotNull(asset,"Late download must return the owned asset.");
            Assert.NotNull(completion,"Deferred request completion must remain available.");
            completion.SetResult(asset);
            Assert.NotNull(pending,"Cancelled load task must remain available for completion.");
            yield return Finished(pending);
            AssertDisposed(asset);Assert.Zero(layer.VisibleCount);

            backend.Deferred=false;layer.gameObject.SetActive(true);layer.Synchronize(Snapshot("r3"));yield return Finished(layer.CurrentLoad);
            filter=layer.GetComponentInChildren<MeshFilter>();mesh=filter.sharedMesh;
            material=filter.GetComponent<MeshRenderer>().sharedMaterial;texture=material.GetTexture("_BaseMap");
            UnityEngine.Object.Destroy(layer.gameObject);yield return null;
            Assert.True(mesh==null);Assert.True(material==null);Assert.True(texture==null);Assert.Zero(_root.transform.childCount);
            UnityEngine.Object.Destroy(_root);yield return null;_root=null;
            yield return new ExitPlayMode();
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if(_root!=null)
            {
                if(Application.isPlaying) { UnityEngine.Object.Destroy(_root);yield return null; }
                else UnityEngine.Object.DestroyImmediate(_root);
                _root=null;
            }
            if(EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
    }
}
