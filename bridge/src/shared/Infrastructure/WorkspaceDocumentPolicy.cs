using System;
using System.IO;
using System.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Host-owned CAD workspace. The safe surface may create, save in place and close documents ONLY
/// inside this directory; every file the user opened from anywhere else stays read-only for it and
/// keeps going through <c>save_artifact</c> copies. Pure path/name policy: no CAD, no filesystem
/// mutation, so it is unit-testable without Inventor.
/// </summary>
public static class WorkspaceDocumentPolicy
{
    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Add-in process variable that moves the workspace, e.g. inside an Inventor project.</summary>
    public const string RootVariable = "INVENTOR_SO_WORKSPACE";

    /// <summary>Managed workspace root. Documents outside it are never saved or closed by this add-in.</summary>
    public static string Root() => Resolve(Environment.GetEnvironmentVariable(RootVariable));

    /// <summary>Default host-owned root, used whenever <see cref="RootVariable"/> is unset.</summary>
    public static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "workspace");

    /// <summary>
    /// Resolves a configured root. An unusable value is an error, never a silent fallback to the
    /// default: a caller that believes it writes into an Inventor project must not be given another
    /// directory instead. Drive roots and Windows/Program Files trees are refused outright.
    /// </summary>
    public static string Resolve(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return DefaultRoot();
        string raw = configured!.Trim();
        // Checked before GetFullPath, which would silently resolve a relative or drive-relative
        // value against the Inventor process working directory.
        if (!IsFullyQualified(raw))
            throw new ArgumentException(RootVariable + " must be an absolute path such as C:\\Projects\\workspace.");
        string full;
        try { full = Path.GetFullPath(raw); }
        catch (Exception ex) { throw new ArgumentException(RootVariable + " is not a usable path: " + ex.Message); }
        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = (Path.GetPathRoot(full) ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (full.Length == 0 || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(RootVariable + " must be a directory below a drive or share root, not the root itself.");
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles,
                                       Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.System })
        {
            string protectedPath = Environment.GetFolderPath(folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (protectedPath.Length == 0) continue;
            if (string.Equals(full, protectedPath, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(protectedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(RootVariable + " must not point inside a Windows or Program Files directory.");
        }
        return full;
    }

    /// <summary>Drive-qualified (<c>C:\dir</c>) or UNC (<c>\\server\share</c>) only: no relative,
    /// drive-relative (<c>C:dir</c>) or current-drive (<c>\dir</c>) values. Written without
    /// <c>Path.IsPathFullyQualified</c>, which the net48 add-ins do not have.</summary>
    private static bool IsFullyQualified(string path)
    {
        bool Separator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        if (path.Length >= 2 && Separator(path[0]) && Separator(path[1])) return true; // UNC
        return path.Length >= 3 && path[1] == ':' && Separator(path[2]) && char.IsLetter(path[0]);
    }

    /// <summary>File extension for a requested document kind. Drawings are created by create_drawing_safe.</summary>
    public static string Extension(string? kind) => kind switch
    {
        "part" => ".ipt",
        "sheet_metal" => ".ipt",   // a sheet-metal part is an .ipt with the sheet-metal sub-type
        "assembly" => ".iam",
        _ => throw new ArgumentException("kind must be 'part', 'sheet_metal' or 'assembly'.")
    };

    /// <summary>Managed CAD extensions, in the spelling the workspace accepts.</summary>
    public static readonly string[] ManagedExtensions = { ".ipt", ".iam", ".idw", ".dwg" };

    /// <summary>Extensions the workspace recognizes as its own managed CAD documents.</summary>
    public static bool IsManagedExtension(string? path)
    {
        string extension = Path.GetExtension(path ?? "").ToLowerInvariant();
        return extension == ".ipt" || extension == ".iam" || extension == ".idw" || extension == ".dwg";
    }

    /// <summary>Validates a caller-supplied document name. Returns it unchanged when acceptable.</summary>
    public static string ValidateName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name!.Length > 60)
            throw new ArgumentException("name must be 1 to 60 characters.");
        if (name != name.Trim() || name.EndsWith("."))
            throw new ArgumentException("name must not start or end with whitespace or a dot.");
        if (!name.All(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-'))
            throw new ArgumentException("name accepts letters, digits, space, underscore and hyphen only.");
        if (!char.IsLetterOrDigit(name[0]))
            throw new ArgumentException("name must start with a letter or a digit.");
        if (ReservedNames.Contains(name.ToUpperInvariant()))
            throw new ArgumentException("name collides with a reserved Windows device name.");
        return name;
    }

    /// <summary>True only when <paramref name="path"/> is a real file path under the workspace root.</summary>
    public static bool IsInside(string root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full;
        try { full = Path.GetFullPath(path!); }
        catch { return false; }
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && full.Length > prefix.Length;
    }

    /// <summary>
    /// Target path for a new workspace document. Flat by design so an assembly and its parts resolve
    /// through one directory. Never overwrites: an existing file is an error, not a replacement.
    /// </summary>
    public static string NewPath(string root, string? name, string? kind)
        => NewPathWithExtension(root, name, Extension(kind));

    /// <summary>
    /// Same rules for a document whose extension comes from the open document itself, so a drawing
    /// created in memory can be persisted as IDW or Inventor DWG.
    /// </summary>
    public static string NewPathWithExtension(string root, string? name, string? extension)
    {
        if (extension == null || Array.IndexOf(ManagedExtensions, extension.ToLowerInvariant()) < 0)
            throw new ArgumentException("Unsupported workspace document extension.");
        string path = Path.GetFullPath(Path.Combine(root, ValidateName(name) + extension.ToLowerInvariant()));
        if (!IsInside(root, path)) throw new ArgumentException("Resolved path escapes the workspace root.");
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("A workspace document with that name already exists; pick another name.");
        return path;
    }

    /// <summary>
    /// Existing workspace document addressed by file name with extension. Returns the resolved path
    /// only when it really is a file inside the workspace.
    /// </summary>
    public static string ExistingPath(string root, string? file)
    {
        if (string.IsNullOrEmpty(file)) throw new ArgumentException("file is required, including its extension.");
        // A directory component is refused rather than stripped: silently resolving "sub/Bracket.ipt"
        // to a different document in the workspace root would open something the caller did not name.
        if (file!.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':' }) >= 0)
            throw new ArgumentException("file must be a plain workspace file name, without any directory or drive.");
        string extension = Path.GetExtension(file).ToLowerInvariant();
        if (Array.IndexOf(ManagedExtensions, extension) < 0)
            throw new ArgumentException("file must name a workspace .ipt, .iam, .idw or .dwg document.");
        ValidateName(Path.GetFileNameWithoutExtension(file));
        string path = Path.GetFullPath(Path.Combine(root, Path.GetFileNameWithoutExtension(file) + extension));
        if (!IsInside(root, path)) throw new ArgumentException("Resolved path escapes the workspace root.");
        if (!File.Exists(path)) throw new FileNotFoundException("No such workspace document.", path);
        return path;
    }
}
