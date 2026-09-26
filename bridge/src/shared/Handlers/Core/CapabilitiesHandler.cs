#if INVENTOR2027
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// <c>get_capabilities</c>: what this add-in build actually registered, so the server computes
/// capabilities instead of assuming them (plan §25). Reads only the command map and the active
/// document's type.
/// </summary>
public sealed class CapabilitiesHandler : HandlerBase, IInventorCommand
{
    public string Name => "get_capabilities";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        string? kind = null;
        try
        {
            var doc = ((Application)ctx.Application!).ActiveDocument;
            kind = doc?.DocumentType switch
            {
                DocumentTypeEnum.kPartDocumentObject => CadDocumentKinds.Part,
                DocumentTypeEnum.kAssemblyDocumentObject => CadDocumentKinds.Assembly,
                DocumentTypeEnum.kDrawingDocumentObject => CadDocumentKinds.Drawing,
                null => null,
                _ => "other",
            };
        }
        catch { /* no document or API not ready */ }
        return Ok(ctx, new JObject
        {
            ["inventor_year"] = ctx.InventorYear,
#if SO_EXPERIMENTAL
            ["experimental_build"] = true,
            ["selection_events"] = Bimwright.Ipt.Shared.Plugin.SelectionEventTracker.Active,
#else
            ["experimental_build"] = false,
            ["selection_events"] = false,
#endif
            ["experimental_enabled"] = ctx.AllowExperimental,
            ["read_only"] = ctx.ReadOnly,
            ["atomic_writes_required"] = ctx.RequireAtomicWrites,
            ["active_document_kind"] = kind,
            ["commands"] = new JArray((ctx.Commands?.Keys ?? Enumerable.Empty<string>()).OrderBy(k => k, StringComparer.Ordinal)),
        });
    }
}
#endif
