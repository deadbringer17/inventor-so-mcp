using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inventor;
using File = System.IO.File;
using Path = System.IO.Path;

// A research executable, never loaded by the product add-in. It does not use the ROT,
// execute a desktop command, edit a user document or write a style library.
internal static partial class Program
{
    private class ResearchFailure(string message) : Exception(message);
    private sealed class AssociationFailure(string message) : ResearchFailure(message);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<int> ForbiddenPids = new() { 91056 };
    private static Inventor.Application? App;
    private static int OwnedPid;
    private static DateTime OwnedStart;
    private static readonly List<Document> OwnedDocuments = new();
    private static JsonObject Report = new();
    private static string Phase = "arguments";
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("interop" or "native" or "persistence" or "glyphframes" or "texttransforms64"))
        { Console.Error.WriteLine("Usage: S1TextProbe interop|native <new-report.json>"); return 64; }
        string reportPath = Path.GetFullPath(args[1]);
        if (File.Exists(reportPath)) throw new ResearchFailure("A research report must not overwrite earlier evidence.");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        Report = new JsonObject
        {
            ["captured_utc"] = DateTime.UtcNow.ToString("O"), ["mode"] = args[0],
            ["evidence"] = "Installed public interop / dedicated owned COM fixture research only; no product, desktop parity, HTTP, Quest or physical proof.",
            ["interop"] = Interop(), ["open_s1_gates"] = 18,
            ["geometry_text"] = new JsonObject { ["status"] = "NOT COVERED", ["reason"] = "No GeometryText/TextOnPath creation API found in scanned installed Sketch/Text interfaces. Ordinary TextBox is distinct; no glyph placement workaround is used." },
            ["desktop_defaults"] = "NOT COVERED: native API/template observations are not a desktop dialog oracle.",
            ["rendering"] = "NOT COVERED: native facet observations do not certify XR or physical glyph orientation.",
            ["quest"] = "NOT COVERED", ["physical"] = "NOT COVERED"
        };
        if (args[0] == "interop") { WriteReport(reportPath); return 0; }
        int exit = 1;
        var existing = Processes();
        Report["existing_processes_before"] = JsonSerializer.SerializeToNode(existing);
        try
        {
            Phase = "create_owned_application";
            App = (Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application", true)!)!;
            GetWindowThreadProcessId(new IntPtr(App.MainFrameHWND), out uint pid);
            if (pid == 0 || ForbiddenPids.Contains((int)pid) || existing.Any(p => p.Pid == pid))
                throw new ResearchFailure("COM did not return a new distinct owned Inventor process. No mutation or Quit permitted.");
            OwnedPid = (int)pid; OwnedStart = Process.GetProcessById(OwnedPid).StartTime.ToUniversalTime();
            Report["owned_pid"] = OwnedPid; Report["owned_start_utc"] = OwnedStart.ToString("O");
            GuardApplication();
            if (App.Documents.Count != 0 || App.ActiveDocument != null)
                throw new ResearchFailure("Owned process must start without any documents before any mutation.");
            Report["owned_initial_empty"] = true;
            var contexts = new JsonArray(); Report["contexts"] = contexts;
            foreach (bool sheet in new[] { false, true })
            {
                if(args[0]=="texttransforms64") RunTextTransforms64(sheet,contexts);
                else if(args[0]=="glyphframes") RunGlyphFrames61(sheet,contexts);
                else if(args[0]=="persistence")
                {var context=new JsonObject{["kind"]=sheet ? "sheetmetal" : "part"};contexts.Add(context);RunPersistence(sheet,context,Path.GetDirectoryName(reportPath)!);}
                else RunContext(sheet, contexts, Path.GetDirectoryName(reportPath)!);
            }
            GuardApplication();
            if (App.Documents.Count != 0) throw new ResearchFailure("Owned documents remain at end of probe.");
            Report["owned_final_empty"] = true;
            int failed = contexts.OfType<JsonObject>().Sum(c => (int?)c["failed"] ?? 0)
                + contexts.OfType<JsonObject>().Count(c => (string?)c["save_copy"]?["status"] == "FAIL");
            Report["failed_cases"] = failed;
            Report["outcome"] = failed == 0
                ? "PASS collection; inspect each case check, refusal, NOT_ASSOCIATIVE and NOT COVERED. Not full text parity."
                : "PARTIAL collection with native case failures; inspect individual results. Not full text parity.";
            exit = failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Report["outcome"] = "FAIL";
            Report["failure"] = Error(ex);
        }
        finally
        {
            var cleanup = new JsonArray(); Report["cleanup"] = cleanup;
            if (OwnedPid != 0 && App != null)
            {
                foreach (var doc in OwnedDocuments.ToArray().Reverse())
                {
                    try { GuardApplication(); doc.Close(true); OwnedDocuments.Remove(doc); cleanup.Add("Owned document closed without saving."); }
                    catch (Exception ex) { cleanup.Add(Error(ex)); exit = 1; }
                }
                try { GuardApplication(); if (App.Documents.Count != 0) throw new ResearchFailure("Unowned document prevents Quit."); App.Quit(); Report["owned_quit_requested"] = true; }
                catch (Exception ex) { Report["quit_failure"] = Error(ex); exit = 1; }
            }
            if (App != null && Marshal.IsComObject(App)) Marshal.ReleaseComObject(App);
            App = null;
            // No process kill. A slow native shutdown is recorded, not replaced with a new run.
            if (OwnedPid != 0)
            {
                bool ended;
                try { using var process = Process.GetProcessById(OwnedPid); ended = process.WaitForExit(15000); }
                catch (ArgumentException) { ended = true; }
                Report["owned_process_exited"] = ended;
                if (!ended) exit = 1;
            }
            var after = Processes();
            Report["existing_processes_after"] = JsonSerializer.SerializeToNode(after);
            Report["preexisting_processes_preserved"] = existing.All(p => after.Any(a => a.Pid == p.Pid && a.StartUtc == p.StartUtc));
            if ((bool?)Report["preexisting_processes_preserved"] != true) exit = 1;
            Report["exit_code"] = exit;
            WriteReport(reportPath);
        }
        return exit;
    }

    private sealed record ProcessIdentity(int Pid, DateTime StartUtc);
    private static ProcessIdentity[] Processes() => Process.GetProcessesByName("Inventor").Select(p =>
    { using (p) return new ProcessIdentity(p.Id, p.StartTime.ToUniversalTime()); }).OrderBy(p => p.Pid).ToArray();
    private static void GuardApplication()
    {
        if (App == null || OwnedPid == 0 || ForbiddenPids.Contains(OwnedPid)) throw new ResearchFailure("No owned application.");
        GetWindowThreadProcessId(new IntPtr(App.MainFrameHWND), out uint current);
        using var process = Process.GetProcessById(OwnedPid);
        if (current != OwnedPid || process.StartTime.ToUniversalTime() != OwnedStart)
            throw new ResearchFailure("Owned process identity changed.");
        if (App.Documents.Count != OwnedDocuments.Count) throw new ResearchFailure("Unexpected document collection; refusing mutation.");
        foreach (Document doc in App.Documents)
            if (!OwnedDocuments.Any(owned => SameCom(owned, doc))) throw new ResearchFailure("Unowned document in test process.");
    }
    private static bool SameCom(object a, object b)
    {
        IntPtr x = Marshal.GetIUnknownForObject(a), y = Marshal.GetIUnknownForObject(b);
        try { return x == y; } finally { Marshal.Release(x); Marshal.Release(y); }
    }
    private static void WriteReport(string path) => File.WriteAllText(path, Report.ToJsonString(JsonOptions));
    private static JsonObject Error(Exception ex)
    {
        var result = new JsonObject { ["phase"] = Phase, ["type"] = ex.GetType().FullName, ["hresult"] = ex.HResult };
        // Only our static research assertions may contribute prose. Never serialize
        // COM/framework exception messages, which can contain document paths or data.
        if (ex is ResearchFailure) result["assertion"] = ex.Message;
        return result;
    }
    private static void Require(bool test, string message) { if (!test) throw new ResearchFailure(message); }
    private static void RequireAssociation(bool test, string message) { if (!test) throw new AssociationFailure(message); }

    private static JsonObject Interop()
    {
        var assembly = typeof(PlanarSketch).Assembly;
        var relevant = new[] { typeof(PlanarSketch), typeof(Sketch), typeof(TextBoxes), typeof(TextBox), typeof(TextStyle), typeof(TextStylesEnumerator), typeof(TextBoxDefinitionHandler) };
        var members = new JsonArray();
        foreach (var type in relevant)
            foreach (var member in type.GetMembers().OrderBy(m => m.Name))
                members.Add(new JsonObject { ["type"] = type.FullName, ["member"] = member.ToString() });
        var paths = assembly.GetTypes().Where(t => t.IsInterface && (t.Name.Contains("Sketch") || t.Name.Contains("Text")))
            .SelectMany(t => t.GetMethods().Where(m => m.Name.Contains("Text") || m.Name.Contains("Path"))
                .Select(m => t.FullName + " " + m)).ToArray();
        return new JsonObject
        {
            ["assembly_version"] = assembly.GetName().Version!.ToString(),
            ["assembly_path"] = assembly.Location,
            ["assembly_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
            ["text_members"] = members, ["sketch_text_path_member_scan"] = JsonSerializer.SerializeToNode(paths)
        };
    }
}
