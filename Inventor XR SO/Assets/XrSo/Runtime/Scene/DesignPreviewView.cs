using System;
using System.Collections.Generic;
using InventorXrSo.Core.Backend;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Real CAD preview at model scale, with the original translucent and no pickable preview topology.</summary>
    public sealed class DesignPreviewView : MonoBehaviour
    {
        private CadSceneView _view;
        private Transform _head;
        private Material _ghost, _solid, _sketch;
        private GameObject _previewRoot;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<(Renderer renderer, Material original)> _originals = new List<(Renderer, Material)>();
        public string PlanId { get; private set; }
        public bool IsShowing => _previewRoot != null && _previewRoot.activeSelf;

        public void Initialize(CadSceneView view, Transform head = null)
        {
            if (_view != null) throw new InvalidOperationException("Preview view is already initialized.");
            if (view == null || view.BodyMaterial == null) throw new ArgumentException("A CAD view with a body material is required.");
            var shader = Shader.Find("XrSo/HighlightOverlay");
            if (shader == null) throw new InvalidOperationException("CAD ghost shader is unavailable.");
            _view = view; _head=head;
            _ghost = new Material(shader) { name = "Design original ghost" };
            _ghost.SetColor("_Color", new Color(0.65f, 0.73f, 0.83f, 0.22f));
            _sketch = new Material(shader) { name = "Design sketch result" };
            _sketch.SetColor("_Color", new Color(0.15f, 0.95f, 0.75f, 1));
            _solid = new Material(view.BodyMaterial) { name = "Design result" };
            _solid.SetColor("_BaseColor", new Color(0.2f, 0.76f, 0.9f, 1));
            _view.Rebuilt += Clear;
        }

        /// <summary>Caller may confirm the preview rendered only after this method succeeds.</summary>
        public void Show(DesignPreview preview)
        {
            if (_view == null) throw new InvalidOperationException("Initialize the preview view first.");
            if (preview == null) { Clear(); return; }
            // A Design preview is a part definition in that part's model space, never an assembly instance.
            if (preview.IsAssembly && _view.DocumentId != preview.DocumentId)
                throw new InvalidOperationException("Assembly preview belongs to another document.");
            foreach (var instance in _view.Instances)
                if (!preview.IsAssembly && (instance.DefinitionId != preview.DocumentId || !string.IsNullOrEmpty(instance.OccurrenceId)))
                    throw new InvalidOperationException("Design preview requires the active standalone part.");
            Clear();
            try
            {
                _previewRoot = new GameObject("Design Preview");
                _previewRoot.transform.SetParent(_view.transform, false);
                foreach (var primitive in preview.Model.Primitives)
                {
                    if (!primitive.Visible) continue;
                    var mesh = MeshFactory.Build(primitive); _meshes.Add(mesh);
                    var body = new GameObject(primitive.BodyName);
                    body.transform.SetParent(_previewRoot.transform, false);
                    body.AddComponent<MeshFilter>().sharedMesh = mesh;
                    body.AddComponent<MeshRenderer>().sharedMaterial = _solid;
                    // No collider/CadBody: aborted topology must never become an authoring reference.
                }
                foreach (var instance in _view.Instances)
                foreach (var body in instance.Bodies)
                {
                    _originals.Add((body.Renderer, body.Renderer.sharedMaterial));
                    body.Renderer.sharedMaterial = _ghost;
                }
                foreach (var sketch in preview.Sketches)
                    if (sketch.Visible)
                    {
                        foreach (var element in sketch.Elements)
                            CadCoordinates.Line(_previewRoot.transform, sketch.Name, System.Linq.Enumerable.Select(element.Outline(), sketch.Frame.ToModel), _sketch);
                        foreach (var dimension in sketch.Dimensions)
                            SketchDimensionLabel.Create(_previewRoot.transform,_view.transform,_head,sketch.Frame.ToModel(dimension.TextPoint),
                                dimension.Name+" = "+dimension.Expression+(dimension.Driven ? " (riferimento)" : ""));
                    }
                PlanId = preview.PlanId;
            }
            catch { Clear(); throw; }
        }

        public void Clear()
        {
            PlanId = null;
            foreach (var entry in _originals)
                if (entry.renderer != null) entry.renderer.sharedMaterial = entry.original;
            _originals.Clear();
            if (_previewRoot != null) { _previewRoot.SetActive(false); Release(_previewRoot); _previewRoot = null; }
            foreach (var mesh in _meshes) Release(mesh);
            _meshes.Clear();
        }

        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (_view != null) _view.Rebuilt -= Clear;
            Clear(); Release(_ghost); Release(_solid); Release(_sketch);
        }
        private static void Release(UnityEngine.Object value)
        { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
