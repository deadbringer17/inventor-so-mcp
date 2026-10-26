using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Inventor.So.RealityCapture;

public sealed record MetricConstraint(string Id, string Type, string[] PointIds, double ValueMm,
    double UncertaintyMm, string Role = "calibration", bool Enabled = true, bool Independent = false, string Source = "manual", string? MeasurementId = null)
{
    public void Validate()
    {
        if (!ValidName(Id) || Type is not ("distance" or "diameter") || PointIds == null ||
            PointIds.Length != (Type == "distance" ? 2 : 3) || PointIds.Any(p => !ValidName(p)) ||
            PointIds.Distinct(StringComparer.Ordinal).Count() != PointIds.Length || !double.IsFinite(ValueMm) || ValueMm <= 0 || ValueMm > 1000000 ||
            !double.IsFinite(UncertaintyMm) || UncertaintyMm < 0.001 || UncertaintyMm > ValueMm ||
            Role is not ("calibration" or "validation") || string.IsNullOrWhiteSpace(Source) || Source.Length > 128 || MeasurementId!=null && !ValidName(MeasurementId))
            throw new CaptureSolveException("CAPTURE_CONSTRAINT_INVALID");
    }
    public static bool ValidName(string? name) => name != null && Regex.IsMatch(name,@"\A[a-zA-Z0-9_.:-]{1,120}\z");
}

public sealed record MeasuredMarker(int MarkerId, double[][] CornersMm, double UncertaintyMm);
public sealed record CalibrationArtifact(string Id, string GeometryVersion, string MeasurementMethod, DateTimeOffset MeasuredAt,
    MeasuredMarker[] Markers, string Role = "calibration", bool Independent = false)
{
    public void Validate()
    {
        if (!MetricConstraint.ValidName(Id) || Id.Length>64 || !MetricConstraint.ValidName(GeometryVersion) || string.IsNullOrWhiteSpace(MeasurementMethod) ||
            MeasurementMethod.Length > 128 || MeasuredAt == default || Role is not ("calibration" or "validation") || Markers == null || Markers.Length is < 1 or > 24)
            throw new CaptureSolveException("CAPTURE_ARTIFACT_INVALID");
        foreach (var marker in Markers)
        {
            if (marker == null || marker.MarkerId is < 0 or >= 50 || !double.IsFinite(marker.UncertaintyMm) || marker.UncertaintyMm < 0.001 ||
                marker.CornersMm == null || marker.CornersMm.Length != 4 || marker.CornersMm.Any(p => p == null || p.Length != 3 || p.Any(v => !double.IsFinite(v) || Math.Abs(v) > 100000)))
                throw new CaptureSolveException("CAPTURE_ARTIFACT_INVALID");
            if ((V3.From(marker.CornersMm[0]) - V3.From(marker.CornersMm[1])).Norm < 1)
                throw new CaptureSolveException("CAPTURE_ARTIFACT_INVALID");
        }
        if (Markers.Select(m => m.MarkerId).Distinct().Count() != Markers.Length) throw new CaptureSolveException("CAPTURE_ARTIFACT_INVALID");
    }
}

public sealed class CaptureSolveException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record CaptureSolveInput(string ScanId, int Revision, string InputDirectory, string OutputDirectory,
    MetricConstraint[] Constraints, CalibrationArtifact[] Artifacts, string EvidenceKind = "unverified");
public sealed record CaptureSolveSummary(string ScanId, int Revision, string State, string? ErrorCode,
    DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, int ConstraintVersion);

public static class CaptureJson
{
    public static readonly JsonSerializerSettings Settings = new() { MaxDepth = 32, TypeNameHandling = TypeNameHandling.None };
    public static T Read<T>(string path, long maxBytes = 8*1024*1024)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        if (stream.Length > maxBytes) throw new CaptureSolveException("CAPTURE_METADATA_TOO_LARGE");
        using var reader=new StreamReader(stream);
        return JsonConvert.DeserializeObject<T>(reader.ReadToEnd(),Settings) ?? throw new CaptureSolveException("CAPTURE_METADATA_INVALID");
    }
    public static void WriteNew(string path, object value)
    {
        using var stream = new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(JsonConvert.SerializeObject(value,Formatting.Indented,Settings)); writer.Flush(); stream.Flush(true);
    }
}

public readonly record struct V3(double X, double Y, double Z)
{
    public static V3 From(double[] v) => new(v[0],v[1],v[2]);
    public double[] Array => new[] { X,Y,Z };
    public double Norm => Math.Sqrt(Dot(this,this));
    public V3 Unit => Norm > 1e-12 ? this / Norm : throw new CaptureSolveException("CAPTURE_DEGENERATE_GEOMETRY");
    public static V3 operator +(V3 a,V3 b) => new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static V3 operator -(V3 a,V3 b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static V3 operator -(V3 a) => a * -1;
    public static V3 operator *(V3 a,double s) => new(a.X*s,a.Y*s,a.Z*s);
    public static V3 operator /(V3 a,double s) => a * (1/s);
    public static double Dot(V3 a,V3 b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    public static V3 Cross(V3 a,V3 b) => new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
}
