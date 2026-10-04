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
using InventorXrSo.Core.Verify;
using ActionCatalog = InventorXrSo.Core.Ui.ActionCatalog;
using XrAction = InventorXrSo.Core.Ui.XrAction;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Opt-in, fixture-scoped M7 acceptance runner (Ispeziona, engineering verification). Every action is invoked by id through the
    /// catalog: SYNTHETIC input. Interference, distance and health run against the real Inventor fixture.
    /// </summary>
    internal sealed class M7QuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers =
        {
            "AppController._inspect",
            "AppController._catalog",
            "AppController.sceneView",
            "AppController.EnterSession",
            "InspectWorkspace._context",
            "InspectWorkspace._busy",
            "InspectWorkspace._documentState",
            "InspectWorkspace._verifySession",
            "InspectWorkspace._visibility",
            "InspectWorkspace._overlay",
            "InspectWorkspace._findings",
            "InspectWorkspace._findingsStale",
        };

        private const double VolumeMm3 = 2000, DistanceMm = 30;

        protected override string Milestone => "m7";
        protected override int TimeoutSeconds => 420;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M7QuestAcceptance>("xr_m7_acceptance");

        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private ActionCatalog Catalog => Read<ActionCatalog>(App, "_catalog");
        private InspectWorkspace Inspect => Read<InspectWorkspace>(App, "_inspect");

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            RequireFixture();
            Check(fixture.Graph.Kind == "assembly", "fixture scene is the assembly");
            Call(App, "EnterSession", EnvironmentMode.MixedReality);
            var inspect = Inspect;
            var view = Read<CadSceneView>(App, "sceneView");
            var context = Read<BrowserContext>(inspect, "_context");
            var session = Read<VerifySession>(inspect, "_verifySession");
            var visibility = Read<ComponentVisibility>(inspect, "_visibility");
            var overlay = Read<VerifyOverlay>(inspect, "_overlay");
            Check(view.Instances.Count == 4, "the fixture shows 4 cubes, found " + view.Instances.Count);
            Node(context, "M7_A"); Node(context, "M7_B"); Node(context, "M7_C"); Node(context, "M7_D");
            Record("Actions are invoked by id through the catalog (synthetic input); Inventor answers are real");

            // M7-01: visibility, local only.
            await SelectByBrowser(inspect, "M7_C", ct);
            var c = view.Find(Node(context, "M7_C").OccurrenceId);
            RunAction(InspectWorkspace.IdHide);
            Check(visibility.Get(c.OccurrenceId) == OccurrenceVisibility.Hidden, "M7_C is hidden");
            Check(!RayHits(view, c), "the ray passes through the hidden M7_C");
            RunAction(InspectWorkspace.IdXRay);
            Check(visibility.Get(c.OccurrenceId) == OccurrenceVisibility.Ghost && RayHits(view, c), "the ghost M7_C is still hit by the ray");
            RunAction(InspectWorkspace.IdIsolate);
            Check(view.Instances.Where(i => i != c).All(i => visibility.Get(i.OccurrenceId) == OccurrenceVisibility.Ghost), "isolate ghosts the other three cubes");
            RunAction(InspectWorkspace.IdShowAll);
            Check(!visibility.AnyChanged, "Mostra tutto restores every cube");
            Pass("M7-01", "hide (ray passes), X-Ray (ray hits), isolate and show all on the scene, no Inventor call");

            // M7-02: interference against the real fixture.
            Check(!session.Gate.Busy, "no verification is running");
            RunAction(InspectWorkspace.IdInterference);
            await WaitUntil(() => session.Interference.Status != VerifyStatus.Running, ct);
            Check(session.Interference.Status == VerifyStatus.Done, "interference finished: " + session.Interference.ErrorMessage);
            var interference = session.Interference.Result;
            Check(interference.Count == 1, "exactly one interfering pair, found " + interference.Count);
            var pair = interference.Pairs[0];
            Check(new[] { pair.AName, pair.BName }.OrderBy(n => n).SequenceEqual(new[] { "M7_A", "M7_B" }), "the pair is M7_A / M7_B: " + pair.AName + " / " + pair.BName);
            Check(Math.Abs(pair.VolumeMm3 - VolumeMm3) <= VolumeMm3 * 0.01, "volume 2000 mm3 +/- 1%, found " + F(pair.VolumeMm3));
            RunAction(InspectWorkspace.IdResults);
            PickRow("M7_A");
            Check(view.Instances.Count(i => visibility.Get(i.OccurrenceId) == OccurrenceVisibility.Ghost) == 2, "the row focuses the pair and ghosts the other two cubes");
            if (pair.Boxes.Count > 0) Check(overlay.BoxCount == pair.Boxes.Count, "one red box per interference body");
            else NotCovered("M7-02", "Inventor returned no interference body box: the pair is shown in red without boxes");
            await CaptureScreenshot("interference", ct);
            inspect.Back();
            Check(!visibility.AnyChanged && overlay.BoxCount == 0, "Back restores the visibility and clears the boxes");
            Pass("M7-02", "1 pair M7_A/M7_B, " + F(pair.VolumeMm3) + " mm3, " + pair.Boxes.Count + " box(es), elapsed " + (interference.ElapsedMs?.ToString() ?? "?") + " ms; row focus and Back verified");

            // M7-03: minimum distance M7_A - M7_C.
            await SelectByBrowser(inspect, "M7_A", ct);
            RunAction(InspectWorkspace.IdDistance);
            await SelectByBrowser(inspect, "M7_C", ct);
            RunAction(InspectWorkspace.IdDistance);
            await WaitUntil(() => session.Distance.Status != VerifyStatus.Running, ct);
            Check(session.Distance.Status == VerifyStatus.Done, "distance finished: " + session.Distance.ErrorMessage);
            var distance = session.Distance.Result;
            Check(Math.Abs(distance.DistanceMm - DistanceMm) <= 0.01, "distance 30 mm +/- 0.01, found " + F(distance.DistanceMm));
            Check(overlay.HasDistance, "the distance line is drawn");
            Check(overlay.DistanceLabel.EndsWith("(linea indicativa)", StringComparison.Ordinal) == !distance.HasPoints, "the label says whether the line is Inventor's or indicative");
            await CaptureScreenshot("distance", ct);
            Pass("M7-03", "M7_A-M7_C " + F(distance.DistanceMm) + " mm; line " + (distance.HasPoints ? "from Inventor points" : "indicative (no Inventor points)"));

            // M7-04: health and BOM.
            RunAction(InspectWorkspace.IdHealth);
            await WaitUntil(() => session.Health.Status != VerifyStatus.Running, ct);
            Check(session.Health.Status == VerifyStatus.Done, "health finished: " + session.Health.ErrorMessage);
            var health = session.Health.Result;
            Check(health.Issues.Any(i => i.Name == "M7_Sick"), "M7_Sick is reported as failing");
            Check(health.Unconstrained.Select(u => u.Name).SequenceEqual(new[] { "M7_D" }), "M7_D is the only unconstrained cube");
            // get_assembly_bom reports a blank part number as the file name, so the fixture's BOM finding is the blank description (a warning).
            Check(health.BomIssues.Any(b => b.Code == "DESCRIPTION_MISSING"), "the BOM reports the blank description");
            var findings = Read<IReadOnlyList<VerifyFinding>>(inspect, "_findings");
            Check(findings.Any(f => f.Title == "Vincolo in errore: M7_Sick"), "the results list carries the failing constraint");
            var sick = health.Issues.First(i => i.Name == "M7_Sick");
            if (sick.AOccurrenceId == null && sick.BOccurrenceId == null)
                NotCovered("M7-04", "Inventor did not expose the occurrences of M7_Sick: its row has no highlight");
            else
            {
                RunAction(InspectWorkspace.IdResults);
                PickRow("M7_Sick");
                string idB = Node(context, "M7_B").OccurrenceId, idC = Node(context, "M7_C").OccurrenceId;
                Check(visibility.Get(idB) == OccurrenceVisibility.Normal && visibility.Get(idC) == OccurrenceVisibility.Normal,
                    "the M7_Sick row keeps its two occurrences M7_B and M7_C visible");
                Check(view.Instances.Where(i => i.OccurrenceId != idB && i.OccurrenceId != idC).All(i => visibility.Get(i.OccurrenceId) == OccurrenceVisibility.Ghost),
                    "the M7_Sick row ghosts the other two cubes");
                await CaptureScreenshot("health-focus", ct);
                inspect.Back();
                Check(!visibility.AnyChanged, "Back restores the visibility after the M7_Sick row");
            }
            Pass("M7-04", "M7_Sick failing and focused from the list, M7_D unconstrained, DESCRIPTION_MISSING; " + findings.Count + " rows");

            // M7-05: stale (synthetic revision), ignore and refusal of a concurrent run.
            var state = Read<DocumentState>(inspect, "_documentState");
            inspect.SetDocumentState(new DocumentState(state.DocumentId, state.Revision + ":synthetic", state.VisualRevision));
            Check(session.Health.Status == VerifyStatus.Stale && ReadBoolean(inspect, "_findingsStale"), "a new revision marks the health result stale");
            Record("M7-05 stale used a SYNTHETIC revision on the client; the real revision check is covered by XrSo.Core.Tests (FakeAddIn)");
            Check(Session.Document != null, "the session has the real document state");
            inspect.SetDocumentState(Session.Document);
            await WaitUntil(() => VerifyEnabled(InspectWorkspace.IdInterference), ct);
            RunAction(InspectWorkspace.IdInterference);
            Check(!Catalog.Find(InspectWorkspace.IdHealth).Enabled, "a second verification is refused while one runs");
            RunAction(InspectWorkspace.IdIgnore);
            Check(session.Interference.Status == VerifyStatus.Idle, "ignored: no result will be shown");
            await WaitUntil(() => !session.Gate.Busy, ct);
            Check(session.Interference.Result == null, "the ignored answer was discarded");
            Pass("M7-05", "stale on revision change (synthetic), concurrent run refused, ignored answer discarded after Inventor answered");

            // M7-07: timings on the fixture only.
            Check(interference.ElapsedMs.HasValue && interference.ElapsedMs.Value < 30000, "fixture interference finishes within the 30 s command limit");
            Pass("M7-07", "fixture only: interference " + interference.ElapsedMs.Value + " ms (< 30 s)");
            NotCovered("M7-07", "timing on a real user assembly is measured by the --probe-active PC probe, not by this runner");
            NotCovered("M7-06", "offline and part-document enablement is covered by EditMode tests, not on the headset");
            NotCovered("M7-08", "physical: readability of red, ghosts, boxes, line and list while seated, real controller tracking");

            if (visibility.AnyChanged) RunAction(InspectWorkspace.IdShowAll);
        }

        private static SceneNode Node(BrowserContext context, string name)
        {
            var node = BrowserContext.Descendants(context.Graph.Root).FirstOrDefault(n => n.Name == name);
            Check(node != null, "the fixture has occurrence " + name);
            return node;
        }

        private bool VerifyEnabled(string id) => Catalog.Find(id)?.Enabled == true;

        private void RunAction(string id)
        {
            var action = Catalog.Find(id);
            Check(action != null, "the action catalog has no action '" + id + "'");
            Check(action.Enabled, "action '" + id + "' is disabled: " + action.DisabledReason);
            Check(action.TryInvoke(), "action '" + id + "' did not run");
        }

        private XrAction FindPick(string text) => Inspect.Actions
            .FirstOrDefault(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal) && a.Label.Contains(text));

        private void PickRow(string text)
        {
            var action = FindPick(text);
            Check(action != null && action.TryInvoke(), "the open list has an entry containing '" + text + "'");
        }

        /// <summary>Esplora, then the component by name: the same path as the palette (synthetic).</summary>
        private async Task SelectByBrowser(InspectWorkspace inspect, string name, CancellationToken ct)
        {
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            RunAction(InspectWorkspace.IdBrowse);
            var action = Inspect.Actions.FirstOrDefault(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal) && a.Label.EndsWith(name, StringComparison.Ordinal));
            Check(action != null && action.TryInvoke(), "Esplora lists " + name);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
        }

        private static bool RayHits(CadSceneView view, CadInstance instance)
        {
            Physics.SyncTransforms();
            var bounds = InspectionGeometry.InstancesBounds(view.transform, new[] { instance }).Value;
            var center = view.transform.TransformPoint(bounds.center);
            return CadRaycaster.TryPick(new Ray(center + Vector3.up, Vector3.down), 3f, out var body, out _, out _) && body.Instance == instance;
        }
    }
}
#endif
