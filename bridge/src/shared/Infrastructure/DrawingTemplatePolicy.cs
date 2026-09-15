using System;
using System.IO;
using System.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Host-owned library of company drawing templates. <c>create_drawing_safe</c> may start a drawing
/// from a template ONLY inside this directory, named by file name: the safe surface never accepts a
/// path from a caller, so a model asked to "use the company title block" cannot be steered into
/// opening an arbitrary file on the machine.
///
/// The library is read-only for this add-in. Inventor copies a template into a new in-memory
/// document, so nothing here is ever written, renamed or deleted.
///
/// Pure path/name policy: no CAD, no filesystem mutation, so it is unit-testable without Inventor.
/// </summary>
public static class DrawingTemplatePolicy
{
    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Add-in process variable that moves the template library.</summary>
    public const string RootVariable = "INVENTOR_SO_TEMPLATES";

    /// <summary>Drawing template extensions the library recognises.</summary>
    public static readonly string[] TemplateExtensions = { ".idw", ".dwg" };

    /// <summary>Configured template library root.</summary>
    public static string Root() => Resolve(Environment.GetEnvironmentVariable(RootVariable));

    /// <summary>Default host-owned library, used whenever <see cref="RootVariable"/> is unset.</summary>
    public static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "templates");

    /// <summary>
    /// Resolves a configured root. An unusable value is an error, never a silent fallback to the
    /// default: a caller that believes it draws on the company title block must not be handed the
    /// stock Inventor one instead. Mirrors <see cref="WorkspaceDocumentPolicy.Resolve"/>.
    /// </summary>
    public static string Resolve(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return DefaultRoot();
        string raw = configured!.Trim();
        // Checked before GetFullPath, which would silently resolve a relative or drive-relative
        // value against the Inventor process working directory.
        if (!IsFullyQualified(raw))
            throw new ArgumentException(RootVariable + " must be an absolute path such as C:\\Standards\\templates.");
        string full;
        try { full = Path.GetFullPath(raw); }
        catch (Exception ex) { throw new ArgumentException(RootVariable + " is not a usable path: " + ex.Message); }
        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = (Path.GetPathRoot(full) ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (full.Length == 0 || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(RootVariable + " must be a directory below a drive or share root, not the root itself.");
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.System })
        {
            string protectedPath = Environment.GetFolderPath(folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (protectedPath.Length == 0) continue;
            if (string.Equals(full, protectedPath, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(protectedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(RootVariable + " must not point inside a Windows or System directory.");
        }
        return full;
    }

    /// <summary>Drive-qualified (<c>C:\dir</c>) or UNC (<c>\\server\share</c>) only. Written without
    /// <c>Path.IsPathFullyQualified</c>, which the net48 add-ins do not have.</summary>
    private static bool IsFullyQualified(string path)
    {
        bool Separator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        if (path.Length >= 2 && Separator(path[0]) && Separator(path[1])) return true; // UNC
        return path.Length >= 3 && path[1] == ':' && Separator(path[2]) && char.IsLetter(path[0]);
    }

    public static bool IsTemplateExtension(string? path)
        => TemplateExtensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

    /// <summary>
    /// Validates a caller-supplied template file name. Returns it unchanged when acceptable.
    /// A name is a LEAF file name with a template extension: no directory separators, no drive
    /// qualifier, no <c>..</c>, so no value a caller can send reaches outside the library.
    /// </summary>
    public static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name!.Length > 120)
            throw new ArgumentException("template must be 1 to 120 characters.");
        string value = name!;
        if (value != value.Trim())
            throw new ArgumentException("template must not start or end with whitespace.");
        if (value.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
            throw new ArgumentException("template must be a file name inside the template library, not a path.");
        if (value == "." || value == ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("template contains characters that are not valid in a file name.");
        if (!IsTemplateExtension(value))
            throw new ArgumentException("template must end in .idw or .dwg.");
        string stem = Path.GetFileNameWithoutExtension(value);
        if (stem.Length == 0) throw new ArgumentException("template must have a file name before its extension.");
        if (ReservedNames.Contains(stem.ToUpperInvariant()))
            throw new ArgumentException("template collides with a reserved Windows device name.");
        return value;
    }

    /// <summary>
    /// Full path of a template inside <paramref name="root"/>. The resolved path is re-checked
    /// against the root, so even a name that survives <see cref="ValidateName"/> on some future
    /// filesystem cannot address a file outside the library.
    /// </summary>
    public static string PathOf(string root, string? name)
    {
        string path = Path.GetFullPath(Path.Combine(root, ValidateName(name)));
        if (!WorkspaceDocumentPolicy.IsInside(root, path))
            throw new ArgumentException("Resolved template path escapes the template library.");
        return path;
    }

    /// <summary>Path of the manifest that must sit next to a template, e.g. <c>Company_A3.json</c>.</summary>
    public static string ManifestPathOf(string templatePath)
        => Path.Combine(Path.GetDirectoryName(templatePath) ?? "",
                        Path.GetFileNameWithoutExtension(templatePath) + ".json");
}
