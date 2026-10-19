using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.Rendering;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Owns native glyph meshes in model coordinates. No colliders or authoring references are created.</summary>
    public sealed class NativeSketchTextLayer : MonoBehaviour,IDisposable
    {
#if XR_SO_ACCEPTANCE
        [UnityEngine.Scripting.Preserve]
#endif
        private sealed class Entry
        {
#if XR_SO_ACCEPTANCE
            [UnityEngine.Scripting.Preserve]
#endif
            public string Id;
#if XR_SO_ACCEPTANCE
            [UnityEngine.Scripting.Preserve]
#endif
            public GameObject Object;
#if XR_SO_ACCEPTANCE
            [UnityEngine.Scripting.Preserve]
#endif
            public Mesh Mesh;
#if XR_SO_ACCEPTANCE
            [UnityEngine.Scripting.Preserve]
#endif
            public MeshRenderer Renderer;
            public Color Color;
        }
        private readonly List<Entry> _entries=new List<Entry>();
        private Material _material;private MaterialPropertyBlock _properties;
        private NativeSketchSnapshot _snapshot;private bool _disposed;
        public int MeshCount=>_entries.Count;
        public static NativeSketchTextLayer Create(Transform parent)
        {
            if(parent==null)throw new ArgumentNullException(nameof(parent));
            var go=new GameObject("Native text glyphs");go.transform.SetParent(parent,false);return go.AddComponent<NativeSketchTextLayer>();
        }
        public void Synchronize(NativeSketchSnapshot snapshot,IReadOnlyList<SketchEntityRef> selected=null)
        {
            if(_disposed)throw new ObjectDisposedException(nameof(NativeSketchTextLayer));
            if(snapshot==null||!isActiveAndEnabled){Clear();return;}
            if(!ReferenceEquals(snapshot,_snapshot))
            {
                Clear();_snapshot=snapshot;
                try{if(snapshot.Visible)foreach(var entity in snapshot.Entities)if(entity.TextGlyphs!=null)Add(entity.TextGlyphs,entity.Reference?.EntityId);}
                catch{Clear();throw;}
            }
            foreach(var entry in _entries)
            {
                bool highlight=false;
                if(selected!=null)for(int i=0;i<selected.Count;i++)if(selected[i].EntityId==entry.Id){highlight=true;break;}
                SetColor(entry,highlight ? UiTheme.Selection : entry.Color);
            }
        }
        public void ShowPreview(IEnumerable<NativeSketchTextGlyphMesh> meshes)
        {
            if(_disposed)throw new ObjectDisposedException(nameof(NativeSketchTextLayer));
            Clear();try{foreach(var mesh in meshes)Add(mesh,null);}catch{Clear();throw;}
        }
        private void Add(NativeSketchTextGlyphMesh source,string id)
        {
            if(source.VerticesMm.Count==0)return;
            if(_material==null)
            {
                var shader=Shader.Find("InventorXrSo/NativeSketchText");
                if(shader==null)throw new InvalidOperationException("Native text shader is unavailable.");
                _material=new Material(shader){name="Native text glyph material"};_properties=new MaterialPropertyBlock();
            }
            Mesh mesh=null;GameObject go=null;
            try
            {
                var vertices=source.VerticesMm.Select(CadCoordinates.ToLocal).ToArray();
                if(vertices.Any(p=>!Finite(p.x)||!Finite(p.y)||!Finite(p.z)))throw new FormatException("Native glyph coordinates exceed the renderer range.");
                mesh=new Mesh{name="Native text glyph mesh",indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.triangles=source.Indices.ToArray();mesh.RecalculateBounds();
                go=new GameObject("Native text glyph");go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=_material;
                var entry=new Entry{Id=id,Object=go,Mesh=mesh,Renderer=renderer,Color=new Color(source.ColorRgb[0]/255f,source.ColorRgb[1]/255f,source.ColorRgb[2]/255f,1)};
                SetColor(entry,entry.Color);_entries.Add(entry);mesh=null;go=null;
            }
            finally{Release(go);Release(mesh);}
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private void SetColor(Entry entry,Color color){_properties.Clear();_properties.SetColor("_BaseColor",color);entry.Renderer.SetPropertyBlock(_properties);}
        public void Clear()
        {
            _snapshot=null;
            foreach(var entry in _entries){if(entry.Object!=null)entry.Object.SetActive(false);Release(entry.Object);Release(entry.Mesh);}
            _entries.Clear();Release(_material);_material=null;_properties=null;
        }
        public void Dispose(){if(_disposed)return;Clear();_disposed=true;}
        private void OnDisable(){if(!_disposed)Clear();}
        private void OnDestroy()=>Dispose();
        private static void Release(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    }
}
