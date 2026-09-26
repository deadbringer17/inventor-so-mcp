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
        private readonly IInventorBackend _backend;

        public SceneLoader(IInventorBackend backend) { _backend = backend; }

        public async Task<LoadedScene> LoadAsync(CancellationToken ct)
        {
            var graph = await _backend.GetSceneGraphAsync(ct);
            var models = new Dictionary<string, GlbModel>();
            var assetIds = new Dictionary<string, string>();
            var omitted = new List<string>();
            foreach (var id in graph.DefinitionIds)
            {
                if (models.Count >= MaxDefinitions)
                {
                    omitted.Add(id);
                    continue;
                }
                try
                {
                    var mesh = await _backend.GetDefinitionMeshAsync(id, ct);
                    models[id] = GlbModel.Parse(await _backend.GetAssetAsync(mesh, ct));
                    assetIds[id] = mesh.AssetId;
                }
                catch (McpToolException ex) when (ex.Code == "MESH_TOO_LARGE")
                {
                    omitted.Add(id);
                }
            }
            return new LoadedScene(graph, models, assetIds, omitted);
        }
    }
}
