#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Xr
{
    /// <summary>Opt-in, fixture-scoped M1 acceptance runner (spec M1 section 1, DoD 1-9) for a dedicated Quest build.</summary>
    internal sealed class M1QuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers =
        {
            "AppController._session",
            "AppController._store",
            "AppController._server",
            "AppController._home",
            "AppController._inspect",
            "AppController._selection",
            "AppController._selecting",
            "AppController._inSession",
            "AppController.sceneView",
            "AppController.selectionVisuals",
            "AppController.environment",
            "AppController.RefreshHome",
            "AppController.EnterSession",
            "AppController.OnPicked",
            "HomePanel._title",
            "HomePanel._body",
            "InspectWorkspace._panel",
            "InspectWorkspace._busy",
            "SelectionVisuals._tinted",
            "SelectionVisuals._overlays",
            "SelectionService._backend",
            "EnvironmentModeController.eye",
        };

        private const double LongSideM = 0.100;
        private const double LongSideToleranceM = 0.001;
        private const double GapMm = 50;

        protected override string Milestone => "m1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M1QuestAcceptance>("xr_m1_acceptance");

        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            RequireFixture();
            Record("Dedicated fixture loaded: " + fixture.Graph.Root.Name);

            // DoD 1-2: online session with the credentials that are already stored (no pairing is exercised).
            var server = Read<PairedServer>(App, "_server");
            var store = Read<object>(App, "_store");
            Check(store != null && server != null && !string.IsNullOrEmpty(server.Host) && !string.IsNullOrEmpty(server.Token)
                && !string.IsNullOrEmpty(server.CertSha256), "the app holds a stored, complete pairing (host, pinned certificate, token)");
            Check(Session.Status == SessionStatus.Online, "session is Online");
            var caps = Session.Capabilities;
            Check(caps != null && caps.IsXrReady, "server reports Inventor reachable and the XR capabilities ready");
            Check(!string.IsNullOrEmpty(caps.ServerVersion) && caps.InventorYear.HasValue, "capabilities carry server version and Inventor year");
            Pass("M1-DoD1", "session Online against " + server.Host + ":" + server.Port + " (backend " + UiText.VersionLabel(caps.ServerVersion)
                + ", Inventor " + caps.InventorYear + ") without pairing");
            Pass("M1-DoD2", "credentials come from the stored pairing (pinned certificate and token present, no QR or code flow run)");

            // DoD 3: what Home renders.
            Check(!ReadBoolean(App, "_inSession"), "app is on Home before entering the session");
            Call(App, "RefreshHome");
            var home = Read<HomePanel>(App, "_home");
            Check(home != null, "Home panel exists");
            var title = Read<Text>(home, "_title").text;
            var body = Read<Text>(home, "_body").text;
            Check(title == UiText.AppTitle, "Home title is the app title");
            Check(body.Contains(UiText.PcLabel + server.Host + ":" + server.Port), "Home shows the PC address");
            Check(body.Contains(UiText.BackendLabel + UiText.VersionLabel(caps.ServerVersion)), "Home shows the backend version");
            Check(body.Contains(UiText.InventorLabel + caps.InventorYear), "Home shows the Inventor year");
            Check(fixture.Graph.Kind == "assembly", "fixture scene is an assembly");
            Check(body.Contains(UiText.DocumentLabel + fixture.Graph.Root.Name + " (" + UiText.DocumentKind(fixture.Graph.Kind) + ")"),
                "Home shows the fixture document name and type");
            Pass("M1-DoD3", "Home shows PC, backend, Inventor year, document '" + fixture.Graph.Root.Name + "' and type " + UiText.DocumentKind(fixture.Graph.Kind));

            // Enter the session as the Home button does.
            Call(App, "EnterSession", EnvironmentMode.MixedReality);
            Check(ReadBoolean(App, "_inSession"), "EnterSession switched the app into the session");
            var view = Read<CadSceneView>(App, "sceneView");
            var visuals = Read<SelectionVisuals>(App, "selectionVisuals");
            var environment = Read<EnvironmentModeController>(App, "environment");
            var inspect = Read<InspectWorkspace>(App, "_inspect");
            var selection = Read<SelectionService>(App, "_selection");
            Check(view != null && visuals != null && environment != null && inspect != null && selection != null,
                "app exposes view, selection visuals, environment, inspect workspace and selection service");
            Check(view.gameObject.activeSelf, "scene view is shown in the session");

            // DoD 4: two occurrences with meshes, one block long side = 0.100 m at 1:1.
            Check(fixture.Omitted.Count == 0, "no component was omitted from the scene");
            var placed = fixture.Graph.PlacedParts().ToList();
            Check(placed.Count == 2, "scene graph has exactly 2 placed occurrences, found " + placed.Count);
            Check(view.Instances.Count == 2, "CadSceneView built exactly 2 instances, found " + view.Instances.Count);
            Check(view.Instances.Select(i => i.OccurrenceId).Distinct().Count() == 2, "the 2 instances are distinct occurrences");
            foreach (var instance in view.Instances)
            {
                Check(instance.Bodies.Count > 0 && instance.Bodies.All(b => b.Mesh != null && b.Mesh.vertexCount > 0 && b.Primitive.FaceMap.Count > 0),
                    "instance " + instance.OccurrenceId + " has meshes with vertices and a face map");
            }
            Check(Mathf.Abs(view.transform.lossyScale.x - 1f) < 1e-4f && Mathf.Abs(view.transform.localScale.x - 1f) < 1e-4f,
                "scene root is at scale 1:1, found " + F(view.transform.lossyScale.x));
            var scale = view.transform.lossyScale.x;
            var boundsA = InstanceBounds(view.transform, view.Instances[0]);
            var boundsB = InstanceBounds(view.transform, view.Instances[1]);
            var longA = Math.Max(boundsA.size.x, Math.Max(boundsA.size.y, boundsA.size.z)) * scale;
            var longB = Math.Max(boundsB.size.x, Math.Max(boundsB.size.y, boundsB.size.z)) * scale;
            Check(Math.Abs(longA - LongSideM) <= LongSideToleranceM && Math.Abs(longB - LongSideM) <= LongSideToleranceM,
                "block long side is 0.100 m +/- 0.001 at 1:1, measured " + F(longA) + " and " + F(longB) + " m");
            var centerDistanceMm = Vector3.Distance(boundsA.center, boundsB.center) * scale * 1000.0;
            Check(Math.Abs(centerDistanceMm - 150.0) <= 1.0, "occurrence centers are 150 mm apart, measured " + F(centerDistanceMm) + " mm");
            var delta = boundsB.center - boundsA.center;
            var axis = Math.Abs(delta.x) >= Math.Abs(delta.y) && Math.Abs(delta.x) >= Math.Abs(delta.z) ? 0 : Math.Abs(delta.y) >= Math.Abs(delta.z) ? 1 : 2;
            var gapMm = (Math.Abs(delta[axis]) - (boundsA.size[axis] + boundsB.size[axis]) * 0.5) * scale * 1000.0;
            Check(Math.Abs(gapMm - GapMm) <= 1.0, "gap between the blocks is 50 mm, measured " + F(gapMm) + " mm");
            var sceneExtent = ScenePlacement.LocalBounds(view.transform).size;
            Pass("M1-DoD4", "2 occurrences with meshes; block long side " + F(longA * 1000) + " / " + F(longB * 1000)
                + " mm at scale " + F(scale) + "; centers " + F(centerDistanceMm) + " mm apart, gap " + F(gapMm)
                + " mm; whole scene " + F(sceneExtent.x * scale * 1000) + " x " + F(sceneExtent.y * scale * 1000) + " x " + F(sceneExtent.z * scale * 1000) + " mm");

            // DoD 5 and 7: pick an occurrence as AppController.OnPicked does, with a real body and a real mesh triangle.
            RequireFixture();
            var target = view.Instances[0];
            Check(TryFindPick(view.transform, target, out var pickedBody, out var triangle, out var viaRay),
                "found a real body and triangle to pick on the first occurrence");
            Record("Pick target " + target.OccurrenceId + ", body " + pickedBody.name + ", triangle " + triangle + (viaRay ? " (physics ray)" : " (first triangle with a face)"));
            Check(selection.Current.Kind == SelectionKind.None, "nothing is selected before the pick");
            Call(App, "OnPicked", pickedBody, triangle);
            await WaitUntil(() => !ReadBoolean(App, "_selecting") && !ReadBoolean(inspect, "_busy"), ct);
            var current = selection.Current;
            Check(current.Kind == SelectionKind.Occurrence && current.OccurrenceId == target.OccurrenceId,
                "SelectionService holds the picked occurrence " + target.OccurrenceId + ", found " + current.Kind + " " + current.OccurrenceId);
            Check(!string.IsNullOrEmpty(current.EntityId), "the picked occurrence carries an entity id resolved for Inventor");
            var tinted = Read<List<(Renderer renderer, Material original)>>(visuals, "_tinted");
            Check(tinted.Count == target.Bodies.Count && tinted.All(t => t.renderer != null && t.renderer.transform.IsChildOf(target.transform)),
                "selection visuals tint exactly the picked occurrence (" + tinted.Count + " bodies)");
            Check(view.Instances.Where(i => i != target).All(i => i.Bodies.All(b => tinted.All(t => t.renderer != b.Renderer))),
                "the other occurrence is not tinted");
            var backend = Read<IInventorBackend>(selection, "_backend");
            Check(backend != null, "selection service exposes the backend");
            await backend.ClearHighlightAsync(ct);
            await backend.HighlightAsync(new[] { current.EntityId }, ct);
            Pass("M1-DoD5", "OnPicked on occurrence " + target.OccurrenceId + " (triangle " + triangle + ") left it selected and tinted locally");
            Pass("M1-DoD7", "local highlight present and the backend highlight request for " + current.EntityId + " was accepted by Inventor");
            await CaptureScreenshot("selection", ct);

            // DoD 6: face resolved from the triangle, locally; then the face selection the second click performs.
            var face = pickedBody.Primitive.FaceMap.FaceAtTriangle(triangle);
            Check(face != null && !string.IsNullOrEmpty(face.FaceId), "triangle " + triangle + " maps to a face id without a round trip");
            RequireFixture();
            await selection.SelectAsync("assembly", target.OccurrenceId, face.FaceId, ct);
            current = selection.Current;
            Check(current.Kind == SelectionKind.Face && current.FaceId == face.FaceId && !string.IsNullOrEmpty(current.EntityId),
                "second selection on the same occurrence resolves the face " + face.FaceId);
            var overlays = Read<List<(GameObject overlay, Mesh mesh)>>(visuals, "_overlays");
            Check(overlays.Count > 0, "selection visuals draw the face overlay");
            Pass("M1-DoD6", "triangle " + triangle + " -> face " + face.FaceId + " (ordinal " + face.Ordinal + "); backend face entity " + current.EntityId + "; face overlay drawn");

            // MR <-> Studio VR through the same buttons the Inspect menu shows.
            var panel = Read<HomePanel>(inspect, "_panel");
            Check(environment.Mode == EnvironmentMode.MixedReality, "session started in Mixed Reality");
            var eye = Read<Camera>(environment, "eye");
            Check(eye != null && eye.backgroundColor.a < 0.01f, "Mixed Reality clears the camera to a transparent background");
            inspect.Open("tools");
            Click(panel, "Studio virtuale");
            Check(environment.Mode == EnvironmentMode.StudioVr && eye.backgroundColor.a > 0.99f, "menu switched to Studio VR with an opaque studio background");
            Click(panel, "Realtà mista");
            Check(environment.Mode == EnvironmentMode.MixedReality && eye.backgroundColor.a < 0.01f, "menu switched back to Mixed Reality");
            Pass("M1-MR", "Mixed Reality -> Studio VR -> Mixed Reality via the Inspect menu; camera background followed each mode");

            // Leave Inventor without a highlight.
            await selection.ClearAsync(ct);
            await backend.ClearHighlightAsync(ct);
            Check(selection.Current.Kind == SelectionKind.None, "selection cleared at the end of the run");

            Record("NOT COVERED [M1-DoD8] a document change made on the desktop is not induced by this runner");
            Record("NOT COVERED [M1-DoD9] network loss and reconnection are not induced by this runner");
            Record("NOT COVERED [M1-Pairing] QR and code pairing are not exercised; the stored pairing is reused");
            NotCovered("M1-Physical", "controller ray, trigger, headset fit and visual readability require a person wearing the Quest");
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

        private static bool TryFindPick(Transform root, CadInstance instance, out CadBody body, out int triangle, out bool viaRay)
        {
            Physics.SyncTransforms();
            var center = root.TransformPoint(InstanceBounds(root, instance).center);
            var ray = new Ray(center + Vector3.up * 1f, Vector3.down);
            if (CadRaycaster.TryPick(ray, 3f, out body, out triangle, out _) && body.Instance == instance)
            {
                viaRay = true;
                return true;
            }
            viaRay = false;
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

        private static void Click(HomePanel panel, string label)
        {
            var button = panel.GetComponentsInChildren<Button>(false)
                .LastOrDefault(b => b.gameObject.activeInHierarchy && b.GetComponentInChildren<Text>()?.text == label);
            Check(button != null, "menu button '" + label + "' is shown");
            Check(button.interactable, "menu button '" + label + "' is enabled");
            button.onClick.Invoke();
        }
    }
}
#endif
