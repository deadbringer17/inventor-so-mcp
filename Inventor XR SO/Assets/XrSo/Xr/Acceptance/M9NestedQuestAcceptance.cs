#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Session;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Opt-in, fixture-scoped M9 runner for NESTED assemblies (fixture m9n, guard XR_M9N_Quest_Acceptance): Assieme3 holds Assieme1
    /// (PartA, PartC) and Assieme2 (PartB). The scenario is the one of the user: from Assieme3 a double Trigger on a body of Assieme1
    /// must enter the sub-assembly (a new Assieme level), a double Trigger on PartA enters the part, the extrusion is modified and
    /// restored with the existing feature-edit path, then Torna goes back twice (X held, then the Documento tab) to Assieme3, whose
    /// scene lists both sub-assemblies again; Assieme2 is entered and left as well. At every step the stack (levels, names), the app
    /// context, the ghost (direct parent only), the revision and the unsaved marker are checked.
    ///
    /// The scene draws leaf parts only, so a ray on a sub-assembly hits a nested leaf occurrence: this runner fails if that selection
    /// does not resolve to the direct sub-assembly occurrence (the bug "double Trigger on a sub-assembly does nothing").
    ///
    /// Same rules as <see cref="M9QuestAcceptance"/> (this class reuses its helpers): input is SYNTHETIC, Inventor answers are real,
    /// PASS COMPLETE only without NOT COVERED sub-cases. Written without a device: compiled and contract-tested, never run.
    /// </summary>
    internal sealed class M9NestedQuestAcceptance : M9QuestAcceptance
    {
        internal new static readonly string[] ReflectedMembers =
        {
            "AppController._inSession",
            "AppController.EnterSession",
            "AssemblyWorkspace._context",
            "AssemblyWorkspace._occurrence",
            "AssemblyWorkspace._busy",
        };

        protected override string Milestone => "m9n";
        protected override string FixtureMilestone => "m9n";
        protected override int TimeoutSeconds => 900;
        protected override bool ReportKnownFeatureGaps => false;
        protected override string RestoreHint => "--restore-quest m9n";
        protected override string CompletionNote =>
            "SYNTHETIC input and the dedicated nested fixture (m9n) only; the physical trial (M9-12) and a sub-assembly whose definition window is not open in Inventor are separate evidence and remain open";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M9NestedQuestAcceptance>("xr_m9n_acceptance");

        private AssemblyContext AssemblyContextNow => Read<AssemblyContext>(_assembly, "_context");
        private AssemblyOccurrence SelectedOccurrence => Read<AssemblyOccurrence>(_assembly, "_occurrence");

        // ------------------------------------------------------------------------------------------------------------------ run

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            RequireFixture();
            Check(fixture.Graph.Kind == "assembly", "the nested run starts on Assieme3 (found " + fixture.Graph.Kind + ")");
            _assemblyDocId = fixture.Graph.DocumentId;
            Record("Dedicated nested fixture m9n (guard " + FixturePrefix + "): " + fixture.Graph.Root.Name);
            Record("Navigation is driven like the app does; a double Trigger is two SYNTHETIC presses on a real body (detector clock driven by the runner)");

            if (!ReadBoolean(App, "_inSession")) Call(App, "EnterSession", EnvironmentMode.StudioVr);
            BindApp();
            BeginSynthetic();
            try
            {
                await CheckNestedScenarioAsync(ct);
            }
            finally
            {
                await CleanupAsync();
            }
            NotCovered("M9-12-physical", "sitting trial with the controllers on a real complex assembly needs a person wearing the Quest");
        }

        // ---------------------------------------------------------------------------------------------------------- diagnostics

        private string StateDump()
        {
            var ghost = _ghost == null ? "n/a" : _ghost.IsShowing ? _ghost.ParentDocumentId + "/" + _ghost.RendererCount + " renderers" : "off";
            return "[levels=" + string.Join(" > ", Nav.Levels.Select(l => l.Name + "/" + l.Context + (l.Dirty ? "*" : ""))) + ", context=" + App.Context
                + ", sceneDoc=" + Session.Scene?.Graph?.DocumentId + ", sceneRoot=" + Session.Scene?.Graph?.Root?.Name
                + ", selected=" + (SelectedOccurrence?.Id ?? "-") + ", assembly.active=" + _assembly.Active + ", assembly.busy=" + ReadBoolean(_assembly, "_busy")
                + ", assembly.notice='" + _assembly.Notice + "', ghost=" + ghost + ", hud='" + HudText().Replace('\n', '|') + "']";
        }

        private static IEnumerable<SceneNode> Leaves(SceneNode node)
        {
            if (node.Children.Count == 0 || node.DefinitionKind == "part") { yield return node; yield break; }
            foreach (var child in node.Children)
                foreach (var leaf in Leaves(child))
                    yield return leaf;
        }

        private SceneNode DirectNode(string occurrenceId) => Session.Scene.Graph.Root.Children.FirstOrDefault(n => n.OccurrenceId == occurrenceId);

        /// <summary>Center of the first drawn leaf of a direct occurrence: the scene has instances for leaf parts only, never for a sub-assembly.</summary>
        private Vector3 LeafCenter(AssemblyOccurrence occurrence)
        {
            var node = DirectNode(occurrence.Id);
            Check(node != null, occurrence.Name + " is a direct occurrence of the scene graph " + StateDump());
            var leaf = Leaves(node).FirstOrDefault(l => l.OccurrenceId != null && _view.Find(l.OccurrenceId) != null);
            Check(leaf != null, occurrence.Name + " has a drawn leaf part in the scene " + StateDump());
            return OccurrenceCenter(leaf.OccurrenceId);
        }

        private string[] GhostNames() => _ghost.IsShowing
            ? _ghost.GetComponentsInChildren<Transform>(true).Where(t => t.parent != null && t.parent.name == "GhostContext").Select(t => t.name).ToArray()
            : new string[0];

        // ----------------------------------------------------------------------------------------------------------- navigation

        /// <summary>The Assieme workspace shows the assembly <paramref name="documentId"/> at <paramref name="levels"/> levels, with context and scene loaded.</summary>
        private Task WaitAssemblyAtAsync(int levels, string documentId, string what, CancellationToken ct) => WaitDiag(() => Nav.Levels.Count == levels
            && Nav.Top.DocumentId == documentId && App.Context == DocContext.Assembly && _assembly.Active && !ReadBoolean(_assembly, "_busy")
            && AssemblyContextNow != null && AssemblyContextNow.Occurrences.Count > 0
            && Session.Scene?.Graph?.DocumentId == documentId && Session.Scene.Graph.Kind == "assembly",
            what + " at level " + levels + " " + StateDump(), ct, 60);

        /// <summary>First Trigger press selects <paramref name="occurrence"/> (by a ray on one of its drawn leaves), second press 0.2 s later enters it.</summary>
        private async Task EnterAsync(AssemblyOccurrence occurrence, Func<Vector3> center, Func<DocContext?, bool> contextOk, string contextName, CancellationToken ct)
        {
            int levels = Nav.Levels.Count;
            SnapPoses();
            var target = center();
            AimAt(target, AwayFromHead(target));
            await DoubleTriggerAsync(() => SelectedOccurrence?.Id == occurrence.Id && !ReadBoolean(_assembly, "_busy"), ct);
            await WaitDiag(() => Nav.Levels.Count == levels + 1 && contextOk(App.Context),
                "the double Trigger on " + occurrence.Name + " to push a level and open " + contextName + " (selected occurrence id " + occurrence.Id + ") " + StateDump(), ct, 60);
            await Task.Delay(300, ct);
        }

        private async Task CheckRootAsync(string rootName, CancellationToken ct)
        {
            await WaitAssemblyAtAsync(1, _assemblyDocId, "Assieme3", ct);
            Check(Nav.Top.Context == DocContext.Assembly && Nav.Parent == null && !Nav.Top.Dirty, "the stack holds Assieme3 alone, unmarked " + StateDump());
            Check(Nav.Breadcrumb == rootName, "the breadcrumb is the document name (" + Nav.Breadcrumb + ")");
            Check(!_ghost.IsShowing, "no ghost at the root of the stack");
        }

        private async Task CheckNestedScenarioAsync(CancellationToken ct)
        {
            await WaitAssemblyAtAsync(1, _assemblyDocId, "Assieme3", ct);
            var rootScene = Session.Scene;
            var rootName = rootScene.Graph.Root.Name;
            var rootState = await BaselineAsync(ct);
            await CheckRootAsync(rootName, ct);

            // ---- preconditions: two sub-assemblies, drawn as leaves under ids that differ from the direct occurrences
            var a1 = await FindOccurrenceAsync("Assieme1", ct);
            var a2 = await FindOccurrenceAsync("Assieme2", ct);
            Check(a1.Kind == "assembly" && a2.Kind == "assembly" && a1.Id != a2.Id, "Assieme1 and Assieme2 are sub-assembly occurrences (kinds " + a1.Kind + "/" + a2.Kind + ")");
            Check(!string.IsNullOrEmpty(a1.DefinitionId) && !string.IsNullOrEmpty(a2.DefinitionId), "the sub-assemblies carry their definition document ids");
            var n1 = DirectNode(a1.Id); var n2 = DirectNode(a2.Id);
            Check(n1 != null && n2 != null && n1.DefinitionKind == "assembly" && n2.DefinitionKind == "assembly", "the scene graph has Assieme1 and Assieme2 as assembly nodes");
            var leaves1 = Leaves(n1).ToList(); var leaves2 = Leaves(n2).ToList();
            Check(leaves1.Count == 2 && leaves2.Count == 1 && leaves1.Concat(leaves2).All(l => l.DefinitionKind == "part" && l.OccurrenceId != a1.Id && l.OccurrenceId != a2.Id),
                "the scene draws the leaf parts (" + string.Join(", ", leaves1.Concat(leaves2).Select(l => l.Name)) + ") under ids different from the direct occurrences: a ray picks a nested leaf");
            Check(rootScene.Graph.PlacedParts().Count() == 3, "the scene of Assieme3 draws the three parts of both sub-assemblies");
            Record("Assieme3 scene: " + a1.Name + " [" + a1.Id + "] -> leaves " + string.Join(", ", leaves1.Select(l => l.Name + " [" + l.OccurrenceId + "]"))
                + "; " + a2.Name + " [" + a2.Id + "] -> " + string.Join(", ", leaves2.Select(l => l.Name + " [" + l.OccurrenceId + "]")) + "; revision " + rootState.Revision);

            // ---- M9-02: the ray on a body of the sub-assembly selects the DIRECT sub-assembly occurrence (no error)
            SnapPoses();
            var c1 = LeafCenter(a1);
            AimAt(c1, AwayFromHead(c1));
            _clock += 5;
            TriggerTap();
            await WaitDiag(() => SelectedOccurrence?.Id == a1.Id && !ReadBoolean(_assembly, "_busy"),
                "the first Trigger press on a body of " + a1.Name + " to select the direct sub-assembly occurrence " + a1.Id + " (BUG if the ray gives a nested leaf id and the bridge refuses it: "
                + "'Select a direct occurrence or activate its subassembly first') " + PressDiagnostics() + " " + StateDump(), ct, 20);
            Check(!(_assembly.Notice ?? "").Contains("direct occurrence") && !(_assembly.Notice ?? "").Contains("REFERENCE_"), "selecting a sub-assembly shows no error: '" + _assembly.Notice + "'");
            Check(ActionEnabled(AssemblyWorkspace.IdOpen) && ActionEnabled(AssemblyWorkspace.IdActivate), "Apri and Attiva questo assieme are offered for the selected sub-assembly");
            await AssertUnchanged(rootState, "selecting a sub-assembly", ct);
            Pass("M9-02", "a ray on a body of " + a1.Name + " selected the direct sub-assembly occurrence " + a1.Id + " (the scene hit leaf " + leaves1[0].OccurrenceId + "), no error, revision unchanged (SYNTHETIC press)");

            // ---- M9-02: double Trigger on the sub-assembly -> new Assieme level
            await EnterAsync(a1, () => LeafCenter(a1), c => c == DocContext.Assembly, "the sub-assembly " + a1.Name, ct);
            await WaitAssemblyAtAsync(2, a1.DefinitionId, "the sub-assembly Assieme1", ct);
            RequireFixture();
            Check(Nav.Parent.DocumentId == _assemblyDocId && Nav.Top.FromOccurrenceId == a1.Id && Nav.Top.OccurrencePose != null && Nav.Top.OccurrencePose.Length == 16,
                "the level remembers Assieme3 as parent, the occurrence and its 16-float pose " + StateDump());
            Check(Nav.Levels[0].Name == rootName && Nav.Top.Name.Contains("Assieme1") && Nav.Breadcrumb.IndexOf("Assieme3", StringComparison.Ordinal) < Nav.Breadcrumb.IndexOf("Assieme1", StringComparison.Ordinal),
                "the path reads Assieme3 > Assieme1 (" + Nav.Breadcrumb + ")");
            Check(Session.Scene.Graph.Root.Name.Contains("Assieme1") && Session.Scene.Graph.PlacedParts().Count() == 2, "Inventor activated Assieme1: its scene draws its two parts");
            Check(_assembly.Active && !_design.Active && !_lamiera.Active && ReferenceEquals(Catalog.Active, _assembly), "the sub-assembly keeps the Assieme workspace open and serving the palette");
            Check(AssemblyContextNow.Occurrences.Any(o => o.Name.Contains("PartA")) && AssemblyContextNow.Occurrences.Any(o => o.Name.Contains("PartC")), "the Assieme context lists PartA and PartC");
            Check(!Nav.Levels.Any(l => l.Dirty), "no unsaved marker after the entry (" + Nav.Breadcrumb + ")");
            Check(ActionEnabled(DocumentActions.IdBack), "Torna is enabled one level down");
            var sub1State = await BaselineAsync(ct);
            Check(sub1State.DocumentId == a1.DefinitionId, "Inventor's active document is the definition of Assieme1");
            // ghost: only the direct parent (Assieme3), without the entered sub-assembly's own parts
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == _assemblyDocId && _ghost.RevisionLabel == rootState.Revision, "the ghost shows Assieme3 as it was at the entry " + StateDump());
            var ghostNames = GhostNames();
            Check(leaves2.All(l => ghostNames.Contains(l.Name)) && !leaves1.Any(l => ghostNames.Contains(l.Name)),
                "the ghost draws Assieme2's part and not the parts of the entered Assieme1 (" + string.Join(", ", ghostNames) + ")");
            await CheckTabsAsync(DocContext.Assembly, AssemblyTabs, ct);
            Pass("M9-02", "double Trigger on a body of " + a1.Name + " (two SYNTHETIC presses) pushed an Assieme level for " + Session.Scene.Graph.Root.Name + "; path " + Nav.Breadcrumb + "; context Assembly");
            Pass("M9-03", "sub-assembly level: ghost of the direct parent Assieme3 (rev " + _ghost.RevisionLabel + "), without the entered sub-assembly's parts");

            // ---- M9-02: PartA of the sub-assembly
            var partA = await FindOccurrenceAsync("PartA", ct);
            await EnterByDoubleTriggerAsync(partA, c => c == DocContext.Part, "Progettazione", ct);
            await WaitDesignReadyAsync(ct);
            RequireFixture();
            var partEntry = await BaselineAsync(ct);
            Check(Nav.Levels.Count == 3 && Nav.Levels[2].FromOccurrenceId == partA.Id && Nav.Levels[1].FromOccurrenceId == a1.Id && Nav.Levels[2].Name.Contains("PartA"),
                "the stack reads Assieme3 > Assieme1 > PartA with the occurrences they came from (" + Nav.Breadcrumb + ")");
            Check(Nav.Levels[0].Context == DocContext.Assembly && Nav.Levels[1].Context == DocContext.Assembly && Nav.Levels[2].Context == DocContext.Part, "level contexts are Assembly, Assembly, Part");
            Check(App.Context == DocContext.Part && _design.Active && !_assembly.Active && !_lamiera.Active, "the part opened in Progettazione and Assieme closed");
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == a1.DefinitionId, "the ghost shows only the direct parent Assieme1, not Assieme3 (" + _ghost.ParentDocumentId + ")");
            var ghostPart = GhostNames();
            Check(ghostPart.Any(n => n.Contains("PartC")) && !ghostPart.Any(n => n.Contains("PartA")) && !ghostPart.Any(n => n.Contains("PartB")),
                "the ghost of Assieme1 draws PartC, not the entered PartA and not Assieme2's PartB (" + string.Join(", ", ghostPart) + ")");
            Pass("M9-02", "double Trigger on PartA inside the sub-assembly: stack " + Nav.Breadcrumb + ", context Part, Progettazione");
            Pass("M9-03", "part level: the ghost is the direct parent Assieme1 only (" + string.Join(", ", ghostPart) + ")");

            // ---- M9-09 / M9-04: modify PartA (existing feature-edit path, XR Undo cleanup), unsaved marker
            await CheckFeatureEditAsync(ct);
            var partState = await BaselineAsync(ct);
            // The marker follows the VISUAL revision (DirtyTracker): after the edit and its XR Undo the visual revision is back at the entry value
            // when the bridge restored it (CadEventJournal.TryRestoreRevision), so the marker must equal (visual revision differs from the entry).
            bool partExpected = partState.VisualRevision != partEntry.VisualRevision;
            await WaitDiag(() => Nav.Levels.Count == 3 && Nav.Top.Dirty == partExpected,
                "the unsaved marker of PartA to follow its visual revision (" + partEntry.VisualRevision + " -> " + partState.VisualRevision + ") " + StateDump(), ct, 20);
            Record("PartA visual revision " + partEntry.VisualRevision + " -> " + partState.VisualRevision + " after edit + XR Undo (revision " + partEntry.Revision + " -> "
                + partState.Revision + "); marker " + (Nav.Top.Dirty ? "on" : "off") + ", expected " + (partExpected ? "on" : "off") + " from DirtyTracker semantics");

            // ---- M9-04: Torna 1 (X held at rest): back to Assieme1
            await EnsureRestAsync(ct);
            Check(_input.RestingProbe(), "the part is at rest before Torna");
            HoldXForBack();
            await WaitAssemblyAtAsync(2, a1.DefinitionId, "the first Torna", ct);
            Check(Nav.Top.Name.Contains("Assieme1") && Nav.Levels.Count == 2 && Nav.Levels.All(l => !l.Name.Contains("PartA")), "the part level is gone (" + Nav.Breadcrumb + ")");
            Check(Session.Scene.Graph.PlacedParts().Count() == 2 && AssemblyContextNow.Occurrences.Any(o => o.Name.Contains("PartA")), "the scene of Assieme1 lists PartA and PartC again");
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == _assemblyDocId, "back in the sub-assembly the ghost is the direct parent Assieme3 again " + StateDump());
            var sub1After = await BaselineAsync(ct);
            await WaitDiag(() => Nav.Top.Dirty == (sub1After.VisualRevision != sub1State.VisualRevision),
                "the unsaved marker of Assieme1 to follow its visual revision (" + sub1State.VisualRevision + " -> " + sub1After.VisualRevision + ") " + StateDump(), ct, 20);
            Record("Assieme1 visual revision " + sub1State.VisualRevision + " -> " + sub1After.VisualRevision + " (revision " + sub1State.Revision + " -> " + sub1After.Revision + "); marker " + (Nav.Top.Dirty ? "on" : "off") + "; PartA revision at the end of the edit " + partState.Revision);
            Pass("M9-04", "Torna (X held 1 s, SYNTHETIC timestamps) from PartA returned to Assieme1 (" + Nav.Breadcrumb + "); the only backend call is the activation of the parent, nothing saved; "
                + "the unsaved marker of Assieme1 follows its visual revision (" + (Nav.Top.Dirty ? "on" : "off") + ")");

            // ---- M9-04: Torna 2 (Documento tab): back to Assieme3, which lists both sub-assemblies again
            RunAction(DocumentActions.IdBack);
            await WaitAssemblyAtAsync(1, _assemblyDocId, "the second Torna", ct);
            Check(Nav.Parent == null && Nav.Breadcrumb.StartsWith(rootName, StringComparison.Ordinal) && !_ghost.IsShowing, "back at the root: one level, no ghost (" + Nav.Breadcrumb + ")");
            Check(Session.Scene.Graph.PlacedParts().Count() == 3 && Session.Scene.Graph.Root.Children.Count(n => n.DefinitionKind == "assembly") == 2,
                "the scene of Assieme3 draws its three parts under two sub-assemblies again");
            Check(AssemblyContextNow.Occurrences.Any(o => o.Id == a1.Id && o.Kind == "assembly") && AssemblyContextNow.Occurrences.Any(o => o.Id == a2.Id && o.Kind == "assembly"),
                "the Assieme context of Assieme3 lists Assieme1 and Assieme2 again");
            var rootAfter = await BaselineAsync(ct);
            Check(rootAfter.DocumentId == _assemblyDocId, "Inventor's active document is Assieme3 again");
            // No net geometry change is expected: the marker may be off; it must equal (visual revision differs), never follow the activation-bumped revision.
            await WaitDiag(() => Nav.Top.Dirty == (rootAfter.VisualRevision != rootState.VisualRevision),
                "the unsaved marker of Assieme3 to follow its visual revision (" + rootState.VisualRevision + " -> " + rootAfter.VisualRevision + ") " + StateDump(), ct, 20);
            Record("Assieme3 visual revision " + rootState.VisualRevision + " -> " + rootAfter.VisualRevision + " (revision " + rootState.Revision + " -> " + rootAfter.Revision + "); marker " + (Nav.Top.Dirty ? "on" : "off"));
            Pass("M9-04", "second Torna (Documento tab) returned to Assieme3 (" + Nav.Breadcrumb + "): both sub-assemblies listed again, no ghost, nothing saved; marker follows the visual revision ("
                + (Nav.Top.Dirty ? "on" : "off") + ")");

            // ---- Assieme2: enter and leave, no edit: the document and its revision are untouched
            var state3 = await BaselineAsync(ct);
            await EnterAsync(a2, () => LeafCenter(a2), c => c == DocContext.Assembly, "the sub-assembly " + a2.Name, ct);
            await WaitAssemblyAtAsync(2, a2.DefinitionId, "the sub-assembly Assieme2", ct);
            Check(Nav.Top.FromOccurrenceId == a2.Id && Nav.Top.Name.Contains("Assieme2") && Nav.Parent.DocumentId == _assemblyDocId, "the level is Assieme2, entered from its occurrence (" + Nav.Breadcrumb + ")");
            Check(Session.Scene.Graph.PlacedParts().Count() == 1 && AssemblyContextNow.Occurrences.Any(o => o.Name.Contains("PartB")), "the scene of Assieme2 draws PartB");
            var ghost2 = GhostNames();
            Check(_ghost.IsShowing && _ghost.ParentDocumentId == _assemblyDocId && ghost2.Any(n => n.Contains("PartA")) && ghost2.Any(n => n.Contains("PartC")) && !ghost2.Any(n => n.Contains("PartB")),
                "the ghost of Assieme3 shows Assieme1's parts and not PartB (" + string.Join(", ", ghost2) + ")");
            RunAction(DocumentActions.IdBack);
            await WaitAssemblyAtAsync(1, _assemblyDocId, "the Torna from Assieme2", ct);
            Check(!_ghost.IsShowing && Session.Scene.Graph.PlacedParts().Count() == 3, "back at Assieme3: no ghost, three parts");
            await AssertUnchanged(state3, "entering and leaving Assieme2", ct, activates: true);
            Pass("M9-02", "Assieme3 > Assieme2 and back with Torna: levels, context Assembly, ghost of the direct parent, scene of Assieme3 complete again, visual revision " + state3.VisualRevision + " unchanged (SYNTHETIC)");
        }
    }
}
#endif
