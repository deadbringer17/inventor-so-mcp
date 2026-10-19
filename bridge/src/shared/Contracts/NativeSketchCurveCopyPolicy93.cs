using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Bounded92 free circle/arc profile only. Not full Copy, capability or desktop parity.</summary>
public static class NativeSketchCurveCopyPolicy93
{
    public const int DefaultLineType=37648,ScreenSpace=49921;
    public sealed class Relation
    {
        public Relation(string kind,bool deletable,bool intrinsicFlag,string one,string two)
        { Kind=kind;Deletable=deletable;IntrinsicFlag=intrinsicFlag;One=one;Two=two; }
        public string Kind {get;}
        public bool Deletable {get;}
        public bool IntrinsicFlag {get;}
        public string One {get;}
        public string Two {get;}
    }
    public static void ValidateFormat(bool reference,bool construction,bool structuralOwner,bool sharedPoint,
        bool layerNull,bool overrideNull,int attributeCount,int blockCount,int lineType,double weight,double scale,int definitionSpace)
    {
        // Zero line weight is the actually observed default, not an invalid weight.
        if(reference||construction||structuralOwner||sharedPoint||!layerNull||!overrideNull||attributeCount!=0||blockCount!=0
            ||lineType!=DefaultLineType||!Finite(weight)||weight!=0||!Finite(scale)||scale!=1||definitionSpace!=ScreenSpace)
            throw new ArgumentException("Curve copy currently requires the verified independent default native circle/arc format.");
    }
    public static void ValidateGeometry(string kind,double centerX,double centerY,double radius,double startAngle,double sweep,
        IEnumerable<(double X,double Y)> points,NativeSketchCopyPolicy.Request request)
    {
        if(points==null||request==null||(kind!="circle"&&kind!="arc")||!Finite(radius)||radius<=0||radius/10==0||!Finite(startAngle)||!Finite(sweep)
            ||kind=="arc"&&(sweep<=0||sweep>=2*Math.PI)) throw new ArgumentException("Native circle/arc geometry is degenerate or nonfinite.");
        NativeSketchCopyPolicy.Translate(centerX,centerY,request);
        foreach(var point in points) NativeSketchCopyPolicy.Translate(point.X,point.Y,request);
        // Check circle extremal coordinates too, before any creator.
        NativeSketchCopyPolicy.Translate(centerX+radius,centerY+radius,request);
        NativeSketchCopyPolicy.Translate(centerX-radius,centerY-radius,request);
    }
    public static void ValidateIntrinsicGraph(string kind,string curve,string center,string? start,string? end,IEnumerable<Relation> relations)
    {
        if(relations==null||(kind!="circle"&&kind!="arc")||string.IsNullOrEmpty(curve)||string.IsNullOrEmpty(center)||curve==center
            ||kind=="arc"&&(string.IsNullOrEmpty(start)||string.IsNullOrEmpty(end)||new[]{curve,center,start,end}.Distinct(StringComparer.Ordinal).Count()!=4))
            throw new ArgumentException("Native curve and genuine structural point identities are incomplete or aliased.");
        var roles=new HashSet<string>(StringComparer.Ordinal);
        foreach(var relation in relations)
        {
            if(relation==null||relation.Deletable||!relation.IntrinsicFlag) throw new ArgumentException("External/deletable curve relation cannot be copied in the free profile.");
            string point=relation.One==curve?relation.Two:relation.Two==curve?relation.One:"";
            string role=relation.Kind=="concentric"&&point==center?"center":kind=="arc"&&relation.Kind=="coincident"&&point==start?"start"
                :kind=="arc"&&relation.Kind=="coincident"&&point==end?"end":"";
            if(role.Length==0||!roles.Add(role)) throw new ArgumentException("Native relation does not bind the exact curve to its genuine center/endpoint role.");
        }
        if(!roles.SetEquals(kind=="circle"?new[]{"center"}:new[]{"center","start","end"}))
            throw new ArgumentException("Native curve intrinsic graph is incomplete.");
    }
    public static bool RadiusMatches(double actual,double expected)=>Finite(actual)&&Finite(expected)&&actual>0&&expected>0&&Math.Abs(actual-expected)<=1e-8;
    public static bool AngleMatches(double actual,double expected)=>Finite(actual)&&Finite(expected)&&Math.Abs(actual-expected)<=1e-10;
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
}
