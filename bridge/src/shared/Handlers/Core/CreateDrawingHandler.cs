#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Core;

public sealed class CreateDrawingHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_drawing_safe";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly) return Fail(ctx, "READ_ONLY", "Drawing creation requires write permission.");
        var app = (Application)ctx.Application!;
        var source = app.ActiveDocument;
        if (source is not PartDocument && source is not AssemblyDocument) return Fail(ctx, "WRONG_DOCUMENT_TYPE", "Active part or assembly required.");
        string id = EntityReferences.DocumentId(source);
        if ((string?)p["document_id"] != id) return Fail(ctx, "INVALID_ARGUMENT", "DOCUMENT_CHANGED");
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id)) return Fail(ctx, "INVALID_ARGUMENT", "STALE_REVISION");
        if (source.RequiresUpdate) return Fail(ctx, "INVALID_ARGUMENT", "Source requires a rebuild first.");
        if (p["scale"]?.Type != JTokenType.Float && p["scale"]?.Type != JTokenType.Integer) throw new ArgumentException("Numeric scale required.");
        double scale = DrawingLayout.ValidateScale((double)p["scale"]!);
        if (p["preview"] != null && p["preview"]!.Type != JTokenType.Boolean) throw new ArgumentException("preview must be boolean.");
        bool preview = (bool?)p["preview"] ?? true;
        string Geometry(global::Inventor.Document d) => d is PartDocument part ? part.ComponentDefinition.ModelGeometryVersion : ((AssemblyDocument)d).ComponentDefinition.ModelGeometryVersion;
        var originals = source.AllReferencedDocuments.Cast<global::Inventor.Document>().Append(source)
            .Select(d => new { Doc = d, Path = d.FullFileName, Dirty = d.Dirty, Geometry = Geometry(d) }).ToArray();
        var probe = app.TransactionManager.StartTransaction((Inventor._Document)source, "Inventor SO drawing ownership check");
        bool nested = probe.HasParentTransaction; probe.Abort();
        if (nested) return Fail(ctx, "INVALID_ARGUMENT", "TRANSACTION_BUSY");
        DrawingDocument? drawing = null;
        Transaction? transaction = null;
        bool priorUi = app.UserInterfaceManager.UserInteractionDisabled;
        try
        {
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before drawing creation.");
            app.UserInterfaceManager.UserInteractionDisabled = true;
            drawing = (DrawingDocument)app.Documents.Add(DocumentTypeEnum.kDrawingDocumentObject,
                app.FileManager.GetTemplateFile(DocumentTypeEnum.kDrawingDocumentObject), true);
            if (drawing.Sheets.Count != 1 || drawing.ActiveSheet.DrawingViews.Count != 0) throw new InvalidOperationException("Default template must contain one sheet without model views.");
            transaction = app.TransactionManager.StartTransaction((Inventor._Document)drawing, "Inventor SO drawing views");
            if (transaction.HasParentTransaction) throw new InvalidOperationException("TRANSACTION_BUSY");
            var sheet = drawing.ActiveSheet;
            sheet.Size = DrawingSheetSizeEnum.kA3DrawingSheetSize;
            sheet.Orientation = PageOrientationTypeEnum.kLandscapePageOrientation;
            double left = sheet.Width*0.27, right = sheet.Width*0.73, bottom = sheet.Height*0.36, top = sheet.Height*0.74;
            var geo = app.TransientGeometry;
            var style = DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle;
            var front = sheet.DrawingViews.AddBaseView((Inventor._Document)source, geo.CreatePoint2d(left,bottom), scale, ViewOrientationTypeEnum.kFrontViewOrientation, style);
            sheet.DrawingViews.AddProjectedView(front, geo.CreatePoint2d(left,top), style);
            sheet.DrawingViews.AddProjectedView(front, geo.CreatePoint2d(right,bottom), style);
            sheet.DrawingViews.AddBaseView((Inventor._Document)source, geo.CreatePoint2d(right,top), scale, ViewOrientationTypeEnum.kIsoTopRightViewOrientation, style);
            foreach (DrawingView view in sheet.DrawingViews) view.ShowLabel = false;
            if (!drawing.Update2()) throw new InvalidOperationException("Drawing update failed.");
            var views = sheet.DrawingViews.Cast<DrawingView>().ToArray();
            double footer = Math.Max(4, sheet.TitleBlock == null ? 4 : sheet.TitleBlock.RangeBox.MaxPoint.Y + 0.2);
            DrawingLayout.Validate(sheet.Width, sheet.Height, views.Select(v => new[] { v.Position.X, v.Position.Y, v.Width, v.Height }).ToArray(), footer);
            foreach (var original in originals)
                if (original.Doc.FullFileName != original.Path || original.Doc.Dirty != original.Dirty || Geometry(original.Doc) != original.Geometry)
                    throw new InvalidOperationException("SOURCE_CHANGED: inspect source state.");
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before drawing commit.");
            if (!ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction)) throw new InvalidOperationException("TRANSACTION_OWNERSHIP_LOST");
            string drawingId = EntityReferences.DocumentId((global::Inventor.Document)drawing);
            var result = new JObject { ["status"] = preview ? "preview_rolled_back" : "created", ["view_count"] = views.Length,
                ["scale"] = scale, ["sheet"] = "A3 landscape", ["document_id"] = preview ? null : drawingId,
                ["manufacturing_ready"] = false, ["dimensions_added"] = 0, ["reserved_footer_mm"] = footer * 10 };
            if (preview) { transaction.Abort(); transaction = null; drawing.Close(true); drawing = null; source.Activate(); }
            else { transaction.End(); transaction = null; result["revision"] = ctx.Events.Revision(drawingId); }
            return Ok(ctx, result);
        }
        catch
        {
            if (transaction != null)
            {
                if (!ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction)) throw new InvalidOperationException("ROLLBACK_FAILED: transaction ownership lost; drawing left open.");
                transaction.Abort();
            }
            if (drawing != null) drawing.Close(true); // only the new tool-owned document
            source.Activate();
            throw;
        }
        finally { app.UserInterfaceManager.UserInteractionDisabled = priorUi; }
    }
}
#endif
