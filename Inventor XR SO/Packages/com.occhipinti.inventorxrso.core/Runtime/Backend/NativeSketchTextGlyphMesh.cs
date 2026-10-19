using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>Native glyph vertices already expressed in model millimetres, with rotation applied.</summary>
    public sealed class NativeSketchTextGlyphMesh
    {
        public IReadOnlyList<CadPoint> VerticesMm { get; }
        public IReadOnlyList<int> Indices { get; }
        public IReadOnlyList<int> ColorRgb { get; }
        private NativeSketchTextGlyphMesh(CadPoint[] vertices,int[] indices,int[] color)
        {VerticesMm=Array.AsReadOnly(vertices);Indices=Array.AsReadOnly(indices);ColorRgb=Array.AsReadOnly(color);}
        public static NativeSketchTextGlyphMesh Parse(JObject mesh)
        {
            SketchTextGlyphMeshContract.Validate(mesh);
            var flat=(JArray)mesh["vertices_mm"];var vertices=new CadPoint[flat.Count/3];
            for(int i=0;i<vertices.Length;i++) vertices[i]=new CadPoint((double)flat[i*3],(double)flat[i*3+1],(double)flat[i*3+2]);
            return new NativeSketchTextGlyphMesh(vertices,((JArray)mesh["indices"]).Values<int>().ToArray(),((JArray)mesh["color_rgb"]).Values<int>().ToArray());
        }
    }
}
