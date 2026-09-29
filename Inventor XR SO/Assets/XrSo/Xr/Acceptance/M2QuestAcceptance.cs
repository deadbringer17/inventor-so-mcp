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
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Xr
{
    /// <summary>Opt-in, fixture-scoped M2 acceptance runner (product spec section 53, Inspect) for a dedicated Quest build.</summary>
    internal sealed class M2QuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers =
        {
            "AppController._inspect",
            "AppController._selection",
            "AppController._selecting",
            "AppController.sceneView",
            "AppController.selectionVisuals",
            "AppController.environment",
            "AppController.EnterSession",
            "AppController.OnPicked",
            "HomePanel._entry",
            "InspectWorkspace._panel",
            "InspectWorkspace._wrist",
            "InspectWorkspace._breadcrumb",
            "InspectWorkspace._context",
            "InspectWorkspace._section",
            "InspectWorkspace._measure",
            "InspectWorkspace._backend",
            "InspectWorkspace._documentState",
            "InspectWorkspace._documents",
            "InspectWorkspace._selected",
            "InspectWorkspace._info",
            "InspectWorkspace._busy",
            "InspectWorkspace._screen",
            "InspectWorkspace._page",
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
            var panel = Read<HomePanel>(inspect, "_panel");
            var wrist = Read<Canvas>(inspect, "_wrist");
            var breadcrumb = Read<Canvas>(inspect, "_breadcrumb");
            var context = Read<BrowserContext>(inspect, "_context");
            var section = Read<SectionPlane>(inspect, "_section");
            var measure = Read<MeasurementView>(inspect, "_measure");
            var inspection = Read<IInspectionBackend>(inspect, "_backend");
            Check(view != null && visuals != null && environment != null && inspect != null && selection != null && panel != null
                && wrist != null && breadcrumb != null && context != null && section != null && measure != null && inspection != null,
                "app exposes the Inspect workspace, its panels, context, section, measurement and backend");
            Check(view.Instances.Count == 2, "CadSceneView shows the 2 fixture occurrences, found " + view.Instances.Count);
            Check(Mathf.Abs(view.transform.lossyScale.x - 1f) < 1e-4f, "scene starts at 1:1");
            var assemblyDocumentId = fixture.Graph.DocumentId;

            // M2-Inspect: the wrist menu opens the Inspect tools.
            Check(AllText(wrist).Contains("ISPEZIONE"), "wrist menu shows the INSPECT mode");
            Click(wrist, "Ispeziona");
            Check(panel.gameObject.activeSelf && Read<string>(inspect, "_screen") == "tools", "wrist 'Ispeziona' opened the tools page");
            var tools = AllText(panel);
            Check(tools.Contains("Solo ispezione") && HasButton(panel, "Esplora") && HasButton(panel, "Misura")
                && HasButton(panel, "Sezione") && HasButton(panel, "Scala"), "tools page lists Browser, Misura, Sezione and Scala");
            Pass("M2-Inspect", "Inspect opened from the wrist menu; tools page shows Browser, Proprieta, Misura, Sezione, Scala and the environment switch");

            // M2-Browser: fixture hierarchy.
            Click(wrist, "Esplora");
            Check(Read<string>(inspect, "_screen") == "browser", "wrist 'Browser' opened the browser page");
            Check(context.Current != null && context.Current.Name.StartsWith(FixturePrefix, StringComparison.Ordinal) && context.Path.Count == 1,
                "browser starts at the fixture assembly root");
            var children = context.Current.Children.ToList();
            Check(children.Count == 2 && children.All(c => !c.Suppressed && c.DefinitionKind == "part"), "root has exactly 2 unsuppressed part occurrences");
            foreach (var child in children) Check(HasButton(panel, child.Name), "browser lists occurrence '" + child.Name + "'");
            Check(AllText(panel).Contains(context.Current.Name), "browser header shows the root name");
            Pass("M2-Browser", "browser lists " + context.Current.Name + " > " + string.Join(", ", children.Select(c => c.Name)));

            // M2-Properties: unknown values first, then a real occurrence.
            var node = children[0];
            inspect.ClearSelection();
            inspect.Open("details");
            var unknown = AllText(panel);
            Check(unknown.Contains("Materiale: " + Dash) && unknown.Contains("Massa: " + Dash) && unknown.Contains("Volume: " + Dash)
                && unknown.Contains("Area: " + Dash), "without loaded properties every value is shown as " + Dash + ", none invented");
            inspect.Open("browser");
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            Click(panel, node.Name);
            var info = await WaitFor(() => Read<InspectionInfo>(inspect, "_info"), ct);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            Check(Read<SceneNode>(inspect, "_selected") == node, "browser click selected occurrence " + node.Name);
            Check(selection.Current.Kind == SelectionKind.Occurrence && selection.Current.OccurrenceId == node.OccurrenceId,
                "SelectionService holds the browser-selected occurrence");
            var state = Read<DocumentState>(inspect, "_documentState");
            var direct = await inspection.InspectAsync(state, node.OccurrenceId, ct);
            Check(info.VolumeMm3.HasValue && Math.Abs(info.VolumeMm3.Value - VolumeMm3) <= 1.0, "occurrence volume is 120000 mm3 +/- 1, loaded " + (info.VolumeMm3.HasValue ? F(info.VolumeMm3.Value) : Dash));
            Check(direct.VolumeMm3.HasValue && Math.Abs(direct.VolumeMm3.Value - info.VolumeMm3.Value) < 1e-6
                && direct.MassKg == info.MassKg && direct.AreaMm2 == info.AreaMm2 && direct.Material == info.Material,
                "properties shown by the workspace equal a direct InspectionInfo read from the backend");
            inspect.Open("details");
            var details = AllText(panel);
            Check(details.Contains(node.Name) && details.Contains("Volume: " + F(info.VolumeMm3.Value)), "details page shows the volume " + F(info.VolumeMm3.Value) + " mm3");
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

            // M2-Breadcrumb: enter the occurrence context, go back with the breadcrumb.
            Click(panel, "Apri contesto");
            Check(context.Path.Count == 2 && context.Current == node, "'Apri contesto' entered " + node.Name);
            var crumbs = AllText(breadcrumb);
            Check(crumbs.Contains(context.Path[0].Name) && crumbs.Contains(node.Name), "breadcrumb shows root and occurrence");
            Check(selection.Current.Kind == SelectionKind.None && Read<SceneNode>(inspect, "_selected") == null, "entering a context clears the local selection");
            Click(breadcrumb, "‹");
            Check(context.Path.Count == 1 && context.Current.OccurrenceId == fixture.Graph.Root.OccurrenceId, "breadcrumb Back returned to the assembly root");
            Call(inspect, "Enter", node);
            Check(context.Path.Count == 2, "occurrence context entered again");
            Click(breadcrumb, context.Path[0].Name);
            Check(context.Path.Count == 1, "breadcrumb root button returned to the assembly root");
            Pass("M2-Breadcrumb", "entered " + node.Name + " (path depth 2) and returned to the root with Back and with the root crumb");

            // M2-Measure: point-to-point between the two occurrence centers, then pin.
            inspect.Open("tools");
            Click(panel, "Misura");
            Check(inspect.Measuring && Read<string>(inspect, "_screen") == "measure", "'Misura' started a measurement");
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
            Click(panel, "Fissa misura");
            Check(measure.PinnedCount == 1 && !measure.DistanceMm.HasValue, "measurement pinned on the model");
            Pass("M2-Measure", "point-to-point between occurrence centers " + F(distance) + " mm (expected 150), pinned (" + measure.PinnedCount + "/20)");

            // M2-Section: enable, numeric offset through the keypad, plane visible.
            inspect.Open("tools");
            Click(panel, "Sezione");
            Check(Read<string>(inspect, "_screen") == "section", "'Sezione' opened the section page");
            Click(panel, "Attiva sezione");
            Check(section.Active, "section plane activated");
            Click(panel, "Scostamento numerico");
            Check(Read<string>(inspect, "_screen") == "numeric", "'Offset numerico' opened the numeric keypad");
            SubmitNumber(panel, "25");
            Check(Read<string>(inspect, "_screen") == "section", "keypad returned to the section page");
            Check(Math.Abs(section.OffsetMm - 25f) < 0.01f, "section offset is 25 mm, found " + F(section.OffsetMm));
            Check(AllText(panel).Contains("Offset: 25"), "section page shows the numeric offset");
            var outline = Read<LineRenderer>(section, "_outline");
            Check(section.gameObject.activeInHierarchy && outline != null && outline.enabled, "section plane outline is visible");
            Check(Shader.GetGlobalFloat("_XrSectionEnabled") > 0.5f, "section clipping is published to the shaders");
            await CaptureScreenshot("section", ct);
            Pass("M2-Section", "section active with numeric offset " + F(section.OffsetMm) + " mm; plane outline visible; clipping enabled");
            Click(panel, "Disattiva sezione");
            Check(!section.Active, "section deactivated before continuing");

            // M2-Scale: Table (<= 0.60 m) and a real shrink, then back to 1:1.
            inspect.Open("tools");
            Click(panel, "Scala");
            var localExtent = Mathf.Max(ScenePlacement.LocalBounds(view.transform).size.x, ScenePlacement.LocalBounds(view.transform).size.y,
                ScenePlacement.LocalBounds(view.transform).size.z);
            ClickStartingWith(panel, "Scala da tavolo");
            var tableExtent = localExtent * view.transform.lossyScale.x;
            Check(Read<string>(inspect, "_screen") == "scale" && tableExtent <= 0.6f + 1e-3f, "Table scale keeps the model within 0.60 m, extent " + F(tableExtent) + " m");
            var tableScale = view.transform.lossyScale.x;
            Click(panel, "Spazio disponibile");
            SubmitNumber(panel, "0.2");
            Check(Mathf.Abs(ReadValue<float>(inspect, "_roomExtent") - 0.2f) < 1e-4f, "available space set to 0.2 m through the keypad");
            Click(panel, "Adatta alla stanza");
            var expected = Mathf.Min(1f, 0.2f / localExtent);
            var fitExtent = localExtent * view.transform.lossyScale.x;
            Check(Mathf.Abs(view.transform.lossyScale.x - expected) < 0.005f && fitExtent <= 0.2f + 1e-3f,
                "Fit to room scaled the model to " + F(expected) + " (extent " + F(fitExtent) + " m), found scale " + F(view.transform.lossyScale.x));
            Click(panel, "Spazio disponibile");
            SubmitNumber(panel, "2");
            Click(panel, "Mantieni 1:1");
            Check(Mathf.Abs(view.transform.lossyScale.x - 1f) < 1e-4f && ReadValue<ModelScaleMode>(inspect, "_scaleMode") == ModelScaleMode.OneToOne
                && Mathf.Abs(ReadValue<float>(inspect, "_roomExtent") - 2f) < 1e-4f, "scale is back to 1:1 and the room extent is restored");
            Pass("M2-Scale", "Table scale factor " + F(tableScale) + " (extent " + F(tableExtent) + " m <= 0.60; model is " + F(localExtent) + " m); Fit to room 0.2 m -> factor "
                + F(expected) + "; back to 1:1");

            // M2-MR: environment switch from the tools page.
            var eye = Read<Camera>(environment, "eye");
            Check(environment.Mode == EnvironmentMode.MixedReality && eye != null && eye.backgroundColor.a < 0.01f, "environment is Mixed Reality");
            inspect.Open("tools");
            Click(panel, "Studio virtuale");
            Check(environment.Mode == EnvironmentMode.StudioVr && eye.backgroundColor.a > 0.99f, "switched to Studio VR");
            Click(panel, "Realtà mista");
            Check(environment.Mode == EnvironmentMode.MixedReality && eye.backgroundColor.a < 0.01f, "switched back to Mixed Reality");
            Pass("M2-MR", "Mixed Reality -> Studio VR -> Mixed Reality from the Inspect tools page");

            // M2-Activate: documents list, activate the fixture part, verify part context, reactivate the assembly.
            inspect.Open("browser");
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            Click(panel, node.Name);
            await WaitFor(() => Read<InspectionInfo>(inspect, "_info"), ct);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            Check(selection.Current.Kind == SelectionKind.Occurrence && measure.PinnedCount == 1,
                "before the document switch: occurrence selected and one measurement pinned");
            Click(panel, "Documenti aperti");
            var documents = await WaitDocuments(inspect, ct);
            var fixtureDocs = documents.Where(d => d.Name != null && d.Name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToList();
            var partDoc = fixtureDocs.FirstOrDefault(d => d.Kind == DocPart && d.Name.StartsWith(FixturePrefix + "_Block", StringComparison.Ordinal));
            var assemblyDoc = fixtureDocs.FirstOrDefault(d => d.Kind == DocAssembly);
            Check(fixtureDocs.Count >= 2 && partDoc != null && assemblyDoc != null,
                "open documents contain the fixture assembly and part, found: " + string.Join(", ", documents.Select(d => d.Name + "/" + d.Kind)));
            Check(AllText(panel).Contains("Attivazione in Inventor"), "documents page is shown");
            Pass("M2-Activate", "open documents list contains " + assemblyDoc.Name + " (assembly) and " + partDoc.Name + " (part)");

            RequireFixture();
            Check(partDoc.Name.StartsWith(FixturePrefix, StringComparison.Ordinal), "only a fixture document is activated");
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            await ClickDocument(inspect, panel, partDoc.Name, ct);
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
            inspect.Open("browser");
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            Click(panel, "Documenti aperti");
            documents = await WaitDocuments(inspect, ct);
            var assemblyAgain = documents.FirstOrDefault(d => d.Kind == DocAssembly && d.Name != null && d.Name.StartsWith(FixturePrefix, StringComparison.Ordinal)
                && !d.Name.StartsWith(FixturePrefix + "_Block", StringComparison.Ordinal));
            Check(assemblyAgain != null, "fixture assembly is still listed");
            await ClickDocument(inspect, panel, assemblyAgain.Name, ct);
            var backScene = await WaitFor<LoadedScene>(() => Session.Scene != null && Session.Scene.Graph.Kind == "assembly"
                && Session.Scene.Graph.DocumentId != partScene.Graph.DocumentId && Session.Scene.Graph.Root.Name.StartsWith(FixturePrefix, StringComparison.Ordinal)
                ? Session.Scene : null, ct);
            await WaitUntil(() => context.Graph != null && context.Graph.DocumentId == backScene.Graph.DocumentId && view.Instances.Count == 2, ct);
            RequireFixture();
            Check(measure.PinnedCount == 0 && selection.Current.Kind == SelectionKind.None && !section.Active,
                "nothing from the previous scene survived the return to the assembly");
            Pass("M2-Activate", "assembly " + backScene.Graph.Root.Name + " reactivated through the Browser; 2 occurrences shown again");

            Record("NOT COVERED [M2-Stale] a revision change of the same document (an edit) is not induced; only the document switch was verified");
            Record("NOT COVERED [M2-Physical] controller ray, grip grab of the panel and section plane, and wrist tracking are not exercised");
        }

        private static string AllText(Component root) => string.Join("\n", root.GetComponentsInChildren<Text>(false)
            .Where(t => t.gameObject.activeInHierarchy).Select(t => t.text));

        private static Button FindButton(Component root, Func<string, bool> match) => root.GetComponentsInChildren<Button>(false)
            .LastOrDefault(b => b.gameObject.activeInHierarchy && match(b.GetComponentInChildren<Text>()?.text ?? ""));

        private static bool HasButton(Component root, string label) => FindButton(root, t => t == label || t.EndsWith(label, StringComparison.Ordinal)) != null;

        private static void Click(Component root, string label)
        {
            var button = FindButton(root, t => t == label);
            Check(button != null, "button '" + label + "' is shown");
            Check(button.interactable, "button '" + label + "' is enabled");
            button.onClick.Invoke();
        }

        private static void ClickStartingWith(Component root, string prefix)
        {
            var button = FindButton(root, t => t.StartsWith(prefix, StringComparison.Ordinal));
            Check(button != null, "button starting with '" + prefix + "' is shown");
            Check(button.interactable, "button '" + prefix + "' is enabled");
            button.onClick.Invoke();
        }

        private static void CheckShown(string text, string label, bool known)
        {
            Check(text.Contains(label + Dash) == !known, "'" + label.Trim() + "' is " + (known ? "a value" : Dash) + " exactly as the backend reported");
        }

        private static void SubmitNumber(HomePanel panel, string value)
        {
            while (Read<string>(panel, "_entry").Length > 0) panel.Press(UiText.KeyBack);
            foreach (var ch in value) panel.Press(ch.ToString());
            panel.Press(UiText.KeyOk);
        }

        private async Task<IReadOnlyList<OpenDocument>> WaitDocuments(InspectWorkspace inspect, CancellationToken ct)
        {
            await WaitUntil(() => Read<string>(inspect, "_screen") == "documents" && !ReadBoolean(inspect, "_busy")
                && Read<IReadOnlyList<OpenDocument>>(inspect, "_documents").Count > 0, ct);
            return Read<IReadOnlyList<OpenDocument>>(inspect, "_documents");
        }

        private async Task ClickDocument(InspectWorkspace inspect, HomePanel panel, string name, CancellationToken ct)
        {
            Check(name.StartsWith(FixturePrefix, StringComparison.Ordinal), "refusing to activate a document outside the fixture: " + name);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            var documentCount = Read<IReadOnlyList<OpenDocument>>(inspect, "_documents").Count;
            var pageCount = Math.Max(1, (documentCount + 5) / 6);
            for (int page = 0; page < pageCount; page++)
            {
                var button = FindButton(panel, t => t == name);
                if (button != null)
                {
                    Check(button.interactable, "document button '" + name + "' is enabled");
                    button.onClick.Invoke();
                    return;
                }
                Click(panel, "Pagina ›");
            }
            Check(false, "document '" + name + "' is not on any page of the documents list (" + documentCount
                + " documents, page " + ReadValue<int>(inspect, "_page") + "; visible: " + AllText(panel) + ")");
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
