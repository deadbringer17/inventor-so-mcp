using InventorXrSo.Core.Glb;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>One solid body of an instance: what the ray hits, and how to turn the hit triangle into a face.</summary>
    public sealed class CadBody : MonoBehaviour
    {
        public CadInstance Instance { get; private set; }
        public GlbPrimitive Primitive { get; private set; }
        public Mesh Mesh { get; private set; }
        public Renderer Renderer { get; private set; }
        public SectionPlane Section { get; set; }
        /// <summary>Whether the solid renderer should draw when no ghost overlay replaces it. Visibility (Hidden/Ghost) clears it; <see cref="GhostBodies"/> never re-enables a body that does not want it.</summary>
        public bool RendererWanted { get; set; } = true;
        /// <summary>How many <see cref="GhostBodies"/> overlays currently replace the solid renderer.</summary>
        public int GhostOverlays { get; internal set; }
        /// <summary>Set while the flat pattern replaces the folded part; independent of visibility and isolation.</summary>
        public bool FoldedHidden { get; set; }

        /// <summary>Re-applies the renderer state from <see cref="RendererWanted"/> <see cref="GhostOverlays"/> and <see cref="FoldedHidden"/>.</summary>
        public void RefreshRenderer()
        {
            if (Renderer != null) Renderer.enabled = RendererWanted && GhostOverlays == 0 && !FoldedHidden;
        }

        public void Init(CadInstance instance, GlbPrimitive primitive, Mesh mesh)
        {
            Instance = instance;
            Primitive = primitive;
            Mesh = mesh;
            Renderer = GetComponent<Renderer>();
            instance.Add(this);
        }
    }
}
