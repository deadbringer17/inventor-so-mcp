using TMPro;
using System.Collections.Generic;
using System.Globalization;
using InventorXrSo.Unity.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Measurements are in model metres, never display metres. Revision changes clear all anchors.</summary>
    public sealed class MeasurementView : MonoBehaviour
    {
        private readonly List<GameObject> _pinned = new List<GameObject>();
        private GameObject _temporary;
        private Vector3? _first;
        private Material _material;
        private Transform _head;
        public bool Measuring { get; private set; }
        public float? DistanceMm { get; private set; }
        public int PinnedCount => _pinned.Count;
        public bool HasFirstPoint => _first.HasValue;
        public void Initialize(Material material, Transform head) { _material = material; _head = head; }
        public void Begin() { ClearTemporary(); Measuring = true; }
        public void Cancel() { ClearTemporary(); Measuring = false; }
        public void Pick(Vector3 worldPoint)
        {
            if (!Measuring) return;
            var local = transform.InverseTransformPoint(worldPoint);
            if (!_first.HasValue) { _first = local; Draw(local, local, "Punto 1"); return; }
            DistanceMm = Vector3.Distance(_first.Value, local) * 1000f;
            Draw(_first.Value, local, "≈ " + DistanceMm.Value.ToString("0.###", CultureInfo.GetCultureInfo("it-IT")) + " mm");
            Measuring = false;
        }
        public bool Pin()
        {
            if (!DistanceMm.HasValue || _temporary == null || _pinned.Count >= 20) return false;
            _pinned.Add(_temporary); _temporary = null; _first = null; DistanceMm = null;
            return true;
        }
        public void ClearAll()
        {
            Cancel();
            foreach (var item in _pinned) Release(item);
            _pinned.Clear();
        }
        private void ClearTemporary()
        {
            if (_temporary != null) Release(_temporary);
            _temporary = null; _first = null; DistanceMm = null;
        }
        private void Draw(Vector3 a, Vector3 b, string text)
        {
            if (_temporary != null) Release(_temporary);
            _temporary = new GameObject("Misura temporanea");
            _temporary.transform.SetParent(transform, false);
            var line = _temporary.AddComponent<LineRenderer>();
            line.sharedMaterial = _material; line.useWorldSpace = false; line.positionCount = 2;
            line.SetPosition(0, a); line.SetPosition(1, b);
            var canvas = UiFactory.WorldCanvas(_temporary.transform, "Quota", new Vector2(300, 38));
            canvas.transform.localPosition = (a + b) * 0.5f;
            var label = UiFactory.Label(canvas.transform, text, 24);
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            UiFactory.Stretch(label.rectTransform);
            UpdatePresentation(_temporary);
        }
        private void LateUpdate()
        {
            if (_temporary != null) UpdatePresentation(_temporary);
            foreach (var item in _pinned) UpdatePresentation(item);
        }
        private void UpdatePresentation(GameObject item)
        {
            var scale = Mathf.Max(0.0001f, transform.lossyScale.x);
            item.GetComponent<LineRenderer>().widthMultiplier = 0.002f / scale;
            var label = item.transform.GetChild(0);
            label.localScale = Vector3.one * (0.001f / scale);
            if (_head != null) label.rotation = _head.rotation;
        }
        private static void Release(GameObject item)
        {
            item.SetActive(false);
            if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
        }
    }
}
