#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr.Input;
using InventorXrSo.Xr.Voice;
using UnityEngine;
using UiSelectionKind = InventorXrSo.Core.Ui.SelectionKind;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Opt-in, fixture-scoped M6 (spatial UX) acceptance runner for a dedicated Quest build. It exercises the reproducible
    /// sub-cases of the gates M6-01...M6-08 on the real <see cref="ActionCatalog"/>: every command is invoked by action id (the
    /// path of palette, ring, commit bar and voice) and every controller event is a frame of a <see cref="SyntheticInputSource"/>
    /// installed on the application's own <see cref="XrInput"/>. All of that input is SYNTHETIC, never a person's hand: the
    /// physical gate M6-10 and everything that needs a body (ergonomics, real tracking, microphone) stays open.
    /// The fixture is one assembly with two parts (a block and a sheet-metal sheet, both kept open): Ispeziona and Assieme run on
    /// the assembly, Progettazione on the block and Lamiera on the sheet, reached the way a user does (Apri in ... from the
    /// isolated component).
    /// </summary>
    internal sealed class M6QuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers =
        {
            "AppController._inSession",
            "AppController._catalog",
            "AppController._inspect",
            "AppController._design",
            "AppController._assembly",
            "AppController._lamiera",
            "AppController._shell",
            "AppController._workbench",
            "AppController._sheet",
            "AppController._input",
            "AppController._voiceTarget",
            "AppController.ray",
            "AppController.head",
            "AppController.sceneView",
            "AppController.EnterSession",
            "DesignWorkspace._backend",
            "DesignWorkspace._session",
            "DesignWorkspace._context",
            "DesignWorkspace._history",
            "DesignWorkspace._busy",
            "DesignWorkspace._pendingMutations",
            "DesignWorkspace._previewView",
            "DesignWorkspace._sketch",
            "DesignWorkspace._chip",
            "DesignWorkspace._ring",
            "DesignWorkspace._ringEdge",
            "DesignWorkspace._edges",
            "DesignWorkspace._face",
            "AssemblyWorkspace._backend",
            "AssemblyWorkspace._context",
            "AssemblyWorkspace._occurrence",
            "AssemblyWorkspace._busy",
            "AssemblyWorkspace._ring",
            "AssemblyWorkspace._grab",
            "AssemblyWorkspace._twoHandActive",
            "AssemblyWorkspace.MoveGrab",
            "AssemblyWorkspace.TwoHandStep",
            "LamieraWorkspace._busy",
            "LamieraWorkspace._designContext",
            "LamieraWorkspace._previewView",
            "LamieraWorkspace._pendingMutations",
            "LamieraWorkspace._manip",
            "LamieraWorkspace._view",
            "LamieraWorkspace.DragStep",
        };

        private const string SketchName = "Base_M6";
        private const string SpacesInspect = "spaces.inspect", SpacesDesign = "spaces.design",
            SpacesLamiera = "spaces.lamiera", SpacesAssembly = "spaces.assembly";
        private const double FlangeHeightMm = 20;
        private const double DictatedHeightMm = 20.5;
        private const string DictatedPhrase = "venti virgola cinque";

        protected override string Milestone => "m6";
        protected override int TimeoutSeconds => 720;
        protected override string CompletionNote =>
            "SYNTHETIC input only: physical controller use, ergonomics, real tracking, microphone, M6-07 height calibration and the M6-10 sitting trial were not exercised by this runner";

        // ---- application parts (read once through reflection)
        private InspectWorkspace _inspect;
        private DesignWorkspace _design;
        private AssemblyWorkspace _assembly;
        private LamieraWorkspace _lamiera;
        private UiShell _shell;
        private Workbench _bench;
        private SketchSheetView _sheet;
        private CadSceneView _view;
        private ControllerRay _ray;
        private Transform _head;
        private IDesignWorkspaceBackend _backend;
        private IAssemblyWorkspaceBackend _assemblyBackend;
        private string _assemblyDocId;

        // ---- synthetic input
        private XrInput _input;
        private SyntheticInputSource _source;
        private IXrInputSource _originalSource;
        private int _fitEvents, _recenterEvents;
        private bool _syntheticKeypadNoted;

        private ActionCatalog Catalog => Read<ActionCatalog>(App, "_catalog");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M6QuestAcceptance>("xr_m6_acceptance");

        // ------------------------------------------------------------------------------------------------------------- run

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            Check(fixture.Graph.Root.Name.StartsWith(FixturePrefix, StringComparison.Ordinal),
                "connected document is the dedicated M6 acceptance fixture");
            Check(fixture.Graph.Kind == "assembly", "the M6 fixture opens as an assembly (found " + fixture.Graph.Kind + ")");
            _assemblyDocId = fixture.Graph.DocumentId;
            Record("PASS; dedicated fixture loaded: " + fixture.Graph.Root.Name);

            if (!ReadBoolean(App, "_inSession")) Call(App, "EnterSession", EnvironmentMode.StudioVr);
            _inspect = Read<InspectWorkspace>(App, "_inspect");
            _design = Read<DesignWorkspace>(App, "_design");
            _assembly = Read<AssemblyWorkspace>(App, "_assembly");
            _lamiera = Read<LamieraWorkspace>(App, "_lamiera");
            _shell = Read<UiShell>(App, "_shell");
            _bench = Read<Workbench>(App, "_workbench");
            _sheet = Read<SketchSheetView>(App, "_sheet");
            _view = Read<CadSceneView>(App, "sceneView");
            _ray = Read<ControllerRay>(App, "ray");
            _head = Read<Transform>(App, "head");
            Check(_inspect != null && _design != null && _assembly != null && _lamiera != null && _shell != null && _bench != null
                && _sheet != null && _view != null && _ray != null && _head != null && Catalog != null,
                "AppController exposes the four workspaces, the shell, the workbench, the sheet view and the ray");
            _backend = Read<IDesignWorkspaceBackend>(_design, "_backend");
            _assemblyBackend = Read<IAssemblyWorkspaceBackend>(_assembly, "_backend");
            Check(_backend != null && _assemblyBackend != null && _backend is IInspectionBackend,
                "the shared backend serves Design, Assembly and document activation");

            BeginSynthetic();
            try
            {
                await CheckShellAsync(ct);          // M6-01
                await CheckAssemblyAsync(ct);       // M6-04 (component), M6-06, M6-07; ends in Progettazione (Apri in Progettazione)
                await CheckDesignAsync(ct);         // M6-01, M6-02, M6-03, M6-04, M6-05
                await OpenLamieraAsync(ct);         // M6-06 (Apri in Lamiera)
                await CheckLamieraAsync(ct);        // M6-01, M6-03, M6-05, M6-08
                await CheckVoiceCatalogAsync(ct);   // M6-08
                CheckCommitBarCore();               // M6-05 (Stale, pure table)
            }
            finally
            {
                await CleanupAsync();
            }

            NotCovered("M6-10", "physical sitting trial with the controllers: legibility at 7 mm, comfort over a 30 minute session, pen precision on the sheet, haptics, Trigger drag and release (M5-08) need a person wearing the Quest; every event above was a SYNTHETIC XrInput frame");
            NotCovered("M6-01-physical", "palette pose under the real left controller, text legibility and wrist comfort: only parentage and tab switching by synthetic stick flicks were checked");
            NotCovered("M6-02-physical", "real pen-tip precision and height above the sheet: the synthetic tip was placed exactly on the plane and a second line was drawn with the ray");
            NotCovered("M6-03-physical", "thumbstick feel (flick thresholds, accidental repeats), real keypad pointing and the microphone: dictation here is injected text, not audio");
            NotCovered("M6-04-Inspect", "Ispeziona has no contextual ring yet (it needs the picked point of the ControllerRay path, which still reads OVRInput)");
            NotCovered("M6-05-Stale-live", "a live STALE_REVISION on the commit bar needs a revision change of the active document; the backend rejection is asserted by M3-Stale and M5-07, here only the pure state table (M6-05-core) is checked");
            NotCovered("M6-07-calibration", "no action or gesture calls Workbench.SetDeskHeight (the API exists, nothing in the app uses it) and a real calibration needs a hand at table height");
            NotCovered("M6-07-physical", "one and two hand manipulation with real tracking, drift and comfort: grabs here were synthetic frames with the hand transforms placed by the runner");
            NotCovered("M6-08-physical", "push-to-talk button B, microphone and audio: recognized text was injected into the real PushToTalkController with silent samples");
            NotCovered("M6-09", "this runner does not certify the migrated M1-M5 runners: they have to be re-run on the Quest with Inventor (PASS COMPLETE each) before the gate can close");
        }

        // ------------------------------------------------------------------------------------------------------ plumbing

        /// <summary>Invokes a declared action by id through the catalog: the path of palette, ring, commit bar and voice (synthetic tap).</summary>
        private void RunAction(string id)
        {
            var action = Catalog.Find(id);
            Check(action != null, "the action catalog has no action '" + id + "'");
            Check(action.Enabled, "action '" + id + "' is disabled: " + action.DisabledReason);
            Check(action.TryInvoke(), "action '" + id + "' did not run");
        }

        private bool ActionEnabled(string id) => Catalog.Find(id)?.Enabled == true;

        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool Near(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;

        private static async Task<bool> TryWaitUntil(Func<bool> condition, double seconds, CancellationToken ct)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < until)
            {
                ct.ThrowIfCancellationRequested();
                if (condition()) return true;
                await Task.Delay(100, ct);
            }
            return condition();
        }

        private async Task<DocumentState> BaselineAsync(CancellationToken ct) => await _backend.GetDocumentStateAsync(ct);

        private async Task AssertUnchanged(DocumentState baseline, string what, CancellationToken ct)
        {
            var now = await _backend.GetDocumentStateAsync(ct);
            Check(now.DocumentId == baseline.DocumentId && now.Revision == baseline.Revision,
                what + " leaves Inventor revision unchanged (" + baseline.Revision + " vs " + now.Revision + ")");
        }

        /// <summary>Types a value on the open keypad entry of the active workspace (synthetic keypad input).</summary>
        private void TypeValue(double value)
        {
            if (!_syntheticKeypadNoted) { Record("Numbers are typed on the keypad entry by the runner (synthetic keypad input)"); _syntheticKeypadNoted = true; }
            var entry = ActiveEntry();
            Check(entry != null && entry.Editing, "the numeric keypad is open");
            foreach (char c in F(value)) entry.Type(c);
            Check(entry.Commit(out var reason), "keypad accepted " + F(value) + ": " + reason);
        }

        private NumericEntry ActiveEntry()
        {
            var active = Catalog.Active;
            if (active is DesignWorkspace design) return design.ActiveEntry;
            if (active is LamieraWorkspace lamiera) return lamiera.ActiveEntry;
            if (active is AssemblyWorkspace assembly) return assembly.ActiveEntry;
            if (active is InspectWorkspace inspect) return inspect.ActiveEntry;
            return null;
        }

        /// <summary>Chooses an entry of the open list of a workspace by label (exact, or by suffix).</summary>
        private void PickItem(IActionProvider workspace, string prefix, string label, bool suffix = false)
        {
            var action = workspace.Actions.FirstOrDefault(a => a.Id.StartsWith(prefix, StringComparison.Ordinal)
                && (suffix ? a.Label.EndsWith(label, StringComparison.Ordinal) : a.Label == label));
            Check(action != null, "the open list has no entry '" + label + "'");
            Check(action.Enabled && action.TryInvoke(), "list entry '" + label + "' did not run");
        }

        private Transform LeftAnchor() => _head.parent == null ? null
            : _head.parent.Find("LeftHandAnchor/LeftControllerAnchor") ?? _head.parent.Find("LeftHandAnchor");

        private void SnapPoses() { _bench.Snap(); _sheet.Snap(); }

        // ----------------------------------------------------------------------------------------------- synthetic input

        /// <summary>
        /// Replaces the source of the application's own XrInput (its real wiring to the palette tabs and to every workspace stays):
        /// every controller event of this run is a frame the runner sends. Restored in <see cref="EndSynthetic"/>.
        /// </summary>
        private void BeginSynthetic()
        {
            _input = Read<XrInput>(App, "_input");
            Check(_input != null, "AppController owns the XrInput");
            _originalSource = _input.Source;
            _source = new SyntheticInputSource { Next = Rest() };
            _input.Source = _source;
            Check(_input.Synthetic, "the input source of the run is flagged synthetic");
            _input.Fit += OnFit;
            _input.Recenter += OnRecenter;
            _assembly.UiHitOverride = () => false;   // deterministic UI hit: the real controller's ray must not decide the gesture
            _lamiera.UiHitOverride = () => false;
            _design.UiHitOverride = () => false;
            Send(Rest());
            Record("Controller events of this run are SYNTHETIC XrInput frames (palette stick, pen trigger/grip, X, Y); the hand transforms are placed by the runner");
        }

        private void EndSynthetic()
        {
            if (_input == null) return;
            if (_source != null) Send(Rest());
            _input.Fit -= OnFit;
            _input.Recenter -= OnRecenter;
            if (_originalSource != null) _input.Source = _originalSource;
            if (_assembly != null) _assembly.UiHitOverride = null;
            if (_lamiera != null) _lamiera.UiHitOverride = null;
            if (_design != null) _design.UiHitOverride = null;
            _source = null;
        }

        private void OnFit() { _fitEvents++; }
        private void OnRecenter() { _recenterEvents++; }

        private static XrInputFrame Rest() => new XrInputFrame { PenTracked = true, PaletteTracked = true };

        /// <summary>One synthetic controller frame: the component's own Update polls the same state, so no phantom edge follows.</summary>
        private void Send(XrInputFrame frame)
        {
            _source.Next = frame;
            _input.Poll(frame, Time.unscaledTime);
        }

        private void SendRest() => Send(Rest());

        /// <summary>Left stick flick: next (+1) or previous (-1) palette tab.</summary>
        private void FlickPalette(int direction)
        {
            var frame = Rest(); frame.PaletteStick = new Vector2(direction, 0); Send(frame); SendRest();
        }

        /// <summary>Right stick flick: x = one step on the armed chip, y = step size 0,1 / 1 / 10.</summary>
        private void FlickPen(float x, float y)
        {
            var frame = Rest(); frame.PenStick = new Vector2(x, y); Send(frame); SendRest();
        }

        private void PressX()
        {
            var frame = Rest(); frame.X = true; Send(frame); SendRest();
        }

        /// <summary>A full pen Trigger click (release first: the events are edge based).</summary>
        private void TriggerTap()
        {
            SendRest();
            var frame = Rest(); frame.PenTrigger = true; Send(frame);
            SendRest();
        }

        /// <summary>Places the pen hand at <paramref name="target"/> + <paramref name="away"/> * distance, looking at the target.</summary>
        private void AimAt(Vector3 target, Vector3 away, float distance = 0.3f)
        {
            var direction = away.normalized;
            _ray.Origin.SetPositionAndRotation(target + direction * distance, Quaternion.LookRotation(-direction));
        }

        /// <summary>Direction from the target towards the user, lifted a little so the ray comes from slightly above.</summary>
        private Vector3 AwayFromHead(Vector3 target)
        {
            var away = _head.position - target;
            if (away.sqrMagnitude < 1e-6f) away = Vector3.back;
            return new Vector3(away.x, Mathf.Max(0.2f, away.y), away.z);
        }

        // ------------------------------------------------------------------------------------------------- M6-01: shell

        private async Task CheckShellAsync(CancellationToken ct)
        {
            var left = LeftAnchor();
            Check(left != null, "the left controller anchor exists");
            Check(_shell.Palette.Canvas.transform.IsChildOf(left), "the palette is parented to the left controller");
            Check(!_shell.Palette.Canvas.transform.IsChildOf(_head), "the palette is not a head-locked panel");
            Check(ReferenceEquals(Catalog.Active, _inspect), "Ispeziona is the default workspace of the catalog: the palette is never empty");
            CheckPalette("Ispeziona", _inspect,
                new[] { InspectWorkspace.TabMeasure, InspectWorkspace.TabSection, InspectWorkspace.TabView }, normalizeFirstTab: true);
            CheckSpacesTab();

            RunAction(SpacesDesign);
            CheckPalette("Progettazione", _design, new[] { DesignWorkspace.TabSketch, DesignWorkspace.TabConstraints,
                DesignWorkspace.TabFeature, DesignWorkspace.TabOptions, DesignWorkspace.TabParameters, DesignWorkspace.TabView });
            CheckApplyOnlyOnBar("Progettazione", _design);
            Check(!ActionEnabled("design.extrude"), "on an assembly document Progettazione offers no CAD command");
            Record("diag Progettazione on the assembly document: " + Catalog.Find("design.extrude").DisabledReason);

            RunAction(SpacesLamiera);
            CheckPalette("Lamiera", _lamiera, new[] { LamieraWorkspace.TabLamiera, LamieraWorkspace.TabSketch,
                LamieraWorkspace.TabFlat, LamieraWorkspace.TabView });
            CheckApplyOnlyOnBar("Lamiera", _lamiera);

            RunAction(SpacesAssembly);
            CheckPalette("Assieme", _assembly,
                new[] { AssemblyWorkspace.TabComponents, AssemblyWorkspace.TabConstraints, AssemblyWorkspace.TabView });
            CheckApplyOnlyOnBar("Assieme", _assembly);

            RunAction(SpacesInspect);
            Check(ReferenceEquals(Catalog.Active, _inspect) && !_design.Active && !_lamiera.Active && !_assembly.Active,
                "the Spazi tab brought the palette back to Ispeziona and closed the other workspaces");
            Pass("M6-01", "palette tabs and the Spazi tab switched across Ispeziona, Progettazione, Lamiera and Assieme by action id; "
                + "palette is a child of the left controller and no workspace has a floating panel (SYNTHETIC stick flicks and taps)");
            await Task.Yield();
        }

        private void CheckPalette(string name, IActionProvider workspace, string[] expectedTabs, bool normalizeFirstTab = false)
        {
            Check(ReferenceEquals(Catalog.Active, workspace), name + ": the catalog serves the workspace that was opened");
            var tabs = Catalog.Tabs.Select(t => t.Id).ToList();
            foreach (var tab in expectedTabs) Check(tabs.Contains(tab), name + ": palette tab '" + tab + "' is present");
            Check(tabs.Contains(ActionCatalog.SpacesTab), name + ": the Spazi tab is on every workspace");
            Check(!tabs.Contains(ActionCatalog.CommitTab), name + ": the commit tab is hidden from the palette");
            var counts = new List<string>();
            foreach (var id in tabs)
            {
                var items = Catalog.Palette(id);   // throws if a tab holds more than ActionCatalog.MaxPalette actions
                Check(items.Count <= ActionCatalog.MaxPalette, name + ": tab '" + id + "' holds at most " + ActionCatalog.MaxPalette + " actions");
                counts.Add(id + "=" + items.Count);
            }
            if (normalizeFirstTab)
                for (int i = 0; i < tabs.Count && _shell.Palette.CurrentTab != tabs[0]; i++) FlickPalette(1);
            Check(_shell.Palette.CurrentTab == tabs[0], name + ": a newly active workspace starts from its first tab (" + _shell.Palette.CurrentTab + ")");
            Check(tabs.Count >= 3, name + ": at least three tabs to rotate");
            FlickPalette(1);
            Check(_shell.Palette.CurrentTab == tabs[1], name + ": left stick right selects the next tab (" + _shell.Palette.CurrentTab + ")");
            FlickPalette(-1);
            Check(_shell.Palette.CurrentTab == tabs[0], name + ": left stick left returns to the first tab");
            FlickPalette(-1);
            Check(_shell.Palette.CurrentTab == tabs[tabs.Count - 1], name + ": the tab rotation wraps around (" + _shell.Palette.CurrentTab + ")");
            FlickPalette(1);
            Check(_shell.Palette.CurrentTab == tabs[0], name + ": and wraps back to the first tab");
            CheckNoFloatingPanel(name);
            Record("diag " + name + " palette tabs: " + string.Join(", ", counts));
        }

        private void CheckSpacesTab()
        {
            var spaces = Catalog.Palette(ActionCatalog.SpacesTab).ToDictionary(a => a.Id);
            foreach (var id in new[] { SpacesInspect, SpacesDesign, SpacesLamiera, SpacesAssembly })
            {
                Check(spaces.ContainsKey(id), "the Spazi tab offers " + id);
                Check(spaces[id].Enabled, "the Spazi action " + id + " is enabled in a session: " + spaces[id].DisabledReason);
            }
            Check(spaces.ContainsKey("spaces.connection"), "the Spazi tab also offers the connection action");
        }

        private static readonly HashSet<string> KnownCanvases = new HashSet<string>
        {
            "Tavolozza", "Barra di conferma", "HUD", "Chip", "Anello", "Sketch coordinates", "Sketch dimension", "Quota",
            "Etichetta sviluppo", "VoicePanel", "StatusBadge",
        };

        /// <summary>No panel floats beside the head: the old workspace panels are gone, only the shell, chips, ring and labels remain.</summary>
        private void CheckNoFloatingPanel(string where)
        {
            var unknown = new List<string>();
            foreach (var canvas in FindObjectsByType<Canvas>())
            {
                if (!canvas.enabled || canvas.renderMode != RenderMode.WorldSpace) continue;
                Check(!canvas.transform.IsChildOf(_head), where + ": no active canvas is attached to the head (found '" + canvas.name + "')");
                Check(canvas.name != "Home" && canvas.name != "StatusBadge", where + ": the home panel and the old status badge are not on screen in a session (found '" + canvas.name + "')");
                if (!KnownCanvases.Contains(canvas.name)) unknown.Add(canvas.name);
            }
            if (unknown.Count > 0) Record("diag " + where + " other active world canvases: " + string.Join(", ", unknown.Distinct()));
            Check(_design.VoicePanel == null && _assembly.VoicePanel == null && _lamiera.VoicePanel == null,
                where + ": the three authoring workspaces have no panel of their own to scan");
        }

        /// <summary>Apply exists once, on the commit bar: no palette tab offers it, voice cannot invoke it.</summary>
        private void CheckApplyOnlyOnBar(string name, IActionProvider workspace)
        {
            var applies = workspace.Actions.Where(a => a.Id.IndexOf("apply", StringComparison.OrdinalIgnoreCase) >= 0
                || a.Label.IndexOf("Applica", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Check(applies.Count == 1 && applies[0].Id == CommitIds.Apply && applies[0].Tab == ActionCatalog.CommitTab,
                name + ": the only Apply action is '" + CommitIds.Apply + "' on the commit bar (found " + applies.Count + ")");
            Check(!applies[0].VoiceInvokes, name + ": voice finds Applica but never executes it");
            foreach (var tab in workspace.Tabs.Where(t => !t.Hidden))
                Check(Catalog.Palette(tab.Id).All(a => a.Id != CommitIds.Apply), name + ": the palette tab '" + tab.Id + "' offers no Apply");
        }

        // --------------------------------------------------------------------------------- Assieme (M6-04/06/07)

        private static string OccurrencesSignature(AssemblyContext context) => string.Join("|", context.Occurrences.Select(o =>
            o.Id + "@" + (o.Center.HasValue ? F(o.Center.Value.X) + "," + F(o.Center.Value.Y) + "," + F(o.Center.Value.Z) : "-")
            + (o.Grounded ? "g" : "f")));

        private async Task<string> OccurrencesSignatureAsync(DocumentState state, string occurrenceId, CancellationToken ct)
            => OccurrencesSignature(await _assemblyBackend.GetAssemblyContextAsync(state, occurrenceId, ct));

        private async Task CheckAssemblyAsync(CancellationToken ct)
        {
            RunAction(SpacesAssembly);
            await WaitUntil(() => _assembly.Active && ActionEnabled(AssemblyWorkspace.IdComponents), ct);
            var context = Read<AssemblyContext>(_assembly, "_context");
            Check(context != null && context.Occurrences.Count >= 2, "the assembly context lists at least two components");
            var block = context.Occurrences.FirstOrDefault(o => o.Name.IndexOf("Block", StringComparison.OrdinalIgnoreCase) >= 0);
            var sheetMetal = context.Occurrences.FirstOrDefault(o => o.Name.IndexOf("Sheet", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(block != null && sheetMetal != null, "the fixture components 'Block' and 'Sheet' are listed ("
                + string.Join(", ", context.Occurrences.Select(o => o.Name)) + ")");
            var state0 = await BaselineAsync(ct);
            var sceneBefore = Session.Scene;

            // ---- M6-06: the assembly is raised on the bench (Workbench.ApplyAssembly) with the layout of the spec
            SnapPoses();
            Check(_bench.Raised && _bench.Frame != null, "the workbench holds the assembly in the raised pose");
            var pose = _bench.PartPose; var frame = _bench.Frame;
            var root = _view.transform;
            Check(Near(pose.Position.Y, frame.Origin.Y - WorkbenchLayout.AssemblyDrop, 1e-6),
                "the raised assembly floats " + F(WorkbenchLayout.AssemblyDrop * 100) + " cm below the head");
            double dx = pose.Position.X - frame.Origin.X, dz = pose.Position.Z - frame.Origin.Z;
            Check(Near(Math.Sqrt(dx * dx + dz * dz), WorkbenchLayout.AssemblyDistance, 1e-6),
                "the raised assembly is " + F(WorkbenchLayout.AssemblyDistance * 100) + " cm in front of the head");
            Check(pose.Position.Y > frame.DeskY + 0.2, "the raised assembly floats well above the work plane (" + F(pose.Position.Y - frame.DeskY) + " m)");
            Check(Vector3.Distance(root.position, new Vector3((float)pose.Position.X, (float)pose.Position.Y, (float)pose.Position.Z)) < 1e-3f
                && Near(root.localScale.x, pose.Scale, 1e-3 * Math.Max(1, pose.Scale)), "the scene root sits at the raised pose and scale");
            Pass("M6-06", "Assieme raised: " + F(WorkbenchLayout.AssemblyDistance * 100) + " cm in front, " + F(WorkbenchLayout.AssemblyDrop * 100)
                + " cm below the head, scale " + F(pose.Scale) + " (workbench pose applied; SYNTHETIC open from the Spazi action)");

            // ---- select the block through the Componenti list (palette path)
            await SelectOccurrenceAsync(block, ct);
            var signatureBefore = await OccurrencesSignatureAsync(state0, block.Id, ct);

            // ---- M6-04: ring on a component, opened by a synthetic pen ray; closed by X and by the empty space
            bool ringShown = await CheckComponentRingAsync(block, ct);

            // ---- M6-06: visual-only isolation (from the ring when it opened, else from the palette)
            await SelectOccurrenceAsync(block, ct);
            if (ringShown)
            {
                var ringAction = Catalog.Context(UiSelectionKind.Component).FirstOrDefault(a => a.Id == AssemblyWorkspace.IdIsolate);
                Check(ringAction != null && ringAction.Enabled && ringAction.TryInvoke(), "the ring action Isola ran");
            }
            else RunAction(AssemblyWorkspace.IdIsolate);
            Check(_assembly.Isolation.Active && _assembly.Isolation.OccurrenceId == block.Id, "the block is isolated");
            _assembly.Isolation.Snap();
            var instance = _view.Find(block.Id);
            Check(instance != null, "the isolated component is in the scene");
            Check(Vector3.Distance(instance.transform.localPosition, _assembly.Isolation.HomeLocalPosition) > 1e-4f,
                "the isolated component advanced towards the user (a visual move of its transform)");
            Check(_assembly.Isolation.FadedBodies > 0, "the rest of the assembly is faded (" + _assembly.Isolation.FadedBodies + " bodies)");
            var state1 = await BaselineAsync(ct);
            Check(state1.DocumentId == state0.DocumentId && state1.Revision == state0.Revision, "isolation leaves Inventor's revision unchanged");
            Check(await OccurrencesSignatureAsync(state1, block.Id, ct) == signatureBefore, "isolation leaves every Inventor occurrence where it was (centres and grounding read back)");
            Check(ReferenceEquals(Session.Scene, sceneBefore), "the session scene was not reloaded by the isolation");
            Check(_assembly.CommitBar.Phase == CommitBarPhase.Empty && !_assembly.Session.CanApply && _assembly.Session.Preview == null, "isolation opens no draft on the commit bar");
            Pass("M6-06", "isolation of " + block.Name + " is visual only: the instance transform moved, " + _assembly.Isolation.FadedBodies
                + " bodies faded, revision " + state1.Revision + " and the Inventor occurrences unchanged");

            PressX();   // X releases the isolation (no keypad, ring or list is open)
            Check(!_assembly.Isolation.Active, "X releases the isolation");
            _assembly.Isolation.Snap();
            Check(Vector3.Distance(instance.transform.localPosition, _assembly.Isolation.HomeLocalPosition) < 1e-4f,
                "the component is back at its place after the release");
            RunAction(AssemblyWorkspace.IdIsolate);
            Check(_assembly.Isolation.Active, "Isola isolates again");
            RunAction(AssemblyWorkspace.IdRelease);
            Check(!_assembly.Isolation.Active, "the Rilascia action also releases it");
            _assembly.Isolation.Snap();
            await AssertUnchanged(state0, "isolate and release", ct);

            // ---- M6-07: one hand, two hands, Fit, Recenter: view only
            await CheckViewManipulationAsync(block, state0, signatureBefore, ct);

            // ---- M6-06: open the isolated part in Progettazione (its document is activated, the workspace changes)
            await SelectOccurrenceAsync(block, ct);
            RunAction(AssemblyWorkspace.IdIsolate);
            Check(ActionEnabled(AssemblyWorkspace.IdOpenDesign) && ActionEnabled(AssemblyWorkspace.IdOpenLamiera),
                "from the isolated component both 'Apri in Progettazione' and 'Apri in Lamiera' are offered");
            RunAction(AssemblyWorkspace.IdOpenDesign);
            Check(await TryWaitUntil(() => _design.Active && !_assembly.Active, 20, ct), "Progettazione opened from the isolated component");
            Check(await TryWaitUntil(() => Session.Scene?.Graph?.Kind == "part" && Session.Scene.Graph.DocumentId != _assemblyDocId, 30, ct),
                "Inventor activated the part document of the isolated component");
            await WaitUntil(() => !ReadBoolean(_design, "_busy") && Read<DesignContext>(_design, "_context") != null
                && Read<DesignHistory>(_design, "_history") != null && ActionEnabled("design.extrude"), ct);
            RequireFixture();
            Pass("M6-06", "'Apri in Progettazione' from the isolated component activated " + Session.Scene.Graph.Root.Name
                + " and opened Progettazione on it (SYNTHETIC action tap)");
        }

        private async Task SelectOccurrenceAsync(AssemblyOccurrence occurrence, CancellationToken ct)
        {
            if (Read<AssemblyOccurrence>(_assembly, "_occurrence")?.Id == occurrence.Id && ActionEnabled(AssemblyWorkspace.IdIsolate)) return;
            await WaitUntil(() => ActionEnabled(AssemblyWorkspace.IdComponents), ct);
            RunAction(AssemblyWorkspace.IdComponents);
            PickItem(_assembly, AssemblyWorkspace.IdPickPrefix, occurrence.Name, suffix: true);
            await WaitUntil(() => Read<AssemblyOccurrence>(_assembly, "_occurrence")?.Id == occurrence.Id
                && !ReadBoolean(_assembly, "_busy") && ActionEnabled(AssemblyWorkspace.IdIsolate), ct);
        }

        private Vector3 OccurrenceCenter(string occurrenceId)
        {
            var instance = _view.Find(occurrenceId);
            Check(instance != null, "the occurrence " + occurrenceId + " is in the scene");
            var bounds = ScenePlacement.LocalBounds(instance.transform);
            return instance.transform.TransformPoint(bounds.center);
        }

        /// <summary>Component ring: the actions of the spec, opened by a synthetic ray, closed with X and with the empty space.</summary>
        private async Task<bool> CheckComponentRingAsync(AssemblyOccurrence block, CancellationToken ct)
        {
            var ids = Catalog.Context(UiSelectionKind.Component).Select(a => a.Id).ToArray();
            Check(ids.Length >= 3 && ids.Length <= ActionCatalog.MaxContext
                && ids.Contains(AssemblyWorkspace.IdIsolate) && ids.Contains(AssemblyWorkspace.IdMove) && ids.Contains(AssemblyWorkspace.IdConstrain)
                && ids.Contains(AssemblyWorkspace.IdOpen), "the ring of a component offers Isola, Sposta, Vincola and Apri (" + string.Join(", ", ids) + ")");
            var ring = Read<RingView>(_assembly, "_ring");
            Check(ring != null, "Assieme owns a ring");
            SnapPoses();
            var center = OccurrenceCenter(block.Id);
            AimAt(center, AwayFromHead(center));
            TriggerTap();
            bool shown = await TryWaitUntil(() => ring.Visible, 5, ct);
            if (!shown)
            {
                NotCovered("M6-04", "the synthetic pen ray did not open the component ring (no body hit or selection pending): ring actions were checked on the catalog only");
                return false;
            }
            PressX();
            Check(!ring.Visible, "X closes the component ring");
            TriggerTap();
            Check(await TryWaitUntil(() => ring.Visible, 5, ct), "the ring opens again on the component");
            _ray.Origin.SetPositionAndRotation(_head.position, Quaternion.LookRotation(Vector3.up));
            TriggerTap();
            Check(!ring.Visible, "a pen click on the empty space closes the component ring");
            // the ring is wanted open for the Isola action that follows
            AimAt(center, AwayFromHead(center));
            TriggerTap();
            shown = await TryWaitUntil(() => ring.Visible, 5, ct);
            Pass("M6-04", "component ring (" + string.Join(", ", ids) + ") opened by a SYNTHETIC pen ray, closed by X and by a click on the empty space");
            return shown;
        }

        private async Task CheckViewManipulationAsync(AssemblyOccurrence block, DocumentState state0, string signatureBefore, CancellationToken ct)
        {
            var root = _view.transform;
            var hand = _ray.Origin;
            var left = LeftAnchor();
            SnapPoses();
            var savedHandPosition = hand.position; var savedHandRotation = hand.rotation;
            var savedLeft = left != null ? left.position : Vector3.zero;
            await SelectOccurrenceAsync(block, ct);
            bool oneHand = false, twoHand = false;
            try
            {
                // one hand: a Grip on the model carries it with the hand (view only)
                var position0 = root.position; var scale0 = root.localScale.x;
                var center = OccurrenceCenter(block.Id);
                AimAt(center, AwayFromHead(center));
                SendRest();
                var grip = Rest(); grip.PenGrip = true; Send(grip);
                if (Read<Transform>(_assembly, "_grab") != null)
                {
                    var delta = new Vector3(0.05f, 0.02f, 0f);
                    hand.position += delta;
                    Call(_assembly, "MoveGrab");
                    Check(Vector3.Distance(root.position, position0 + delta) < 1e-3f, "a synthetic Grip moved the scene root by the hand displacement");
                    Check(Near(root.localScale.x, scale0, 1e-6), "a one hand grab does not scale the model");
                    SendRest();
                    Check(Read<Transform>(_assembly, "_grab") == null, "releasing the Grip ends the grab");
                    oneHand = true;
                }
                else
                {
                    SendRest();
                    NotCovered("M6-07", "the synthetic ray did not hit the model, so the one hand grab did not start");
                }

                // two hands: Grips of both controllers scale the model with the distance between the hands (view only)
                if (left != null)
                {
                    SnapPoses();
                    var scaleStart = root.localScale.x;
                    float ratio = scaleStart > 5f ? 0.8f : 1.25f;
                    Vector3 rightStart = hand.position, leftStart = rightStart + Vector3.left * 0.3f;
                    left.position = leftStart;
                    var both = Rest(); both.PenGrip = true; both.PaletteGrip = true; Send(both);
                    if (ReadBoolean(_assembly, "_twoHandActive"))
                    {
                        var mid = (leftStart + rightStart) * 0.5f; var half = (rightStart - leftStart) * 0.5f * ratio;
                        hand.position = mid + half; left.position = mid - half;
                        Call(_assembly, "TwoHandStep");
                        Check(Near(root.localScale.x, scaleStart * ratio, 1e-3 * scaleStart), "two hands moved apart by x" + F(ratio) + " scaled the model by the same ratio ("
                            + F(scaleStart) + " -> " + F(root.localScale.x) + ")");
                        SendRest();
                        Check(!ReadBoolean(_assembly, "_twoHandActive"), "releasing the Grips ends the two hand manipulation");
                        twoHand = true;
                    }
                    else
                    {
                        SendRest();
                        NotCovered("M6-07", "the two hand manipulation did not start (left controller pose unavailable to the workspace)");
                    }
                }
                else NotCovered("M6-07", "no left controller anchor was found for the two hand manipulation");
            }
            finally
            {
                SendRest();
                hand.SetPositionAndRotation(savedHandPosition, savedHandRotation);
                if (left != null) left.position = savedLeft;
            }

            var state = await BaselineAsync(ct);
            Check(state.DocumentId == state0.DocumentId && state.Revision == state0.Revision, "view manipulation leaves Inventor's revision unchanged");
            Check(await OccurrencesSignatureAsync(state, block.Id, ct) == signatureBefore, "view manipulation leaves every Inventor occurrence where it was");
            Check(_assembly.CommitBar.Phase == CommitBarPhase.Empty && !_assembly.Session.CanApply && _assembly.Session.Preview == null, "view manipulation opens no draft");

            // Fit (short Y): the model returns to the raised pose
            int fits = _fitEvents;
            var yDown = Rest(); yDown.Y = true; Send(yDown); SendRest();
            Check(_fitEvents == fits + 1, "a short press of Y raised the Fit event");
            _bench.Snap();
            var pose = _bench.PartPose;
            Check(Vector3.Distance(root.position, new Vector3((float)pose.Position.X, (float)pose.Position.Y, (float)pose.Position.Z)) < 1e-3f
                && Near(root.localScale.x, pose.Scale, 1e-3 * Math.Max(1, pose.Scale)), "Adatta (Y) put the model back at the raised pose and scale");

            // Recenter (Y held 1 s): a new bench frame from the head, no Fit on release
            var frameBefore = _bench.Frame;
            int recenters = _recenterEvents; fits = _fitEvents;
            Send(yDown);
            bool held = await TryWaitUntil(() => _recenterEvents > recenters, 4, ct);
            SendRest();
            Check(held, "holding Y for one second raised the Recenter event");
            Check(_fitEvents == fits, "releasing Y after a Recenter does not also Fit");
            Check(!ReferenceEquals(_bench.Frame, frameBefore), "Ricentra computed a new bench frame from the head");
            _bench.Snap();
            pose = _bench.PartPose;
            Check(Vector3.Distance(root.position, new Vector3((float)pose.Position.X, (float)pose.Position.Y, (float)pose.Position.Z)) < 1e-3f,
                "the model sits at the pose of the new frame after Ricentra");
            await AssertUnchanged(state0, "Fit and Recenter", ct);
            Pass("M6-07", "view only: " + (oneHand ? "one hand grab moved the root; " : "one hand grab NOT exercised; ")
                + (twoHand ? "two hand grips scaled it; " : "two hand grips NOT exercised; ")
                + "Y short = Fit and Y held 1 s = Recenter (new bench frame); revision " + state0.Revision + " and the Inventor occurrences unchanged (SYNTHETIC)");
        }

        // ------------------------------------------------------------------------------ Progettazione (M6-01..05)

        private DesignSession DesignSess => Read<DesignSession>(_design, "_session");
        private DesignPreviewView DesignPreview => Read<DesignPreviewView>(_design, "_previewView");
        private DesignContext DesignCtx => Read<DesignContext>(_design, "_context");

        private static bool IsLine(DesignEdge edge) => edge.Kind == "kLineSegmentCurve" || edge.Kind == "kLineCurve";

        private async Task CheckDesignAsync(CancellationToken ct)
        {
            Check(ReferenceEquals(Catalog.Active, _design), "the palette serves Progettazione");
            CheckPalette("Progettazione (part)", _design, new[] { DesignWorkspace.TabSketch, DesignWorkspace.TabFeature, DesignWorkspace.TabView });
            var context = DesignCtx;
            Check(context.Sketches.Any(s => s.Name == SketchName), "the block part lists the unconsumed sketch " + SketchName);
            Check(context.Planes.Count > 0 && context.Faces.Count > 0 && context.Edges.Count > 0, "Design context exposes planes, faces and edges");
            var state0 = await BaselineAsync(ct);
            Check(state0.DocumentId != _assemblyDocId, "Progettazione works on the part document, not on the assembly");

            await CheckSketchSheetAsync(state0, ct);                 // M6-02
            await CheckDesignRingsAsync(state0, ct);                 // M6-04
            await CheckDesignChipAndBarAsync(state0, ct);            // M6-03, M6-05 (Draft, Previewing, Ready)
            await CheckDesignErrorOfflineUncertainAsync(state0, ct); // M6-05 (Error, Offline, Uncertain)
            state0 = await CheckDesignAppliedAsync(state0, ct);      // M6-05 (Applied)
            Check(ReferenceEquals(Catalog.Active, _design), "Progettazione is still the active workspace");
        }

        private string SketchSignature(SketchDraft sketch)
        {
            var frame = sketch.Frame;
            var parts = new List<string>
            {
                "o" + F(frame.OriginMm.X) + "," + F(frame.OriginMm.Y) + "," + F(frame.OriginMm.Z),
                "x" + F(frame.XAxis.X) + "," + F(frame.XAxis.Y) + "," + F(frame.XAxis.Z),
                "y" + F(frame.YAxis.X) + "," + F(frame.YAxis.Y) + "," + F(frame.YAxis.Z),
            };
            foreach (var element in sketch.Elements)
                parts.Add(element.Shape + ":" + F(element.A.X) + "," + F(element.A.Y) + "," + F(element.B.X) + "," + F(element.B.Y) + "," + F(element.Radius));
            return string.Join("|", parts);
        }

        /// <summary>M6-02: sketch on the horizontal sheet, drawn with the pen tip and with the ray, Vista modello and Foglio leave the CAD coordinates alone.</summary>
        private async Task CheckSketchSheetAsync(DocumentState baseline, CancellationToken ct)
        {
            var context = DesignCtx;
            var plane = context.Planes[0];
            Check(Read<string>(_design, "_face") == null, "no face is selected before the sketch");
            RunAction("design.sketch.create");
            PickItem(_design, "design.pick.", plane.Name);
            var sketch = Read<SketchDraft>(_design, "_sketch");
            Check(sketch != null && sketch.Frame != null, "the sketch on plane '" + plane.Name + "' has its frame");
            Check(_sheet.State == SketchSheetState.Sheet, "the sketch opened on the sheet");
            SnapPoses();
            var root = _view.transform; var bench = _bench.Frame; var frame = sketch.Frame;
            var normalWorld = root.TransformDirection(CadCoordinates.ToLocal(frame.Normal)).normalized;
            Check(Mathf.Abs(Vector3.Dot(normalWorld, Vector3.up)) > 0.999f, "the sketch plane lies horizontal on the table (normal dot up " + F(Vector3.Dot(normalWorld, Vector3.up)) + ", sheet state " + _sheet.State + ", tweening " + _sheet.Tweening + ")");
            Vector3 World(CadPoint sketchMm) => root.TransformPoint(CadCoordinates.ToLocal(frame.ToModel(sketchMm)));
            Check(Near(World(new CadPoint(0, 0)).y, bench.DeskY, 2e-3), "the sheet is at the height of the work plane (" + F(World(new CadPoint(0, 0)).y) + " vs " + F(bench.DeskY) + " m)");
            Check(Near(frame.Normal.X, plane.Frame.Normal.X, 1e-9) && Near(frame.Normal.Y, plane.Frame.Normal.Y, 1e-9) && Near(frame.Normal.Z, plane.Frame.Normal.Z, 1e-9),
                "the sketch frame is the one Inventor reported for the plane");
            var originalFrame = SketchSignature(sketch);

            // pen tip: within 2 cm of the sheet the tip itself draws
            var p1 = new CadPoint(20, 15); var p2 = new CadPoint(60, 40);
            var tip1 = World(p1);
            _ray.Origin.SetPositionAndRotation(tip1, Quaternion.LookRotation(Vector3.down));
            TriggerTap();
            _ray.Origin.SetPositionAndRotation(World(p2), Quaternion.LookRotation(Vector3.down));
            TriggerTap();
            Check(sketch.Elements.Count == 1, "two pen tip clicks drew one line (elements: " + sketch.Elements.Count + ")");
            var line = sketch.Elements[0];
            Check(Near(line.A.X, p1.X, 0.05) && Near(line.A.Y, p1.Y, 0.05) && Near(line.B.X, p2.X, 0.05) && Near(line.B.Y, p2.Y, 0.05),
                "the pen tip line runs from (" + F(line.A.X) + ", " + F(line.A.Y) + ") to (" + F(line.B.X) + ", " + F(line.B.Y) + ") mm");

            // ray: from 30 cm above the sheet the ray draws
            var p3 = new CadPoint(75, 10); var p4 = new CadPoint(30, 60);
            _ray.Origin.SetPositionAndRotation(World(p3) + Vector3.up * 0.3f, Quaternion.LookRotation(Vector3.down));
            TriggerTap();
            _ray.Origin.SetPositionAndRotation(World(p4) + Vector3.up * 0.3f, Quaternion.LookRotation(Vector3.down));
            TriggerTap();
            Check(sketch.Elements.Count == 2, "two ray clicks drew a second line (elements: " + sketch.Elements.Count + ")");
            var second = sketch.Elements[1];
            Check(Near(second.A.X, p3.X, 0.5) && Near(second.A.Y, p3.Y, 0.5) && Near(second.B.X, p4.X, 0.5) && Near(second.B.Y, p4.Y, 0.5),
                "the ray line runs from (" + F(second.A.X) + ", " + F(second.A.Y) + ") to (" + F(second.B.X) + ", " + F(second.B.Y) + ") mm");
            await AssertUnchanged(baseline, "drawing on the sheet", ct);
            Pass("M6-02", "sketch on the horizontal sheet at table height, drawn with the pen tip (SYNTHETIC tip on the plane) and with the ray (SYNTHETIC ray from 30 cm); revision " + baseline.Revision + " unchanged");

            // Vista modello <-> Foglio: only the pose of the scene root changes
            var signature = SketchSignature(sketch);
            Check(string.Join("|", signature.Split('|').Take(3)) == string.Join("|", originalFrame.Split('|').Take(3)), "the sketch frame did not move while drawing");
            RunAction("design.view.model");
            SnapPoses();
            Check(_sheet.State == SketchSheetState.Model, "Vista modello leaves the sheet");
            var pose = _bench.PartPose;
            Check(Vector3.Distance(root.position, new Vector3((float)pose.Position.X, (float)pose.Position.Y, (float)pose.Position.Z)) < 1e-3f
                && Near(root.localScale.x, pose.Scale, 1e-3 * Math.Max(1, pose.Scale)), "Vista modello puts the scene root on the work plane pose");
            Check(Catalog.Find("design.view.model").IsOn && !Catalog.Find("design.view.sheet").IsOn, "the view toggles reflect Vista modello");
            Check(SketchSignature(sketch) == signature, "the sketch elements and frame (CAD coordinates) are identical in Vista modello");
            await AssertUnchanged(baseline, "Vista modello", ct);
            RunAction("design.view.sheet");
            SnapPoses();
            Check(_sheet.State == SketchSheetState.Sheet, "Foglio shows the sheet again");
            Check(Near(World(new CadPoint(0, 0)).y, bench.DeskY, 2e-3), "the sheet is back at table height");
            Check(SketchSignature(sketch) == signature, "the sketch elements and frame (CAD coordinates) are identical after the round trip");
            Check(_sheet.ProjectPen(tip1, new Ray(tip1, Vector3.down), frame, out var back) && Near(back.x, p1.X, 0.05) && Near(back.y, p1.Y, 0.05),
                "the same physical point still projects to the same sketch coordinates (" + F(back.x) + ", " + F(back.y) + ") mm");
            await AssertUnchanged(baseline, "Foglio", ct);
            Pass("M6-02", "Vista modello <-> Foglio moved only the scene root: " + sketch.Elements.Count + " elements and the sketch frame unchanged, "
                + "the same tip point projects to the same CAD coordinates, revision " + baseline.Revision + " unchanged");

            RunAction(CommitIds.Cancel);
            Check(DesignSess.Status == DesignStatus.Empty && !DesignPreview.IsShowing, "Annulla comando discards the sketch draft");
            SnapPoses();
            await AssertUnchanged(baseline, "cancelled sketch", ct);
        }

        /// <summary>M6-04: contextual ring on an edge and on a planar face, closed by X and by the empty space.</summary>
        private async Task CheckDesignRingsAsync(DocumentState baseline, CancellationToken ct)
        {
            var context = DesignCtx;
            var planarIds = Catalog.Context(UiSelectionKind.PlanarFace).Select(a => a.Id).ToArray();
            var edgeIds = Catalog.Context(UiSelectionKind.Edge).Select(a => a.Id).ToArray();
            Check(planarIds.Length <= ActionCatalog.MaxContext && planarIds.Contains("design.sketch.create") && planarIds.Contains("design.extrude")
                && planarIds.Contains("design.hole"), "the ring of a planar face offers Crea schizzo, Estrusione, Foro (" + string.Join(", ", planarIds) + ")");
            Check(edgeIds.Length <= ActionCatalog.MaxContext && edgeIds.Contains("design.fillet") && edgeIds.Contains("design.chamfer"),
                "the ring of an edge offers Raccordo and Smusso (" + string.Join(", ", edgeIds) + ")");
            Check(Catalog.Context(UiSelectionKind.None).Count == 0, "no selection, no ring");
            var ring = Read<RingView>(_design, "_ring");
            Check(ring != null, "Progettazione owns a ring");
            SnapPoses();
            var model = _view.transform;
            var face = context.Faces.FirstOrDefault(f => Near(f.Normal.Z, 1, 1e-3) && Near(f.PointMm.Z, 10, 0.01)) ?? context.Faces[0];
            var away = model.TransformDirection(CadCoordinates.ToLocal(face.Normal)).normalized;
            var edge = context.Edges.FirstOrDefault(e => IsLine(e) && e.PointsMm.All(p => Near(p.Z, face.PointMm.Z, 0.01)));
            string covered = "";

            if (edge != null)
            {
                var mid = model.TransformPoint(CadCoordinates.ToLocal(FlangeManipulator.Midpoint(edge.PointsMm)));
                AimAt(mid, away);
                TriggerTap();
                if (ring.Visible && Read<string>(_design, "_ringEdge") == edge.Id)
                {
                    PressX();
                    Check(!ring.Visible, "X closes the edge ring");
                    AimAt(mid, away); TriggerTap();
                    Check(ring.Visible, "the edge ring opens again");
                    _ray.Origin.SetPositionAndRotation(_head.position, Quaternion.LookRotation(Vector3.up));
                    TriggerTap();
                    Check(!ring.Visible, "a pen click on the empty space closes the edge ring");
                    AimAt(mid, away); TriggerTap();
                    Check(ring.Visible, "the edge ring opens once more for its action");
                    var fillet = Catalog.Context(UiSelectionKind.Edge).First(a => a.Id == "design.fillet");
                    Check(fillet.Enabled && fillet.TryInvoke(), "the ring action Raccordo ran");
                    Check(Read<HashSet<string>>(_design, "_edges").Contains(edge.Id), "Raccordo from the ring took the selected edge");
                    Check(DesignSess.Status == DesignStatus.Draft && _design.CommitBar.Phase == CommitBarPhase.Draft, "the ring action opened a draft on the commit bar");
                    Check(!ring.Visible, "the ring closes after its action");
                    await AssertUnchanged(baseline, "ring Raccordo draft", ct);
                    RunAction(CommitIds.Cancel);
                    Check(DesignSess.Status == DesignStatus.Empty, "the ring draft is cancelled");
                    covered += "edge (" + string.Join(", ", edgeIds) + ")";
                }
                else NotCovered("M6-04", "the synthetic pen ray did not open the edge ring on edge " + edge.Id + " (selection diag: ring visible " + ring.Visible + ")");
            }
            else NotCovered("M6-04", "the fixture block exposes no straight edge on its top face to aim the ray at");

            var faceWorld = model.TransformPoint(CadCoordinates.ToLocal(face.PointMm));
            AimAt(faceWorld, away);
            TriggerTap();
            if (ring.Visible && Read<string>(_design, "_face") != null)
            {
                PressX();
                Check(!ring.Visible, "X closes the face ring");
                AimAt(faceWorld, away); TriggerTap();
                Check(ring.Visible, "the face ring opens again");
                _ray.Origin.SetPositionAndRotation(_head.position, Quaternion.LookRotation(Vector3.up));
                TriggerTap();
                Check(!ring.Visible, "a pen click on the empty space closes the face ring");
                covered += (covered.Length > 0 ? " and " : "") + "planar face (" + string.Join(", ", planarIds) + ")";
            }
            else NotCovered("M6-04", "the synthetic pen ray did not open the planar face ring (ring visible " + ring.Visible + ")");

            await AssertUnchanged(baseline, "ring selection", ct);
            if (covered.Length > 0)
                Pass("M6-04", "contextual ring on " + covered + " opened by a SYNTHETIC pen ray, closed by X and by the empty space; no CAD change (revision " + baseline.Revision + ")");
        }

        private async Task PreviewExtrudeAsync(DocumentState baseline, double lengthMm, CancellationToken ct)
        {
            RunAction("design.extrude");
            PickItem(_design, "design.pick.", SketchName);
            RunAction("design.dimension");
            Check(_shell.Palette.KeypadVisible, "the dimension keypad opens on the palette");
            TypeValue(lengthMm);
            Check(DesignSess.Status == DesignStatus.Draft, "the typed length leaves a draft: Anteprima is needed (status " + DesignSess.Status + ")");
            RunAction(CommitIds.Preview);
            await WaitUntil(() => DesignSess.Status != DesignStatus.Previewing, ct);
            Check(DesignSess.Status == DesignStatus.PreviewReady && DesignSess.CanApply && DesignPreview.IsShowing,
                "the extrusion preview is ready and rendered (status " + DesignSess.Status + ", error '" + DesignSess.Error + "')");
            await AssertUnchanged(baseline, "extrusion preview", ct);
        }

        /// <summary>M6-03 chip and keypad, thumbstick steps 10/1/0,1, precision flag; M6-05 Empty, Draft, Previewing, Ready.</summary>
        private async Task CheckDesignChipAndBarAsync(DocumentState baseline, CancellationToken ct)
        {
            var bar = _design.CommitBar;
            Check(bar.Phase == CommitBarPhase.Empty, "with no command the commit bar is empty (" + bar.Phase + ")");
            Check(!ActionEnabled(CommitIds.Apply) && !ActionEnabled(CommitIds.Preview) && !ActionEnabled(CommitIds.Cancel),
                "with no command Anteprima, Applica and Annulla are disabled");

            RunAction("design.extrude");
            PickItem(_design, "design.pick.", SketchName);
            var chip = Read<ChipView>(_design, "_chip");
            Check(chip != null && chip.Canvas.gameObject.activeSelf && chip.Entry != null, "the dimension chip is on screen once the extrusion is chosen");
            RunAction("design.dimension");
            Check(_shell.Palette.KeypadVisible && _design.ActiveEntry != null && _design.ActiveEntry.Editing, "tapping the chip action opens the keypad on the palette");
            PressX();
            Check(!_shell.Palette.KeypadVisible, "X closes the keypad");
            RunAction("design.dimension");
            TypeValue(20);
            Check(Near(chip.Entry.Value, 20, 1e-9) && chip.ValueText.Contains("20"), "the chip shows the typed value (" + chip.ValueText + ")");
            Check(bar.Phase == CommitBarPhase.Draft && bar.CanPreview && !bar.CanApply, "a changed value leaves the commit bar in Draft (" + bar.Phase + ")");
            Check(!ActionEnabled(CommitIds.Apply) && ActionEnabled(CommitIds.Preview), "Draft: Applica is disabled, Anteprima is enabled");

            // thumbstick: x = one step, y = step size 0,1 / 1 / 10
            Check(Near(chip.Entry.Step, 1, 1e-9), "the default step is 1 mm (" + chip.StepText + ")");
            FlickPen(1, 0);
            Check(Near(chip.Entry.Value, 21, 1e-9), "stick right adds one step of 1 mm (" + F(chip.Entry.Value) + ")");
            FlickPen(0, 1);
            Check(Near(chip.Entry.Step, 10, 1e-9) && chip.StepText.Contains("10"), "stick up raises the step to 10 mm (" + chip.StepText + ")");
            FlickPen(1, 0);
            Check(Near(chip.Entry.Value, 31, 1e-9), "stick right adds one step of 10 mm (" + F(chip.Entry.Value) + ")");
            FlickPen(0, -1); FlickPen(0, -1);
            Check(Near(chip.Entry.Step, 0.1, 1e-9) && chip.StepText.Contains("0,1"), "stick down twice lowers the step to 0,1 mm (" + chip.StepText + ")");
            FlickPen(-1, 0);
            Check(Near(chip.Entry.Value, 30.9, 1e-9), "stick left subtracts one step of 0,1 mm (" + F(chip.Entry.Value) + ")");
            FlickPen(0, -1);
            Check(Near(chip.Entry.Step, 0.1, 1e-9), "the step does not go below 0,1 mm");
            Check(chip.Entry.Value >= 0 && Near(chip.Entry.Value, 30.9, 1e-9) && chip.Entry.Min > 0, "the value stays inside its limits (min " + F(chip.Entry.Min) + " mm)");
            Check(DesignSess.Status == DesignStatus.Draft && !DesignSess.CanApply, "stepping the chip changes only the local draft");
            Check(Near(chip.Entry.Value, 30.9, 1e-9), "chip value kept for the preview");

            // precision: left Trigger held = precision mode (pen drags are 10x slower)
            var precision = Rest(); precision.PaletteTrigger = true; Send(precision);
            Check(_input.Precision, "the left Trigger held puts the input in precision mode");
            SendRest();
            Check(!_input.Precision, "releasing the left Trigger ends precision mode");
            await AssertUnchanged(baseline, "chip and stick steps", ct);
            Pass("M6-03", "chip and keypad on the palette (X closes the keypad), stick steps 1 -> 10 -> 0,1 mm with the right limits, precision flag from the left Trigger (SYNTHETIC frames)");

            // M6-05: Previewing -> Ready; editing a Ready preview returns to Draft; Apply only on the bar
            RunAction(CommitIds.Preview);
            Check(bar.Phase == CommitBarPhase.Previewing, "while the preview runs the commit bar shows Previewing (" + bar.Phase + ")");
            Check(!ActionEnabled(CommitIds.Apply), "Previewing: Applica is disabled");
            await WaitUntil(() => DesignSess.Status != DesignStatus.Previewing, ct);
            Check(DesignSess.Status == DesignStatus.PreviewReady && DesignSess.CanApply && DesignPreview.IsShowing,
                "the preview of " + F(chip.Entry.Value) + " mm is ready (status " + DesignSess.Status + ", error '" + DesignSess.Error + "')");
            Check(bar.Phase == CommitBarPhase.Ready && bar.CanApply, "Ready: the commit bar offers Applica (" + bar.Phase + ")");
            Check(ActionEnabled(CommitIds.Apply), "Ready: the Applica action is enabled");
            await AssertUnchanged(baseline, "ready preview", ct);

            // the voice-surface command Apply only shows a notice: it never commits
            string planBefore = DesignSess.Preview.PlanId;
            Check(_design.IsEnabled(CommandIds.Apply) && _design.Invoke(CommandIds.Apply), "the voice surface answers Apply with a notice");
            Check(DesignSess.Status == DesignStatus.PreviewReady && DesignSess.Preview.PlanId == planBefore && ReadValue<int>(_design, "_pendingMutations") == 0,
                "Apply from the voice surface started no commit and left the preview in place");
            await AssertUnchanged(baseline, "Apply on the voice surface", ct);

            FlickPen(1, 0);
            Check(bar.Phase == CommitBarPhase.Draft && !ActionEnabled(CommitIds.Apply) && !DesignSess.CanApply,
                "editing a ready preview returns to Draft and disables Applica (" + bar.Phase + ")");
            FlickPen(0, 1);   // back to the default step of 1 mm
            RunAction(CommitIds.Cancel);
            Check(bar.Phase == CommitBarPhase.Empty && DesignSess.Status == DesignStatus.Empty && !DesignPreview.IsShowing, "Annulla comando returns the bar to Empty");
            await AssertUnchanged(baseline, "cancelled extrusion", ct);
            Pass("M6-05", "commit bar Empty -> Draft -> Previewing -> Ready -> Draft -> Empty on the extrusion; Applica enabled only in Ready and offered only by the bar (SYNTHETIC taps); revision " + baseline.Revision + " unchanged");
        }

        /// <summary>M6-05 Error (fillet radius that fails in Inventor), Offline (workspace flag) and Uncertain (commit outcome unknown).</summary>
        private async Task CheckDesignErrorOfflineUncertainAsync(DocumentState baseline, CancellationToken ct)
        {
            var bar = _design.CommitBar;
            // ---- Error: a 100 mm radius on the whole straight edge group of the block fails in Inventor
            var blockEdges = DesignCtx.Edges.Where(IsLine).Select(e => e.Id).Distinct().ToArray();
            if (blockEdges.Length >= 8)
            {
                RunAction("design.fillet");
                Read<HashSet<string>>(_design, "_edges").UnionWith(blockEdges);
                RunAction("design.dimension");
                TypeValue(100);
                RunAction(CommitIds.Preview);
                await WaitUntil(() => DesignSess.Status != DesignStatus.Previewing, ct);
                Check(DesignSess.Status == DesignStatus.Error && !string.IsNullOrEmpty(DesignSess.Error), "a 100 mm fillet on the edge group fails validation (status " + DesignSess.Status + ")");
                Check(bar.Phase == CommitBarPhase.Error && !string.IsNullOrEmpty(bar.Message), "the commit bar shows the short error (" + bar.Phase + ": " + bar.Message + ")");
                Check(!ActionEnabled(CommitIds.Apply) && bar.CanCancel, "Error: Applica is disabled and the command can be cancelled");
                await AssertUnchanged(baseline, "failed fillet preview", ct);
                RunAction(CommitIds.Cancel);
                Check(bar.Phase == CommitBarPhase.Empty, "Annulla comando clears the error");
                Pass("M6-05", "Error: a 100 mm fillet preview failed in Inventor, the bar showed the short message, Applica stayed disabled; revision " + baseline.Revision + " unchanged");
            }
            else NotCovered("M6-05-Error", "the block exposes fewer than 8 straight edges, so the failing fillet case could not be built");

            // ---- Offline: the workspace loses the connection flag
            RunAction("design.extrude");
            PickItem(_design, "design.pick.", SketchName);
            Check(bar.Phase == CommitBarPhase.Draft, "an extrusion draft is open before going offline");
            _design.SetOnline(false);
            Check(bar.Phase == CommitBarPhase.Offline, "Offline: the commit bar shows Offline (" + bar.Phase + ")");
            Check(!ActionEnabled(CommitIds.Apply) && !ActionEnabled(CommitIds.Preview) && !ActionEnabled("design.extrude") && !ActionEnabled("design.fillet"),
                "Offline: Applica, Anteprima and the CAD commands are disabled");
            Check(Catalog.Find("design.extrude").DisabledReason.IndexOf("Offline", StringComparison.OrdinalIgnoreCase) >= 0,
                "Offline: the disabled reason says why (" + Catalog.Find("design.extrude").DisabledReason + ")");
            _design.SetOnline(true);
            await WaitUntil(() => !ReadBoolean(_design, "_busy") && DesignCtx != null && Read<DesignHistory>(_design, "_history") != null && ActionEnabled("design.extrude"), ct);
            Check(bar.Phase == CommitBarPhase.Empty, "back online the bar is empty again (" + bar.Phase + ")");
            Pass("M6-05", "Offline: the bar shows Offline, Applica/Anteprima/CAD commands disabled with a reason, restored when back online (workspace connection flag, SYNTHETIC)");

            // ---- Uncertain: a commit whose outcome is unknown blocks the other workspaces and offers only the review
            var session = DesignSess;
            session.RequireCadReview();
            Check(bar.Phase == CommitBarPhase.Uncertain, "Uncertain: the commit bar shows the unknown outcome (" + bar.Phase + ")");
            Check(bar.RecoveryLabel == "Ho controllato il CAD", "Uncertain: the only way forward is 'Ho controllato il CAD' (" + bar.RecoveryLabel + ")");
            Check(!ActionEnabled(CommitIds.Apply) && !ActionEnabled(CommitIds.Preview), "Uncertain: Applica and Anteprima are disabled");
            Check(ActionEnabled(CommitIds.Recover), "Uncertain: the review action is enabled");
            Check(!ActionEnabled(SpacesAssembly) && !ActionEnabled(SpacesLamiera) && Catalog.Find(SpacesAssembly).DisabledReason.Length > 0,
                "Uncertain: the Spazi tab blocks the other CAD workspaces (" + Catalog.Find(SpacesAssembly).DisabledReason + ")");
            RunAction(CommitIds.Recover);
            await WaitUntil(() => bar.Phase == CommitBarPhase.Empty && !ReadBoolean(_design, "_busy") && DesignCtx != null
                && Read<DesignHistory>(_design, "_history") != null && ActionEnabled("design.extrude"), ct);
            Check(ActionEnabled(SpacesAssembly), "after the review the Spazi tab offers the other workspaces again");
            await AssertUnchanged(baseline, "uncertain outcome review", ct);
            Pass("M6-05", "Uncertain: the bar showed the unknown outcome with only 'Ho controllato il CAD', Applica/Anteprima and the other workspaces blocked; the review restored Empty (SYNTHETIC: the guard was set, no commit was made)");
        }

        /// <summary>M6-05 Applied: one real Apply from the bar (extrusion of the fixture sketch) and its XR Undo.</summary>
        private async Task<DocumentState> CheckDesignAppliedAsync(DocumentState baseline, CancellationToken ct)
        {
            var bar = _design.CommitBar;
            bool sawApplied = false;
            Action watch = () => { if (bar.Phase == CommitBarPhase.Applied) sawApplied = true; };
            bar.Changed += watch;
            try
            {
                await PreviewExtrudeAsync(baseline, 20, ct);
                Check(bar.Phase == CommitBarPhase.Ready, "the bar offers Applica");
                RequireFixture();
                RunAction(CommitIds.Apply);
                await SettleAsync(baseline.Revision, ct);
                var applied = await BaselineAsync(ct);
                Check(applied.DocumentId == baseline.DocumentId && applied.Revision != baseline.Revision, "Apply from the bar changed the Inventor revision (" + baseline.Revision + " -> " + applied.Revision + ")");
                Check(sawApplied, "the commit bar went through the Applied state");
                Check(Read<DesignHistory>(_design, "_history").CanUndo, "XR history offers Undo after Apply");
                RequireFixture();
                RunAction("design.history.undo");
                await SettleAsync(applied.Revision, ct);
                var restored = await BaselineAsync(ct);
                Check(restored.Revision != applied.Revision, "XR Undo changed the revision again (" + applied.Revision + " -> " + restored.Revision + ")");
                Pass("M6-05", "Applied: Apply from the commit bar changed revision " + baseline.Revision + " -> " + applied.Revision
                    + " and the bar showed Applied; XR Undo moved it to " + restored.Revision + " (revision equals the original: " + (restored.Revision == baseline.Revision) + ")");
                return restored;
            }
            finally { bar.Changed -= watch; }
        }

        private async Task SettleAsync(string previousRevision, CancellationToken ct)
        {
            await WaitUntil(() =>
            {
                var session = DesignSess;
                Check(session.Status != DesignStatus.Error, "mutation failed: " + session.Error);
                Check(!session.CommitOutcomeUnknown, "mutation outcome unknown");
                var ctx = DesignCtx;
                var history = Read<DesignHistory>(_design, "_history");
                return ReadValue<int>(_design, "_pendingMutations") == 0 && session.Status == DesignStatus.Empty
                    && !ReadBoolean(_design, "_busy") && ctx != null && ctx.State.Revision != previousRevision
                    && history != null && history.State.Revision == ctx.State.Revision;
            }, ct);
        }

        // ------------------------------------------------------------------------------------------- back to Lamiera

        /// <summary>Activates the fixture assembly again through the open-documents list (the way the Browser does), found by kind and name.</summary>
        private async Task ActivateAssemblyDocumentAsync(CancellationToken ct)
        {
            var inspection = (IInspectionBackend)_backend;
            var documents = await inspection.ListOpenAsync(ct);
            var assembly = documents.FirstOrDefault(d => d.Kind == "kAssemblyDocumentObject" && d.Name != null
                && d.Name.StartsWith(FixturePrefix, StringComparison.Ordinal));
            Check(assembly != null, "the fixture assembly is still open in Inventor, found: "
                + string.Join(", ", documents.Select(d => d.Name + "/" + d.Kind)));
            await inspection.ActivateOpenAsync(assembly.Id, ct);
            Check(await TryWaitUntil(() => Session.Scene?.Graph?.Kind == "assembly" && Session.Scene.Graph.DocumentId == _assemblyDocId
                && Session.Status == SessionStatus.Online, 30, ct), "the assembly document is active again");
        }

        private async Task ReturnToAssemblyDocumentAsync(CancellationToken ct)
        {
            RunAction(SpacesInspect);
            await WaitUntil(() => ReferenceEquals(Catalog.Active, _inspect), ct);
            await ActivateAssemblyDocumentAsync(ct);
            RequireFixture();
            await Task.Delay(500, ct);
        }

        private async Task OpenLamieraAsync(CancellationToken ct)
        {
            await ReturnToAssemblyDocumentAsync(ct);
            RunAction(SpacesAssembly);
            await WaitUntil(() => _assembly.Active && ActionEnabled(AssemblyWorkspace.IdComponents), ct);
            var context = Read<AssemblyContext>(_assembly, "_context");
            var sheetMetal = context.Occurrences.FirstOrDefault(o => o.Name.IndexOf("Sheet", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(sheetMetal != null, "the sheet-metal component is listed");
            await SelectOccurrenceAsync(sheetMetal, ct);
            RunAction(AssemblyWorkspace.IdIsolate);
            RunAction(AssemblyWorkspace.IdOpenLamiera);
            Check(await TryWaitUntil(() => _lamiera.Active && !_assembly.Active, 20, ct), "Lamiera opened from the isolated component");
            Check(await TryWaitUntil(() => Session.Scene?.Graph?.Kind == "part" && Session.Scene.Graph.DocumentId != _assemblyDocId, 30, ct),
                "Inventor activated the sheet-metal part document");
            await WaitUntil(() => _lamiera.IsPrimary && !ReadBoolean(_lamiera, "_busy") && Read<DesignContext>(_lamiera, "_designContext") != null
                && ActionEnabled(LamieraWorkspace.IdFlange), ct);
            RequireFixture();
            Pass("M6-06", "'Apri in Lamiera' from the isolated component activated " + Session.Scene.Graph.Root.Name + " and opened Lamiera on it (SYNTHETIC action tap)");
        }

        // -------------------------------------------------------------------------------------------- Lamiera (M6-03/05/08)

        private DesignSession LamieraSess => _lamiera.Session;
        private DesignPreviewView LamieraPreview => Read<DesignPreviewView>(_lamiera, "_previewView");

        private bool _voiceNoted;
        private InjectedRecognizer _recognizer;
        private VoiceCommandBridge _bridge;

        private async Task CheckLamieraAsync(CancellationToken ct)
        {
            CheckPalette("Lamiera (sheet metal part)", _lamiera, new[] { LamieraWorkspace.TabLamiera, LamieraWorkspace.TabFlat, LamieraWorkspace.TabView });
            CheckApplyOnlyOnBar("Lamiera (sheet metal part)", _lamiera);
            var design0 = Read<DesignContext>(_lamiera, "_designContext");
            var state0 = await BaselineAsync(ct);
            Check(state0.DocumentId != _assemblyDocId, "Lamiera works on the sheet part document");
            var candidates = FlangeCandidates(design0);
            Check(candidates.Length > 0, "the sheet exposes straight edges of 100 or 60 mm");

            string edgeId = null;
            foreach (var candidate in candidates)
            {
                if (await TryFlangePreviewAsync(candidate, FlangeHeightMm, ct)) { edgeId = candidate; break; }
                Record("Flange preview rejected on edge " + candidate + ": " + LamieraSess.Error);
                RunAction(CommitIds.Cancel);
                await WaitUntil(() => LamieraSess.Status == DesignStatus.Empty && ActionEnabled(LamieraWorkspace.IdFlange), ct);
            }
            Check(edgeId != null, "at least one real edge of the sheet accepted a flange preview");
            var bar = _lamiera.CommitBar;
            Check(bar.Phase == CommitBarPhase.Ready && bar.CanApply && ActionEnabled(CommitIds.Apply), "Ready: the commit bar offers Applica on the flange preview (" + bar.Phase + ")");
            await AssertUnchanged(state0, "flange preview", ct);

            // ---- M6-03: thumbstick on the Lamiera chip (each step previews; wait for it before the next)
            Check(_lamiera.ArmedField?.Id == LamieraWorkspace.FieldFlangeHeight || _lamiera.TryArmField(LamieraWorkspace.FieldFlangeHeight),
                "the flange height chip is armed: " + _lamiera.LastFieldError);
            FlickPen(1, 0);
            Check(Near(_lamiera.Flange.HeightMm, FlangeHeightMm + 1, 1e-9), "stick right adds one step of 1 mm to the flange height (" + F(_lamiera.Flange.HeightMm) + ")");
            Check(bar.Phase == CommitBarPhase.Previewing || bar.Phase == CommitBarPhase.Draft || bar.Phase == CommitBarPhase.Ready,
                "the changed height moved the bar out of its previous Ready preview (" + bar.Phase + ")");
            await WaitUntil(() => LamieraSess.Status == DesignStatus.PreviewReady || LamieraSess.Status == DesignStatus.Error, ct);
            Check(LamieraSess.Status == DesignStatus.PreviewReady, "the stepped height previewed: " + LamieraSess.Error);
            FlickPen(0, 1);
            FlickPen(1, 0);
            Check(Near(_lamiera.Flange.HeightMm, FlangeHeightMm + 11, 1e-9), "stick up (step 10) then right adds 10 mm (" + F(_lamiera.Flange.HeightMm) + ")");
            await WaitUntil(() => LamieraSess.Status == DesignStatus.PreviewReady || LamieraSess.Status == DesignStatus.Error, ct);
            Check(LamieraSess.Status == DesignStatus.PreviewReady, "the 31 mm flange previewed: " + LamieraSess.Error);
            await AssertUnchanged(state0, "stepped flange previews", ct);
            FlickPen(0, -1);   // back to the default step of 1 mm
            RunAction(CommitIds.Cancel);
            await WaitUntil(() => LamieraSess.Status == DesignStatus.Empty && ActionEnabled(LamieraWorkspace.IdFlange), ct);
            Pass("M6-03", "Lamiera chip: stick steps of 1 and 10 mm each produced a new native preview, revision " + state0.Revision + " unchanged (SYNTHETIC)");

            // ---- M6-03: precision (left Trigger) makes the knob drag ten times slower
            await CheckKnobPrecisionAsync(edgeId, state0, ct);

            // ---- M6-05 / M6-08: voice on the Ready flange preview
            await CheckLamieraVoiceAsync(edgeId, state0, ct);

            // ---- tidy: no open command, back to the Spazi tab
            if (ActionEnabled(CommitIds.Cancel)) RunAction(CommitIds.Cancel);
            await WaitUntil(() => LamieraSess.Status == DesignStatus.Empty, ct);
            Check(bar.Phase == CommitBarPhase.Empty, "Lamiera ends with an empty commit bar");
            Pass("M6-05", "Lamiera: commit bar Ready on the flange preview, Previewing/Draft while the chip changed it, Empty after Annulla; revision " + state0.Revision + " unchanged (SYNTHETIC)");
        }

        private async Task<bool> TryFlangePreviewAsync(string edgeId, double heightMm, CancellationToken ct)
        {
            await WaitUntil(() => ActionEnabled(LamieraWorkspace.IdFlange), ct);
            RunAction(LamieraWorkspace.IdFlange);
            Check(_lamiera.Mode.Armed == SheetMetalCommand.Flange, "flange command is armed");
            _lamiera.Flange.ToggleEdge(edgeId);
            Check(_lamiera.Flange.EdgeIds.Count == 1 && _lamiera.Flange.EdgeIds[0] == edgeId, "flange draft holds the chosen edge");
            RunAction(LamieraWorkspace.IdFlangeHeight);
            Check(_lamiera.ArmedField?.Id == LamieraWorkspace.FieldFlangeHeight, "flange height field is armed on the keypad");
            TypeValue(heightMm);
            await WaitUntil(() => LamieraSess.Status == DesignStatus.PreviewReady || LamieraSess.Status == DesignStatus.Error, ct);
            return LamieraSess.Status == DesignStatus.PreviewReady;
        }

        private static Vector3 AxisWorld(FlangeManipulator manip, Transform model)
            => model.TransformDirection(CadCoordinates.ToLocal(manip.Axis)).normalized;

        private static void AimAtKnob(FlangeManipulator manip, Transform model, Transform hand)
        {
            var axis = AxisWorld(manip, model);
            var side = Vector3.Cross(axis, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.1f) side = Vector3.Cross(axis, Vector3.right).normalized;
            hand.SetPositionAndRotation(manip.KnobWorldPosition + side * 0.15f, Quaternion.LookRotation(-side));
        }

        private static void MoveAlongAxis(FlangeManipulator manip, Transform model, Transform hand, double cadMm)
            => hand.position += AxisWorld(manip, model) * (float)(cadMm * 0.001 * model.lossyScale.x);

        /// <summary>One synthetic frame while the knob may be captured; the workspace advances a held drag in its own Update, so step it now.</summary>
        private void SendDrag(XrInputFrame frame, FlangeManipulator manip)
        {
            Send(frame);
            if (manip.Dragging) Call(_lamiera, "DragStep");
        }

        private async Task CheckKnobPrecisionAsync(string edgeId, DocumentState baseline, CancellationToken ct)
        {
            var hand = _ray.Origin;
            var model = Read<CadSceneView>(_lamiera, "_view").transform;
            var manip = Read<FlangeManipulator>(_lamiera, "_manip");
            var savedHandPosition = hand.position; var savedHandRotation = hand.rotation;
            var savedPosition = model.position; var savedRotation = model.rotation; var savedScale = model.localScale;
            const double DragMm = 10;
            double normal = double.NaN, precise = double.NaN;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    bool withPrecision = pass == 1;
                    Check(await TryFlangePreviewAsync(edgeId, FlangeHeightMm, ct), "flange preview before the " + (withPrecision ? "precision" : "normal") + " drag: " + LamieraSess.Error);
                    Check(manip.Visible, "the flange knob is visible");
                    SendRest();
                    var held = Rest();
                    if (withPrecision) { held.PaletteTrigger = true; Send(held); Check(_input.Precision, "precision mode is on before the press"); }
                    AimAtKnob(manip, model, hand);
                    held.PenTrigger = true;
                    SendDrag(held, manip);
                    Check(manip.Dragging, "a synthetic Trigger held on the knob starts a drag (" + (withPrecision ? "precision" : "normal") + ")");
                    MoveAlongAxis(manip, model, hand, DragMm);
                    SendDrag(held, manip);
                    double reached = _lamiera.Flange.HeightMm;
                    if (withPrecision) precise = reached - FlangeHeightMm; else normal = reached - FlangeHeightMm;
                    held.PenTrigger = false; held.PaletteTrigger = false;
                    Send(held);
                    Check(!manip.Dragging, "releasing the Trigger ends the drag");
                    await WaitUntil(() => LamieraSess.Status == DesignStatus.PreviewReady || LamieraSess.Status == DesignStatus.Error, ct);
                    Check(LamieraSess.Status == DesignStatus.PreviewReady, "a native preview follows the drag: " + LamieraSess.Error);
                    await AssertUnchanged(baseline, (withPrecision ? "precision" : "normal") + " knob drag", ct);
                    RunAction(CommitIds.Cancel);
                    await WaitUntil(() => LamieraSess.Status == DesignStatus.Empty && ActionEnabled(LamieraWorkspace.IdFlange), ct);
                    model.SetPositionAndRotation(savedPosition, savedRotation);
                }
                Check(Near(normal, DragMm, 0.05), "a normal drag of " + F(DragMm) + " mm gives +" + F(normal) + " mm");
                Check(Near(precise, DragMm * FlangeManipulator.PrecisionFactor, 0.05), "the same drag in precision mode gives +" + F(precise) + " mm (a tenth)");
                Pass("M6-03", "pen drag of " + F(DragMm) + " mm on the flange knob: +" + F(normal) + " mm normally, +" + F(precise)
                    + " mm with the left Trigger held (precision, SYNTHETIC frames); previews rendered, revision " + baseline.Revision + " unchanged");
            }
            finally
            {
                SendRest();
                model.localScale = savedScale;
                model.SetPositionAndRotation(savedPosition, savedRotation);
                hand.SetPositionAndRotation(savedHandPosition, savedHandRotation);
            }
        }

        /// <summary>Pushes recognized text through the real push-to-talk controller, router and bridge with silent samples.</summary>
        private async Task SpeakAsync(VoiceCommandBridge bridge, InjectedRecognizer recognizer, string text, CancellationToken ct)
        {
            bridge.Pump();
            recognizer.Text = text;
            var talk = bridge.Controller;
            talk.Press();
            Check(talk.State == PushToTalkState.Pressed, "push-to-talk controller accepted the injected press");
            talk.Tick();
            Check(talk.State == PushToTalkState.Listening, "push-to-talk controller entered Listening");
            var silence = new short[PushToTalkController.SampleRate / 2];
            talk.AppendAudio(silence, silence.Length);
            talk.Release();
            await WaitUntil(() => talk.State == PushToTalkState.Result || talk.State == PushToTalkState.Error, ct);
            Check(talk.State == PushToTalkState.Result, "injected recognition result was delivered: " + talk.ErrorMessage);
            Check(!talk.MicrophoneOpen, "no microphone capture is open");
            bridge.Pump();
        }

        private Task SpeakAsync(string text, CancellationToken ct) => SpeakAsync(_bridge, _recognizer, text, ct);

        /// <summary>M6-08 on the live workspace: Applica does not commit, a disabled label and an unknown phrase change nothing, an enabled label runs.</summary>
        private async Task CheckLamieraVoiceAsync(string edgeId, DocumentState baseline, CancellationToken ct)
        {
            var voiceTarget = Read<WorkspaceVoiceTarget>(App, "_voiceTarget");
            Check(voiceTarget != null && voiceTarget.InSession && voiceTarget.AcceptsVoice && voiceTarget.Catalog != null, "the app voice target is active in the session and holds the catalog");
            _recognizer = new InjectedRecognizer();
            _bridge = new VoiceCommandBridge(voiceTarget, _recognizer, armingThreshold: TimeSpan.Zero);
            if (!_voiceNoted) { Record("Speech is injected text pushed through the real PushToTalkController (SYNTHETIC: no microphone, no audio)"); _voiceNoted = true; }
            try
            {
                Check(await TryFlangePreviewAsync(edgeId, FlangeHeightMm, ct), "flange preview before the voice cases: " + LamieraSess.Error);
                var bar = _lamiera.CommitBar;
                Check(bar.Phase == CommitBarPhase.Ready, "a Ready preview exists for the voice cases");
                _lamiera.DisarmField();   // no numeric field is armed: spoken numbers would otherwise be dictation, not commands

                // M6-08: "applica" with a valid preview opens only the confirmation
                var match = Catalog.ResolveVoice("applica");
                Check(match.Kind == VoiceMatchKind.Ok && match.Action != null && match.Action.Id == CommitIds.Apply && !match.Action.VoiceInvokes,
                    "the catalog finds 'Applica' on the commit bar and marks it as not voice-invokable");
                string plan = LamieraSess.Preview.PlanId; int version = _lamiera.Flange.Version;
                await SpeakAsync("applica", ct);
                Check(_bridge.Outcome == VoiceOutcomeKind.ApplyConfirmationShown, "spoken Applica only shows the confirmation, outcome " + _bridge.Outcome);
                Check(LamieraSess.Status == DesignStatus.PreviewReady && LamieraSess.Preview.PlanId == plan && ReadValue<int>(_lamiera, "_pendingMutations") == 0 && bar.Phase == CommitBarPhase.Ready,
                    "spoken Applica started no commit and left the preview and the bar in place");
                await AssertUnchanged(baseline, "spoken Applica", ct);
                Pass("M6-08", "spoken \"applica\" with a valid preview opened only the confirmation: no commit, preview kept, revision " + baseline.Revision + " unchanged (injected speech)");

                // M6-08: a label the catalog finds but the state disables is refused; an unknown phrase is rejected; nothing mutates
                var disabled = DisabledVoiceActions().FirstOrDefault();
                if (disabled != null)
                {
                    await SpeakAsync(disabled.Label, ct);
                    Check(_bridge.Outcome == VoiceOutcomeKind.NotExecuted || _bridge.Outcome == VoiceOutcomeKind.Rejected,
                        "the disabled action '" + disabled.Label + "' is not executed, outcome " + _bridge.Outcome);
                    Check(LamieraSess.Status == DesignStatus.PreviewReady && LamieraSess.Preview.PlanId == plan && _lamiera.Flange.Version == version && ReadValue<int>(_lamiera, "_pendingMutations") == 0,
                        "the disabled voice command changed neither draft nor preview");
                    Record("diag disabled label spoken: '" + disabled.Label + "' (" + disabled.Id + "): " + disabled.DisabledReason + " -> " + _bridge.Outcome);
                }
                else NotCovered("M6-08-disabled", "no disabled, voice-invokable action with a unique label was available on Lamiera to speak");
                await SpeakAsync("vorrei una pizza", ct);
                Check(_bridge.Outcome == VoiceOutcomeKind.Rejected, "a phrase outside the vocabulary is rejected, outcome " + _bridge.Outcome);
                Check(LamieraSess.Status == DesignStatus.PreviewReady && LamieraSess.Preview.PlanId == plan && _lamiera.Flange.Version == version && ReadValue<int>(_lamiera, "_pendingMutations") == 0,
                    "the unknown phrase changed neither draft nor preview");
                await AssertUnchanged(baseline, "disabled and unknown voice phrases", ct);
                Pass("M6-08", "a disabled action label and an out-of-vocabulary phrase were refused with no mutation (draft, preview and revision " + baseline.Revision + " unchanged)");

                // positive control: an enabled label resolved on the catalog runs through the same Invoke (view only here)
                Check(ActionEnabled(LamieraWorkspace.IdFit), "the Adatta action is enabled");
                var fit = Catalog.ResolveVoice("adatta");
                Check(fit.Kind == VoiceMatchKind.Ok && fit.Action.Id == LamieraWorkspace.IdFit, "the catalog resolves 'adatta' to " + LamieraWorkspace.IdFit);
                await SpeakAsync("adatta", ct);
                Check(_bridge.Outcome == VoiceOutcomeKind.Executed, "an enabled catalog label runs, outcome " + _bridge.Outcome);
                Check(LamieraSess.Status == DesignStatus.PreviewReady && LamieraSess.Preview.PlanId == plan, "the view command left the preview untouched");
                await AssertUnchanged(baseline, "spoken Adatta", ct);

                // M6-03: dictation in the armed field changes only that field, after a physical confirmation
                _lamiera.DisarmField();
                Check(_lamiera.TryArmField(LamieraWorkspace.FieldFlangeHeight), "flange height field is armed for dictation: " + _lamiera.LastFieldError);
                double angleBefore = _lamiera.Flange.AngleDegrees; string datumBefore = _lamiera.Flange.Datum;
                await SpeakAsync(DictatedPhrase, ct);
                Check(_bridge.Outcome == VoiceOutcomeKind.DictationProposed, "dictation is proposed, outcome " + _bridge.Outcome);
                var proposal = _bridge.Dictation.Pending;
                Check(proposal != null && proposal.Accepted && proposal.FieldId == LamieraWorkspace.FieldFlangeHeight && Near(proposal.Value, DictatedHeightMm, 1e-9),
                    "dictation proposal is " + F(DictatedHeightMm) + " mm for the height field");
                Check(Near(_lamiera.Flange.HeightMm, FlangeHeightMm, 1e-9), "the proposal alone does not change the draft");
                Check(_bridge.ConfirmPendingAny(), "the physical Conferma path applies the dictated value");
                Check(Near(_lamiera.Flange.HeightMm, DictatedHeightMm, 1e-9) && Near(_lamiera.Flange.AngleDegrees, angleBefore, 1e-9) && _lamiera.Flange.Datum == datumBefore,
                    "only the height changed (" + F(_lamiera.Flange.HeightMm) + " mm)");
                Check(!ActionEnabled(CommitIds.Apply) || LamieraSess.Preview?.PlanId != plan, "Applica needs the new preview of the dictated height");
                await WaitUntil(() => LamieraSess.Status == DesignStatus.PreviewReady || LamieraSess.Status == DesignStatus.Error, ct);
                Check(LamieraSess.Status == DesignStatus.PreviewReady, "the dictated height produced a new preview: " + LamieraSess.Error);
                await AssertUnchanged(baseline, "dictated height", ct);
                Pass("M6-03", "dictation \"" + DictatedPhrase + "\" in the armed height field proposed " + F(DictatedHeightMm) + " mm; after the physical confirmation only the height changed and a new preview was needed (injected speech)");
            }
            finally
            {
                _bridge.Dispose();
                _bridge = null;
            }
            if (ActionEnabled(CommitIds.Cancel)) RunAction(CommitIds.Cancel);
            await WaitUntil(() => LamieraSess.Status == DesignStatus.Empty && ActionEnabled(LamieraWorkspace.IdFlange), ct);
        }

        /// <summary>Palette actions that are disabled right now, speakable, and found by exactly one name on the live catalog.</summary>
        private IEnumerable<XrAction> DisabledVoiceActions()
        {
            var result = new List<XrAction>();
            foreach (var tab in Catalog.Tabs)
                foreach (var action in Catalog.Palette(tab.Id))
                {
                    if (action.Enabled || !action.VoiceInvokes || action.Tab == ActionCatalog.CommitTab
                        || action.Id.IndexOf(".history.", StringComparison.Ordinal) >= 0) continue;
                    var resolved = Catalog.ResolveVoice(action.Label);
                    if (resolved.Kind == VoiceMatchKind.Disabled && resolved.Action != null && resolved.Action.Id == action.Id) result.Add(action);
                }
            return result;
        }

        private static string[] FlangeCandidates(DesignContext context)
        {
            var lines = context.Edges.Where(IsLine).ToArray();
            if (lines.Length == 0) lines = context.Edges.ToArray();
            return lines
                .Select(edge => new { edge.Id, Length = EdgeLength(edge) })
                .Where(item => Near(item.Length, 100, 0.5) || Near(item.Length, 60, 0.5))
                .OrderBy(item => Near(item.Length, 100, 0.5) ? 0 : 1)
                .Select(item => item.Id)
                .Take(6)
                .ToArray();
        }

        private static double EdgeLength(DesignEdge edge)
        {
            double length = 0;
            for (int i = 1; i < edge.PointsMm.Count; i++) length += (edge.PointsMm[i] - edge.PointsMm[i - 1]).Length;
            return length;
        }

        // ----------------------------------------------------------------------------- M6-08 on a controlled catalog

        private sealed class SyntheticProvider : IActionProvider
        {
            private readonly XrAction[] _actions;
            public SyntheticProvider(XrTab[] tabs, params XrAction[] actions) { Tabs = tabs; _actions = actions; }
            public IReadOnlyList<XrTab> Tabs { get; }
            public IEnumerable<XrAction> Actions => _actions;
            public IEnumerable<XrAction> ContextActions(UiSelectionKind selection) => Array.Empty<XrAction>();
            public CommitBarState CommitBar => null;
        }

        private int _syntheticInvocations;

        /// <summary>
        /// Ambiguity cannot be produced on demand with the labels of the real workspaces, so the same ActionCatalog class is built over a
        /// controlled provider (labelled synthetic in the log): two enabled actions that share a spoken name, one disabled action.
        /// Voice reaches it through the real WorkspaceVoiceTarget, PushToTalkController and VoiceCommandBridge.
        /// </summary>
        private async Task CheckVoiceCatalogAsync(CancellationToken ct)
        {
            _syntheticInvocations = 0;
            var tab = new XrTab("prova", "Prova");
            var provider = new SyntheticProvider(new[] { tab },
                new XrAction("prova.uno", "Valore prova: 5 mm", "prova", () => true, () => _syntheticInvocations++),
                new XrAction("prova.due", "Valore prova: 7 mm", "prova", () => true, () => _syntheticInvocations++),
                new XrAction("prova.fermo", "Quarzo fittizio", "prova", () => false, () => _syntheticInvocations++, () => "Spento per la prova."),
                new XrAction("prova.ok", "Zaffiro fittizio", "prova", () => true, () => _syntheticInvocations++));
            var catalog = new ActionCatalog(new SyntheticProvider(new XrTab[0]));
            catalog.SetActive(provider);

            var ambiguous = catalog.ResolveVoice("valore prova");
            Check(ambiguous.Kind == VoiceMatchKind.Ambiguous && ambiguous.Conflicts.Count == 2 && ambiguous.Action == null, "two enabled actions with the same spoken name resolve as ambiguous");
            var disabled = catalog.ResolveVoice("quarzo fittizio");
            Check(disabled.Kind == VoiceMatchKind.Disabled && disabled.Reason == "Spento per la prova.", "a disabled action resolves as disabled with its reason");
            var found = catalog.ResolveVoice("zaffiro fittizio");
            Check(found.Kind == VoiceMatchKind.Ok && found.Action.Id == "prova.ok", "an unambiguous enabled name resolves to its action");
            Check(catalog.ResolveVoice("nessuna di queste").Kind == VoiceMatchKind.NotFound, "an unknown name is not found");
            Check(_syntheticInvocations == 0, "resolving never invokes anything");

            var target = new WorkspaceVoiceTarget(null, catalog: catalog) { InSession = true };
            var recognizer = new InjectedRecognizer();
            var bridge = new VoiceCommandBridge(target, recognizer, armingThreshold: TimeSpan.Zero);
            var state = await BaselineAsync(ct);
            try
            {
                await SpeakAsync(bridge, recognizer, "valore prova", ct);
                Check(bridge.Outcome == VoiceOutcomeKind.Rejected, "the ambiguous phrase is rejected by the voice bridge, outcome " + bridge.Outcome);
                Check(_syntheticInvocations == 0, "the ambiguous phrase executed nothing");
                await SpeakAsync(bridge, recognizer, "quarzo fittizio", ct);
                Check(bridge.Outcome == VoiceOutcomeKind.NotExecuted, "the disabled phrase is not executed, outcome " + bridge.Outcome);
                Check(_syntheticInvocations == 0, "the disabled phrase executed nothing");
                await SpeakAsync(bridge, recognizer, "zaffiro fittizio", ct);
                Check(bridge.Outcome == VoiceOutcomeKind.Executed && _syntheticInvocations == 1, "the unambiguous enabled phrase ran exactly once, outcome " + bridge.Outcome);
            }
            finally { bridge.Dispose(); }
            await AssertUnchanged(state, "voice on the controlled catalog", ct);
            Pass("M6-08", "ActionCatalog.ResolveVoice (SYNTHETIC controlled provider, same class): ambiguity rejected, disabled action refused with its reason, "
                + "neither mutated anything; the unambiguous phrase ran once through the real PushToTalkController and VoiceCommandBridge (injected speech)");
        }

        // ------------------------------------------------------------------------------------------ M6-05 core table

        private void CheckCommitBarCore()
        {
            var cases = new (string name, CommitBarInputs inputs, CommitBarPhase phase, bool apply)[]
            {
                ("Empty", new CommitBarInputs(true, false, false, false, false, false, ""), CommitBarPhase.Empty, false),
                ("Draft", new CommitBarInputs(true, false, false, false, false, true, ""), CommitBarPhase.Draft, false),
                ("Previewing", new CommitBarInputs(true, false, false, true, false, true, ""), CommitBarPhase.Previewing, false),
                ("Ready", new CommitBarInputs(true, false, false, false, true, true, ""), CommitBarPhase.Ready, true),
                ("Stale", new CommitBarInputs(true, false, true, false, true, true, ""), CommitBarPhase.Stale, false),
                ("Uncertain", new CommitBarInputs(true, true, true, false, true, true, ""), CommitBarPhase.Uncertain, false),
                ("Error", new CommitBarInputs(true, false, false, false, false, true, "errore"), CommitBarPhase.Error, false),
                ("Offline", new CommitBarInputs(false, false, false, false, true, true, ""), CommitBarPhase.Offline, false),
            };
            foreach (var item in cases)
            {
                var state = new CommitBarState();
                state.Update(item.inputs, 100);
                Check(state.Phase == item.phase, "pure table: " + item.name + " derives " + item.phase + " (got " + state.Phase + ")");
                Check(state.CanApply == item.apply, "pure table: " + item.name + (item.apply ? " allows" : " does not allow") + " Apply");
            }
            var stale = new CommitBarState(); stale.Update(cases[4].inputs, 100);
            Check(stale.RecoveryLabel == "Aggiorna documento", "pure table: Stale offers only 'Aggiorna documento'");
            var applied = new CommitBarState(); applied.MarkApplied(100);
            Check(applied.Phase == CommitBarPhase.Applied && !applied.CanApply, "pure table: Applied does not allow Apply");
            Pass("M6-05-core", "pure CommitBarState table (not a live workspace): Empty, Draft, Previewing, Ready, Stale, Uncertain, Error, Offline and Applied derive as specified; "
                + "only Ready allows Apply, so stale, uncertain and offline never enable it");
        }

        // ---------------------------------------------------------------------------------------------------- cleanup

        private async Task CleanupAsync()
        {
            try
            {
                EndSynthetic();
                if (_design != null && _design.Active) _design.Close();
                if (_lamiera != null && _lamiera.Active) _lamiera.Close();
                if (_assembly != null && _assembly.Active) _assembly.Close();
                if (_assemblyDocId != null && _backend is IInspectionBackend && Session?.Scene?.Graph?.DocumentId != _assemblyDocId)
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40)))
                        await ActivateAssemblyDocumentAsync(timeout.Token);
                }
                Record("Clean end: synthetic input removed, workspaces closed, the assembly document is the active one again (restore the fixture before rerunning: --restore-quest m6)");
            }
            catch (Exception ex)
            {
                Record("Cleanup could not complete: " + ex.GetType().Name + ": " + ex.Message + " (activate the assembly document before --restore-quest m6)");
            }
        }

        private void OnDestroy() { EndSynthetic(); }

        private sealed class InjectedRecognizer : ISpeechRecognizer
        {
            public string Text;
            public Task<string> RecognizeAsync(short[] pcm16, CancellationToken cancellationToken) => Task.FromResult(Text);
        }
    }
}
#endif
