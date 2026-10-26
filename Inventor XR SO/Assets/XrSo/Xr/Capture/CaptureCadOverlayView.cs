using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr.Capture
{
    /// <summary>Read-only copy of a CAD snapshot for comparison/physical overlay; never CAD picking or editing.</summary>
    public sealed class CaptureCadOverlayView:MonoBehaviour
    {
        private readonly List<Mesh> _meshes=new List<Mesh>();
        private readonly List<GameObject> _objects=new List<GameObject>();
        private Material _material;
        public string DocumentId {get;private set;}
        public string Revision {get;private set;}
        public string VisualRevision {get;private set;}
        public void Show(LoadedScene scene,Matrix4x4 unityCadToWorld)
        {
            Clear();
            if(scene==null||scene.Graph==null||scene.Graph.Truncated||scene.Omitted.Count!=0) throw new ArgumentException("A complete CAD snapshot is required.");
            if(scene.Models.Values.Sum(m=>m.Primitives.Sum(p=>(long)p.Positions.Length/3))>1000000||scene.Graph.PlacedParts().Take(2001).Count()>2000) throw new ArgumentException("CAD comparison exceeds the bounded viewer budget.");
            DocumentId=scene.Graph.DocumentId;Revision=scene.Graph.Revision;VisualRevision=scene.Graph.VisualRevision;
            if(_material==null) _material=new Material(Shader.Find("Sprites/Default"));
            transform.SetPositionAndRotation(unityCadToWorld.GetColumn(3),unityCadToWorld.rotation);
            var scale=unityCadToWorld.lossyScale;var parent=transform.parent==null?Vector3.one:transform.parent.lossyScale;
            transform.localScale=new Vector3(scale.x/parent.x,scale.y/parent.y,scale.z/parent.z);
            var definitions=new Dictionary<string,Mesh[]>();
            foreach(var placed in scene.Graph.PlacedParts())
            {
                if(!scene.Models.TryGetValue(placed.Node.DefinitionDocumentId,out var model)) throw new ArgumentException("CAD definition missing.");
                if(!definitions.TryGetValue(placed.Node.DefinitionDocumentId,out var meshes))
                {
                    meshes=new Mesh[model.Primitives.Count];definitions.Add(placed.Node.DefinitionDocumentId,meshes);
                    for(int i=0;i<model.Primitives.Count;i++)
                    {
                        if(!model.Primitives[i].Visible) continue;
                        var mesh=MeshFactory.Build(model.Primitives[i]);meshes[i]=mesh;_meshes.Add(mesh);
                        var colors=new Color[mesh.vertexCount];for(int c=0;c<colors.Length;c++) colors[c]=new Color(.2f,1,.35f,.55f);mesh.colors=colors;
                    }
                }
                var instance=new GameObject("Capture CAD comparison");instance.transform.SetParent(transform,false);_objects.Add(instance);
                MatrixUtil.Apply(instance.transform,Handedness.ConvertMatrix(placed.MatrixGltf));
                for(int i=0;i<model.Primitives.Count;i++)
                {
                    if(meshes[i]==null) continue;
                    var go=new GameObject("Estimated registration · CAD");go.transform.SetParent(instance.transform,false);go.AddComponent<MeshFilter>().sharedMesh=meshes[i];go.AddComponent<MeshRenderer>().sharedMaterial=_material;
                }
            }
            gameObject.SetActive(true);
        }
        public void Clear()
        {
            foreach(var go in _objects) {go.SetActive(false);Release(go);}foreach(var mesh in _meshes) Release(mesh);_objects.Clear();_meshes.Clear();
            DocumentId=Revision=VisualRevision=null;gameObject.SetActive(false);
        }
        private static void Release(UnityEngine.Object value) {if(Application.isPlaying) Destroy(value);else DestroyImmediate(value);}
        private void OnDestroy() {Clear();if(_material!=null) Release(_material);}
    }
}
