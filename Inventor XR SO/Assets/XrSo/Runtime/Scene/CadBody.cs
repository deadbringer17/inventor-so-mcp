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
