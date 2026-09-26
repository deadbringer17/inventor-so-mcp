using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Release package (plan §16.3): composes the verified <c>save_artifact</c> exports and the BOM
/// query into one new host-owned folder with metadata, validation results and SHA-256 checksums.
/// Only the artifacts that apply to the document are produced; every exclusion is explained.
/// </summary>
[McpServerToolType]
public sealed class ReleaseTools
{
    private readonly PluginClient _client;
    private readonly InventorMcpConfig _config;
    private readonly ICallerIdentity _caller;

    public ReleaseTools(PluginClient client, InventorMcpConfig config, ICallerIdentity caller)
    {
        _client = client; _config = config; _caller = caller;
    }

    [McpServerTool(Name = "inventor_build_release_package"), Description("Build a release package of the active document in a NEW host-owned folder (never overwrites): model.step (part/assembly), flat.dxf (sheet-metal part with an existing flat pattern), drawing.pdf (drawing), bom.csv plus BOM validation (assembly), metadata.json (document, revision, included and excluded artifacts with the reason), validation.json and checksums.sha256. name is the folder label (1-60 letters, digits, space, underscore, hyphen). include optionally narrows to step, dxf, pdf, bom. Requires document_id/expected_revision; the sources are never saved in place. Filesystem output is not a CAD transaction: a failed export is recorded and the package is marked incomplete. Experimental tier.")]
    public async Task<string> BuildReleasePackage(string document_id, string expected_revision, string name = "release",
        string[]? include = null, CancellationToken ct = default)
    {
        if (!ValidName(name)) return XrTools.Error(InventorErrorCodes.INVALID_ARGUMENT, "name must be 1-60 letters, digits, space, underscore or hyphen.");
        var wanted = new HashSet<string>(include ?? new[] { "step", "dxf", "pdf", "bom" }, StringComparer.OrdinalIgnoreCase);
        if (wanted.Any(w => w is not ("step" or "dxf" or "pdf" or "bom")))
            return XrTools.Error(InventorErrorCodes.INVALID_ARGUMENT, "include accepts step, dxf, pdf and bom.");

        JObject info;
        try { info = (JObject)await _client.SendAsync("get_document_info", new JObject(), ct); }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
        if ((string?)info["id"] != document_id)
            return XrTools.Error(InventorErrorCodes.DOCUMENT_CHANGED, "The active document is not " + document_id + ".");
        if ((string?)info["revision"] != expected_revision)
            return XrTools.Error(InventorErrorCodes.STALE_REVISION, "The document moved to revision " + info["revision"] + "; read it again.");
        string kind = KindOf((string?)info["document_type"]);

        string root = Path.GetFullPath(_config.ReleaseDirectory);
        string folder = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + name.Replace(' ', '_'));
        Directory.CreateDirectory(folder);

        var included = new JArray();
        var excluded = new JArray();
        var validation = new JObject();
        string revision = expected_revision;
        bool complete = true;

        async Task Export(string key, string format, string fileName, bool applies, string notApplicable)
        {
            if (!wanted.Contains(key)) { excluded.Add(new JObject { ["artifact"] = fileName, ["reason"] = "not requested" }); return; }
            if (!applies) { excluded.Add(new JObject { ["artifact"] = fileName, ["reason"] = notApplicable }); return; }
            try
            {
                var saved = await _client.SendAsync("save_artifact", new JObject
                {
                    ["document_id"] = document_id, ["expected_revision"] = revision, ["format"] = format, ["name"] = Path.GetFileNameWithoutExtension(fileName),
                }, ct);
                // Exporting a copy can itself advance the journal (a save event); chain the token the
                // add-in reports for the state it just exported, never a guessed one.
                revision = (string?)saved["revision"] ?? revision;
                string source = CheckArtifactPath((string?)saved["path"]);
                File.Copy(source, Path.Combine(folder, fileName), overwrite: false);
                included.Add(new JObject { ["artifact"] = fileName, ["format"] = format, ["source_artifact"] = source });
            }
            catch (InventorGatewayException ex)
            {
                complete = false;
                excluded.Add(new JObject { ["artifact"] = fileName, ["reason"] = "export failed", ["error"] = ex.ToErrorJson()["error"] });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                complete = false;
                excluded.Add(new JObject { ["artifact"] = fileName, ["reason"] = "copy failed: " + ex.Message });
            }
        }

        await Export("step", "step", "model.step", kind is "part" or "assembly", "STEP applies to parts and assemblies");
        await Export("dxf", "dxf", "flat.dxf", kind == "part", "flat-pattern DXF applies to sheet-metal parts");
        await Export("pdf", "pdf", "drawing.pdf", kind == "drawing", "PDF applies to drawings (open and activate the drawing to release it)");
        // A part that is not sheet metal refuses the DXF export: that is an exclusion, not a failure.
        foreach (var entry in excluded.OfType<JObject>().Where(e => (string?)e["artifact"] == "flat.dxf" && (string?)e["reason"] == "export failed"))
            entry["reason"] = "not a sheet-metal part with a flat pattern";
        complete = !excluded.OfType<JObject>().Any(e => (string?)e["reason"] is "export failed" || ((string?)e["reason"])?.StartsWith("copy failed") == true);

        if (wanted.Contains("bom") && kind == "assembly")
        {
            try
            {
                var bom = await _client.SendAsync("get_assembly_bom", new JObject { ["max_rows"] = 20000 }, ct);
                var rows = BomAnalysis.ReadRows(bom);
                File.WriteAllText(Path.Combine(folder, "bom.csv"), BomAnalysis.ToCsv(rows), new UTF8Encoding(true));
                included.Add(new JObject { ["artifact"] = "bom.csv", ["rows"] = rows.Count });
                validation["bom"] = BomAnalysis.Validate(rows, (bool?)bom["truncated"] ?? false);
            }
            catch (InventorGatewayException ex)
            {
                complete = false;
                excluded.Add(new JObject { ["artifact"] = "bom.csv", ["reason"] = "export failed", ["error"] = ex.ToErrorJson()["error"] });
            }
        }
        else excluded.Add(new JObject { ["artifact"] = "bom.csv", ["reason"] = wanted.Contains("bom") ? "BOM applies to assemblies" : "not requested" });

        validation["source"] = new JObject { ["document_id"] = document_id, ["revision_at_start"] = expected_revision, ["dirty"] = info["dirty"]?.DeepClone() };
        validation["complete"] = complete;
        validation["manufacturing_ready"] = false;
        validation["note"] = "A release package is an export bundle, not an engineering approval: tolerances and drafting sign-off stay with the user.";

        var metadata = new JObject
        {
            ["package"] = Path.GetFileName(folder),
            ["created_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["created_by"] = _caller.Client,
            ["document"] = new JObject
            {
                ["document_id"] = document_id, ["title"] = info["title"]?.DeepClone(), ["kind"] = kind,
                ["path"] = info["path"]?.DeepClone(), ["revision"] = expected_revision, ["database_revision"] = info["database_revision"]?.DeepClone(),
            },
            ["included"] = included,
            ["excluded"] = excluded,
        };
        File.WriteAllText(Path.Combine(folder, "metadata.json"), metadata.ToString(Formatting.Indented));
        File.WriteAllText(Path.Combine(folder, "validation.json"), validation.ToString(Formatting.Indented));
        File.WriteAllText(Path.Combine(folder, "checksums.sha256"), Checksums(folder));

        return new JObject
        {
            ["folder"] = folder,
            ["complete"] = complete,
            ["files"] = new JArray(Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal)),
            ["metadata"] = metadata,
            ["validation"] = validation,
        }.ToString(Formatting.None);
    }

    /// <summary><c>sha256sum</c>-compatible listing of every file except the listing itself.</summary>
    internal static string Checksums(string folder)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.GetFiles(folder).Where(f => Path.GetFileName(f) != "checksums.sha256").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            using var stream = File.OpenRead(file);
            builder.Append(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()).Append("  ").Append(Path.GetFileName(file)).Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>An exported file must sit under the add-in's own artifact root; anything else is refused.</summary>
    internal static string CheckArtifactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("The export reported no path.");
        string artifacts = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "artifacts"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The export path is outside the host artifact folder.");
        return full;
    }

    internal static string KindOf(string? documentType) => documentType switch
    {
        "kPartDocumentObject" => "part",
        "kAssemblyDocumentObject" => "assembly",
        "kDrawingDocumentObject" => "drawing",
        _ => "other",
    };

    internal static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 60 && name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-');
}
