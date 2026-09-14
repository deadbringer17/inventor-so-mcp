#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers;

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
        double? requestedScale = null;
        if (p["scale"] != null && p["scale"]!.Type != JTokenType.Null)
        {
            if (p["scale"]!.Type != JTokenType.Float && p["scale"]!.Type != JTokenType.Integer)
                throw new ArgumentException("scale must be numeric, or omitted for automatic scaling.");
            requestedScale = DrawingLayout.ValidateScale((double)p["scale"]!);
        }
        if (p["preview"] != null && p["preview"]!.Type != JTokenType.Boolean) throw new ArgumentException("preview must be boolean.");
        bool preview = (bool?)p["preview"] ?? true;
        var kinds = DrawingViewSet.Parse((string?)p["views"]);
        var projection = ParseProjection((string?)p["projection"]);
        double gutterMm = 15;
        if (p["gutter_mm"] != null && p["gutter_mm"]!.Type != JTokenType.Null)
        {
            if (p["gutter_mm"]!.Type != JTokenType.Float && p["gutter_mm"]!.Type != JTokenType.Integer)
                throw new ArgumentException("gutter_mm must be numeric.");
            gutterMm = (double)p["gutter_mm"]!;
        }
        double gutter = UnitConvert.MmToCm(gutterMm);
        string sheetSizeName = ((string?)p["sheet_size"] ?? "A3").Trim().ToUpperInvariant();
        string orientationName = ((string?)p["orientation"] ?? "landscape").Trim().ToLowerInvariant();
        SheetSizes.Resolve(sheetSizeName, orientationName, out _, out _);   // fail fast before creating anything
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
            sheet.Size = SheetSizeEnum(sheetSizeName);
            sheet.Orientation = orientationName == "portrait"
                ? PageOrientationTypeEnum.kPortraitPageOrientation
                : PageOrientationTypeEnum.kLandscapePageOrientation;
            ApplyProjection(drawing, projection);

            var geo = app.TransientGeometry;
            var style = DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle;
            double reference = SheetPlanner.ReferenceScale;
            // Provisional pitch only has to make each projected view's direction unambiguous:
            // AddProjectedView derives the direction from the position relative to its parent.
            double pitchX = sheet.Width / 5, pitchY = sheet.Height / 5;
            double centerX = sheet.Width / 2, centerY = sheet.Height / 2;

            var created = new System.Collections.Generic.Dictionary<ViewKind, DrawingView>();
            DrawingView? baseView = null;
            // Base views first, then projected: DrawingViewSet.Parse only guarantees 'front' is
            // present SOMEWHERE in the caller's list, not that it comes first (e.g. views=top,front
            // is valid). Iterating in caller order would throw below on a valid, front-inclusive set.
            foreach (var kind in Ordered(kinds))
            {
                var slot = DrawingViewSet.Slot(kind, projection, kinds);
                var at = geo.CreatePoint2d(centerX + slot.Column * pitchX, centerY + slot.Row * pitchY);
                DrawingView view;
                if (DrawingViewSet.IsProjected(kind))
                {
                    // Unreachable once Ordered() creates every base view first: Parse already
                    // guarantees 'front' is present whenever a projected kind is requested.
                    if (baseView == null) throw new ArgumentException("Projected views require 'front'.");
                    view = sheet.DrawingViews.AddProjectedView(baseView, at, style);
                }
                else
                {
                    view = sheet.DrawingViews.AddBaseView((Inventor._Document)source, at, reference, Orientation(kind), style);
                    if (kind == ViewKind.Front) baseView = view;
                }
                created[kind] = view;
            }
            foreach (DrawingView view in sheet.DrawingViews) view.ShowLabel = false;
            if (!drawing.Update2()) throw new InvalidOperationException("Drawing update failed.");

            double footer = Math.Max(4, sheet.TitleBlock == null ? 4 : sheet.TitleBlock.RangeBox.MaxPoint.Y + 0.2);

            // View extents scale linearly with view scale, so normalising the measured reference-scale
            // extents to scale 1 lets the planner answer every candidate scale without another update.
            var extents = new System.Collections.Generic.List<ViewExtent>();
            foreach (var kind in kinds)
                extents.Add(new ViewExtent(kind, created[kind].Width / reference, created[kind].Height / reference));

            var plan = SheetPlanner.Plan(sheet.Width, sheet.Height, footer, extents, projection, gutter, requestedScale);
            if (!plan.Fits)
            {
                string needed = UnitConvert.CmToMm(plan.RequiredWidthCm).ToString("F0") + " x "
                    + UnitConvert.CmToMm(plan.RequiredHeightCm).ToString("F0") + " mm";
                throw new InvalidOperationException(requestedScale.HasValue
                    ? "VIEW_OUTSIDE_LAYOUT: at the requested scale the views need " + needed
                      + "; omit scale for automatic scaling, or use a larger sheet_size."
                    : "NO_FITTING_SCALE: the views need " + needed + " even at 1:500"
                      + (plan.SuggestedSheetSize == null ? "; no listed sheet size fits."
                          : "; retry with sheet_size=" + plan.SuggestedSheetSize + "."));
            }
            double scale = plan.Scale;

            // Apply on base views only: projected views inherit their parent's scale.
            foreach (var kind in kinds)
                if (!DrawingViewSet.IsProjected(kind)) created[kind].Scale = scale;
            if (!drawing.Update2()) throw new InvalidOperationException("Drawing update failed after scaling.");

            // Re-plan on the MEASURED geometry, pinned to the scale just applied, so the positions
            // belong to the drawing as it actually is rather than to the predicted layout.
            var applied = SheetPlanner.Plan(sheet.Width, sheet.Height, footer,
                BuildExtents(kinds, created), projection, gutter, scale);
            if (!applied.Fits) throw new InvalidOperationException(
                "VIEW_OUTSIDE_LAYOUT: the measured views do not fit at the planned scale.");
            // Position the front view first: Inventor keeps projected views aligned to their parent,
            // and the planner keeps every projected view in the front view's own row or column.
            foreach (var kind in Ordered(kinds))
                foreach (var planned in applied.Views!)
                    if (planned.Kind == kind)
                        created[kind].Position = geo.CreatePoint2d(planned.CenterX, planned.CenterY);
            if (!drawing.Update2()) throw new InvalidOperationException("Drawing update failed after placement.");

            var views = sheet.DrawingViews.Cast<DrawingView>().ToArray();
            DrawingLayout.Validate(sheet.Width, sheet.Height,
                views.Select(v => new[] { v.Position.X, v.Position.Y, v.Width, v.Height }).ToArray(), footer);
            foreach (var original in originals)
                if (original.Doc.FullFileName != original.Path || original.Doc.Dirty != original.Dirty || Geometry(original.Doc) != original.Geometry)
                    throw new InvalidOperationException("SOURCE_CHANGED: inspect source state.");
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before drawing commit.");
            if (!ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction)) throw new InvalidOperationException("TRANSACTION_OWNERSHIP_LOST");
            string drawingId = EntityReferences.DocumentId((global::Inventor.Document)drawing);
            var viewNames = new System.Text.StringBuilder();
            foreach (var kind in kinds)
            {
                if (viewNames.Length > 0) viewNames.Append(',');
                viewNames.Append(kind.ToString().ToLowerInvariant());
            }
            var result = new JObject
            {
                ["status"] = preview ? "preview_rolled_back" : "created",
                ["view_count"] = views.Length,
                ["scale"] = scale,
                ["scale_mode"] = requestedScale.HasValue ? "explicit" : "auto",
                ["sheet"] = sheetSizeName + " " + orientationName,
                ["projection"] = projection == ProjectionAngle.First ? "first" : "third",
                ["views"] = viewNames.ToString(),
                ["gutter_mm"] = gutterMm,
                ["document_id"] = preview ? null : drawingId,
                ["manufacturing_ready"] = false,
                ["dimensions_added"] = 0,
                ["reserved_footer_mm"] = UnitConvert.CmToMm(footer)
            };
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

    private static ProjectionAngle ParseProjection(string? value)
    {
        string text = (string.IsNullOrWhiteSpace(value) ? "first" : value!).Trim().ToLowerInvariant();
        if (text == "first") return ProjectionAngle.First;
        if (text == "third") return ProjectionAngle.Third;
        throw new ArgumentException("projection must be 'first' or 'third'.");
    }

    private static DrawingSheetSizeEnum SheetSizeEnum(string name)
    {
        switch (name)
        {
            case "A4": return DrawingSheetSizeEnum.kA4DrawingSheetSize;
            case "A3": return DrawingSheetSizeEnum.kA3DrawingSheetSize;
            case "A2": return DrawingSheetSizeEnum.kA2DrawingSheetSize;
            case "A1": return DrawingSheetSizeEnum.kA1DrawingSheetSize;
            case "A0": return DrawingSheetSizeEnum.kA0DrawingSheetSize;
            default: throw new ArgumentException("Unknown sheet_size '" + name + "'. Use A4, A3, A2, A1 or A0.");
        }
    }

    private static ViewOrientationTypeEnum Orientation(ViewKind kind)
    {
        switch (kind)
        {
            case ViewKind.Front: return ViewOrientationTypeEnum.kFrontViewOrientation;
            case ViewKind.Back: return ViewOrientationTypeEnum.kBackViewOrientation;
            case ViewKind.Top: return ViewOrientationTypeEnum.kTopViewOrientation;
            case ViewKind.Bottom: return ViewOrientationTypeEnum.kBottomViewOrientation;
            case ViewKind.Left: return ViewOrientationTypeEnum.kLeftViewOrientation;
            case ViewKind.Right: return ViewOrientationTypeEnum.kRightViewOrientation;
            case ViewKind.Iso: return ViewOrientationTypeEnum.kIsoTopRightViewOrientation;
            default: throw new ArgumentException("Unsupported view kind.");
        }
    }

    /// <summary>
    /// Force the drawing's projection convention. The template's own standard decides whether a view
    /// placed above the front view reads as the plan (third angle) or the bottom view (first angle),
    /// so leaving it to the host silently produces mirrored drawings on differently configured machines.
    /// </summary>
    private static void ApplyProjection(DrawingDocument drawing, ProjectionAngle projection)
    {
        try
        {
            // DrawingStylesManager.ActiveStandardStyle is a DrawingStandardStyle, whose projection
            // convention is the bool FirstAngleProjection. ProjectionTypeEnum is unrelated: it
            // selects orthographic vs perspective, not first vs third angle.
            drawing.StylesManager.ActiveStandardStyle.FirstAngleProjection =
                projection == ProjectionAngle.First;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "PROJECTION_UNAVAILABLE: the drawing standard does not accept a projection change: " + ex.Message);
        }
    }

    /// <summary>Measured extents of already-scaled views, normalised back to scale 1.</summary>
    private static System.Collections.Generic.List<ViewExtent> BuildExtents(
        ViewKind[] kinds, System.Collections.Generic.Dictionary<ViewKind, DrawingView> created)
    {
        var extents = new System.Collections.Generic.List<ViewExtent>();
        foreach (var kind in kinds)
        {
            var view = created[kind];
            double scale = view.Scale <= 0 ? 1 : view.Scale;
            extents.Add(new ViewExtent(kind, view.Width / scale, view.Height / scale));
        }
        return extents;
    }

    /// <summary>Base views first, so projected children are repositioned against a settled parent.</summary>
    private static System.Collections.Generic.IEnumerable<ViewKind> Ordered(ViewKind[] kinds)
    {
        foreach (var kind in kinds) if (!DrawingViewSet.IsProjected(kind)) yield return kind;
        foreach (var kind in kinds) if (DrawingViewSet.IsProjected(kind)) yield return kind;
    }
}
#endif
