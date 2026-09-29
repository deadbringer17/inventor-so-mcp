using System;
using System.Security.Cryptography;
using System.Text;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// API-agnostic part of the document id. Inventor files copied in Windows Explorer share the same
/// <c>InternalName</c> GUID, so the id is that name when it is unique among the open documents and
/// gains a stable <c>~xxxxxxxx</c> path hash when it is not. The suffix alphabet is [0-9a-f], so the id
/// stays inside <c>[A-Za-z0-9_{}~-]</c> like every id issued before.
/// </summary>
public static class DocumentIdComposer
{
    public const string Prefix = "doc_";
    public const char SuffixSeparator = '~';

    /// <summary>Unique name: exactly <c>doc_</c> + InternalName (unchanged for ordinary files).</summary>
    public static string Compose(string internalName, string? fullFileName, bool isDuplicated)
    {
        if (!isDuplicated) return Prefix + internalName;
        return Prefix + internalName + SuffixSeparator + PathHash(fullFileName);
    }

    /// <summary>First 4 bytes of SHA-256 of the lower-cased path with '/' folded to '\', as 8 hex chars.</summary>
    public static string PathHash(string? fullFileName)
    {
        string normalised = (fullFileName ?? "").Trim().Replace('/', (char)92).ToLowerInvariant();
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalised));
        var sb = new StringBuilder(8);
        for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }
}
