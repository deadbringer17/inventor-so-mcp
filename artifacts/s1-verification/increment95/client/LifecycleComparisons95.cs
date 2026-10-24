using System.Xml;
using System.Xml.Linq;
using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private sealed record Annotation95(string ConstraintId,string ConstraintKey,string AnchorPointId,JArray ExpectedBindings,string NativeType);
    private sealed class Baseline95
    {
        internal DocumentState State=null!;internal JObject Initial=null!,Last=null!;
        internal HashSet<string> CopyIds=new(StringComparer.Ordinal);
        internal Dictionary<string,Annotation95> Annotations=new(StringComparer.Ordinal);
    }
    private static Dictionary<string,JObject> Rows95(JObject snapshot,string collection)=>((JArray)snapshot[collection]!).Cast<JObject>().ToDictionary(row=>Text94(row,"id"),StringComparer.Ordinal);
    private static JArray Offset95(JArray point,double dx,double dy)=>new((double)point[0]+dx,(double)point[1]+dy);
    private static void NearPoint95(JToken? actual,JArray expected)
    {
        var a=XY95(actual);for(int i=0;i<2;i++)Require(Math.Abs((double)a[i]-(double)expected[i])<=Tol,"First Core source point differs from the prescribed native edit.");
    }
    private static Dictionary<string,Annotation95> AnnotationRulesFromData95(JObject snapshot,Document95 metadata)
    {
        var entities=Rows95(snapshot,"entities");var constraints=Rows95(snapshot,"constraints");
        var curves=metadata.Curves.Values.ToDictionary(c=>c.Id,StringComparer.Ordinal);
        var nativeCurve=metadata.Curves.Values.ToDictionary(c=>c.Key,StringComparer.Ordinal);
        var points=new Dictionary<string,(Curve95 Curve,string Role)>(StringComparer.Ordinal);
        foreach(var curve in metadata.Curves.Values)
        {
            Require(entities.ContainsKey(curve.Id)&&Opaque94(curve.Id,metadata.DocumentId,"sketch_entity")==curve.Key,"Initial source curve identity missing.");
            foreach(string role in curve.PointIds.Keys)
            {
                string id=curve.PointIds[role];Require(entities.TryGetValue(id,out var point)&&(string?)point["type"]=="point"&&Exact94(point["point_mm"],curve.Points[role]),"Initial structural point differs from frozen native identity/coordinate.");
                Require(points.TryAdd(curve.PointKeys[role],(curve,role)),"Initial source structural points collide.");
            }
            NearPoint95(entities[curve.Id]["center_mm"],curve.Center);
        }
        // Reproduce only the reader's public typed association algorithm, using
        // the actual Core entity order and attested native structural keys.
        // No ownership is inferred from equal/near coordinates.
        var bindings=new Dictionary<string,JArray>(StringComparer.Ordinal);
        void Register(string key,string entity,string type,string part)
        {
            if(!bindings.TryGetValue(key,out var entries)){entries=new JArray();bindings.Add(key,entries);}
            var link=new JObject{["entity_id"]=entity,["type"]=type,["subpart"]=part};if(!entries.Any(e=>Exact94(e,link)))entries.Add(link);
        }
        foreach(JObject entity in (JArray)snapshot["entities"]!)
        {
            string entityId=Text94(entity,"id"),entityType=Text94(entity,"type");
            if(curves.TryGetValue(entityId,out var curve))
            {
                Register(curve.Key,curve.Id,entityType,"Entity");
                foreach(string role in curve.Role=="circle"?new[]{"center"}:new[]{"start","end","center"})Register(curve.PointKeys[role],curve.Id,entityType,char.ToUpperInvariant(role[0])+role[1..]);
            }
            else
            {
                var match=points.Where(p=>p.Value.Curve.PointIds[p.Value.Role]==entityId).ToArray();
                if(match.Length==1){Register(match[0].Key,entityId,"point","Entity");Register(match[0].Key,entityId,"point","Point");}
            }
        }
        var result=new Dictionary<string,Annotation95>(StringComparer.Ordinal);
        foreach(var curve in metadata.Curves.Values)
        foreach(JObject anchor in curve.Anchors)
        {
            string key=Text94(anchor,"constraint_key",131072);var matching=constraints.Values.Where(c=>Opaque94(Text94(c,"id"),metadata.DocumentId,"sketch_constraint")==key).ToArray();
            Require(matching.Length==1,"Attested intrinsic constraint has no unique Core identity.");var constraint=matching[0];
            Require((string?)constraint["type"]=="constraint"&&(string?)constraint["native_type"]==Text94(anchor,"native_type",128)&&constraint["deletable"]?.Type==JTokenType.Boolean&&!(bool)constraint["deletable"]!
                &&(string?)constraint["binding_state"]=="complete","Source annotation rule is not a complete nondeletable intrinsic constraint.");
            var expected=new JArray();foreach(JObject member in (JArray)anchor["members"]!){string memberKey=Text94(member,"key",131072);Require(bindings.ContainsKey(memberKey),"Native source constraint member lacks exact typed Core bindings.");foreach(var link in bindings[memberKey])if(!expected.Any(e=>Exact94(e,link)))expected.Add(link.DeepClone());}
            Require(Exact94(constraint["related_entities"],expected),"Ordered source constraint bindings differ from actual reader/native member map.");
            string firstKey=Text94((JObject)anchor["members"]![0]!,"key",131072),anchorPoint;
            if(nativeCurve.TryGetValue(firstKey,out var owner))anchorPoint=owner.PointIds[owner.Role=="circle"?"center":"start"];
            else{Require(points.ContainsKey(firstKey),"Native first constraint member has no known reader anchor.");var point=points[firstKey];anchorPoint=point.Curve.PointIds[point.Role];}
            Require(Exact94(constraint["annotation_mm"],entities[anchorPoint]["point_mm"])&&Exact94(anchor["anchor_mm"],entities[anchorPoint]["point_mm"]),"Initial annotation does not equal the exact native first-member anchor.");
            string id=Text94(constraint,"id");Require(result.TryAdd(id,new Annotation95(id,key,anchorPoint,expected,Text94(anchor,"native_type",128))),"Source intrinsic constraint is shared across source curves.");
        }
        var sourceIds=metadata.Curves.Values.Select(c=>c.Id).Concat(metadata.Curves.Values.SelectMany(c=>c.PointIds.Values)).ToHashSet(StringComparer.Ordinal);
        foreach(var pair in constraints)
            if(pair.Value["related_entities"] is JArray related&&related.Any(r=>sourceIds.Contains((string?)r["entity_id"]??"")))Require(result.ContainsKey(pair.Key),"Source has an additional external/unsupported Core relation.");
        return result;
    }
    private static void ValidateFresh95(NativeSketchSnapshot snapshot,DocumentState state,Document95 metadata,Baseline95 baseline)
    {
        Require(snapshot.NativeMutationComplete&&!snapshot.Partial&&Same(snapshot.State,state)&&snapshot.SketchId==metadata.SketchId,"First95 snapshot is not complete/current/owned.");
        foreach(var entity in snapshot.Entities.Concat(snapshot.Dimensions).Concat(snapshot.Constraints))Require(entity.Reference!=null&&entity.Reference.Matches(state,metadata.SketchId),"First95 snapshot contains an old typed reference.");
        var entities=Rows95(Represented(snapshot),"entities");Require(baseline.CopyIds.All(entities.ContainsKey)&&metadata.SourceIds.Values.All(entities.ContainsKey)
            &&metadata.Curves.Values.SelectMany(c=>c.PointIds.Values).All(entities.ContainsKey),"First95 snapshot lost a source/copy/structural identity.");
    }
    private static void CheckIndependent95(JObject before,JObject after,Document95 oldMeta,Document95 newMeta,Baseline95 baseline,bool reopened)
    {
        Require(oldMeta.DocumentId==newMeta.DocumentId&&oldMeta.ParameterKey==newMeta.ParameterKey,"Relocation changed parameter native owner/key.");
        var oldEntities=Rows95(before,"entities");var newEntities=Rows95(after,"entities");var mutableCurves=oldMeta.Curves.Values.ToDictionary(c=>c.Id,StringComparer.Ordinal);var mutablePoints=oldMeta.Curves.Values.SelectMany(c=>c.PointIds.Values).ToHashSet(StringComparer.Ordinal);
        foreach(string collection in new[]{"entities","dimensions","constraints"})
        {
            var one=Rows95(before,collection);var two=Rows95(after,collection);Require(one.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(two.Keys),"Lifecycle95 native represented identity inventory changed.");
            foreach(var pair in one)
            {
                string id=pair.Key;var expected=(JObject)pair.Value.DeepClone();var actual=(JObject)two[id].DeepClone();
                if(collection=="entities"&&id==oldMeta.SourceIds["parameter_text"]){Require(Exact94(Parameter95(expected,oldMeta),Parameter95(actual,newMeta)),"Uncopied parameter changed beyond strict owned path relocation.");continue;}
                if(!reopened&&collection=="entities")
                {
                    if(mutableCurves.TryGetValue(id,out var curve))foreach(string field in curve.Role=="circle"?new[]{"center_mm","radius_mm","strokes_mm"}:new[]{"center_mm","radius_mm","start_mm","end_mm","strokes_mm"}){expected.Remove(field);actual.Remove(field);}
                    else if(mutablePoints.Contains(id)){expected.Remove("point_mm");actual.Remove("point_mm");}
                }
                if(!reopened&&collection=="constraints"&&baseline.Annotations.TryGetValue(id,out var rule))
                {
                    Require((string?)expected["native_type"]==rule.NativeType&&(string?)actual["native_type"]==rule.NativeType&&Exact94(expected["related_entities"],rule.ExpectedBindings)&&Exact94(actual["related_entities"],rule.ExpectedBindings)
                        &&Exact94(expected["annotation_mm"],oldEntities[rule.AnchorPointId]["point_mm"])&&Exact94(actual["annotation_mm"],newEntities[rule.AnchorPointId]["point_mm"]),"Source annotation does not follow its exact attested typed native first-member anchor.");expected.Remove("annotation_mm");actual.Remove("annotation_mm");
                }
                Require(Exact94(expected,actual),"Copy/unrelated Data or undeclared source property changed during95.");
            }
        }
        foreach(string field in new[]{"complete","partial","constraint_status"})Require(Exact94(before[field],after[field]),"Lifecycle95 solver/completeness changed.");
        var initial=Rows95(baseline.Initial,"entities");foreach(string copy in baseline.CopyIds)Require(Exact94(initial[copy],newEntities[copy]),"Explicit copy changed from completed94 baseline.");
    }
    private static void SourceGeometry95(JObject snapshot,Document95 metadata,Baseline95 baseline,int pass)
    {
        double dx=pass==1?5:-6,dy=pass==1?7:4;var entities=Rows95(snapshot,"entities");
        foreach(var curve in metadata.Curves.Values)
        {
            string type=curve.Role=="circle"?"circle":"arc";var row=entities[curve.Id];Require((string?)row["type"]==type,"Source curve type changed.");
            NearPoint95(row["center_mm"],Offset95(curve.Center,dx,dy));double radius=(double)row["radius_mm"]!,expected=curve.Role=="circle"?(pass==1?12:6):9;
            Require(double.IsFinite(radius)&&Math.Abs(radius-expected)<=1e-8,"First Core radius does not match circle edit or fixed arc9mm.");
            foreach(string role in curve.PointIds.Keys)
            {var target=Offset95(curve.Points[role],dx,dy);NearPoint95(entities[curve.PointIds[role]]["point_mm"],target);if(role is "start" or "end")NearPoint95(row[role+"_mm"],target);}
            CurveLocus94(row,type);
            // Arc radius/extent is unchanged: its ordered strokes are a rigid
            // translation. A resized circle may legitimately be retessellated;
            // use its native type/radius/center/closed locus, not old vertex count.
            if(type=="arc")Shift94((JArray)Rows95(baseline.Initial,"entities")[curve.Id]["strokes_mm"]!, (JArray)row["strokes_mm"]!,2,dx,dy);
            else{var strokes=(JArray)row["strokes_mm"]!;NearPoint95(new JArray(strokes[0],strokes[1]),new JArray(strokes[strokes.Count-2],strokes[strokes.Count-1]));}
        }
    }
    private static JObject Parameter95(JObject data,Document95 metadata)
    {
        Require((string?)data["id"]==metadata.SourceIds["parameter_text"]&&(string?)data["type"]=="text"&&(string?)data["text"]=="Bound=13","Only attested unselected13mm parameter text may relocate.");string markup=(string?)data["formatted_text"]??"";Require(markup.Length<=4096,"Parameter markup bound exceeded.");
        var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4111};using var input=new StringReader("<owner>"+markup+"</owner>");using var reader=XmlReader.Create(input,settings);var root=XElement.Load(reader,LoadOptions.PreserveWhitespace);var nodes=root.Nodes().ToArray();
        Require(nodes.Length==2&&nodes[0].GetType()==typeof(XText)&&((XText)nodes[0]).Value=="Bound="&&nodes[1] is XElement,"Parameter field grammar differs.");var parameter=(XElement)nodes[1];
        Require(parameter.Name==XName.Get("Parameter")&&parameter.Attributes().Count()==4&&parameter.Attributes().All(a=>a.Name.Namespace==XNamespace.None&&new[]{"Resolved","ComponentIdentifier","Name","Precision"}.Contains(a.Name.LocalName,StringComparer.Ordinal))
            &&(string?)parameter.Attribute("Resolved")=="True"&&(string?)parameter.Attribute("Name")=="S1Copy88Length"&&(string?)parameter.Attribute("Precision")=="0"&&parameter.Nodes().Count()==1&&parameter.FirstNode!.GetType()==typeof(XText)&&parameter.Value=="13","Parameter field is not exact unmodified89 binding.");
        string owner=(string?)parameter.Attribute("ComponentIdentifier")??"";Require(SamePath95(owner,metadata.Path),"Parameter field refers to an external/unattested native file.");parameter.Attribute("ComponentIdentifier")!.Value="owned-local:"+metadata.DocumentId+"#"+metadata.ParameterKey;
        string canonical=root.ToString(SaveOptions.DisableFormatting);var result=(JObject)data.DeepClone();result["formatted_text"]=canonical.Substring(7,canonical.Length-15);return result;
    }
}
