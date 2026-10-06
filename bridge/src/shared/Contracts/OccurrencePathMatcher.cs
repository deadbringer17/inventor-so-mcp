using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Outcome of <see cref="OccurrencePathMatcher.Resolve"/>.</summary>
public enum OccurrenceMatchKind { Found, NotFound, Ambiguous }

/// <summary>
/// Result of matching an occurrence reference. <see cref="Candidates"/> holds the indexes (into the
/// entries that were passed in) of the single match, or of every match when ambiguous.
/// </summary>
public sealed class OccurrenceMatch
{
    public OccurrenceMatch(OccurrenceMatchKind kind, IReadOnlyList<int> candidates)
    {
        Kind = kind;
        Candidates = candidates;
    }

    public OccurrenceMatchKind Kind { get; }
    public IReadOnlyList<int> Candidates { get; }
    /// <summary>The matched entry when <see cref="Kind"/> is <c>Found</c>.</summary>
    public int Index => Candidates[0];
}

/// <summary>
/// Pure matching rules for assembly occurrence references, over a flattened occurrence tree of
/// (full path, own name) pairs. A first-depth-first-match rule silently resolved <c>Bolt:1</c> to the
/// copy under the first sub-assembly even when several sub-assemblies hold one, so a constraint or a
/// move could land on the wrong component.
/// <list type="bullet">
/// <item>A path such as <c>SubA:1/Bolt:1</c> (separator <c>/</c>) resolves through the tree.</item>
/// <item>A bare name that matches a top-level occurrence wins.</item>
/// <item>Otherwise a bare name resolves only when exactly one occurrence in the whole tree carries it.</item>
/// </list>
/// Comparison is case-insensitive, as Inventor names are.
/// </summary>
public static class OccurrencePathMatcher
{
    public const char PathSeparator = '/';

    /// <summary>How many names an error message lists before it says "+N more".</summary>
    public const int ListCap = 20;

    /// <summary>Builds the full path of a child from its parent's path ("" for top level).</summary>
    public static string Combine(string parentPath, string name)
        => string.IsNullOrEmpty(parentPath) ? name : parentPath + PathSeparator + name;

    public static OccurrenceMatch Resolve(IReadOnlyList<(string Path, string Name)> entries, string query)
    {
        string wanted = (query ?? "").Trim();
        if (wanted.Length == 0) return new OccurrenceMatch(OccurrenceMatchKind.NotFound, Array.Empty<int>());

        var cmp = StringComparer.OrdinalIgnoreCase;
        List<int> hits;
        if (wanted.IndexOf(PathSeparator) >= 0)
        {
            hits = Indexes(entries, e => cmp.Equals(e.Path, wanted));
        }
        else
        {
            // An exact top-level match wins over a same-named component deeper in the tree.
            hits = Indexes(entries, e => cmp.Equals(e.Path, e.Name) && cmp.Equals(e.Name, wanted));
            if (hits.Count == 0) hits = Indexes(entries, e => cmp.Equals(e.Name, wanted));
        }
        if (hits.Count == 0) return new OccurrenceMatch(OccurrenceMatchKind.NotFound, hits);
        return new OccurrenceMatch(hits.Count == 1 ? OccurrenceMatchKind.Found : OccurrenceMatchKind.Ambiguous, hits);
    }

    /// <summary>"a, b, c" for at most <paramref name="cap"/> items, then ", ... +N more".</summary>
    public static string FormatList(IReadOnlyList<string> items, int cap = ListCap)
    {
        if (items.Count <= cap) return string.Join(", ", items);
        return string.Join(", ", items.Take(cap)) + ", ... +" + (items.Count - cap) + " more";
    }

    private static List<int> Indexes(IReadOnlyList<(string Path, string Name)> entries, Func<(string Path, string Name), bool> test)
    {
        var hits = new List<int>();
        for (int i = 0; i < entries.Count; i++)
            if (test(entries[i])) hits.Add(i);
        return hits;
    }
}
