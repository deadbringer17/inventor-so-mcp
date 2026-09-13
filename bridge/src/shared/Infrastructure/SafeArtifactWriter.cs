using System;
using System.IO;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>Unique outputs only. Never overwrites or removes an existing file.</summary>
public static class SafeArtifactWriter
{
    public static string Write(string root, string extension, Action<string> write, Func<bool>? expired = null,
        string? name = null)
    {
        if (extension != ".ipt" && extension != ".step" && extension != ".pdf" && extension != ".idw" &&
            extension != ".dwg" && extension != ".dxf") throw new ArgumentException("Unsupported artifact format.");
        root = Path.GetFullPath(root);
        CheckAncestors(root);
        if (expired?.Invoke() == true) throw new TimeoutException("Expired before artifact creation.");
        Directory.CreateDirectory(root);
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Artifact directory collision.");
        Directory.CreateDirectory(directory);
        CheckAncestors(directory);
        // The file keeps its own directory, so a caller-chosen name can never overwrite an earlier
        // artifact; it only saves the caller renaming ten exports that were all called "model".
        string stem = name == null ? "model" : WorkspaceDocumentPolicy.ValidateName(name);
        string pending = Path.Combine(directory, "pending" + extension);
        string final = Path.Combine(directory, stem + extension);
        try
        {
            write(pending);
            CheckAncestors(pending);
            if (!File.Exists(pending) || new FileInfo(pending).Length == 0) throw new IOException("Exporter produced no data.");
            if (expired?.Invoke() == true) throw new TimeoutException("Expired after export; artifact not published.");
            File.Move(pending, final); // no-overwrite overload
            return final;
        }
        catch (Exception ex)
        {
            throw new IOException("Artifact not published; partial output may remain in " + directory + ". " + ex.Message, ex);
        }
    }

    internal static void CheckAncestors(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reparse points are not allowed in the artifact path.");
    }
}
