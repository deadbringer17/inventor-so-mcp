using System.Collections.Generic;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Selection;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.Rendering;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// Local highlight, in the semantic selection colour (<see cref="UiTheme.Selection"/>): a tinted occurrence plus a rim around
    /// it (inverted hull of the same mesh), or a fill over the selected face's triangles plus a line on its boundary. A thinner,
    /// lighter rim marks the body under the ray before anything is selected (hover). Materials are shared instances created
    /// once; selecting allocates only the per-selection GameObjects and face meshes, never per frame.
    /// </summary>
    public sealed class SelectionVisuals : MonoBehaviour
    {
        /// <summary>Bodies of one selection that get a hull rim; larger selections keep the tint and drop the rim (draw-call budget).</summary>
        public const int MaxRimBodies = 48;
        private const float OutlineLift = 0.0008f;

        [SerializeField] private CadSceneView view;
        [SerializeField] private Material occurrenceMaterial;
        [SerializeField] private Material faceMaterial;
        private readonly List<(Renderer renderer, Material original)> _tinted = new List<(Renderer, Material)>();
        private readonly List<(GameObject overlay, Mesh mesh)> _overlays = new List<(GameObject, Mesh)>();
        private readonly List<GameObject> _rims = new List<GameObject>();
        private readonly List<GameObject> _outlines = new List<GameObject>();
        private readonly List<(Transform body, Bounds local)> _faceBounds = new List<(Transform, Bounds)>();
        private readonly List<CadBody> _selectedBodies = new List<CadBody>();
        private Material _rimMaterial, _hoverMaterial, _outlineMaterial;
        private GameObject _hover;
        private CadBody _hoverBody;

        /// <summary>Shared rim material of the selected body (null if the overlay shader is missing).</summary>
        public Material RimMaterial => EnsureMaterials() ? _rimMaterial : null;
        /// <summary>Shared rim material of the hovered body: lighter and thinner than the selection rim.</summary>
        public Material HoverMaterial => EnsureMaterials() ? _hoverMaterial : null;
        /// <summary>Shared line material of face boundaries.</summary>
        public Material OutlineMaterial => EnsureMaterials() ? _outlineMaterial : null;
        /// <summary>Hull rims currently drawn around the selected bodies.</summary>
        public int RimCount => _rims.Count;
        /// <summary>Boundary lines currently drawn around the selected face.</summary>
        public int OutlineCount => _outlines.Count;
        /// <summary>The body whose hover rim is requested (it is drawn only when it is not part of the selection).</summary>
        public CadBody HoverBody => _hoverBody;
        public bool HoverVisible => _hover != null && _hover.activeSelf;
        public bool HasHighlight => _tinted.Count > 0 || _overlays.Count > 0;
        /// <summary>The selection last passed to <see cref="Show"/> and still drawn (None after Clear or <see cref="ShowOccurrences"/>).</summary>
        public Selection Current { get; private set; } = Selection.None;

        public void Configure(CadSceneView sceneView, Material occurrence, Material face)
        {
            if (view != null) view.Rebuilt -= OnRebuilt;
            view = sceneView;
            occurrenceMaterial = occurrence;
            faceMaterial = face;
            // The assets in the scene predate the semantic tokens: the theme decides the colours, whatever the asset says.
            if (occurrence != null && occurrence.HasProperty("_BaseColor")) occurrence.SetColor("_BaseColor", UiTheme.Selection);
            if (face != null && face.HasProperty("_Color"))
                face.SetColor("_Color", new Color(UiTheme.Selection.r, UiTheme.Selection.g, UiTheme.Selection.b, UiTheme.SelectionFaceAlpha));
            view.Rebuilt += OnRebuilt;
        }

        private void OnRebuilt() { ClearCore(); ClearHover(); }
        private void OnEnable() { if (view != null) { view.Rebuilt -= OnRebuilt; view.Rebuilt += OnRebuilt; } }
        private void OnDisable() { if (view != null) view.Rebuilt -= OnRebuilt; ClearCore(); ClearHover(); }

        public void Show(Selection selection)
        {
            ClearCore();
            if (selection == null || selection.Kind == SelectionKind.None) { RefreshHover(); return; }
            var instance = view.Find(selection.OccurrenceId);
            if (instance == null) { RefreshHover(); return; }
            foreach (var body in instance.Bodies)
            {
                if (selection.Kind == SelectionKind.Occurrence)
                {
                    Tint(body);
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
                _selectedBodies.Add(body);
                Outline(body, range);
            }
            if (HasHighlight) Current = selection;
            RefreshHover();
        }

        public void ShowOccurrences(IEnumerable<string> occurrenceIds)
        {
            ClearCore();
            foreach (var id in occurrenceIds)
            {
                var instance = view.Find(id);
                if (instance == null) continue;
                foreach (var body in instance.Bodies) Tint(body);
            }
            RefreshHover();
        }

        private void Tint(CadBody body)
        {
            _tinted.Add((body.Renderer, body.Renderer.sharedMaterial));
            body.Renderer.sharedMaterial = occurrenceMaterial;
            _selectedBodies.Add(body);
            if (_rims.Count >= MaxRimBodies || !EnsureMaterials()) return;
            _rims.Add(HullChild(body, "SelectionRim", _rimMaterial));
        }

        private void Outline(CadBody body, FaceRange range)
        {
            _faceBounds.Add((body.transform, FaceLocalBounds(body.Mesh, range)));
            if (!EnsureMaterials()) return;
            var chains = MeshFactory.FaceBoundary(body.Mesh, range, out var normal);
            var lift = normal * OutlineLift;
            foreach (var chain in chains)
            {
                if (chain.Length < 2) continue;
                var go = new GameObject("FaceOutline");
                go.transform.SetParent(body.transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.alignment = LineAlignment.View;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.widthMultiplier = UiTheme.SelectionLineWidth;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sharedMaterial = _outlineMaterial;
                line.positionCount = chain.Length;
                for (int i = 0; i < chain.Length; i++) line.SetPosition(i, chain[i] + lift);
                _outlines.Add(go);
            }
        }

        private static Bounds FaceLocalBounds(Mesh mesh, FaceRange range)
        {
            var positions = mesh.vertices;
            var indices = mesh.GetIndices(0);
            var bounds = new Bounds(positions[indices[range.FirstIndex]], Vector3.zero);
            for (int i = range.FirstIndex + 1; i < range.FirstIndex + range.IndexCount; i++) bounds.Encapsulate(positions[indices[i]]);
            return bounds;
        }

        /// <summary>World-space bounds of everything highlighted (tinted bodies, selected face); false when nothing is.</summary>
        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var (renderer, _) in _tinted)
            {
                if (renderer == null) continue;
                if (!any) { bounds = renderer.bounds; any = true; } else bounds.Encapsulate(renderer.bounds);
            }
            foreach (var (body, local) in _faceBounds)
            {
                if (body == null) continue;
                var c = local.center; var e = local.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = body.TransformPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                    if (!any) { bounds = new Bounds(corner, Vector3.zero); any = true; } else bounds.Encapsulate(corner);
                }
            }
            return any;
        }

        /// <summary>Rim of the body under the ray; null (or a selected body) hides it. Cheap: one pooled GameObject is re-parented.</summary>
        public void Hover(CadBody body)
        {
            if (body == _hoverBody && HoverVisible == (body != null && !IsSelected(body))) return;
            _hoverBody = body;
            RefreshHover();
        }

        public void ClearHover() { _hoverBody = null; if (_hover != null) _hover.SetActive(false); }

        private bool IsSelected(CadBody body) => _selectedBodies.Contains(body);

        private void RefreshHover()
        {
            var body = _hoverBody;
            if (body == null || IsSelected(body) || !EnsureMaterials()) { if (_hover != null) _hover.SetActive(false); return; }
            if (_hover == null)
            {
                _hover = new GameObject("HoverRim");
                _hover.AddComponent<MeshFilter>();
                var renderer = _hover.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _hoverMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            _hover.transform.SetParent(body.transform, false);
            _hover.GetComponent<MeshFilter>().sharedMesh = body.Mesh;
            _hover.SetActive(true);
        }

        private static GameObject HullChild(CadBody body, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(body.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = body.Mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private bool EnsureMaterials()
        {
            if (_rimMaterial != null) return true;
            var shader = Shader.Find("XrSo/HighlightOverlay");
            if (shader == null) return false;
            _rimMaterial = Overlay(shader, "Selection rim", UiTheme.SelectionOutline, 1f, (float)CullMode.Front, UiTheme.SelectionRimWidth);
            _hoverMaterial = Overlay(shader, "Hover rim", UiTheme.SelectionHover, 0.9f, (float)CullMode.Front, UiTheme.SelectionHoverRimWidth);
            _outlineMaterial = Overlay(shader, "Selection outline", UiTheme.SelectionOutline, 1f, (float)CullMode.Off, 0f);
            return true;
        }

        private static Material Overlay(Shader shader, string name, Color color, float alpha, float cull, float expand)
        {
            var material = new Material(shader) { name = name };
            material.SetColor("_Color", new Color(color.r, color.g, color.b, alpha));
            material.SetFloat("_Cull", cull);
            material.SetFloat("_Expand", expand);
            return material;
        }

        /// <summary>Removes the highlight; the hover rim comes back if the ray is still on a body that is no longer selected.</summary>
        public void Clear() { ClearCore(); RefreshHover(); }

        private void ClearCore()
        {
            foreach (var (renderer, original) in _tinted) if (renderer != null) renderer.sharedMaterial = original;
            _tinted.Clear();
            Current = Selection.None;
            _selectedBodies.Clear();
            _faceBounds.Clear();
            foreach (var go in _rims) Release(go);
            _rims.Clear();
            foreach (var go in _outlines) Release(go);
            _outlines.Clear();
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

        private static void Release(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private void OnDestroy()
        {
            if (view != null) view.Rebuilt -= OnRebuilt;
            if (_hover != null) Release(_hover);
            foreach (var material in new[] { _rimMaterial, _hoverMaterial, _outlineMaterial })
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            }
            _rimMaterial = _hoverMaterial = _outlineMaterial = null;
        }
    }
}
