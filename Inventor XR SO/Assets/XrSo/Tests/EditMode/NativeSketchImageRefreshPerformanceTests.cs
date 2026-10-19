using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace InventorXrSo.Tests
{
    public sealed class NativeSketchImageRefreshPerformanceTests
    {
        private sealed class Transport : IHttpTransport
        {
            public byte[] Png;
            public int Requests;
            public Task<TransportResponse> SendAsync(TransportRequest request,CancellationToken ct)
            {
                Requests++;
                return Task.FromResult(new TransportResponse(200,new Dictionary<string,string>{{"Content-Type","image/png"}},(byte[])Png.Clone()));
            }
            public Task<int> StreamLinesAsync(TransportRequest request,Action<string> line,CancellationToken ct)=>throw new NotSupportedException();
        }
        private sealed class Backend : INativeSketchImageBackend
        {
            private readonly NativeSketchImageClient _client;
            private readonly byte[] _png;
            private readonly string _sha;
            public Backend(Transport transport)
            {
                _png=transport.Png;
                using(var digest=SHA256.Create()) _sha=BitConverter.ToString(digest.ComputeHash(_png)).Replace("-","").ToLowerInvariant();
                _client=new NativeSketchImageClient(transport,new PairedServer("127.0.0.1",7443,new string('a',64),"test","secret"));
            }
            public Task<NativeSketchImageAsset> GetNativeSketchImageAsync(DocumentState state,string sketch,string image,CancellationToken ct)
                =>_client.DownloadAsync(new JObject
                {
                    ["document_id"]=state.DocumentId,["revision"]=state.Revision,["sketch_id"]=sketch,["image_id"]=image,
                    ["source_kind"]="native_bitmap",["pixel_width"]=2,["pixel_height"]=1,["width_mm"]=30,["height_mm"]=20,
                    ["visible"]=true,["transparent"]=false,
                    ["image_frame"]=new JObject { ["schema"]=1,["provenance"]="native_boundary_roles",
                        ["corner_order"]=new JArray("top_left","top_right","bottom_right","bottom_left"),
                        ["corners_mm"]=new JArray(new JArray(13,21),new JArray(43,21),new JArray(43,1),new JArray(13,1)) },
                    ["asset"]=new JObject { ["asset_id"]="a_"+_sha,["sha256"]=_sha,["mime_type"]="image/png",
                        ["size_bytes"]=_png.Length,["asset_url"]="/assets/a_"+_sha }
                },state,sketch,image,ct);
        }
        private static NativeSketchSnapshot Snapshot(double originX=100)
        {
            var images=new JArray();
            for(int i=0;i<32;i++) images.Add(new JObject { ["id"]="ent_image"+i,["type"]="image",["supported"]=false,["visible"]=true });
            return NativeSketchSnapshot.Parse(new JObject
            {
                ["schema_version"]=1,["document_id"]="part",["revision"]="r1",["sketch_id"]="ent_sketch",["sketch_name"]="Native",
                ["frame"]=new JObject { ["origin_mm"]=new JArray(originX,200,300),["x_axis"]=new JArray(0,1,0),["y_axis"]=new JArray(0,0,1) },
                ["entities"]=images,["dimensions"]=new JArray(),["constraints"]=new JArray(),["partial"]=true
            },new DocumentState("part","r1","v1"),"ent_sketch");
        }
        private static IEnumerator Finished(Task task)
        {
            var deadline=DateTime.UtcNow.AddSeconds(30);
            while(!task.IsCompleted && DateTime.UtcNow<deadline) yield return null;
            Assert.True(task.IsCompleted,"Native bitmap setup timed out.");Assert.False(task.IsFaulted,task.Exception?.ToString());
        }

        [UnityTest]
        public IEnumerator RepeatedImmutableSnapshotRefreshAllocatesNothingAndPreservesAllResources()
        {
            var model=new GameObject("native bitmap refresh performance");
            var source=new Texture2D(2,1,TextureFormat.RGBA32,false,false);
            try
            {
                source.SetPixels(new[]{Color.red,Color.blue});source.Apply();
                var transport=new Transport { Png=ImageConversion.EncodeToPNG(source) };
                var layer=NativeSketchImageLayer.Create(model.transform,new Backend(transport));var snapshot=Snapshot();
                layer.Synchronize(snapshot);yield return Finished(layer.CurrentLoad);
                Assert.AreEqual("",layer.UnavailableReason);Assert.AreEqual(32,layer.VisibleCount);Assert.AreEqual(1,layer.TextureCount);Assert.AreEqual(2,layer.AllocatedPixels);
                var filters=layer.GetComponentsInChildren<MeshFilter>();var meshes=filters.Select(f=>f.sharedMesh).ToArray();
                var materials=filters.Select(f=>f.GetComponent<MeshRenderer>().sharedMaterial).ToArray();
                var texture=materials[0].GetTexture("_BaseMap");var firstVertex=meshes[0].vertices[0];var pending=layer.CurrentLoad;
                Assert.True(materials.All(m=>ReferenceEquals(texture,m.GetTexture("_BaseMap"))));
                for(int i=0;i<10;i++) layer.Synchronize(snapshot);
                // Unity's Mono per-thread GC counter can return zero even for known allocations.
                // Require a positive native allocation sample before trusting the measured zero.
                using(var control=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",16,ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    long monoBefore=GC.GetAllocatedBytesForCurrentThread();
                    GC.KeepAlive(new byte[4096]);long monoControl=GC.GetAllocatedBytesForCurrentThread()-monoBefore;
                    control.Stop();
                    UnityEngine.Debug.Log("S1 IMAGE55 COUNTER native_control_events="+control.Count+" mono_control_bytes="+monoControl);
                    Assert.Greater(control.Count,0,"GC.Alloc observer failed its explicit allocation control.");
                }
                long ticks;int allocationSamples;
                using(var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",2000000,ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    ticks=Stopwatch.GetTimestamp();
                    for(int i=0;i<1000;i++) layer.Synchronize(snapshot);
                    ticks=Stopwatch.GetTimestamp()-ticks;recorder.Stop();allocationSamples=recorder.Count;
                    Assert.Less(allocationSamples,recorder.Capacity,"Allocation observer saturated; the measurement is incomplete.");
                }
                UnityEngine.Debug.Log("S1 IMAGE55 PERF iterations=1000 images=32 allocation_events="+allocationSamples+" elapsed_ticks="+ticks+" stopwatch_frequency="+Stopwatch.Frequency);
                Assert.AreEqual(32,transport.Requests);Assert.AreSame(pending,layer.CurrentLoad);
                for(int i=0;i<filters.Length;i++)
                {
                    Assert.AreSame(meshes[i],filters[i].sharedMesh);Assert.AreSame(materials[i],filters[i].GetComponent<MeshRenderer>().sharedMaterial);
                    Assert.AreSame(texture,materials[i].GetTexture("_BaseMap"));
                }
                // A new immutable snapshot with the same revision/key still carries a new CAD plane.
                var moved=Snapshot(150);layer.Synchronize(moved);
                Assert.AreEqual(32,transport.Requests);Assert.AreSame(meshes[0],filters[0].sharedMesh);
                Assert.Less(Vector3.Distance(firstVertex+new Vector3(-.05f,0,0),meshes[0].vertices[0]),1e-6f);
                layer.enabled=false;layer.Synchronize(moved);Assert.Zero(layer.VisibleCount);Assert.Zero(layer.TextureCount);
                layer.enabled=true;layer.Synchronize(snapshot);yield return Finished(layer.CurrentLoad);Assert.AreEqual(64,transport.Requests);
                layer.Synchronize(null);Assert.Zero(layer.VisibleCount);Assert.Zero(layer.AllocatedPixels);
                layer.Dispose();Assert.Throws<ObjectDisposedException>(()=>layer.Synchronize(snapshot));
                Assert.AreEqual(0,allocationSamples,"Repeated refresh of the identical immutable snapshot must not allocate managed memory.");
            }
            finally { UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
