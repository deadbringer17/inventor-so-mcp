using System.Runtime.InteropServices;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;

internal static partial class Program
{
    private static int RunIsolatedImageProbe45(string output, bool transformResearch = false, bool history = false, bool saveCopy = false, bool download = false, bool frame = false, bool nativePixels = false, bool liveFrame = false, bool liveSaveCopy = false)
    {
        var report = new JObject { ["captured_utc"] = DateTime.UtcNow.ToString("O"),
            ["evidence"] = "Separate owned Inventor process, native image batch handler; no HTTP/Quest/desktop equivalence" };
        Inventor.Application? existing = null, owned = null;
        PartDocument? fixture = null;
        bool canQuit = false;
        int exit = 1;
        var priorScratch = System.IO.Directory.GetDirectories(System.IO.Path.GetTempPath(), "inventor-so-image-*")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        JObject ReadExisting()
        {
            if (existing == null) return new JObject();
            return new JObject { ["active"] = existing.ActiveDocument == null ? "" : existing.ActiveDocument.InternalName,
                ["documents"] = new JArray(existing.Documents.Cast<Document>().Select(d => new JObject
                { ["id"] = d.InternalName, ["name"] = d.DisplayName, ["revision"] = d.DatabaseRevisionId, ["dirty"] = d.Dirty })
                    .OrderBy(d => (string?)d["id"], StringComparer.Ordinal)) };
        }
        try
        {
            var type = Type.GetTypeFromProgID("Inventor.Application")!; var clsid = type.GUID;
            try { GetActiveObject(ref clsid, IntPtr.Zero, out var current); existing = (Inventor.Application)current; }
            catch (COMException error) when (error.HResult == unchecked((int)0x800401E3)) { }
            report["existing_before"] = ReadExisting();
            uint previousPid = 0;
            if (existing != null && GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)existing.MainFrameHWND)), out previousPid) == 0)
                throw new InvalidOperationException("Cannot identify existing Inventor process.");
            owned = (Inventor.Application)Activator.CreateInstance(type)!;
            if (GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)owned.MainFrameHWND)), out uint pid) == 0
                || pid == 0 || pid == previousPid || owned.Documents.Count != 0)
                throw new InvalidOperationException("COM did not return a distinct empty Inventor process; no documents will be modified or instance closed.");
            canQuit = true; report["owned_process_id"] = pid;
            var contexts = new JArray(); report["contexts"] = contexts;
            foreach (bool sheet in new[] { false, true })
            {
                string template = sheet ? owned.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,
                    SystemOfMeasureEnum.kDefaultSystemOfMeasure, DraftingStandardEnum.kDefault_DraftingStandard,
                    "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}") : owned.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
                fixture = (PartDocument)owned.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
                fixture.DisplayName = "S1_ArcInput_Oracle.ipt";
                var sketch = fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);
                sketch.Name = "S1_ArcInput_1";
                var context = new JObject(); contexts.Add(context);
                if (liveSaveCopy) RunImageLiveSaveCopy52(owned, fixture, context);
                else if (nativePixels || liveFrame) RunImagePixelsInProcess50(owned, fixture, context, verifyFrame: liveFrame);
                else if (download || frame) RunImageDownloadProbe48(owned, fixture, context, frame);
                else if (saveCopy) RunImageSaveCopyProbe46(owned, fixture, context);
                else if (history) RunImageHistoryProbe46(owned, fixture, context);
                else if (transformResearch) RunImageTransformResearch45(owned, fixture, context);
                else RunImageBatchProbe(owned, fixture, context);
                fixture.Close(true); fixture = null;
                if (owned.Documents.Count != 0) throw new InvalidOperationException("Owned fixture closure incomplete.");
            }
            report["outcome"] = liveSaveCopy ? "PASS legacy embedded native bitmap/live frame persisted through saved copy and reopen; no XR or physical proof"
                : liveFrame ? "PASS in-process native bitmap colors and live UV-frame theory; no XR or physical proof"
                : nativePixels ? "OBSERVATIONS in-process native bitmap extraction and transforms; no XR or physical proof"
                : frame ? "PASS native persistent image corner bindings and download; texture and Quest unverified"
                : download ? "PASS native managed image bounded download; no HTTP or Quest proof"
                : saveCopy ? "PASS native embedded image payload saved copy and hidden reopen; no Quest proof"
                : history ? "PARTIAL native image model history and payload assertions; exact OLE Undo unresolved"
                : transformResearch ? "OBSERVATIONS collected: native image transform API; no product PASS"
                : "PASS native owned-process image batch cases; no Quest or physical proof";
            exit = 0;
        }
        catch (Exception error) { report["outcome"] = "FAIL"; report["error"] = error.ToString(); }
        finally
        {
            try
            {
                if (canQuit && owned != null)
                {
                    if (fixture != null && fixture.DisplayName == "S1_ArcInput_Oracle.ipt" && string.IsNullOrEmpty(fixture.FullFileName))
                        fixture.Close(true);
                    if (owned.Documents.Count == 0) { owned.Quit(); report["owned_process_quit_requested"] = true; }
                    else throw new InvalidOperationException("Unexpected documents in owned process; refusing to quit.");
                }
            }
            catch (Exception error) { report["cleanup_error"] = error.ToString(); exit = 1; }
            try
            {
                report["existing_after"] = ReadExisting();
                report["existing_unchanged"] = JToken.DeepEquals(report["existing_before"], report["existing_after"]);
                if (!(bool)report["existing_unchanged"]!) exit = 1;
            }
            catch (Exception error) { report["existing_readback_error"] = error.ToString(); exit = 1; }
            var remainingScratch = System.IO.Directory.GetDirectories(System.IO.Path.GetTempPath(), "inventor-so-image-*")
                .Where(path => !priorScratch.Contains(path)).ToArray();
            report["remaining_native_image_scratch"] = new JArray(remainingScratch);
            if (remainingScratch.Length != 0) exit = 1;
            if (exit != 0) report["outcome"] = "FAIL";
            File.WriteAllText(output, report.ToString());
        }
        return exit;
    }
}
