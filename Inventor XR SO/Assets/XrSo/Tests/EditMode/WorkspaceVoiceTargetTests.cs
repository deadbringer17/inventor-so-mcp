using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Voice;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public class WorkspaceVoiceTargetTests
    {
        private sealed class Surface : IWorkspaceVoiceSurface
        {
            public bool Active { get; set; }
            public bool Enabled { get; set; } = true;
            public string LastInvoked, LastField; public double LastValue;
            public bool IsEnabled(string id) => Enabled;
            public bool Invoke(string id) { LastInvoked = id; return true; }
            public DictationField ArmedField { get; set; }
            public bool SetArmedField(string id, double v) { LastField = id; LastValue = v; return true; }
            public KeyValuePair<string, string>[] Occurrences;
            public string Opened;
            public VoiceOpenResult FindOccurrence(string name) => Occurrences == null
                ? VoiceOpenResult.UnavailableHere : OccurrenceNameMatcher.Match(Occurrences, name);
            public VoiceOpenResult OpenOccurrence(string name)
            {
                var r = FindOccurrence(name);
                if (r.Found) Opened = r.OccurrenceId;
                return r;
            }
        }

        /// <summary>Workspace M6: declares its actions to the catalog; voice labels come from there.</summary>
        private sealed class Provider : IActionProvider
        {
            public readonly List<XrAction> All = new List<XrAction>();
            public readonly List<string> Invoked = new List<string>();
            public IReadOnlyList<XrTab> Tabs { get; } = new[] { new XrTab("t", "T") };
            public IEnumerable<XrAction> Actions => All;
            public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Enumerable.Empty<XrAction>();
            public CommitBarState CommitBar => null;
            public XrAction Add(string id, string label, bool enabled = true, string[] synonyms = null, bool voice = true, string reason = "spento")
            {
                var action = new XrAction(id, label, "t", () => enabled, () => Invoked.Add(id), () => reason, synonyms, voiceInvokes: voice);
                All.Add(action);
                return action;
            }
        }

        private static ActionCatalog CatalogWith(Provider provider, Action<string> openSpace = null, bool inSession = true)
        {
            Action act(string name) => () => openSpace?.Invoke(name);
            var stack = new NavigationStack();
            stack.Reset(new NavLevel("a", DocContext.Assembly, "A.iam"));
            stack.Push(new NavLevel("p", DocContext.Part, "P.ipt", "o"));
            var document = TestDocs.Create(stack, goBack: act("back"), save: act("save"), inSession: () => inSession,
                openConnection: act("connection"), leaveSession: act("exit"));
            var catalog = new ActionCatalog(document);
            catalog.SetActive(provider);
            return catalog;
        }
        [Test]
        public void Catalog_actions_resolve_by_label_and_synonym_and_apply_is_never_voiced()
        {
            var provider = new Provider();
            provider.Add("design.extrude", "Estrusione", synonyms: new[] { "estrudi", "crea estrusione" });
            provider.Add("design.hole", "Foro", synonyms: new[] { "crea foro" });
            provider.Add(CommitIds.Apply, "Applica", voice: false);
            provider.Add("design.fillet", "Raccordo", enabled: false, reason: "Scegli prima uno spigolo.");
            provider.Add("a", "Linea"); provider.Add("b", "Linea");
            var target = new WorkspaceVoiceTarget(null) { InSession = true, Catalog = CatalogWith(provider) };
            Assert.IsTrue(target.TryResolveAction("estrudi", out var extrude));
            Assert.AreEqual("act:design.extrude", extrude.Id);
            Assert.IsTrue(target.IsEnabled(extrude.Id));
            Assert.IsTrue(target.Invoke(extrude.Id));
            CollectionAssert.AreEqual(new[] { "design.extrude" }, provider.Invoked);
            Assert.IsTrue(target.TryResolveAction("crea foro", out _));
            Assert.IsFalse(target.TryResolveAction("applica", out _), "M5-11: Apply is physical only");
            Assert.IsFalse(target.TryResolveAction("linea", out _), "homonymous labels need the pointer");
            Assert.IsFalse(target.TryResolveAction("flangia", out _), "finite vocabulary: unknown labels are refused");
            Assert.IsTrue(target.TryResolveAction("raccordo", out var disabled));
            Assert.IsFalse(disabled.Enabled);
            Assert.IsFalse(target.Invoke(disabled.Id), "disabled means no mutation");
            Assert.AreEqual("Scegli prima uno spigolo.", target.DisabledReason(disabled.Id));
        }

        [Test]
        public void Prefixes_and_values_in_labels_are_understood_without_scanning_any_panel()
        {
            var provider = new Provider();
            provider.Add("inspect.section.offset", "Scostamento: 25 mm");
            provider.Add("inspect.scale.table", "Scala da tavolo • 60 cm");
            provider.Add("inspect.context.enter", "Apri contesto");
            var target = new WorkspaceVoiceTarget(null) { InSession = true, Catalog = CatalogWith(provider) };
            Assert.IsTrue(target.TryResolveAction("premi scostamento", out var offset));
            Assert.AreEqual("act:inspect.section.offset", offset.Id);
            Assert.IsTrue(target.TryResolveAction("scala da tavolo", out _));
            Assert.IsTrue(target.TryResolveAction("apri contesto", out var enter));
            Assert.AreEqual("act:inspect.context.enter", enter.Id);
        }

        [Test]
        public void Document_tab_actions_are_selected_through_the_catalog()
        {
            var opened = new List<string>();
            var target = new WorkspaceVoiceTarget(null) { InSession = true, Catalog = CatalogWith(new Provider(), opened.Add) };
            Assert.IsTrue(target.TryResolveAction("torna", out var action));
            Assert.AreEqual("act:doc.back", action.Id);
            Assert.IsTrue(target.Invoke(action.Id));
            Assert.IsTrue(target.TryResolveAction("salva", out var save));
            Assert.IsTrue(target.Invoke(save.Id));
            Assert.IsTrue(target.TryResolveAction("connessione", out var connection));
            Assert.IsTrue(target.Invoke(connection.Id));
            CollectionAssert.AreEqual(new[] { "back", "save", "connection" }, opened);
        }
        // ---- M9-10: voce coerente con il contesto

        [Test]
        public void Actions_of_other_contexts_are_not_found_or_disabled()
        {
            var assembly = new Provider(); assembly.Add("assembly.isolate", "Isola", synonyms: new[] { "isola componente" });
            assembly.Add("assembly.move", "Sposta", enabled: false, reason: "Seleziona un componente.");
            var part = new Provider(); part.Add("design.extrude", "Estrusione", synonyms: new[] { "estrudi" });
            var inAssembly = new WorkspaceVoiceTarget(null) { InSession = true, Catalog = CatalogWith(assembly) };
            var inPart = new WorkspaceVoiceTarget(null) { InSession = true, Catalog = CatalogWith(part) };
            Assert.IsFalse(inAssembly.TryResolveAction("estrudi", out _), "Estrudi does not exist in Assembly");
            Assert.IsFalse(inPart.TryResolveAction("isola", out _));
            Assert.IsFalse(inPart.TryResolveAction("sposta", out _), "Sposta does not exist in Part");
            Assert.IsTrue(inAssembly.TryResolveAction("sposta", out var move));
            Assert.IsFalse(move.Enabled, "present but not usable: Disabled, never invoked");
            Assert.IsFalse(inAssembly.Invoke(move.Id));
            Assert.AreEqual(VoiceMatchKind.NotFound, inAssembly.Catalog.ResolveVoice("estrudi").Kind);
            Assert.AreEqual(VoiceMatchKind.NotFound, inPart.Catalog.ResolveVoice("sposta").Kind);
        }

        [Test]
        public void Space_phrases_never_resolve_to_an_action_or_a_command()
        {
            var provider = new Provider(); provider.Add("design.hole", "Foro");
            var surface = new Surface { Active = true };
            var target = new WorkspaceVoiceTarget(surface) { InSession = true, Catalog = CatalogWith(provider) };
            foreach (var phrase in new[] { "vai in progettazione", "progettazione", "lamiera", "assieme", "assemblaggio", "vai in lamiera", "vai in assieme" })
            {
                Assert.IsFalse(target.TryResolveAction(phrase, out _), phrase);
                var route = new VoiceCommandRouter().Route(phrase, new DelegateCommandAvailability(_ => CommandAvailability.Available));
                Assert.AreEqual(VoiceRejectReason.SpaceCommand, route.RejectReason, phrase);
            }
            Assert.IsEmpty(provider.Invoked);
            Assert.IsNull(surface.LastInvoked);
        }

        [Test]
        public void Apri_name_opens_the_unique_occurrence_in_assembly_only()
        {
            var surface = new Surface
            {
                Active = true,
                Occurrences = new[]
                {
                    new KeyValuePair<string, string>("o1", "Staffa:1"), new KeyValuePair<string, string>("o2", "Staffa:2"),
                    new KeyValuePair<string, string>("o3", "Piastra base:1"),
                }
            };
            var target = new WorkspaceVoiceTarget(null, null, surface) { InSession = true, Catalog = CatalogWith(new Provider()) };
            Assert.IsTrue(target.TryResolveAction("apri piastra base", out var open));
            Assert.IsTrue(open.Enabled);
            Assert.IsTrue(target.IsEnabled(open.Id));
            Assert.IsNull(surface.Opened, "resolving does not open");
            Assert.IsTrue(target.Invoke(open.Id));
            Assert.AreEqual("o3", surface.Opened);

            surface.Opened = null;
            Assert.IsTrue(target.TryResolveAction("apri staffa", out var ambiguous));
            Assert.IsFalse(ambiguous.Enabled);
            StringAssert.Contains("Più componenti", ambiguous.Reply);
            Assert.IsFalse(target.Invoke(ambiguous.Id));
            Assert.IsTrue(target.TryResolveAction("apri flangia", out var missing));
            Assert.IsFalse(missing.Enabled);
            StringAssert.Contains("non trovato", missing.Reply);
            Assert.IsNull(surface.Opened);
            Assert.IsTrue(target.TryResolveAction("apri staffa 2", out var second));
            Assert.IsTrue(target.Invoke(second.Id));
            Assert.AreEqual("o2", surface.Opened);
        }

        [Test]
        public void Apri_name_outside_assembly_is_not_available_and_declared_apri_actions_still_win()
        {
            var provider = new Provider(); provider.Add("inspect.context.enter", "Apri contesto");
            var part = new Surface { Active = true };      // nessun elenco di occorrenze: non e un Assieme
            var target = new WorkspaceVoiceTarget(null, part) { InSession = true, Catalog = CatalogWith(provider) };
            Assert.IsTrue(target.TryResolveAction("apri staffa", out var refused));
            Assert.IsFalse(refused.Enabled);
            StringAssert.Contains("non è disponibile", refused.Reply);
            Assert.IsFalse(target.Invoke(refused.Id));
            Assert.IsNull(part.Opened);
            Assert.IsTrue(target.TryResolveAction("apri contesto", out var declared));
            Assert.AreEqual("act:inspect.context.enter", declared.Id);
        }

        [Test]
        public void Apply_is_not_resolved_by_voice_with_or_without_the_apri_prefix()
        {
            var provider = new Provider(); provider.Add(CommitIds.Apply, "Applica", voice: false);
            var target = new WorkspaceVoiceTarget(null) { InSession = true, Catalog = CatalogWith(provider) };
            Assert.IsFalse(target.TryResolveAction("applica", out _));
            Assert.IsFalse(target.TryResolveAction("premi applica", out _));
            Assert.IsEmpty(provider.Invoked);
        }

        [Test]
        public void Catalog_actions_are_refused_outside_a_session_or_without_a_catalog()
        {
            var provider = new Provider();
            provider.Add("design.hole", "Foro");
            var target = new WorkspaceVoiceTarget(null) { InSession = false, Catalog = CatalogWith(provider) };
            Assert.IsFalse(target.IsEnabled("act:design.hole"));
            Assert.IsFalse(target.Invoke("act:design.hole"));
            Assert.IsEmpty(provider.Invoked);
            target.InSession = true;
            Assert.IsTrue(target.Invoke("act:design.hole"));
            target.Catalog = null;
            Assert.IsFalse(target.TryResolveAction("foro", out _));
            Assert.IsFalse(target.Invoke("act:design.hole"));
        }

        [Test]
        public void Undo_redo_and_recovery_labels_need_a_spoken_confirmation()
        {
            var provider = new Provider();
            provider.Add("u", "Annulla modifica XR"); provider.Add("r", "Ho controllato il CAD"); provider.Add("h", "Foro");
            var target = new WorkspaceVoiceTarget(null) { InSession = true, Catalog = CatalogWith(provider) };
            Assert.IsTrue(target.TryResolveAction("annulla modifica xr", out var undo)); Assert.IsTrue(undo.RequiresConfirmation);
            Assert.IsTrue(target.TryResolveAction("ho controllato il cad", out var checkedCad)); Assert.IsTrue(checkedCad.RequiresConfirmation);
            Assert.IsTrue(target.TryResolveAction("foro", out var hole)); Assert.IsFalse(hole.RequiresConfirmation);
        }

        [Test]
        public void InactiveWorkspaceIsDisabledWithItalianReason()
        {
            var s = new Surface { Active = false };
            var t = new WorkspaceVoiceTarget(s) { InSession = true };
            Assert.IsFalse(t.IsEnabled(CommandIds.Flange));
            Assert.AreEqual("Comando non disponibile in questa modalità", t.DisabledReason(CommandIds.Flange));
            Assert.IsFalse(t.Invoke(CommandIds.Flange));
            Assert.IsNull(t.ArmedField);
            t.SetField("x", 1); t.ShowApplyConfirmation();
            Assert.IsNull(s.LastInvoked); Assert.IsNull(s.LastField);
        }

        [Test]
        public void NullSurfaceIsSafe()
        {
            var t = new WorkspaceVoiceTarget(null) { InSession = true };
            Assert.IsFalse(t.IsEnabled(CommandIds.Undo));
            Assert.IsFalse(t.Invoke(CommandIds.Undo));
        }

        [Test]
        public void ActiveWorkspaceRoutesCommandsFieldsAndApplyConfirmation()
        {
            var s = new Surface { Active = true, ArmedField = new DictationField("f", QuantityUnit.Degrees, 0, 90) };
            var t = new WorkspaceVoiceTarget(s) { InSession = true };
            Assert.IsTrue(t.IsEnabled(CommandIds.Flange));
            Assert.AreEqual("", t.DisabledReason(CommandIds.Flange));
            Assert.IsTrue(t.Invoke(CommandIds.Flange));
            Assert.AreEqual(CommandIds.Flange, s.LastInvoked);
            Assert.AreEqual(QuantityUnit.Degrees, t.ArmedField.Unit);
            t.SetField("f", 45);
            Assert.AreEqual("f", s.LastField); Assert.AreEqual(45, s.LastValue);
            t.ShowApplyConfirmation();
            Assert.AreEqual(CommandIds.Apply, s.LastInvoked);
        }

        [Test]
        public void DisabledCommandGivesReason()
        {
            var t = new WorkspaceVoiceTarget(new Surface { Active = true, Enabled = false }) { InSession = true };
            Assert.IsFalse(t.IsEnabled(CommandIds.Flange));
            Assert.AreEqual(WorkspaceVoiceTarget.UnavailableReason, t.DisabledReason(CommandIds.Flange));
        }

        [Test]
        public void HomeDoesNotAcceptVoiceEvenWithAnActiveSurface()
        {
            var s = new Surface { Active = true };
            var t = new WorkspaceVoiceTarget(s);
            Assert.IsFalse(t.AcceptsVoice);
            Assert.IsFalse(t.IsEnabled(CommandIds.Flange));
            Assert.IsFalse(t.Invoke(CommandIds.Flange));
            t.InSession = true;
            Assert.IsTrue(t.AcceptsVoice);
            Assert.IsTrue(t.Invoke(CommandIds.Flange));
            t.InSession = false;
            Assert.IsNull(t.ArmedField);
        }

        [Test]
        public void CommandsGoToTheActiveWorkspaceOnly()
        {
            var lamiera = new Surface { Active = false };
            var design = new Surface { Active = true, ArmedField = new DictationField("design.dimension", QuantityUnit.Millimeters, 0.001, 10000) };
            var assembly = new Surface { Active = false };
            var inspect = new Surface { Active = true };
            var t = new WorkspaceVoiceTarget(lamiera, design, assembly, inspect) { InSession = true };
            Assert.IsTrue(t.Invoke(CommandIds.Chamfer));
            Assert.AreEqual(CommandIds.Chamfer, design.LastInvoked);
            Assert.IsNull(inspect.LastInvoked); Assert.IsNull(lamiera.LastInvoked);
            Assert.AreEqual("design.dimension", t.ArmedField.Id);
            t.SetField("design.dimension", 3);
            Assert.AreEqual(3, design.LastValue);

            design.Active = false; assembly.Active = true;
            Assert.IsTrue(t.Invoke(CommandIds.Undo));
            Assert.AreEqual(CommandIds.Undo, assembly.LastInvoked);

            assembly.Active = false;
            Assert.IsTrue(t.Invoke(CommandIds.Measure));
            Assert.AreEqual(CommandIds.Measure, inspect.LastInvoked);

            lamiera.Active = true;
            Assert.IsTrue(t.Invoke(CommandIds.Flange));
            Assert.AreEqual(CommandIds.Flange, lamiera.LastInvoked);
        }

        [Test]
        public void EnablementFollowsTheActiveSurfaceAndIsolateHasItsOwnReason()
        {
            var inspect = new Surface { Active = true, Enabled = false };
            var t = new WorkspaceVoiceTarget(new Surface(), null, null, inspect) { InSession = true };
            Assert.IsFalse(t.IsEnabled(CommandIds.Isolate));
            Assert.AreEqual(WorkspaceVoiceTarget.IsolateUnavailableReason, t.DisabledReason(CommandIds.Isolate));
            Assert.AreEqual(WorkspaceVoiceTarget.UnavailableReason, t.DisabledReason(CommandIds.Fillet));
        }
    }
}
