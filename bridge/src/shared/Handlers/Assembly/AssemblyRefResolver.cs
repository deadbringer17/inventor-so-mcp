#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Inv = global::Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// Authoritative helper to find assembly occurrences and resolve named geometry references
/// (iMates, user work features, or origin geometry) into Inventor geometry objects/proxies.
/// </summary>
public static class AssemblyRefResolver
{
    private static readonly string[] OriginPlanes = { "XY Plane", "XZ Plane", "YZ Plane" };
    private static readonly string[] OriginAxes = { "X Axis", "Y Axis", "Z Axis" };
    private const string CenterPoint = "Center Point";

    /// <summary>
    /// Finds an occurrence in the assembly. If name is null/empty, sets occ=null representing the
    /// assembly itself. Returns true if successful. A top-level name wins; "SubA:1/Bolt:1" resolves
    /// through the tree; a bare nested name resolves only when it is unique (see
    /// <see cref="OccurrencePathMatcher"/>). On failure, returns false and lists the available
    /// occurrence paths (capped).
    /// </summary>
    public static bool TryFindOccurrence(
        Inv.AssemblyComponentDefinition def,
        string? name,
        out Inv.ComponentOccurrence? occ,
        out string? error)
        => TryFindOccurrence(def, name, out occ, out error, out _);

    /// <summary>
    /// As above, and <paramref name="details"/> carries <c>reason = AMBIGUOUS_OCCURRENCE</c> plus the
    /// candidate paths when a bare name matches several occurrences, so a caller never parses the message.
    /// </summary>
    public static bool TryFindOccurrence(
        Inv.AssemblyComponentDefinition def,
        string? name,
        out Inv.ComponentOccurrence? occ,
        out string? error,
        out JObject? details)
    {
        occ = null;
        error = null;
        details = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        string occurrenceName = name!.Trim();

        // Cheap path first: an exact top-level name needs no walk of the sub-assemblies.
        if (occurrenceName.IndexOf(OccurrencePathMatcher.PathSeparator) < 0)
        {
            var topLevel = new List<Inv.ComponentOccurrence>();
            foreach (Inv.ComponentOccurrence o in def.Occurrences)
                if (string.Equals(o.Name, occurrenceName, StringComparison.OrdinalIgnoreCase)) topLevel.Add(o);
            if (topLevel.Count == 1)
            {
                occ = topLevel[0];
                return true;
            }
        }

        var entries = new List<(string Path, string Name)>();
        var found = new List<Inv.ComponentOccurrence>();
        CollectOccurrences(def.Occurrences, "", entries, found);

        var match = OccurrencePathMatcher.Resolve(entries, occurrenceName);
        switch (match.Kind)
        {
            case OccurrenceMatchKind.Found:
                occ = found[match.Index];
                return true;

            case OccurrenceMatchKind.Ambiguous:
            {
                var paths = match.Candidates.Select(i => entries[i].Path).ToList();
                error = $"Occurrence '{occurrenceName}' is ambiguous; use the full path (parent/child). " +
                        $"Matches: {OccurrencePathMatcher.FormatList(paths)}";
                details = new JObject
                {
                    ["reason"] = "AMBIGUOUS_OCCURRENCE",
                    ["candidates"] = new JArray(paths.Take(OccurrencePathMatcher.ListCap).Cast<object>().ToArray()),
                    ["candidate_count"] = paths.Count,
                };
                return false;
            }

            default:
                error = $"Occurrence '{occurrenceName}' not found. Available occurrences: " +
                        OccurrencePathMatcher.FormatList(entries.Select(e => e.Path).ToList());
                return false;
        }
    }

    private static void CollectOccurrences(System.Collections.IEnumerable occurrences, string parentPath,
        List<(string Path, string Name)> entries, List<Inv.ComponentOccurrence> found)
    {
        foreach (Inv.ComponentOccurrence o in occurrences)
        {
            string path = OccurrencePathMatcher.Combine(parentPath, o.Name);
            entries.Add((path, o.Name));
            found.Add(o);
            try
            {
                if (o.SubOccurrences != null && o.SubOccurrences.Count > 0)
                {
                    CollectOccurrences(o.SubOccurrences, path, entries, found);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Resolves an occurrence and reference name in the assembly. Returns true if successful.
    /// If occurrenceName is null/empty, resolves against the top-level assembly's origin/work geometry.
    /// If resolved within an occurrence, automatically creates and returns a geometry proxy.
    /// </summary>
    public static bool TryResolveInAssembly(
        Inv.AssemblyComponentDefinition def,
        string? occurrenceName,
        string refName,
        out object? entity,
        out string? error)
        => TryResolveInAssembly(def, occurrenceName, refName, out entity, out error, out _);

    /// <summary>As above; <paramref name="details"/> carries the AMBIGUOUS_OCCURRENCE reason and candidates.</summary>
    public static bool TryResolveInAssembly(
        Inv.AssemblyComponentDefinition def,
        string? occurrenceName,
        string refName,
        out object? entity,
        out string? error,
        out JObject? details)
    {
        entity = null;
        error = null;

        if (!TryFindOccurrence(def, occurrenceName, out var occ, out var occErr, out details))
        {
            error = occErr;
            return false;
        }

        try
        {
            entity = ResolveRef(def, occ, refName);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Resolves a reference name against a part document. Returns true if successful.
    /// </summary>
    public static bool TryResolveInPart(
        Inv.PartComponentDefinition def,
        string refName,
        out object? entity,
        out string? error)
    {
        entity = null;
        error = null;

        try
        {
            entity = ResolveRef(def, occ: null, refName);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // def is passed as object because Inventor interop does NOT model inheritance between
    // ComponentDefinition and Part/AssemblyComponentDefinition (assignment fails), yet COM QueryInterface
    // via `is` pattern-matching works — so we accept object and type-test.
    private static object ResolveRef(object def, Inv.ComponentOccurrence? occ, string refName)
    {
        if (string.IsNullOrWhiteSpace(refName))
        {
            throw new ArgumentException("Reference name cannot be empty");
        }

        Inv.WorkPlanes? workPlanes = null;
        Inv.WorkAxes? workAxes = null;
        Inv.WorkPoints? workPoints = null;
        Inv.iMateDefinitions? iMates = null;

        if (occ != null)
        {
            var occDef = occ.Definition;
            if (occDef is Inv.PartComponentDefinition partDef)
            {
                workPlanes = partDef.WorkPlanes;
                workAxes = partDef.WorkAxes;
                workPoints = partDef.WorkPoints;
                iMates = partDef.iMateDefinitions;
            }
            else if (occDef is Inv.AssemblyComponentDefinition asmDef)
            {
                workPlanes = asmDef.WorkPlanes;
                workAxes = asmDef.WorkAxes;
                workPoints = asmDef.WorkPoints;
                iMates = asmDef.iMateDefinitions;
            }
        }
        else if (def is Inv.PartComponentDefinition partDefTop)
        {
            workPlanes = partDefTop.WorkPlanes;
            workAxes = partDefTop.WorkAxes;
            workPoints = partDefTop.WorkPoints;
            iMates = partDefTop.iMateDefinitions;
        }
        else if (def is Inv.AssemblyComponentDefinition asmDefTop)
        {
            workPlanes = asmDefTop.WorkPlanes;
            workAxes = asmDefTop.WorkAxes;
            workPoints = asmDefTop.WorkPoints;
            iMates = asmDefTop.iMateDefinitions;
        }

        // Resolution order per design: iMate name -> named work feature -> origin geometry.
        // (Origin planes/axes/points are themselves members of the WorkPlanes/WorkAxes/WorkPoints
        // collections, so a user iMate that happens to share an origin name still wins.)
        object? resolved = null;

        // 1. iMates
        if (iMates != null)
        {
            foreach (Inv.iMateDefinition im in iMates)
            {
                try
                {
                    if (string.Equals(im.Name, refName, StringComparison.OrdinalIgnoreCase))
                    {
                        // The strongly typed ReferencedEntity property returns an opaque COM object
                        // on Inventor 2027. The verified API path exposes the usable Face/Edge here.
                        resolved = ((dynamic)im).Entity;
                        break;
                    }
                }
                catch { }
            }
        }

        // 2. Named work features (this also matches origin geometry by its enumerated name)
        try
        {
            if (resolved == null && workPlanes != null)
            {
                foreach (Inv.WorkPlane wp in workPlanes)
                {
                    if (string.Equals(wp.Name, refName, StringComparison.OrdinalIgnoreCase)) { resolved = wp; break; }
                }
            }
            if (resolved == null && workAxes != null)
            {
                foreach (Inv.WorkAxis wa in workAxes)
                {
                    if (string.Equals(wa.Name, refName, StringComparison.OrdinalIgnoreCase)) { resolved = wa; break; }
                }
            }
            if (resolved == null && workPoints != null)
            {
                foreach (Inv.WorkPoint wpt in workPoints)
                {
                    if (string.Equals(wpt.Name, refName, StringComparison.OrdinalIgnoreCase)) { resolved = wpt; break; }
                }
            }
        }
        catch { }

        // 3. Origin geometry by canonical name via indexer (fallback for localized enumerated names)
        if (resolved == null && OriginPlanes.Contains(refName, StringComparer.OrdinalIgnoreCase) && workPlanes != null)
        {
            try { resolved = workPlanes[refName]; } catch { }
        }
        if (resolved == null && OriginAxes.Contains(refName, StringComparer.OrdinalIgnoreCase) && workAxes != null)
        {
            try { resolved = workAxes[refName]; } catch { }
        }
        if (resolved == null && string.Equals(refName, CenterPoint, StringComparison.OrdinalIgnoreCase) && workPoints != null)
        {
            try { resolved = workPoints[refName]; } catch { }
        }

        if (resolved == null)
        {
            var available = AvailableNames(workPlanes, workAxes, workPoints, iMates);
            throw new ArgumentException($"Reference '{refName}' not found. Available references on target: {string.Join(", ", available)}");
        }

        object resolvedEntity = resolved;

        // If occurrence is provided, create geometry proxy for assembly constraint Solver
        if (occ != null)
        {
            occ.CreateGeometryProxy(resolvedEntity, out var proxy);
            return proxy;
        }

        return resolvedEntity;
    }

    /// <summary>
    /// Collects all valid reference names (origin, iMates, user work features) on the definition.
    /// </summary>
    public static List<string> AvailableNames(
        Inv.WorkPlanes? workPlanes,
        Inv.WorkAxes? workAxes,
        Inv.WorkPoints? workPoints,
        Inv.iMateDefinitions? iMates)
    {
        var names = new List<string>();
        names.AddRange(OriginPlanes);
        names.AddRange(OriginAxes);
        names.Add(CenterPoint);

        if (iMates != null)
        {
            foreach (Inv.iMateDefinition im in iMates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(im.Name)) names.Add(im.Name);
                }
                catch { }
            }
        }

        try
        {
            if (workPlanes != null)
            {
                foreach (Inv.WorkPlane wp in workPlanes)
                {
                    if (!OriginPlanes.Contains(wp.Name, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(wp.Name))
                        names.Add(wp.Name);
                }
            }
            if (workAxes != null)
            {
                foreach (Inv.WorkAxis wa in workAxes)
                {
                    if (!OriginAxes.Contains(wa.Name, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(wa.Name))
                        names.Add(wa.Name);
                }
            }
            if (workPoints != null)
            {
                foreach (Inv.WorkPoint wpt in workPoints)
                {
                    if (!string.Equals(wpt.Name, CenterPoint, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(wpt.Name))
                        names.Add(wpt.Name);
                }
            }
        }
        catch { }

        return names;
    }

    /// <summary>
    /// Structured self-teaching payload per spec §9: <c>{imates, work_features, origin}</c> for a part
    /// or assembly component definition (or an occurrence's definition). Names are grouped so an agent
    /// can retry a failed ref against the correct category. scopeDef is object because Inventor interop
    /// has no ComponentDefinition inheritance (see ResolveRef); we COM type-test it.
    /// </summary>
    public static JObject AvailableNamesStructured(object? scopeDef)
    {
        Inv.WorkPlanes? workPlanes = null;
        Inv.WorkAxes? workAxes = null;
        Inv.WorkPoints? workPoints = null;
        Inv.iMateDefinitions? iMates = null;

        if (scopeDef is Inv.PartComponentDefinition pd)
        {
            workPlanes = pd.WorkPlanes; workAxes = pd.WorkAxes; workPoints = pd.WorkPoints; iMates = pd.iMateDefinitions;
        }
        else if (scopeDef is Inv.AssemblyComponentDefinition ad)
        {
            workPlanes = ad.WorkPlanes; workAxes = ad.WorkAxes; workPoints = ad.WorkPoints; iMates = ad.iMateDefinitions;
        }

        var imates = new JArray();
        if (iMates != null)
        {
            foreach (Inv.iMateDefinition im in iMates)
            {
                try { if (!string.IsNullOrEmpty(im.Name)) imates.Add(im.Name); } catch { }
            }
        }

        var workFeatures = new JArray();
        var origin = new JArray();
        try
        {
            if (workPlanes != null)
                foreach (Inv.WorkPlane wp in workPlanes)
                {
                    if (string.IsNullOrEmpty(wp.Name)) continue;
                    (OriginPlanes.Contains(wp.Name, StringComparer.OrdinalIgnoreCase) ? origin : workFeatures).Add(wp.Name);
                }
            if (workAxes != null)
                foreach (Inv.WorkAxis wa in workAxes)
                {
                    if (string.IsNullOrEmpty(wa.Name)) continue;
                    (OriginAxes.Contains(wa.Name, StringComparer.OrdinalIgnoreCase) ? origin : workFeatures).Add(wa.Name);
                }
            if (workPoints != null)
                foreach (Inv.WorkPoint wpt in workPoints)
                {
                    if (string.IsNullOrEmpty(wpt.Name)) continue;
                    (string.Equals(wpt.Name, CenterPoint, StringComparison.OrdinalIgnoreCase) ? origin : workFeatures).Add(wpt.Name);
                }
        }
        catch { }

        return new JObject { ["imates"] = imates, ["work_features"] = workFeatures, ["origin"] = origin };
    }

    /// <summary>
    /// Converts a constraint HealthStatusEnum to its stable wire string format.
    /// </summary>
    public static string HealthToString(Inv.HealthStatusEnum h)
    {
        return h switch
        {
            Inv.HealthStatusEnum.kUpToDateHealth => "up_to_date",
            Inv.HealthStatusEnum.kOutOfDateHealth => "out_of_date",
            Inv.HealthStatusEnum.kDriverLostHealth => "driver_lost",
            Inv.HealthStatusEnum.kInErrorHealth => "in_error",
            Inv.HealthStatusEnum.kSuppressedHealth => "suppressed",
            Inv.HealthStatusEnum.kInconsistentHealth => "inconsistent",
            _ => "unknown_" + ((int)h)
        };
    }
}
#endif
