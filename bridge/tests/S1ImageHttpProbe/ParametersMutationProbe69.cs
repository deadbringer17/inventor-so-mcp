using System.Globalization;
using System.Text.RegularExpressions;
using Inventor;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static void RunParameters69(PartDocument doc, PlanarSketch sketch, InventorBackend backend,
        ProbeTransport transport, PairedServer paired, JObject report, JArray cases, bool sheet)
    {
        const string sourceName = "S1Length69", unusedName = "S1Unused69", renamedName = "S1Renamed69";
        const double toleranceMm = 0.00001;
        string kind = sheet ? "sheetmetal" : "part";
        report["phase"] = kind + "_parameters69_fixture";
        var app = (Inventor.Application)sketch.Application;
        if (app.Documents.Count != 1 || doc.ComponentDefinition.Sketches.Count != 1
            || Identity(doc.ComponentDefinition.Sketches[1]) != Identity(sketch)
            || !string.IsNullOrEmpty(doc.FullFileName) || sketch.SketchEntities.Count != 0
            || sketch.GeometricConstraints.Count != 0 || sketch.DimensionConstraints.Count != 0
            || sketch.TextBoxes.Count != 0 || sketch.SketchImages.Count != 0 || doc.ReferencedOLEFileDescriptors.Count != 0
            || doc.ComponentDefinition.Parameters.Cast<Parameter>().Any(p => p.Name is sourceName or unusedName or renamedName))
            throw new ProbeFailure("Parameters69 requires the exact empty unsaved owned fixture.");

        var geometry = app.TransientGeometry;
        var source = doc.ComponentDefinition.Parameters.UserParameters.AddByExpression(sourceName, "10 mm", "mm");
        var line = sketch.SketchLines.AddByTwoPoints(geometry.CreatePoint2d(0, 0), geometry.CreatePoint2d(1, 0));
        sketch.GeometricConstraints.AddGround((SketchEntity)line.StartSketchPoint);
        sketch.GeometricConstraints.AddHorizontal((SketchEntity)line, true);
        var dimension = sketch.DimensionConstraints.AddTwoPointDistance(line.StartSketchPoint, line.EndSketchPoint,
            DimensionOrientationEnum.kHorizontalDim, geometry.CreatePoint2d(.5, -.5), false);
        dimension.Parameter.Expression = sourceName;
        var text = sketch.TextBoxes.AddFitted(geometry.CreatePoint2d(1, 2),
            "L=<Parameter Name='S1Length69' Precision='0'>S1Length69</Parameter>", Type.Missing);
        if (!doc.Rebuild2(false)) throw new ProbeFailure("Parameters69 fixture rebuild failed.");
        _ = ParameterGlyphs69(text); // Output arrays are initialized before the native call.
        string lineKey = Identity(line), dimensionKey = Identity(dimension), textKey = Identity(text);
        string sourceKey = Identity(source), dimensionParameterName = dimension.Parameter.Name;
        string documentId = Bimwright.Ipt.Shared.Contracts.DocumentIdComposer.Compose(doc.InternalName, doc.FullFileName, false);
        string sketchId = Reference56(documentId, sketch, "sketch");
        var mcp = new McpClient(transport, paired.BaseUrl, paired.Token);
        mcp.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        DocumentState state;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            try { state = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult(); break; }
            catch (McpToolException error) when (error.Code == "NO_TARGET" && DateTime.UtcNow < deadline) { Thread.Sleep(500); }
        }
        if (state.DocumentId != documentId) throw new ProbeFailure("Parameters69 host targets another document.");
        string staleRevision = state.Revision;
        var row = new JObject
        {
            ["kind"] = kind, ["render_confirmation"] = "Synthetic DesignSession ConfirmRendered only; no UI68, Unity, Quest or physical evidence.",
            ["numeric_tolerance_mm"] = toleranceMm, ["rollback_comparison"] = "Exact native values, styles, identities, geometry and glyph arrays.",
            ["workflows"] = new JArray(), ["source_updates"] = new JArray(), ["refusals"] = new JArray(),
            ["open_s1_gates"] = 18
        };
        cases.Add(row);
        JArray workflows = (JArray)row["workflows"]!, updates = (JArray)row["source_updates"]!, refusals = (JArray)row["refusals"]!;

        void CheckContext(double expectedMm, string? additionalParameter = null)
        {
            var before = ParametersState69(doc, sketch);
            var context = backend.GetDesignContextAsync(state, CancellationToken.None).GetAwaiter().GetResult();
            if (!SameState56(context.State, state) || context.Truncated)
                throw new ProbeFailure("Parameters69 revision-bound Design context is missing or partial.");
            var value = context.Parameters.OfType<JObject>().SingleOrDefault(p => (string?)p["name"] == sourceName);
            if (value == null || value["value_mm"]?.Type is not (JTokenType.Integer or JTokenType.Float)
                || Math.Abs((double)value["value_mm"]! - expectedMm) > toleranceMm
                || (string?)value["expression"] != source.Expression || (string?)value["unit"] != source.get_Units())
                throw new ProbeFailure("Parameters69 Design context parameter differs from the native source.");
            if (additionalParameter != null && context.Parameters.OfType<JObject>().Count(p => (string?)p["name"] == additionalParameter) != 1)
                throw new ProbeFailure("Parameters69 Design context omitted the created or renamed parameter.");
            if (!JToken.DeepEquals(before, ParametersState69(doc, sketch))
                || !SameState56(state, backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult()))
                throw new ProbeFailure("Parameters69 Design context read changed native state or revision.");
        }

        void CheckConsumers(double expectedMm, bool compareOracle)
        {
            report["phase"] = kind + "_parameters69_consumers_" + expectedMm.ToString(CultureInfo.InvariantCulture);
            source = doc.ComponentDefinition.Parameters.UserParameters[sourceName];
            line = sketch.SketchLines.Cast<SketchLine>().Single(l => Identity(l) == lineKey);
            dimension = sketch.DimensionConstraints.Cast<DimensionConstraint>().OfType<TwoPointDistanceDimConstraint>()
                .Single(d => Identity(d) == dimensionKey);
            text = sketch.TextBoxes.Cast<TextBox>().Single(t => Identity(t) == textKey);
            double dx = (line.EndSketchPoint.Geometry.X - line.StartSketchPoint.Geometry.X) * 10;
            double dy = (line.EndSketchPoint.Geometry.Y - line.StartSketchPoint.Geometry.Y) * 10;
            var field = System.Xml.Linq.XElement.Parse("<root>" + text.FormattedText + "</root>")
                .Descendants("Parameter").SingleOrDefault();
            if (Identity(source) != sourceKey || dimension.Parameter.Name != dimensionParameterName || dimension.Parameter.Expression != sourceName
                || Math.Abs(Convert.ToDouble(source.Value, CultureInfo.InvariantCulture) * 10 - expectedMm) > toleranceMm
                || Math.Abs(Math.Sqrt(dx * dx + dy * dy) - expectedMm) > toleranceMm
                || Math.Abs(Convert.ToDouble(dimension.Parameter.Value, CultureInfo.InvariantCulture) * 10 - expectedMm) > toleranceMm
                || !Regex.IsMatch(text.Text, "^L=" + expectedMm.ToString(CultureInfo.InvariantCulture) + "$")
                || field == null || field.Attribute("Name")?.Value != sourceName || field.Attribute("Resolved")?.Value != "True"
                || field.Attribute("Precision")?.Value != "0")
                throw new ProbeFailure("Parameters69 native consumers, identities or formatted binding differ.");

            var nativeBeforeRead = ParametersState69(doc, sketch);
            var snapshot = backend.GetNativeSketchAsync(state, sketchId, CancellationToken.None).GetAwaiter().GetResult();
            var representedLine = snapshot.Entities.Single(e => e.Reference?.EntityId == Reference56(documentId, line, "sketch_entity"));
            var representedDimension = snapshot.Dimensions.Single(e => e.Reference?.EntityId == Reference56(documentId, dimension, "sketch_dimension"));
            var representedText = snapshot.Entities.Single(e => e.Reference?.EntityId == Reference56(documentId, text, "sketch_text"));
            var glyphs = representedText.TextGlyphs;
            var frozenGlyphs = ParameterGlyphs69(text);
            if (!snapshot.NativeMutationComplete || snapshot.Partial
                || (string?)representedText.Data["text"] != text.Text || (string?)representedText.Data["formatted_text"] != text.FormattedText
                || Math.Abs((double?)representedDimension.Data["value_mm"] - expectedMm ?? double.PositiveInfinity) > toleranceMm
                || (string?)representedDimension.Data["expression"] != sourceName
                || representedLine.Data["start_mm"] is not JArray start || representedLine.Data["end_mm"] is not JArray end
                || (CadPoint.Parse(start, 2) - new CadPoint(line.StartSketchPoint.Geometry.X * 10, line.StartSketchPoint.Geometry.Y * 10)).Length > toleranceMm
                || (CadPoint.Parse(end, 2) - new CadPoint(line.EndSketchPoint.Geometry.X * 10, line.EndSketchPoint.Geometry.Y * 10)).Length > toleranceMm
                || glyphs == null || glyphs.VerticesMm.Count != (int)frozenGlyphs["vertices"]!
                || glyphs.Indices.Count != ((JArray)frozenGlyphs["indices"]!).Count)
                throw new ProbeFailure("Parameters69 production snapshot does not contain the actual native consumers.");
            var coordinates = ((JArray)frozenGlyphs["coordinates_cm"]!).Values<double>().ToArray();
            for (int i = 0; i < glyphs.VerticesMm.Count; i++)
                if ((glyphs.VerticesMm[i] - new CadPoint(coordinates[i * 3] * 10, coordinates[i * 3 + 1] * 10, coordinates[i * 3 + 2] * 10)).Length > toleranceMm)
                    throw new ProbeFailure("Parameters69 production glyph model coordinates differ.");
            if (!glyphs.Indices.SequenceEqual(((JArray)frozenGlyphs["indices"]!).Values<int>().Select(i => i - 1))
                || !JToken.DeepEquals(nativeBeforeRead, ParametersState69(doc, sketch))
                || !SameState56(state, backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult()))
                throw new ProbeFailure("Parameters69 snapshot topology or read-only comparison differs.");
            CheckContext(expectedMm);
            if (!compareOracle) return;

            var observation = new JObject { ["parameter_mm"] = expectedMm, ["line_mm"] = Math.Sqrt(dx * dx + dy * dy),
                ["dimension_mm"] = Convert.ToDouble(dimension.Parameter.Value, CultureInfo.InvariantCulture) * 10,
                ["text"] = text.Text, ["formatted_text"] = text.FormattedText,
                ["production_snapshot_verified_before_oracle"] = true, ["native_identities_preserved"] = true };
            updates.Add(observation);
            // The oracle is created only after native AND production snapshot checks. Freeze
            // actual facets before creation; an oracle which repairs stale glyphs must fail.
            var oracleBaseline = ParametersState69(doc, sketch);
            var transaction = app.TransactionManager.StartTransaction((_Document)doc, "S1 parameters69 glyph oracle, abort");
            try
            {
                var oracle = sketch.TextBoxes.AddFitted(text.Origin.Copy(),
                    "L=<Parameter Name='S1Length69' Precision='0'>S1Length69</Parameter>", text.Style.Name);
                var comparison = GlyphComparison60(text, oracle); observation["glyph_comparison"] = comparison;
                if (!JToken.DeepEquals(frozenGlyphs, ParameterGlyphs69(text))
                    || (bool?)comparison["centered_points_equal"] != true
                    || !JToken.DeepEquals(comparison["actual_vertices"], comparison["expected_vertices"])
                    || !JToken.DeepEquals(comparison["actual_facets"], comparison["expected_facets"]))
                    throw new ProbeFailure("Parameters69 frozen production glyphs differ from fresh native creation.");
            }
            finally { transaction.Abort(); }
            if (!JToken.DeepEquals(oracleBaseline, ParametersState69(doc, sketch)))
                throw new ProbeFailure("Parameters69 native glyph oracle did not abort exactly.");
            observation["oracle_abort_exact"] = true;
            // Direct native oracle events may advance the backend journal even after Abort.
            // Re-read that token; never reuse a pre-oracle token for the next production commit.
            state = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        void Workflow(string name, JObject operation, double sourceMm, string? additionalParameter = null)
        {
            report["phase"] = kind + "_parameters69_" + name;
            var before = ParametersState69(doc, sketch); var beforeState = state;
            using var workflow = new DesignSession(backend);
            workflow.SetContext(state, true, true); workflow.SetDraft(new JArray(operation));
            workflow.PreviewAsync().GetAwaiter().GetResult();
            if (workflow.Status != DesignStatus.PreviewReady || workflow.Preview == null || workflow.CanApply
                || workflow.Preview.Sketches.Count != 1 || workflow.Preview.Sketches.Any(s => !s.NativeMutationComplete || s.NativePartial)
                || workflow.Preview.Sketches[0].NativeTextGlyphs.Count != 1
                || !JToken.DeepEquals(before, ParametersState69(doc, sketch))
                || !SameState56(beforeState, backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult()))
                throw new ProbeFailure("Parameters69 preview failed or did not restore exact native baseline and revision.");
            workflow.ConfirmRendered(workflow.Preview.PlanId);
            if (!workflow.CanApply) throw new ProbeFailure("Parameters69 synthetic render confirmation did not enable Apply.");
            workflow.ApplyAsync().GetAwaiter().GetResult();
            var after = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (workflow.LastCommit == null || workflow.CommitOutcomeUnknown || workflow.LastCommit.DocumentId != documentId
                || workflow.LastCommit.Revision != after.Revision || after.DocumentId != documentId || after.Revision == beforeState.Revision)
                throw new ProbeFailure("Parameters69 native commit was not confirmed.");
            state = after; CheckContext(sourceMm, additionalParameter); CheckConsumers(sourceMm, false);
            workflows.Add(new JObject { ["name"] = name, ["command"] = operation["command"]?.DeepClone(),
                ["preview_abort_exact"] = true, ["revision_before"] = beforeState.Revision,
                ["revision_after"] = state.Revision, ["commit_confirmed"] = true });
        }

        CheckConsumers(10, false);
        Workflow("set_expression_20", NativeParameterOperations.SetExpression(sourceName, "20 mm"), 20);
        CheckConsumers(20, true);
        Workflow("set_expression_35", NativeParameterOperations.SetExpression(sourceName, "35 mm"), 35);
        CheckConsumers(35, true);
        Workflow("create_unused", NativeParameterOperations.Create(unusedName, "12 mm", "mm"), 35, unusedName);
        var unused = doc.ComponentDefinition.Parameters.UserParameters[unusedName]; string unusedKey = Identity(unused);
        if (Math.Abs(Convert.ToDouble(unused.Value, CultureInfo.InvariantCulture) * 10 - 12) > toleranceMm || unused.Dependents.Count != 0)
            throw new ProbeFailure("Parameters69 unused parameter creation differs.");
        Workflow("rename_unused", NativeParameterOperations.Rename(unusedName, renamedName), 35, renamedName);
        if (Identity(doc.ComponentDefinition.Parameters.UserParameters[renamedName]) != unusedKey
            || doc.ComponentDefinition.Parameters.Cast<Parameter>().Any(p => p.Name == unusedName))
            throw new ProbeFailure("Parameters69 rename changed identity or retained the old name.");
        Workflow("delete_unused", NativeParameterOperations.Delete(renamedName), 35);
        if (doc.ComponentDefinition.Parameters.Cast<Parameter>().Any(p => p.Name is unusedName or renamedName))
            throw new ProbeFailure("Parameters69 deletion retained the unused parameter.");
        CheckConsumers(35, false);

        void Refuse(string name, JObject operation, string expectedStep, bool stale = false, JObject? precedingOperation = null)
        {
            report["phase"] = kind + "_parameters69_refuse_" + name;
            var before = ParametersState69(doc, sketch); bool refused = false;
            try
            {
                mcp.CallToolAsync("inventor_atomic_batch", new JObject { ["document_id"] = documentId,
                    ["expected_revision"] = stale ? staleRevision : state.Revision,
                    ["operations"] = precedingOperation == null ? new JArray(operation)
                        : new JArray(precedingOperation, operation) }, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (McpToolException error)
            {
                string? step = (string?)error.Details?["details"]?["step_code"];
                refused = stale ? error.Code == "STALE_REVISION" : error.Code == "ROLLED_BACK" && step == expectedStep;
                refusals.Add(new JObject { ["name"] = name, ["code"] = error.Code, ["step_code"] = step });
            }
            if (!refused || !JToken.DeepEquals(before, ParametersState69(doc, sketch))
                || !SameState56(state, backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult()))
                throw new ProbeFailure("Parameters69 refusal was missing or changed native state and revision.");
        }
        if (source.Dependents.Count == 0) throw new ProbeFailure("Parameters69 source has no native consumer dependencies.");
        Refuse("delete_source_with_dependents", NativeParameterOperations.Delete(sourceName), "INVALID_ARGUMENT");
        Refuse("delete_dimension_model_parameter", NativeParameterOperations.Delete(dimensionParameterName), "INVALID_ARGUMENT");
        Refuse("stale_source_expression", NativeParameterOperations.SetExpression(sourceName, "40 mm"), "STALE_REVISION", true);
        Refuse("invalid_expression", NativeParameterOperations.SetExpression(sourceName, "__MissingParameter69__ + ("), "API_ERROR");
        Refuse("invalid_after_parameter_rebuild80", NativeParameterOperations.SetExpression(sourceName, "__MissingParameter69__ + ("),
            "API_ERROR", precedingOperation: NativeParameterOperations.SetExpression(sourceName, "40 mm"));
        CheckConsumers(35, false);
        row["previews"] = workflows.Count; row["commits"] = workflows.Count;
        row["two_source_updates_verified"] = updates.Count == 2; row["refusals_count"] = refusals.Count;
        row["outcome"] = "PASS native parameter builders/workflows; command74 UI68, full options/defaults, Quest and physical remain NOT COVERED.";
    }

    private static JObject ParameterGlyphs69(TextBox text)
    {
        int vertices = 0, facets = 0; double[] coordinates = Array.Empty<double>(); int[] indices = Array.Empty<int>();
        text.GetFacetsInfo(out vertices, out facets, out coordinates, out indices);
        if (vertices < 1 || facets < 1 || coordinates.Length != vertices * 3 || indices.Length != facets * 3
            || coordinates.Any(v => !double.IsFinite(v)) || indices.Any(i => i < 1 || i > vertices))
            throw new ProbeFailure("Parameters69 native glyph arrays are unavailable or malformed.");
        return new JObject { ["vertices"] = vertices, ["facets"] = facets,
            ["coordinates_cm"] = new JArray(coordinates), ["indices"] = new JArray(indices) };
    }

    private static JObject ParametersState69(PartDocument doc, PlanarSketch sketch)
    {
        var state = TextState60(doc, sketch);
        state["full_geometry"] = WholeState56(doc, sketch);
        state["parameter_values"] = new JArray(doc.ComponentDefinition.Parameters.Cast<Parameter>().Select(p => new JObject
        { ["key"] = Identity(p), ["name"] = p.Name, ["expression"] = p.Expression, ["unit"] = p.get_Units(),
            ["type"] = p.ParameterType.ToString(), ["native_value"] = JToken.FromObject(p.Value) }));
        state["dimension_values"] = new JArray(sketch.DimensionConstraints.Cast<DimensionConstraint>().Select(d => new JObject
        { ["key"] = Identity(d), ["parameter_key"] = Identity(d.Parameter), ["parameter_name"] = d.Parameter.Name,
            ["expression"] = d.Parameter.Expression, ["value_cm"] = Convert.ToDouble(d.Parameter.Value, CultureInfo.InvariantCulture),
            ["text_cm"] = new JArray(d.TextPoint.X, d.TextPoint.Y), ["driven"] = d.Driven }));
        state["glyphs"] = new JArray(sketch.TextBoxes.Cast<TextBox>().Select(t => new JObject
        { ["key"] = Identity(t), ["mesh"] = ParameterGlyphs69(t) }));
        return state;
    }
}
