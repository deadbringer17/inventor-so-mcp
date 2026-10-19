using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Read-only native bitmap presentation. Owns all created GPU resources and no CAD or scene selection state.</summary>
    public sealed class NativeSketchImageLayer : MonoBehaviour, IDisposable
    {
        private sealed class TextureEntry { public Texture2D Texture; public long Pixels; public int References; }
        private sealed class Quad { public GameObject Object; public Mesh Mesh; public Material Material; public CadPoint[] Corners; }
        private readonly Dictionary<string, TextureEntry> _textures = new Dictionary<string, TextureEntry>(StringComparer.Ordinal);
        private readonly List<Quad> _quads = new List<Quad>();
        private INativeSketchImageBackend _backend;
        private NativeSketchSnapshot _snapshot;
        private string _key;
        private CancellationTokenSource _cancel;
        private long _generation;
        private bool _disposed;
        public string UnavailableReason { get; private set; } = "";
        public int VisibleCount => _quads.Count;
        public int TextureCount => _textures.Count;
        public long AllocatedPixels { get; private set; }
        public bool Loading { get; private set; }
        public Task CurrentLoad { get; private set; } = Task.CompletedTask;
        public static NativeSketchImageLayer Create(Transform parent, INativeSketchImageBackend backend)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            var go = new GameObject("Native sketch images"); go.transform.SetParent(parent, false);
            var layer = go.AddComponent<NativeSketchImageLayer>(); layer._backend = backend; return layer;
        }
        public void BindBackend(INativeSketchImageBackend backend)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeSketchImageLayer));
            if (ReferenceEquals(_backend, backend)) return;
            Clear(); _backend = backend;
        }
        public void Synchronize(NativeSketchSnapshot snapshot)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeSketchImageLayer));
            if (snapshot == null || !isActiveAndEnabled) { Clear(); return; }
            // Snapshots and their CAD frame are immutable. UI refreshes can reuse the same
            // instance without rebuilding the image key, cloning metadata or touching meshes.
            if (ReferenceEquals(_snapshot, snapshot)) return;
            // Unsupported and legacy native images still have useful opaque references; the backend decides bitmap availability.
            var images = snapshot.Visible ? snapshot.Entities.Where(IsVisibleImage).ToArray() : Array.Empty<NativeSketchObject>();
            string key = snapshot.State.DocumentId + "\n" + snapshot.State.Revision + "\n" + snapshot.SketchId + "\n"
                + string.Join("\n", images.Select(e => e.Reference.EntityId).OrderBy(id => id, StringComparer.Ordinal));
            if (_key == key)
            {
                _snapshot = snapshot;
                foreach (var quad in _quads) Position(quad, snapshot.Frame);
                return;
            }
            Clear(); _snapshot = snapshot; _key = key;
            if (images.Length == 0) return;
            if (_backend == null) { UnavailableReason = "Native image backend is unavailable."; return; }
            if (images.Length > 32) { UnavailableReason = "Native image count exceeds the 32-image budget."; return; }
            _cancel = new CancellationTokenSource(); Loading = true;
            long generation = _generation; var token = _cancel.Token; var backend = _backend;
            CurrentLoad = LoadAsync(snapshot, images, backend, generation, token);
        }
        private bool Current(long generation, CancellationToken token) => !_disposed && !token.IsCancellationRequested
            && generation == _generation && this != null && isActiveAndEnabled;
        private async Task LoadAsync(NativeSketchSnapshot snapshot, NativeSketchObject[] images, INativeSketchImageBackend backend, long generation, CancellationToken token)
        {
            try
            {
                foreach (var image in images)
                {
                    if (!Current(generation, token)) return;
                    NativeSketchImageAsset asset = null;
                    try
                    {
                        asset = await backend.GetNativeSketchImageAsync(snapshot.State, snapshot.SketchId, image.Reference.EntityId, token);
                        if (!Current(generation, token)) return;
                        if (asset == null || !asset.Matches(snapshot.State, snapshot.SketchId, image.Reference.EntityId))
                            throw new FormatException("Native bitmap belongs to a different document, revision or image.");
                        if (!asset.Visible) continue;
                        AddQuad(asset, _snapshot.Frame);
                    }
                    catch (OperationCanceledException) { if (Current(generation, token)) UnavailableReason = "Native image download was cancelled."; return; }
                    catch (Exception ex)
                    {
                        if (!Current(generation, token)) return;
                        UnavailableReason = ex is ImageBudgetException ? "Native image decoded-pixel budget exceeded."
                            : "Native image unavailable: " + ex.GetType().Name + ".";
                    }
                    finally { asset?.Dispose(); }
                }
            }
            finally { if (generation == _generation) Loading = false; }
        }
        private void AddQuad(NativeSketchImageAsset asset, SketchFrame frame)
        {
            bool cached = _textures.TryGetValue(asset.AssetId, out var entry);
            if (cached && (entry.Texture.width != asset.PixelWidth || entry.Texture.height != asset.PixelHeight))
                throw new FormatException("Shared native bitmap dimensions differ.");
            if (!cached)
            {
                long pixels = (long)asset.PixelWidth * asset.PixelHeight;
                if (_textures.Count >= 32 || pixels <= 0 || pixels > 8 * 1024 * 1024 - AllocatedPixels)
                    throw new ImageBudgetException();
                byte[] bytes = null; Texture2D texture = null;
                try
                {
                    bytes = asset.CopyPngBytes();
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { name = "Native sketch bitmap", wrapMode = TextureWrapMode.Clamp };
                    if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width != asset.PixelWidth || texture.height != asset.PixelHeight)
                        throw new FormatException("Decoded native bitmap dimensions differ or PNG decoding failed.");
                    entry = new TextureEntry { Texture = texture, Pixels = pixels }; texture = null;
                    _textures.Add(asset.AssetId, entry); AllocatedPixels += pixels;
                }
                finally { if (bytes != null) Array.Clear(bytes, 0, bytes.Length); DestroyOwned(texture); }
            }
            GameObject go = null; Mesh mesh = null; Material material = null;
            try
            {
                var shader = Shader.Find("InventorXrSo/NativeSketchImage");
                if (shader == null) throw new InvalidOperationException("Native sketch image shader is unavailable.");
                material = new Material(shader) { name = "Native sketch image material" }; material.SetTexture("_BaseMap", entry.Texture);
                mesh = new Mesh { name = "Native sketch image quad", vertices = new Vector3[4],
                    uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) },
                    triangles = new[] { 0, 1, 2, 0, 2, 3 } };
                go = new GameObject("Native image " + asset.ImageId); go.transform.SetParent(transform, false); go.SetActive(false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
                var quad = new Quad { Object = go, Mesh = mesh, Material = material, Corners = asset.CornersMm.ToArray() };
                Position(quad, frame); _quads.Add(quad); entry.References++; go.SetActive(true);
                go = null; mesh = null; material = null;
            }
            finally
            {
                if (go != null) go.SetActive(false); DestroyOwned(go); DestroyOwned(mesh); DestroyOwned(material);
                if (entry.References == 0) { _textures.Remove(asset.AssetId); AllocatedPixels -= entry.Pixels; DestroyOwned(entry.Texture); }
            }
        }
        private static void Position(Quad quad, SketchFrame frame)
        {
            var vertices = quad.Corners.Select(p => CadCoordinates.ToLocal(frame.ToModel(p))).ToArray();
            if (vertices.Any(v => !Finite(v.x) || !Finite(v.y) || !Finite(v.z))) throw new FormatException("Native bitmap plane exceeds renderer coordinates.");
            quad.Mesh.vertices = vertices; quad.Mesh.RecalculateBounds();
        }
        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        private sealed class ImageBudgetException : Exception { }
        private static bool IsVisibleImage(NativeSketchObject entity)
        {
            if (entity.Type != "image" || entity.Reference == null) return false;
            var visible = entity.Data["visible"];
            return visible?.Type != Newtonsoft.Json.Linq.JTokenType.Boolean || (bool)visible;
        }
        public void Clear()
        {
            _generation++; _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
            Loading = false; CurrentLoad = Task.CompletedTask; _key = null; _snapshot = null; UnavailableReason = "";
            foreach (var quad in _quads)
            { if (quad.Object != null) quad.Object.SetActive(false); DestroyOwned(quad.Object); DestroyOwned(quad.Mesh); DestroyOwned(quad.Material); }
            _quads.Clear();
            foreach (var entry in _textures.Values) DestroyOwned(entry.Texture);
            _textures.Clear(); AllocatedPixels = 0;
        }
        private static void DestroyOwned(UnityEngine.Object obj)
        { if (obj == null) return; if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
        public void Dispose() { if (_disposed) return; Clear(); _disposed = true; }
        private void OnDisable() { if (!_disposed) Clear(); }
        private void OnDestroy() => Dispose();
    }
}
