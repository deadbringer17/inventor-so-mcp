using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void RequireIdle86()
    {
        GuardDocuments73();
        Require86(Application!.CommandManager.ActiveCommand == "AppSelectNorthwestArrowCmd", "Native command prevents owned persistence.");
        var current = Application.TransactionManager.CurrentTransaction;
        if (current == null) return;
        bool identified = true;
        try { _ = current.HasParentTransaction; }
        catch (COMException error) when (error.ErrorCode == unchecked((int)0x80004005)) { identified = false; }
        Require86(!identified, "Identified native transaction prevents owned persistence.");
    }

    private static void PersistAssociationCopies86()
    {
        Require86((bool?)Evidence["http_stopped_before_persistence"] == true && Bearer == null,
            "HTTP must be completely stopped before lifecycle mutation.");
        Require86(AssociationReadIndex86 == 6, "All six first-read/native checkpoints are required before persistence.");
        RequireIdle86();
        // This command never accepts a path, document id, parameter or COM handle.
        // All copy paths are derived from this run owned directory and fixed names.
        var rows = new JArray();
        foreach (var original in Documents.ToArray())
        {
            Phase = original.Sheet ? "persist_sheet86" : "persist_part86";
            RequireIdle86();
            Require86(original.SavedPath != null && !string.IsNullOrEmpty(original.DocumentId), "Owned source is not saved.");
            string originalPath = original.SavedPath!, originalId = original.DocumentId;
            string originalHash = HashFile73(originalPath), originalInternal = original.InternalName;
            Require86(originalHash == original.BaselineSha, "Original fixture disk bytes changed before SaveCopy.");
            var before = ReadAssociation86(original, 30, true);
            string copyPath = Path.GetFullPath(Path.Combine(FixtureDirectory,
                original.Sheet ? "S1_Association86_Sheet_Copy.ipt" : "S1_Association86_Part_Copy.ipt"));
            Require86(Path.GetDirectoryName(copyPath) == Path.GetFullPath(FixtureDirectory) && !File.Exists(copyPath)
                && !Directory.Exists(copyPath) && !SamePath73(copyPath, originalPath), "Copy destination is not a new exact owned file.");
            var transition = new JObject { ["context"] = original.Sheet ? "sheetmetal" : "part",
                ["original_path"] = originalPath, ["original_document_id"] = originalId, ["original_internal_name"] = originalInternal,
                ["copy_path"] = copyPath, ["before_save_copy"] = before, ["state"] = "before_save_copy" };
            rows.Add(transition); Evidence["persistence86"] = rows; PersistOwnership73();
            original.Document.SaveAs(copyPath, true);
            // SaveCopy must leave the open original in place. Never open a second
            // document with the same InternalName while that original remains loaded.
            GuardDocuments73();
            Require86(original.Document.InternalName == originalInternal && SamePath73(original.Document.FullFileName, originalPath)
                && HashFile73(originalPath) == originalHash, "SaveCopy moved or wrote the original.");
            string copyHash = HashFile73(copyPath);
            var afterSave = ReadAssociation86(original, 30, true);
            Require86(JToken.DeepEquals(before["keys"], afterSave["keys"]), "SaveCopy changed persistent object keys.");
            transition["copy_sha256"] = copyHash; transition["after_save_copy"] = afterSave;
            transition["state"] = "copy_written_original_still_owned"; PersistOwnership73();
            RequireIdle86();
            original.Document.Close(true); // SkipSave: original disk baseline must remain unchanged.
            Documents.Remove(original); PersistOwnership73(); GuardDocuments73();
            transition["state"] = "original_closed"; PersistOwnership73();
            Require86(!Application!.Documents.Cast<Document>().Any(d => d.InternalName == originalInternal)
                && HashFile73(originalPath) == originalHash, "Original identity remains loaded or its baseline changed.");
            // The one returned owned RCW is released only after confirmed close/removal.
            if (Marshal.IsComObject(original.Document)) Marshal.ReleaseComObject(original.Document);
            RequireIdle86();
            var opened = (PartDocument)Application.Documents.Open(copyPath, true);
            // Adopt returned object immediately so any later refusal still owns cleanup.
            var replacement = new OwnedDocument73(opened, original.Sheet, opened.InternalName, copyPath)
            { SavedPath = copyPath, BaselineSha = copyHash };
            Documents.Add(replacement); PersistOwnership73(); GuardDocuments73();
            Require86(opened.InternalName == originalInternal && SamePath73(opened.FullFileName, copyPath)
                && (opened.ComponentDefinition is SheetMetalComponentDefinition) == original.Sheet,
                "Reopened SaveCopy identity/path/subtype differs.");
            replacement.DocumentId = DocumentIdComposer.Compose(opened.InternalName, opened.FullFileName, false);
            // The real composer preserves doc_InternalName for a unique open identity.
            // HTTP is already stopped: no old session or references are reused after Open.
            Require86(replacement.DocumentId == DocumentIdComposer.Prefix+originalInternal,
                "Reopened unique document identity differs from the authoritative composer.");
            PersistOwnership73();
            // First semantic native read after Open/adoption/ownership guards. No
            // Update, Rebuild, new oracle, mutation, display repair or retry is allowed.
            var reopened = ReadAssociation86(replacement, 30, true);
            Require86(JToken.DeepEquals(before["keys"], reopened["keys"]) && HashFile73(copyPath) == copyHash
                && HashFile73(originalPath) == originalHash, "Reopened copy lost keys or wrote owned files.");
            transition["reopened_first_native_read"] = reopened;
            transition["reopened_document_id"] = replacement.DocumentId;
            transition["wire_document_id_rule"]="unique_internal_name_without_path_suffix";
            transition["old_http_binding_reused"]=false;
            transition["original_disk_unchanged"] = true; transition["copy_disk_readonly"] = true;
            transition["identity_overlap_avoided"] = true; transition["state"] = "reopened_inspected";
            WritePublic73("persistence-" + (original.Sheet ? "sheet" : "part") + "86.json", transition);
            PersistOwnership73();
        }
        RequireIdle86();
        WritePublic73("persistence86.json", new JObject { ["schema"] = 1, ["run_id"] = Settings.RunId,
            ["status"] = "OBSERVATIONS_COMPLETE", ["documents"] = rows,
            ["http_stopped"] = true, ["rebuild_or_repair"] = false, ["open_s1_gates"] = 18,
            ["quest_proof"] = false, ["physical_proof"] = false });
    }
}
