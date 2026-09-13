using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>Stages an exact dependency set. CAD reference rewriting and validation are required callbacks,
/// not inferred from successful file copying. Never writes to source files.</summary>
public static class NativePackageStore
{
    public sealed record Entry(string Source, string RelativePath);

    public static string Create(string root, IEnumerable<Entry> dependencies,
        Action<string> writeAndValidateCad, Func<bool>? expired = null)
    {
        if (writeAndValidateCad == null) throw new ArgumentNullException(nameof(writeAndValidateCad));
        var entries = dependencies.Take(10001).ToArray();
        if (entries.Length > 10000) throw new ArgumentException("Package exceeds 10000 dependencies.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            ValidateRelativePath(entry.RelativePath);
            if (!names.Add(entry.RelativePath.Replace('\\', '/')))
                throw new ArgumentException("Duplicate package destination.");
            SafeArtifactWriter.CheckAncestors(Path.GetFullPath(entry.Source));
            if (!File.Exists(entry.Source)) throw new FileNotFoundException("Missing package dependency.", entry.Source);
        }
        root = Path.GetFullPath(root);
        SafeArtifactWriter.CheckAncestors(root);
        CheckDeadline();
        Directory.CreateDirectory(root);
        string key = Guid.NewGuid().ToString("N");
        string staging = Path.Combine(root, key + ".pending");
        string final = Path.Combine(root, key);
        if (Directory.Exists(staging) || Directory.Exists(final) || File.Exists(staging) || File.Exists(final))
            throw new IOException("Package directory collision.");
        Directory.CreateDirectory(staging);
        try
        {
            var originals = new List<(string Path, string Hash)>();
            foreach (var entry in entries)
            {
                CheckDeadline();
                string source = Path.GetFullPath(entry.Source);
                SafeArtifactWriter.CheckAncestors(source);
                string before = Hash(source);
                string target = Path.Combine(staging, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                SafeArtifactWriter.CheckAncestors(target);
                File.Copy(source, target, overwrite: false);
                if (Hash(target) != before || Hash(source) != before)
                    throw new IOException("SOURCE_CHANGED_DURING_PACKAGE_COPY");
                originals.Add((source, before));
            }
            CheckDeadline();
            writeAndValidateCad(staging);
            CheckDeadline();
            foreach (var source in originals)
            {
                SafeArtifactWriter.CheckAncestors(source.Path);
                if (Hash(source.Path) != source.Hash) throw new IOException("SOURCE_CHANGED_DURING_PACKAGE_VALIDATION");
            }
            var files = new JArray();
            InspectDirectory(staging);
            using (var writer = new StreamWriter(new FileStream(Path.Combine(staging, "package.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None)))
                writer.Write(new JObject { ["schema"] = 1, ["files"] = files }.ToString());
            CheckDeadline();
            Directory.Move(staging, final); // same-parent publication; refuses existing destination
            return final;

            void InspectDirectory(string directory)
            {
                SafeArtifactWriter.CheckAncestors(directory);
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    CheckDeadline();
                    SafeArtifactWriter.CheckAncestors(file);
                    if (files.Count >= 20000) throw new IOException("Package output exceeds 20000 files.");
                    files.Add(new JObject { ["path"] = PathCompat.GetRelativePath(staging, file).Replace('\\', '/'),
                        ["bytes"] = new FileInfo(file).Length, ["sha256"] = Hash(file) });
                }
                foreach (var child in Directory.EnumerateDirectories(directory)) InspectDirectory(child);
            }
        }
        catch (Exception ex)
        {
            throw new IOException("Package not published; partial output retained in " + staging + ". " + ex.Message, ex);
        }
        void CheckDeadline()
        {
            if (expired?.Invoke() == true) throw new TimeoutException("Package deadline exceeded.");
        }
    }

    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':'))
            throw new ArgumentException("Relative package path required.");
        var segments = path.Replace('\\', '/').Split('/');
        foreach (var segment in segments)
        {
            string stem = segment.Split('.')[0];
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal) ||
                segment.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Unsafe package path segment.");
        }
        if (path.Equals("package.json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Reserved manifest path.");
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = SHA256.Create();
        return PathCompat.ToHex(hash.ComputeHash(stream));
    }
}
