#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class DrawingX
{
    public static Sheet SheetNamed(DrawingDocument drawing, string name)
    {
        foreach (Sheet sheet in drawing.Sheets)
            if (string.Equals(sheet.Name, name, StringComparison.Ordinal)) return sheet;
        throw new ArgumentException("No sheet named '" + name + "' (names look like 'Sheet:1').");
    }

    public static DrawingView ViewNamed(DrawingDocument drawing, string name)
    {
        foreach (DrawingView view in drawing.ActiveSheet.DrawingViews)
            if (string.Equals(view.Name, name, StringComparison.Ordinal)) return view;
        throw new ArgumentException("The active sheet has no view named '" + name + "'.");
    }

    public static JObject View(DrawingView view) => new()
    {
        ["view"] = view.Name,
        ["center_mm"] = new JArray(UnitConvert.CmToMm(view.Position.X), UnitConvert.CmToMm(view.Position.Y)),
        ["size_mm"] = new JArray(UnitConvert.CmToMm(view.Width), UnitConvert.CmToMm(view.Height)),
        ["scale"] = view.Scale,
    };

    public static DrawingSheetSizeEnum Size(string? name) => (name ?? "A3").ToUpperInvariant() switch
    {
        "A4" => DrawingSheetSizeEnum.kA4DrawingSheetSize,
        "A3" => DrawingSheetSizeEnum.kA3DrawingSheetSize,
        "A2" => DrawingSheetSizeEnum.kA2DrawingSheetSize,
        "A1" => DrawingSheetSizeEnum.kA1DrawingSheetSize,
        "A0" => DrawingSheetSizeEnum.kA0DrawingSheetSize,
        _ => throw new ArgumentException("size must be A4, A3, A2, A1 or A0."),
    };

    public static ViewOrientationTypeEnum Orientation(string? name) => (name ?? "front").ToLowerInvariant() switch
    {
        "front" => ViewOrientationTypeEnum.kFrontViewOrientation,
        "back" => ViewOrientationTypeEnum.kBackViewOrientation,
        "top" => ViewOrientationTypeEnum.kTopViewOrientation,
        "bottom" => ViewOrientationTypeEnum.kBottomViewOrientation,
        "left" => ViewOrientationTypeEnum.kLeftViewOrientation,
        "right" => ViewOrientationTypeEnum.kRightViewOrientation,
        "iso" => ViewOrientationTypeEnum.kIsoTopRightViewOrientation,
        _ => throw new ArgumentException("orientation must be front, back, top, bottom, left, right or iso."),
    };

    public static DrawingViewStyleEnum Style(string? name) => (name ?? "hidden").ToLowerInvariant() switch
    {
        "hidden" => DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle,
        "visible" => DrawingViewStyleEnum.kHiddenLineDrawingViewStyle,
        "shaded" => DrawingViewStyleEnum.kShadedDrawingViewStyle,
        _ => throw new ArgumentException("style must be hidden, visible or shaded."),
    };

    /// <summary>Findings of the drawing check, shared by validate_drawing and the drawing_references validator.</summary>
    public static JArray Findings(DrawingDocument drawing)
    {
        var findings = new JArray();
        void Add(string severity, string code, string message, string? sheet = null, string? view = null)
            => findings.Add(new JObject { ["severity"] = severity, ["code"] = code, ["message"] = message, ["sheet"] = sheet, ["view"] = view });
        foreach (Sheet sheet in drawing.Sheets)
        {
            var views = sheet.DrawingViews.Cast<DrawingView>().ToArray();
            if (views.Length == 0) Add("warning", "SHEET_EMPTY", "The sheet has no views.", sheet.Name);
            bool assemblyShown = false;
            foreach (var view in views)
            {
                try
                {
                    var descriptor = view.ReferencedDocumentDescriptor;
                    if (descriptor == null) Add("error", "VIEW_WITHOUT_MODEL", "The view references no model.", sheet.Name, view.Name);
                    else
                    {
                        if (descriptor.ReferenceMissing) Add("error", "MODEL_REFERENCE_MISSING", "The view's model file is missing.", sheet.Name, view.Name);
                        if (descriptor.ReferencedDocumentType == DocumentTypeEnum.kAssemblyDocumentObject) assemblyShown = true;
                    }
                }
                catch (Exception ex) { Add("error", "MODEL_REFERENCE_UNREADABLE", ex.Message, sheet.Name, view.Name); }
                double left = view.Position.X - view.Width / 2, right = view.Position.X + view.Width / 2;
                double bottom = view.Position.Y - view.Height / 2, top = view.Position.Y + view.Height / 2;
                if (left < 0 || bottom < 0 || right > sheet.Width || top > sheet.Height)
                    Add("error", "VIEW_OUTSIDE_SHEET", "The view extends beyond the sheet.", sheet.Name, view.Name);
            }
            for (int i = 0; i < views.Length; i++)
                for (int j = i + 1; j < views.Length; j++)
                {
                    var a = views[i];
                    var b = views[j];
                    bool overlapX = Math.Abs(a.Position.X - b.Position.X) * 2 < a.Width + b.Width;
                    bool overlapY = Math.Abs(a.Position.Y - b.Position.Y) * 2 < a.Height + b.Height;
                    if (overlapX && overlapY) Add("warning", "VIEW_OVERLAP", "Views " + a.Name + " and " + b.Name + " overlap.", sheet.Name, a.Name);
                }
            if (assemblyShown && sheet.PartsLists.Count == 0)
                Add("warning", "PARTS_LIST_MISSING", "An assembly is drawn without a parts list on this sheet.", sheet.Name);
        }
        try { if (((dynamic)drawing).RequiresUpdate) Add("warning", "DRAWING_NEEDS_UPDATE", "The drawing needs an update."); } catch { }
        return findings;
    }
}

public sealed class AddSheetHandler : ExperimentalHandler
{
    public override string Name => "add_sheet";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var drawing = X.ActiveDrawing(app, Name);
        var orientation = ((string?)p["orientation"] ?? "landscape").ToLowerInvariant() switch
        {
            "landscape" => PageOrientationTypeEnum.kLandscapePageOrientation,
            "portrait" => PageOrientationTypeEnum.kPortraitPageOrientation,
            _ => throw new ArgumentException("orientation must be landscape or portrait."),
        };
        dynamic sheets = drawing.Sheets;
        Sheet sheet = sheets.Add(DrawingX.Size((string?)p["size"]), orientation);
        if (!string.IsNullOrWhiteSpace((string?)p["name"])) ((dynamic)sheet).Name = ((string)p["name"]!).Trim();
        return new JObject { ["sheet"] = sheet.Name, ["sheet_count"] = drawing.Sheets.Count };
    }
}

public sealed class ActivateSheetHandler : ExperimentalHandler
{
    public override string Name => "activate_sheet";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var drawing = X.ActiveDrawing(app, Name);
        DrawingX.SheetNamed(drawing, X.Str(p, "sheet")).Activate();
        return new JObject { ["active_sheet"] = drawing.ActiveSheet.Name };
    }
}

public sealed class DeleteSheetHandler : ExperimentalHandler
{
    public override string Name => "delete_sheet";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var drawing = X.ActiveDrawing(app, Name);
        if (drawing.Sheets.Count <= 1) throw new ArgumentException("The last sheet of a drawing cannot be deleted.");
        string name = X.Str(p, "sheet");
        DrawingX.SheetNamed(drawing, name).Delete();
        return new JObject { ["deleted"] = name, ["sheet_count"] = drawing.Sheets.Count };
    }
}

public sealed class AddBaseViewHandler : ExperimentalHandler
{
    public override string Name => "add_base_view";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var drawing = X.ActiveDrawing(app, Name);
        var source = X.Document(app, X.Str(p, "document_id"));
        if (source is not (PartDocument or AssemblyDocument)) throw new ArgumentException("A base view needs a part or assembly document.");
        double scale = X.Num(p, "scale", 1);
        if (scale <= 0 || scale > 1000) throw new ArgumentException("scale must be in (0, 1000].");
        var view = drawing.ActiveSheet.DrawingViews.AddBaseView((_Document)source, X.P2(app, X.Num(p, "x_mm"), X.Num(p, "y_mm")),
            scale, DrawingX.Orientation((string?)p["orientation"]), DrawingX.Style((string?)p["style"]));
        return DrawingX.View(view);
    }
}

public sealed class AddProjectedViewHandler : ExperimentalHandler
{
    public override string Name => "add_projected_view";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var drawing = X.ActiveDrawing(app, Name);
        var parent = DrawingX.ViewNamed(drawing, X.Str(p, "parent_view"));
        var view = drawing.ActiveSheet.DrawingViews.AddProjectedView(parent, X.P2(app, X.Num(p, "x_mm"), X.Num(p, "y_mm")),
            DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle);
        return DrawingX.View(view);
    }
}

public sealed class MoveDrawingViewHandler : ExperimentalHandler
{
    public override string Name => "move_drawing_view";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var view = DrawingX.ViewNamed(X.ActiveDrawing(app, Name), X.Str(p, "view"));
        view.Position = X.P2(app, X.Num(p, "x_mm"), X.Num(p, "y_mm"));
        return DrawingX.View(view);
    }
}

public sealed class SetViewScaleHandler : ExperimentalHandler
{
    public override string Name => "set_view_scale";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var view = DrawingX.ViewNamed(X.ActiveDrawing(app, Name), X.Str(p, "view"));
        double scale = X.Positive(p, "scale");
        if (scale > 1000) throw new ArgumentException("scale must be at most 1000.");
        view.Scale = scale;
        return DrawingX.View(view);
    }
}

public sealed class AddNoteHandler : ExperimentalHandler
{
    public override string Name => "add_note";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var drawing = X.ActiveDrawing(app, Name);
        string text = X.Str(p, "text");
        if (text.Length > 2000) throw new ArgumentException("text is limited to 2000 characters.");
        // Plain text only: Inventor's formatted-text markup is escaped, never interpreted.
        string escaped = System.Security.SecurityElement.Escape(text) ?? "";
        dynamic notes = drawing.ActiveSheet.DrawingNotes.GeneralNotes;
        notes.AddFitted(X.P2(app, X.Num(p, "x_mm"), X.Num(p, "y_mm")), escaped);
        return new JObject { ["sheet"] = drawing.ActiveSheet.Name, ["text"] = text };
    }
}

public sealed class AddPartsListHandler : ExperimentalHandler
{
    public override string Name => "add_parts_list";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var drawing = X.ActiveDrawing(app, Name);
        var view = DrawingX.ViewNamed(drawing, X.Str(p, "view"));
        dynamic lists = drawing.ActiveSheet.PartsLists;
        lists.Add(view, X.P2(app, X.Num(p, "x_mm"), X.Num(p, "y_mm")));
        return new JObject { ["sheet"] = drawing.ActiveSheet.Name, ["parts_lists"] = drawing.ActiveSheet.PartsLists.Count };
    }
}

/// <summary>validate_drawing: read-only drawing checks (plan §13.7).</summary>
public sealed class ValidateDrawingHandler : ExperimentalHandler
{
    public override string Name => "validate_drawing";
    public override bool IsReadOnly => true;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var drawing = X.ActiveDrawing(app, Name);
        var findings = DrawingX.Findings(drawing);
        return new JObject
        {
            ["valid"] = !findings.Any(f => (string?)f["severity"] == "error"),
            ["sheet_count"] = drawing.Sheets.Count,
            ["findings"] = findings,
        };
    }
}

/// <summary>Validation checks that ride on the experimental build (see InventorBatchBackend.SupportsCheck).</summary>
internal static class ExperimentalValidators
{
    public static void Run(global::Inventor.Document doc, string check)
    {
        switch (check)
        {
            case ValidationSpec.SketchFullyConstrained:
                if (doc is not PartDocument part) return;
                foreach (PlanarSketch sketch in part.ComponentDefinition.Sketches)
                {
                    string status = sketch.ConstraintStatus.ToString();
                    if (status != "kFullyConstrainedConstraintStatus")
                        throw new CodedFailureException(InventorErrorCodes.VALIDATION_FAILED, "Sketch " + sketch.Name + " is not fully constrained (" + status + ").",
                            new JObject { ["check"] = check, ["sketch"] = sketch.Name, ["status"] = status });
                }
                return;
            case ValidationSpec.DrawingReferences:
                if (doc is not DrawingDocument drawing) return;
                var errors = DrawingX.Findings(drawing).Where(f => (string?)f["code"] is "MODEL_REFERENCE_MISSING" or "VIEW_WITHOUT_MODEL" or "MODEL_REFERENCE_UNREADABLE").ToArray();
                if (errors.Length > 0)
                    throw new CodedFailureException(InventorErrorCodes.VALIDATION_FAILED, errors.Length + " drawing reference problem(s).",
                        new JObject { ["check"] = check, ["findings"] = new JArray(errors) });
                return;
        }
    }
}
#endif
