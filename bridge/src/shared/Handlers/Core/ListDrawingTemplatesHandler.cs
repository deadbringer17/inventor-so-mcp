#if INVENTOR2027
using System;
using System.Linq;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;
using FileInfo = System.IO.FileInfo;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// <c>list_drawing_templates</c> — read-only. Lists the company drawing templates installed in the
/// host-owned template library, with the sheet and usable area each one declares.
///
/// Discovery is a tool of its own because the safe surface never takes a path: without a listing a
/// caller would have to guess file names, and every guess would come back as TEMPLATE_NOT_FOUND.
/// A template whose manifest is missing or malformed is still listed, marked unusable with the
/// reason, so the problem is visible here instead of only at drawing time.
/// </summary>
public sealed class ListDrawingTemplatesHandler : HandlerBase, IInventorCommand
{
    private const int MaxListed = 200;

    public string Name => "list_drawing_templates";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        string root;
        try { root = DrawingTemplatePolicy.Root(); }
        catch (ArgumentException ex)
        {
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "TEMPLATE_LIBRARY_NOT_CONFIGURED: " + ex.Message);
        }

        var files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root).Where(DrawingTemplatePolicy.IsTemplateExtension)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(MaxListed + 1).ToArray()
            : Array.Empty<string>();
        bool truncated = files.Length > MaxListed;

        var templates = new JArray();
        foreach (var file in files.Take(MaxListed))
        {
            var entry = new JObject
            {
                ["template"] = Path.GetFileName(file),
                ["bytes"] = new FileInfo(file).Length
            };
            string manifestPath = DrawingTemplatePolicy.ManifestPathOf(file);
            if (!File.Exists(manifestPath))
            {
                entry["usable"] = false;
                entry["reason"] = "No manifest next to the template; create " + Path.GetFileName(manifestPath) + ".";
            }
            else
            {
                try
                {
                    var manifest = DrawingTemplateManifest.Parse(File.ReadAllText(manifestPath));
                    entry["usable"] = true;
                    entry["sheet"] = manifest.SheetSize + " " + manifest.Orientation;
                    entry["usable_area_mm"] = manifest.ToJson()["usable_area_mm"];
                }
                catch (Exception ex)
                {
                    entry["usable"] = false;
                    entry["reason"] = ex.Message;
                }
            }
            templates.Add(entry);
        }

        return Ok(ctx, new JObject
        {
            ["template_root"] = root,
            ["root_exists"] = Directory.Exists(root),
            ["count"] = templates.Count,
            ["truncated"] = truncated,
            ["templates"] = templates
        });
    }
}
#endif
