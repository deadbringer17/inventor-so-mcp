using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Mcp;

namespace InventorXrSo.Core.Session
{
    public sealed class LoadedScene
    {
        public LoadedScene(SceneGraph graph, IReadOnlyDictionary<string, GlbModel> models, IReadOnlyDictionary<string, string> assetIds, IReadOnlyList<string> omitted)
        {
            Graph = graph;
            Models = models;
            AssetIds = assetIds;
            Omitted = omitted;
        }

        public SceneGraph Graph { get; }
        /// <summary>Parsed mesh per definition document id.</summary>
        public IReadOnlyDictionary<string, GlbModel> Models { get; }
        /// <summary>Asset id per definition: unchanged id = unchanged geometry, so the view can reuse its meshes.</summary>
        public IReadOnlyDictionary<string, string> AssetIds { get; }
        /// <summary>Definitions not shown (too large, or beyond <see cref="SceneLoader.MaxDefinitions"/>).</summary>
        public IReadOnlyList<string> Omitted { get; }
    }

    /// <summary>Scene graph plus one mesh per distinct definition (per-definition calls, so one oversized part does not sink the scene).</summary>
    public sealed class SceneLoader
    {
        public const int MaxDefinitions = 200;
        /// <summary>Definitions fetched at the same time: each costs two round-trips, so serial loading is slow on the headset link.</summary>
        public const int MaxParallelFetches = 4;
        private readonly IInventorBackend _backend;

        public SceneLoader(IInventorBackend backend) { _backend = backend; }

        private sealed class FetchResult
        {
            public GlbModel Model;
            public string AssetId;
            public bool Omitted;
        }

        public async Task<LoadedScene> LoadAsync(CancellationToken ct)
        {
            var graph = await _backend.GetSceneGraphAsync(ct);
            var models = new Dictionary<string, GlbModel>();
            var assetIds = new Dictionary<string, string>();
            var omitted = new List<string>();
            var ids = new List<string>(graph.DefinitionIds);
            int next = 0;
            // Waves of at most (MaxDefinitions - loaded) ids: the cap counts loaded definitions only, as when loading
            // serially, so omitted ones never use up a slot. Results are applied in id order within and across waves.
            while (next < ids.Count)
            {
                int room = MaxDefinitions - models.Count;
                if (room <= 0)
                {
                    for (; next < ids.Count; next++) omitted.Add(ids[next]);
                    break;
                }
                int count = Math.Min(room, ids.Count - next);
                var wave = ids.GetRange(next, count);
                next += count;
                var results = await FetchAllAsync(wave, ct);
                for (int i = 0; i < wave.Count; i++)
                {
                    if (results[i].Omitted) { omitted.Add(wave[i]); continue; }
                    models[wave[i]] = results[i].Model;
                    assetIds[wave[i]] = results[i].AssetId;
                }
            }
            return new LoadedScene(graph, models, assetIds, omitted);
        }

        private async Task<FetchResult[]> FetchAllAsync(List<string> wave, CancellationToken ct)
        {
            var results = new FetchResult[wave.Count];
            using (var gate = new SemaphoreSlim(MaxParallelFetches))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                Exception fatal = null;
                // No Task.Run: backend calls start and resume on the caller's context (Unity transports need the main thread).
                var tasks = new Task[wave.Count];
                for (int i = 0; i < wave.Count; i++)
                {
                    int index = i;
                    tasks[i] = ((Func<Task>)(async () =>
                    {
                        try
                        {
                            await gate.WaitAsync(linked.Token);
                            try
                            {
                                if (linked.IsCancellationRequested) return;   // a sibling failed or the caller cancelled
                                results[index] = await FetchOneAsync(wave[index], linked.Token);
                            }
                            finally { gate.Release(); }
                        }
                        catch (OperationCanceledException) when (linked.IsCancellationRequested)
                        {
                            // Cancelled by the caller or by the first fatal error; reported after the join.
                        }
                        catch (Exception ex)
                        {
                            if (Interlocked.CompareExchange(ref fatal, ex, null) == null) linked.Cancel();
                        }
                    }))();
                }
                await Task.WhenAll(tasks);
                ct.ThrowIfCancellationRequested();
                if (fatal != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fatal).Throw();
            }
            return results;
        }

        private async Task<FetchResult> FetchOneAsync(string id, CancellationToken ct)
        {
            try
            {
                var mesh = await _backend.GetDefinitionMeshAsync(id, ct);
                var model = GlbModel.Parse(await _backend.GetAssetAsync(mesh, ct));
                return new FetchResult { Model = model, AssetId = mesh.AssetId };
            }
            catch (McpException ex) when (ex.Code == "MESH_TOO_LARGE" || ex.Code == "ASSET_CORRUPT")
            {
                return new FetchResult { Omitted = true };
            }
            catch (Exception ex) when (ex is FormatException || ex is NotSupportedException)
            {
                return new FetchResult { Omitted = true };
            }
        }
    }
}
