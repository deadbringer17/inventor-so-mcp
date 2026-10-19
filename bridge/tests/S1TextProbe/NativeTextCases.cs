using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Text.RegularExpressions;
using Inventor;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void RunContext(bool sheet, JsonArray contexts, string outputDirectory)
    {
        var context = new JsonObject { ["kind"] = sheet ? "sheetmetal" : "part" };
        contexts.Add(context);
        var cases = new JsonArray(); context["cases"] = cases;
        RunCase(sheet, "fitted_default", cases, false, (doc, sketch, row) =>
        {
            var text = sketch.TextBoxes.AddFitted(Point(10, 20), "S1 native text 59", Type.Missing);
            row["created"] = ReadText(text);
            Require(text.Text == "S1 native text 59" && text.Fitted, "Fitted literal does not round-trip.");
            Require(SameCom(text.Parent, sketch), "Text owner differs.");
            text.FormattedText = "S1 edited literal 59"; doc.Update2(false);
            row["edited"] = ReadText(text);
            Require(text.Text == "S1 edited literal 59", "Literal edit not read back.");
            row["identity_after_edit"] = Key(text);
        });
        RunCase(sheet, "rectangle_multiline_default", cases, false, (doc, sketch, row) =>
        {
            var text = sketch.TextBoxes.AddByRectangle(Point(10, 30), Point(70, 10), "First line<Br/>Second line", Type.Missing);
            doc.Update2(false); row["created"] = ReadText(text);
            Require(!text.Fitted && text.Text.Contains("First line") && text.Text.Contains("Second line"), "Rectangle/multiline native text missing.");
            text.Width = 8; text.Height = 3; doc.Update2(false);
            row["resized"] = ReadText(text);
            Require(Math.Abs(text.Width - 8) < 1e-7 && Math.Abs(text.Height - 3) < 1e-7, "Native rectangle size not retained.");
        });
        RunCase(sheet, "format_and_placement", cases, false, (doc, sketch, row) =>
        {
            Phase = "format_and_placement:add_fitted";
            var text = sketch.TextBoxes.AddFitted(Point(10, 20), "A&amp;B &lt;C&gt;", Type.Missing);
            string key = Key(text); row["created"] = ReadText(text);
            Require(text.Text == "A&B <C>", "Escaped literal does not round-trip.");
            Phase = "format_and_placement:set_formatted_text";
            text.FormattedText = "<StyleOverride FontSize='0.5' Bold='True' Italic='True'>Styled 59</StyleOverride>";
            row["after_text_edit"] = ReadText(text);
            Phase = "format_and_placement:set_horizontal";
            text.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextCenter;
            Phase = "format_and_placement:set_vertical";
            text.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextMiddle;
            Phase = "format_and_placement:set_origin";
            text.Origin = Point(35, 45);
            Phase = "format_and_placement:set_width_scale";
            text.WidthScale = 1.25;
            Phase = "format_and_placement:set_rotation";
            text.Rotation = 37 * Math.PI / 180;
            Phase = "format_and_placement:update_readback";
            doc.Update2(false); row["edited"] = ReadText(text);
            Require(Key(text) == key && text.Text == "Styled 59", "Text identity or edited content differs.");
            Require(Math.Abs(text.Origin.X - 3.5) < 1e-7 && Math.Abs(text.Origin.Y - 4.5) < 1e-7, "Native origin differs.");
            Require(Math.Abs(text.Rotation - 37 * Math.PI / 180) < 1e-10 && Math.Abs(text.WidthScale - 1.25) < 1e-10, "Native override property differs.");
            row["rotation_limitation"] = "Property readback only; facet coordinates recorded, but no independent desktop glyph-orientation oracle.";
        });
        RunCase(sheet, "local_style_copy", cases, false, (doc, sketch, row) =>
        {
            var text = sketch.TextBoxes.AddFitted(Point(10, 20), "Local style 59", Type.Missing);
            var original = text.Style; var originalBefore = ReadStyle(original);
            row["original_style"] = originalBefore.DeepClone();
            Phase = "local_style_copy:copy_style";
            var copy = (TextStyle)original.Copy("S1_Text59_" + Guid.NewGuid().ToString("N"));
            // All mutations are to the new style in the unsaved owned fixture.
            // SaveToGlobal/UpdateFromGlobal/ConvertToLocal are deliberately never called.
            row["copied_style_initial"] = ReadStyle(copy);
            Phase = "local_style_copy:set_font_size"; copy.FontSize = .4;
            Phase = "local_style_copy:set_bold"; copy.Bold = true;
            Phase = "local_style_copy:set_italic"; copy.Italic = true;
            Phase = "local_style_copy:assign_style"; text.Style = copy;
            Phase = "local_style_copy:update_readback"; doc.Update2(false); row["edited"] = ReadText(text);
            Require(!SameCom(original, copy), "Style copy returned original.");
            Require(JsonNode.DeepEquals(originalBefore, ReadStyle(original)), "Original template style was modified.");
            Require(Math.Abs(text.Style.FontSize - .4) < 1e-10 && text.Style.Bold && text.Style.Italic, "Copied style not applied.");
            row["original_style_unchanged"] = true;
        });
        RunCase(sheet, "parameter_local_tag", cases, true, (doc, sketch, row) =>
            ParameterCase(doc, sketch, row, "Length=<Parameter Name='S1TextLength' Precision='0'>S1TextLength</Parameter>"));
        RunCase(sheet, "parameter_component_empty_tag", cases, true, (doc, sketch, row) =>
            ParameterCase(doc, sketch, row, "Length=<Parameter ComponentIdentifier='' Name='S1TextLength' Precision='0'>S1TextLength</Parameter>"));
        RunCase(sheet, "custom_property_tag", cases, true, PropertyCase);
        RunCase(sheet, "parameter_exported_property_tag", cases, true, (doc, sketch, row) =>
        {
            var parameter = doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1TextLength", "10 mm", "mm");
            parameter.ExposedAsProperty = true;
            doc.Update2(false);
            var properties = doc.PropertySets["{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"];
            row["exported_property_names"] = JsonSerializer.SerializeToNode(properties.Cast<Property>().Select(p => p.Name).ToArray());
            var property = properties["S1TextLength"];
            string tag = PropertyTag(property, "S1TextLength");
            var text = sketch.TextBoxes.AddFitted(Point(10, 20), tag, Type.Missing); doc.Update2(false);
            row["created"] = ReadText(text); string previous = text.Text, key = Key(text);
            var updates = new JsonArray(); row["updates"] = updates;
            foreach (int mm in new[] { 22, 7 })
            {
                parameter.Expression = mm + " mm"; doc.Update2(false);
                var state = ReadText(text); state["parameter_mm"] = (double)parameter.Value * 10;
                state["exported_property_value"] = Convert.ToString(property.Value);
                state["text_changed"] = text.Text != previous; updates.Add(state);
                Require(Key(text) == key, "Exported parameter update changed text identity.");
                RequireAssociation(text.Text != previous && ContainsNumber(text.Text, mm), "Exported parameter property did not update text to the source value."); previous = text.Text;
            }
            row["two_source_updates_verified"] = true;
        });
        RunOptionCases(sheet,cases);
        RunPersistence(sheet, context, outputDirectory);
        context["case_count"] = cases.Count;
        context["passed"] = cases.OfType<JsonObject>().Count(r => (string?)r["status"] == "PASS");
        context["refused_api"] = cases.OfType<JsonObject>().Count(r => (string?)r["status"] == "REFUSED_API");
        context["not_associative"] = cases.OfType<JsonObject>().Count(r => (string?)r["status"] == "NOT_ASSOCIATIVE");
        context["failed"] = cases.OfType<JsonObject>().Count(r => (string?)r["status"] == "FAIL");
        context["rollback_exact"] = cases.OfType<JsonObject>().Count(r => (bool?)r["rollback_exact"] == true);
    }

    private static PartDocument NewDocument(bool sheet, string label)
    {
        GuardApplication(); Require(OwnedDocuments.Count == 0, "A case must start without owned documents.");
        string template = sheet ? App!.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,
            SystemOfMeasureEnum.kDefaultSystemOfMeasure, DraftingStandardEnum.kDefault_DraftingStandard,
            "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}") : App!.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
        var doc = (PartDocument)App.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
        OwnedDocuments.Add((Document)doc);
        GuardApplication(); doc.DisplayName = "S1_Text59_" + label + ".ipt";
        Require(string.IsNullOrEmpty(doc.FullFileName), "Fresh document is unexpectedly saved.");
        Require((doc.ComponentDefinition is SheetMetalComponentDefinition) == sheet, "Template context differs.");
        return doc;
    }
    private static Point2d Point(double xMm, double yMm) => App!.TransientGeometry.CreatePoint2d(xMm / 10, yMm / 10);
    private static void CloseOwned(Document doc)
    {
        GuardApplication();
        var owned = OwnedDocuments.SingleOrDefault(d => SameCom(d, doc));
        Require(owned != null, "Cannot close unowned document.");
        doc.Close(true); OwnedDocuments.Remove(owned!);
    }

    private static void RunCase(bool sheet, string name, JsonArray cases, bool exploratory, Action<PartDocument, PlanarSketch, JsonObject> execute)
    {
        Phase = name + ":fixture";
        var row = new JsonObject { ["name"] = name, ["exploratory_format_syntax"] = exploratory }; cases.Add(row);
        var doc = NewDocument(sheet, name); Transaction? transaction = null;
        try
        {
            GuardApplication(); var sketch = doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);
            sketch.Name = "S1_Text59"; doc.Update2(false);
            var baseline = ReadDocument(doc); row["before"] = baseline.DeepClone();
            GuardApplication(); transaction = App!.TransactionManager.StartTransaction((_Document)doc, "S1 text59 research, abort");
            try
            {
                Phase = name + ":native"; GuardApplication(); execute(doc, sketch, row);
                doc.Update2(false); row["result"] = ReadDocument(doc); row["status"] = "PASS";
            }
            catch (Exception ex)
            {
                row["status"] = exploratory && ex is System.Runtime.InteropServices.COMException ? "REFUSED_API"
                    : exploratory && ex is AssociationFailure ? "NOT_ASSOCIATIVE" : "FAIL";
                row["failure"] = Error(ex);
            }
            finally
            {
                Phase = name + ":abort"; GuardApplication(); transaction.Abort(); transaction = null;
                var after = ReadDocument(doc); row["after_abort"] = after.DeepClone();
                row["rollback_exact"] = JsonNode.DeepEquals(baseline, after);
                if ((bool?)row["rollback_exact"] != true) { row["status"] = "FAIL"; row["rollback_difference"] = "Exact document/style/parameter/reference snapshot differs; fresh fixture will be discarded."; }
            }
        }
        finally
        {
            if (transaction != null) { GuardApplication(); transaction.Abort(); }
            Phase = name + ":close"; CloseOwned((Document)doc); row["fixture_closed_unsaved"] = true;
        }
    }
    private static void ParameterCase(PartDocument doc, PlanarSketch sketch, JsonObject row, string formatted)
    {
        var parameter = doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1TextLength", "10 mm", "mm");
        var text = sketch.TextBoxes.AddFitted(Point(10, 20), formatted, Type.Missing);
        doc.Update2(false); row["created"] = ReadText(text); string previous = text.Text, key = Key(text);
        var updates = new JsonArray(); row["updates"] = updates;
        foreach (int mm in new[] { 22, 7 })
        {
            parameter.Expression = mm + " mm"; doc.Update2(false);
            var state = ReadText(text); state["parameter_mm"] = (double)parameter.Value * 10;
            state["text_changed"] = text.Text != previous; updates.Add(state);
            Require(Key(text) == key, "Native parameter update changed text identity.");
            RequireAssociation(text.Text != previous && ContainsNumber(text.Text, mm), "Native parameter tag does not update text to the source value.");
            previous = text.Text;
        }
        row["two_source_updates_verified"] = true;
    }
    private static bool ContainsNumber(string text, double expected) =>
        Regex.Matches(text, @"[-+]?\d+(?:[.,]\d+)?")
            .Any(m => double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && Math.Abs(value - expected) < 1e-8);
    private static string PropertyTag(Property property, string name) =>
        "<Property Document='model' PropertySet='Inventor User Defined Properties' Property='" + name +
        "' FormatID='{D5CDD505-2E9C-101B-9397-08002B2CF9AE}' PropertyID='" + property.PropId + "'>" + name + "</Property>";
    private static void PropertyCase(PartDocument doc, PlanarSketch sketch, JsonObject row)
    {
        var property = doc.PropertySets["{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"].Add("Alpha59", "S1TextLabel");
        var text = sketch.TextBoxes.AddFitted(Point(10, 20), PropertyTag(property, "S1TextLabel"), Type.Missing);
        doc.Update2(false); row["created"] = ReadText(text);
        RequireAssociation(text.Text.Contains("Alpha59"), "Custom property was not resolved initially."); string key = Key(text);
        var updates = new JsonArray(); row["updates"] = updates;
        foreach (string value in new[] { "Beta59", "Gamma59" })
        {
            property.Value = value; doc.Update2(false); updates.Add(ReadText(text));
            Require(Key(text) == key, "Custom property update changed text identity.");
            RequireAssociation(text.Text.Contains(value), "Custom property source update not reflected in same native text.");
        }
        row["two_source_updates_verified"] = true;
    }

    private static JsonObject ReadStyle(TextStyle style) => new()
    {
        ["name"] = style.Name, ["internal_name"] = style.InternalName, ["location"] = style.StyleLocation.ToString(),
        ["font"] = style.Font, ["font_size_mm"] = style.FontSize * 10, ["bold"] = style.Bold,
        ["italic"] = style.Italic, ["underline"] = style.Underline, ["rotation_rad"] = style.Rotation,
        ["width_scale"] = style.WidthScale, ["horizontal"] = style.HorizontalJustification.ToString(),
        ["vertical"] = style.VerticalJustification.ToString(), ["line_spacing"] = style.LineSpacing,
        ["line_spacing_type"] = style.LineSpacingType.ToString()
    };
    private static string Key(TextBox text) { byte[] key = Array.Empty<byte>(); text.GetReferenceKey(ref key, 0); return Convert.ToBase64String(key); }
    private static JsonObject ReadText(TextBox text)
    {
        var result = new JsonObject
        {
            ["native_type"] = text.Type.ToString(), ["key"] = Key(text), ["text"] = text.Text, ["formatted_text"] = text.FormattedText,
            ["fitted"] = text.Fitted, ["width_mm"] = text.Width * 10, ["height_mm"] = text.Height * 10,
            ["fitted_width_mm"] = text.FittedTextWidth * 10, ["fitted_height_mm"] = text.FittedTextHeight * 10,
            ["origin_mm"] = new JsonArray(text.Origin.X * 10, text.Origin.Y * 10),
            ["horizontal"] = text.HorizontalJustification.ToString(), ["vertical"] = text.VerticalJustification.ToString(),
            ["rotation_rad"] = text.Rotation, ["width_scale"] = text.WidthScale, ["show_boundaries"] = text.ShowBoundaries,
            ["single_line"] = text.SingleLineText, ["line_spacing"] = text.LineSpacing,
            ["line_spacing_type"] = text.LineSpacingType.ToString(), ["style"] = ReadStyle(text.Style),
            ["range_mm"] = new JsonArray(text.RangeBox.MinPoint.X * 10, text.RangeBox.MinPoint.Y * 10, text.RangeBox.MaxPoint.X * 10, text.RangeBox.MaxPoint.Y * 10)
        };
        try
        {
            int vertices = 0, facets = 0; double[] coordinates = Array.Empty<double>(); int[] indices = Array.Empty<int>();
            text.GetFacetsInfo(out vertices, out facets, out coordinates, out indices);
            Require(coordinates.All(double.IsFinite), "Non-finite native text facets.");
            result["facets"] = new JsonObject { ["vertex_count"] = vertices, ["facet_count"] = facets,
                ["coordinate_count"] = coordinates.Length, ["index_count"] = indices.Length,
                ["coordinates_sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(coordinates)))),
                ["indices_sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(indices)))) };
        }
        catch (Exception ex) { result["facets_not_covered"] = Error(ex); }
        return result;
    }
    private static JsonObject ReadDocument(PartDocument doc)
    {
        var sketches = new JsonArray();
        foreach (PlanarSketch sketch in doc.ComponentDefinition.Sketches)
        {
            var texts = new JsonArray(); foreach (TextBox text in sketch.TextBoxes) texts.Add(ReadText(text));
            sketches.Add(new JsonObject { ["name"] = sketch.Name, ["entity_count"] = sketch.SketchEntities.Count,
                ["geometric_constraints"] = sketch.GeometricConstraints.Count, ["dimensions"] = sketch.DimensionConstraints.Count,
                ["text"] = texts, ["images"] = sketch.SketchImages.Count });
        }
        var styles = new JsonArray(); foreach (TextStyle style in doc.TextStyles) styles.Add(ReadStyle(style));
        var parameters = new JsonArray(); foreach (Parameter parameter in doc.ComponentDefinition.Parameters)
            parameters.Add(new JsonObject { ["name"] = parameter.Name, ["expression"] = parameter.Expression,
                ["value"] = JsonSerializer.SerializeToNode(parameter.Value), ["units"] = parameter.get_Units() });
        var properties = new JsonArray(); foreach (Property property in doc.PropertySets["{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"])
            properties.Add(new JsonObject { ["name"] = property.Name, ["value"] = Convert.ToString(property.Value), ["id"] = property.PropId });
        return new JsonObject { ["internal_name"] = doc.InternalName, ["database_revision"] = doc.DatabaseRevisionId,
            ["dirty"] = doc.Dirty, ["filename"] = doc.FullFileName, ["sketches"] = sketches,
            ["styles"] = styles, ["parameters"] = parameters, ["custom_properties"] = properties,
            ["ole_count"] = doc.ReferencedOLEFileDescriptors.Count };
    }
}
