using System.Globalization;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static readonly string[] CurveRoles95={"circle","arc_quarter","arc_wrap","arc_major","arc_clockwise"};
    private static readonly (string Action,string Context,int Pass)[] Steps95={
        ("prepare","all",0),("edit","Part",1),("inspect","Part",1),("edit","Part",2),("inspect","Part",2),
        ("reopen","Part",0),("inspect_reopen","Part",0),("edit","Sheet",1),("inspect","Sheet",1),
        ("edit","Sheet",2),("inspect","Sheet",2),("reopen","Sheet",0),("inspect_reopen","Sheet",0),("complete","all",0)};
    private sealed class Curve95
    {
        internal JObject Raw=null!;internal string Role="",Id="",Key="";
        internal Dictionary<string,string> PointIds=new(StringComparer.Ordinal),PointKeys=new(StringComparer.Ordinal);
        internal Dictionary<string,JArray> Points=new(StringComparer.Ordinal);
        internal JArray Constraints=null!,Anchors=null!,Center=null!;
    }
    private sealed class Document95
    {
        internal JObject Raw=null!;internal string Context="",DocumentId="",InternalName="",Path="",DisplayName="",SketchId="",ParameterKey="";
        internal Dictionary<string,string> SourceIds=new(StringComparer.Ordinal);
        internal Dictionary<string,Curve95> Curves=new(StringComparer.Ordinal);
    }
    private sealed class Protocol95
    {
        internal string Directory="";internal int Ordinal;internal DateTimeOffset Expiry;
        internal Dictionary<string,Document95> Documents=new(StringComparer.Ordinal);internal JArray Trace=new();
    }
    private static JArray XY95(JToken? value)
    {
        Require(value is JArray a&&a.Count==2&&a.All(p=>p.Type is JTokenType.Float or JTokenType.Integer)&&a.All(p=>double.IsFinite((double)p)),"Native metadata XY pair is invalid.");return (JArray)value!;
    }
    private static void Key95(string value)=>Require(Convert.FromBase64String(value).Length is >0 and <=65536,"Native metadata key exceeds bound.");
    private static bool SamePath95(string a,string b)=>Path.IsPathFullyQualified(a)&&Path.IsPathFullyQualified(b)&&string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
    private static async Task Prerequisite95(string directory,S1AcceptanceBootstrapMetadata binding,JObject report,CancellationToken ct)
    {
        Phase="prerequisite94_native_oracle";string ack=Path.Combine(directory,"prerequisite94-verified.json");Require(!File.Exists(ack),"Prerequisite ACK already exists; no replay permitted.");
        await Publish94(Path.Combine(directory,"prerequisite94-ready.json"),new JObject{["run_id"]=binding.RunId},ct);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(120));
        while(true)
        {
            timeout.Token.ThrowIfCancellationRequested();Require(binding.ExpiresUtc>DateTimeOffset.UtcNow,"Prerequisite bootstrap expired.");
            if(File.Exists(ack))
            {
                var value=await ReadPublic94(ack,4096,timeout.Token);Closed94(value,"run_id");Require(Text94(value,"run_id",32)==binding.RunId,"Prerequisite94 ACK belongs to another run.");
                report["prerequisite94"]=new JObject{["baseline_file"]="copy-client94.json",["native_verified_ack"]=value.DeepClone()};return;
            }
            await Task.Delay(100,timeout.Token); // Local metadata only, no native query/retry.
        }
    }
    private static Document95 DocumentMetadata95(JObject raw,Catalog94 catalog,string context)
    {
        Closed94(raw,"context","document_id","internal_name","native_path","display_name","subtype","sketch_id","parameter_key","native_database_revision_id","source_ids","source_curves");
        var expected=catalog.Documents[context];Require(Text94(raw,"context",16)==context&&Text94(raw,"subtype",16)==context,"ACK95 context/subtype differs.");
        var result=new Document95{Raw=(JObject)raw.DeepClone(),Context=context,DocumentId=Text94(raw,"document_id",256),InternalName=Text94(raw,"internal_name",256),Path=Text94(raw,"native_path",32768),DisplayName=Text94(raw,"display_name",1024),SketchId=Text94(raw,"sketch_id"),ParameterKey=Text94(raw,"parameter_key",131072)};
        Require(result.DocumentId==expected.DocumentId&&result.SketchId==expected.SketchId&&result.DocumentId.StartsWith("doc_",StringComparison.Ordinal)
            &&Guid.TryParse(result.DocumentId[4..],out var documentGuid)&&Guid.TryParse(result.InternalName,out var nativeGuid)&&documentGuid==nativeGuid
            &&Path.IsPathFullyQualified(result.Path)&&Path.GetExtension(result.Path).Equals(".ipt",StringComparison.OrdinalIgnoreCase),"ACK95 native document/path identity differs.");
        _=Text94(raw,"native_database_revision_id",256);Key95(result.ParameterKey);Opaque94(result.SketchId,result.DocumentId,"sketch");
        Require(raw["source_ids"] is JObject&&raw["source_curves"] is JObject,"ACK95 source maps absent.");var sources=(JObject)raw["source_ids"]!;Closed94(sources,Roles94);var curves=(JObject)raw["source_curves"]!;Closed94(curves,CurveRoles95);
        foreach(string role in Roles94){string id=Text94(sources,role);Require(id==expected.Sources[role],"ACK95 changed an attested creator source ID.");result.SourceIds.Add(role,id);}
        var allPointKeys=new HashSet<string>(StringComparer.Ordinal);
        foreach(string role in CurveRoles95)
        {
            Require(curves[role] is JObject,"ACK95 curve metadata missing.");var value=(JObject)curves[role]!;
            Closed94(value,"entity_id","entity_key","point_ids","point_keys","point_mm","constraint_keys","constraint_anchors","initial_center_mm","radius_mm","angles","domain");
            var curve=new Curve95{Raw=(JObject)value.DeepClone(),Role=role,Id=Text94(value,"entity_id"),Key=Text94(value,"entity_key",131072),Center=XY95(value["initial_center_mm"])};
            Require(curve.Id==result.SourceIds[role]&&Opaque94(curve.Id,result.DocumentId,"sketch_entity")==curve.Key,"ACK95 curve native key/ID differs.");
            string[] pointRoles=role=="circle"?new[]{"center"}:new[]{"center","start","end"};
            Require(value["point_ids"] is JObject&&value["point_keys"] is JObject&&value["point_mm"] is JObject,"ACK95 structural point maps missing.");
            var pointIds=(JObject)value["point_ids"]!;var pointKeys=(JObject)value["point_keys"]!;var points=(JObject)value["point_mm"]!;Closed94(pointIds,pointRoles);Closed94(pointKeys,pointRoles);Closed94(points,pointRoles);
            foreach(string pointRole in pointRoles)
            {
                string id=Text94(pointIds,pointRole),key=Text94(pointKeys,pointRole,131072);Require(Opaque94(id,result.DocumentId,"sketch_entity")==key&&allPointKeys.Add(key)&&!expected.Sources.Values.Contains(id,StringComparer.Ordinal),"ACK95 structural identity is unresolved/shared with another source.");
                curve.PointIds.Add(pointRole,id);curve.PointKeys.Add(pointRole,key);curve.Points.Add(pointRole,(JArray)XY95(points[pointRole]).DeepClone());
            }
            Require(Exact94(curve.Points["center"],curve.Center)&&value["radius_mm"]?.Type is (JTokenType.Integer or JTokenType.Float)&&double.IsFinite((double)value["radius_mm"]!)&&Math.Abs((double)value["radius_mm"]!-9)<=1e-8,"ACK95 initial center/radius differs.");
            var domain=XY95(value["domain"]);Require((double)domain[1]>(double)domain[0],"ACK95 native domain invalid.");Require(value["angles"] is JObject,"ACK95 angles object absent.");var angles=(JObject)value["angles"]!;
            Closed94(angles,role=="circle"?Array.Empty<string>():new[]{"start_angle_rad","sweep_angle_rad","geometry_start_angle_rad","geometry_sweep_angle_rad"});foreach(var angle in angles.Properties())Require(angle.Value.Type is (JTokenType.Integer or JTokenType.Float)&&double.IsFinite((double)angle.Value),"ACK95 native angle invalid.");
            Require(value["constraint_keys"] is JArray keys&&keys.Count is >0 and <=16&&keys.All(k=>k.Type==JTokenType.String)&&value["constraint_anchors"] is JArray anchors&&anchors.Count==keys.Count,"ACK95 intrinsic constraint metadata invalid.");
            curve.Constraints=(JArray)value["constraint_keys"]!;curve.Anchors=(JArray)value["constraint_anchors"]!;var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(JToken token in curve.Anchors)
            {
                Require(token is JObject,"ACK95 anchor metadata not an object.");var anchor=(JObject)token;Closed94(anchor,"constraint_key","native_type","members","anchor_mm");string key=Text94(anchor,"constraint_key",131072);Key95(key);
                Require(seen.Add(key)&&curve.Constraints.Values<string>().Contains(key,StringComparer.Ordinal)&&Text94(anchor,"native_type",128) is "kCoincidentConstraintObject" or "kConcentricConstraintObject","ACK95 source constraint identity/type differs.");XY95(anchor["anchor_mm"]);
                Require(anchor["members"] is JArray members&&members.Count==2&&members.All(m=>m is JObject),"ACK95 source members must be two ordered typed native members.");int ordinal=0;
                foreach(JObject member in (JArray)anchor["members"]!){Closed94(member,"role","key","type");Require(Text94(member,"role",32)==(ordinal++==0?"EntityOne":"EntityTwo"),"ACK95 source constraint member order differs.");string memberKey=Text94(member,"key",131072);string type=Text94(member,"type",128);Require(memberKey==curve.Key?type==(role=="circle"?"kSketchCircleObject":"kSketchArcObject"):curve.PointKeys.Values.Contains(memberKey,StringComparer.Ordinal)&&type=="kSketchPointObject","ACK95 source constraint crosses into external geometry or has wrong native member type.");}
            }
            Require(seen.Count==curve.Constraints.Count&&curve.Constraints.Values<string>().Distinct(StringComparer.Ordinal).Count()==seen.Count,"ACK95 source constraint keys duplicate/missing.");result.Curves.Add(role,curve);
        }
        return result;
    }
    private static async Task Action95(Protocol95 protocol,S1AcceptanceBootstrapMetadata binding,Catalog94 catalog,string action,string context,int pass,CancellationToken ct)
    {
        int ordinal=protocol.Ordinal+1;Require(ordinal<=Steps95.Length&&Steps95[ordinal-1]==(action,context,pass)&&binding.ExpiresUtc>DateTimeOffset.UtcNow,"Lifecycle95 FSM order/lifetime differs.");
        string nonce=Guid.NewGuid().ToString("N"),ackPath=Path.Combine(protocol.Directory,"copy-core-ack95-"+nonce+".json");Require(!File.Exists(ackPath),"ACK95 nonce already exists.");
        var request=new JObject{["schema"]=1,["run_id"]=binding.RunId,["nonce"]=nonce,["ordinal"]=ordinal,["action"]=action,["context"]=context,["pass"]=pass};
        Phase="protocol95_"+ordinal+"_"+action;await Publish94(Path.Combine(protocol.Directory,"copy-core-request95.json"),request,ct,true);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(120));JObject? ack=null;
        while(ack==null){timeout.Token.ThrowIfCancellationRequested();if(File.Exists(ackPath)){try{ack=await ReadPublic94(ackPath,1048576,timeout.Token);}catch(IOException){}catch(UnauthorizedAccessException){}}if(ack==null)await Task.Delay(100,timeout.Token);}
        Closed94(ack,"schema","run_id","target_id","owned_pid","owned_start_utc_ticks","nonce","ordinal","action","context","pass","status","expires_utc","documents");
        foreach(string field in new[]{"schema","run_id","nonce","ordinal","action","context","pass"})Require(Exact94(request[field],ack[field]),"ACK95 request binding differs.");
        Require(Text94(ack,"target_id",128)==binding.TargetId&&Text94(ack,"status",16)=="ACK"&&ack["owned_pid"]?.Type==JTokenType.Integer&&(int)ack["owned_pid"]! ==catalog.Pid&&ack["owned_start_utc_ticks"]?.Type==JTokenType.Integer&&(long)ack["owned_start_utc_ticks"]! ==catalog.Start,"ACK95 process/target binding differs.");
        Require(DateTimeOffset.TryParseExact(Text94(ack,"expires_utc",64),"O",CultureInfo.InvariantCulture,DateTimeStyles.None,out var expires)&&expires.Offset==TimeSpan.Zero&&expires>DateTimeOffset.UtcNow&&expires<=binding.ExpiresUtc,"ACK95 lifetime invalid.");
        Require(ack["documents"] is JArray docs&&docs.Count==2&&docs.All(d=>d is JObject),"ACK95 must attest both owned documents.");var current=new Dictionary<string,Document95>(StringComparer.Ordinal);
        foreach(string kind in new[]{"Part","Sheet"}){var matching=((JArray)ack["documents"]!).Cast<JObject>().Where(d=>(string?)d["context"]==kind).ToArray();Require(matching.Length==1,"ACK95 context missing/duplicated.");current.Add(kind,DocumentMetadata95(matching[0],catalog,kind));}
        Require(!SamePath95(current["Part"].Path,current["Sheet"].Path)&&SamePath95(Path.GetDirectoryName(current["Part"].Path)!,Path.GetDirectoryName(current["Sheet"].Path)!),"ACK95 native document paths collide or have different owned roots.");
        if(protocol.Ordinal==0)protocol.Expiry=expires;
        else
        {
            Require(protocol.Expiry==expires,"ACK95 lifetime changed.");
            foreach(string kind in new[]{"Part","Sheet"}){var previous=protocol.Documents[kind];var next=current[kind];Require(previous.DocumentId==next.DocumentId&&previous.InternalName==next.InternalName&&previous.SketchId==next.SketchId&&previous.ParameterKey==next.ParameterKey&&Exact94(previous.Raw["source_ids"],next.Raw["source_ids"])&&Exact94(previous.Raw["source_curves"],next.Raw["source_curves"]),"ACK95 frozen source/structural identities changed.");bool reopening=action=="reopen"&&kind==context;Require(reopening?!SamePath95(previous.Path,next.Path)&&SamePath95(Path.GetDirectoryName(previous.Path)!,Path.GetDirectoryName(next.Path)!):SamePath95(previous.Path,next.Path),"ACK95 native path transition differs.");}
        }
        protocol.Documents=current;protocol.Ordinal=ordinal;protocol.Trace.Add(new JObject{["request"]=request,["ack"]=ack});
    }
}
