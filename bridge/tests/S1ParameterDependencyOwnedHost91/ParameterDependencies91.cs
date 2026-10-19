using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private sealed record DependencyTarget91(string InternalName,string Role,TextBox Text,PlanarSketch Sketch);
    private static readonly List<DependencyTarget91> Targets91=new();
    private static bool DependenciesDone91;
    private const int MaxParameters91=1024,MaxDependents91=1024,MaxTotalDependents91=8192;
    private static int TotalDependents91;
    private static DateTimeOffset ReadDeadline91;
    private delegate void ReadKey91(ref byte[] key,int context);
    private static string TypedKey91(ReadKey91 read)
    {
        byte[] key=Array.Empty<byte>();read(ref key,0);
        if(key.Length==0||key.Length>65536) throw new HostFailure73("Native dependency reference key missing or oversized.");
        return Convert.ToBase64String(key);
    }
    private static JObject Error91(string stage,Exception error)=>new(){["stage"]=stage,["status"]="UNKNOWN",["type"]=error.GetType().FullName,["hresult"]=error.HResult};
    private static void ReadBudget91()
    {
        GuardDocuments73();
        Require89(DateTimeOffset.UtcNow<ReadDeadline91&&DateTimeOffset.UtcNow<ExpiresUtc&&TotalDependents91<=MaxTotalDependents91,"Dependency91 bounded read deadline/budget reached.");
    }
    private static void RunParameterDependencies91()
    {
        Require89(!DependenciesDone91,"Dependency91 observations already consumed.");DependenciesDone91=true;
        Require89(Bearer==null&&!BootstrapWritten,"Dependency91 must precede any HTTP/bootstrap phase.");
        RequireIdle89();CheckInstallation73("installation_before_dependencies91");
        ReadDeadline91=DateTimeOffset.UtcNow.AddSeconds(90);TotalDependents91=0;
        var report=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["owned_pid"]=OwnedPid,["status"]="RUNNING",
            ["profile"]="Saved owned Part/Sheet fixture88, native absolute local parameter-field path; read-only dependency research",
            ["product_pass"]=false,["http_started"]=false,["targets"]=new JArray(),["parameters"]=new JArray(),
            ["not_covered"]=new JArray("Alias/unsaved/external fields", "Field-level attribution of multiple fields", "Parameter text copy", "Updates/persistence", "Other parameter families not instantiated by this fixture", "Desktop/Quest") };
        JObject? before=null;
        WritePublic73("parameter-dependencies91.json",report);
        try
        {
            ReadBudget91();before=CopyFullNative88();report["native_before"]=before;
            Require89(Targets91.Count==4&&Documents.Count==2,"Dependency91 requires four creator-known TextBox targets in two owned documents.");
            foreach(var target in Targets91)
            {
                ReadBudget91();var owned=Documents.Single(d=>d.InternalName==target.InternalName);
                Require89(SameCom73(target.Text.Parent,target.Sketch)&&SameCom73(target.Sketch.Parent,owned.Document.ComponentDefinition),"Creator-known target native ownership differs.");
                ((JArray)report["targets"]!).Add(new JObject { ["document_id"]=owned.DocumentId,["context"]=owned.Sheet?"Sheet":"Part",["role"]=target.Role,
                    ["text_key"]=TypedKey91(target.Text.GetReferenceKey),["sketch_key"]=TypedKey91(target.Sketch.GetReferenceKey),
                    ["native_type"]=target.Text.Type.ToString(),["formatted_text"]=target.Text.FormattedText,["native_path"]=owned.SavedPath });
            }
            // Validate the actual first profile, without treating markup as a generic
            // owner resolver. Target identity itself comes from the native creators.
            foreach(var owned in Documents)
            {
                var target=Targets91.Single(t=>t.InternalName==owned.InternalName&&t.Role=="parameter_field");
                var literal=Targets91.Single(t=>t.InternalName==owned.InternalName&&t.Role=="literal");
                var line=target.Sketch.SketchLines.Cast<SketchLine>().Single();
                var source=new CopyBinding89(owned.DocumentId,TypedKey91(target.Sketch.GetReferenceKey),
                    TypedKey91(((SketchEntity)line).GetReferenceKey),TypedKey91(literal.Text.GetReferenceKey));
                var state=ReadCopyLifecycle89(owned,source);
                try { _=OwnedParameterRelocation89.Normalize(state,owned.SavedPath!);report["profile_"+(owned.Sheet?"sheet":"part") ]="ABSOLUTE_LOCAL_PATH_VERIFIED"; }
                catch(Exception error) { report["profile_"+(owned.Sheet?"sheet":"part")]=Error91("validate_absolute_fixture_profile",error); }
            }
            foreach(var owned in Documents)
            {
                ReadBudget91();var parameters=owned.Document.ComponentDefinition.Parameters;
                int count=parameters.Count;Require89(count>=0&&count<=MaxParameters91,"Dependency91 parameter collection exceeds bounded budget.");
                for(int index=1;index<=count;index++)
                {
                    ReadBudget91();var row=new JObject { ["document_id"]=owned.DocumentId,["context"]=owned.Sheet?"Sheet":"Part",["collection_ordinal_observation_only"]=index,["dependents"]=new JArray() };
                    ((JArray)report["parameters"]!).Add(row);
                    try
                    {
                        Parameter parameter=parameters[index];
                        row["key"]=TypedKey91(parameter.GetReferenceKey);row["native_type"]=parameter.Type.ToString();row["name"]=parameter.Name;
                        row["parameter_type"]=parameter.ParameterType.ToString();row["units"]=parameter.get_Units();row["expression"]=parameter.Expression;
                        row["parent_same_com_as_owned_parameters"]=SameCom73(parameter.Parent,parameters);
                        ObjectCollection dependents=parameter.Dependents;
                        if(dependents==null) { row["status"]="UNKNOWN_NULL_DEPENDENTS";continue; }
                        int dependentCount=dependents.Count;row["dependent_count_reported"]=dependentCount;
                        if(dependentCount<0||dependentCount>MaxDependents91) { row["status"]="UNKNOWN_DEPENDENT_BUDGET";continue; }
                        bool unknown=false;int directTargets=0;
                        for(int member=1;member<=dependentCount;member++)
                        {
                            ReadBudget91();Require89(++TotalDependents91<=MaxTotalDependents91,"Dependency91 total dependent budget exceeded.");
                            var entry=new JObject { ["collection_ordinal_observation_only"]=member };
                            ((JArray)row["dependents"]!).Add(entry);
                            try
                            {
                                object dependent=dependents[member];
                                if(dependent==null) { entry["status"]="UNKNOWN_NULL_MEMBER";unknown=true;continue; }
                                bool memberUnknown=false;
                                try
                                {
                                    var identity=DescribeDependent91(dependent);entry["typed_native_identity"]=identity;
                                    if(((string?)identity["status"]??"").StartsWith("UNKNOWN",StringComparison.Ordinal)) memberUnknown=true;
                                }
                                catch(Exception error) { entry["typed_native_identity"]=Error91("read_typed_dependent_identity",error);memberUnknown=true; }
                                var identities=new JArray();entry["target_same_com"]=identities;
                                foreach(var target in Targets91)
                                {
                                    try
                                    {
                                        bool same=SameCom73(dependent,target.Text);
                                        identities.Add(new JObject { ["target_text_key"]=TypedKey91(target.Text.GetReferenceKey),["target_internal_name"]=target.InternalName,["target_role"]=target.Role,["same_com"]=same });
                                        if(same) directTargets++;
                                    }
                                    catch(Exception error) { identities.Add(Error91("SameCom_dependent_target",error));memberUnknown=true; }
                                }
                                unknown|=memberUnknown;entry["status"]=memberUnknown?"UNKNOWN_PARTIAL_READ":"OBSERVED_PUBLIC_API";
                            }
                            catch(Exception error) { entry["failure"]=Error91("read_dependent_member",error);entry["status"]="UNKNOWN";unknown=true; }
                        }
                        row["direct_known_textbox_edges"]=directTargets;
                        row["status"]=unknown?"UNKNOWN_PARTIAL_READ":directTargets>0?"OBSERVED_DIRECT_TEXTBOX_EDGE":"UNKNOWN_NO_DIRECT_KNOWN_TEXTBOX_EDGE";
                        row["interpretation"]=directTargets>0?"Aggregate parameter-to-native-TextBox dependency observed; not a per-XML-field owner resolver":"No direct edge established; empty/nonmatching collection is UNKNOWN, never proof of absence";
                    }
                    catch(Exception error) { row["failure"]=Error91("read_parameter_dependents",error);row["status"]="UNKNOWN"; }
                    WritePublic73("parameter-dependencies91.json",report);
                }
            }
            report["status"]="OBSERVATIONS_COMPLETE";
        }
        catch(Exception error) { report["status"]="FAILED";report["failure"]=Error91("probe_fatal",error);throw; }
        finally
        {
            bool exact=false;
            try
            {
                var after=CopyFullNative88();report["native_after"]=after;
                exact=before!=null&&JToken.DeepEquals(before,after);report["readonly_full_native_exact"]=exact;
            }
            catch(Exception error) { report["readonly_verification_failure"]=Error91("mandatory_native_after",error); }
            if(!exact) report["status"]="FAILED_READONLY_VERIFICATION";
            report["parameter_count_observed"]=((JArray)report["parameters"]!).Count;report["dependent_members_observed"]=TotalDependents91;
            report["unknown_parameter_rows"]=((JArray)report["parameters"]!).Count(p=>((string?)p["status"]??"").StartsWith("UNKNOWN",StringComparison.Ordinal));
            WritePublic73("parameter-dependencies91.json",report);CheckInstallation73("installation_after_dependencies91");
            Require89(exact,"Dependency91 read-only native state differs or cannot be verified.");
        }
    }
    private static JObject DescribeDependent91(object value)
    {
        // Only documented typed interfaces. Unknown RCWs are not inspected through
        // raw IDispatch/type-info/vtables and are not assigned an invented native type.
        JObject Known(string type,ReadKey91 read,string adapter)
        {
            var row=new JObject { ["status"]="TYPED_PUBLIC_API",["native_type"]=type,["adapter"]=adapter };
            try { row["key"]=TypedKey91(read); }
            catch(Exception error) { row["status"]="UNKNOWN_KEY";row["key_failure"]=Error91("typed_GetReferenceKey",error); }
            return row;
        }
        return value switch
        {
            TextBox text=>Known(text.Type.ToString(),text.GetReferenceKey,"TextBox"),
            SketchEntity entity=>Known(entity.Type.ToString(),entity.GetReferenceKey,"SketchEntity"),
            Sketch sketch=>Known(sketch.Type.ToString(),sketch.GetReferenceKey,"Sketch"),
            Parameter parameter=>Known(parameter.Type.ToString(),parameter.GetReferenceKey,"Parameter"),
            PartFeature feature=>Known(feature.Type.ToString(),feature.GetReferenceKey,"PartFeature"),
            DimensionConstraint dimension=>Known(dimension.Type.ToString(),dimension.GetReferenceKey,"DimensionConstraint"),
            GeometricConstraint constraint=>Known(constraint.Type.ToString(),constraint.GetReferenceKey,"GeometricConstraint"),
            Document document=>new JObject { ["status"]="TYPED_PUBLIC_API_NO_KEY",["adapter"]="Document",["native_type"]=document.Type.ToString() },
            _=>new JObject { ["status"]="UNKNOWN_PUBLIC_TYPE",["managed_wrapper_type"]=value.GetType().FullName,["key_capture"]="NOT_AVAILABLE_WITH_VERIFIED_TYPED_ADAPTER" }
        };
    }
}
