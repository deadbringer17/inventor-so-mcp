#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr.Input;
using InventorXrSo.Xr.Voice;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using InputAction = InventorXrSo.Core.Input.InputAction;
using InputMap = InventorXrSo.Core.Input.InputMap;
using InputState = InventorXrSo.Core.Input.InputState;
using Key = InventorXrSo.Core.Input.Key;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Opt-in, fixture-scoped M9 (context-driven navigation) acceptance runner. It reuses the dedicated M6 fixture (an assembly with
    /// a block and a sheet-metal sheet, both parts kept open) and, like M8, refuses to run on any other document
    /// (<see cref="QuestAcceptanceRunner.FixtureMilestone"/> = m6, so the guard prefix is XR_M6_Quest_Acceptance).
    ///
    /// Three outcomes are kept apart (CLAUDE.md): unit / FakeAddIn tests (suites, not here), this runner with SYNTHETIC input, and the
    /// physical trial (M9-12, never claimed here). Every controller event is a frame of a <see cref="SyntheticInputSource"/> on the
    /// application's own <see cref="XrInput"/>; the double Trigger is two such presses on a real model body with the detector clock
    /// of the workspace driven by the runner (window of 350 ms kept, no real-time sleeping), and X held is a synthetic Poll with
    /// explicit timestamps. Inventor answers are real. The log carries "PASS [M9-xx] ..." and "NOT COVERED [M9-xx...] reason"; the last
    /// line is "PASS COMPLETE" only when no sub-case of the runner was left uncovered, else "PARTIAL; NOT COVERED: ...". Gates that
    /// belong to other evidence (probe, suites, physical) are reported as NOT COVERED with a "-probe", "-suite" or "-physical" suffix
    /// and do not turn the verdict into PARTIAL.
    ///
    /// Written without a device (no Quest in the authoring environment): compiled with XR_SO_ACCEPTANCE and contract-tested, never run.
    /// </summary>
    internal class M9QuestAcceptance : QuestAcceptanceRunner
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
            "AppController._input",
            "AppController._legend",
            "AppController._ghost",
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
            "DesignWorkspace._face",
            "AssemblyWorkspace._context",
            "AssemblyWorkspace._occurrence",
            "AssemblyWorkspace._busy",
            "LamieraWorkspace._busy",
            "LamieraWorkspace._designContext",
            "LamieraWorkspace._visible",
            "LamieraWorkspace._online",
            "LamieraWorkspace._kind",
            "LamieraWorkspace._sheetContext",
            "LamieraWorkspace._mode",
            "LamieraWorkspace._state",
        };

        /// <summary>Every gate of the spec (spec section "Gate di consegna M9"): the log must name each of them (PASS, NOT COVERED or the verdict).</summary>
        internal static readonly string[] GateIds =
        {
            "M9-01", "M9-02", "M9-03", "M9-04", "M9-05", "M9-06", "M9-07", "M9-08", "M9-09", "M9-10", "M9-11", "M9-12",
        };

        /// <summary>Suffixes of the NOT COVERED gates that name evidence outside this runner (they never make the verdict PARTIAL).</summary>
        internal static readonly string[] OutsideRunnerSuffixes = { "-physical", "-probe", "-suite", "-nested" };

        protected const string FeaturePrefix = "design.feature.p.";
        protected const string SketchName = "Base_M6";
        protected const double ExtrudeStartMm = 10, ExtrudeEditedMm = 12;

        /// <summary>The M9 runner on the M6 fixture reports the known feature-edit gaps; the nested runner repeats the edit only to prove navigation.</summary>
        protected virtual bool ReportKnownFeatureGaps => true;
        protected virtual string RestoreHint => "--restore-quest m6";

        protected override string Milestone => "m9";
        protected override string FixtureMilestone => "m6";
        protected override int TimeoutSeconds => 900;
        protected override string CompletionNote =>
            "SYNTHETIC input and the dedicated M6 Inventor fixture only; the physical trial (M9-12), legend placement and readability, the microphone, "
            + "the face_feature live probe (M9-08) and the M1-M8 regression suites are separate evidence and remain open until recorded in docs/xr-m9-verification.md";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M9QuestAcceptance>("xr_m9_acceptance");

        // ---- application parts
        protected InspectWorkspace _inspect;
        protected DesignWorkspace _design;
        protected AssemblyWorkspace _assembly;
        protected LamieraWorkspace _lamiera;
        protected UiShell _shell;
        protected Workbench _bench;
        protected CadSceneView _view;
        protected ControllerRay _ray;
        protected Transform _head;
        protected ControllerLegend _legend;
        protected GhostContext _ghost;
        protected IDesignWorkspaceBackend _backend;
        protected IInspectionBackend _inspection;
        protected string _assemblyDocId;

        // ---- synthetic input
        protected XrInput _input;
        protected SyntheticInputSource _source;
        protected IXrInputSource _originalSource;
        protected bool _syntheticKeypadNoted;
        /// <summary>Clock of the double Trigger detectors: the runner moves it instead of sleeping, so the 350 ms window is exact.</summary>
        protected double _clock = 1000;
        protected bool? _legendWasOn;
        protected readonly HashSet<InputState> _liveStates = new HashSet<InputState>();

        protected ActionCatalog Catalog => Read<ActionCatalog>(App, "_catalog");
        protected NavigationStack Nav => App.Navigation;

        // ------------------------------------------------------------------------------------------------------------- verdict

        protected override string Completion()
        {
            var open = NotCoveredGates.Where(g => !IsOutsideRunner(g)).Distinct().ToList();
            if (open.Count == 0) return "PASS COMPLETE; " + CompletionNote;
            return "PARTIAL; NOT COVERED: " + string.Join(", ", open) + "; " + CompletionNote;
        }

        private static bool IsOutsideRunner(string gate) => OutsideRunnerSuffixes.Any(s => gate.EndsWith(s, StringComparison.Ordinal));

        // ------------------------------------------------------------------------------------------------------------------ run

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            RequireFixture();
            Check(fixture.Graph.Kind == "assembly", "the M9 run starts on the fixture assembly (found " + fixture.Graph.Kind + ")");
            _assemblyDocId = fixture.Graph.DocumentId;
            Record("Dedicated M6 fixture reused by M9 (guard " + FixturePrefix + "): " + fixture.Graph.Root.Name);
            Record("Navigation here is driven like the app does: the active document decides the context; a double Trigger is two SYNTHETIC presses on a real body");

            if (!ReadBoolean(App, "_inSession")) Call(App, "EnterSession", EnvironmentMode.StudioVr);
            BindApp();
            BeginSynthetic();
            try
            {
                await CheckAssemblyRootAsync(ct);          // M9-01, M9-02 (blocked), M9-05 (Assieme)
                await CheckBlockAsync(ct);                  // M9-02, M9-03, M9-04 (X held), M9-05 (Parte), M9-06 live, M9-09
                await CheckSheetAsync(ct);                  // M9-02, M9-04 (Documento tab), M9-05 (Lamiera), M9-09 (unsupported type)
                await CheckVoiceNavigationAsync(ct);        // M9-10
                await CheckDesktopChangeAndJumpAsync(ct);   // M9-01
                await CheckSubassemblyAsync(ct);            // M9-02
                await CheckInputMapAsync(ct);               // M9-06
                await CheckLegendAsync(ct);                 // M9-07
            }
            finally
            {
                await CleanupAsync();
            }

            NotCovered("M9-08-probe", "face_feature on every supported feature type against Inventor 2027 is the bridge live probe (bridge/tests/*LiveProbe), not this runner: here only the block extrusion was read");
            NotCovered("M9-11-suite", "regressions M1-M8 are the core, EditMode and bridge suites plus the migrated M6/M7 runners (each needs its own passing run on the Quest): this runner does not certify them");
            NotCovered("M9-12-physical", "sitting trial with the controllers: discovering the double Trigger and Torna, legend readability, no accidental Torna in 30 minutes need a person wearing the Quest");
            NotCovered("M9-07-physical", "label placement on the real controller models, readability, the 25 degree look cone with a real head and haptics: only the label set, texts, progress and toggle were checked, with a synthetic state");
            NotCovered("M9-06-B-physical", "button B (push-to-talk) is routed by PushToTalkInput through OVRInput, not by the dispatcher: only its InputMap row (Parla) was checked");
            NotCovered("M9-10-physical", "microphone and audio: recognized text was injected into the real PushToTalkController with silent samples");
        }

        protected void BindApp()
        {
            _inspect = Read<InspectWorkspace>(App, "_inspect");
            _design = Read<DesignWorkspace>(App, "_design");
            _assembly = Read<AssemblyWorkspace>(App, "_assembly");
            _lamiera = Read<LamieraWorkspace>(App, "_lamiera");
            _shell = Read<UiShell>(App, "_shell");
            _bench = Read<Workbench>(App, "_workbench");
            _view = Read<CadSceneView>(App, "sceneView");
            _ray = Read<ControllerRay>(App, "ray");
            _head = Read<Transform>(App, "head");
            _legend = Read<ControllerLegend>(App, "_legend");
            _ghost = Read<GhostContext>(App, "_ghost");
            Check(_inspect != null && _design != null && _assembly != null && _lamiera != null && _shell != null && _bench != null
                && _view != null && _ray != null && _head != null && _legend != null && _ghost != null && Catalog != null,
                "AppController exposes the workspaces, shell, workbench, ray, legend and the ghost context");
            _backend = Read<IDesignWorkspaceBackend>(_design, "_backend");
            _inspection = _backend as IInspectionBackend;
            Check(_backend != null && _inspection != null, "the shared backend serves Design, document activation and face_feature");
        }

        // ------------------------------------------------------------------------------------------------------------ plumbing

        protected void RunAction(string id)
        {
            var action = Catalog.Find(id);
            Check(action != null, "the action catalog has no action '" + id + "'");
            Check(action.Enabled, "action '" + id + "' is disabled: " + action.DisabledReason);
            Check(action.TryInvoke(), "action '" + id + "' did not run");
        }

        protected bool ActionEnabled(string id) => Catalog.Find(id)?.Enabled == true;

        protected static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        protected static bool Near(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;

        protected static async Task<bool> TryWaitUntil(Func<bool> condition, double seconds, CancellationToken ct)
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

        /// <summary>WaitUntil that names what it waited for and what the app said meanwhile (HUD, workspace notices).</summary>
        protected async Task WaitDiag(Func<bool> condition, string what, CancellationToken ct, int seconds = 45)
        {
            if (await TryWaitUntil(condition, seconds, ct)) return;
            throw new InvalidOperationException("timed out after " + seconds + "s waiting for " + what + " [context=" + App.Context + ", levels="
                + Nav.Levels.Count + ", top=" + Nav.Top?.Name + ", assembly.notice='" + _assembly.Notice + "', design.notice='" + _design.Notice
                + "', lamiera.notice='" + _lamiera.Notice + "', hud='" + HudText().Replace('\n', '|') + "']");
        }

        /// <summary>Why a synthetic Trigger press did not select: ray hit, selected occurrence, workspace state (read by reflection).</summary>
        protected string PressDiagnostics()
        {
            var ray = new Ray(_ray.Origin.position, _ray.Origin.forward);
            bool hit = CadRaycaster.TryPick(ray, 20, out var body, out _, out var point);
            bool Prop(string name)
            {
                var info = typeof(AssemblyWorkspace).GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                return info != null && (bool)info.GetValue(_assembly);
            }
            return "[diag: rayHit=" + hit + " body=" + (body?.Instance?.OccurrenceId ?? "-") + " selected=" + (Read<AssemblyOccurrence>(_assembly, "_occurrence")?.Id ?? "-")
                + " Editable=" + Prop("Editable") + " CanCapture=" + Prop("CanCapture") + " Idle=" + Prop("Idle")
                + " busy=" + ReadBoolean(_assembly, "_busy") + " pickSuppressed=" + (_assembly.PickSuppressed?.Invoke() == true)
                + " penTracked=" + _input.PenTracked + " origin=" + _ray.Origin.position + " fwd=" + _ray.Origin.forward + "]";
        }

        private string DescribeSheetContext()
        {
            var c = Read<SheetMetalContext>(_lamiera, "_sheetContext");
            var mode = Read<SheetMetalMode>(_lamiera, "_mode");
            return c == null ? "null" : "{isSheetMetal=" + c.IsSheetMetal + " complete=" + c.IsComplete + " reason=" + c.Reason + " ctxRev=" + c.State?.Revision
                + " availability=" + mode?.Availability + " modeReason=" + mode?.Reason + " stateRev=" + Read<DocumentState>(_lamiera, "_state")?.Revision + "}";
        }

        protected string HudText() => _shell?.Hud == null ? ""
            : string.Join("\n", _shell.Hud.Canvas.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => t.text));

        protected async Task<DocumentState> BaselineAsync(CancellationToken ct) => await _backend.GetDocumentStateAsync(ct);

        protected async Task AssertUnchanged(DocumentState baseline, string what, CancellationToken ct)
        {
            var now = await _backend.GetDocumentStateAsync(ct);
            Check(now.DocumentId == baseline.DocumentId && now.Revision == baseline.Revision,
                what + " leaves Inventor's revision unchanged (" + baseline.Revision + " vs " + now.Revision + ")");
        }

        protected void PickItem(IActionProvider workspace, string prefix, string label, bool suffix = false)
        {
            var action = workspace.Actions.FirstOrDefault(a => a.Id.StartsWith(prefix, StringComparison.Ordinal)
                && (suffix ? a.Label.EndsWith(label, StringComparison.Ordinal) : a.Label == label));
            Check(action != null, "the open list has no entry '" + label + "'");
            Check(action.Enabled && action.TryInvoke(), "list entry '" + label + "' did not run");
        }

        protected NumericEntry ActiveEntry()
        {
            var active = Catalog.Active;
            if (active is DesignWorkspace design) return design.ActiveEntry;
            if (active is LamieraWorkspace lamiera) return lamiera.ActiveEntry;
            if (active is AssemblyWorkspace assembly) return assembly.ActiveEntry;
            return null;
        }

        protected void TypeValue(double value)
        {
            if (!_syntheticKeypadNoted) { Record("Numbers are typed on the keypad entry by the runner (synthetic keypad input)"); _syntheticKeypadNoted = true; }
            var entry = ActiveEntry();
            Check(entry != null && entry.Editing, "the numeric keypad is open");
            foreach (char c in F(value)) entry.Type(c);
            Check(entry.Commit(out var reason), "keypad accepted " + F(value) + ": " + reason);
        }

        protected void SnapPoses() { _bench.Snap(); }

        protected Vector3 OccurrenceCenter(string occurrenceId)
        {
            var instance = _view.Find(occurrenceId);
            Check(instance != null, "the occurrence " + occurrenceId + " is in the scene");
            var bounds = ScenePlacement.LocalBounds(instance.transform);
            return instance.transform.TransformPoint(bounds.center);
        }

        /// <summary>The assembly context can still be refreshing after a previous step (partial occurrence list): wait for the fragment instead of failing on the first look.</summary>
        protected async Task<AssemblyOccurrence> FindOccurrenceAsync(string nameFragment, CancellationToken ct)
        {
            await TryWaitUntil(() => Read<AssemblyContext>(_assembly, "_context")?.Occurrences
                .Any(o => o.Name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0) == true, 20, ct);
            return FindOccurrence(nameFragment);
        }

        protected AssemblyOccurrence FindOccurrence(string nameFragment)
        {
            var context = Read<AssemblyContext>(_assembly, "_context");
            Check(context != null, "the assembly context is loaded");
            var occurrence = context.Occurrences.FirstOrDefault(o => o.Name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0);
            Check(occurrence != null, "the assembly lists a component named like '" + nameFragment + "' ("
                + string.Join(", ", context.Occurrences.Select(o => o.Name)) + ")");
            return occurrence;
        }

        private Transform LeftAnchor() => _head.parent == null ? null
            : _head.parent.Find("LeftHandAnchor/LeftControllerAnchor") ?? _head.parent.Find("LeftHandAnchor");

        // ------------------------------------------------------------------------------------------------------- synthetic input

        protected void BeginSynthetic()
        {
            _input = Read<XrInput>(App, "_input");
            Check(_input != null, "AppController owns the XrInput");
            _originalSource = _input.Source;
            _source = new SyntheticInputSource { Next = Rest() };
            _input.Source = _source;
            Check(_input.Synthetic, "the input source of the run is flagged synthetic");
            _assembly.UiHitOverride = () => false;   // deterministic UI hit: the real controller's ray must not decide the gesture
            _lamiera.UiHitOverride = () => false;
            _design.UiHitOverride = () => false;
            _assembly.DoubleTriggerClock = () => _clock;
            _design.DoubleTriggerClock = () => _clock;
            _lamiera.DoubleTriggerClock = () => _clock;
            Send(Rest());
            Record("Controller events of this run are SYNTHETIC XrInput frames; the double Trigger detector clock is driven by the runner (350 ms window respected)");
        }

        protected void EndSynthetic()
        {
            if (_input == null) return;
            if (_source != null) Send(Rest());
            if (_originalSource != null) _input.Source = _originalSource;
            if (_assembly != null) { _assembly.UiHitOverride = null; _assembly.DoubleTriggerClock = null; }
            if (_lamiera != null) { _lamiera.UiHitOverride = null; _lamiera.DoubleTriggerClock = null; }
            if (_design != null) { _design.UiHitOverride = null; _design.DoubleTriggerClock = null; }
            _source = null;
        }

        protected static XrInputFrame Rest() => new XrInputFrame { PenTracked = true, PaletteTracked = true };

        protected void Send(XrInputFrame frame)
        {
            _source.Next = frame;
            _input.Poll(frame, Time.unscaledTime);
        }

        protected void SendRest() => Send(Rest());

        protected void FlickPalette(int direction)
        {
            var frame = Rest(); frame.PaletteStick = new Vector2(direction, 0); Send(frame); SendRest();
        }

        protected void FlickPen(float x, float y)
        {
            var frame = Rest(); frame.PenStick = new Vector2(x, y); Send(frame); SendRest();
        }

        protected void PressX()
        {
            var frame = Rest(); frame.X = true; Send(frame); SendRest();
        }

        protected void TriggerTap()
        {
            SendRest();
            var frame = Rest(); frame.PenTrigger = true; Send(frame);
            SendRest();
        }

        protected void AimAt(Vector3 target, Vector3 away, float distance = 0.3f)
        {
            var direction = away.normalized;
            _ray.Origin.SetPositionAndRotation(target + direction * distance, Quaternion.LookRotation(-direction));
        }

        protected void AimAtSky() => _ray.Origin.SetPositionAndRotation(_head.position, Quaternion.LookRotation(Vector3.up));

        protected Vector3 AwayFromHead(Vector3 target)
        {
            var away = _head.position - target;
            if (away.sqrMagnitude < 1e-6f) away = Vector3.back;
            return new Vector3(away.x, Mathf.Max(0.2f, away.y), away.z);
        }

        /// <summary>
        /// The real double Trigger path: press 1 selects the target under the ray, the runner waits for that selection, then press 2
        /// arrives 0.2 s later on the detector clock (inside the 350 ms window): same target, same ray, so the workspace recognizes it.
        /// </summary>
        protected async Task DoubleTriggerAsync(Func<bool> firstPressDone, CancellationToken ct, double gapSeconds = 0.2)
        {
            // A press is ignored while the workspace is busy (previous step still refreshing): wait until it is idle and
            // retry the first press (each attempt starts a new detector window) instead of firing once and hoping.
            for (int attempt = 1; attempt <= 6; attempt++)
            {
                await TryWaitUntil(() => !ReadBoolean(_assembly, "_busy"), 10, ct);
                _clock += 5;   // far from any earlier press: this one starts a new window
                TriggerTap();
                if (await TryWaitUntil(firstPressDone, 5, ct)) break;
                if (attempt == 6) throw new InvalidOperationException("timed out after 6 attempts waiting for the first Trigger press to select its target [context="
                    + App.Context + ", levels=" + Nav.Levels.Count + ", top=" + Nav.Top?.Name + ", assembly.notice='" + _assembly.Notice + "'] " + PressDiagnostics());
            }
            _clock += gapSeconds;
            TriggerTap();
        }

        /// <summary>Enters a component of the open assembly with a double Trigger on its model, then waits for the router to push the level.</summary>
        protected async Task EnterByDoubleTriggerAsync(AssemblyOccurrence occurrence, Func<DocContext?, bool> contextOk, string contextName, CancellationToken ct)
        {
            int levels = Nav.Levels.Count;
            SnapPoses();
            var center = OccurrenceCenter(occurrence.Id);
            AimAt(center, AwayFromHead(center));
            await DoubleTriggerAsync(() => Read<AssemblyOccurrence>(_assembly, "_occurrence")?.Id == occurrence.Id
                && !ReadBoolean(_assembly, "_busy"), ct);
            await WaitDiag(() => Nav.Levels.Count == levels + 1 && contextOk(App.Context), "the router to push the level of " + occurrence.Name
                + " and open " + contextName, ct);
            await Task.Delay(300, ct);
        }

        /// <summary>Waits until the Part workspace of the entered document has its context and history loaded.</summary>
        protected Task WaitDesignReadyAsync(CancellationToken ct) => WaitDiag(() => _design.Active && !ReadBoolean(_design, "_busy")
            && Read<DesignContext>(_design, "_context") != null && Read<DesignHistory>(_design, "_history") != null
            && ActionEnabled("design.extrude"), "Progettazione to load its context", ct);

        protected Task WaitLamieraReadyAsync(CancellationToken ct) => WaitDiag(() => _lamiera.Active && _lamiera.IsPrimary
            && !ReadBoolean(_lamiera, "_busy") && Read<DesignContext>(_lamiera, "_designContext") != null
            && ActionEnabled(LamieraWorkspace.IdFlange), "Lamiera to load its context", ct);

        protected Task WaitAssemblyReadyAsync(int levels, CancellationToken ct) => WaitDiag(() => Nav.Levels.Count == levels
            && App.Context == DocContext.Assembly && _assembly.Active && !ReadBoolean(_assembly, "_busy")
            && Read<AssemblyContext>(_assembly, "_context") != null && Session.Scene?.Graph?.Kind == "assembly",
            "the assembly context at level " + levels, ct);

        /// <summary>Backs out of whatever is open (feature tab, list, keypad) with X, as a user would, until the workspace is at rest.</summary>
        protected async Task EnsureRestAsync(CancellationToken ct)
        {
            for (int i = 0; i < 6 && !_input.RestingProbe(); i++) { PressX(); await Task.Delay(150, ct); }
            SendRest();
        }

        /// <summary>
        /// Torna with X held for 1 s at rest: a SYNTHETIC press at time t and a Poll of the same frame at t + 1.05 s. The XrInput state
        /// machine measures the hold from the timestamps it receives, so no real second is spent.
        /// </summary>
        protected void HoldXForBack()
        {
            SendRest();
            Check(_input.RestingProbe != null && _input.RestingProbe(), "the workspace is at rest (no ring, keypad, group, draft or isolation): X is tap/hold");
            float t = Time.unscaledTime;
            var down = Rest(); down.X = true;
            _source.Next = down;
            _input.Poll(down, t);
            _input.Poll(down, t + XrInput.BackHoldSeconds + 0.05f);
            Check(Mathf.Approximately(_input.BackHoldProgress, 1f), "X held for 1 s reached full progress");
            SendRest();
        }

        // ------------------------------------------------------------------------------- M9-01 / M9-02 blocked / M9-05: Assieme

        protected static readonly string[] AssemblyTabs = { "componenti", "vincoli", ContextTabs.Inspect, "vista", ActionCatalog.DocumentTab };
        private static readonly string[] PartTabs = { "schizzo", "feature", "parametri", ContextTabs.Inspect, "vista", ActionCatalog.DocumentTab };
        private static readonly string[] SheetTabs = { "lamiera", "schizzo", "sviluppo", ContextTabs.Inspect, "vista", ActionCatalog.DocumentTab };

        private async Task CheckAssemblyRootAsync(CancellationToken ct)
        {
            await WaitAssemblyReadyAsync(1, ct);
            var state0 = await BaselineAsync(ct);

            // M9-01: the context follows the document; no manual choice of workspace exists any more.
            Check(!_design.Active && !_lamiera.Active && ReferenceEquals(Catalog.Active, _assembly), "on an assembly document only Assieme is open and serves the palette");
            Check(Nav.Top.DocumentId == _assemblyDocId && Nav.Top.Context == DocContext.Assembly && Nav.Parent == null, "the navigation stack holds the assembly as its root");
            Check(Nav.Breadcrumb == Session.Scene.Graph.Root.Name, "the breadcrumb is the document name (" + Nav.Breadcrumb + ")");
            var everyAction = Catalog.Tabs.SelectMany(t => Catalog.Palette(t.Id)).ToList();
            Check(everyAction.All(a => !a.Id.StartsWith("spaces.", StringComparison.Ordinal)) && Catalog.Tabs.All(t => t.Id != "spazi"),
                "no palette tab offers a manual choice of workspace");
            var document = Catalog.Palette(ActionCatalog.DocumentTab).ToDictionary(a => a.Id);
            foreach (var id in new[] { DocumentActions.IdPath, DocumentActions.IdBack, DocumentActions.IdSave, DocumentActions.IdDocuments,
                DocumentActions.IdRecenter, DocumentActions.IdCalibrate })
                Check(document.ContainsKey(id), "the Documento tab offers " + id);
            Check(!ActionEnabled(DocumentActions.IdBack) && !string.IsNullOrEmpty(Catalog.Find(DocumentActions.IdBack).DisabledReason),
                "Torna is disabled at the root with a reason (" + Catalog.Find(DocumentActions.IdBack).DisabledReason + ")");
            RunAction(DocumentActions.IdPath);
            var crumb = Catalog.Find(DocumentActions.IdCrumbPrefix + "0");
            Check(crumb != null && crumb.Label.Contains(Session.Scene.Graph.Root.Name), "the path list shows the root level as a button");
            Check(Catalog.Find(DocumentActions.IdListClose + "0")?.TryInvoke() == true, "the path list closes");
            var frameBefore = _bench.Frame;
            RunAction(DocumentActions.IdRecenter);
            Check(!ReferenceEquals(_bench.Frame, frameBefore), "Ricentra (Documento tab) computed a new bench frame");
            Pass("M9-01", "assembly document: Assieme opens by itself, no Spazi action, Documento tab with path (breadcrumb), Torna disabled at the root, "
                + "Salva, open documents and Ricentra (SYNTHETIC taps); revision " + state0.Revision);

            // M9-05: tabs of Assieme, Inspect group, single Vista.
            await CheckTabsAsync(DocContext.Assembly, AssemblyTabs, ct);
            Pass("M9-05", "Assieme: tabs " + string.Join(", ", Catalog.Tabs.Select(t => t.Id)) + ", none inert; Ispeziona group with Misura, Sezione, Visibilita, Verifica; one Vista tab");

            // M9-02: the double Trigger is refused with a reason while a CAD review is pending; nothing is activated.
            var block = await FindOccurrenceAsync("Block", ct);
            await SelectOccurrenceAsync(block, ct);
            _assembly.Session.RequireCadReview();
            Check(_assembly.RequiresCadReview, "a pending CAD review is set on Assieme (synthetic guard, no commit was made)");
            Check(!_assembly.TryEnterSelected(out var reason) && !string.IsNullOrEmpty(reason) && reason.IndexOf("Revisione CAD", StringComparison.Ordinal) >= 0,
                "the entry is refused while a CAD review is pending, with the reason: " + reason);
            Check(Nav.Levels.Count == 1 && App.Context == DocContext.Assembly, "a refused entry leaves the stack and the context where they were");
            Check(!ActionEnabled(DocumentActions.IdDocuments), "Documenti aperti is blocked during the CAD review");
            RunAction(CommitIds.Recover);
            await WaitDiag(() => !_assembly.RequiresCadReview && !ReadBoolean(_assembly, "_busy") && Read<AssemblyContext>(_assembly, "_context") != null, "the CAD review to be resolved", ct);
            await AssertUnchanged(state0, "the refused entry", ct);
            await TryBlockedByCommandAsync(state0, ct);
        }

        /// <summary>M9-02: a command in progress (Sposta on the free sheet) also blocks the entry; the HUD/notice carries the reason.</summary>
        private async Task TryBlockedByCommandAsync(DocumentState state0, CancellationToken ct)
        {
            var sheet = await FindOccurrenceAsync("Sheet", ct);
            await SelectOccurrenceAsync(sheet, ct);
            if (!ActionEnabled(AssemblyWorkspace.IdMove))
            {
                NotCovered("M9-02-command", "the fixture sheet cannot be moved in Assieme (" + Catalog.Find(AssemblyWorkspace.IdMove)?.DisabledReason
                    + "), so no command could be started to prove the entry guard 'Comando in corso'; the review guard above was exercised");
                return;
            }
            RunAction(AssemblyWorkspace.IdMove);
            Check(!_assembly.TryEnterSelected(out var reason) && !string.IsNullOrEmpty(reason), "the entry is refused while Sposta is in progress, with the reason: " + reason);
            Check(Nav.Levels.Count == 1 && App.Context == DocContext.Assembly, "a refused entry during a command leaves the stack where it was");
            RunAction(CommitIds.Cancel);
            await AssertUnchanged(state0, "the refused entry during Sposta", ct);
            Pass("M9-02", "double Trigger entry is refused with a reason during a pending CAD review (synthetic guard) and during Sposta; stack, context and revision unchanged");
        }

        protected async Task SelectOccurrenceAsync(AssemblyOccurrence occurrence, CancellationToken ct)
        {
            if (Read<AssemblyOccurrence>(_assembly, "_occurrence")?.Id == occurrence.Id && ActionEnabled(AssemblyWorkspace.IdIsolate)) return;
            await WaitDiag(() => ActionEnabled(AssemblyWorkspace.IdComponents), "the Componenti action", ct, 30);
            RunAction(AssemblyWorkspace.IdComponents);
            PickItem(_assembly, AssemblyWorkspace.IdPickPrefix, occurrence.Name, suffix: true);
            await WaitDiag(() => Read<AssemblyOccurrence>(_assembly, "_occurrence")?.Id == occurrence.Id
                && !ReadBoolean(_assembly, "_busy") && ActionEnabled(AssemblyWorkspace.IdIsolate), "the selection of " + occurrence.Name, ct, 30);
        }

        // ----------------------------------------------------------------------------------------------- M9-05 tab composition

        protected async Task CheckTabsAsync(DocContext context, string[] expected, CancellationToken ct)
        {
            Check(App.Context == context, "the app context is " + context + " (found " + App.Context + ")");
            var tabs = Catalog.Tabs.Select(t => t.Id).ToList();
            foreach (var id in expected) Check(tabs.Contains(id), context + ": tab '" + id + "' is present (" + string.Join(", ", tabs) + ")");
            Check(!tabs.Contains("esplora") && !tabs.Contains("misura") && !tabs.Contains("sezione") && !tabs.Contains("visibilita") && !tabs.Contains("verifica"),
                context + ": Misura, Sezione, Visibilita, Verifica and Esplora are not main tabs (they live in the Ispeziona group)");
            foreach (var id in tabs)
            {
                var items = Catalog.Palette(id);
                Check(items.Count > 0 && items.Count <= ActionCatalog.MaxPalette, context + ": tab '" + id + "' is never inert and holds at most " + ActionCatalog.MaxPalette + " actions");
            }
            Check(tabs.Count(t => t == "vista") == 1, context + ": exactly one Vista tab");
            var vista = Catalog.Palette("vista").Select(a => a.Id).ToList();
            Check(vista.Contains(ViewActions.IdFit) && vista.Contains(ViewActions.IdLegend), context + ": the single Vista tab offers Adatta and the legend switch");
            var ordered = tabs.Where(t => expected.Contains(t)).ToList();
            Check(ordered.SequenceEqual(expected.Where(t => tabs.Contains(t))), context + ": the main tabs keep the spec order (" + string.Join(", ", ordered) + ")");

            // Ispeziona group: Misura, Sezione (+ Visibilita, Verifica in Assieme), left stick scrolls it, X leaves it.
            var group = ContextTabs.InspectGroup(context);
            Check(group.Contains("misura") && group.Contains("sezione") && group.Contains("visibilita") == (context == DocContext.Assembly)
                && group.Contains("verifica") == (context == DocContext.Assembly), context + ": the Ispeziona group has the tabs of the spec");
            foreach (var id in group) Check(Catalog.Palette(id).Count > 0, context + ": group tab '" + id + "' holds actions");
            if (_shell.Palette.CurrentTab != ContextTabs.Inspect) _shell.Palette.ShowTab(ContextTabs.Inspect);
            RunAction(InspectWorkspace.IdGroupOpen);
            Check(_shell.Palette.InTabGroup, context + ": Ispeziona opens a tab group");
            var seen = new List<string> { _shell.Palette.CurrentTab };
            for (int i = 0; i < 10; i++)
            {
                FlickPalette(1);
                if (_shell.Palette.CurrentTab == seen[0]) break;
                seen.Add(_shell.Palette.CurrentTab);
            }
            Check(seen.SequenceEqual(group.Concat(new[] { ActionCatalog.InspectExitTab })), context + ": the left stick scrolls exactly the group and its exit tab (" + string.Join(", ", seen) + ")");
            PressX();
            Check(!_shell.Palette.InTabGroup, context + ": X leaves the Ispeziona group");
            await Task.Yield();
        }

        // ------------------------------------------------------------------- M9-02 / 03 / 04 / 05 / 06 / 09: the block (Parte)

        private async Task CheckBlockAsync(CancellationToken ct)
        {
            var block = await FindOccurrenceAsync("Block", ct);
            var assemblyScene = Session.Scene;
            var assemblyRevision = assemblyScene.Graph.Revision;
            await EnterByDoubleTriggerAsync(block, c => c == DocContext.Part, "Progettazione", ct);
            await WaitDesignReadyAsync(ct);
            RequireFixture();

            // M9-02: right context, one level pushed with the occurrence it came from.
            Check(Nav.Levels.Count == 2 && Nav.Parent.DocumentId == _assemblyDocId, "the double Trigger pushed one level above the assembly");
            Check(Nav.Top.FromOccurrenceId == block.Id && Nav.Top.OccurrencePose != null && Nav.Top.OccurrencePose.Length == 16,
                "the level remembers the occurrence and its 16-float pose in the parent");
            Check(App.Context == DocContext.Part && _design.Active && !_assembly.Active && !_lamiera.Active, "the part opened in Progettazione and Assieme closed");
            Check(Session.Scene.Graph.Kind == "part" && Session.Scene.Graph.DocumentId != _assemblyDocId && Nav.Top.DocumentId == Session.Scene.Graph.DocumentId,
                "Inventor activated the part document of the component");
            Pass("M9-02", "double Trigger on " + block.Name + " (two SYNTHETIC presses on the body) activated " + Session.Scene.Graph.Root.Name + ": level pushed, Progettazione opened");

            await CheckGhostAsync(assemblyScene, assemblyRevision, ct);          // M9-03
            await CheckTabsAsync(DocContext.Part, PartTabs, ct);                 // M9-05
            await CheckPartToolsAsync(ct);                                       // M9-05 (during modification), M9-06 live states
            await CheckFeatureEditAsync(ct);                                     // M9-09

            // M9-04: a short X at rest is only a hint; X held for 1 s goes back to the assembly; nothing is saved.
            int levels = Nav.Levels.Count;
            var stateBefore = await BaselineAsync(ct);
            await EnsureRestAsync(ct);
            Check(_input.RestingProbe(), "the part is at rest before the Torna gestures");
            float t = Time.unscaledTime;
            var down = Rest(); down.X = true;
            _source.Next = down; _input.Poll(down, t);
            _input.Poll(Rest(), t + 0.1f);
            SendRest();
            Check(HudText().Contains("Tieni X per tornare a"), "a short X at rest only shows the hint: " + HudText().Replace('\n', '|'));
            Check(Nav.Levels.Count == levels && Session.Scene.Graph.DocumentId == Nav.Top.DocumentId, "a short X did not navigate");
            await AssertUnchanged(stateBefore, "the short X", ct);
            HoldXForBack();
            await WaitAssemblyReadyAsync(1, ct);
            Check(!_ghost.IsShowing, "back at the assembly the ghost is gone");
            Check(Session.Scene.Graph.DocumentId == _assemblyDocId, "Inventor re-activated the assembly document");
            Pass("M9-04", "X short = hint 'Tieni X per tornare a ...' with no navigation; X held 1 s (SYNTHETIC timestamps) returned to the assembly; the only backend "
                + "call of Torna is the activation of the parent document (BackNavigator never saves)");
        }

        /// <summary>M9-03: the ghost of the parent assembly: present, translucent, not selectable, placed at the occurrence, labelled, moves with Adatta.</summary>
        private async Task CheckGhostAsync(LoadedScene assemblyScene, string assemblyRevision, CancellationToken ct)
        {
            Check(_ghost.IsShowing && _ghost.RendererCount > 0, "the ghost of the parent assembly is shown (" + _ghost.RendererCount + " renderers)");
            Check(_ghost.ParentDocumentId == _assemblyDocId, "the ghost shows the direct parent");
            Check(_ghost.Label == GhostContext.LabelText && _ghost.Label.Contains("prima delle modifiche"), "the ghost carries the label '" + _ghost.Label + "'");
            Check(!string.IsNullOrEmpty(_ghost.RevisionLabel) && _ghost.RevisionLabel == assemblyRevision,
                "the ghost shows the assembly revision at the entry (" + _ghost.RevisionLabel + ")");
            Check(_ghost.Material != null && _ghost.Material.GetColor("_Color").a < 1f, "the ghost material is translucent (alpha " + F(_ghost.Material.GetColor("_Color").a) + ")");
            Check(!_ghost.HasSelectableColliders && _ghost.GetComponentsInChildren<CadBody>(true).Length == 0, "the ghost has no collider and no CadBody: the ray cannot pick it");
            var renderers = _ghost.GetComponentsInChildren<MeshRenderer>(true);
            Check(renderers.All(r => r.gameObject.layer == 2 && r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off && !r.receiveShadows),
                "every ghost renderer is on the Ignore Raycast layer and casts or receives no shadow");
            Check(_ghost.transform.parent == _view.transform, "the ghost is a child of the scene root that Adatta and Ricentra move");

            // Not selectable: a ray through the middle of the ghost sheet hits only the real part, never a ghost body.
            var sheetNode = assemblyScene.Graph.PlacedParts().FirstOrDefault(p => p.Node.Name != null && p.Node.Name.IndexOf("Sheet", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(sheetNode != null, "the parent scene places the sheet occurrence");
            var ghostSheet = _ghost.GetComponentsInChildren<Transform>(true).FirstOrDefault(tr => tr.name == sheetNode.Node.Name);
            Check(ghostSheet != null, "the ghost draws the sheet occurrence '" + sheetNode.Node.Name + "'");
            var ghostBounds = renderers.Where(r => r.transform.IsChildOf(ghostSheet)).Select(r => r.bounds).ToList();
            Check(ghostBounds.Count > 0, "the ghost sheet has renderers");
            var center = ghostBounds.Aggregate((a, b) => { a.Encapsulate(b); return a; }).center;
            Physics.SyncTransforms();
            bool picked = CadRaycaster.TryPick(new Ray(center + Vector3.up, Vector3.down), 3f, out var body, out _, out _);
            Check(!picked || body.transform.IsChildOf(_view.transform) && !body.transform.IsChildOf(_ghost.transform), "a ray through the ghost sheet never picks a ghost body");

            // Posed at the occurrence: the ghost sheet sits at the sheet-minus-block offset of the assembly (distance from the part origin).
            var blockPose = Nav.Top.OccurrencePose;
            var rel = Multiply(InventorXrSo.Core.Navigation.GhostPose.Inverse(blockPose), sheetNode.MatrixGltf);
            double expectedOffset = Math.Sqrt(rel[12] * rel[12] + rel[13] * rel[13] + rel[14] * rel[14]);
            double actualOffset = _ghost.transform.InverseTransformPoint(ghostSheet.position).magnitude;
            Check(Near(actualOffset, expectedOffset, 2e-3), "the ghost sheet origin is " + F(actualOffset * 1000) + " mm from the part origin, as the assembly places it ("
                + F(expectedOffset * 1000) + " mm)");

            // Adatta (Y) moves the part and the ghost together: their relative pose is untouched.
            var before = _view.transform.worldToLocalMatrix * ghostSheet.localToWorldMatrix;
            var yDown = Rest(); yDown.Y = true; Send(yDown); SendRest();
            SnapPoses();
            var after = _view.transform.worldToLocalMatrix * ghostSheet.localToWorldMatrix;
            Check(_ghost.IsShowing && MatrixNear(before, after, 1e-4f), "Adatta (Y) keeps the ghost coherent with the part (relative pose unchanged)");
            await CaptureScreenshot("ghost-context", ct);
            Pass("M9-03", "ghost of " + _ghost.ParentDocumentId + " translucent, non-selectable (no collider, no CadBody, ignore-raycast layer, no shadows), posed at the occurrence, "
                + "labelled '" + _ghost.Label + "' rev " + _ghost.RevisionLabel + ", coherent after Adatta; screenshot ghost-context (SYNTHETIC double Trigger)");
        }

        private static float[] Multiply(float[] a, float[] b)
        {
            var c = new float[16];
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    float sum = 0;
                    for (int k = 0; k < 4; k++) sum += a[k * 4 + row] * b[col * 4 + k];
                    c[col * 4 + row] = sum;
                }
            return c;
        }

        private static bool MatrixNear(Matrix4x4 a, Matrix4x4 b, float tolerance)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > tolerance) return false;
            return true;
        }

        /// <summary>M9-05 during modification (Misura and Sezione stay active, suspend on a CAD review) and M9-06 live input states (Rest, Sketch, Armed, Keypad).</summary>
        private async Task CheckPartToolsAsync(CancellationToken ct)
        {
            var state0 = await BaselineAsync(ct);
            var dispatcher = _input.Dispatcher;
            Check(dispatcher.State == InputState.Rest, "Progettazione at rest resolves the Rest input state");
            _liveStates.Add(InputState.Rest);

            // Sketch open: the constraints tab appears by itself and the state is SketchOpen.
            var plane = Read<DesignContext>(_design, "_context").Planes[0];
            RunAction("design.sketch.create");
            PickItem(_design, "design.pick.", plane.Name);
            await WaitDiag(() => _design.SketchOpen, "the sketch to open", ct, 20);
            Check(dispatcher.State == InputState.SketchOpen, "an open sketch resolves the SketchOpen input state (found " + dispatcher.State + ")");
            _liveStates.Add(InputState.SketchOpen);
            Check(Catalog.Tabs.Any(t => t.Id == "vincoli"), "the sketch constraints tab appears with a sketch open");
            Check(ActionEnabled(InspectWorkspace.IdMeasure) && ActionEnabled(InspectWorkspace.IdSection), "Misura and Sezione stay active while a sketch is open");
            RunAction(CommitIds.Cancel);
            Check(!_design.SketchOpen && Catalog.Tabs.All(t => t.Id != "vincoli"), "cancelling the sketch removes the constraints tab");

            // Extrusion in progress: the options tab appears, the chip is armed, the keypad takes over.
            RunAction("design.extrude");
            PickItem(_design, "design.pick.", SketchName);
            Check(Catalog.Tabs.Any(t => t.Id == "opzioni"), "the feature options tab appears with a feature in progress");
            Check(dispatcher.State == InputState.ArmedOrHandle, "an armed chip resolves the ArmedOrHandle input state (found " + dispatcher.State + ")");
            _liveStates.Add(InputState.ArmedOrHandle);
            Check(ActionEnabled(InspectWorkspace.IdMeasure) && ActionEnabled(InspectWorkspace.IdSection),
                "Misura and Sezione stay active while a feature is being drafted");
            RunAction("design.dimension");
            Check(_shell.Palette.KeypadVisible && dispatcher.State == InputState.Keypad, "the keypad resolves the Keypad input state (found " + dispatcher.State + ")");
            _liveStates.Add(InputState.Keypad);
            PressX();
            Check(!_shell.Palette.KeypadVisible, "X closes the keypad");
            RunAction(CommitIds.Cancel);
            Check(dispatcher.State == InputState.Rest, "after Annulla the state is Rest again");

            // A pending CAD review suspends Misura and Sezione (they pause, they do not break), and resuming is automatic.
            var session = Read<DesignSession>(_design, "_session");
            session.RequireCadReview();
            Check(!ActionEnabled(InspectWorkspace.IdMeasure) && !ActionEnabled(InspectWorkspace.IdSection),
                "Misura and Sezione are suspended during a CAD review (" + Catalog.Find(InspectWorkspace.IdMeasure).DisabledReason + ")");
            RunAction(CommitIds.Recover);
            await WaitDiag(() => !_design.RequiresCadReview && !ReadBoolean(_design, "_busy") && Read<DesignContext>(_design, "_context") != null
                && Read<DesignHistory>(_design, "_history") != null && ActionEnabled("design.extrude"), "the part review to be resolved", ct);
            Check(ActionEnabled(InspectWorkspace.IdMeasure) && ActionEnabled(InspectWorkspace.IdSection), "Misura and Sezione resume after the review");
            await AssertUnchanged(state0, "tab, sketch, chip and review checks", ct);
            Pass("M9-05", "Parte: the constraints tab appears with a sketch, the options tab with a feature, Misura and Sezione stay active during modification "
                + "and pause only on a CAD review (SYNTHETIC guard, no commit)");
        }

        // ------------------------------------------------------------------------------------------- M9-09: face -> feature edit

        protected async Task CheckFeatureEditAsync(CancellationToken ct)
        {
            var context = Read<DesignContext>(_design, "_context");
            var state0 = await BaselineAsync(ct);
            var face = context.Faces.FirstOrDefault(f => Near(f.Normal.Z, 1, 1e-3) && Near(f.PointMm.Z, 10, 0.01));
            if (face == null) { NotCovered("M9-09", "the fixture block exposes no +Z planar face at z = 10 mm to double-Trigger on"); return; }
            SnapPoses();
            var model = _view.transform;
            var away = model.TransformDirection(CadCoordinates.ToLocal(face.Normal)).normalized;
            var faceWorld = model.TransformPoint(CadCoordinates.ToLocal(face.PointMm));
            AimAt(faceWorld, away);
            _clock += 5;
            TriggerTap();   // press 1: selects the face (as today)
            if (!await TryWaitUntil(() => Read<string>(_design, "_face") != null, 10, ct))
            {
                NotCovered("M9-09", "the synthetic ray did not select the planar face of the block (no first press result): the double Trigger on a face could not be exercised");
                return;
            }
            _clock += 0.2;
            TriggerTap();   // press 2 inside the window: opens the feature edit
            if (!await TryWaitUntil(() => _design.FeatureEdit != null, 30, ct))
            {
                NotCovered("M9-09", "the double Trigger on the face did not open the feature edit within 30 s (design notice: '" + _design.Notice + "'): the chip, preview and Apply steps were not run");
                return;
            }
            var edit = _design.FeatureEdit;
            Check(edit.Info.Supported && edit.BlockReason == null, "the extrusion of the block is a supported, editable feature (" + edit.Info.FeatureType + ", " + edit.Name + ")");
            var chip = edit.Chips.FirstOrDefault(c => c.Role == "distance");
            Check(chip != null && chip.Editable && Near(chip.Value, ExtrudeStartMm, 0.01), "the feature exposes an editable distance of " + F(ExtrudeStartMm) + " mm"
                + (chip == null ? "" : " (found " + F(chip.Value) + ")"));
            Check(Catalog.Tabs.Any(t => t.Id == _design.FeatureTabId) && _shell.Palette.CurrentTab == _design.FeatureTabId,
                "the tab 'Feature: " + edit.Name + "' is the active tab");
            Check(ContextTabs.Main(DocContext.Part, _design.TabState).Contains(_design.FeatureTabId), "ContextTabs lists the feature tab for the open feature edit");
            await AssertUnchanged(state0, "reading the feature", ct);

            // Chip -> keypad -> local draft -> ONE set_parameter in the preview batch.
            int index = edit.Chips.ToList().IndexOf(chip);
            RunAction(FeaturePrefix + index);
            TypeValue(ExtrudeEditedMm);
            var ops = edit.BuildOperations();
            Check(ops.Count == 1 && ops.ToString().Contains("set_parameter") && ops.ToString().Contains(chip.Name),
                "the draft is exactly one set_parameter on " + chip.Name + " (" + ops.Count + " operation)");
            var session = Read<DesignSession>(_design, "_session");
            if (!await TryWaitUntil(() => session.Status == DesignStatus.Previewing || session.Status == DesignStatus.PreviewReady || session.Status == DesignStatus.Error, 5, ct)
                && ActionEnabled(CommitIds.Preview)) RunAction(CommitIds.Preview);
            await WaitDiag(() => session.Status == DesignStatus.PreviewReady || session.Status == DesignStatus.Error, "the feature edit preview", ct, 60);
            Check(session.Status == DesignStatus.PreviewReady && session.CanApply && _design.CommitBar.Phase == CommitBarPhase.Ready,
                "the preview is ready on the commit bar (status " + session.Status + ", error '" + session.Error + "')");
            await AssertUnchanged(state0, "the feature edit preview", ct);

            // Apply from the bar, then read the parameter back from Inventor.
            RequireFixture();
            RunAction(CommitIds.Apply);
            await WaitDiag(() => ReadValue<int>(_design, "_pendingMutations") == 0 && session.Status == DesignStatus.Empty && !ReadBoolean(_design, "_busy")
                && Read<DesignContext>(_design, "_context")?.State.Revision != state0.Revision && Read<DesignHistory>(_design, "_history") != null, "the Apply to settle", ct, 90);
            Check(!session.CommitOutcomeUnknown, "the commit outcome is known");
            var applied = await BaselineAsync(ct);
            Check(applied.DocumentId == state0.DocumentId && applied.Revision != state0.Revision, "Apply changed the Inventor revision (" + state0.Revision + " -> " + applied.Revision + ")");
            double actual = await ReadParameterMmAsync(applied, chip.Name, face.Id, ct);
            Check(Near(actual, ExtrudeEditedMm, 0.01), "Inventor now reports " + chip.Name + " = " + F(actual) + " mm (wanted " + F(ExtrudeEditedMm) + ")");
            await WaitDiag(() => Nav.Top.Dirty && Nav.Breadcrumb.Contains("●"), "the unsaved marker on the breadcrumb", ct, 10);
            Pass("M9-04", "after the Apply the breadcrumb marks the part with the unsaved marker (" + Nav.Breadcrumb + "): modified, unsaved");
            Pass("M9-09", "double Trigger on the +Z face (two SYNTHETIC presses) -> 'Feature: " + edit.Name + "' tab, chip " + chip.Name + " " + F(ExtrudeStartMm)
                + " -> " + F(ExtrudeEditedMm) + " mm -> one set_parameter -> preview -> Apply from the bar; Inventor read back " + F(actual) + " mm (revision "
                + state0.Revision + " -> " + applied.Revision + ")");

            // Cleanup: XR Undo restores the previous value; the fixture is closed unsaved by --restore-quest anyway.
            RunAction("design.history.undo");
            await WaitDiag(() => ReadValue<int>(_design, "_pendingMutations") == 0 && session.Status == DesignStatus.Empty && !ReadBoolean(_design, "_busy")
                && Read<DesignContext>(_design, "_context")?.State.Revision != applied.Revision && Read<DesignHistory>(_design, "_history") != null, "the Undo to settle", ct, 90);
            var restored = await BaselineAsync(ct);
            double back = await ReadParameterMmAsync(restored, chip.Name, face.Id, ct);
            if (Near(back, ExtrudeStartMm, 0.01)) Pass("M9-09", "XR Undo restored " + chip.Name + " = " + F(back) + " mm (cleanup)");
            else NotCovered("M9-09-restore", "after XR Undo Inventor reports " + chip.Name + " = " + F(back) + " mm instead of " + F(ExtrudeStartMm)
                + " mm: run " + RestoreHint + " before rerunning");

            if (!ReportKnownFeatureGaps) return;
            NotCovered("M9-09-handle", "the extrusion-distance handle on the geometry is not wired for the feature edit (only chips): the spec asks for chip and handle (DesignFeatureEdit.cs)");
            NotCovered("M9-09-highlight", "face_feature returns no list of faces, so only the picked face is highlighted instead of every face of the feature");
            NotCovered("M9-09-expression-suite", "an expression-driven parameter (read-only chip, never overwritten) and a suppressed feature need geometry the M6 fixture does not have: covered by FeatureEditTests and FaceFeatureTests only");
        }

        protected async Task<double> ReadParameterMmAsync(DocumentState state, string name, string faceId, CancellationToken ct)
        {
            var context = await _backend.GetDesignContextAsync(state, ct);
            var parameter = context.Parameters.OfType<JObject>().FirstOrDefault(p => (string)p["name"] == name);
            double? value = (double?)parameter?["value_mm"];
            if (value.HasValue) return value.Value;
            var info = await ((IFaceFeatureBackend)_inspection).GetFaceFeatureAsync(state, faceId, ct);
            var read = info.Parameters.FirstOrDefault(p => p.Name == name);
            Check(read != null, "Inventor reports the parameter " + name + " (design context and face_feature both lack it)");
            return read.Value;
        }

        // ------------------------------------------------------------------------------- M9-02 / 04 / 05 / 09: the sheet (Lamiera)

        private async Task CheckSheetAsync(CancellationToken ct)
        {
            var sheet = await FindOccurrenceAsync("Sheet", ct);
            var assemblyScene = Session.Scene;
            await EnterByDoubleTriggerAsync(sheet, c => c == DocContext.Part || c == DocContext.SheetMetal, "Lamiera", ct);
            try { await WaitDiag(() => App.Context == DocContext.SheetMetal && _lamiera.Active, "sheet-metal detection to open Lamiera", ct, 60); }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(ex.Message + " [lamiera: IsPrimary=" + _lamiera.IsPrimary + " busy=" + ReadBoolean(_lamiera, "_busy")
                    + " visible=" + ReadBoolean(_lamiera, "_visible") + " online=" + ReadBoolean(_lamiera, "_online") + " kind=" + Read<string>(_lamiera, "_kind")
                    + " sheetContext=" + DescribeSheetContext() + " stateDoc=" + Read<DocumentState>(_lamiera, "_state")?.DocumentId
                    + " activeDoc=" + Nav.Top?.DocumentId + " sceneDoc=" + Session.Scene?.Graph.DocumentId + " sceneKind=" + Session.Scene?.Graph.Kind + "]");
            }
            await WaitLamieraReadyAsync(ct);
            RequireFixture();
            Check(Nav.Levels.Count == 2 && Nav.Top.FromOccurrenceId == sheet.Id && Nav.Parent.DocumentId == _assemblyDocId, "the sheet level sits above the assembly");
            Check(!_design.Active && !_assembly.Active && ReferenceEquals(Catalog.Active, _lamiera), "only Lamiera is open and serves the palette");
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == _assemblyDocId, "the ghost of the assembly is shown behind the sheet");
            Pass("M9-02", "double Trigger on " + sheet.Name + " (SYNTHETIC) opened the sheet-metal part in Lamiera (" + Session.Scene.Graph.Root.Name + ")");
            await CheckTabsAsync(DocContext.SheetMetal, SheetTabs, ct);
            Pass("M9-05", "Lamiera: tabs " + string.Join(", ", Catalog.Tabs.Select(t => t.Id)) + ", none inert; Ispeziona group with Misura and Sezione only; one Vista tab");
            await CheckUnsupportedFeatureAsync(ct);

            // M9-04: Torna from the Documento tab.
            Check(ActionEnabled(DocumentActions.IdBack), "Torna is enabled one level down");
            var stateBefore = await BaselineAsync(ct);
            RunAction(DocumentActions.IdBack);
            await WaitAssemblyReadyAsync(1, ct);
            Check(!_ghost.IsShowing, "back at the assembly the ghost is gone");
            Pass("M9-04", "Torna from the Documento tab returned from Lamiera to the assembly, with no save (the part revision " + stateBefore.Revision + " was not touched)");
        }

        /// <summary>M9-09: a feature type outside the table (sheet-metal base face) shows name and type with "Modifica dal desktop" and writes nothing.</summary>
        private async Task CheckUnsupportedFeatureAsync(CancellationToken ct)
        {
            var context = Read<DesignContext>(_lamiera, "_designContext");
            var face = context.Faces.FirstOrDefault(f => Near(f.Normal.Z, 1, 1e-3));
            if (face == null) { NotCovered("M9-09-unsupported", "the sheet exposes no +Z planar face to double-Trigger on"); return; }
            var state0 = await BaselineAsync(ct);
            SnapPoses();
            var model = _view.transform;
            var away = model.TransformDirection(CadCoordinates.ToLocal(face.Normal)).normalized;
            AimAt(model.TransformPoint(CadCoordinates.ToLocal(face.PointMm)), away);
            _clock += 5; TriggerTap();
            await TryWaitUntil(() => false, 1.0, ct);
            _clock += 0.2; TriggerTap();
            if (!await TryWaitUntil(() => _lamiera.FeatureEdit != null, 25, ct))
            {
                NotCovered("M9-09-unsupported", "the double Trigger on the sheet face did not open a feature tab within 25 s (lamiera notice: '" + _lamiera.Notice
                    + "'); the unsupported-type path was not observed");
                return;
            }
            var edit = _lamiera.FeatureEdit;
            if (!edit.Info.Supported)
            {
                Check(edit.BlockReason != null && edit.Chips.Count == 0 && Catalog.Palette(_lamiera.FeatureTabId).Any(a => a.Label == "Modifica dal desktop"),
                    "an unsupported feature type shows its name and type with 'Modifica dal desktop' and no chip");
                await AssertUnchanged(state0, "reading an unsupported feature", ct);
                Pass("M9-09", "sheet face feature '" + edit.Name + "' (" + edit.Info.FeatureType + ") is outside the table: name, type and 'Modifica dal desktop', no chip, no CAD write");
            }
            else
            {
                await AssertUnchanged(state0, "reading the sheet feature", ct);
                Record("diag the sheet face feature '" + edit.Name + "' (" + edit.Info.FeatureType + ") is a supported type with " + edit.Chips.Count + " chip(s): read only here, nothing was written");
            }
        }

        // ------------------------------------------------------------------------------------------------- M9-10: voice by context

        private sealed class InjectedRecognizer : ISpeechRecognizer
        {
            public string Text;
            public Task<string> RecognizeAsync(short[] pcm16, CancellationToken cancellationToken) => Task.FromResult(Text);
        }

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
            await WaitDiag(() => talk.State == PushToTalkState.Result || talk.State == PushToTalkState.Error, "the injected recognition result", ct, 30);
            Check(talk.State == PushToTalkState.Result, "injected recognition result was delivered: " + talk.ErrorMessage);
            Check(!talk.MicrophoneOpen, "no microphone capture is open");
            bridge.Pump();
        }

        private async Task CheckVoiceNavigationAsync(CancellationToken ct)
        {
            await WaitAssemblyReadyAsync(1, ct);
            var voiceTarget = Read<WorkspaceVoiceTarget>(App, "_voiceTarget");
            Check(voiceTarget != null && voiceTarget.InSession && voiceTarget.AcceptsVoice && voiceTarget.Catalog != null, "the app voice target is active in the session and holds the catalog");
            var recognizer = new InjectedRecognizer();
            var bridge = new VoiceCommandBridge(voiceTarget, recognizer, armingThreshold: TimeSpan.Zero);
            Record("Speech is injected text pushed through the real PushToTalkController (SYNTHETIC: no microphone, no audio)");
            var state0 = await BaselineAsync(ct);
            var sheet = await FindOccurrenceAsync("Sheet", ct);
            var blockName = (await FindOccurrenceAsync("Block", ct)).Name;
            try
            {
                // Space commands change nothing and explain the new way.
                foreach (var phrase in new[] { "vai in progettazione", "lamiera", "ispeziona" })
                {
                    await SpeakAsync(bridge, recognizer, phrase, ct);
                    Check(bridge.Outcome == VoiceOutcomeKind.Rejected && bridge.OutcomeText.IndexOf("spazi non si scelgono", StringComparison.OrdinalIgnoreCase) >= 0,
                        "the space command '" + phrase + "' is rejected with the explanation (" + bridge.Outcome + ": " + bridge.OutcomeText + ")");
                    Check(App.Context == DocContext.Assembly && Nav.Levels.Count == 1 && _assembly.Active && !_design.Active && !_lamiera.Active,
                        "'" + phrase + "' did not change the context");
                }

                // The vocabulary is the one of the context: a Progettazione label does not resolve in Assieme; Apply is never spoken.
                var extrudeLabel = _design.Actions.First(a => a.Id == "design.extrude").Label;
                Check(Catalog.ResolveVoice(extrudeLabel).Kind == VoiceMatchKind.NotFound, "'" + extrudeLabel + "' (Progettazione) does not resolve in Assieme");
                await SpeakAsync(bridge, recognizer, "applica", ct);
                Check(bridge.Outcome != VoiceOutcomeKind.Executed && _assembly.CommitBar.Phase == CommitBarPhase.Empty, "spoken Applica executes nothing (" + bridge.Outcome + ")");
                await AssertUnchanged(state0, "the space commands and the spoken Applica", ct);

                // "apri <componente>" enters like the double Trigger; inside, it is not available; "torna" goes up.
                await SpeakAsync(bridge, recognizer, "apri " + sheet.Name, ct);
                Check(bridge.Outcome == VoiceOutcomeKind.Executed, "'apri " + sheet.Name + "' runs (" + bridge.Outcome + ": " + bridge.OutcomeText + ")");
                await WaitDiag(() => Nav.Levels.Count == 2 && App.Context == DocContext.SheetMetal && _lamiera.Active, "the voice entry into the sheet", ct, 60);
                await WaitLamieraReadyAsync(ct);
                await SpeakAsync(bridge, recognizer, "apri " + blockName, ct);
                Check(bridge.Outcome == VoiceOutcomeKind.NotExecuted && Nav.Levels.Count == 2, "'apri' inside a part is not available and changes nothing (" + bridge.Outcome + ": " + bridge.OutcomeText + ")");
                await SpeakAsync(bridge, recognizer, "torna", ct);
                Check(bridge.Outcome == VoiceOutcomeKind.Executed, "'torna' runs (" + bridge.Outcome + ")");
                await WaitAssemblyReadyAsync(1, ct);
            }
            finally { bridge.Dispose(); }
            Pass("M9-10", "voice by context: 'vai in progettazione', 'lamiera' and 'ispeziona' rejected with the explanation and nothing changed; a Progettazione label does not resolve in "
                + "Assieme; spoken Applica executes nothing; 'apri <componente>' entered the sheet like the double Trigger, was unavailable inside the part, and 'torna' went back (injected speech)");
        }

        // ------------------------------------------------------------------------ M9-01: the desktop changes the document; Documenti aperti

        private async Task CheckDesktopChangeAndJumpAsync(CancellationToken ct)
        {
            await WaitAssemblyReadyAsync(1, ct);
            var documents = await _inspection.ListOpenAsync(ct);
            var fixtureDocs = documents.Where(d => d.Name != null && d.Name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToList();
            var sheetDoc = fixtureDocs.FirstOrDefault(d => d.Name.IndexOf("_Sheet", StringComparison.Ordinal) >= 0);
            var blockDoc = fixtureDocs.FirstOrDefault(d => d.Name.IndexOf("_Block", StringComparison.Ordinal) >= 0);
            var assemblyDoc = fixtureDocs.FirstOrDefault(d => d.Id == _assemblyDocId);
            Check(sheetDoc != null && blockDoc != null && assemblyDoc != null, "the fixture documents are open: " + string.Join(", ", documents.Select(d => d.Name + "/" + d.Kind)));
            RequireFixture();

            // The PC activates another document: the stack restarts on it and the HUD says so.
            await _inspection.ActivateOpenAsync(sheetDoc.Id, ct);
            bool sawNotice = false;
            await WaitDiag(() =>
            {
                if (HudText().Contains("Documento cambiato dal PC")) sawNotice = true;   // the HUD flash is short-lived: sample it while waiting
                return Nav.Levels.Count == 1 && Nav.Top.DocumentId == sheetDoc.Id && (_lamiera.Active || _design.Active);
            }, "the router to follow the desktop change to the sheet", ct, 60);
            Check(sawNotice || HudText().Contains("Documento cambiato dal PC"), "the HUD announces the change from the PC");
            await WaitDiag(() => App.Context == DocContext.SheetMetal && _lamiera.Active, "Lamiera for the sheet activated from the PC", ct, 60);
            Check(Nav.Parent == null && !_ghost.IsShowing, "after a change from the PC the stack has no parent and no ghost");
            await _inspection.ActivateOpenAsync(_assemblyDocId, ct);
            await WaitAssemblyReadyAsync(1, ct);
            Check(Nav.Top.DocumentId == _assemblyDocId, "the router followed the PC back to the assembly");

            // The Documento tab jumps to a listed document: the stack restarts there (no push, no PC notice).
            RunAction(DocumentActions.IdDocuments);
            var entry = Enumerable.Range(0, 10).SelectMany(page => Catalog.Palette(DocumentActions.OpenTabPrefix + page))
                .FirstOrDefault(a => a.Id.StartsWith(DocumentActions.IdOpenPrefix, StringComparison.Ordinal) && a.Label != null && a.Label.IndexOf("_Block", StringComparison.Ordinal) >= 0);
            Check(entry != null, "the open documents list offers the block");
            Check(entry.TryInvoke(), "the list entry runs");
            await WaitDiag(() => Nav.Levels.Count == 1 && Nav.Top.DocumentId == blockDoc.Id && _design.Active, "the jump to the block", ct, 60);
            Check(Nav.Parent == null && !_ghost.IsShowing, "a jump restarts the stack (no level pushed, no ghost)");
            await _inspection.ActivateOpenAsync(_assemblyDocId, ct);
            await WaitAssemblyReadyAsync(1, ct);
            Pass("M9-01", "the context follows the document from the desktop too: activating the sheet and the assembly from the PC reset the stack on them with the HUD notice; "
                + "a jump from 'Documenti aperti' restarted the stack on the block without a pushed level (SYNTHETIC taps, real Inventor activations)");
        }

        // ------------------------------------------------------------------------------------------ M9-02: sub-assembly two levels

        private async Task CheckSubassemblyAsync(CancellationToken ct)
        {
            await WaitAssemblyReadyAsync(1, ct);
            var context = Read<AssemblyContext>(_assembly, "_context");
            var sub = context.Occurrences.FirstOrDefault(o => o.Kind == "assembly");
            if (sub == null)
            {
                NotCovered("M9-02-subassembly-nested", "the dedicated fixture XR_M6_Quest_Acceptance has no sub-assembly occurrence (" + string.Join(", ", context.Occurrences.Select(o => o.Name + "/" + o.Kind))
                    + "): the descent assembly > sub-assembly > part and its double Torna are exercised by the separate nested runner (M9NestedQuestAcceptance, fixture m9n, run-m9-nested-acceptance.ps1)");
                return;
            }
            await EnterByDoubleTriggerAsync(sub, c => c == DocContext.Assembly, "the sub-assembly", ct);
            await WaitDiag(() => App.Context == DocContext.Assembly && _assembly.Active && !ReadBoolean(_assembly, "_busy") && Read<AssemblyContext>(_assembly, "_context") != null
                && Nav.Top.DocumentId == Session.Scene.Graph.DocumentId, "the sub-assembly context", ct, 60);
            Check(Nav.Levels.Count == 2 && Nav.Top.Context == DocContext.Assembly && _ghost.IsShowing, "a sub-assembly is a new Assieme level with the parent as ghost");
            var inner = Read<AssemblyContext>(_assembly, "_context").Occurrences.FirstOrDefault(o => o.Kind != "assembly");
            if (inner == null)
            {
                NotCovered("M9-02-subassembly", "the sub-assembly holds no part occurrence: the second level was not exercised");
                RunAction(DocumentActions.IdBack);
                await WaitAssemblyReadyAsync(1, ct);
                return;
            }
            await EnterByDoubleTriggerAsync(inner, c => c == DocContext.Part || c == DocContext.SheetMetal, "the part of the sub-assembly", ct);
            Check(Nav.Levels.Count == 3, "assembly > sub-assembly > part: three levels (" + Nav.Breadcrumb + ")");
            RunAction(DocumentActions.IdBack);
            await WaitDiag(() => Nav.Levels.Count == 2 && App.Context == DocContext.Assembly, "the first Torna", ct, 60);
            RunAction(DocumentActions.IdBack);
            await WaitAssemblyReadyAsync(1, ct);
            Pass("M9-02", "sub-assembly two levels: " + sub.Name + " pushed an Assieme level, its part a third one; two Torna returned to the root (SYNTHETIC double Trigger)");
        }

        // ------------------------------------------------------------------------------- M9-06: every row of the InputMap

        private async Task CheckInputMapAsync(CancellationToken ct)
        {
            await WaitAssemblyReadyAsync(1, ct);
            var dispatcher = _input.Dispatcher;
            var originalProbe = dispatcher.StateProbe;
            var originalResting = _input.RestingProbe;
            Check(originalResting != null && originalResting(), "the assembly root is at rest before the table sweep");
            var seen = new List<(InputAction action, int arg)>();
            Action<InputAction, int> collect = (a, i) => seen.Add((a, i));
            dispatcher.Invoked += collect;
            var rotation0 = _view.transform.rotation;
            int bound = 0, silent = 0;
            try
            {
                foreach (InputState state in Enum.GetValues(typeof(InputState)))
                {
                    var forced = state;
                    dispatcher.StateProbe = () => forced;
                    _input.RestingProbe = state == InputState.Rest ? originalResting : (Func<bool>)(() => false);
                    foreach (Key key in Enum.GetValues(typeof(Key)))
                    {
                        var binding = InputMap.Lookup(state, key);
                        seen.Clear();
                        if (key == Key.B)
                        {
                            Check(binding != null && binding.Action == InputAction.Speak, state + "/B: the table maps the push-to-talk button to Parla");
                            bound++;
                            continue;
                        }
                        ExerciseKey(state, key, binding);
                        if (binding == null)
                        {
                            Check(seen.Count == 0, state + "/" + key + ": a key with no binding does nothing (raised " + string.Join(",", seen.Select(s => s.action)) + ")");
                            silent++;
                        }
                        else
                        {
                            Check(seen.Any(s => s.action == binding.Action), state + "/" + key + ": raises " + binding.Action + " (raised " + string.Join(",", seen.Select(s => s.action)) + ")");
                            bound++;
                            if (binding.Secondary != InputAction.None)
                            {
                                seen.Clear();
                                Check(dispatcher.InvokeSecondary(key) && seen.Any(s => s.action == binding.Secondary), state + "/" + key + ": the time-threshold action " + binding.Secondary + " is routed");
                            }
                            else
                            {
                                seen.Clear();
                                Check(!dispatcher.InvokeSecondary(key) && seen.Count == 0, state + "/" + key + ": no time-threshold action is routed");
                            }
                        }
                    }
                }
            }
            finally
            {
                dispatcher.Invoked -= collect;
                dispatcher.StateProbe = originalProbe;
                _input.RestingProbe = originalResting;
                SendRest();
            }
            Check(Quaternion.Angle(rotation0, _view.transform.rotation) < 0.5f, "the +15 / -15 degree view rotations of the sweep cancel out");
            RunAction(ViewActions.IdFit);   // zoom flicks of the sweep scale the model: Adatta puts it back
            SnapPoses();
            var live = string.Join(", ", _liveStates.OrderBy(s => (int)s));
            Pass("M9-06", "InputMap swept with SYNTHETIC frames through the real XrInput and InputDispatcher: " + bound + " bound key/state rows raised their action (incl. X short = Suggest, "
                + "X held = BackHold, double Trigger = DoubleSelect, stick right at rest = RotateView 15 degrees), " + silent + " unbound rows stayed silent; "
                + "states also observed live: " + live + " (ComponentSelected was exercised by the legend and entry steps)");
        }

        /// <summary>One synthetic gesture for <paramref name="key"/> (events only: the state comes from the forced probe).</summary>
        private void ExerciseKey(InputState state, Key key, InventorXrSo.Core.Input.InputBinding binding)
        {
            switch (key)
            {
                case Key.Trigger:
                    AimAtSky(); _clock += 5; TriggerTap(); break;
                case Key.Grip:
                    { var f = Rest(); f.PenGrip = true; Send(f); SendRest(); break; }
                case Key.StickRightH:
                    {
                        var before = _view.transform.rotation;
                        FlickPen(1, 0);
                        if (binding != null && binding.Action == InputAction.RotateView)
                            Check(Mathf.Abs(Quaternion.Angle(before, _view.transform.rotation) - 15f) < 0.5f, state + ": stick right rotates the view by 15 degrees");
                        FlickPen(-1, 0);
                        break;
                    }
                case Key.StickRightV:
                    FlickPen(0, 1); FlickPen(0, -1); break;
                case Key.A:
                    { var f = Rest(); f.A = true; Send(f); SendRest(); break; }
                case Key.TriggerLeft:
                    { var f = Rest(); f.PaletteTrigger = true; Send(f); SendRest(); break; }
                case Key.StickLeftH:
                    FlickPalette(1); FlickPalette(-1); break;
                case Key.StickLeftV:
                    { var f = Rest(); f.PaletteStick = new Vector2(0, 1); Send(f); SendRest(); f.PaletteStick = new Vector2(0, -1); Send(f); SendRest(); break; }
                case Key.X:
                    {
                        float t = Time.unscaledTime;
                        var down = Rest(); down.X = true;
                        _source.Next = down; _input.Poll(down, t); _input.Poll(Rest(), t + 0.1f);   // short press
                        SendRest();
                        if (state == InputState.Rest)
                        {
                            t = Time.unscaledTime;
                            _source.Next = down; _input.Poll(down, t); _input.Poll(down, t + XrInput.BackHoldSeconds + 0.05f);   // held 1 s (root: only the hint)
                            SendRest();
                        }
                        break;
                    }
                case Key.Y:
                    { var f = Rest(); f.Y = true; Send(f); SendRest(); break; }
                case Key.TwoGrips:
                    { var f = Rest(); f.PenGrip = true; f.PaletteGrip = true; Send(f); SendRest(); break; }
            }
        }

        // ---------------------------------------------------------------------------------------------------- M9-07: the legend

        private async Task CheckLegendAsync(CancellationToken ct)
        {
            await WaitAssemblyReadyAsync(1, ct);
            var dispatcher = _input.Dispatcher;
            var originalProbe = dispatcher.StateProbe;
            var originalResting = _input.RestingProbe;
            var legendAction = Catalog.Find(ViewActions.IdLegend);
            Check(legendAction != null, "the Vista tab offers the legend switch");
            _legendWasOn = legendAction.IsOn;
            if (!legendAction.IsOn) RunAction(ViewActions.IdLegend);
            Check(_legend.Enabled && _legend.Shown, "the legend is on and shown in the session");
            int stateChecks = 0;
            try
            {
                foreach (InputState state in Enum.GetValues(typeof(InputState)))
                {
                    var forced = state;
                    dispatcher.StateProbe = () => forced;
                    var started = Time.realtimeSinceStartup;
                    while (_legend.State != state && Time.realtimeSinceStartup - started < 3f) await Task.Delay(10, ct);
                    float elapsed = Time.realtimeSinceStartup - started;
                    Check(_legend.State == state, "the legend follows the state " + state);
                    Check(elapsed <= 0.15f, "the legend applied " + state + " within 150 ms (" + F(elapsed * 1000) + " ms)");
                    var expected = InputMap.Active(state).ToList();
                    Check(_legend.Labels.Count == expected.Count && expected.All(e => _legend.Labels.Any(l => l.Key == e.key)),
                        state + ": the labels are exactly the active keys of InputMap (" + string.Join(", ", _legend.Labels.Select(l => l.Key)) + ")");
                    foreach (var label in _legend.Labels)
                    {
                        Check(label.Text.Length > 0 && label.Text.Length <= 12, state + "/" + label.Key + ": the label '" + label.Text + "' has at most 12 characters");
                        Check(label.Visible && label.Opacity > 0f && label.Opacity <= 1f, state + "/" + label.Key + ": the label is visible with an opacity (" + F(label.Opacity) + ")");
                    }
                    foreach (Key key in Enum.GetValues(typeof(Key)))
                        if (InputMap.Lookup(state, key) == null)
                            Check(_legend.Labels.All(l => l.Key != key), state + "/" + key + ": a key with no binding has no label");
                    stateChecks++;
                    if (state == InputState.Rest) await CaptureScreenshot("legend-rest", ct);
                    if (state == InputState.ArmedOrHandle) await CaptureScreenshot("legend-armed", ct);
                }

                // Progress rings of the time-threshold actions.
                dispatcher.StateProbe = () => InputState.Rest;
                await Task.Delay(100, ct);
                var xLabel = _legend.Labels.First(l => l.Key == Key.X);
                Check(xLabel.Secondary == InputAction.BackHold, "the X label at rest announces the held action");
                float t = Time.unscaledTime;
                var down = Rest(); down.X = true;
                _source.Next = down; _input.Poll(down, t); _input.Poll(down, t + 0.5f);
                _legend.Tick();
                Check(xLabel.Progress > 0.3f && xLabel.Progress < 0.8f, "the X label shows the held progress (" + F(xLabel.Progress) + ")");
                SendRest();
                _legend.Tick();
                Check(xLabel.Progress == 0f, "the X progress ring clears on release");

                var block = await FindOccurrenceAsync("Block", ct);
                SnapPoses();
                var center = OccurrenceCenter(block.Id);
                AimAt(center, AwayFromHead(center));
                _clock += 5; TriggerTap();
                await WaitDiag(() => Read<AssemblyOccurrence>(_assembly, "_occurrence")?.Id == block.Id && !ReadBoolean(_assembly, "_busy"), "the first press to select the block", ct, 30);
                _clock += 0.1;
                _legend.Tick();
                var triggerLabel = _legend.Labels.First(l => l.Key == Key.Trigger);
                Check(triggerLabel.Secondary == InputAction.DoubleSelect && triggerLabel.Progress > 0f && triggerLabel.Progress < 1f,
                    "the Trigger label shows the double Trigger window after a first press (" + F(triggerLabel.Progress) + ")");
                _clock += 5;   // window expired: no entry happens
                dispatcher.StateProbe = () => InputState.ComponentSelected;
                await Task.Delay(100, ct);
                await CaptureScreenshot("legend-component", ct);

                // Switch off from the Vista tab; the preference follows and comes back on.
                RunAction(ViewActions.IdLegend);
                Check(!_legend.Enabled && _legend.Labels.All(l => !l.Visible) && PlayerPrefs.GetInt(ControllerLegend.PrefKey, 1) == 0,
                    "the Vista switch hides every label and stores the preference");
                RunAction(ViewActions.IdLegend);
                Check(_legend.Enabled && _legend.Labels.All(l => l.Visible), "switching it on again shows the labels");
            }
            finally
            {
                dispatcher.StateProbe = originalProbe;
                _input.RestingProbe = originalResting;
                SendRest();
            }
            Pass("M9-07", "legend: for each of the " + stateChecks + " input states the labels are exactly InputMap.Active (<= 12 characters, only active keys), applied within 150 ms; progress rings on X held "
                + "and on the Trigger after a first press; the Vista switch hides and restores it; screenshots legend-rest, legend-armed, legend-component (SYNTHETIC states)");
        }

        // ---------------------------------------------------------------------------------------------------------------- cleanup

        protected async Task CleanupAsync()
        {
            try
            {
                EndSynthetic();
                if (_legendWasOn == false && Catalog.Find(ViewActions.IdLegend)?.IsOn == true) Catalog.Find(ViewActions.IdLegend).TryInvoke();
                if (_assemblyDocId != null && _inspection != null && Session?.Scene?.Graph?.DocumentId != _assemblyDocId)
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40)))
                    {
                        await _inspection.ActivateOpenAsync(_assemblyDocId, timeout.Token);
                        await TryWaitUntil(() => Session.Scene?.Graph?.DocumentId == _assemblyDocId && Session.Status == SessionStatus.Online, 30, timeout.Token);
                    }
                }
                Record("Clean end: synthetic input removed, probes restored, the assembly document is the active one again (restore the fixture before rerunning: " + RestoreHint + ")");
            }
            catch (Exception ex)
            {
                Record("Cleanup could not complete: " + ex.GetType().Name + ": " + ex.Message + " (activate the assembly document before " + RestoreHint + ")");
            }
        }

        private void OnDestroy() { EndSynthetic(); }
    }
}
#endif
