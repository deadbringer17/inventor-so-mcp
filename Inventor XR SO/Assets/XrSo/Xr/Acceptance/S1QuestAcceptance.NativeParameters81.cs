#if XR_SO_ACCEPTANCE
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventorXrSo.Xr
{
    // Staged increment81; not executed evidence. The shared product editor owns all drafts and rendering proof.
    internal sealed partial class S1QuestAcceptance
    {
        private async Task<DesignContext> NativeParameterContext(NativeSketchEditorUi editor, CancellationToken ct)
        {
            await GuardOwned(ct);
            var state = await _backend.GetDocumentStateAsync(ct);
            Check(editor.IsOpen && editor.Session.SketchId == editor.Session.Snapshot?.SketchId
                && editor.Session.Snapshot.Name == TextSketchName
                && state.DocumentId == _expectedDocumentId
                && editor.Session.Snapshot.State.DocumentId == state.DocumentId
                && editor.Session.Snapshot.State.Revision == state.Revision,
                "Native parameter context is outside the bound current open text sketch.");
            var context = await _backend.GetDesignContextAsync(state, ct);
            Check(!context.Truncated && context.State.DocumentId == state.DocumentId
                && context.State.Revision == state.Revision, "Native parameter context is truncated or stale.");
            return context;
        }

        private async Task<string> SelectNativeParameter(bool sheet, string name, CancellationToken ct)
        {
            var provider = sheet ? (IActionProvider)_lamiera : _design;
            await WaitUntil(() => Catalog.Find("sketch.native.AppParametersCmd")?.Enabled == true, ct);
            await GuardOwned(ct); Action("sketch.native.AppParametersCmd");
            await WaitUntil(() => provider.Actions.Any(a => a.Id.StartsWith("sketch.parameter.pick.", StringComparison.Ordinal)
                && a.Label.StartsWith(name + " = ", StringComparison.Ordinal) && a.Enabled), ct);
            var item = provider.Actions.Single(a => a.Id.StartsWith("sketch.parameter.pick.", StringComparison.Ordinal)
                && a.Label.StartsWith(name + " = ", StringComparison.Ordinal));
            string label = item.Label; Action(item.Id); return label;
        }

        private void NativeParameterInput(NativeSketchEditorUi editor, params string[] values)
        {
            foreach (string value in values)
            {
                GuardAction();
                var entry = editor.ActiveParameterTextEntry;
                Check(entry != null && entry.Editing, "Actual shared editor parameter text keypad is not editing.");
                Check(entry.Replace(value), "Actual parameter text keypad rejected replacement.");
                Check(entry.Commit(out var reason), "Actual parameter text keypad rejected confirmation: " + reason);
            }
        }

        private async Task NativeParameterPreview(NativeSketchEditorUi editor, bool sheet,
            DesignContext baseline, NativeSketchSnapshot consumer, CancellationToken ct)
        {
            var native = editor.Session;
            Check(native.HasDraft && native.CanConfirmDraft && native.Mutation.Status == DesignStatus.Draft
                && !native.Mutation.CanApply && Catalog.Find(CommitIds.Apply)?.Enabled == false,
                "Native parameter draft must wait for a real rendered preview before Apply.");
            Check(Catalog.Find("sketch.editor.finish")?.Enabled == false, "Native parameter draft did not block Finish.");
            await GuardOwned(ct); Action(CommitIds.Preview);
            await WaitUntil(() => native.Mutation.Status == DesignStatus.PreviewReady && native.Mutation.CanApply, ct);
            var preview = native.Mutation.Preview;
            var view = PreviewView(sheet);
            string rendered = sheet ? Read<string>(_lamiera, "_renderedPlan") : Read<string>(_design, "_renderedPlan");
            Check(preview != null && preview.DocumentId == baseline.State.DocumentId && preview.Revision == baseline.State.Revision
                && view != null && view.IsShowing && view.PlanId == preview.PlanId && rendered == view.PlanId,
                "The actual native parameter preview is not rendered for its baseline.");
            var root = Read<GameObject>(view, "_previewRoot");
            Check(root != null && root.activeInHierarchy && root.GetComponentsInChildren<MeshFilter>(false)
                .Any(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 0), "Actual parameter preview geometry is missing.");
            var fresh = await NativeParameterContext(editor, ct);
            Check(fresh.State.Revision == baseline.State.Revision && JToken.DeepEquals(fresh.Parameters, baseline.Parameters),
                "Native parameter preview changed parameters/revision before Apply.");
            await AssertTextUnchanged(consumer, ct);
            await CaptureScreenshot((sheet ? "Sheet" : "Part") + "-inside-parameter-preview-" + Guid.NewGuid().ToString("N"), ct);
        }

        private async Task NativeParameterCancel(NativeSketchEditorUi editor, bool sheet,
            DesignContext context, NativeSketchSnapshot consumer, CancellationToken ct)
        {
            await NativeParameterPreview(editor, sheet, context, consumer, ct);
            Action(CommitIds.Cancel);
            await WaitUntil(() => !editor.Session.HasDraft && editor.Session.Mutation.Status == DesignStatus.Empty, ct);
            Check(!PreviewView(sheet).IsShowing && PreviewView(sheet).PlanId == null,
                "Native parameter Cancel left the actual preview visible.");
            var fresh = await NativeParameterContext(editor, ct);
            Check(fresh.State.Revision == context.State.Revision && JToken.DeepEquals(fresh.Parameters, context.Parameters),
                "Native parameter Cancel failed exact parameter/revision rollback.");
            await AssertTextUnchanged(consumer, ct);
        }

        private async Task<DesignContext> NativeParameterApply(NativeSketchEditorUi editor, bool sheet,
            DesignContext context, NativeSketchSnapshot consumer, CancellationToken ct)
        {
            await NativeParameterPreview(editor, sheet, context, consumer, ct);
            await GuardOwned(ct); Action(CommitIds.Apply);
            await WaitUntil(() => editor.IsOpen && editor.Session.Snapshot != null && editor.Session.CanEdit
                && !editor.Session.NeedsRefresh && !editor.Session.HasDraft
                && editor.Session.Mutation.Status == DesignStatus.Empty
                && editor.Session.Snapshot.State.Revision != context.State.Revision
                && Catalog.Find("sketch.native.AppParametersCmd")?.Enabled == true, ct);
            var fresh = await NativeParameterContext(editor, ct);
            Check(fresh.State.DocumentId == context.State.DocumentId && fresh.State.Revision != context.State.Revision
                && editor.Session.Snapshot.State.Revision == fresh.State.Revision
                && editor.Session.SketchId == consumer.SketchId, "Apply failed to refresh the same native editor/sketch.");
            return fresh;
        }

        private static void UnchangedParameterConsumers(NativeSketchSnapshot before, NativeSketchSnapshot after)
        {
            var originals = Texts(before); var actual = Texts(after);
            Check(actual.Length == originals.Length, "Unused parameter operation changed the native text count.");
            foreach (var original in originals)
            {
                var current = actual.Single(t => t.Reference.EntityId == original.Reference.EntityId);
                Check((string)current.Data["text"] == (string)original.Data["text"]
                    && (string)current.Data["formatted_text"] == (string)original.Data["formatted_text"]
                    && JToken.DeepEquals(current.Data["text_glyphs"], original.Data["text_glyphs"])
                    && JToken.DeepEquals(current.Data["text_bounds_mm"], original.Data["text_bounds_mm"]),
                    "Unused parameter operation unexpectedly changed native text content/geometry/identity.");
            }
        }

        private void VerifyRefreshedNativeText(NativeSketchEditorUi editor, bool sheet, NativeSketchSnapshot fresh)
        {
            var presented = editor.Session.Snapshot;
            Check(!presented.Partial && presented.State.DocumentId == fresh.State.DocumentId
                && presented.State.Revision == fresh.State.Revision && presented.SketchId == fresh.SketchId,
                "Actual editor snapshot differs from fresh native readback.");
            var geometry = sheet ? _lamiera.Geometry : _design.Geometry;
            var layer = Read<NativeSketchTextLayer>(geometry, "_textLayer");
            var texts = Texts(fresh);
            Check(layer != null && layer.MeshCount == texts.Length
                && layer.GetComponentsInChildren<TMPro.TMP_Text>(false).Length == 0, "Actual refreshed native glyph layer is missing.");
            foreach (var text in texts)
            {
                var shown = Texts(presented).Single(t => t.Reference.EntityId == text.Reference.EntityId);
                Check(JToken.DeepEquals(shown.Data, text.Data), "Displayed native object differs from the fresh native object.");
                VerifyNativeMesh(layer, text, false);
            }
        }

        private async Task RunNativeParameterUiContext(string documentId, bool sheet, int initialLength, CancellationToken ct)
        {
            string label = sheet ? "Sheet" : "Part";
            await Activate(documentId, sheet, ct);
            await GuardOwned(ct); Action("sketch.editor.open");
            var provider = sheet ? (IActionProvider)_lamiera : _design;
            string prefix = sheet ? LamieraWorkspace.IdPickPrefix : "design.pick.";
            await WaitUntil(() => provider.Actions.Any(a => a.Label == TextSketchName && a.Id.StartsWith(prefix, StringComparison.Ordinal)), ct);
            Action(provider.Actions.Single(a => a.Label == TextSketchName && a.Id.StartsWith(prefix, StringComparison.Ordinal)).Id);
            await WaitUntil(() => Editor(sheet)?.Session.CanEdit == true && Editor(sheet).Session.Snapshot?.Name == TextSketchName, ct);
            var editor = Editor(sheet);
            var context = await NativeParameterContext(editor, ct);
            var consumer = await TextConsumer(context, ct);
            Check(ParameterLengthMatches(context, LengthParameter, initialLength)
                && (bool?)Parameter(context, FlagParameter)?["value_bool"] == false
                && (string)Parameter(context, LabelParameter)?["value_text"] == "Quest73"
                && (string)Texts(consumer).Single(ParameterText).Data["text"] == "L=" + initialLength.ToString(CultureInfo.InvariantCulture),
                "Native editor parameter fixture differs from its explicit expected starting state.");
            VerifyRefreshedNativeText(editor, sheet, consumer);
            foreach (string name in new[] { FlagParameter, LabelParameter })
            {
                string description = await SelectNativeParameter(sheet, name, ct);
                Check(description.Contains(name == FlagParameter ? "False" : "\"Quest73\""), "Native editor typed parameter value is incorrect.");
                Action("sketch.parameter.info");
            }
            await AssertTextUnchanged(consumer, ct);
            Pass("S1-Parameters-Q-inside-typed-read-" + label,
                "SYNTHETIC shared native editor catalog: actual Boolean/Text values and native glyph meshes read without mutation.");

            // Typed expressions also exercise the real native preview, but remain rollback-only here.
            foreach (var field in new[] { new[] { FlagParameter, "True" }, new[] { LabelParameter, "\"Quest81\"" },
                new[] { LengthParameter, "20 mm" } })
            {
                await SelectNativeParameter(sheet, field[0], ct);
                Action("sketch.parameter.expression"); NativeParameterInput(editor, field[1]);
                await NativeParameterCancel(editor, sheet, context, consumer, ct);
            }
            VerifyRefreshedNativeText(editor, sheet, consumer);
            Pass("S1-Parameters-Q-inside-cancel-" + label,
                "Actual shared text keypad/Preview/Cancel for Boolean, Text and length; parameters/text/revision unchanged.");
            await PaceQaRateBudget(ct);

            foreach (int length in new[] { 20, 35 })
            {
                await SelectNativeParameter(sheet, LengthParameter, ct);
                Action("sketch.parameter.expression"); NativeParameterInput(editor, length + " mm");
                var previous = consumer;
                context = await NativeParameterApply(editor, sheet, context, consumer, ct);
                // No retry or pacing between Apply and this first fresh native consumer read.
                consumer = await TextConsumer(context, ct);
                var before = Texts(previous).Single(ParameterText); var after = Texts(consumer).Single(ParameterText);
                var checks = new JObject
                {
                    ["same_entity_id"] = before.Reference.EntityId == after.Reference.EntityId,
                    ["expected_parameter_value"] = Math.Abs((double?)Parameter(context, LengthParameter)?["value_mm"] - length ?? double.PositiveInfinity) <= 1e-6,
                    ["expected_resolved_text"] = (string)after.Data["text"] == "L=" + length.ToString(CultureInfo.InvariantCulture),
                    ["text_changed"] = (string)after.Data["text"] != (string)before.Data["text"],
                    ["glyphs_changed"] = !JToken.DeepEquals(after.Data["text_glyphs"], before.Data["text_glyphs"])
                };
                Record("inside-parameter-native-consumer-first-read " + new JObject
                {
                    ["context"] = label, ["requested_length_mm"] = length,
                    ["before"] = TextConsumerDiagnostic(previous, before), ["after"] = TextConsumerDiagnostic(consumer, after),
                    ["checks"] = checks, ["missing_conditions"] = new JArray(checks.Properties().Where(p => !(bool)p.Value).Select(p => p.Name))
                }.ToString(Newtonsoft.Json.Formatting.None));
                Check(checks.Properties().All(p => (bool)p.Value), "Native editor parameter identity/value/geometry update failed; see granular first-read diagnostics.");
                VerifyRefreshedNativeText(editor, sheet, consumer);
                await CaptureScreenshot(label + "-inside-parameter-applied-" + length, ct);
            }
            Pass("S1-Parameters-Q-inside-expression-" + label,
                "Actual native editor Preview/Apply20 then35mm: strict first-read source identity/value/glyphs and actual rendered native mesh.");
            await PaceQaRateBudget(ct);

            string unused = "S1QuestInsideUnused81", renamed = "S1QuestInsideRenamed81";
            Check(Parameter(context, unused) == null && Parameter(context, renamed) == null, "Unused native editor QA parameter name already exists.");
            var beforeCrud = consumer;
            await SelectNativeParameter(sheet, LengthParameter, ct); // Load actual panel/context before Create.
            Action("sketch.parameter.create"); NativeParameterInput(editor, unused, "3 mm", "mm");
            context = await NativeParameterApply(editor, sheet, context, consumer, ct);
            consumer = await TextConsumer(context, ct);
            Check(ParameterLengthMatches(context, unused, 3), "Native editor unused parameter was not created.");
            UnchangedParameterConsumers(beforeCrud, consumer); VerifyRefreshedNativeText(editor, sheet, consumer);
            await SelectNativeParameter(sheet, unused, ct); Action("sketch.parameter.rename"); NativeParameterInput(editor, renamed);
            context = await NativeParameterApply(editor, sheet, context, consumer, ct);
            consumer = await TextConsumer(context, ct);
            Check(Parameter(context, unused) == null && ParameterLengthMatches(context, renamed, 3),
                "Native editor unused parameter rename differs from the native result.");
            UnchangedParameterConsumers(beforeCrud, consumer); VerifyRefreshedNativeText(editor, sheet, consumer);
            await SelectNativeParameter(sheet, renamed, ct); Action("sketch.parameter.delete");
            context = await NativeParameterApply(editor, sheet, context, consumer, ct);
            consumer = await TextConsumer(context, ct);
            Check(Parameter(context, unused) == null && Parameter(context, renamed) == null
                && (double?)Parameter(context, LengthParameter)?["value_mm"] == 35
                && (bool?)Parameter(context, FlagParameter)?["value_bool"] == false
                && (string)Parameter(context, LabelParameter)?["value_text"] == "Quest73",
                "Native editor unused parameter CRUD changed an existing fixture parameter.");
            UnchangedParameterConsumers(beforeCrud, consumer); VerifyRefreshedNativeText(editor, sheet, consumer);
            await CaptureScreenshot(label + "-inside-parameter-crud", ct);
            Pass("S1-Parameters-Q-inside-unused-CRUD-" + label,
                "Actual shared editor Create/Rename/Delete, native parameter readback and unchanged text identities/geometry after each commit.");
            Action("sketch.editor.finish"); await WaitUntil(() => !editor.IsOpen, ct);
            await AssertTextUnchanged(consumer, ct);
            NotCovered("S1-Parameters-inside-typed-Apply-" + label, "Boolean/Text expressions exercised with Preview/Cancel; Boolean/Text commits and changing existing Units property remain untested.");
        }
    }
}
#endif
