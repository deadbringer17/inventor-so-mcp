using System;
using System.IO;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Path helpers the net48 add-ins lack. Behaviour matches <c>Path.GetRelativePath</c>: paths on a
/// different root come back absolute rather than silently becoming a traversal.
/// </summary>
public static class PathCompat
{
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    public static string GetRelativePath(string relativeTo, string path)
    {
        if (string.IsNullOrEmpty(relativeTo)) throw new ArgumentException("relativeTo is required.", nameof(relativeTo));
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required.", nameof(path));
        string from = Path.GetFullPath(relativeTo);
        string to = Path.GetFullPath(path);
        if (!string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
            return to;

        var fromParts = from.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var toParts = to.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        while (common < fromParts.Length && common < toParts.Length &&
               string.Equals(fromParts[common], toParts[common], StringComparison.OrdinalIgnoreCase))
            common++;
        if (common == 0) return to;

        var segments = new System.Collections.Generic.List<string>();
        for (int i = common; i < fromParts.Length; i++) segments.Add("..");
        for (int i = common; i < toParts.Length; i++) segments.Add(toParts[i]);
        return segments.Count == 0 ? "." : string.Join(Path.DirectorySeparatorChar.ToString(), segments);
    }

    /// <summary>Lower-case hex digest; <c>Convert.ToHexString</c> does not exist on net48.</summary>
    public static string ToHex(byte[] bytes)
        => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
}
