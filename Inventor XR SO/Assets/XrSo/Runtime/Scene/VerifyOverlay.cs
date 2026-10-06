using TMPro;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// M7 drawings in model space (child of the CadSceneView root, model metres): red overlay on interfering occurrences, red
    /// wireframe boxes of the interference bodies, and the minimum-distance line with its label. Never touches Inventor.
    /// </summary>
    public sealed class VerifyOverlay : MonoBehaviour
    {
        public static readonly Color Red = new Color(0.95f, 0.2f, 0.15f, 1f);
        // Corner order: bottom 0..3 (z min), top 4..7 (z max); this path walks all 12 edges in one polyline.
        private static readonly int[] BoxPath = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };

        private readonly List<GameObject> _boxes = new List<GameObject>();
        private readonly List<GameObject> _tints = new List<GameObject>();
        private GameObject _distance;
        private Material _line, _tint;
        private Transform _head;

        public int BoxCount => _boxes.Count;
        public int TintedBodies => _tints.Count;
        public bool HasDistance => _distance != null;
        public string DistanceLabel { get; private set; }

        /// <summary>Red pulse after a verification view opens: length, frequency and alpha depth (0..1) of the dip.</summary>
        public const float PulseSeconds = 5f;
        public const float PulseHz = 1.2f;
        public const float PulseDepth = 0.6f;
        private const float BoxWidth = 0.0015f;
        private static readonly int PulseWaveId = Shader.PropertyToID("_PulseWave");
        private static readonly int PulseDepthId = Shader.PropertyToID("_PulseDepth");

        private float _pulseStart;

        /// <summary>True while the red tint and boxes are pulsing (first 5 s after StartPulse).</summary>
        public bool Pulsing { get; private set; }
        /// <summary>The shared red tint material (exposed for tests).</summary>
        public Material TintMaterial => _tint;

        public void Initialize(Material lineMaterial, Transform head)
        {
            _line = lineMaterial; _head = head;
            _tint = GhostBodies.CreateMaterial("Verify red", new Color(Red.r, Red.g, Red.b, 0.45f));
        }

        /// <summary>Starts the 5 s pulse on the tint and the boxes now.</summary>
        public void StartPulse() => StartPulse(Time.unscaledTime);

        /// <summary>Starts the pulse at the given clock value (Time.unscaledTime in play; injectable for tests).</summary>
        public void StartPulse(float now)
        {
            Pulsing = true; _pulseStart = now;
            if (_tint != null) _tint.SetFloat(PulseDepthId, PulseDepth);
            TickPulse(now);
        }

        /// <summary>Advances the pulse to the given clock value; after PulseSeconds it stops and everything goes back to fixed values.</summary>
        public void TickPulse(float now)
        {
            if (!Pulsing) return;
            float elapsed = now - _pulseStart;
            if (elapsed >= PulseSeconds) { StopPulse(); return; }
            float wave = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * PulseHz * Mathf.Max(0f, elapsed));
            if (_tint != null) _tint.SetFloat(PulseWaveId, wave);
            ApplyBoxPulse(BoxWidth * (1f + wave), 1f - PulseDepth * wave);
        }

        private void StopPulse()
        {
            Pulsing = false;
            if (_tint != null) { _tint.SetFloat(PulseWaveId, 0f); _tint.SetFloat(PulseDepthId, 0f); }
            ApplyBoxPulse(BoxWidth, 1f);
        }

        private void ApplyBoxPulse(float width, float alpha)
        {
            var color = new Color(Red.r, Red.g, Red.b, alpha);
            foreach (var go in _boxes)
            {
                if (go == null) continue;
                var line = go.GetComponent<LineRenderer>();
                if (line == null) continue;
                line.widthMultiplier = width;
                line.startColor = line.endColor = color;
            }
        }

        private void Update()
        {
            if (Pulsing) TickPulse(Time.unscaledTime);
        }

        /// <summary>Assembly millimetres (Inventor axes, as the GLB) to model metres in Unity: X mirrored like every mesh.</summary>
        public static Vector3 ToLocal(double[] mm) => new Vector3(-(float)(mm[0] / 1000.0), (float)(mm[1] / 1000.0), (float)(mm[2] / 1000.0));

        public void Tint(IEnumerable<CadInstance> instances)
        {
            foreach (var instance in instances.Where(i => i != null))
                foreach (var body in instance.Bodies)
                {
                    var overlay = new GameObject("VerifyTint");
                    overlay.transform.SetParent(body.transform, false);
                    overlay.AddComponent<MeshFilter>().sharedMesh = body.Mesh;
                    overlay.AddComponent<MeshRenderer>().sharedMaterial = _tint;
                    _tints.Add(overlay);
                }
        }

        public void ShowBoxes(IEnumerable<VerifyBox> boxes)
        {
            foreach (var box in boxes ?? Enumerable.Empty<VerifyBox>())
            {
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    corners[i] = ToLocal(new[]
                    {
                        (i == 1 || i == 2 || i == 5 || i == 6) ? box.MaxMm[0] : box.MinMm[0],
                        (i == 2 || i == 3 || i == 6 || i == 7) ? box.MaxMm[1] : box.MinMm[1],
                        i >= 4 ? box.MaxMm[2] : box.MinMm[2],
                    });
                var go = new GameObject("InterferenceBox");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = _line; line.useWorldSpace = false; line.widthMultiplier = BoxWidth;
                line.startColor = line.endColor = Red;
                line.positionCount = BoxPath.Length;
                for (int i = 0; i < BoxPath.Length; i++) line.SetPosition(i, corners[BoxPath[i]]);
                _boxes.Add(go);
            }
        }

        public void ShowDistance(Vector3 aLocal, Vector3 bLocal, string label)
        {
            ClearDistance();
            _distance = new GameObject("MinimumDistance");
            _distance.transform.SetParent(transform, false);
            var line = _distance.AddComponent<LineRenderer>();
            line.sharedMaterial = _line; line.useWorldSpace = false; line.widthMultiplier = 0.002f; line.positionCount = 2;
            line.SetPosition(0, aLocal); line.SetPosition(1, bLocal);
            var canvas = UiFactory.WorldCanvas(_distance.transform, "Distanza minima", new Vector2(420, 38));
            canvas.transform.localPosition = (aLocal + bLocal) * 0.5f;
            var text = UiFactory.Label(canvas.transform, label, 24);
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            UiFactory.Stretch(text.rectTransform);
            DistanceLabel = label;
        }

        public void ClearDistance()
        {
            if (_distance != null) Release(_distance);
            _distance = null; DistanceLabel = null;
        }

        public void Clear()
        {
            if (Pulsing) StopPulse();
            foreach (var go in _boxes) Release(go);
            foreach (var go in _tints) Release(go);
            _boxes.Clear(); _tints.Clear();
            ClearDistance();
        }

        /// <summary>
        /// Indicative closest points between two groups of instances, from mesh vertices (at most ~2000 per group). Used only when
        /// Inventor gives no points: the value shown is always Inventor's.
        /// </summary>
        public static bool ClosestVertices(IEnumerable<CadInstance> a, IEnumerable<CadInstance> b, Transform root, out Vector3 aLocal, out Vector3 bLocal)
        {
            var left = Sample(a, root); var right = Sample(b, root);
            aLocal = bLocal = default;
            if (left.Count == 0 || right.Count == 0) return false;
            float best = float.MaxValue;
            foreach (var p in left)
                foreach (var q in right)
                {
                    float d = (p - q).sqrMagnitude;
                    if (d < best) { best = d; aLocal = p; bLocal = q; }
                }
            return true;
        }

        private static List<Vector3> Sample(IEnumerable<CadInstance> instances, Transform root)
        {
            var bodies = instances.Where(i => i != null).SelectMany(i => i.Bodies).Where(b => b != null && b.Mesh != null).ToList();
            int total = bodies.Sum(b => b.Mesh.vertexCount);
            int stride = Mathf.Max(1, total / 2000);
            var points = new List<Vector3>();
            foreach (var body in bodies)
            {
                var vertices = body.Mesh.vertices;
                for (int i = 0; i < vertices.Length; i += stride) points.Add(root.InverseTransformPoint(body.transform.TransformPoint(vertices[i])));
            }
            return points;
        }

        private void LateUpdate()
        {
            if (_distance == null || _head == null) return;
            var canvas = _distance.GetComponentInChildren<Canvas>();
            if (canvas != null) canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - _head.position);
        }

        private void OnDestroy()
        {
            Clear();
            if (_tint != null) { if (Application.isPlaying) Destroy(_tint); else DestroyImmediate(_tint); }
        }

        private static void Release(GameObject go)
        {
            if (go == null) return; // already destroyed with its parent
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
