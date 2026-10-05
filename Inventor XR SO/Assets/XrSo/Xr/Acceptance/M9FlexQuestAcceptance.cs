#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Opt-in, fixture-scoped M9 runner for FLEXIBLE sub-assemblies on a LARGE scene (fixture m9f, guard XR_M9F_Quest_Acceptance). It
    /// reproduces at small scale the user's robot: the top assembly Robot holds PartL1 (loose), AsmFixed (grounded, not flexible; a normal
    /// AsmInner and PartF) and AsmFlex (ungrounded, ComponentOccurrence.Flexible = True, 5 m away at z = -5000 mm; a FLEXIBLE AsmFlexInner
    /// with PartX and PartY, plus PartG). Entering a flexible sub-assembly activates its DEFINITION document, so it must work exactly like
    /// entering a normal one; only Sposta, Vincola and Giunto (batch edits on the occurrence) stay unavailable, with a reason.
    ///
    /// Scenario: select AsmFlex by a ray on a body 5 m from the origin and log the selection text the user reads on the HUD (Italian,
    /// explicit, no raw "flexible"); double Trigger enters AsmFlex (Robot > AsmFlex) while Sposta/Vincola/Giunto/Isola are disabled with a
    /// reason; double Trigger enters AsmFlexInner (Robot > AsmFlex > AsmFlexInner), then PartX (Part, Progettazione); the same face-feature
    /// chip edit and XR Undo as m9n; Torna three times (X held, Documento tab, X held) back to Robot checking stack, ghost (direct parent
    /// only), no save and the visual revision; then AsmFixed > AsmInner (entered with the ring's Apri) and back, with the visual revision
    /// strictly unchanged.
    ///
    /// Same rules as <see cref="M9QuestAcceptance"/> (helpers reused through <see cref="M9NestedQuestAcceptance"/>): input is SYNTHETIC,
    /// Inventor answers are real, PASS COMPLETE only without NOT COVERED sub-cases. Written without a device: compiled and
    /// contract-tested, never run.
    /// </summary>
    internal sealed class M9FlexQuestAcceptance : M9NestedQuestAcceptance
    {
        internal new static readonly string[] ReflectedMembers =
        {
            "AppController._inSession",
            "AppController.EnterSession",
            "AssemblyWorkspace._busy",
        };

        protected override string Milestone => "m9f";
        protected override string FixtureMilestone => "m9f";
        protected override int TimeoutSeconds => 960;
        protected override string RestoreHint => "--restore-quest m9f";
        protected override string CompletionNote =>
            "SYNTHETIC input and the dedicated flexible-assembly fixture (m9f) only; the physical trial (M9-12), a sub-assembly whose definition window is not open in Inventor "
            + "and an assembly constraint on a flexible sub-assembly (the fixture has none) are separate evidence and remain open";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M9FlexQuestAcceptance>("xr_m9f_acceptance");

        // ------------------------------------------------------------------------------------------------------------------ run

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            RequireFixture();
            Check(fixture.Graph.Kind == "assembly", "the flex run starts on Robot (found " + fixture.Graph.Kind + ")");
            _assemblyDocId = fixture.Graph.DocumentId;
            Record("Dedicated flexible-assembly fixture m9f (guard " + FixturePrefix + "): " + fixture.Graph.Root.Name);
            Record("Navigation is driven like the app does; a double Trigger is two SYNTHETIC presses on a real body (detector clock driven by the runner)");

            if (!ReadBoolean(App, "_inSession")) Call(App, "EnterSession", EnvironmentMode.StudioVr);
            BindApp();
            BeginSynthetic();
            try
            {
                await CheckFlexScenarioAsync(ct);
                await CheckFixedBranchAsync(ct);
            }
            finally
            {
                await CleanupAsync();
            }
            NotCovered("M9F-constraint-probe", "the fixture has no assembly constraint on AsmFlex (a flush/mate on a flexible sub-assembly cannot be created and verified without a live Inventor): "
                + "a constrained flexible sub-assembly needs a live probe");
            NotCovered("M9-12-physical", "sitting trial with the controllers on the real robot assembly needs a person wearing the Quest");
        }

        // ---------------------------------------------------------------------------------------------------------------- helpers

        /// <summary>The level (or document) name is the one of <paramref name="token"/> and not of a longer name that merely contains it (AsmFlex vs AsmFlexInner).</summary>
        private static bool NameIs(string name, string token, string longer = null)
            => name != null && name.IndexOf(token, StringComparison.Ordinal) >= 0 && (longer == null || name.IndexOf(longer, StringComparison.Ordinal) < 0);

        private string Stack() => string.Join(" > ", Nav.Levels.Select(l => l.Name));

        private string TopLevelOf(string leafId) => Session.Scene.Graph.TopLevelOccurrenceId(leafId);

        /// <summary>
        /// Aims the synthetic ray at a drawn leaf of the direct occurrence <paramref name="directId"/> and checks that the ray really hits a body that
        /// resolves to it BEFORE pressing (a body 5 m from the origin must be reachable after the Workbench placed and scaled the model). Tries a few
        /// distances and records which one worked; throws with the press diagnostics when none does.
        /// </summary>
        private void AimAtDirect(Vector3 target, string directId, string what)
        {
            var attempts = new List<string>();
            foreach (var distance in new[] { 0.3f, 0.15f, 0.6f, 1.0f })
            {
                AimAt(target, AwayFromHead(target), distance);
                Physics.SyncTransforms();
                var ray = new Ray(_ray.Origin.position, _ray.Origin.forward);
                bool hit = CadRaycaster.TryPick(ray, 20, out var body, out _, out _);
                string leaf = hit ? body.Instance?.OccurrenceId : null;
                string top = leaf == null ? null : TopLevelOf(leaf);
                if (hit && top == directId)
                {
                    if (distance != 0.3f) Record("Ray at " + F(distance) + " m reached " + what + " (the default 0.3 m did not: " + string.Join("; ", attempts) + ")");
                    return;
                }
                attempts.Add(F(distance) + " m: " + (hit ? "hit " + leaf + " -> " + top : "no hit"));
            }
            throw new InvalidOperationException("no synthetic ray reaches " + what + " (target " + target + ", head " + _head.position + ", view scale " + _view.transform.lossyScale
                + "; attempts: " + string.Join("; ", attempts) + ") " + PressDiagnostics() + " " + StateDump());
        }

        /// <summary>First press selects the direct occurrence (ray on one of its drawn leaves); the second one, 0.2 s later on the detector clock, enters it.</summary>
        private async Task EnterWithRayAsync(AssemblyOccurrence occurrence, Func<DocContext?, bool> contextOk, string contextName, CancellationToken ct)
        {
            int levels = Nav.Levels.Count;
            SnapPoses();
            AimAtDirect(LeafCenter(occurrence), occurrence.Id, occurrence.Name);
            await DoubleTriggerAsync(() => SelectedOccurrence?.Id == occurrence.Id && !ReadBoolean(_assembly, "_busy"), ct);
            await WaitDiag(() => Nav.Levels.Count == levels + 1 && contextOk(App.Context),
                "the double Trigger on " + occurrence.Name + " to push a level and open " + contextName + " (selected occurrence id " + occurrence.Id + ") " + StateDump(), ct, 60);
            await Task.Delay(300, ct);
        }

        /// <summary>First press only: selects the direct occurrence and waits for the selection (no entry).</summary>
        private async Task SelectWithRayAsync(AssemblyOccurrence occurrence, CancellationToken ct)
        {
            SnapPoses();
            AimAtDirect(LeafCenter(occurrence), occurrence.Id, occurrence.Name);
            for (int attempt = 1; attempt <= 6; attempt++)
            {
                await TryWaitUntil(() => !ReadBoolean(_assembly, "_busy"), 10, ct);
                _clock += 5;
                TriggerTap();
                if (await TryWaitUntil(() => SelectedOccurrence?.Id == occurrence.Id && !ReadBoolean(_assembly, "_busy"), 5, ct)) return;
            }
            throw new InvalidOperationException("the first Trigger press did not select " + occurrence.Name + " " + PressDiagnostics() + " " + StateDump());
        }

        /// <summary>The wording the user reads when a FLEXIBLE sub-assembly is selected: explicit, Italian, how to enter; never the raw English reason.</summary>
        private void CheckFlexibleSummary(string summary, string name)
        {
            Check(summary.Contains("Sottoassieme flessibile: Sposta e Vincola non sono disponibili."), name + ": the summary says the sub-assembly is flexible and Sposta/Vincola are unavailable ('" + summary.Replace('\n', '|') + "')");
            Check(summary.Contains("Doppio Trigger (o Apri) per entrare e modificarne i componenti"), name + ": the summary says how to enter ('" + summary.Replace('\n', '|') + "')");
            Check(summary.Contains("tutte le istanze"), name + ": the summary says the changes affect every instance");
            Check(!summary.Contains("non modificabile: flexible") && !summary.Contains("flexible") && !summary.Contains("Attivare la definizione"),
                name + ": no raw English 'flexible' and no old wording ('" + summary.Replace('\n', '|') + "')");
        }

        private void CheckFlexibleActions(string name)
        {
            foreach (var id in new[] { AssemblyWorkspace.IdMove, AssemblyWorkspace.IdConstrain, AssemblyWorkspace.IdJoint, AssemblyWorkspace.IdIsolate })
            {
                var action = Catalog.Find(id);
                Check(action != null && !action.Enabled && !string.IsNullOrEmpty(action.DisabledReason), name + ": action '" + id + "' is disabled with a reason ("
                    + (action == null ? "missing" : action.Enabled ? "ENABLED" : "'" + action.DisabledReason + "'") + ")");
                Check(!action.DisabledReason.Contains("flexible"), name + ": the reason of '" + id + "' is Italian ('" + action.DisabledReason + "')");
            }
            Check(Catalog.Find(AssemblyWorkspace.IdMove).DisabledReason.Contains("flessibile") && Catalog.Find(AssemblyWorkspace.IdConstrain).DisabledReason.Contains("flessibile"),
                name + ": Sposta and Vincola say the sub-assembly is flexible");
            Check(ActionEnabled(AssemblyWorkspace.IdOpen) && ActionEnabled(AssemblyWorkspace.IdActivate), name + ": Apri and Attiva questo assieme stay enabled (entering does not depend on the occurrence being flexible)");
        }

        // -------------------------------------------------------------------------------------------------------------- flex branch

        private async Task CheckFlexScenarioAsync(CancellationToken ct)
        {
            await WaitAssemblyAtAsync(1, _assemblyDocId, "Robot", ct);
            var rootScene = Session.Scene;
            var rootName = rootScene.Graph.Root.Name;
            var rootState = await BaselineAsync(ct);
            Check(Nav.Top.Context == DocContext.Assembly && Nav.Parent == null && !Nav.Top.Dirty && Nav.Breadcrumb == rootName && !_ghost.IsShowing,
                "the stack holds Robot alone, unmarked, without a ghost " + StateDump());

            // ---- preconditions: the structure of the fixture as the bridge reports it
            var l1 = await FindOccurrenceAsync("PartL1", ct);
            var fixedAsm = await FindOccurrenceAsync("AsmFixed", ct);
            var flex = await FindOccurrenceAsync("AsmFlex", ct);
            Record("Robot context: " + string.Join("; ", AssemblyContextNow.Occurrences.Select(o => o.Name + " [" + o.Kind + ", editable=" + o.Editable + ", flexible=" + o.Flexible
                + ", grounded=" + o.Grounded + ", reason=" + (o.UnavailableReason ?? "-") + ", dof=" + (o.TotalDof?.ToString() ?? "?") + "]")));
            Check(l1.Kind == "part" && fixedAsm.Kind == "assembly" && flex.Kind == "assembly", "PartL1 is a part, AsmFixed and AsmFlex are sub-assembly occurrences (" + l1.Kind + "/" + fixedAsm.Kind + "/" + flex.Kind + ")");
            Check(NameIs(flex.Name, "AsmFlex", "AsmFlexInner") && NameIs(fixedAsm.Name, "AsmFixed"), "the found occurrences are AsmFlex and AsmFixed (" + flex.Name + ", " + fixedAsm.Name + ")");
            Check(flex.Flexible && !flex.Editable && flex.UnavailableReason == "flexible" && !flex.Grounded,
                "AsmFlex is reported by Inventor as flexible, ungrounded, not editable, reason 'flexible' (flexible=" + flex.Flexible + ", editable=" + flex.Editable + ", grounded=" + flex.Grounded + ", reason=" + flex.UnavailableReason + ")");
            Check(!fixedAsm.Flexible && fixedAsm.Editable && fixedAsm.Grounded && string.IsNullOrEmpty(fixedAsm.UnavailableReason), "AsmFixed is a grounded, editable, non flexible sub-assembly");
            Check(!string.IsNullOrEmpty(flex.DefinitionId) && !string.IsNullOrEmpty(fixedAsm.DefinitionId) && flex.DefinitionId != fixedAsm.DefinitionId, "both sub-assemblies carry their own definition document id");
            var flexNode = DirectNode(flex.Id); var fixedNode = DirectNode(fixedAsm.Id);
            Check(flexNode != null && fixedNode != null && flexNode.DefinitionKind == "assembly" && fixedNode.DefinitionKind == "assembly", "the scene graph has AsmFlex and AsmFixed as assembly nodes");
            var flexLeaves = Leaves(flexNode).ToList(); var fixedLeaves = Leaves(fixedNode).ToList();
            Check(flexLeaves.Count == 3 && fixedLeaves.Count == 2 && rootScene.Graph.PlacedParts().Count() == 6, "the scene of Robot draws six leaf parts (3 under AsmFlex, 2 under AsmFixed, PartL1): "
                + string.Join(", ", rootScene.Graph.PlacedParts().Select(p => p.Node.Name)));

            // ---- large scene: the two branches are 5 m apart in the model; the Workbench places and scales the model, rays must still reach both
            SnapPoses();
            var fixedCenter = LeafCenter(fixedAsm); var flexCenter = LeafCenter(flex);
            var scale = _view.transform.lossyScale;
            float world = Vector3.Distance(fixedCenter, flexCenter);
            Record("Large scene: view scale " + scale.x.ToString("0.#####") + ", AsmFixed leaf " + fixedCenter + ", AsmFlex leaf " + flexCenter + " (" + world.ToString("0.###") + " m apart in the scene = "
                + (scale.x > 0 ? (world / scale.x * 1000f).ToString("0") : "?") + " mm in the model), head " + _head.position);
            Check(!float.IsNaN(world) && !float.IsInfinity(world) && world > 1e-4f, "the leaf centers of the two branches are finite and distinct in the scene");
            Check(Mathf.Abs(world / scale.x * 1000f - 5000f) < 150f, "the scene keeps the 5 m separation of the model (" + (world / scale.x * 1000f).ToString("0") + " mm between the two leaf centers; the Workbench scales the whole model uniformly)");

            // ---- select AsmFlex with a ray: the text the user reads
            await SelectWithRayAsync(flex, ct);
            var summary = _assembly.OccurrenceSummary();
            var notice = _assembly.Notice ?? "";
            await TryWaitUntil(() => HudText().Contains("flessibile"), 5, ct);
            var hud = HudText();
            Record("HUD text shown after selecting AsmFlex (workspace notice): '" + notice.Replace('\n', '|') + "'");
            Record("OccurrenceSummary of AsmFlex: '" + summary.Replace('\n', '|') + "'");
            Record("Full HUD canvas text: '" + hud.Replace('\n', '|') + "'");
            CheckFlexibleSummary(summary, flex.Name);
            Check(notice.Length == 0 || notice.Contains("flessibile") || notice.Contains("Doppio Trigger"), "the workspace notice after the selection is the summary or its successor ('" + notice.Replace('\n', '|') + "')");
            if (!hud.Contains("flessibile") || !hud.Contains("Doppio Trigger") || hud.Contains("flexible"))
                NotCovered("M9F-hud-text", "the HUD canvas did not show the new wording of the summary (flessibile / Doppio Trigger, no raw 'flexible'): '" + hud.Replace('\n', '|') + "'");
            CheckFlexibleActions("AsmFlex");
            await AssertUnchanged(rootState, "selecting AsmFlex", ct);
            Pass("M9F-text", "selecting the flexible AsmFlex shows an explicit Italian text (flessibile, Doppio Trigger o Apri per entrare, tutte le istanze) and no raw 'flexible'; Sposta, Vincola, Giunto and Isola are disabled with a reason; Apri stays enabled; revision unchanged");
            Pass("M9F-large-scene", "a synthetic ray on a body of AsmFlex, " + (world / scale.x * 1000f).ToString("0") + " mm from AsmFixed in the model, selected the direct occurrence " + flex.Id);

            // ---- M9-02: double Trigger on the flexible sub-assembly -> new Assieme level (like a normal one)
            await EnterWithRayAsync(flex, c => c == DocContext.Assembly, "the flexible sub-assembly " + flex.Name, ct);
            await WaitAssemblyAtAsync(2, flex.DefinitionId, "the flexible sub-assembly AsmFlex", ct);
            RequireFixture();
            Check(Nav.Parent.DocumentId == _assemblyDocId && Nav.Top.FromOccurrenceId == flex.Id && Nav.Top.OccurrencePose != null && Nav.Top.OccurrencePose.Length == 16,
                "the level remembers Robot as parent, the occurrence and its 16-float pose " + StateDump());
            Check(NameIs(Nav.Top.Name, "AsmFlex", "AsmFlexInner") && Nav.Levels[0].Name == rootName, "the path reads Robot > AsmFlex (" + Nav.Breadcrumb + ")");
            Check(NameIs(Session.Scene.Graph.Root.Name, "AsmFlex", "AsmFlexInner") && Session.Scene.Graph.PlacedParts().Count() == 3, "Inventor activated AsmFlex: its scene draws its three parts");
            Check(_assembly.Active && !_design.Active && !_lamiera.Active && ReferenceEquals(Catalog.Active, _assembly), "the flexible sub-assembly keeps the Assieme workspace open and serving the palette");
            var flexInner = await FindOccurrenceAsync("AsmFlexInner", ct);
            var partG = await FindOccurrenceAsync("PartG", ct);
            Check(flexInner.Kind == "assembly" && flexInner.Flexible && !flexInner.Editable && partG.Kind == "part", "AsmFlex lists the FLEXIBLE sub-assembly AsmFlexInner and PartG");
            Check(!Nav.Levels.Any(l => l.Dirty), "no unsaved marker after the entry (" + Nav.Breadcrumb + ")");
            Check(ActionEnabled(DocumentActions.IdBack), "Torna is enabled one level down");
            var flexState = await BaselineAsync(ct);
            Check(flexState.DocumentId == flex.DefinitionId, "Inventor's active document is the definition of AsmFlex");
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == _assemblyDocId && _ghost.RevisionLabel == rootState.Revision, "the ghost shows Robot as it was at the entry " + StateDump());
            var ghost1 = GhostNames();
            Check(fixedLeaves.All(l => ghost1.Contains(l.Name)) && !flexLeaves.Any(l => ghost1.Contains(l.Name)),
                "the ghost draws AsmFixed's and PartL1's parts and not the parts of the entered AsmFlex (" + string.Join(", ", ghost1) + ")");
            await CheckTabsAsync(DocContext.Assembly, AssemblyTabs, ct);
            Record("HUD after entering AsmFlex: '" + _assembly.Notice.Replace('\n', '|') + "'");
            Pass("M9-02", "double Trigger on a body of the flexible " + flex.Name + " (two SYNTHETIC presses) pushed an Assieme level for " + Session.Scene.Graph.Root.Name + "; path " + Nav.Breadcrumb + "; context Assembly");
            Pass("M9-03", "flexible sub-assembly level: ghost of the direct parent Robot (rev " + _ghost.RevisionLabel + "), without the entered sub-assembly's parts");

            // ---- M9-02: the flexible AsmFlexInner inside the flexible AsmFlex
            await SelectWithRayAsync(flexInner, ct);
            var innerSummary = _assembly.OccurrenceSummary();
            Record("OccurrenceSummary of AsmFlexInner: '" + innerSummary.Replace('\n', '|') + "'");
            CheckFlexibleSummary(innerSummary, flexInner.Name);
            CheckFlexibleActions("AsmFlexInner");
            await AssertUnchanged(flexState, "selecting AsmFlexInner", ct);
            await EnterWithRayAsync(flexInner, c => c == DocContext.Assembly, "the flexible sub-assembly " + flexInner.Name, ct);
            await WaitAssemblyAtAsync(3, flexInner.DefinitionId, "the flexible sub-assembly AsmFlexInner", ct);
            RequireFixture();
            Check(Nav.Levels.Count == 3 && Nav.Levels[2].FromOccurrenceId == flexInner.Id && Nav.Levels[1].FromOccurrenceId == flex.Id && NameIs(Nav.Levels[2].Name, "AsmFlexInner"),
                "the stack reads Robot > AsmFlex > AsmFlexInner with the occurrences they came from (" + Nav.Breadcrumb + ")");
            Check(Session.Scene.Graph.PlacedParts().Count() == 2 && AssemblyContextNow.Occurrences.Any(o => o.Name.Contains("PartX")) && AssemblyContextNow.Occurrences.Any(o => o.Name.Contains("PartY")),
                "AsmFlexInner draws and lists PartX and PartY");
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == flex.DefinitionId, "the ghost shows only the direct parent AsmFlex, not Robot (" + _ghost.ParentDocumentId + ")");
            var ghost2 = GhostNames();
            Check(ghost2.Any(n => n.Contains("PartG")) && !ghost2.Any(n => n.Contains("PartX")) && !ghost2.Any(n => n.Contains("PartY")) && !ghost2.Any(n => n.Contains("PartL1")) && !ghost2.Any(n => n.Contains("PartF")),
                "the ghost of AsmFlex draws PartG only (" + string.Join(", ", ghost2) + ")");
            var innerState = await BaselineAsync(ct);
            Check(innerState.DocumentId == flexInner.DefinitionId && !Nav.Levels.Any(l => l.Dirty), "Inventor's active document is the definition of AsmFlexInner, no unsaved marker");
            Pass("M9-02", "double Trigger on the flexible AsmFlexInner inside the flexible AsmFlex: stack " + Nav.Breadcrumb + ", context Assembly");
            Pass("M9-03", "level 3: the ghost is the direct parent AsmFlex only (" + string.Join(", ", ghost2) + ")");

            // ---- M9-02: PartX
            var partX = await FindOccurrenceAsync("PartX", ct);
            await EnterByDoubleTriggerAsync(partX, c => c == DocContext.Part, "Progettazione", ct);
            await WaitDesignReadyAsync(ct);
            RequireFixture();
            var partEntry = await BaselineAsync(ct);
            Check(Nav.Levels.Count == 4 && Nav.Levels[3].FromOccurrenceId == partX.Id && NameIs(Nav.Levels[3].Name, "PartX"), "the stack reads Robot > AsmFlex > AsmFlexInner > PartX (" + Nav.Breadcrumb + ")");
            Check(Nav.Levels[0].Context == DocContext.Assembly && Nav.Levels[1].Context == DocContext.Assembly && Nav.Levels[2].Context == DocContext.Assembly && Nav.Levels[3].Context == DocContext.Part,
                "level contexts are Assembly, Assembly, Assembly, Part");
            Check(App.Context == DocContext.Part && _design.Active && !_assembly.Active && !_lamiera.Active, "the part opened in Progettazione and Assieme closed");
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == flexInner.DefinitionId, "the ghost shows only the direct parent AsmFlexInner (" + _ghost.ParentDocumentId + ")");
            var ghost3 = GhostNames();
            Check(ghost3.Any(n => n.Contains("PartY")) && !ghost3.Any(n => n.Contains("PartX")) && !ghost3.Any(n => n.Contains("PartG")), "the ghost of AsmFlexInner draws PartY, not PartX and not PartG (" + string.Join(", ", ghost3) + ")");
            Pass("M9-02", "double Trigger on PartX inside two flexible sub-assemblies: stack " + Nav.Breadcrumb + ", context Part, Progettazione");
            Pass("M9-03", "part level: the ghost is the direct parent AsmFlexInner only (" + string.Join(", ", ghost3) + ")");

            // ---- M9-09 / M9-04: the same face-feature edit as m9n (XR Undo cleanup), unsaved marker
            await CheckFeatureEditAsync(ct);
            var partState = await BaselineAsync(ct);
            bool partExpected = partState.VisualRevision != partEntry.VisualRevision;
            await WaitDiag(() => Nav.Levels.Count == 4 && Nav.Top.Dirty == partExpected,
                "the unsaved marker of PartX to follow its visual revision (" + partEntry.VisualRevision + " -> " + partState.VisualRevision + ") " + StateDump(), ct, 20);

            // ---- M9-04: Torna 1 (X held): back to AsmFlexInner
            await EnsureRestAsync(ct);
            HoldXForBack();
            await WaitAssemblyAtAsync(3, flexInner.DefinitionId, "the first Torna", ct);
            Check(Nav.Levels.Count == 3 && NameIs(Nav.Top.Name, "AsmFlexInner") && Nav.Levels.All(l => !l.Name.Contains("PartX")), "the part level is gone (" + Stack() + ")");
            Check(Session.Scene.Graph.PlacedParts().Count() == 2 && AssemblyContextNow.Occurrences.Any(o => o.Name.Contains("PartX")), "the scene of AsmFlexInner lists PartX and PartY again");
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == flex.DefinitionId, "back in AsmFlexInner the ghost is the direct parent AsmFlex again " + StateDump());
            var innerAfter = await BaselineAsync(ct);
            await WaitDiag(() => Nav.Top.Dirty == (innerAfter.VisualRevision != innerState.VisualRevision),
                "the unsaved marker of AsmFlexInner to follow its visual revision (" + innerState.VisualRevision + " -> " + innerAfter.VisualRevision + ") " + StateDump(), ct, 20);
            Record("AsmFlexInner visual revision " + innerState.VisualRevision + " -> " + innerAfter.VisualRevision + " (revision " + innerState.Revision + " -> " + innerAfter.Revision + "); marker " + (Nav.Top.Dirty ? "on" : "off"));
            Pass("M9-04", "Torna (X held 1 s, SYNTHETIC timestamps) from PartX returned to AsmFlexInner (" + Nav.Breadcrumb + "); nothing saved; the unsaved marker follows the visual revision (" + (Nav.Top.Dirty ? "on" : "off") + ")");

            // ---- M9-04: Torna 2 (Documento tab): back to AsmFlex
            RunAction(DocumentActions.IdBack);
            await WaitAssemblyAtAsync(2, flex.DefinitionId, "the second Torna", ct);
            Check(Nav.Levels.Count == 2 && NameIs(Nav.Top.Name, "AsmFlex", "AsmFlexInner") && _ghost.IsShowing && _ghost.ParentDocumentId == _assemblyDocId, "back in AsmFlex with the ghost of Robot (" + Stack() + ")");
            Check(Session.Scene.Graph.PlacedParts().Count() == 3 && AssemblyContextNow.Occurrences.Any(o => o.Id == flexInner.Id && o.Kind == "assembly" && o.Flexible),
                "the scene of AsmFlex draws its three parts and the context lists the flexible AsmFlexInner again");
            var flexAfter = await BaselineAsync(ct);
            await WaitDiag(() => Nav.Top.Dirty == (flexAfter.VisualRevision != flexState.VisualRevision),
                "the unsaved marker of AsmFlex to follow its visual revision (" + flexState.VisualRevision + " -> " + flexAfter.VisualRevision + ") " + StateDump(), ct, 20);
            Record("AsmFlex visual revision " + flexState.VisualRevision + " -> " + flexAfter.VisualRevision + " (revision " + flexState.Revision + " -> " + flexAfter.Revision + "); marker " + (Nav.Top.Dirty ? "on" : "off"));
            Pass("M9-04", "second Torna (Documento tab) returned to AsmFlex (" + Nav.Breadcrumb + "); ghost of Robot; marker follows the visual revision (" + (Nav.Top.Dirty ? "on" : "off") + ")");

            // ---- M9-04: Torna 3 (X held): back to Robot, both branches listed again
            await EnsureRestAsync(ct);
            HoldXForBack();
            await WaitAssemblyAtAsync(1, _assemblyDocId, "the third Torna", ct);
            Check(Nav.Parent == null && Nav.Breadcrumb.StartsWith(rootName, StringComparison.Ordinal) && !_ghost.IsShowing, "back at the root: one level, no ghost (" + Nav.Breadcrumb + ")");
            Check(Session.Scene.Graph.PlacedParts().Count() == 6 && Session.Scene.Graph.Root.Children.Count(n => n.DefinitionKind == "assembly") == 2,
                "the scene of Robot draws its six parts under two sub-assemblies again");
            Check(AssemblyContextNow.Occurrences.Any(o => o.Id == flex.Id && o.Kind == "assembly" && o.Flexible && !o.Editable)
                && AssemblyContextNow.Occurrences.Any(o => o.Id == fixedAsm.Id && o.Kind == "assembly" && !o.Flexible && o.Editable) && AssemblyContextNow.Occurrences.Any(o => o.Id == l1.Id),
                "the Assieme context of Robot lists the flexible AsmFlex, the fixed AsmFixed and PartL1 again");
            var rootAfter = await BaselineAsync(ct);
            Check(rootAfter.DocumentId == _assemblyDocId, "Inventor's active document is Robot again");
            await WaitDiag(() => Nav.Top.Dirty == (rootAfter.VisualRevision != rootState.VisualRevision),
                "the unsaved marker of Robot to follow its visual revision (" + rootState.VisualRevision + " -> " + rootAfter.VisualRevision + ") " + StateDump(), ct, 20);
            Record("Robot visual revision " + rootState.VisualRevision + " -> " + rootAfter.VisualRevision + " (revision " + rootState.Revision + " -> " + rootAfter.Revision + "); marker " + (Nav.Top.Dirty ? "on" : "off"));
            Pass("M9-04", "third Torna (X held) returned to Robot (" + Nav.Breadcrumb + "): flexible and fixed branches listed again, no ghost, nothing saved; marker follows the visual revision (" + (Nav.Top.Dirty ? "on" : "off") + ")");
        }

        // -------------------------------------------------------------------------------------------------------------- fixed branch

        private async Task CheckFixedBranchAsync(CancellationToken ct)
        {
            await WaitAssemblyAtAsync(1, _assemblyDocId, "Robot", ct);
            var fixedAsm = await FindOccurrenceAsync("AsmFixed", ct);
            var state3 = await BaselineAsync(ct);

            await SelectWithRayAsync(fixedAsm, ct);
            var fixedSummary = _assembly.OccurrenceSummary();
            Record("OccurrenceSummary of AsmFixed (not flexible): '" + fixedSummary.Replace('\n', '|') + "'");
            Check(fixedSummary.Contains("Doppio Trigger (o Apri) per entrare e modificarne i componenti; le modifiche riguardano tutte le istanze.") && !fixedSummary.Contains("flessibile") && !fixedSummary.Contains("flexible"),
                "a normal sub-assembly says how to enter and is not called flexible ('" + fixedSummary.Replace('\n', '|') + "')");
            await EnterWithRayAsync(fixedAsm, c => c == DocContext.Assembly, "the fixed sub-assembly " + fixedAsm.Name, ct);
            await WaitAssemblyAtAsync(2, fixedAsm.DefinitionId, "the fixed sub-assembly AsmFixed", ct);
            Check(NameIs(Nav.Top.Name, "AsmFixed") && Nav.Top.FromOccurrenceId == fixedAsm.Id && Nav.Parent.DocumentId == _assemblyDocId, "the level is AsmFixed, entered from its occurrence (" + Nav.Breadcrumb + ")");
            var inner = await FindOccurrenceAsync("AsmInner", ct);
            var partF = await FindOccurrenceAsync("PartF", ct);
            Check(inner.Kind == "assembly" && !inner.Flexible && inner.Editable && partF.Kind == "part", "AsmFixed lists the normal sub-assembly AsmInner and PartF");
            var ghost = GhostNames();
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == _assemblyDocId && ghost.Any(n => n.Contains("PartL1")) && ghost.Any(n => n.Contains("PartX")) && !ghost.Any(n => n.Contains("PartF")) && !ghost.Any(n => n.Contains("PartI")),
                "the ghost of Robot shows PartL1 and the flexible branch, not the entered AsmFixed's parts (" + string.Join(", ", ghost) + ")");

            // AsmInner: select with the ray, enter with the ring's Apri (the same entry as the double Trigger)
            await SelectWithRayAsync(inner, ct);
            Check(ActionEnabled(AssemblyWorkspace.IdConstrain) && ActionEnabled(AssemblyWorkspace.IdOpen), "a normal sub-assembly keeps Vincola and Apri enabled");
            RunAction(AssemblyWorkspace.IdOpen);
            await WaitDiag(() => Nav.Levels.Count == 3 && App.Context == DocContext.Assembly, "the ring's Apri on AsmInner to push a level " + StateDump(), ct, 60);
            await WaitAssemblyAtAsync(3, inner.DefinitionId, "the normal sub-assembly AsmInner", ct);
            Check(NameIs(Nav.Top.Name, "AsmInner") && Nav.Top.FromOccurrenceId == inner.Id && Session.Scene.Graph.PlacedParts().Count() == 1, "the stack reads Robot > AsmFixed > AsmInner; its scene draws PartI (" + Nav.Breadcrumb + ")");
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == fixedAsm.DefinitionId, "the ghost is the direct parent AsmFixed (" + _ghost.ParentDocumentId + ")");
            RunAction(DocumentActions.IdBack);
            await WaitAssemblyAtAsync(2, fixedAsm.DefinitionId, "the Torna from AsmInner", ct);
            await EnsureRestAsync(ct);
            HoldXForBack();
            await WaitAssemblyAtAsync(1, _assemblyDocId, "the Torna from AsmFixed", ct);
            Check(!_ghost.IsShowing && Session.Scene.Graph.PlacedParts().Count() == 6, "back at Robot: no ghost, six parts");
            await AssertUnchanged(state3, "entering AsmFixed > AsmInner and leaving", ct, activates: true);
            Pass("M9-02", "Robot > AsmFixed > AsmInner (double Trigger, then the ring's Apri) and back with Torna: levels, context Assembly, ghost of the direct parent, scene of Robot complete again, visual revision "
                + state3.VisualRevision + " unchanged (SYNTHETIC)");
        }
    }
}
#endif
