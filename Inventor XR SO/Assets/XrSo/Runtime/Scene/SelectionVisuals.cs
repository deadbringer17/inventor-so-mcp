using System.Collections.Generic;
using InventorXrSo.Core.Selection;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Local highlight: tinted occurrence, or an overlay on the selected face's triangles.</summary>
    public sealed class SelectionVisuals : MonoBehaviour
    {
        [SerializeField] private CadSceneView view;
        [SerializeField] private Material occurrenceMaterial;
        [SerializeField] private Material faceMaterial;
        private readonly List<(Renderer renderer, Material original)> _tinted = new List<(Renderer, Material)>();
        private readonly List<(GameObject overlay, Mesh mesh)> _overlays = new List<(GameObject, Mesh)>();

        public void Configure(CadSceneView sceneView, Material occurrence, Material face)
        {
            if (view != null) view.Rebuilt -= Clear;
            view = sceneView;
            occurrenceMaterial = occurrence;
            faceMaterial = face;
            view.Rebuilt += Clear;
        }

        private void OnEnable() { if (view != null) { view.Rebuilt -= Clear; view.Rebuilt += Clear; } }
        private void OnDisable() { if (view != null) view.Rebuilt -= Clear; Clear(); }

        public void Show(Selection selection)
        {
            Clear();
            if (selection == null || selection.Kind == SelectionKind.None) return;
            var instance = view.Find(selection.OccurrenceId);
            if (instance == null) return;
            foreach (var body in instance.Bodies)
            {
                if (selection.Kind == SelectionKind.Occurrence)
                {
                    _tinted.Add((body.Renderer, body.Renderer.sharedMaterial));
                    body.Renderer.sharedMaterial = occurrenceMaterial;
                    continue;
                }
                var range = body.Primitive.FaceMap.Find(selection.FaceId);
                if (range == null) continue;
                var overlay = new GameObject("FaceHighlight");
                overlay.transform.SetParent(body.transform, false);
                var mesh = MeshFactory.BuildFaceOverlay(body.Mesh, range);
                overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
                overlay.AddComponent<MeshRenderer>().sharedMaterial = faceMaterial;
                _overlays.Add((overlay, mesh));
            }
        }

        public void Clear()
        {
            foreach (var (renderer, original) in _tinted) if (renderer != null) renderer.sharedMaterial = original;
            _tinted.Clear();
            foreach (var (overlay, mesh) in _overlays)
            {
                // In EditMode a rebuild may already have destroyed the overlay GameObject.
                // The generated mesh still has independent ownership and must be released.
                if (overlay != null) overlay.SetActive(false);
                if (Application.isPlaying)
                {
                    if (mesh != null) Destroy(mesh);
                    if (overlay != null) Destroy(overlay);
                }
                else
                {
                    if (mesh != null) DestroyImmediate(mesh);
                    if (overlay != null) DestroyImmediate(overlay);
                }
            }
            _overlays.Clear();
        }

        public void ShowOccurrences(IEnumerable<string> occurrenceIds)
        {
            Clear();
            foreach (var id in occurrenceIds)
            {
                var instance = view.Find(id);
                if (instance == null) continue;
                foreach (var body in instance.Bodies)
                {
                    _tinted.Add((body.Renderer, body.Renderer.sharedMaterial));
                    body.Renderer.sharedMaterial = occurrenceMaterial;
                }
            }
        }
    }
}
