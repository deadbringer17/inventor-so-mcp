using System.Diagnostics;
using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal sealed class OwnedDocument73(PartDocument document, bool sheet, string internalName, string intendedPath)
{
    internal PartDocument Document { get; } = document;
    internal bool Sheet { get; } = sheet;
    internal string InternalName { get; } = internalName;
    internal string IntendedPath { get; } = intendedPath;
    internal string? SavedPath { get; set; }
    internal string DocumentId { get; set; } = "";
    internal string BaselineSha { get; set; } = "";
}

internal static partial class Program
{
    private static Dictionary<int, long> Inventory73()
    {
        var result = new Dictionary<int, long>();
        foreach (var process in Process.GetProcessesByName("Inventor"))
            using (process) result.Add(process.Id, process.StartTime.ToUniversalTime().Ticks);
        return result;
    }
    private static JArray ProcessJson73(Dictionary<int, long> values) => new(values.OrderBy(p => p.Key).Select(p => new JObject
    { ["pid"] = p.Key, ["start_utc_ticks"] = p.Value }));

    private static void GuardProcess73()
    {
        if (System.Environment.CurrentManagedThreadId != StaThread || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA
            || Application == null || OwnedPid == 0 || OwnedPid == 77764 || OwnedPid == 91056 || Existing.ContainsKey(OwnedPid))
            throw new HostFailure73("Owned Inventor STA/process proof is unavailable.");
        using var process = Process.GetProcessById(OwnedPid);
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != OwnedStart)
            throw new HostFailure73("Owned process identity changed.");
        GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)Application.MainFrameHWND)), out uint actual);
        if (actual != OwnedPid) throw new HostFailure73("COM application no longer belongs to the owned PID.");
    }

    private static void GuardDocuments73()
    {
        GuardProcess73();
        if (Application!.Documents.Count != Documents.Count) throw new HostFailure73("Owned document count differs; no mutation allowed.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Document native in Application.Documents)
        {
            var matches = Documents.Where(d => d.InternalName == native.InternalName).ToArray();
            if (matches.Length != 1 || !SameCom73(matches[0].Document, native) || !seen.Add(native.InternalName))
                throw new HostFailure73("An unowned or ambiguous document is present.");
            var owned = matches[0];
            if (owned.SavedPath == null)
            {
                if (!string.IsNullOrEmpty(native.FullFileName)) throw new HostFailure73("Unsaved owned document acquired an unexpected path.");
            }
            else if (!SamePath73(native.FullFileName, owned.SavedPath))
                throw new HostFailure73("Owned saved document moved to another path.");
        }
    }
    private static bool SameCom73(object a, object b)
    {
        IntPtr first = Marshal.GetIUnknownForObject(a), second = Marshal.GetIUnknownForObject(b);
        try { return first == second; } finally { Marshal.Release(first); Marshal.Release(second); }
    }
    private static bool SamePath73(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void WaitDescriptor73()
    {
        TargetId = "inventor-2027-" + OwnedPid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string path = Path.Combine(Settings.DescriptorDirectory, TargetId + ".json");
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(45))
        {
            GuardProcess73();
            if (File.Exists(path))
            {
                try
                {
                    if (new FileInfo(path).Length > 32768) throw new HostFailure73("Owned descriptor exceeds the metadata budget.");
                    var descriptor = JsonConvert.DeserializeObject<TargetDescriptor>(File.ReadAllText(path));
                    if (descriptor?.TargetId == TargetId && descriptor.ProcessId == OwnedPid && descriptor.InventorYear == 2027
                        && descriptor.HostApp == "Inventor" && descriptor.Transport == "pipe" && !string.IsNullOrEmpty(descriptor.PipeName)
                        && !string.IsNullOrEmpty(descriptor.AuthToken) && descriptor.LastHeartbeatUtc.UtcTicks >= OwnedStart
                        && descriptor.LastHeartbeatUtc >= DateTimeOffset.UtcNow.AddSeconds(-120)) return;
                    // Auth remains in the local object only. No descriptor or exception text is serialized.
                }
                catch (IOException) { }
                catch (JsonException) { }
            }
            Thread.Sleep(100);
        }
        throw new HostFailure73("Exact owned authenticated add-in descriptor unavailable.");
    }

    private static void PersistOwnership73()
    {
        Evidence["owned_documents"] = new JArray(Documents.Select(d => new JObject
        { ["context"] = d.Sheet ? "sheetmetal" : "part", ["internal_name"] = d.InternalName,
            ["intended_path"] = d.IntendedPath, ["saved_path"] = d.SavedPath, ["document_id"] = d.DocumentId,
            ["baseline_file_sha256"] = d.BaselineSha }));
        WritePublic73("ownership.json", Evidence);
    }

    private static void CloseOwned73()
    {
        if (OwnedPid == 0 || Application == null) return; // Never Quit a returned preexisting COM instance.
        GuardDocuments73();
        // Use the same read-only idle classifier as ActivateOpenDocumentXrHandler.
        // Inventor 2027 exposes a nonnull unidentified transaction while idle.
        if (Application.CommandManager.ActiveCommand != "AppSelectNorthwestArrowCmd")
            throw new HostFailure73("An active native command prevents owned shutdown.");
        var current = Application.TransactionManager.CurrentTransaction;
        if (current != null)
        {
            bool identified = true;
            try { _ = current.HasParentTransaction; }
            catch (COMException error) when (error.ErrorCode == unchecked((int)0x80004005)) { identified = false; }
            if (identified) throw new HostFailure73("An identified native transaction prevents owned shutdown.");
        }
        Evidence["cleanup_idle_classification"] = "select_command_and_null_or_unidentified_E_FAIL_transaction";
        foreach (var owned in Documents.ToArray().Reverse())
        {
            GuardDocuments73();
            if (owned.Document.InternalName != owned.InternalName
                || owned.SavedPath != null && !SamePath73(owned.Document.FullFileName, owned.SavedPath))
                throw new HostFailure73("Owned fixture identity/path changed before close.");
            owned.Document.Close(true); Documents.Remove(owned);
            PersistOwnership73();
        }
        GuardDocuments73();
        if (Application.Documents.Count != 0) throw new HostFailure73("Nonempty owned process prevents Quit.");
        Evidence["owned_final_empty"] = true;
        Application.Quit(); Evidence["owned_quit_requested"] = true;
    }
}
