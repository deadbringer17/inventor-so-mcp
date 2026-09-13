using System;
using System.IO;
using System.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Punch tools are iFeature files executed inside the model, so only Inventor's own punch catalog is
/// accepted: a plain file name, resolved inside a known catalog directory. An arbitrary path would let
/// a caller run any iFeature file on the machine.
/// </summary>
public static class PunchCatalogPolicy
{
    public const string Extension = ".ide";

    /// <summary>Validates the requested punch name, returning it with its extension.</summary>
    public static string ValidateName(string? punch)
    {
        if (string.IsNullOrWhiteSpace(punch)) throw new ArgumentException("punch is required, for example 'obround.ide'.");
        string name = punch!.Trim();
        if (name.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':' }) >= 0)
            throw new ArgumentException("punch must be a catalog file name, without any directory or drive.");
        if (name.Contains("..")) throw new ArgumentException("punch must be a catalog file name.");
        if (!name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) name += Extension;
        if (Path.GetFileName(name) != name || Path.GetFileNameWithoutExtension(name).Length == 0)
            throw new ArgumentException("punch must be a catalog file name, for example 'obround.ide'.");
        return name;
    }

    /// <summary>
    /// Candidate catalog directories, derived from Inventor's own template location so a non-default
    /// installation still resolves. Order is fixed; the first existing directory wins.
    /// </summary>
    public static string[] CandidateRoots(string? templatesPath, string? designDataPath, int inventorYear)
    {
        var roots = new System.Collections.Generic.List<string>();
        void AddFrom(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var directory = new DirectoryInfo(path!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                for (var current = directory; current != null && roots.Count < 12; current = current.Parent)
                {
                    roots.Add(Path.Combine(current.FullName, "Catalog", "Punches"));
                    if (current.Name.IndexOf("Inventor", StringComparison.OrdinalIgnoreCase) >= 0) break;
                }
            }
            catch { /* a malformed location simply contributes no candidate */ }
        }
        AddFrom(templatesPath);
        AddFrom(designDataPath);
        string publicDocuments = Environment.GetEnvironmentVariable("PUBLIC") ?? "";
        if (publicDocuments.Length > 0)
            roots.Add(Path.Combine(publicDocuments, "Documents", "Autodesk", "Inventor " + inventorYear, "Catalog", "Punches"));
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Full path of a punch inside the first catalog directory that holds it.</summary>
    public static string Resolve(string[] roots, string validatedName, Func<string, bool> fileExists)
    {
        foreach (var root in roots)
        {
            string candidate = Path.Combine(root, validatedName);
            if (fileExists(candidate)) return candidate;
        }
        throw new FileNotFoundException("PUNCH_NOT_IN_CATALOG: '" + validatedName +
            "' was not found in Inventor's punch catalog. Searched: " + string.Join("; ", roots), validatedName);
    }
}
