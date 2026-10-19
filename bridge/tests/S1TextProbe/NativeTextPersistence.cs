using System.Text.Json.Nodes;
using System.Xml.Linq;
using Inventor;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void RunPersistence(bool sheet, JsonObject context, string outputDirectory)
    {
        Phase = "persistence:fixture";
        var row = new JsonObject { ["whole_process_restart"] = "NOT COVERED" }; context["save_copy"] = row;
        var doc = NewDocument(sheet, "persistence");
        PartDocument? copy = null;
        string directory = Path.Combine(outputDirectory, "generated-text-" + (sheet ? "sheet" : "part") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.GetFullPath(Path.Combine(directory, "text-copy.ipt"));
        Require(Path.GetDirectoryName(path) == Path.GetFullPath(directory) && !File.Exists(path), "SaveCopy escaped new owned directory.");
        row["generated_copy_path"] = path;
        row["generated_copy_disposition"] = "Retained as generated native research evidence; no user source or template is saved.";
        try
        {
            GuardApplication(); var sketch = doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);
            sketch.Name = "S1_Text59_Persistent";
            var text = sketch.TextBoxes.AddFitted(Point(10, 20), "<StyleOverride FontSize='0.4' Bold='True'>Persist59</StyleOverride>", Type.Missing);
            text.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextCenter;
            text.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextMiddle;
            text.Rotation = Math.PI / 2; text.WidthScale = 1.1;
            var rectangle = sketch.TextBoxes.AddByRectangle(Point(10, 50), Point(80, 30), "First59<Br/>Second59", Type.Missing);
            doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1TextLength", "10 mm", "mm");
            var parameterText = sketch.TextBoxes.AddFitted(Point(10, 70),
                "Length=<Parameter Name='S1TextLength' Precision='0'>S1TextLength</Parameter>", Type.Missing);
            doc.Update2(false);
            var textBefore = ReadText(text); var rectangleBefore = ReadText(rectangle);
            var parameterBefore = ReadText(parameterText); var original = ReadDocument(doc);
            row["text_before"] = textBefore.DeepClone(); row["rectangle_before"] = rectangleBefore.DeepClone();
            row["parameter_text_before"] = parameterBefore.DeepClone();
            Phase = "persistence:save_copy"; GuardApplication(); doc.SaveAs(path, true);
            row["original_preserved_after_save"] = JsonNode.DeepEquals(original, ReadDocument(doc));
            Require((bool?)row["original_preserved_after_save"] == true, "SaveCopy modified original owned document.");
            Require(File.Exists(path), "SaveCopy did not create file.");
            GuardApplication(); var opened = (PartDocument)App!.Documents.Open(path, false);
            Require(!SameCom(opened, doc), "Open returned original instead of generated saved copy; refusing ownership registration and closure.");
            Require(string.Equals(Path.GetFullPath(opened.FullFileName), path, StringComparison.OrdinalIgnoreCase),
                "Open returned a different filename; refusing ownership registration and closure.");
            copy = opened; OwnedDocuments.Add((Document)copy);
            GuardApplication(); Phase = "persistence:read_copy";
            var copiedSketch = copy.ComponentDefinition.Sketches["S1_Text59_Persistent"];
            Require(copiedSketch.TextBoxes.Count == 3, "Reopened text count differs.");
            var actualText = ReadText(copiedSketch.TextBoxes[1]); var actualRectangle = ReadText(copiedSketch.TextBoxes[2]);
            row["text_reopened"] = actualText.DeepClone(); row["rectangle_reopened"] = actualRectangle.DeepClone();
            // Copy identity/reference namespace is recorded, not assumed equal to original.
            textBefore.Remove("key"); actualText.Remove("key"); rectangleBefore.Remove("key"); actualRectangle.Remove("key");
            row["text_properties_and_facets_equal"] = JsonNode.DeepEquals(textBefore, actualText);
            row["rectangle_properties_and_facets_equal"] = JsonNode.DeepEquals(rectangleBefore, actualRectangle);
            Require((bool?)row["text_properties_and_facets_equal"] == true && (bool?)row["rectangle_properties_and_facets_equal"] == true,
                "Reopened text geometry or properties differ.");
            var reopenedParameterText = copiedSketch.TextBoxes[3];
            var parameterAfter = ReadText(reopenedParameterText);
            row["parameter_text_reopened"] = parameterAfter.DeepClone();
            parameterBefore.Remove("key"); parameterAfter.Remove("key");
            var beforeMarkup=XElement.Parse("<root>"+(string)parameterBefore["formatted_text"]!+"</root>");
            var afterMarkup=XElement.Parse("<root>"+(string)parameterAfter["formatted_text"]!+"</root>");
            var originalBinding=beforeMarkup.Descendants("Parameter").Single();
            var copiedBinding=afterMarkup.Descendants("Parameter").Single();
            Require((string?)originalBinding.Attribute("Name")=="S1TextLength"&&(string?)copiedBinding.Attribute("Name")=="S1TextLength"
                &&(string?)copiedBinding.Attribute("Resolved")=="True","Saved text parameter binding differs.");
            string copiedOwner=(string?)copiedBinding.Attribute("ComponentIdentifier") ?? "";
            Require(string.Equals(Path.GetFullPath(copiedOwner),path,StringComparison.OrdinalIgnoreCase),"Saved text does not identify the exact exclusively owned copy.");
            copiedBinding.SetAttributeValue("ComponentIdentifier",(string?)originalBinding.Attribute("ComponentIdentifier"));
            parameterBefore["formatted_text"]=beforeMarkup.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            parameterAfter["formatted_text"]=afterMarkup.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            row["parameter_owner_identifier_verified"]=true;
            row["parameter_owner_normalization"]="Only the verified owner identifier changes from unsaved original to exact generated copy path; all other properties/facets compared.";
            row["parameter_properties_and_facets_equal"] = JsonNode.DeepEquals(parameterBefore, parameterAfter);
            Require((bool?)row["parameter_properties_and_facets_equal"] == true, "Reopened parameter text geometry or properties differ.");
            string key = Key(reopenedParameterText);
            var updates = new JsonArray(); row["reopened_parameter_updates"] = updates;
            foreach (int mm in new[] { 22, 7 })
            {
                GuardApplication(); copy.ComponentDefinition.Parameters.UserParameters["S1TextLength"].Expression = mm + " mm";
                copy.Update2(false); var state = ReadText(reopenedParameterText); state["source_mm"] = mm; updates.Add(state);
                Require(Key(reopenedParameterText) == key && ContainsNumber(reopenedParameterText.Text, mm),
                    "Reopened parameter text does not follow the saved native binding.");
                Require(JsonNode.DeepEquals(original, ReadDocument(doc)), "Updating the copy changed original document.");
            }
            row["reopened_two_source_updates_verified"] = true;
            CloseOwned((Document)copy); copy = null;
            row["original_preserved_after_reopen_close"] = JsonNode.DeepEquals(original, ReadDocument(doc));
            Require((bool?)row["original_preserved_after_reopen_close"] == true, "Copy lifecycle modified original.");
            row["saved_copy_path"] = path; row["saved_copy_bytes"] = new FileInfo(path).Length; row["status"] = "PASS";
        }
        catch (Exception ex) { row["status"] = "FAIL"; row["failure"] = Error(ex); }
        finally
        {
            if (copy != null) CloseOwned((Document)copy);
            CloseOwned((Document)doc); row["fixtures_closed_unsaved"] = true;
        }
    }
}
