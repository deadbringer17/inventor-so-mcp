#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Parameters;

/// <summary>
/// <c>list_parameters</c> — read-only. Lists every parameter of a part document (model + user +
/// reference) with name, expression, evaluated value, and unit. Defaults to the active document; with
/// <c>document_id</c> it reads any open part without activating it, which is what lets one part's
/// dimension drive another's without switching documents back and forth. Inventor has no cross-document
/// parameter link outside iLogic or a derived part, so propagation is the caller's to perform.
/// </summary>
public sealed class ListParametersHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_parameters";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        string? documentId = (string?)p["document_id"];
        global::Inventor.Document? target;
        if (!string.IsNullOrWhiteSpace(documentId))
        {
            var matches = app.Documents.Cast<global::Inventor.Document>()
                .Where(d => Core.EntityReferences.DocumentId(d) == documentId).ToArray();
            if (matches.Length != 1)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    matches.Length == 0 ? "DOCUMENT_NOT_OPEN: open it with inventor_open_document_safe first."
                                        : "AMBIGUOUS_DOCUMENT");
            target = matches[0];
        }
        else
        {
            try { target = app.ActiveDocument; } catch { target = null; }
            if (target is null) return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        }
        if (target is not PartDocument doc)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "list_parameters requires a part document");

        var arr = new JArray();
        try
        {
            foreach (Parameter prm in doc.ComponentDefinition.Parameters)
            {
                arr.Add(ParameterValueDto.From(prm));
            }
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to read parameters: " + ex.Message);
        }

        string resolvedId = Core.EntityReferences.DocumentId((global::Inventor.Document)target);
        return Ok(ctx, new JObject { ["count"] = arr.Count, ["parameters"] = arr,
            ["document_id"] = resolvedId,
            ["active"] = app.ActiveDocument != null &&
                Core.EntityReferences.DocumentId(app.ActiveDocument) == resolvedId });
    }
}
#endif
