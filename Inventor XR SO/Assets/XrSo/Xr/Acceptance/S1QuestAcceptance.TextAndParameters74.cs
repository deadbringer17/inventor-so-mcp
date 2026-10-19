#if XR_SO_ACCEPTANCE
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventorXrSo.Xr
{
    internal sealed partial class S1QuestAcceptance
    {
        private const string TextSketchName = "S1_TextRead";
        private const string LengthParameter = "S1QuestLength73";
        private const string FlagParameter = "S1QuestFlag73";
        private const string LabelParameter = "S1QuestLabel73";

        private async Task PaceQaRateBudget(CancellationToken ct)
        {
            Record("QA PACING: 65 seconds between acceptance phases to respect the ordinary 240 HTTP requests/minute budget; no CAD call, mutation or retry.");
            await Task.Delay(TimeSpan.FromSeconds(65), ct);
        }

        private void GuardBinding()
        {
            Check(_binding != null && ReferenceEquals(App.AcceptanceBootstrap, _binding)
                && _binding.ExpiresUtc > DateTimeOffset.UtcNow
                && !string.IsNullOrEmpty(_binding.RunId) && !string.IsNullOrEmpty(_binding.TargetId)
                && _binding.PartDocumentId != _binding.SheetDocumentId,
                "QA fixture binding is absent, replaced or expired.");
            var graph = Session.Scene?.Graph;
            Check(graph != null && (graph.DocumentId == _binding.PartDocumentId
                || graph.DocumentId == _binding.SheetDocumentId), "Active document is outside the QA binding.");
            Check(graph.Root?.Name == (graph.DocumentId == _binding.PartDocumentId ? PartName : SheetName),
                "Active fixture name differs from the exact bound Part/Sheet name.");
        }

        private void GuardAction()
        {
            RequireFixture(); GuardBinding();
            Check(ReadBoolean(App, "_inSession") && Session.Status == SessionStatus.Online
                && Session.Scene?.Graph?.DocumentId == _expectedDocumentId,
                "Action refused: offline, outside session or active fixture identity changed.");
        }

        private async Task GuardOwned(CancellationToken ct)
        {
            GuardAction();
            var documents = await _inspection.ListOpenAsync(ct);
            Check(documents.Count(d => d.Id == _binding.PartDocumentId && d.Name == PartName) == 1
                && documents.Count(d => d.Id == _binding.SheetDocumentId && d.Name == SheetName) == 1,
                "Fresh native document inventory no longer matches both bound fixtures.");
            var state = await _backend.GetDocumentStateAsync(ct);
            Check(state.DocumentId == _expectedDocumentId, "Fresh active native identity changed.");
            GuardAction();
        }

        private async Task RunTextReadContext(string documentId, bool sheet, CancellationToken ct)
        {
            string context = sheet ? "Sheet" : "Part";
            await Activate(documentId, sheet, ct);
            await GuardOwned(ct);
            Action("sketch.editor.open");
            var provider = sheet ? (IActionProvider)_lamiera : _design;
            string prefix = sheet ? LamieraWorkspace.IdPickPrefix : "design.pick.";
            await WaitUntil(() => provider.Actions.Any(a => a.Label == TextSketchName
                && a.Id.StartsWith(prefix, StringComparison.Ordinal)), ct);
            Action(provider.Actions.Single(a => a.Label == TextSketchName && a.Id.StartsWith(prefix, StringComparison.Ordinal)).Id);
            await WaitUntil(() => Editor(sheet)?.Session.CanEdit == true
                && Editor(sheet).Session.Snapshot?.Name == TextSketchName, ct);
            var editor = Editor(sheet);
            var baseline = editor.Session.Snapshot;
            Check(baseline.State.DocumentId == documentId && !baseline.Partial, "Readonly text fixture is partial or unbound.");
            var texts = Texts(baseline);
            Check(texts.Length == 2 && texts.Any(t => (string)t.Data["text"] == "Quest73")
                && texts.Count(t => ParameterText(t)) == 1
                && (string)texts.Single(ParameterText).Data["text"] == "L=10",
                "Expected one native L=10 parameter text and one Quest73 text.");
            var geometry = sheet ? _lamiera.Geometry : _design.Geometry;
            var layer = await WaitFor(() => Read<NativeSketchTextLayer>(geometry, "_textLayer"), ct);
            Check(layer.MeshCount == 2 && layer.GetComponentsInChildren<TMPro.TMP_Text>(false).Length == 0,
                "Two native glyph meshes are required; TMP is not native text evidence.");
            foreach (var text in texts) VerifyNativeMesh(layer, text, false);
            await AssertTextUnchanged(baseline, ct);
            await CaptureScreenshot(context + "-native-text-read", ct);
            foreach (var text in texts)
            {
                GuardAction();
                var corners = text.TextBoundsMm;
                var point = new CadPoint(corners.Average(p => p.X), corners.Average(p => p.Y));
                Check(editor.Pick(point), "Synthetic bounds-center pick was rejected.");
                if (!editor.Selected.Any(r => r.EntityId == text.Reference.EntityId))
                {
                    // Same public hit ordering as the product. Select an explicit entity reference,
                    // never infer the selected object from proximity or the displayed string.
                    var hits = baseline.PickTargets(point, 2, null, SketchSubpart.Entity);
                    int index = Enumerable.Range(0, hits.Count).Single(i => hits[i].Reference.EntityId == text.Reference.EntityId);
                    var candidate = provider.Actions.SingleOrDefault(a => a.Id.StartsWith("sketch.selection.candidate.", StringComparison.Ordinal)
                        && a.Id.EndsWith("." + index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
                    Check(candidate != null, "Native text disambiguation action is absent.");
                    Action(candidate.Id);
                }
                Check(editor.Selected.Count == 1 && editor.Selected[0].EntityId == text.Reference.EntityId,
                    "Readonly pick did not select the actual native text reference.");
                VerifyNativeMesh(layer, text, true);
                await AssertTextUnchanged(baseline, ct);
                await CaptureScreenshot(context + "-native-text-highlight-" + (ParameterText(text) ? "parameter" : "literal"), ct);
            }
            Record("native-text-read " + context + ": " + new JArray(texts.Select(t => t.Data))
                .ToString(Newtonsoft.Json.Formatting.None));
            Action("sketch.editor.finish");
            await WaitUntil(() => !editor.IsOpen, ct);
            await AssertTextUnchanged(baseline, ct);
            Pass("S1-TextRead-Q-" + context,
                "SYNTHETIC open/pick: actual native glyph vertices, indices, colors, bounds and reference-linked highlight; fresh revision/content unchanged. No text authoring command.");
        }

        private static NativeSketchObject[] Texts(NativeSketchSnapshot snapshot)
        {
            var texts = snapshot.Entities.Where(e => e.TextGlyphs != null).ToArray();
            foreach (var text in texts)
                Check(text.Supported && text.Reference.Matches(snapshot.State, snapshot.SketchId)
                    && text.TextBoundsMm.Count == 4 && text.TextGlyphs.VerticesMm.Count > 0
                    && text.TextGlyphs.Indices.Count > 0, "Native text lacks current source reference, bounds or glyphs.");
            return texts;
        }

        private static bool ParameterText(NativeSketchObject text)
        {
            var root = XElement.Parse("<root>" + ((string)text.Data["formatted_text"] ?? "") + "</root>");
            return root.Descendants().Any(e => e.Name.LocalName == "Parameter"
                && (string)e.Attribute("Name") == LengthParameter);
        }

        private static void VerifyNativeMesh(NativeSketchTextLayer layer, NativeSketchObject text, bool highlighted)
        {
            var entries = Read<IEnumerable>(layer, "_entries").Cast<object>();
            var entry = entries.Single(e => Read<string>(e, "Id") == text.Reference.EntityId);
            var go = Read<GameObject>(entry, "Object");
            var mesh = Read<Mesh>(entry, "Mesh");
            var renderer = Read<MeshRenderer>(entry, "Renderer");
            Check(go.activeInHierarchy && renderer.enabled && go.GetComponent<MeshFilter>().sharedMesh == mesh
                && renderer.sharedMaterial.shader.name == "InventorXrSo/NativeSketchText", "Actual native text renderer is inactive or unbound.");
            var expected = text.TextGlyphs.VerticesMm.Select(CadCoordinates.ToLocal).ToArray();
            Check(mesh.vertexCount == expected.Length && mesh.triangles.SequenceEqual(text.TextGlyphs.Indices)
                && mesh.vertices.Zip(expected, (a, b) => Vector3.Distance(a, b) <= 1e-7f).All(x => x),
                "Rendered glyph mesh differs from raw native model coordinates/indices.");
            var bounds = new Bounds(expected[0], Vector3.zero);
            foreach (var point in expected) bounds.Encapsulate(point);
            Check(Vector3.Distance(mesh.bounds.center, bounds.center) <= 1e-7f
                && Vector3.Distance(mesh.bounds.size, bounds.size) <= 1e-7f, "Actual glyph mesh bounds differ from native vertices.");
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            var rgb = text.TextGlyphs.ColorRgb;
            var color = highlighted ? UiTheme.Selection : new Color(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f, 1);
            var actual = block.GetColor("_BaseColor");
            Check(Math.Abs(actual.r - color.r) < 1e-5 && Math.Abs(actual.g - color.g) < 1e-5
                && Math.Abs(actual.b - color.b) < 1e-5 && Math.Abs(actual.a - color.a) < 1e-5,
                "Native source color/selection highlight differs from the actual renderer.");
        }

        private async Task AssertTextUnchanged(NativeSketchSnapshot baseline, CancellationToken ct)
        {
            await GuardOwned(ct);
            var state = await _backend.GetDocumentStateAsync(ct);
            Check(state.DocumentId == baseline.State.DocumentId && state.Revision == baseline.State.Revision,
                "Readonly text/preview operation mutated document identity or revision.");
            var actual = await _backend.GetNativeSketchAsync(state, baseline.SketchId, ct);
            Check(!actual.Partial && actual.Name == baseline.Name
                && JToken.DeepEquals(new JArray(actual.Entities.Select(e => e.Data)), new JArray(baseline.Entities.Select(e => e.Data)))
                && JToken.DeepEquals(new JArray(actual.Dimensions.Select(e => e.Data)), new JArray(baseline.Dimensions.Select(e => e.Data)))
                && JToken.DeepEquals(new JArray(actual.Constraints.Select(e => e.Data)), new JArray(baseline.Constraints.Select(e => e.Data))),
                "Fresh native text sketch differs after readonly/rollback operation.");
        }

        private async Task<DesignContext> FreshParameters(CancellationToken ct)
        {
            await GuardOwned(ct);
            var state = await _backend.GetDocumentStateAsync(ct);
            var context = await _backend.GetDesignContextAsync(state, ct);
            Check(!context.Truncated && context.State.DocumentId == _binding.PartDocumentId,
                "Parameter context is truncated or outside the bound Part.");
            return context;
        }

        private static JObject Parameter(DesignContext context, string name)
            => context.Parameters.OfType<JObject>().SingleOrDefault(p => (string)p["name"] == name);

        private static bool ParameterLengthMatches(DesignContext context, string name, double expectedMm)
            => Math.Abs((double?)Parameter(context, name)?["value_mm"] - expectedMm ?? double.PositiveInfinity) <= 1e-6;

        private async Task<NativeSketchSnapshot> TextConsumer(DesignContext context, CancellationToken ct)
        {
            var sketch = context.Sketches.Single(s => s.Name == TextSketchName);
            Check(!string.IsNullOrEmpty(sketch.NativeSketchId), "Native parameter consumer sketch reference is absent.");
            return await _backend.GetNativeSketchAsync(context.State, sketch.NativeSketchId, ct);
        }

        private async Task<string> SelectParameter(string name, CancellationToken ct)
        {
            await WaitUntil(() => Catalog.Find("design.parameters.expressions")?.Enabled == true, ct);
            await GuardOwned(ct); Action("design.parameters.expressions");
            await WaitUntil(() => _design.Actions.Any(a => a.Id.StartsWith("design.pick.", StringComparison.Ordinal)
                && a.Label.StartsWith(name + " = ", StringComparison.Ordinal)), ct);
            var item = _design.Actions.Single(a => a.Id.StartsWith("design.pick.", StringComparison.Ordinal)
                && a.Label.StartsWith(name + " = ", StringComparison.Ordinal));
            string label = item.Label;
            Action(item.Id);
            return label;
        }

        private void ParameterInput(params string[] values)
        {
            foreach (string value in values)
            {
                GuardAction();
                var entry = _design.ActiveParameterTextEntry;
                Check(entry != null && entry.Editing, "Actual parameter text keypad is not editing.");
                entry.Replace(value);
                Check(entry.Commit(out var reason), "Parameter text keypad rejected input: " + reason);
            }
        }

        private async Task ParameterPreview(DesignContext baseline, NativeSketchSnapshot consumer, CancellationToken ct)
        {
            var mutation = Read<DesignSession>(_design, "_session");
            Check(mutation.Status == DesignStatus.Draft && !mutation.CanApply
                && Catalog.Find(CommitIds.Apply)?.Enabled == false, "Parameter draft can Apply before actual rendered preview.");
            await GuardOwned(ct); Action(CommitIds.Preview);
            await WaitUntil(() => mutation.Status == DesignStatus.PreviewReady && mutation.CanApply, ct);
            var view = PreviewView(false);
            Check(mutation.Preview != null && view.IsShowing && view.PlanId == mutation.Preview.PlanId
                && Read<string>(_design, "_renderedPlan") == view.PlanId, "Actual product preview plan is not rendered.");
            var root = Read<GameObject>(view, "_previewRoot");
            Check(root != null && root.activeInHierarchy && root.GetComponentsInChildren<MeshFilter>(false)
                .Any(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 0), "Parameter preview has no visible actual geometry mesh.");
            var fresh = await FreshParameters(ct);
            Check(fresh.State.Revision == baseline.State.Revision && JToken.DeepEquals(fresh.Parameters, baseline.Parameters),
                "Parameter preview changed native parameters or revision before Apply.");
            await AssertTextUnchanged(consumer, ct);
            await CaptureScreenshot("Part-parameter-preview-" + Guid.NewGuid().ToString("N"), ct);
        }

        private async Task<DesignContext> ParameterApply(DesignContext baseline, NativeSketchSnapshot consumer, CancellationToken ct)
        {
            await ParameterPreview(baseline, consumer, ct);
            await GuardOwned(ct); Action(CommitIds.Apply);
            var mutation = Read<DesignSession>(_design, "_session");
            await WaitUntil(() => mutation.Status == DesignStatus.Empty
                && Read<DesignContext>(_design, "_context")?.State.Revision != baseline.State.Revision
                && Catalog.Find("design.parameters.expressions")?.Enabled == true, ct);
            var current = await FreshParameters(ct);
            Check(current.State.Revision != baseline.State.Revision, "Parameter Apply did not advance native revision.");
            return current;
        }

        private static string DiagnosticHash(JToken value)
        {
            if (value == null) return null;
            using var hash = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(value.ToString(Newtonsoft.Json.Formatting.None));
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static JObject TextConsumerDiagnostic(NativeSketchSnapshot snapshot, NativeSketchObject text)
        {
            var data = text.Data;
            var glyphs = data["text_glyphs"] as JObject;
            var vertices = glyphs?["vertices_mm"] as JArray;
            var indices = glyphs?["indices"] as JArray;
            return new JObject
            {
                ["document_id"] = snapshot.State.DocumentId, ["revision"] = snapshot.State.Revision,
                ["sketch_id"] = snapshot.SketchId, ["reference"] = text.Reference.ToJson(),
                ["reference_matches_snapshot"] = text.Reference.Matches(snapshot.State, snapshot.SketchId),
                ["text"] = data["text"], ["formatted_text"] = data["formatted_text"],
                ["text_bounds_mm"] = data["text_bounds_mm"],
                ["glyphs_present"] = glyphs != null, ["vertex_scalar_count"] = vertices?.Count ?? -1,
                ["vertex_count"] = text.TextGlyphs?.VerticesMm.Count ?? -1,
                ["index_count"] = indices?.Count ?? -1, ["triangle_count"] = text.TextGlyphs?.Indices.Count / 3 ?? -1,
                ["glyphs_sha256"] = DiagnosticHash(glyphs),
                ["vertices_sha256"] = DiagnosticHash(vertices), ["indices_sha256"] = DiagnosticHash(indices),
                ["color_rgb"] = glyphs?["color_rgb"]
            };
        }

        private async Task RunParameterUiContext(string documentId, CancellationToken ct)
        {
            await Activate(documentId, false, ct);
            Check(!Editor(false).IsOpen, "Parameter UI evidence requires the native editor closed.");
            var context = await FreshParameters(ct);
            Check((double?)Parameter(context, LengthParameter)?["value_mm"] == 10
                && (bool?)Parameter(context, FlagParameter)?["value_bool"] == false
                && (string)Parameter(context, LabelParameter)?["value_text"] == "Quest73", "Typed native fixture parameters differ from host73 baseline.");
            var consumer = await TextConsumer(context, ct);
            foreach (string name in new[] { FlagParameter, LabelParameter })
            {
                string label = await SelectParameter(name, ct);
                Check(label.Contains(name == FlagParameter ? "False" : "\"Quest73\""), "Typed parameter picker value is incorrect.");
                Action("design.parameter.info");
            }
            await AssertTextUnchanged(consumer, ct);
            Pass("S1-Parameters-Q-typed-readonly-Part", "Actual catalog shows native boolean/text values; info actions leave native text/revision unchanged.");

            await SelectParameter(LengthParameter, ct);
            Action("design.parameter.expression"); ParameterInput("20 mm");
            await ParameterPreview(context, consumer, ct);
            Action(CommitIds.Cancel);
            await WaitUntil(() => Read<DesignSession>(_design, "_session").Status == DesignStatus.Empty, ct);
            Check(!PreviewView(false).IsShowing && PreviewView(false).PlanId == null, "Parameter Cancel left actual preview visible.");
            var canceled = await FreshParameters(ct);
            Check(canceled.State.Revision == context.State.Revision && JToken.DeepEquals(canceled.Parameters, context.Parameters),
                "Parameter Cancel failed exact native parameter rollback.");
            await AssertTextUnchanged(consumer, ct);
            Pass("S1-Parameters-Q-cancel-Part", "Actual text keypad/Preview/Cancel: native parameters, text consumer and revision unchanged.");
            await PaceQaRateBudget(ct);

            foreach (int length in new[] { 20, 35 })
            {
                await SelectParameter(LengthParameter, ct);
                Action("design.parameter.expression"); ParameterInput(length + " mm");
                var previous = consumer;
                context = await ParameterApply(context, consumer, ct);
                Check(Math.Abs((double?)Parameter(context, LengthParameter)?["value_mm"] - length ?? double.PositiveInfinity) <= 1e-6,
                    "Fresh native length parameter differs from applied expression.");
                consumer = await TextConsumer(context, ct);
                var before = Texts(previous).Single(ParameterText);
                var after = Texts(consumer).Single(ParameterText);
                // Record the first authoritative post-Apply result before asserting it.
                // Diagnostics do not retry, repair, or replace this snapshot with a later read.
                var checks = new JObject
                {
                    ["same_entity_id"] = after.Reference.EntityId == before.Reference.EntityId,
                    ["text_changed"] = (string)after.Data["text"] != (string)before.Data["text"],
                    ["glyphs_changed"] = !JToken.DeepEquals(after.Data["text_glyphs"], before.Data["text_glyphs"]),
                    ["expected_resolved_text"] = (string)after.Data["text"] == "L=" + length.ToString(CultureInfo.InvariantCulture)
                };
                Record("parameter-native-consumer-first-read " + new JObject
                {
                    ["requested_length_mm"] = length,
                    ["before"] = TextConsumerDiagnostic(previous, before),
                    ["after"] = TextConsumerDiagnostic(consumer, after), ["checks"] = checks,
                    ["missing_conditions"] = new JArray(checks.Properties().Where(p => !(bool)p.Value).Select(p => p.Name))
                }.ToString(Newtonsoft.Json.Formatting.None));
                Check(after.Reference.EntityId == before.Reference.EntityId
                    && (string)after.Data["text"] != (string)before.Data["text"]
                    && !JToken.DeepEquals(after.Data["text_glyphs"], before.Data["text_glyphs"]),
                    "Native parameter text reference/value/glyphs did not update after expression Apply.");
                Check((string)after.Data["text"] == "L=" + length.ToString(CultureInfo.InvariantCulture),
                    "Resolved native text differs from the new parameter value.");
                Record("parameter-native-consumer " + length + ": " + after.Data.ToString(Newtonsoft.Json.Formatting.None));
            }
            Pass("S1-Parameters-Q-expression-Part", "Actual UI Preview/Apply twice (20 then35mm), fresh native parameter/text/glyph updates with stable source reference.");
            await PaceQaRateBudget(ct);

            const string unused = "S1QuestUnused74", renamed = "S1QuestRenamed74";
            var beforeCrudConsumer = consumer;
            Check(Parameter(context, unused) == null && Parameter(context, renamed) == null, "Unused QA parameter names already exist; refusing overwrite.");
            Action("design.parameter.create"); ParameterInput(unused, "3 mm", "mm");
            context = await ParameterApply(context, consumer, ct);
            Check(ParameterLengthMatches(context, unused, 3), "Native unused user parameter was not created.");
            consumer = await TextConsumer(context, ct);
            await SelectParameter(unused, ct); Action("design.parameter.rename"); ParameterInput(renamed);
            context = await ParameterApply(context, consumer, ct);
            Check(Parameter(context, unused) == null && ParameterLengthMatches(context, renamed, 3),
                "Native unused parameter rename did not preserve its value.");
            consumer = await TextConsumer(context, ct);
            await SelectParameter(renamed, ct); Action("design.parameter.delete");
            context = await ParameterApply(context, consumer, ct);
            Check(Parameter(context, renamed) == null && Parameter(context, unused) == null, "Native unused parameter delete failed.");
            var finalConsumer = await TextConsumer(context, ct);
            foreach (var original in Texts(beforeCrudConsumer))
            {
                var actual = Texts(finalConsumer).Single(t => t.Reference.EntityId == original.Reference.EntityId);
                Check((string)actual.Data["text"] == (string)original.Data["text"]
                    && (string)actual.Data["formatted_text"] == (string)original.Data["formatted_text"]
                    && JToken.DeepEquals(actual.Data["text_glyphs"], original.Data["text_glyphs"]),
                    "Deleting the unused parameter unexpectedly changed a native text consumer.");
            }
            Check((double?)Parameter(context, LengthParameter)?["value_mm"] == 35
                && (bool?)Parameter(context, FlagParameter)?["value_bool"] == false
                && (string)Parameter(context, LabelParameter)?["value_text"] == "Quest73",
                "Unused parameter CRUD changed an existing fixture parameter.");
            await CaptureScreenshot("Part-parameter-crud-complete", ct);
            Pass("S1-Parameters-Q-unused-CRUD-Part", "Actual catalog/text keypad Create/Rename/Delete of unused user parameter; each Preview/Apply followed by fresh native readback.");
            NotCovered("S1-Parameters-stale-offline", "This increment does not inject callback races or transport failure; deterministic unit contracts cover stale input separately.");
        }
    }
}
#endif
