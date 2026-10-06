using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Draws a <see cref="LoadedScene"/>: one mesh per definition body, instanced per occurrence, with colliders for picking.</summary>
    public sealed class CadSceneView : MonoBehaviour
    {
        [SerializeField] private Material bodyMaterial;
        private readonly Dictionary<string, Mesh[]> _meshesByAsset = new Dictionary<string, Mesh[]>();
        private readonly List<CadInstance> _instances = new List<CadInstance>();
        private readonly Dictionary<string, CadInstance> _byOccurrence = new Dictionary<string, CadInstance>();
        private CadInstance _withoutOccurrence;   // a part document's root has no occurrence id; Find(null) keeps resolving it

        public Material BodyMaterial { get => bodyMaterial; set => bodyMaterial = value; }
        public IReadOnlyList<CadInstance> Instances => _instances;
        public event Action Rebuilt;
        /// <summary>A released instance was detached from this root and is about to be destroyed (destruction is deferred in play mode).</summary>
        public event Action<GameObject> InstanceDetached;
        public SectionPlane Section { get; set; }
        public string DocumentId { get; private set; }

        public CadInstance Find(string occurrenceId) =>
            occurrenceId == null ? _withoutOccurrence : _byOccurrence.TryGetValue(occurrenceId, out var instance) ? instance : null;

        public void Show(LoadedScene scene)
        {
            DocumentId = scene?.Graph.DocumentId;
            foreach (var instance in _instances)
                if (instance != null) { instance.gameObject.SetActive(false); ReleaseInstance(instance.gameObject); }
            _instances.Clear();
            _byOccurrence.Clear();
            _withoutOccurrence = null;
            var used = new HashSet<string>();
            if (scene != null)
            {
                foreach (var placed in scene.Graph.PlacedParts())
                {
                    var definition = placed.Node.DefinitionDocumentId;
                    if (!scene.Models.TryGetValue(definition, out var model)) continue;   // omitted
                    var assetId = scene.AssetIds[definition];
                    used.Add(assetId);
                    if (!_meshesByAsset.TryGetValue(assetId, out var meshes))
                    {
                        meshes = model.Primitives.Select(MeshFactory.Build).ToArray();
                        _meshesByAsset[assetId] = meshes;
                    }
                    var created = CreateInstance(placed.Node.Name ?? definition, placed.Node.OccurrenceId, definition, placed.MatrixGltf, model, meshes);
                    _instances.Add(created);
                    if (created.OccurrenceId == null) { if (_withoutOccurrence == null) _withoutOccurrence = created; }
                    else _byOccurrence.TryAdd(created.OccurrenceId, created);
                }
            }
            foreach (var stale in _meshesByAsset.Keys.Where(k => !used.Contains(k)).ToList())
            {
                foreach (var mesh in _meshesByAsset[stale]) Release(mesh);
                _meshesByAsset.Remove(stale);
            }
            Rebuilt?.Invoke();
        }

        private CadInstance CreateInstance(string name, string occurrenceId, string definition, float[] matrixGltf, GlbModel model, Mesh[] meshes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            MatrixUtil.Apply(go.transform, Handedness.ConvertMatrix(matrixGltf));
            var instance = go.AddComponent<CadInstance>();
            instance.Init(occurrenceId, definition);
            for (int i = 0; i < model.Primitives.Count; i++)
            {
                var primitive = model.Primitives[i];
                if (!primitive.Visible) continue;
                var child = new GameObject(primitive.BodyName);
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                child.AddComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
                var collider = child.AddComponent<MeshCollider>();
                // Do not weld or clean the mesh: collider triangle indices address extras.faces.
                collider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
                collider.sharedMesh = meshes[i];
                var body = child.AddComponent<CadBody>();
                body.Init(instance, primitive, meshes[i]);
                body.Section = Section;
            }
            return instance;
        }

        private void OnDestroy()
        {
            foreach (var meshes in _meshesByAsset.Values)
                foreach (var mesh in meshes) Release(mesh);
            _meshesByAsset.Clear();
        }

        /// <summary>
        /// Destroy is deferred to the end of the frame in play mode: detach first, so the old instances are no longer children of the
        /// root (<see cref="ScenePlacement.LocalBounds"/> walks the children including inactive ones) when the new scene is placed.
        /// </summary>
        private void ReleaseInstance(GameObject instance)
        {
            instance.transform.SetParent(null, false);
            InstanceDetached?.Invoke(instance);
            Release(instance);
        }

        private static void Release(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
