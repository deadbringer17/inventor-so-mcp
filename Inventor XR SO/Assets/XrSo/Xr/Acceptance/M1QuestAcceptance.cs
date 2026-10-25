#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Capture;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using ActionCatalog = InventorXrSo.Core.Ui.ActionCatalog;
using XrAction = InventorXrSo.Core.Ui.XrAction;
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
            "AppController._catalog",
            "AppController._selection",
            "AppController._selecting",
            "AppController._inSession",
            "AppController._capture",
            "AppController._shell",
            "AppController._input",
            "AppController._voice",
            "AppController._legend",
            "AppController.ray",
            "AppController.sceneView",
            "AppController.selectionVisuals",
            "AppController.environment",
            "AppController.RefreshHome",
            "AppController.LeaveSession",
            "AppController.EnterSession",
            "AppController.CloseAuthoring",
            "AppController.OnPicked",
            "HomePanel._title",
            "HomePanel._body",
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

            await CheckRealityCaptureSection(home, ct);

            // Enter the session as the Home button does.
            App.RequestActivity(InventorXrSo.Core.Experience.Activity.Verification);
            Call(App, "EnterSession", EnvironmentMode.MixedReality);
            Check(ReadBoolean(App, "_inSession"), "EnterSession switched the app into the session");
            // QX: explicit Verifica entry owns Ispeziona selection and the 1:1 scene; document changes cannot open authoring.
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

            // MR <-> Studio VR through the Ispeziona action (Vista tab), invoked by id. The tap is SYNTHETIC: no controller ray or hand.
            Check(environment.Mode == EnvironmentMode.MixedReality, "session started in Mixed Reality");
            var eye = Read<Camera>(environment, "eye");
            Check(eye != null && eye.backgroundColor.a < 0.01f, "Mixed Reality clears the camera to a transparent background");
            Check(inspect.Active && ReferenceEquals(Read<ActionCatalog>(App, "_catalog").Active, inspect), "Ispeziona is the workspace on the palette");
            Record("Environment switched by invoking the action '" + InspectWorkspace.IdEnvironment + "' by id (synthetic input)");
            RunAction(InspectWorkspace.IdEnvironment);
            Check(environment.Mode == EnvironmentMode.StudioVr && eye.backgroundColor.a > 0.99f, "action switched to Studio VR with an opaque studio background");
            RunAction(InspectWorkspace.IdEnvironment);
            Check(environment.Mode == EnvironmentMode.MixedReality && eye.backgroundColor.a < 0.01f, "action switched back to Mixed Reality");
            Pass("M1-MR", "Mixed Reality -> Studio VR -> Mixed Reality via the Ispeziona action (synthetic tap); camera background followed each mode");

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

        private async Task CheckRealityCaptureSection(HomePanel home, CancellationToken ct)
        {
            Record("RC0 navigation uses SYNTHETIC UI callbacks on the dedicated M1 fixture; no physical gesture or image acquisition.");
            var capture = Read<RealityCaptureWorkspace>(App, "_capture");
            var shell = Read<UiShell>(App, "_shell");
            var input = Read<InventorXrSo.Xr.Input.XrInput>(App, "_input");
            var voice = Read<InventorXrSo.Xr.Voice.VoiceRig>(App, "_voice");
            var view = Read<CadSceneView>(App, "sceneView");
            var ray = Read<ControllerRay>(App, "ray");
            var revision = Session.Scene.Graph.Revision;
            try
            {
                Check(home.InvokeVoiceAction(UiText.RealityCapture), "Home exposes the Reality Capture entry");
                Check(capture.Active && !home.gameObject.activeSelf && !ReadBoolean(App, "_inSession"), "independent section is open");
                Check(Read<EnvironmentModeController>(App, "environment").Mode == EnvironmentMode.MixedReality, "capture selects passthrough");
                Check(!view.gameObject.activeSelf && !shell.Palette.gameObject.activeSelf && !shell.CommitBar.gameObject.activeSelf
                    && !shell.Hud.gameObject.activeSelf, "CAD scene and canvases are hidden");
                Check(!input.enabled && !ray.CanPick && !Read<InspectWorkspace>(App, "_inspect").Active,
                    "CAD semantic input, picking and inspect tools are suspended");
                Check(voice == null || !voice.gameObject.activeSelf && !voice.Panel.gameObject.activeSelf,
                    "CAD voice and its detached panel are suspended");
                Check(!Read<ControllerLegend>(App, "_legend").Shown, "CAD controller legend is hidden");
                Read<ActionCatalog>(App, "_catalog").NotifyChanged();
                Call(App, "RefreshHome");
                await Task.Delay(200, ct);
                Check(capture.Active && !home.gameObject.activeSelf && !shell.Palette.gameObject.activeSelf
                    && !shell.CommitBar.gameObject.activeSelf, "CAD refresh does not reopen its UI");
                Check(capture.Panel.InvokeVoiceAction(UiText.CaptureReferences), "capture has its own references page");
                Check(capture.Panel.InvokeVoiceAction(UiText.RealityCapture), "references return to capture overview");
                Check(capture.Panel.InvokeVoiceAction(UiText.CaptureNewObject), "RC1 object setup is available");
                Check(!capture.Panel.InvokeVoiceAction(UiText.CaptureStart), "RC1 cannot record before volume confirmation");
                Check(capture.Panel.InvokeVoiceAction(UiText.Cancel), "RC1 volume setup cancels without camera access");
                await CheckRealityCaptureStorage(ct);
                await CheckRealityCaptureReviewSynthetic(capture,ct);
                await CaptureScreenshot("reality-capture", ct);
                Pass("RC0-01", "paired Home opens capture by synthetic UI callback; unpaired entry is covered by EditMode only");
                Pass("RC0-02", "passthrough selected; CAD scene, palette, confirm bar, HUD and legend hidden");
                Pass("RC0-03", "CAD semantic input, picking and voice suspended; capture's own UI callbacks available");
                Pass("RC0-04", "catalog and Home refresh preserve capture; network failures are covered by EditMode only");
                Check(capture.Panel.InvokeVoiceAction(UiText.ReturnHome) && !capture.Active && home.gameObject.activeSelf,
                    "return to Home works");
                Check(home.InvokeVoiceAction(UiText.RealityCapture) && capture.Active, "repeated entry works");
                Check(capture.Panel.InvokeVoiceAction(UiText.ReturnHome) && !capture.Active, "repeated return works");
                App.RequestActivity(InventorXrSo.Core.Experience.Activity.Verification);
                Call(App, "EnterSession", EnvironmentMode.StudioVr);
                Check(!capture.Active && view.gameObject.activeSelf && shell.Palette.gameObject.activeSelf && input.enabled && ray.CanPick,
                    "returning to CAD restores its shell and input");
                Check(Session.Scene.Graph.Revision == revision, "section navigation does not change the CAD revision");
                Pass("RC0-05", "Home/capture/Home/CAD transitions restore shell and input without changing CAD revision");
                NotCovered("RC1-camera", "real RGB acquisition, native timestamp/pose alignment, permission denial and tracking need a separate device run; synthetic storage is not camera evidence");
                NotCovered("RC2-transfer", "the M1 runner does not upload scans; resumable transfer and token isolation have separate HTTPS software tests");
                NotCovered("RC2-physical-reconstruction", "RGB reconstruction and metrology on physical objects have not been exercised by this runner; PC synthetic software tests are separate evidence");
                NotCovered("RC3-physical", "scan readability, physical point picking and dimensional repeatability require device and independent physical controls");
                NotCovered("RC4-live-CAD", "RC4 native reference creation is implemented but not exercised by this M1 navigation/renderer fixture");
                NotCovered("RC0-physical", "physical menu/trigger, readability, passthrough and tracking require a physical test");
            }
            finally { Call(App, "LeaveSession"); }
        }

        private async Task CheckRealityCaptureReviewSynthetic(RealityCaptureWorkspace capture,CancellationToken ct)
        {
            Record("RC3 viewer uses a SYNTHETIC triangle/point fixture in memory; no camera, PC solve or dimensional validation is exercised.");
            var scan=capture.OpenSyntheticReviewFixture();
            var fixture=InventorXrSo.Xr.Capture.ScanReviewFixture.Create();
            scan.SelectIds(new[]{"p0","p1"});
            Check(scan.Selected.Count==2 && Math.Abs(ScanGeometry.Distance(fixture.Points[0].Position,fixture.Points[1].Position)-1000)<1e-6,"RC3 original scan coordinates retain millimetres despite display reduction");
            var target=scan.transform.TransformPoint(scan.DisplayPoint(fixture.Points[2].Position));
            var syntheticRay=new Ray(target-Vector3.forward,Vector3.forward);
            Check(!capture.ProcessReviewRay(syntheticRay,true,true,false,true) && !scan.Selected.Contains("p2"),"RC3 synthetic UI hit prevents click-through");
            Check(capture.ProcessReviewRay(syntheticRay,true,true,false,false) && scan.Selected.Contains("p2"),"RC3 synthetic input selects original point ID without CAD picking");
            scan.SelectWeak();Check(scan.Selected.Count==2 && !scan.Selected.Contains("p1"),"RC3 weak observation selection uses two-view support, not a coverage claim");
            var plane=ScanGeometry.Fit("qa_plane","plane",fixture.Points);
            Check(plane.FitRms<1e-6 && plane.Uncertainty==null,"RC3 fitting scatter remains distinct from unknown measurement uncertainty");
            var source=fixture.Points.Select(p=>p.Position).ToArray();var basis=ScanRegistration.ScanToCadBasis;basis[3]=120;basis[7]=230;basis[11]=340;
            var targets=source.Select(p=>ScanRegistration.Point(basis,p)).ToArray();var registration=ScanRegistration.ThreePoints(source,targets);
            Check(ScanRegistration.Residuals(registration,source,targets).Max()<1e-6,"RC4-01 SYNTHETIC LH scan to RH CAD registration preserves millimetres without scale fitting");
            var inverse=ScanRegistration.Inverse(registration);var fourth=new[]{25d,35,45};
            Check(ScanGeometry.Distance(fourth,ScanRegistration.Point(inverse,ScanRegistration.Point(registration,fourth)))<1e-6,"RC4-01 SYNTHETIC inverse registration retains an off-plane point");
            var cadCopyObject=new GameObject("RC4 SYNTHETIC comparison of dedicated fixture");
            try
            {
                var copy=cadCopyObject.AddComponent<InventorXrSo.Xr.Capture.CaptureCadOverlayView>();
                copy.Show(Session.Scene,Matrix4x4.TRS(scan.transform.position,scan.transform.rotation,Vector3.one*.25f));
                Check(copy.DocumentId==Session.Scene.Graph.DocumentId && cadCopyObject.GetComponentsInChildren<Collider>().Length==0 && cadCopyObject.GetComponentsInChildren<CadBody>().Length==0,"RC4-05 SYNTHETIC CAD copy has no native picking or edit state");
                copy.Clear();Check(!cadCopyObject.activeSelf,"RC4-07 SYNTHETIC comparison cleanup removes its rendering");
            }
            finally {UnityEngine.Object.Destroy(cadCopyObject);}
            Pass("RC4-software-synthetic","registration basis/inverse and read-only rendering of the dedicated CAD fixture exercised; no native import or physical alignment");
            NotCovered("RC4-native-reference","native reference preview/commit/save requires its own experimental Inventor fixture; PC stand-in transaction tests are separate evidence");
            NotCovered("RC4-physical-overlay","controller origin placement, 1:1 registration, tracking loss/recenter and as-designed/as-built accuracy require the physical device run");
            Check(!Read<ControllerRay>(App,"ray").CanPick && !Read<CadSceneView>(App,"sceneView").gameObject.activeSelf,"RC3 scan leaves CAD input and scene suspended");
            await CaptureScreenshot("rc3-review-synthetic",ct);
            Check(capture.Panel.InvokeVoiceAction(UiText.RealityCapture) && !scan.gameObject.activeSelf,"RC3 leaving review clears its geometry");
            Pass("RC3-viewer-synthetic","independent viewer, original point IDs, synthetic ray picking, mm measures, weak support selection, fitting uncertainty and cleanup exercised in memory");
            NotCovered("RC3-PC-review", "token-owned annotations, revision CAS, export and solve rerun are covered by PC HTTPS tests, not this in-memory device fixture");
        }

        private async Task CheckRealityCaptureStorage(CancellationToken ct)
        {
            string root = Path.Combine(Application.temporaryCachePath,"rc1-synthetic-" + Guid.NewGuid().ToString("N"));
            Record("RC1 storage uses SYNTHETIC RGB/quality/pose in a temporary sandbox; no headset camera or geometric coverage is exercised.");
            try
            {
                await Task.Run(() =>
                {
                    long free = long.MaxValue;
                    var store = new LocalScanStore(root,10000000,0,() => free);
                    var session = new ScanSession(Guid.NewGuid().ToString("N"),CaptureMode.Object,"synthetic-runner","1",
                        DateTimeOffset.UtcNow,new CaptureVolume(CaptureTransform.Identity,600,600,600),CaptureTransform.Identity);
                    var recorder = new ScanRecorder(store,session);
                    var intrinsics = new CaptureIntrinsics(16,16,12,12,8,8);
                    var quality = new CaptureQuality(120,0.01,100,0.3);
                    CaptureFrame Frame(long timestamp,double x,bool tracked = true)
                    {
                        var matrix = CaptureTransform.Identity.Values; matrix[3] = x;
                        return new CaptureFrame(timestamp,"synthetic-left",intrinsics,new CaptureTransform(matrix),
                            new CaptureTransform(matrix),CaptureTransform.Identity,quality,tracked);
                    }
                    var pixels = new byte[16*16*3];
                    recorder.Start();
                    if (!recorder.Record(Frame(1000000,0),pixels)) throw new InvalidOperationException("Synthetic first frame rejected.");
                    recorder.Pause();
                    if (recorder.Record(Frame(2000000,100),pixels)) throw new InvalidOperationException("Paused recorder accepted a frame.");
                    recorder.Start();
                    if (!recorder.Record(Frame(2000000,100),pixels)) throw new InvalidOperationException("Synthetic resume failed.");
                    free = 1;
                    if (recorder.Record(Frame(3000000,200),pixels) || recorder.State != CaptureState.Interrupted || recorder.LastReason != "storage_full")
                        throw new InvalidOperationException("Storage exhaustion did not stop capture.");
                    var reopened = new LocalScanStore(root,10000000,0,() => long.MaxValue);
                    if (reopened.Verify(session.SessionId) != 2 || reopened.GetStatus(session.SessionId).State != CaptureState.Interrupted)
                        throw new InvalidOperationException("Raw recovery or hashes failed.");
                },ct);
                Pass("RC1-raw-synthetic", "temporary raw frames retain hashes, metadata and sequence after reopen; no real image input");
                Pass("RC1-lifecycle-synthetic", "pause rejects frames, explicit resume preserves baseline, storage exhaustion interrupts without losing saved raw");
            }
            finally { if (Directory.Exists(root)) await Task.Run(() => Directory.Delete(root,true)); }
        }

        /// <summary>Invokes a declared action by id through the catalog, the same path as palette, ring and voice.</summary>
        private void RunAction(string id)
        {
            var action = Read<ActionCatalog>(App, "_catalog").Find(id);
            Check(action != null, "the action catalog has no action '" + id + "'");
            Check(action.Enabled, "action '" + id + "' is disabled: " + action.DisabledReason);
            Check(action.TryInvoke(), "action '" + id + "' did not run");
        }
    }
}
#endif
