#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using ActionCatalog = InventorXrSo.Core.Ui.ActionCatalog;
using XrAction = InventorXrSo.Core.Ui.XrAction;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>Opt-in, fixture-scoped M2 acceptance runner (product spec section 53, Inspect) for a dedicated Quest build.</summary>
    internal sealed class M2QuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers =
        {
            "AppController._inspect",
            "AppController._catalog",
            "AppController._selection",
            "AppController._selecting",
            "AppController.sceneView",
            "AppController.selectionVisuals",
            "AppController.environment",
            "AppController.EnterSession",
            "AppController.OnPicked",
            "InspectWorkspace._context",
            "InspectWorkspace._section",
            "InspectWorkspace._measure",
            "InspectWorkspace._backend",
            "InspectWorkspace._documentState",
            "InspectWorkspace._documents",
            "InspectWorkspace._selected",
            "InspectWorkspace._info",
            "InspectWorkspace._busy",
            "InspectWorkspace._scaleMode",
            "InspectWorkspace._roomExtent",
            "InspectWorkspace.OnPointPicked",
            "InspectWorkspace.Enter",
            "SelectionVisuals._tinted",
            "SectionPlane._outline",
            "EnvironmentModeController.eye",
        };

        private const string Dash = "—";
        private const double VolumeMm3 = 120000;
        private const string DocAssembly = "kAssemblyDocumentObject";
        private const string DocPart = "kPartDocumentObject";

        protected override string Milestone => "m2";
        protected override int TimeoutSeconds => 300;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M2QuestAcceptance>("xr_m2_acceptance");

        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            RequireFixture();
            Check(fixture.Graph.Kind == "assembly", "fixture scene is the assembly");
            Record("Dedicated fixture loaded: " + fixture.Graph.Root.Name);

            Call(App, "EnterSession", EnvironmentMode.MixedReality);
            var view = Read<CadSceneView>(App, "sceneView");
            var visuals = Read<SelectionVisuals>(App, "selectionVisuals");
            var environment = Read<EnvironmentModeController>(App, "environment");
            var inspect = Read<InspectWorkspace>(App, "_inspect");
            var selection = Read<SelectionService>(App, "_selection");
            var context = Read<BrowserContext>(inspect, "_context");
            var section = Read<SectionPlane>(inspect, "_section");
            var measure = Read<MeasurementView>(inspect, "_measure");
            var inspection = Read<IInspectionBackend>(inspect, "_backend");
            Check(view != null && visuals != null && environment != null && inspect != null && selection != null
                && context != null && section != null && measure != null && inspection != null,
                "app exposes the Inspect workspace, its context, section, measurement and backend");
            Check(view.Instances.Count == 2, "CadSceneView shows the 2 fixture occurrences, found " + view.Instances.Count);
            Check(Mathf.Abs(view.transform.lossyScale.x - 1f) < 1e-4f, "scene starts at 1:1");
            var assemblyDocumentId = fixture.Graph.DocumentId;

            // M2-Inspect: Ispeziona is on the palette by default; its tabs and actions are declared, no panel or wrist menu exists.
            // Every tap below is an action invoked by id: SYNTHETIC input, never a hand or a controller ray.
            Record("Actions are invoked by id through the catalog and numbers typed on the keypad entry (synthetic input)");
            Check(inspect.Active && ReferenceEquals(Catalog.Active, inspect), "Ispeziona is the active workspace of the palette");
            var tabs = Catalog.Tabs.Select(t => t.Label).ToList();
            Check(tabs.SequenceEqual(new[] { "Misura", "Sezione", "Vista", "Visibilità", "Verifica", "Spazi" }), "palette tabs are " + string.Join(" / ", tabs));
            foreach (var id in new[] { InspectWorkspace.IdBrowse, InspectWorkspace.IdProperties, InspectWorkspace.IdMeasure, InspectWorkspace.IdSection,
                InspectWorkspace.IdScale, InspectWorkspace.IdEnvironment, InspectWorkspace.IdDocuments })
                Check(Catalog.Find(id) != null, "the catalog declares '" + id + "'");
            Pass("M2-Inspect", "Ispeziona on the palette with tabs Misura, Sezione, Vista, Visibilita, Verifica; Esplora, Proprieta, Misura, Sezione, Scala and the environment switch are declared actions");

            // M2-Browser: fixture hierarchy as a picker list of the Vista tab.
            RunAction(InspectWorkspace.IdBrowse);
            Check(context.Current != null && context.Current.Name.StartsWith(FixturePrefix, StringComparison.Ordinal) && context.Path.Count == 1,
                "browser starts at the fixture assembly root");
            var children = context.Current.Children.ToList();
            Check(children.Count == 2 && children.All(c => !c.Suppressed && c.DefinitionKind == "part"), "root has exactly 2 unsuppressed part occurrences");
            foreach (var child in children) Check(FindPick(child.Name) != null, "browser lists occurrence '" + child.Name + "'");
            Check((inspect.Notice ?? "").Contains(context.Current.Name), "the HUD shows the root context");
            Pass("M2-Browser", "browser lists " + context.Current.Name + " > " + string.Join(", ", children.Select(c => c.Name)));

            // M2-Properties: unknown values first, then a real occurrence.
            var node = children[0];
            inspect.ClearSelection();
            var unknown = inspect.InfoText;
            Check(unknown.Contains("Materiale: " + Dash) && unknown.Contains("Massa: " + Dash) && unknown.Contains("Volume: " + Dash)
                && unknown.Contains("Area: " + Dash), "without loaded properties every value is shown as " + Dash + ", none invented");
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            PickItem(node.Name);
            var info = await WaitFor(() => Read<InspectionInfo>(inspect, "_info"), ct);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            Check(Read<SceneNode>(inspect, "_selected") == node, "browser entry selected occurrence " + node.Name);
            Check(selection.Current.Kind == SelectionKind.Occurrence && selection.Current.OccurrenceId == node.OccurrenceId,
                "SelectionService holds the browser-selected occurrence");
            var state = Read<DocumentState>(inspect, "_documentState");
            var direct = await inspection.InspectAsync(state, node.OccurrenceId, ct);
            Check(info.VolumeMm3.HasValue && Math.Abs(info.VolumeMm3.Value - VolumeMm3) <= 1.0, "occurrence volume is 120000 mm3 +/- 1, loaded " + (info.VolumeMm3.HasValue ? F(info.VolumeMm3.Value) : Dash));
            Check(direct.VolumeMm3.HasValue && Math.Abs(direct.VolumeMm3.Value - info.VolumeMm3.Value) < 1e-6
                && direct.MassKg == info.MassKg && direct.AreaMm2 == info.AreaMm2 && direct.Material == info.Material,
                "properties shown by the workspace equal a direct InspectionInfo read from the backend");
            RunAction(InspectWorkspace.IdProperties);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy") && inspect.Notice.Contains("Volume: "), ct);
            var details = inspect.InfoText;
            Check(details.Contains(node.Name) && details.Contains("Volume: " + F(info.VolumeMm3.Value)), "properties on the HUD show the volume " + F(info.VolumeMm3.Value) + " mm3");
            Check(inspect.Notice.Contains("Volume: " + F(info.VolumeMm3.Value)), "the HUD message carries the properties");
            CheckShown(details, "Massa: ", info.MassKg.HasValue);
            CheckShown(details, "Area: ", info.AreaMm2.HasValue);
            CheckShown(details, "Vincoli: ", info.Constraints.HasValue);
            if (info.TranslationDof.HasValue == info.RotationDof.HasValue) CheckShown(details, "DOF: ", info.TranslationDof.HasValue);
            Check(details.Contains("Materiale: " + Dash) == (info.Material == null), "material is shown only when the backend returned one");
            var tinted = Read<List<(Renderer renderer, Material original)>>(visuals, "_tinted");
            Check(tinted.Count > 0, "the selected occurrence is tinted");
            Pass("M2-Properties", node.Name + ": volume " + F(info.VolumeMm3.Value) + " mm3 (direct read " + F(direct.VolumeMm3.Value) + "), mass "
                + (info.MassKg.HasValue ? F(info.MassKg.Value) + " kg" : Dash) + ", area " + (info.AreaMm2.HasValue ? F(info.AreaMm2.Value) + " mm2" : Dash)
                + "; unknown values render as " + Dash);

            // M2-Breadcrumb: enter the occurrence context and go back (the context path now lives on the HUD).
            RunAction(InspectWorkspace.IdEnter);
            Check(context.Path.Count == 2 && context.Current == node, "'Apri contesto' entered " + node.Name);
            Check(inspect.Notice.Contains(context.Path[0].Name) && inspect.Notice.Contains(node.Name), "the HUD shows root and occurrence in the context path");
            Check(selection.Current.Kind == SelectionKind.None && Read<SceneNode>(inspect, "_selected") == null, "entering a context clears the local selection");
            RunAction(InspectWorkspace.IdBack);
            Check(context.Path.Count == 1 && context.Current.OccurrenceId == fixture.Graph.Root.OccurrenceId, "'Indietro' returned to the assembly root");
            Check(!Catalog.Find(InspectWorkspace.IdBack).Enabled, "'Indietro' is disabled at the root");
            Call(inspect, "Enter", node);
            Check(context.Path.Count == 2, "occurrence context entered again (double-pick path)");
            RunAction(InspectWorkspace.IdBack);
            Check(context.Path.Count == 1, "'Indietro' returned to the assembly root again");
            Pass("M2-Breadcrumb", "entered " + node.Name + " (path depth 2, shown on the HUD) and returned to the root with Indietro");

            // M2-Measure: point-to-point between the two occurrence centers, then pin. The two points are SYNTHETIC (no ray).
            RunAction(InspectWorkspace.IdMeasure);
            Check(inspect.Measuring, "'Misura' started a measurement");
            var pointA = view.transform.TransformPoint(InstanceBounds(view.transform, view.Instances[0]).center);
            var pointB = view.transform.TransformPoint(InstanceBounds(view.transform, view.Instances[1]).center);
            Call(inspect, "OnPointPicked", pointA);
            Check(inspect.Measuring && measure.HasFirstPoint, "first point accepted");
            Call(inspect, "OnPointPicked", pointB);
            Check(!inspect.Measuring && measure.DistanceMm.HasValue, "second point completed the measurement");
            var distance = measure.DistanceMm.Value;
            var geometric = InspectionGeometry.DistanceMm(view.transform, pointA, pointB);
            Check(Math.Abs(distance - 150.0) <= 0.5, "distance between the occurrence centers is 150 mm +/- 0.5, measured " + F(distance) + " mm");
            Check(Math.Abs(distance - geometric) <= 0.01, "measurement equals InspectionGeometry.DistanceMm, " + F(geometric) + " mm");
            RunAction(InspectWorkspace.IdMeasurePin);
            Check(measure.PinnedCount == 1 && !measure.DistanceMm.HasValue, "measurement pinned on the model");
            Pass("M2-Measure", "point-to-point between occurrence centers " + F(distance) + " mm (expected 150), pinned (" + measure.PinnedCount + "/20)");

            // M2-Section: enable, numeric offset through the keypad entry, plane visible.
            RunAction(InspectWorkspace.IdSection);
            Check(section.Active, "section plane activated");
            RunAction(InspectWorkspace.IdSectionOffset);
            Check(inspect.ActiveEntry != null && inspect.ActiveEntry.Editing, "'Scostamento' opened the numeric keypad");
            TypeNumber(inspect, "25");
            Check(inspect.ActiveEntry == null, "keypad closed after confirming");
            Check(Math.Abs(section.OffsetMm - 25f) < 0.01f, "section offset is 25 mm, found " + F(section.OffsetMm));
            Check(Catalog.Find(InspectWorkspace.IdSectionOffset).Label.Contains("25"), "the declared action shows the numeric offset");
            var outline = Read<LineRenderer>(section, "_outline");
            Check(section.gameObject.activeInHierarchy && outline != null && outline.enabled, "section plane outline is visible");
            Check(Shader.GetGlobalFloat("_XrSectionEnabled") > 0.5f, "section clipping is published to the shaders");
            await CaptureScreenshot("section", ct);
            Pass("M2-Section", "section active with numeric offset " + F(section.OffsetMm) + " mm; plane outline visible; clipping enabled");
            RunAction(InspectWorkspace.IdSection);
            Check(!section.Active, "section deactivated before continuing");

            // M2-Scale: Table (<= 0.60 m) and a real shrink, then back to 1:1 (the Scala list is a picker tab of the Vista tab).
            var localExtent = Mathf.Max(ScenePlacement.LocalBounds(view.transform).size.x, ScenePlacement.LocalBounds(view.transform).size.y,
                ScenePlacement.LocalBounds(view.transform).size.z);
            RunAction(InspectWorkspace.IdScale);
            RunAction(InspectWorkspace.IdScaleTable);
            var tableExtent = localExtent * view.transform.lossyScale.x;
            Check(ReadValue<ModelScaleMode>(inspect, "_scaleMode") == ModelScaleMode.Table && tableExtent <= 0.6f + 1e-3f, "Table scale keeps the model within 0.60 m, extent " + F(tableExtent) + " m");
            var tableScale = view.transform.lossyScale.x;
            RunAction(InspectWorkspace.IdScale);
            RunAction(InspectWorkspace.IdScaleRoom);
            TypeNumber(inspect, "0.2");
            Check(Mathf.Abs(ReadValue<float>(inspect, "_roomExtent") - 0.2f) < 1e-4f, "available space set to 0.2 m through the keypad");
            RunAction(InspectWorkspace.IdScale);
            RunAction(InspectWorkspace.IdScaleFit);
            var expected = Mathf.Min(1f, 0.2f / localExtent);
            var fitExtent = localExtent * view.transform.lossyScale.x;
            Check(Mathf.Abs(view.transform.lossyScale.x - expected) < 0.005f && fitExtent <= 0.2f + 1e-3f,
                "Fit to room scaled the model to " + F(expected) + " (extent " + F(fitExtent) + " m), found scale " + F(view.transform.lossyScale.x));
            RunAction(InspectWorkspace.IdScale);
            RunAction(InspectWorkspace.IdScaleRoom);
            TypeNumber(inspect, "2");
            RunAction(InspectWorkspace.IdScale);
            RunAction(InspectWorkspace.IdScaleOne);
            Check(Mathf.Abs(view.transform.lossyScale.x - 1f) < 1e-4f && ReadValue<ModelScaleMode>(inspect, "_scaleMode") == ModelScaleMode.OneToOne
                && Mathf.Abs(ReadValue<float>(inspect, "_roomExtent") - 2f) < 1e-4f, "scale is back to 1:1 and the room extent is restored");
            Pass("M2-Scale", "Table scale factor " + F(tableScale) + " (extent " + F(tableExtent) + " m <= 0.60; model is " + F(localExtent) + " m); Fit to room 0.2 m -> factor "
                + F(expected) + "; back to 1:1");

            // M2-MR: environment switch from the Vista tab.
            var eye = Read<Camera>(environment, "eye");
            Check(environment.Mode == EnvironmentMode.MixedReality && eye != null && eye.backgroundColor.a < 0.01f, "environment is Mixed Reality");
            RunAction(InspectWorkspace.IdEnvironment);
            Check(environment.Mode == EnvironmentMode.StudioVr && eye.backgroundColor.a > 0.99f, "switched to Studio VR");
            RunAction(InspectWorkspace.IdEnvironment);
            Check(environment.Mode == EnvironmentMode.MixedReality && eye.backgroundColor.a < 0.01f, "switched back to Mixed Reality");
            Pass("M2-MR", "Mixed Reality -> Studio VR -> Mixed Reality from the Ispeziona action");

            // M2-Activate: documents list, activate the fixture part, verify part context, reactivate the assembly.
            RunAction(InspectWorkspace.IdBrowse);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            PickItem(node.Name);
            await WaitFor(() => Read<InspectionInfo>(inspect, "_info"), ct);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            Check(selection.Current.Kind == SelectionKind.Occurrence && measure.PinnedCount == 1,
                "before the document switch: occurrence selected and one measurement pinned");
            RunAction(InspectWorkspace.IdDocuments);
            var documents = await WaitDocuments(inspect, ct);
            var fixtureDocs = documents.Where(d => d.Name != null && d.Name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToList();
            var partDoc = fixtureDocs.FirstOrDefault(d => d.Kind == DocPart && d.Name.StartsWith(FixturePrefix + "_Block", StringComparison.Ordinal));
            var assemblyDoc = fixtureDocs.FirstOrDefault(d => d.Kind == DocAssembly);
            Check(fixtureDocs.Count >= 2 && partDoc != null && assemblyDoc != null,
                "open documents contain the fixture assembly and part, found: " + string.Join(", ", documents.Select(d => d.Name + "/" + d.Kind)));
            Check(inspect.Notice.Contains("Attivazione in Inventor"), "the HUD explains that activation does not touch the CAD");
            Pass("M2-Activate", "open documents list contains " + assemblyDoc.Name + " (assembly) and " + partDoc.Name + " (part)");

            RequireFixture();
            Check(partDoc.Name.StartsWith(FixturePrefix, StringComparison.Ordinal), "only a fixture document is activated");
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            await PickDocument(inspect, partDoc.Name, ct);
            var partScene = await WaitFor<LoadedScene>(() => Session.Scene != null && Session.Scene.Graph.Kind == "part"
                && Session.Scene.Graph.DocumentId != assemblyDocumentId && Session.Scene.Graph.Root.Name.StartsWith(FixturePrefix, StringComparison.Ordinal)
                ? Session.Scene : null, ct);
            await WaitUntil(() => context.Graph != null && context.Graph.DocumentId == partScene.Graph.DocumentId
                && view.DocumentId == partScene.Graph.DocumentId && view.Instances.Count >= 1, ct);
            Pass("M2-Activate", "Browser activated " + partDoc.Name + "; session scene root is now the part " + partScene.Graph.Root.Name);

            // M2-Stale: the scene change invalidated measurements, selection and section.
            Check(measure.PinnedCount == 0 && !measure.Measuring, "pinned measurements were cleared by the scene change");
            Check(selection.Current.Kind == SelectionKind.None && Read<SceneNode>(inspect, "_selected") == null, "selection was cleared by the scene change");
            Check(Read<List<(Renderer renderer, Material original)>>(visuals, "_tinted").Count == 0, "selection tint was removed");
            Check(!section.Active, "section plane was reset by the scene change");
            Pass("M2-Stale", "after switching to document " + partScene.Graph.DocumentId + " no measurement, selection or section survived");

            // Part context: face selection through OnPicked.
            RequireFixture();
            Check(context.Current != null && context.Current.DefinitionKind == "part", "inspection context is the part");
            var partInstance = view.Instances[0];
            Check(TryFindPick(view.transform, partInstance, out var pickedBody, out var triangle), "found a real body and triangle on the part");
            var face = pickedBody.Primitive.FaceMap.FaceAtTriangle(triangle);
            Check(face != null && !string.IsNullOrEmpty(face.FaceId), "triangle " + triangle + " maps to a face id locally");
            Call(App, "OnPicked", pickedBody, triangle);
            await WaitUntil(() => !ReadBoolean(App, "_selecting") && !ReadBoolean(inspect, "_busy"), ct);
            var picked = selection.Current;
            Check(picked.Kind == SelectionKind.Face && picked.FaceId == face.FaceId && !string.IsNullOrEmpty(picked.EntityId),
                "part pick selected face " + face.FaceId + ", found " + picked.Kind);
            Pass("M2-Activate", "part context: pick selected face " + face.FaceId + " (entity " + picked.EntityId + ")");
            var backend = Read<IInventorBackend>(selection, "_backend");
            await selection.ClearAsync(ct);
            await backend.ClearHighlightAsync(ct);

            // Reactivate the assembly through the Browser.
            RequireFixture();
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            RunAction(InspectWorkspace.IdDocuments);
            documents = await WaitDocuments(inspect, ct);
            var assemblyAgain = documents.FirstOrDefault(d => d.Kind == DocAssembly && d.Name != null && d.Name.StartsWith(FixturePrefix, StringComparison.Ordinal)
                && !d.Name.StartsWith(FixturePrefix + "_Block", StringComparison.Ordinal));
            Check(assemblyAgain != null, "fixture assembly is still listed");
            await PickDocument(inspect, assemblyAgain.Name, ct);
            var backScene = await WaitFor<LoadedScene>(() => Session.Scene != null && Session.Scene.Graph.Kind == "assembly"
                && Session.Scene.Graph.DocumentId != partScene.Graph.DocumentId && Session.Scene.Graph.Root.Name.StartsWith(FixturePrefix, StringComparison.Ordinal)
                ? Session.Scene : null, ct);
            await WaitUntil(() => context.Graph != null && context.Graph.DocumentId == backScene.Graph.DocumentId && view.Instances.Count == 2, ct);
            RequireFixture();
            Check(measure.PinnedCount == 0 && selection.Current.Kind == SelectionKind.None && !section.Active,
                "nothing from the previous scene survived the return to the assembly");
            Pass("M2-Activate", "assembly " + backScene.Graph.Root.Name + " reactivated through the Browser; 2 occurrences shown again");

            Record("NOT COVERED [M2-Stale] a revision change of the same document (an edit) is not induced; only the document switch was verified");
            Record("NOT COVERED [M2-Physical] controller ray, grip grab of the model and section plane, palette ergonomics and HUD legibility are not exercised: every action here is invoked by id (synthetic input)");
        }

        private ActionCatalog Catalog => Read<ActionCatalog>(App, "_catalog");

        /// <summary>Invokes a declared action by id through the catalog, the same path as palette, ring and voice (synthetic tap).</summary>
        private void RunAction(string id)
        {
            var action = Catalog.Find(id);
            Check(action != null, "the action catalog has no action '" + id + "'");
            Check(action.Enabled, "action '" + id + "' is disabled: " + action.DisabledReason);
            Check(action.TryInvoke(), "action '" + id + "' did not run");
        }

        /// <summary>An entry of the open picker list (components, documents) by label, any page.</summary>
        private XrAction FindPick(string label) => Read<InspectWorkspace>(App, "_inspect").Actions
            .FirstOrDefault(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal) && a.Label.EndsWith(label, StringComparison.Ordinal));

        private void PickItem(string label)
        {
            var action = FindPick(label);
            Check(action != null, "the open list has no entry '" + label + "'");
            Check(action.Enabled, "list entry '" + label + "' is enabled: " + action.DisabledReason);
            Check(action.TryInvoke(), "list entry '" + label + "' did not run");
        }

        /// <summary>Types a value on the open keypad entry and confirms it (synthetic keypad input).</summary>
        private static void TypeNumber(InspectWorkspace inspect, string value)
        {
            var entry = inspect.ActiveEntry;
            Check(entry != null && entry.Editing, "numeric keypad is open");
            foreach (var ch in value) entry.Type(ch);
            Check(entry.Commit(out var reason), "keypad accepted " + value + ": " + reason);
        }

        private static void CheckShown(string text, string label, bool known)
        {
            Check(text.Contains(label + Dash) == !known, "'" + label.Trim() + "' is " + (known ? "a value" : Dash) + " exactly as the backend reported");
        }

        private async Task<IReadOnlyList<OpenDocument>> WaitDocuments(InspectWorkspace inspect, CancellationToken ct)
        {
            await WaitUntil(() => !ReadBoolean(inspect, "_busy") && Read<IReadOnlyList<OpenDocument>>(inspect, "_documents").Count > 0
                && FindPick(Read<IReadOnlyList<OpenDocument>>(inspect, "_documents")[0].Name) != null, ct);
            return Read<IReadOnlyList<OpenDocument>>(inspect, "_documents");
        }

        private async Task PickDocument(InspectWorkspace inspect, string name, CancellationToken ct)
        {
            Check(name.StartsWith(FixturePrefix, StringComparison.Ordinal), "refusing to activate a document outside the fixture: " + name);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            PickItem(name);
        }

        private static Bounds InstanceBounds(Transform root, CadInstance instance)
        {
            var first = true;
            var bounds = new Bounds();
            foreach (var body in instance.Bodies)
            {
                var b = body.Mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = root.InverseTransformPoint(body.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            return bounds;
        }

        private static bool TryFindPick(Transform root, CadInstance instance, out CadBody body, out int triangle)
        {
            Physics.SyncTransforms();
            var center = root.TransformPoint(InstanceBounds(root, instance).center);
            var ray = new Ray(center + Vector3.up * 1f, Vector3.down);
            if (CadRaycaster.TryPick(ray, 3f, out body, out triangle, out _) && body.Instance == instance) return true;
            body = instance.Bodies.FirstOrDefault();
            triangle = -1;
            if (body == null) return false;
            int count = (int)(body.Mesh.GetIndexCount(0) / 3);
            for (int t = 0; t < count; t++)
            {
                if (body.Primitive.FaceMap.FaceAtTriangle(t) == null) continue;
                triangle = t;
                return true;
            }
            return false;
        }
    }
}
#endif
