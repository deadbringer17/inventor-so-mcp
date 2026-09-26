using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Ipt.Server;

/// <summary>
/// Production SO surface, derived from <see cref="ToolContracts"/>: a tool without a contract is never
/// exposed. Experimental tools (implemented, not yet live-verified) are exposed only on explicit
/// opt-in. Full access is a separate explicit opt-in for trusted local installs.
/// </summary>
public static class SoToolPolicy
{
    private static readonly HashSet<string> Allowed =
        new(ToolContracts.NamesIn(ToolContracts.Production), StringComparer.Ordinal);

    private static readonly HashSet<string> ExperimentalTools =
        new(ToolContracts.NamesIn(ToolContracts.Experimental), StringComparer.Ordinal);

    public static IReadOnlyCollection<string> ProductionTools => Allowed;
    public static IReadOnlyCollection<string> Experimental => ExperimentalTools;

    public static bool IsExposed(string? name, bool fullAccess = false) => IsExposed(name, fullAccess, false);

    public static bool IsExposed(string? name, bool fullAccess, bool experimental) =>
        name != null && (fullAccess || Allowed.Contains(name) || (experimental && ExperimentalTools.Contains(name)));

    public static string TierOf(string name) =>
        Allowed.Contains(name) ? ToolContracts.Production
        : ExperimentalTools.Contains(name) ? ToolContracts.Experimental
        : "unreviewed";
}
