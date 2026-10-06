using System;
using System.Collections.Generic;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// Bodies drawn as translucent ghosts: a copy of the mesh with the XrSo/HighlightOverlay shader replaces the body's renderer.
    /// The collider stays, so the ray still hits a ghost. No other material is swapped.
    /// </summary>
    public sealed class GhostBodies
    {
        private readonly Material _material;
        private readonly string _ghostName;
        private readonly Dictionary<CadBody, GameObject> _ghosts = new Dictionary<CadBody, GameObject>();

        public GhostBodies(Material material, string ghostName = "Ghost")
        {
            _material = material ?? throw new ArgumentNullException(nameof(material));
            _ghostName = ghostName;
        }

        public int Count => _ghosts.Count;

        public static Material CreateMaterial(string name, Color color)
        {
            var shader = Shader.Find("XrSo/HighlightOverlay");
            if (shader == null) throw new InvalidOperationException("CAD ghost shader is unavailable.");
            var material = new Material(shader) { name = name };
            material.SetColor("_Color", color);
            return material;
        }

        public bool Contains(CadBody body) => body != null && _ghosts.ContainsKey(body);

        public void Add(CadBody body)
        {
            if (body == null || body.Renderer == null || Contains(body)) return;
            var ghost = new GameObject(_ghostName);
            ghost.transform.SetParent(body.transform, false);
            ghost.AddComponent<MeshFilter>().sharedMesh = body.Mesh;
            ghost.AddComponent<MeshRenderer>().sharedMaterial = _material;
            body.GhostOverlays++;
            body.Renderer.enabled = false;
            _ghosts.Add(body, ghost);
        }

        public void Remove(CadBody body)
        {
            if (body == null || !_ghosts.TryGetValue(body, out var ghost)) return;
            Restore(body, ghost);
            _ghosts.Remove(body);
        }

        public void Clear()
        {
            foreach (var entry in _ghosts) Restore(entry.Key, entry.Value);
            _ghosts.Clear();
        }

        /// <summary>After a scene rebuild the bodies are already destroyed: drop them without touching them.</summary>
        public void Forget() => _ghosts.Clear();

        private static void Restore(CadBody body, GameObject ghost)
        {
            if (body != null)
            {
                if (body.GhostOverlays > 0) body.GhostOverlays--;
                body.RefreshRenderer();
            }
            if (ghost == null) return;
            ghost.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(ghost); else UnityEngine.Object.DestroyImmediate(ghost);
        }
    }
}
