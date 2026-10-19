using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void ReopenCore90(bool sheet)
    {
        RequireIdle89();var original=Owned90(sheet);var b=Bindings90[sheet];
        Require89(((JArray)Rows90[sheet]["updates"]!).Count==2&&((JArray)Rows90[sheet]["updates"]!).All(u=>(bool?)u["copies_independent_exact"]==true),"Two native/Core source update checkpoints required before reopen.");
        Require89(Application!.ActiveDocument!=null&&SameCom73(Application.ActiveDocument,original.Document),"Core90 reopen active document differs.");
        string originalPath=original.SavedPath??throw new HostFailure73("Saved owned original required.");
        string originalHash=HashFile73(originalPath),identity=original.InternalName;
        Require89(originalHash==original.BaselineSha,"Core90 original disk baseline changed.");
        string destination=Path.GetFullPath(Path.Combine(FixtureDirectory,sheet?"S1_CopyCore90_Sheet_Reopen.ipt":"S1_CopyCore90_Part_Reopen.ipt"));
        Require89(Path.GetDirectoryName(destination)==Path.GetFullPath(FixtureDirectory)&&!File.Exists(destination)&&!Directory.Exists(destination)&&!SamePath73(destination,originalPath),"Core90 SaveCopy destination is not a new exact owned file.");
        var transition=new JObject { ["original_path"]=originalPath,["original_sha256"]=originalHash,["copy_path"]=destination,
            ["state"]="before_save_copy",["before_save_copy"]=LastInspect90[sheet].DeepClone(),["http_remains_live"]=true };
        Rows90[sheet]["persistence"]=transition;Evidence["copy_core90_persistence_transition"]=transition;
        PersistOwnership73();WritePublic73("copy-native-lifecycle90.json",Report90!);
        Phase="core90_save_copy";original.Document.SaveAs(destination,true);GuardDocuments73();
        Require89(original.Document.InternalName==identity&&SamePath73(original.Document.FullFileName,originalPath)&&HashFile73(originalPath)==originalHash,"Core90 SaveCopy moved/wrote original.");
        string copyHash=HashFile73(destination);transition["copy_sha256"]=copyHash;transition["state"]="copy_written_original_open";
        PersistOwnership73();WritePublic73("copy-native-lifecycle90.json",Report90!);
        // No semantic read here: all geometry/glyph assertions are delayed until
        // inspect_reopen, after the client's first Core snapshot has passed.
        RequireIdle89();Phase="core90_close_original";original.Document.Close(true);
        Documents.Remove(original);PersistOwnership73();GuardDocuments73();transition["state"]="original_closed";
        PersistOwnership73();WritePublic73("copy-native-lifecycle90.json",Report90!);
        Require89(!Application!.Documents.Cast<Document>().Any(d=>d.InternalName==identity)&&HashFile73(originalPath)==originalHash,"Core90 original identity remains open or original disk changed.");
        if(Marshal.IsComObject(original.Document)) Marshal.ReleaseComObject(original.Document);
        RequireIdle89();Phase="core90_open_saved_copy";transition["state"]="pending_open_identity_validation";
        PersistOwnership73();WritePublic73("copy-native-lifecycle90.json",Report90!);
        var opened=(PartDocument)Application.Documents.Open(destination,true);
        GuardProcess73();
        Require89(opened.InternalName==identity&&SamePath73(opened.FullFileName,destination)
            &&(opened.ComponentDefinition is SheetMetalComponentDefinition)==sheet&&Application.Documents.Count==Documents.Count+1,
            "Core90 returned copy identity/path/subtype/inventory differs; do not adopt or close it.");
        string reopenedId=DocumentIdComposer.Compose(opened.InternalName,opened.FullFileName,false);
        Require89(reopenedId==DocumentIdComposer.Prefix+identity&&reopenedId==b.Source.DocumentId,"Core90 returned document composer differs; do not adopt it.");
        foreach(Document native in Application.Documents)
        {
            if(SameCom73(native,opened)) continue;
            Require89(Documents.Any(known=>SameCom73(known.Document,native)&&known.InternalName==native.InternalName
                &&known.SavedPath!=null&&SamePath73(known.SavedPath,native.FullFileName)),"Core90 Open introduced/changed an unrelated document; do not adopt returned object.");
        }
        var adopted=new OwnedDocument73(opened,sheet,opened.InternalName,destination) { SavedPath=destination,BaselineSha=copyHash,DocumentId=reopenedId };
        Documents.Add(adopted);PersistOwnership73();GuardDocuments73();
        Require89(Application.ActiveDocument!=null&&SameCom73(Application.ActiveDocument,opened),"Core90 reopened copy is not active.");
        transition["state"]="reopened_awaiting_core_first_snapshot";transition["reopened_document_id"]=reopenedId;
        transition["post_open_native_semantic_reads"]=0;transition["old_typed_refs_reused"]=false;
        RequireIdle89();WritePublic73("copy-native-lifecycle90.json",Report90!);
    }
    private static void InspectCoreReopen90(bool sheet)
    {
        RequireIdle89();var owned=Owned90(sheet);var b=Bindings90[sheet];
        Require89(Application!.ActiveDocument!=null&&SameCom73(Application.ActiveDocument,owned.Document),"Core90 reopened inspect active document differs.");
        var transition=(JObject)Rows90[sheet]["persistence"]!;
        Require89((string?)transition["state"]=="reopened_awaiting_core_first_snapshot","Core90 reopened copy is not awaiting Core proof.");
        string originalPath=(string)transition["original_path"]!,copyPath=(string)transition["copy_path"]!;
        var first=ReadCopyLifecycle89(owned,b.Source);transition["first_reopened_native_read"]=first;
        var expected=LastInspect90[sheet];
        transition["raw_first_difference"]=FirstDifference89(Semantic89(expected),Semantic89(first),"$");
        var expectedPersistent=OwnedParameterRelocation89.Normalize(expected,originalPath);
        var actualPersistent=OwnedParameterRelocation89.Normalize(first,copyPath);
        transition["first_difference"]=FirstDifference89(expectedPersistent,actualPersistent,"$");
        transition["local_parameter_owner_relocation_verified"]=true;
        WritePublic73("copy-native-lifecycle90.json",Report90!);
        Require89(JToken.DeepEquals(expectedPersistent,actualPersistent),"Core90 reopened state differs beyond strict owned NONcopied parameter-text relocation.");
        CheckIndependence89(OwnedParameterRelocation89.Normalize(Initial90[sheet],originalPath),actualPersistent,b.Source);
        Require89(HashFile73(originalPath)==(string)transition["original_sha256"]!&&HashFile73(copyPath)==(string)transition["copy_sha256"]!,"Core90 reopened read changed original/copy disk bytes.");
        transition["state"]="reopened_verified";transition["original_disk_unchanged"]=true;transition["copy_disk_readonly"]=true;
        transition["first_core_snapshot_precedes_native_read"]="Protocol ordering; independent client report required.";
        Rows90[sheet]["state"]="two_source_updates_and_reopen_observed";
        LastInspect90[sheet]=first;Report90!["reopens"]=(int)Report90!["reopens"]!+1;PersistOwnership73();
    }
}

