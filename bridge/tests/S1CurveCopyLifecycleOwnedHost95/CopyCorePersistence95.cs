using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void ReopenCore95(bool sheet)
    {
        RequireIdle95();var original=Owned95(sheet);var b=Bindings95[sheet];
        Require89(((JArray)Rows95[sheet]["updates"]!).Count==2&&((JArray)Rows95[sheet]["updates"]!).All(u=>(bool?)u["copies_independent_exact"]==true),"Two native/Core source update checkpoints required before reopen.");
        Require89(Application!.ActiveDocument!=null&&SameCom73(Application.ActiveDocument,original.Document),"Core95 reopen active document differs.");
        string originalPath=original.SavedPath??throw new HostFailure73("Saved owned original required.");
        string originalHash=HashFile73(originalPath),identity=original.InternalName;
        Require89(originalHash==original.BaselineSha,"Core95 original disk baseline changed.");
        string destination=Path.GetFullPath(Path.Combine(FixtureDirectory,sheet?"S1_CopyCore95_Sheet_Reopen.ipt":"S1_CopyCore95_Part_Reopen.ipt"));
        Require89(Path.GetDirectoryName(destination)==Path.GetFullPath(FixtureDirectory)&&!File.Exists(destination)&&!Directory.Exists(destination)&&!SamePath73(destination,originalPath),"Core95 SaveCopy destination is not a new exact owned file.");
        var transition=new JObject { ["original_path"]=originalPath,["original_sha256"]=originalHash,["copy_path"]=destination,
            ["state"]="before_save_copy",["before_save_copy"]=Rows95[sheet]["updates"]![1]!["first_native_read_after_core"]!.DeepClone(),["http_remains_live"]=true };
        Rows95[sheet]["persistence"]=transition;Evidence["copy_core95_persistence_transition"]=transition;
        PersistOwnership73();WritePublic73("copy-native-lifecycle95.json",Report95!);
        Phase="core95_save_copy";original.Document.SaveAs(destination,true);GuardDocuments73();
        Require89(original.Document.InternalName==identity&&SamePath73(original.Document.FullFileName,originalPath)&&HashFile73(originalPath)==originalHash,"Core95 SaveCopy moved/wrote original.");
        string copyHash=HashFile73(destination);transition["copy_sha256"]=copyHash;transition["state"]="copy_written_original_open";
        PersistOwnership73();WritePublic73("copy-native-lifecycle95.json",Report95!);
        // No semantic read here: all geometry/glyph assertions are delayed until
        // inspect_reopen, after the client's first Core snapshot has passed.
        RequireIdle95();Phase="core95_close_original";original.Document.Close(true);
        Documents.Remove(original);PersistOwnership73();GuardDocuments73();transition["state"]="original_closed";
        PersistOwnership73();WritePublic73("copy-native-lifecycle95.json",Report95!);
        Require89(!Application!.Documents.Cast<Document>().Any(d=>d.InternalName==identity)&&HashFile73(originalPath)==originalHash,"Core95 original identity remains open or original disk changed.");
        if(Marshal.IsComObject(original.Document)) Marshal.ReleaseComObject(original.Document);
        RequireIdle95();Phase="core95_open_saved_copy";transition["state"]="pending_open_identity_validation";
        PersistOwnership73();WritePublic73("copy-native-lifecycle95.json",Report95!);
        var opened=(PartDocument)Application.Documents.Open(destination,true);
        GuardProcess73();
        Require89(opened.InternalName==identity&&SamePath73(opened.FullFileName,destination)
            &&(opened.ComponentDefinition is SheetMetalComponentDefinition)==sheet&&Application.Documents.Count==Documents.Count+1,
            "Core95 returned copy identity/path/subtype/inventory differs; do not adopt or close it.");
        string reopenedId=DocumentIdComposer.Compose(opened.InternalName,opened.FullFileName,false);
        Require89(reopenedId==DocumentIdComposer.Prefix+identity&&reopenedId==b.DocumentId,"Core95 returned document composer differs; do not adopt it.");
        foreach(Document native in Application.Documents)
        {
            if(SameCom73(native,opened)) continue;
            Require89(Documents.Any(known=>SameCom73(known.Document,native)&&known.InternalName==native.InternalName
                &&known.SavedPath!=null&&SamePath73(known.SavedPath,native.FullFileName)),"Core95 Open introduced/changed an unrelated document; do not adopt returned object.");
        }
        var adopted=new OwnedDocument73(opened,sheet,opened.InternalName,destination) { SavedPath=destination,BaselineSha=copyHash,DocumentId=reopenedId };
        Documents.Add(adopted);PersistOwnership73();GuardDocuments73();
        Require89(Application.ActiveDocument!=null&&SameCom73(Application.ActiveDocument,opened),"Core95 reopened copy is not active.");
        transition["state"]="reopened_awaiting_core_first_snapshot";transition["reopened_document_id"]=reopenedId;
        transition["post_open_native_semantic_reads"]=0;transition["old_typed_refs_reused"]=false;
        RequireIdle95();WritePublic73("copy-native-lifecycle95.json",Report95!);
    }
    private static void InspectCoreReopen95(bool sheet)
    {
        RequireIdle95();var owned=Owned95(sheet);var b=Bindings95[sheet];
        Require89(Application!.ActiveDocument!=null&&SameCom73(Application.ActiveDocument,owned.Document),"Core95 reopened inspect active document differs.");
        var transition=(JObject)Rows95[sheet]["persistence"]!;
        Require89((string?)transition["state"]=="reopened_awaiting_core_first_snapshot","Core95 reopened copy is not awaiting Core proof.");
        string originalPath=(string)transition["original_path"]!,copyPath=(string)transition["copy_path"]!;
        var full=FullState92();var first=DocumentState95(full,owned);transition["first_reopened_native_read"]=Raw95("inspect_reopen_"+(sheet?"Sheet":"Part"),full);
        var expected=LastInspect95[sheet];
        var expectedPersistent=PersistentSemantic95(expected,originalPath);
        var actualPersistent=PersistentSemantic95(first,copyPath);
        Require89(ExactNative94(expectedPersistent,actualPersistent),"Core95 reopened state differs beyond strict owned NONcopied parameter relocation.");
        Equal88(OwnedRemainder95(LastFull95[sheet],owned.DocumentId,true),OwnedRemainder95(full,owned.DocumentId,true),"Core95 reopen other sketches/all parameters/body");
        Equal88(Unrelated95(LastFull95[sheet],owned.DocumentId),Unrelated95(full,owned.DocumentId),"Core95 reopen unrelated document");
        transition["local_parameter_owner_relocation_verified"]=true;
        transition["untouched_fixture_parameter_owner_relocation_verified"]=true;
        Require89(HashFile73(originalPath)==(string)transition["original_sha256"]!&&HashFile73(copyPath)==(string)transition["copy_sha256"]!,"Core95 reopened read changed original/copy disk bytes.");
        transition["state"]="reopened_verified";transition["original_disk_unchanged"]=true;transition["copy_disk_readonly"]=true;
        transition["first_core_snapshot_precedes_native_read"]="Protocol ordering; independent client report required.";
        Rows95[sheet]["state"]="two_source_updates_and_reopen_observed";
        LastInspect95[sheet]=first;LastFull95[sheet]=full;Report95!["reopens"]=(int)Report95!["reopens"]!+1;PersistOwnership73();
    }
}
