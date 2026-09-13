using System;
using System.Linq;
namespace Bimwright.Ipt.Shared.Infrastructure;

public static class ArtifactFormatPolicy
{
    /// <summary>
    /// AutoCAD versions Inventor's flat-pattern translator accepts. Pinned to an allowlist because the
    /// version string is passed straight into the translator options.
    /// </summary>
    public static readonly string[] DxfVersions = { "2000", "2004", "2007", "2010", "2013", "2018" };

    public const string DefaultDxfVersion = "2018";

    /// <summary>
    /// Layer option keys Inventor's flat-pattern translator understands. Allowlisted because the whole
    /// option string is handed to the translator: an unchecked key or value would be passed straight in.
    /// </summary>
    public static readonly string[] DxfLayerOptions =
    {
        "OuterProfileLayer", "InteriorProfilesLayer", "BendUpLayer", "BendDownLayer",
        "ToolCenterLayer", "FeatureProfilesUpLayer", "FeatureProfilesDownLayer",
        "AltRepFrontLayer", "AltRepBackLayer", "UnconsumedSketchesLayer", "TangentLayer",
    };

    /// <summary>Translator option string for a flat-pattern DXF, refusing any unlisted version.</summary>
    public static string FlatPatternDxfOptions(string? version)
        => FlatPatternDxfOptions(version, null);

    /// <summary>
    /// Translator option string with optional layer names. A layer name is restricted to plain text:
    /// the separators of the option string itself would otherwise let a caller inject further options.
    /// </summary>
    public static string FlatPatternDxfOptions(string? version, System.Collections.Generic.IReadOnlyDictionary<string, string>? layers)
    {
        string requested = string.IsNullOrWhiteSpace(version) ? DefaultDxfVersion : version!.Trim();
        if (Array.IndexOf(DxfVersions, requested) < 0)
            throw new ArgumentException("dxf_version must be one of " + string.Join(", ", DxfVersions) + ".");
        var options = new System.Text.StringBuilder("FLAT PATTERN DXF?AcadVersion=").Append(requested);
        if (layers != null)
            foreach (var layer in layers)
            {
                if (Array.IndexOf(DxfLayerOptions, layer.Key) < 0)
                    throw new ArgumentException("dxf_layers accepts only " + string.Join(", ", DxfLayerOptions) + ".");
                string name = (layer.Value ?? "").Trim();
                if (name.Length == 0 || name.Length > 64)
                    throw new ArgumentException("Layer name for " + layer.Key + " must be 1 to 64 characters.");
                if (name.IndexOfAny(new[] { '&', '=', '?', ';', '"', '\'', '<', '>', '*', '|' }) >= 0 ||
                    name.Any(char.IsControl))
                    throw new ArgumentException("Layer name for " + layer.Key + " must not contain option separators or control characters.");
                options.Append('&').Append(layer.Key).Append('=').Append(name);
            }
        return options.ToString();
    }

    public static string Extension(string documentKind, string format, bool inventorDwg = false) => (documentKind, format) switch
    {
        ("part", "native") => ".ipt",
        ("part", "step") => ".step",
        ("part", "dxf") => ".dxf",          // sheet-metal flat pattern only; the handler enforces that
        ("assembly", "step") => ".step",
        ("drawing", "native") => inventorDwg ? ".dwg" : ".idw",
        ("drawing", "pdf") => ".pdf",
        ("assembly", "native") => throw new ArgumentException("Assembly output supports STEP only; native dependency packaging is not implemented."),
        _ => throw new ArgumentException("Unsupported document/format pair. Part: native/step, dxf for a sheet-metal flat pattern; assembly: step; drawing: native/pdf.")
    };
}
