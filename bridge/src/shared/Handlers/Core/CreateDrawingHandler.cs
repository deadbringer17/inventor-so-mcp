#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Handlers.Properties;
using File = System.IO.File;

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
        if ((string?)p["document_id"] != id) return Fail(ctx, ConcurrencyFailure.DocumentChanged((string?)p["document_id"], id));
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id)) return Fail(ctx, ConcurrencyFailure.StaleRevision((string?)p["expected_revision"], ctx.Events?.Revision(id)));
        if (source.RequiresUpdate) return Fail(ctx, "INVALID_ARGUMENT", "Source requires a rebuild first.");
        // All argument validation below runs before anything is created (the drawing document, its
        // transaction), so an ArgumentException here can safely become a structured INVALID_ARGUMENT
        // result instead of an exception that CommandDispatcher would sanitize into an opaque
        // API_ERROR, losing the ability for a caller to tell "you typed the views wrong" from
        // "Inventor crashed".
        double? requestedScale;
        bool preview;
        ViewKind[] kinds;
        ProjectionAngle projection;
        double gutterMm = 15;
        double gutter;
        string sheetSizeName;
        string orientationName;
        string? templateName;
        string? templatePath = null;
        DrawingTemplateManifest? manifest = null;
        System.Collections.Generic.IReadOnlyList<TitleBlockField> titleBlock;
        try
        {
            requestedScale = null;
            if (p["scale"] != null && p["scale"]!.Type != JTokenType.Null)
            {
                if (p["scale"]!.Type != JTokenType.Float && p["scale"]!.Type != JTokenType.Integer)
                    throw new ArgumentException("scale must be numeric, or omitted for automatic scaling.");
                requestedScale = DrawingLayout.ValidateScale((double)p["scale"]!);
            }
            if (p["preview"] != null && p["preview"]!.Type != JTokenType.Boolean) throw new ArgumentException("preview must be boolean.");
            preview = (bool?)p["preview"] ?? true;
            kinds = DrawingViewSet.Parse((string?)p["views"]);
            projection = ParseProjection((string?)p["projection"]);
            if (p["gutter_mm"] != null && p["gutter_mm"]!.Type != JTokenType.Null)
            {
                if (p["gutter_mm"]!.Type != JTokenType.Float && p["gutter_mm"]!.Type != JTokenType.Integer)
                    throw new ArgumentException("gutter_mm must be numeric.");
                gutterMm = (double)p["gutter_mm"]!;
            }
            gutter = UnitConvert.MmToCm(gutterMm);
            titleBlock = TitleBlockFields.Parse(p["title_block"]);
            templateName = (string?)p["template"];
            if (string.IsNullOrWhiteSpace(templateName)) templateName = null;
            if (templateName != null)
            {
                // The template's own sheet is authoritative. A company border and title block are
                // drawn to one paper size and do NOT rescale when Sheet.Size changes, so honouring an
                // explicit sheet_size here would silently produce a drawing with the frame in the
                // wrong place - worse than refusing.
                if (p["sheet_size"] != null && p["sheet_size"]!.Type != JTokenType.Null)
                    throw new ArgumentException("sheet_size cannot be combined with template: the template's own sheet is used.");
                if (p["orientation"] != null && p["orientation"]!.Type != JTokenType.Null)
                    throw new ArgumentException("orientation cannot be combined with template: the template's own sheet is used.");
            }
            sheetSizeName = ((string?)p["sheet_size"] ?? "A3").Trim().ToUpperInvariant();
            orientationName = ((string?)p["orientation"] ?? "landscape").Trim().ToLowerInvariant();
            SheetSizes.Resolve(sheetSizeName, orientationName, out _, out _);   // fail fast before creating anything
            if (templateName != null) ResolveTemplate(templateName, out templatePath, out manifest);
        }
        catch (ArgumentException ex)
        {
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message);
        }
        catch (CodedFailureException failure)
        {
            // Template resolution runs before anything is created, so there is nothing to roll back.
            return Fail(ctx, failure);
        }
        string Geometry(global::Inventor.Document d) => d is PartDocument part ? part.ComponentDefinition.ModelGeometryVersion : ((AssemblyDocument)d).ComponentDefinition.ModelGeometryVersion;
        var originals = source.AllReferencedDocuments.Cast<global::Inventor.Document>().Append(source)
            .Select(d => new { Doc = d, Path = d.FullFileName, Dirty = d.Dirty, Geometry = Geometry(d) }).ToArray();
        var probe = app.TransactionManager.StartTransaction((Inventor._Document)source, "Inventor SO drawing ownership check");
        bool nested = probe.HasParentTransaction; probe.Abort();
        if (nested) return Fail(ctx, ConcurrencyFailure.TransactionBusy());
        DrawingDocument? drawing = null;
        Transaction? transaction = null;
        bool priorUi = app.UserInterfaceManager.UserInteractionDisabled;
        // Shared by both catch clauses below: abort the owned transaction (if still owned) and
        // close only the tool-created draft, regardless of whether the failure is reported back as
        // a structured Fail() or rethrown.
        void RollbackDraft()
        {
            if (transaction != null)
            {
                if (!ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction))
                    throw new CodedFailureException(InventorErrorCodes.ROLLBACK_FAILED,
                        "Transaction ownership was lost during rollback; the draft drawing is left open. Inspect it before continuing.");
                transaction.Abort();
            }
            if (drawing != null) drawing.Close(true); // only the new tool-owned document
            source.Activate();
        }
        try
        {
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before drawing creation.");
            app.UserInterfaceManager.UserInteractionDisabled = true;
            drawing = (DrawingDocument)app.Documents.Add(DocumentTypeEnum.kDrawingDocumentObject,
                templatePath ?? app.FileManager.GetTemplateFile(DocumentTypeEnum.kDrawingDocumentObject), true);
            if (drawing.Sheets.Count != 1 || drawing.ActiveSheet.DrawingViews.Count != 0)
                throw templatePath == null
                    ? new InvalidOperationException("Default template must contain one sheet without model views.")
                    : new CodedFailureException(InventorErrorCodes.TEMPLATE_UNUSABLE,
                        "Template '" + templateName + "' must contain exactly one sheet and no model views; this one has "
                          + drawing.Sheets.Count + " sheet(s) and "
                          + drawing.ActiveSheet.DrawingViews.Count + " view(s) on the active sheet.");
            transaction = app.TransactionManager.StartTransaction((Inventor._Document)drawing, "Inventor SO drawing views");
            if (transaction.HasParentTransaction) throw ConcurrencyFailure.TransactionBusy();
            var sheet = drawing.ActiveSheet;
            // With a template the sheet comes as the company drew it: size, orientation, border and
            // title block are left exactly as they are.
            if (templatePath == null)
            {
                sheet.Size = SheetSizeEnum(sheetSizeName);
                sheet.Orientation = orientationName == "portrait"
                    ? PageOrientationTypeEnum.kPortraitPageOrientation
                    : PageOrientationTypeEnum.kLandscapePageOrientation;
            }
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

            double footer = Math.Max(SheetPlanner.MinReservedBottomCm,
                sheet.TitleBlock == null ? SheetPlanner.MinReservedBottomCm : sheet.TitleBlock.RangeBox.MaxPoint.Y + 0.2);
            // With a template the usable region is whatever its manifest declares, checked against the
            // sheet that was actually produced; without one it is the sheet above the measured title
            // block, exactly as before.
            UsableArea area;
            if (manifest != null)
            {
                try { area = manifest.Verify(sheet.Width, sheet.Height); }
                catch (ArgumentException ex)
                { throw new CodedFailureException(InventorErrorCodes.TEMPLATE_SHEET_MISMATCH, ex.Message); }
            }
            else area = UsableArea.FromReservedBottom(sheet.Width, sheet.Height, footer);

            // View extents scale linearly with view scale, so normalising the measured reference-scale
            // extents to scale 1 lets the planner answer every candidate scale without another update.
            var extents = new System.Collections.Generic.List<ViewExtent>();
            foreach (var kind in kinds)
                extents.Add(new ViewExtent(kind, created[kind].Width / reference, created[kind].Height / reference));

            var plan = SheetPlanner.Plan(area, extents, projection, gutter, requestedScale);
            if (!plan.Fits)
            {
                double neededWidthMm = UnitConvert.CmToMm(plan.RequiredWidthCm);
                double neededHeightMm = UnitConvert.CmToMm(plan.RequiredHeightCm);
                string needed = neededWidthMm.ToString("F0") + " x " + neededHeightMm.ToString("F0") + " mm";
                var details = new JObject { ["required_width_mm"] = neededWidthMm, ["required_height_mm"] = neededHeightMm };
                if (requestedScale.HasValue)
                    throw new CodedFailureException(InventorErrorCodes.VIEW_OUTSIDE_LAYOUT,
                        "At the requested scale the views need " + needed
                          + "; omit scale for automatic scaling, or use a larger sheet_size.", details);
                if (plan.SuggestedSheetSize != null) details["sheet_size"] = plan.SuggestedSheetSize;
                // A template pins the sheet, so "retry with a bigger sheet_size" is advice the caller
                // cannot take: it has to pick the company template drawn for that size instead.
                throw new CodedFailureException(InventorErrorCodes.NO_FITTING_SCALE,
                    "The views need " + needed + " even at 1:500"
                      + (plan.SuggestedSheetSize == null ? "; no listed sheet size fits."
                          : templateName != null
                              ? "; use a template drawn on " + plan.SuggestedSheetSize + " or larger."
                              : "; retry with sheet_size=" + plan.SuggestedSheetSize + "."), details);
            }
            double scale = plan.Scale;

            // Apply on base views only: projected views inherit their parent's scale.
            foreach (var kind in kinds)
                if (!DrawingViewSet.IsProjected(kind)) created[kind].Scale = scale;
            if (!drawing.Update2()) throw new InvalidOperationException("Drawing update failed after scaling.");

            // Re-plan on the MEASURED geometry, pinned to the scale just applied, so the positions
            // belong to the drawing as it actually is rather than to the predicted layout.
            var applied = SheetPlanner.Plan(area, BuildExtents(kinds, created), projection, gutter, scale);
            if (!applied.Fits)
                throw new CodedFailureException(InventorErrorCodes.VIEW_OUTSIDE_LAYOUT,
                    "The measured views do not fit at the planned scale.",
                    new JObject
                    {
                        ["required_width_mm"] = UnitConvert.CmToMm(applied.RequiredWidthCm),
                        ["required_height_mm"] = UnitConvert.CmToMm(applied.RequiredHeightCm)
                    });
            // Position the front view first: Inventor keeps projected views aligned to their parent,
            // and the planner keeps every projected view in the front view's own row or column.
            foreach (var kind in Ordered(kinds))
                foreach (var planned in applied.Views!)
                    if (planned.Kind == kind)
                        created[kind].Position = geo.CreatePoint2d(planned.CenterX, planned.CenterY);
            if (!drawing.Update2()) throw new InvalidOperationException("Drawing update failed after placement.");

            int fieldsSet = 0, fieldsCreated = 0;
            ApplyTitleBlock((global::Inventor.Document)drawing, titleBlock, ref fieldsSet, ref fieldsCreated);

            var views = sheet.DrawingViews.Cast<DrawingView>().ToArray();
            DrawingLayout.Validate(area,
                views.Select(v => new[] { v.Position.X, v.Position.Y, v.Width, v.Height }).ToArray());
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
                // With a template the requested sheet_size/orientation were refused, so the defaults
                // held in those variables describe nothing; the manifest's verified sheet does.
                ["sheet"] = manifest != null
                    ? manifest.SheetSize + " " + manifest.Orientation
                    : sheetSizeName + " " + orientationName,
                ["projection"] = projection == ProjectionAngle.First ? "first" : "third",
                ["views"] = viewNames.ToString(),
                ["gutter_mm"] = gutterMm,
                ["document_id"] = preview ? null : drawingId,
                ["manufacturing_ready"] = false,
                ["dimensions_added"] = 0,
                ["reserved_footer_mm"] = UnitConvert.CmToMm(footer),
                ["template"] = templateName,
                ["usable_area_mm"] = new JObject
                {
                    ["x_min"] = UnitConvert.CmToMm(area.XMinCm),
                    ["y_min"] = UnitConvert.CmToMm(area.YMinCm),
                    ["x_max"] = UnitConvert.CmToMm(area.XMaxCm),
                    ["y_max"] = UnitConvert.CmToMm(area.YMaxCm)
                },
                ["title_block_fields_set"] = fieldsSet,
                ["title_block_fields_created"] = fieldsCreated
            };
            if (preview) { transaction.Abort(); transaction = null; drawing.Close(true); drawing = null; source.Activate(); }
            else { transaction.End(); transaction = null; result["revision"] = ctx.Events.Revision(drawingId); }
            return Ok(ctx, result);
        }
        catch (CodedFailureException failure)
        {
            // A layout/projection failure the caller can branch on — including the ones
            // DrawingLayout.Validate raises. Report it as a structured result with the same rollback
            // the generic catch below performs, rather than letting it travel as an exception.
            RollbackDraft();
            return Fail(ctx, failure);
        }
        catch
        {
            RollbackDraft();
            throw;
        }
        finally { app.UserInterfaceManager.UserInteractionDisabled = priorUi; }
    }

    /// <summary>
    /// Resolves a template name against the host-owned library and loads its manifest, before any
    /// document exists. Every outcome is a code of its own: the caller has a different thing to fix
    /// for a missing file, a missing sidecar and a malformed sidecar.
    /// </summary>
    private static void ResolveTemplate(string name, out string path, out DrawingTemplateManifest manifest)
    {
        string root;
        try { root = DrawingTemplatePolicy.Root(); }
        catch (ArgumentException ex)
        {
            throw new CodedFailureException(InventorErrorCodes.TEMPLATE_NOT_FOUND,
                "TEMPLATE_LIBRARY_NOT_CONFIGURED: " + ex.Message);
        }
        path = DrawingTemplatePolicy.PathOf(root, name);   // ArgumentException => INVALID_ARGUMENT
        if (!File.Exists(path))
            throw new CodedFailureException(InventorErrorCodes.TEMPLATE_NOT_FOUND,
                "No drawing template named '" + name + "' in the template library. List the installed templates with list_drawing_templates.",
                new JObject { ["template_root"] = root });
        string manifestPath = DrawingTemplatePolicy.ManifestPathOf(path);
        if (!File.Exists(manifestPath))
            throw new CodedFailureException(InventorErrorCodes.TEMPLATE_MANIFEST_MISSING,
                "Template '" + name + "' has no manifest next to it. Create '"
                  + System.IO.Path.GetFileName(manifestPath)
                  + "' declaring sheet_size, orientation and usable_area_mm (x_min, y_min, x_max, y_max).",
                new JObject { ["manifest_path"] = manifestPath });
        string json;
        try { json = File.ReadAllText(manifestPath); }
        catch (Exception ex)
        {
            throw new CodedFailureException(InventorErrorCodes.TEMPLATE_MANIFEST_INVALID,
                "The manifest of template '" + name + "' cannot be read: " + ex.Message);
        }
        try { manifest = DrawingTemplateManifest.Parse(json); }
        catch (ArgumentException ex)
        {
            throw new CodedFailureException(InventorErrorCodes.TEMPLATE_MANIFEST_INVALID,
                "The manifest of template '" + name + "' is not usable: " + ex.Message,
                new JObject { ["manifest_path"] = manifestPath });
        }
    }

    /// <summary>
    /// Writes the caller's title-block values into the drawing's iProperties, which is how an
    /// Inventor title block is populated: its text fields are bound to property names, not written
    /// into the sheet. A name the document does not carry is created as a user-defined property,
    /// because a company title block usually reads custom ones.
    /// </summary>
    private static void ApplyTitleBlock(global::Inventor.Document drawing,
        System.Collections.Generic.IReadOnlyList<TitleBlockField> fields, ref int set, ref int created)
    {
        foreach (var field in fields)
        {
            var existing = FindProperty(drawing, field.Name);
            try
            {
                if (existing != null) { existing.Value = field.Value; set++; }
                else { UserDefinedSet(drawing).Add(field.Value, field.Name); created++; }
            }
            catch (Exception ex)
            {
                // A typed property (a date, a count) rejects a string; naming the field turns that
                // into something the caller can act on instead of an opaque API_ERROR.
                throw new CodedFailureException(InventorErrorCodes.TITLE_BLOCK_FIELD_REJECTED,
                    "The title block field '" + field.Name + "' could not be written: " + ex.Message,
                    new JObject { ["field"] = field.Name });
            }
        }
    }

    private static Property? FindProperty(global::Inventor.Document drawing, string name)
    {
        foreach (PropertySet propertySet in drawing.PropertySets)
        {
            var found = PropertyAccess.FindProperty(propertySet, name);
            if (found != null) return found;
        }
        return null;
    }

    private static PropertySet UserDefinedSet(global::Inventor.Document drawing)
        => PropertyAccess.FindSet(drawing, "Inventor User Defined Properties")
           ?? PropertyAccess.FindSet(drawing, "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}")
           ?? drawing.PropertySets.Add("Inventor User Defined Properties");

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

    // Only reached for base views (AddBaseView branch): Front, Back and Iso. Top, Bottom, Left and
    // Right are projected views whose orientation AddProjectedView derives from the direction to
    // their parent, so those arms would be dead code here.
    private static ViewOrientationTypeEnum Orientation(ViewKind kind)
    {
        switch (kind)
        {
            case ViewKind.Front: return ViewOrientationTypeEnum.kFrontViewOrientation;
            case ViewKind.Back: return ViewOrientationTypeEnum.kBackViewOrientation;
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
            var style = drawing.StylesManager.ActiveStandardStyle;
            if (style.StyleLocation == StyleLocationEnum.kLibraryStyleLocation)
            {
                // A library-only style must gain a document copy before it is edited (mirrors
                // SetSheetMetalRuleHandler's guard): writing FirstAngleProjection straight onto
                // ActiveStandardStyle here would edit it in place, and that write is not part of the
                // document transaction, so transaction.Abort() on a preview would NOT undo it. That
                // would let preview=true, documented as leaving nothing behind, permanently flip the
                // projection convention for every future drawing on this machine.
                style.ConvertToLocal();
                style = drawing.StylesManager.ActiveStandardStyle;
            }
            style.FirstAngleProjection = projection == ProjectionAngle.First;
        }
        catch (Exception ex)
        {
            throw new CodedFailureException(InventorErrorCodes.PROJECTION_UNAVAILABLE,
                "The drawing standard does not accept a projection change: " + ex.Message);
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
