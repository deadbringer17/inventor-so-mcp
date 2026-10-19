using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Tests
{
    internal sealed class ImportBrowserFixture : IHttpTransport
    {
        private const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private readonly byte[] _png;
        private readonly JObject _descriptor;
        internal TaskCompletionSource<TransportResponse> Pending;
        internal string Id => "a_" + Source;
        internal ImportBrowserFixture()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.red); texture.Apply(); _png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            _descriptor = new JObject {
                ["ok"]=true,["storage"]="memory",["cad_import_available"]=false,["inspection_state"]="verified_metadata",
                ["sha256"]=Source,["expires_utc"]=DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"),["preview_available"]=true,
                ["resource"]=new JObject { ["kind"]="Image",["dependencies"]=new JArray(),["image"]=new JObject { ["pixel_width"]=1,["pixel_height"]=1 },
                    ["geometry"]=JValue.CreateNull(),["asset_id"]=Id,["format"]="bmp",["byte_length"]=58 }
            };
        }
        internal TransportResponse Page() => Json(new JObject { ["ok"]=true,["storage"]="memory",["cad_import_available"]=false,
            ["resources"]=new JArray(_descriptor.DeepClone()),["next_offset"]=JValue.CreateNull() });
        private static TransportResponse Json(JObject json) => new TransportResponse(200,
            new Dictionary<string,string> { ["Content-Type"]="application/json" },Encoding.UTF8.GetBytes(json.ToString()));
        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
        {
            Assert.Greater(request.MaxResponseBytes, 0);
            if (Pending != null) return Pending.Task;
            if (request.Url.EndsWith("/preview",StringComparison.Ordinal))
            {
                string hash; using(var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(_png)).Replace("-", "").ToLowerInvariant();
                return Task.FromResult(new TransportResponse(200,new Dictionary<string,string> { ["Content-Type"]="image/png",
                    ["X-Content-SHA256"]=hash,["X-Source-SHA256"]=Source },_png));
            }
            return Task.FromResult(request.Url.Contains("?offset=") ? Page() : Json(_descriptor));
        }
        public Task<int> StreamLinesAsync(TransportRequest request,Action<string> callback,CancellationToken ct) => throw new NotSupportedException();
        internal void Bind(NativeSketchEditorUi editor) => editor.BindImportResources(NativeSketchEditorUi.CreateImportResourceBrowser(
            new InventorBackend(this,new PairedServer("localhost",8787,new string('a',64),"test","test-token"),new MemoryAssetCache())));
        internal static async Task Show(NativeSketchEditorUi editor,Func<string,XrAction> action,PaletteView palette)
        {
            var fixture=new ImportBrowserFixture();fixture.Bind(editor);
            Assert.True(action("sketch.import.resources").TryInvoke());
            var resource=action("sketch.import.pick."+fixture.Id);
            Assert.True(resource.Enabled,resource.DisabledReason);
            Assert.True(resource.TryInvoke());
            Assert.NotNull(editor.SelectedImportResource);
            Assert.False(editor.Session.HasDraft);
            Assert.False(editor.SelectedImportResource.CadImportAvailable);
            var image=palette.GetComponentInChildren<RawImage>();Assert.NotNull(image);
            Assert.AreEqual(1,image.texture.width);Assert.AreEqual(1,image.texture.height);Assert.False(image.raycastTarget);
            Assert.True(action("sketch.import.cancel").TryInvoke());
            Assert.Null(editor.SelectedImportResource);Assert.IsNull(palette.GetComponentInChildren<RawImage>());
            await Task.CompletedTask;
        }
        internal static async Task Stale(NativeSketchEditorUi editor, bool dispose)
        {
            var fixture=new ImportBrowserFixture();fixture.Bind(editor);
            fixture.Pending=new TaskCompletionSource<TransportResponse>();
            var task=editor.LoadImportResourcesAsync();Assert.True(editor.ImportResourceBusy);
            var state=editor.Session.Snapshot.State;
            if(dispose) editor.Dispose();
            else editor.SetEnvironment(new DocumentState(state.DocumentId,state.Revision+"changed",state.VisualRevision),true,true);
            fixture.Pending.SetResult(fixture.Page());await task;
            Assert.False(editor.ImportResourceBusy);Assert.Null(editor.SelectedImportResource);
            Assert.False(editor.Actions().Any(a=>a.Id.StartsWith("sketch.import.pick.",StringComparison.Ordinal)));
        }
    }
    public partial class DesignWorkspaceTests
    {
        [Test] public async Task ImportBrowserShowsHashedResourcePreviewWithoutCadMutationInPart()
        {
            OpenNative();var editor=Field<NativeSketchEditorUi>("_nativeEditor");
            await ImportBrowserFixture.Show(editor,Act,Field<UiShell>("_shell").Palette);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
        [TestCase(false)][TestCase(true)] public async Task ImportBrowserDiscardsLateResponseAfterRevisionChangeOrDispose(bool dispose)
        { OpenNative();await ImportBrowserFixture.Stale(Field<NativeSketchEditorUi>("_nativeEditor"),dispose);Assert.Zero(_backend.Commits); }
    }
    public partial class LamieraWorkspaceTests
    {
        [Test] public async Task ImportBrowserShowsHashedResourcePreviewWithoutCadMutationInSheet()
        {
            OpenNative();var editor=(NativeSketchEditorUi)typeof(InventorXrSo.Xr.LamieraWorkspace).GetField("_nativeEditor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_workspace);
            var shell=(UiShell)typeof(InventorXrSo.Xr.LamieraWorkspace).GetField("_shell",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_workspace);
            await ImportBrowserFixture.Show(editor,Act,shell.Palette);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
    }
}
