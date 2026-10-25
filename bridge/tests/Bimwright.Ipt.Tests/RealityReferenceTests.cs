using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class RealityReferenceTests
{
    private static JObject Payload()=>new()
    {
        ["reference_id"]="RC_abcdef012345",["unit"]="mm",["geometry"]="estimated",
        ["source"]=new JObject {["scan_id"]="11111111111111111111111111111111",["solve_revision"]=1,["review_version"]=0,["document_id"]="cad",["cad_revision"]="rev1",["points_sha256"]=new string('a',64),
            ["scan_metric_to_inventor_mm"]=new JArray(1,0,0,100,0,1,0,200,0,0,-1,300,0,0,0,1)},
        ["points"]=new JArray(new JObject {["id"]="p0",["position_mm"]=new JArray(10,20,30)}),["primitives"]=new JArray(),
        ["known_constraints"]=new JArray(new JObject {["Id"]="known",["ValueMm"]=50,["UncertaintyMm"]=.1,["Source"]="manual"})
    };
    [Fact] public void ReferenceProvenanceAndMillimetreHandednessAreExplicit()
    {
        var input=new RealityReferenceContract(Payload());Assert.Equal(new[]{110d,220,270},input.Transform(new[]{10d,20,30}));
        Assert.Equal(new[]{0d,0,-1},input.Transform(new[]{0d,0,1},true));Assert.Single(input.Constraints);
    }
    [Theory][InlineData("matrix")][InlineData("identity")][InlineData("points")][InlineData("uncertainty")]
    public void InvalidReferenceNeverReachesNativeMutation(string field)
    {
        var payload=Payload();
        if(field=="matrix") payload["source"]!["scan_metric_to_inventor_mm"]![0]=2;
        if(field=="identity") ((JObject)payload["source"]!).Remove("review_version");
        if(field=="points") payload["points"]![0]!["position_mm"]=new JArray(0,double.NaN,0);
        if(field=="uncertainty") payload["known_constraints"]![0]!["UncertaintyMm"]=0;
        Assert.Throws<ArgumentException>(()=>new RealityReferenceContract(payload));
    }
    private sealed class NativeStandIn:ICadBatchBackend
    {
        public string DocumentId {get;set;}="destination";
        public string Revision {get;set;}="rev";
        public int Points,Attributes,BeforePoints,BeforeAttributes,Commits,Rollbacks;
        public bool Fail;
        public void Begin(string name) {BeforePoints=Points;BeforeAttributes=Attributes;}
        public JObject Execute(string command,JObject arguments)
        {Assert.Equal("create_reality_reference",command);var data=new RealityReferenceContract((JObject)arguments["reference"]!);Attributes++;Points+=data.Points.Count;if(Fail) throw new InvalidOperationException("Synthetic native failure after partial creation.");return new JObject {["reference_name"]=data.Name};}
        public void Validate() { }
        public void Commit() {Commits++;Revision="after";}
        public void Rollback() {Rollbacks++;Points=BeforePoints;Attributes=BeforeAttributes;}
        public void RestoreRevision(string revision)=>Revision=revision;
    }
    private static JArray Operation()=>new(new JObject {["command"]="create_reality_reference",["arguments"]=new JObject {["reference"]=Payload()}});
    private static CadBatchOptions Options()=>new() {AllowExperimental=true,DocumentKind=CadDocumentKinds.Part};
    [Fact] public void PreviewRestoresPointsAttributesAndRevisionBeforeAnExactCommit()
    {
        var native=new NativeStandIn();var result=AtomicCadBatch.Run(native,"destination","rev",Operation(),true,null,Options());
        Assert.Equal("preview_rolled_back",result.Value<string>("status"));Assert.Equal(0,native.Points);Assert.Equal(0,native.Attributes);Assert.Equal("rev",native.Revision);
        AtomicCadBatch.Run(native,"destination","rev",Operation(),false,null,Options());Assert.Equal(1,native.Points);Assert.Equal(1,native.Attributes);Assert.Equal(1,native.Commits);
    }
    [Fact] public void PartialNativeFailureRollsBackAndForeignOrStaleDestinationIsRefused()
    {
        var native=new NativeStandIn {Fail=true};Assert.Throws<CadBatchException>(()=>AtomicCadBatch.Run(native,"destination","rev",Operation(),false,null,Options()));
        Assert.Equal(0,native.Points);Assert.Equal(0,native.Attributes);Assert.Equal(1,native.Rollbacks);
        native.Fail=false;Assert.Throws<CadBatchException>(()=>AtomicCadBatch.Run(native,"other","rev",Operation(),false,null,Options()));
        Assert.Throws<CadBatchException>(()=>AtomicCadBatch.Run(native,"destination","stale",Operation(),false,null,Options()));Assert.Equal(0,native.Commits);
    }
}
