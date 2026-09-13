using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

public static class NativePackagePlan
{
    public sealed record Node(string Id, string Path, bool Dirty, bool RequiresUpdate, string[] References);

    // Initial layout preserves the active workspace hierarchy, never flattens names.
    public static JObject Build(string workspace, IEnumerable<Node> documents)
    {
        workspace = System.IO.Path.GetFullPath(workspace);
        var nodes = documents.Take(10001).ToArray();
        if (nodes.Length == 0 || nodes.Length > 10000) throw new ArgumentException("Expected 1-10000 documents.");
        var files = new JArray();
        var blockers = new JArray();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
            if (!string.IsNullOrWhiteSpace(node.Path)) paths.Add(System.IO.Path.GetFullPath(node.Path));
        foreach (var node in nodes)
        {
            if (node.Dirty) Block(node.Id, "UNSAVED_CHANGES");
            if (node.RequiresUpdate) Block(node.Id, "REBUILD_REQUIRED");
            if (string.IsNullOrWhiteSpace(node.Path)) { Block(node.Id, "UNSAVED_DOCUMENT"); continue; }
            string full = System.IO.Path.GetFullPath(node.Path);
            string relative = PathCompat.GetRelativePath(workspace, full);
            try { NativePackageStore.ValidateRelativePath(relative); }
            catch (ArgumentException) { Block(node.Id, "OUTSIDE_WORKSPACE_OR_UNSAFE_PATH"); continue; }
            if (!destinations.Add(relative.Replace('\\', '/'))) Block(node.Id, "DESTINATION_COLLISION");
            foreach (var reference in node.References)
                if (string.IsNullOrWhiteSpace(reference) || !paths.Contains(System.IO.Path.GetFullPath(reference)))
                    Block(node.Id, "REFERENCE_NOT_IN_PACKAGE");
            files.Add(new JObject { ["document_id"] = node.Id, ["source"] = full,
                ["relative_path"] = relative.Replace('\\', '/'), ["references"] = new JArray(node.References) });
        }
        return new JObject { ["files"] = files, ["blockers"] = blockers,
            ["ready_for_copy"] = blockers.Count == 0, ["portable_verified"] = false,
            ["layout"] = "preserve_workspace_hierarchy", ["files_written"] = 0 };
        void Block(string id, string reason) => blockers.Add(new JObject { ["document_id"] = id, ["reason"] = reason });
    }
}
