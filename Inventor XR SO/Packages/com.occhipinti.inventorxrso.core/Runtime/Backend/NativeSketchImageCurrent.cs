using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>Current native UV geometry. Pixel retrieval and image mutation remain separate capabilities.</summary>
    public sealed class NativeSketchImageCurrent
    {
        private NativeSketchImageCurrent() { }
        public IReadOnlyList<CadPoint> CornersMm { get; private set; }
        public double WidthMm { get; private set; }
        public double HeightMm { get; private set; }
        public bool Visible { get; private set; }
        public string Provenance => "native_boundary_roles";
        public string SourceKind => "native_bitmap";
        public string AssetState => "deferred";

        internal static NativeSketchImageCurrent Parse(JObject data)
        {
            void Fields(JObject obj, params string[] fields)
            { if(obj == null || obj.Count != fields.Length || obj.Properties().Any(p=>!fields.Contains(p.Name,StringComparer.Ordinal))) throw Invalid(); }
            double Positive(string name)
            {
                var token=data[name];
                if(token == null || token.Type != JTokenType.Float && token.Type != JTokenType.Integer) throw Invalid();
                double value=(double)token;
                if(double.IsNaN(value) || double.IsInfinity(value) || value<=0) throw Invalid();
                return value;
            }
            Fields(data,"schema","frame","width_mm","height_mm","source_kind","asset_state","visible","transparent");
            if(data["schema"]?.Type!=JTokenType.Integer || data["schema"].ToString()!="1"
                || data["source_kind"]?.Type!=JTokenType.String || (string)data["source_kind"]!="native_bitmap"
                || data["asset_state"]?.Type!=JTokenType.String || (string)data["asset_state"]!="deferred"
                || data["visible"]?.Type!=JTokenType.Boolean || data["transparent"]?.Type!=JTokenType.Boolean
                || (bool)data["transparent"]) throw Invalid();
            var frame=data["frame"] as JObject;
            Fields(frame,"schema","provenance","corner_order","corners_mm");
            if(frame["schema"]?.Type!=JTokenType.Integer || frame["schema"].ToString()!="1"
                || frame["provenance"]?.Type!=JTokenType.String || (string)frame["provenance"]!="native_boundary_roles"
                || !(frame["corner_order"] is JArray order) || order.Count!=4 || order.Any(x=>x.Type!=JTokenType.String)
                || !order.Values<string>().SequenceEqual(new[]{"top_left","top_right","bottom_right","bottom_left"},StringComparer.Ordinal)
                || !(frame["corners_mm"] is JArray points) || points.Count!=4) throw Invalid();
            double width=Positive("width_mm"),height=Positive("height_mm");
            CadPoint[] corners=points.Select(p=>CadPoint.Parse(p,2)).ToArray();
            var u=corners[1]-corners[0];var v=corners[3]-corners[0];
            bool Near(double actual,double expected)=>!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual-expected)<=1e-5;
            if(!(u.Length>0) || !(v.Length>0) || !Near(u.Length,width) || !Near(v.Length,height) || !Near((corners[2]-corners[3]-u).Length,0)
                || !Near((corners[2]-corners[1]-v).Length,0) || !Near(u.Dot(v)/width,0) || !Near(u.Dot(v)/height,0)) throw Invalid();
            return new NativeSketchImageCurrent { CornersMm=Array.AsReadOnly(corners),WidthMm=width,HeightMm=height,Visible=(bool)data["visible"] };
        }
        private static FormatException Invalid()=>new FormatException("Descrizione del frame corrente dell'immagine nativa non valida.");
    }
}
