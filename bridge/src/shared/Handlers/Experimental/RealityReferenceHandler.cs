#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Core;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

public sealed class CreateRealityReferenceHandler:ExperimentalHandler
{
    public override string Name => "create_reality_reference";
    public override bool IsReadOnly=>false;
    protected override JToken Run(InventorCommandContext ctx,Application app,JObject p)
    {
        if(ctx.OwnedBatchTransaction==null||!ReferenceEquals(app.TransactionManager.CurrentTransaction,ctx.OwnedBatchTransaction)) throw new CodedFailureException(InventorErrorCodes.ATOMIC_REQUIRED,"Reality references require an owned atomic preview/commit.");
        if(app.ActiveDocument is not PartDocument part) throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE,"A reference requires a part document.");
        var input=new RealityReferenceContract(p["reference"] as JObject??throw new ArgumentException("Reference missing."));
        var def=part.ComponentDefinition;var tg=app.TransientGeometry;
        if(!WorkspaceDocumentPolicy.IsInside(WorkspaceDocumentPolicy.Root(),part.FullFileName)||def.SurfaceBodies.Count!=0||def.Features.Count!=0||def.Sketches.Count!=0||def.Sketches3D.Count!=0)
            throw new ArgumentException("Create the reference in an empty managed workspace part; existing models are refused.");
        def.BOMStructure=BOMStructureEnum.kReferenceBOMStructure;
        if(part.AttributeSets.NameIsUsed[input.Name]) throw new ArgumentException("This reference already exists; no duplicate import is allowed.");
        // Document attribute sets cannot be copied with an owning entity. Persist without CopyWithOwner.
        var metadata=part.AttributeSets.Add(input.Name,false);
        metadata.Add("source_json",ValueTypeEnum.kByteArrayType,Encoding.UTF8.GetBytes(input.Payload.ToString(Formatting.None)));
        metadata.Add("status",ValueTypeEnum.kStringType,"estimated; uncertainty not estimated; not B-Rep");
        var cloud=def.Sketches3D.Add();cloud.Name=input.Name+"_PointCloud";
        Point Position(double[] scan) {var q=input.Transform(scan);return tg.CreatePoint(q[0]/10,q[1]/10,q[2]/10);}
        cloud.DeferUpdates=true;
        try {foreach(var point in input.Points)
            {if(ctx.IsDeadlineExceeded?.Invoke()==true) throw new TimeoutException("Expired while creating point cloud.");cloud.SketchPoints3D.Add(Position(RealityReferenceContract.Vec(point["position_mm"])),false);}}
        finally {cloud.DeferUpdates=false;}
        int count=0;
        foreach(var primitive in input.Primitives)
        {
            string name=input.Name+"_P"+(++count);string kind=primitive.Value<string>("kind")!;var origin=RealityReferenceContract.Vec(primitive["origin_mm"]);var direction=RealityReferenceContract.Vec(primitive["direction"]);
            var center=def.WorkPoints.AddFixed(Position(origin),false);center.Name=name+"_Center";
            var attrs=center.AttributeSets.Add("RealityCapture",true);attrs.Add("primitive_json",ValueTypeEnum.kStringType,primitive.ToString(Formatting.None));
            if(kind=="axis"||kind=="cylinder"||kind=="circle")
            {var end=def.WorkPoints.AddFixed(Position(new[]{origin[0]+direction[0]*100,origin[1]+direction[1]*100,origin[2]+direction[2]*100}),true);var axis=def.WorkAxes.AddByTwoPoints(center,end,false);axis.Name=name+"_Axis";}
            if(kind=="plane"||kind=="circle")
            {
                var n=input.Transform(direction,true);var helper=Math.Abs(n[0])<.8?new[]{1d,0,0}:new[]{0d,1,0};
                var u=new[]{n[1]*helper[2]-n[2]*helper[1],n[2]*helper[0]-n[0]*helper[2],n[0]*helper[1]-n[1]*helper[0]};double norm=Math.Sqrt(u.Sum(x=>x*x));u=u.Select(x=>x/norm).ToArray();
                var v=new[]{n[1]*u[2]-n[2]*u[1],n[2]*u[0]-n[0]*u[2],n[0]*u[1]-n[1]*u[0]};
                var plane=def.WorkPlanes.AddFixed(Position(origin),tg.CreateUnitVector(u[0],u[1],u[2]),tg.CreateUnitVector(v[0],v[1],v[2]),false);plane.Name=name+"_Plane";
            }
            if(kind=="circle"||kind=="cylinder") AddParameter(name+"_EstimatedDiameter",primitive.Value<double>("radius_mm")*2,"estimated",primitive);
            if(kind=="cylinder") AddParameter(name+"_ObservedLength",primitive.Value<double>("length_mm"),"estimated observed extent",primitive);
        }
        int dimension=0;foreach(var known in input.Constraints) AddParameter(input.Name+"_Known"+(++dimension),known.Value<double>("ValueMm"),"known external dimension; not reconstructed accuracy",known);
        void AddParameter(string name,double mm,string status,JToken source)
        {
            if(mm<=0) throw new ArgumentException("Reference dimensions must be positive.");
            var parameter=def.Parameters.UserParameters.AddByValue(name,mm/10,"mm");parameter.Comment=status;
            var attributes=parameter.AttributeSets.Add("RealityCapture",true);attributes.Add("source_json",ValueTypeEnum.kStringType,source.ToString(Formatting.None));
        }
        return new JObject {["reference_name"]=input.Name,["point_count"]=input.Points.Count,["primitive_count"]=count,["known_parameters"]=dimension,["geometry"]="estimated",["brep_created"]=false,["document_id"]=EntityReferences.DocumentId(part)};
    }
}
#endif
