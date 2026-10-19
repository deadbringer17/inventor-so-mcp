using Inventor;
using Newtonsoft.Json.Linq;
using File=System.IO.File;
using Path=System.IO.Path;

internal static partial class Program
{
    // Separate read-only post-run evidence, also when the previous active
    // document was null. Never starts, activates, closes or updates anything.
    private static int ReadNativeState(string path)
    {
        var report=new JObject {["captured_utc"]=DateTime.UtcNow.ToString("O"),["evidence"]="Read-only Inventor state; no fixture or document mutation"};
        try
        {
            var clsid=Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            GetActiveObject(ref clsid,IntPtr.Zero,out var active);var app=(global::Inventor.Application)active;
            report["version"]=app.SoftwareVersion.DisplayVersion;
            report["active_document"]=State(app.ActiveDocument);
            report["open_document_count"]=app.Documents.Count;
            var fixtures=new JArray();
            foreach(Document document in app.Documents)
                if(document.DisplayName.StartsWith("S1_Probe_",StringComparison.Ordinal)
                    || document.DisplayName is "S1_ArcInput_Oracle.ipt" or "S1_ArcSlotInput_Oracle.ipt"
                        or "S1_PolygonInput_Oracle.ipt" or "S1_SlotInput_Oracle.ipt" or "S1_EllipseInput_Oracle.ipt")
                    fixtures.Add(State(document));
            report["remaining_native_fixtures"]=fixtures;
            report["outcome"]=fixtures.Count==0 ? "PASS read-only state, no native fixtures remain" : "FAIL native fixture remains open";
        }
        catch(Exception ex){report["outcome"]="FAIL";report["error"]=ex.ToString();}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,report.ToString());
        Console.WriteLine(report["outcome"]);return ((string?)report["outcome"])?.StartsWith("PASS",StringComparison.Ordinal)==true ? 0 : 1;
    }
}
