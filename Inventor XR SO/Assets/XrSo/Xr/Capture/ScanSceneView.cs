using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Capture;
using InventorXrSo.Core.Glb;
using UnityEngine;
using UnityEngine.Rendering;

namespace InventorXrSo.Xr.Capture
{
    /// <summary>Independent scan viewer; contains no CAD bodies, colliders or CAD selection state.</summary>
    public sealed class ScanSceneView : MonoBehaviour
    {
        private ScanReview _review;
        private Vector3 _center;
        private float _scale;
        private Material _material;
        private GlbModel _surface;
        private readonly List<GameObject> _objects=new List<GameObject>();
        private readonly List<Mesh> _meshes=new List<Mesh>();
        private readonly List<string> _selected=new List<string>();
        public IReadOnlyList<string> Selected => _selected;
        public event Action SelectionChanged;
        public bool SurfaceVisible {get;set;}=true;
        public Vector3 DisplayPoint(double[] mm)=>(Mm(mm)-_center)*_scale;
        public Matrix4x4 ScanMetresToWorld=>transform.localToWorldMatrix*Matrix4x4.TRS(-_center*_scale,Quaternion.identity,Vector3.one*_scale);
        private static Vector3 Mm(double[] mm)=>new Vector3((float)(mm[0]/1000),(float)(mm[1]/1000),(float)(mm[2]/1000));
        public void Load(ScanReview review,Vector3 position,Quaternion rotation)
        {
            _review=review;_selected.Clear();SurfaceVisible=true;
            _surface=null;
            try {_surface=GlbModel.Parse(review.Glb);}catch(NotSupportedException) { }
            if(_surface!=null&&(_surface.Primitives.Sum(p=>p.Positions.Length)>6000||_surface.Primitives.Sum(p=>p.Indices.Length)>120000||
                _surface.Primitives.Any(p=>p.Positions.Any(n=>float.IsNaN(n)||float.IsInfinity(n))))) throw new FormatException("Scan mesh exceeds viewer limits.");
            transform.SetPositionAndRotation(position,rotation);
            var bounds=new Bounds(Mm(review.Points[0].Position),Vector3.zero);
            foreach(var p in review.Points) bounds.Encapsulate(Mm(p.Position));
            _center=bounds.center;_scale=.55f/Mathf.Max(.01f,Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z)));
            Redraw();
        }
        public void ClearSelection() {_selected.Clear();Redraw();SelectionChanged?.Invoke();}
        public void SelectAll() {_selected.Clear();_selected.AddRange(_review.Points.Select(p=>p.Id));Redraw();SelectionChanged?.Invoke();}
        public void SelectWeak() {_selected.Clear();_selected.AddRange(_review.Points.Where(p=>p.Observations<=2).Select(p=>p.Id));Redraw();SelectionChanged?.Invoke();}
        public void SelectIds(IEnumerable<string> ids) {_selected.Clear();var known=new HashSet<string>(_review.Points.Select(p=>p.Id));_selected.AddRange(ids.Where(known.Contains).Distinct());Redraw();SelectionChanged?.Invoke();}
        public void SelectRegion(double radiusMm)
        {
            if(_selected.Count==0) return;
            var point=_review.Points.First(p=>p.Id==_selected[_selected.Count-1]);_selected.Clear();
            _selected.AddRange(_review.Points.Where(p=>ScanGeometry.Distance(p.Position,point.Position)<=radiusMm).Select(p=>p.Id));
            Redraw();SelectionChanged?.Invoke();
        }
        public bool Pick(Ray ray)
        {
            if(_review==null||!gameObject.activeInHierarchy) return false;
            string id=null;float best=.012f;
            foreach(var p in _review.Points)
            {
                var v=transform.TransformPoint(DisplayPoint(p.Position))-ray.origin;float along=Vector3.Dot(v,ray.direction);
                if(along<=0||along>5) continue;
                float distance=(v-ray.direction*along).magnitude;
                if(distance<best) {id=p.Id;best=distance;}
            }
            if(id==null) return false;
            if(!_selected.Remove(id)) _selected.Add(id);
            Redraw();SelectionChanged?.Invoke();return true;
        }
        public void Redraw()
        {
            ReleaseGeometry();if(_review==null) return;
            if(_material==null) _material=new Material(Shader.Find("Sprites/Default"));
            if(SurfaceVisible)
            {
                try
                {
                    foreach(var p in _surface?.Primitives??Array.Empty<GlbPrimitive>())
                    {
                        var vertices=new Vector3[p.Positions.Length/3];var colors=new Color[vertices.Length];
                        for(int i=0;i<vertices.Length;i++) {vertices[i]=(new Vector3(p.Positions[3*i],p.Positions[3*i+1],-p.Positions[3*i+2])-_center)*_scale;colors[i]=new Color(.3f,.5f,.55f,.5f);}
                        var indices=p.Indices.Select(i=>(int)i).ToArray();for(int i=0;i<indices.Length;i+=3) {int t=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=t;}
                        AddMesh("Estimated sparse surface",vertices,indices,colors,MeshTopology.Triangles);
                    }
                }
                catch(NotSupportedException) { } // Worker emits mode POINTS when triangulation has no supported surface.
            }
            var v=new List<Vector3>();var c=new List<Color>();var ids=new List<int>();
            void Segment(Vector3 a,Vector3 b,Color color) {ids.Add(v.Count);v.Add(a);ids.Add(v.Count);v.Add(b);c.Add(color);c.Add(color);}
            foreach(var p in _review.Points)
            {
                var x=DisplayPoint(p.Position);var color=_selected.Contains(p.Id)?Color.yellow:p.Observations<=2?new Color(1,.5f,.1f):Color.cyan;
                float r=_selected.Contains(p.Id)?.004f:.0015f;
                Segment(x-Vector3.right*r,x+Vector3.right*r,color);Segment(x-Vector3.up*r,x+Vector3.up*r,color);Segment(x-Vector3.forward*r,x+Vector3.forward*r,color);
            }
            foreach(var primitive in _review.Annotations["primitives"]?.ToObject<ScanPrimitive[]>()??Array.Empty<ScanPrimitive>())
            {
                var o=DisplayPoint(primitive.Origin);var n=new Vector3((float)primitive.Direction[0],(float)primitive.Direction[1],(float)primitive.Direction[2]).normalized;
                var u=Vector3.Cross(n,Mathf.Abs(n.x)<.8f?Vector3.right:Vector3.up).normalized;var w=Vector3.Cross(n,u);
                var support=_review.Points.Where(p=>primitive.SupportIds.Contains(p.Id)).ToArray();
                float span=support.Length==0?.03f:support.Max(p=>(DisplayPoint(p.Position)-o).magnitude);
                var color=Color.magenta;
                if(primitive.Kind=="axis") Segment(o-n*span,o+n*span,color);
                if(primitive.Kind=="point") {Segment(o-u*.006f,o+u*.006f,color);Segment(o-w*.006f,o+w*.006f,color);}
                if(primitive.Kind=="plane")
                {var corners=new[]{o+(u+w)*span,o+(u-w)*span,o+(-u-w)*span,o+(-u+w)*span};for(int k=0;k<4;k++) Segment(corners[k],corners[(k+1)%4],color);Segment(o,o+n*.04f,color);}
                if(primitive.Kind=="circle"||primitive.Kind=="cylinder")
                {
                    float radius=(float)(primitive.Radius/1000)*_scale,half=primitive.Kind=="cylinder"?(float)(primitive.Length/2000)*_scale:0;
                    for(int k=0;k<64;k++)
                    {
                        float a=k*Mathf.PI/32,b=(k+1)*Mathf.PI/32;
                        var x=(u*Mathf.Cos(a)+w*Mathf.Sin(a))*radius;var y=(u*Mathf.Cos(b)+w*Mathf.Sin(b))*radius;
                        Segment(o+x-n*half,o+y-n*half,color);
                        if(half>0) {Segment(o+x+n*half,o+y+n*half,color);if(k%16==0) Segment(o+x-n*half,o+x+n*half,color);}
                    }
                }
            }
            AddMesh("Points and estimated primitives",v.ToArray(),ids.ToArray(),c.ToArray(),MeshTopology.Lines);
        }
        private void AddMesh(string label,Vector3[] vertices,int[] indices,Color[] colors,MeshTopology topology)
        {
            var mesh=new Mesh {name=label,indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.colors=colors;mesh.SetIndices(indices,topology,0);mesh.RecalculateBounds();
            var go=new GameObject(label);go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=_material;
            _objects.Add(go);_meshes.Add(mesh);
        }
        public void Clear() {_review=null;_surface=null;_selected.Clear();ReleaseGeometry();gameObject.SetActive(false);}
        private void ReleaseGeometry()
        {foreach(var go in _objects) {go.SetActive(false);Release(go);}foreach(var mesh in _meshes) Release(mesh);_objects.Clear();_meshes.Clear();}
        private static void Release(UnityEngine.Object item) {if(Application.isPlaying) Destroy(item);else DestroyImmediate(item);}
        private void OnDestroy() {ReleaseGeometry();if(_material!=null) Release(_material);}
    }
}
