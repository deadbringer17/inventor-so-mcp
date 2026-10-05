using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// M9 "assieme fantasma": la scena del padre diretto, semitrasparente, senza ombre e senza collider, mentre si lavora su una parte
    /// o un sottoassieme. Va messo come figlio della radice della scena (quella che Workbench sposta): Adatta (Y) e Ricentra muovono il
    /// fantasma insieme alla parte. Nessun <see cref="CadBody"/>: non entra nei limiti di Adatta e il ControllerRay non lo vede.
    /// Posa: la parte aperta sta nella propria origine, quindi il fantasma e trasformato con l'inversa della matrice dell'occorrenza
    /// (<see cref="GhostPose"/>), cosi la parte coincide con il suo posto nell'assieme. Si fotografa all'ingresso e non si aggiorna.
    /// </summary>
    public sealed class GhostContext : MonoBehaviour
    {
        public const string LabelText = "contesto: prima delle modifiche";
        public const int MaxDefinitions = SceneLoader.MaxDefinitions;
        private const int IgnoreRaycastLayer = 2;

        private readonly Dictionary<string, Mesh[]> _meshes = new Dictionary<string, Mesh[]>();
        private GameObject _root;
        private Material _material;

        public string Label => LabelText;
        /// <summary>Etichetta di revisione dell'assieme al momento dell'ingresso (null se il fantasma e vuoto).</summary>
        public string RevisionLabel { get; private set; }
        /// <summary>Documento padre mostrato (null se vuoto).</summary>
        public string ParentDocumentId { get; private set; }
        public bool IsShowing => _root != null;
        /// <summary>Il padre supera i limiti di mesh (200 definizioni / omesse / grafo troncato): solo le definizioni caricate.</summary>
        public bool Truncated { get; private set; }
        public int RendererCount => _root == null ? 0 : _root.GetComponentsInChildren<MeshRenderer>(true).Length;
        public Material Material => _material;
        public bool HasSelectableColliders => _root != null && _root.GetComponentsInChildren<Collider>(true).Any(c => c.enabled);

        /// <param name="parent">Scena dell'assieme padre (livello sotto nella pila).</param>
        /// <param name="occurrencePose">16 float glTF dell'occorrenza nel padre; null = nessuna rimappatura (identita).</param>
        /// <param name="revisionLabel">Revisione dell'assieme all'ingresso; se null si usa quella del grafo.</param>
        /// <param name="skipOccurrenceId">Occorrenza appena aperta: e gia disegnata dalla scena reale, non si duplica.</param>
        public void Show(LoadedScene parent, float[] occurrencePose, string revisionLabel, string skipOccurrenceId = null)
        {
            Clear();
            if (parent == null) return;
            EnsureMaterial();
            _root = new GameObject("GhostContext") { layer = IgnoreRaycastLayer };
            _root.transform.SetParent(transform, false);
            MatrixUtil.Apply(_root.transform, Handedness.ConvertMatrix(GhostPose.Inverse(occurrencePose)));
            ParentDocumentId = parent.Graph.DocumentId;
            RevisionLabel = revisionLabel ?? parent.Graph.Revision;
            Truncated = parent.Omitted.Count > 0 || parent.Graph.Truncated || parent.Models.Count > MaxDefinitions;

            var allowed = new HashSet<string>(parent.Models.Keys.Take(MaxDefinitions));
            foreach (var placed in parent.Graph.PlacedParts())
            {
                var definition = placed.Node.DefinitionDocumentId;
                if (!allowed.Contains(definition) || !parent.Models.TryGetValue(definition, out var model)) continue;
                if (skipOccurrenceId != null && placed.Node.OccurrenceId == skipOccurrenceId) continue;
                if (!_meshes.TryGetValue(definition, out var meshes))
                {
                    meshes = model.Primitives.Select(MeshFactory.Build).ToArray();
                    _meshes[definition] = meshes;
                }
                var go = new GameObject(placed.Node.Name ?? definition) { layer = IgnoreRaycastLayer };
                go.transform.SetParent(_root.transform, false);
                MatrixUtil.Apply(go.transform, Handedness.ConvertMatrix(placed.MatrixGltf));
                for (int i = 0; i < model.Primitives.Count; i++)
                {
                    if (!model.Primitives[i].Visible) continue;
                    var child = new GameObject(model.Primitives[i].BodyName) { layer = IgnoreRaycastLayer };
                    child.transform.SetParent(go.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = _material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
            }
        }

        public void Clear()
        {
            if (_root != null) { _root.SetActive(false); Release(_root); _root = null; }
            foreach (var set in _meshes.Values) foreach (var mesh in set) Release(mesh);
            _meshes.Clear();
            RevisionLabel = null; ParentDocumentId = null; Truncated = false;
        }

        private void EnsureMaterial()
        {
            if (_material != null) return;
            _material = GhostBodies.CreateMaterial("GhostContext", UiTheme.Ghost);
            _material.renderQueue = (int)RenderQueue.Transparent - 10;   // dietro anteprime e selezione
        }

        private void OnDestroy()
        {
            Clear();
            if (_material != null) Release(_material);
        }

        private static void Release(Object target)
        {
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }
    }
}
