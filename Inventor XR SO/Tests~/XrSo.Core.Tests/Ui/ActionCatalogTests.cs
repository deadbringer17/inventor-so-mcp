using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class ActionCatalogTests
    {
        private sealed class Provider : IActionProvider
        {
            public List<XrTab> TabList = new List<XrTab>();
            public List<XrAction> All = new List<XrAction>();
            public Dictionary<SelectionKind, List<XrAction>> Ctx = new Dictionary<SelectionKind, List<XrAction>>();
            public IReadOnlyList<XrTab> Tabs => TabList;
            public IEnumerable<XrAction> Actions => All;
            public IEnumerable<XrAction> ContextActions(SelectionKind s) => Ctx.TryGetValue(s, out var l) ? l : Enumerable.Empty<XrAction>();
            public CommitBarState CommitBar => null;
        }

        private static XrAction A(string id, string label, string tab, bool enabled = true, Action run = null, string[] syn = null, bool voice = true)
            => new XrAction(id, label, tab, () => enabled, run ?? (() => { }), () => "spento", syn, voiceInvokes: voice);

        private static Provider Spaces()
        {
            var p = new Provider();
            p.TabList.Add(new XrTab(ActionCatalog.DocumentTab, "Documento"));
            p.All.Add(A("doc.save", "Ispeziona", ActionCatalog.DocumentTab));
            return p;
        }

        [Fact]
        public void Tabs_are_active_tabs_then_spaces_and_hide_internal_tabs()
        {
            var design = new Provider();
            design.TabList.Add(new XrTab("sketch", "Schizzo"));
            design.TabList.Add(new XrTab(ActionCatalog.CommitTab, "commit"));
            var c = new ActionCatalog(Spaces());
            c.SetActive(design);
            Assert.Equal(new[] { "sketch", ActionCatalog.DocumentTab }, c.Tabs.Select(t => t.Id));
        }

        [Fact]
        public void Without_active_provider_only_spaces_is_shown()
        {
            var c = new ActionCatalog(Spaces());
            Assert.Equal(new[] { ActionCatalog.DocumentTab }, c.Tabs.Select(t => t.Id));
            Assert.Single(c.Palette(ActionCatalog.DocumentTab));
        }

        [Fact]
        public void Palette_rejects_more_than_eight_actions()
        {
            var p = new Provider();
            p.TabList.Add(new XrTab("t", "T"));
            for (int i = 0; i < 9; i++) p.All.Add(A("a" + i, "Azione " + i, "t"));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            Assert.Throws<InvalidOperationException>(() => c.Palette("t"));
        }

        [Fact]
        public void Context_rejects_more_than_six_actions()
        {
            var p = new Provider();
            p.Ctx[SelectionKind.Edge] = Enumerable.Range(0, 7).Select(i => A("e" + i, "E" + i, "t")).ToList();
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            Assert.Throws<InvalidOperationException>(() => c.Context(SelectionKind.Edge));
        }

        [Fact]
        public void Duplicate_ids_between_provider_and_spaces_are_rejected()
        {
            var p = new Provider();
            p.All.Add(A("doc.save", "Altro", "t"));
            var c = new ActionCatalog(Spaces());
            Assert.Throws<InvalidOperationException>(() => c.SetActive(p));
        }

        [Fact]
        public void TryInvoke_runs_enabled_actions_only()
        {
            int runs = 0;
            var p = new Provider();
            p.All.Add(A("on", "Acceso", "t", true, () => runs++));
            p.All.Add(A("off", "Spento", "t", false, () => runs++));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            Assert.True(c.TryInvoke("on"));
            Assert.False(c.TryInvoke("off"));
            Assert.False(c.TryInvoke("missing"));
            Assert.Equal(1, runs);
        }

        [Fact]
        public void Disabled_action_reports_its_reason()
        {
            var off = A("off", "Spento", "t", false);
            Assert.False(off.Enabled);
            Assert.Equal("spento", off.DisabledReason);
            Assert.Equal("", A("on", "Acceso", "t").DisabledReason);
        }

        [Theory]
        [InlineData("estrusione", VoiceMatchKind.Ok, "design.extrude")]
        [InlineData("Estrudi", VoiceMatchKind.Ok, "design.extrude")]
        [InlineData("raccordo", VoiceMatchKind.Disabled, "design.fillet")]
        [InlineData("flangia", VoiceMatchKind.NotFound, null)]
        public void ResolveVoice_matches_labels_and_synonyms(string phrase, VoiceMatchKind kind, string id)
        {
            var p = new Provider();
            p.All.Add(A("design.extrude", "Estrusione", "t", syn: new[] { "estrudi" }));
            p.All.Add(A("design.fillet", "Raccordo", "t", enabled: false));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            var m = c.ResolveVoice(phrase);
            Assert.Equal(kind, m.Kind);
            Assert.Equal(id, m.Action?.Id);
            if (kind == VoiceMatchKind.Disabled) Assert.Equal("spento", m.Reason);
        }

        [Theory]
        [InlineData("distanza", "Distanza: 5 mm")]
        [InlineData("scala da tavolo", "Scala da tavolo • 60 cm")]
        [InlineData("asse", "Asse 2")]
        public void ResolveVoice_accepts_a_label_without_its_value(string phrase, string label)
        {
            var p = new Provider();
            p.All.Add(A("v", label, "t"));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            var m = c.ResolveVoice(phrase);
            Assert.Equal(VoiceMatchKind.Ok, m.Kind);
            Assert.Equal("v", m.Action.Id);
        }

        [Fact]
        public void ResolveVoice_value_aliases_stay_ambiguous_between_two_enabled_labels()
        {
            var p = new Provider();
            p.All.Add(A("a1", "Asse 1", "t"));
            p.All.Add(A("a2", "Asse 2", "t"));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            Assert.Equal(VoiceMatchKind.Ambiguous, c.ResolveVoice("asse").Kind);
            Assert.Equal("a2", c.ResolveVoice("asse 2").Action.Id);
        }

        [Fact]
        public void ResolveVoice_rejects_two_enabled_actions_with_the_same_name()
        {
            var p = new Provider();
            p.All.Add(A("a", "Estrusione", "t"));
            p.All.Add(A("b", "Estrudi schizzo", "t", syn: new[] { "estrusione" }));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            var m = c.ResolveVoice("estrusione");
            Assert.Equal(VoiceMatchKind.Ambiguous, m.Kind);
            Assert.Equal(new[] { "a", "b" }, m.Conflicts.Select(x => x.Id).OrderBy(x => x));
        }

        [Fact]
        public void Ambiguity_ignores_disabled_duplicates()
        {
            var p = new Provider();
            p.All.Add(A("a", "Estrusione", "t"));
            p.All.Add(A("b", "Estrusione", "u", enabled: false));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            Assert.Equal("a", c.ResolveVoice("estrusione").Action.Id);
        }

        [Fact]
        public void Apply_is_found_by_voice_but_marked_as_not_invokable()
        {
            var p = new Provider();
            p.All.Add(A(CommitIds.Apply, "Applica", ActionCatalog.CommitTab, voice: false));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            var m = c.ResolveVoice("applica");
            Assert.Equal(VoiceMatchKind.Ok, m.Kind);
            Assert.False(m.Action.VoiceInvokes);
        }

        [Fact]
        public void SetActive_and_NotifyChanged_raise_Changed()
        {
            int changes = 0;
            var c = new ActionCatalog(Spaces());
            c.Changed += () => changes++;
            c.SetActive(new Provider());
            c.NotifyChanged();
            c.SetActive(null);
            Assert.Equal(3, changes);
        }

        [Fact]
        public void Constructor_validates_required_fields()
        {
            Assert.Throws<ArgumentException>(() => new XrAction("", "L", "t", () => true, () => { }));
            Assert.Throws<ArgumentException>(() => new XrAction("id", " ", "t", () => true, () => { }));
            Assert.Throws<ArgumentException>(() => new XrAction("id", "L", "", () => true, () => { }));
            Assert.Throws<ArgumentNullException>(() => new XrAction("id", "L", "t", null, () => { }));
        }

        [Fact]
        public void FindTab_also_returns_hidden_tabs_of_the_active_provider()
        {
            var c = new ActionCatalog(Spaces());
            var p = new Provider();
            p.TabList.Add(new XrTab("a", "A"));
            p.TabList.Add(new XrTab("_pick.0", "Elenco"));
            c.SetActive(p);
            Assert.DoesNotContain(c.Tabs, t => t.Id == "_pick.0");
            Assert.Equal("Elenco", c.FindTab("_pick.0").Label);
            Assert.Null(c.FindTab("assente"));
        }

        // ---- M9 composizione delle schede dal contesto

        private sealed class StatefulProvider : IActionProvider, ITabStateSource
        {
            public List<XrTab> TabList = new List<XrTab>();
            public List<XrAction> All = new List<XrAction>();
            public TabState State = new TabState();
            public IReadOnlyList<XrTab> Tabs => TabList;
            public IEnumerable<XrAction> Actions => All;
            public IEnumerable<XrAction> ContextActions(SelectionKind s) => Enumerable.Empty<XrAction>();
            public CommitBarState CommitBar => null;
            public TabState TabState => State;
        }

        private static Provider SharedView()
        {
            var v = new Provider();
            v.TabList.Add(new XrTab("vista", "Vista"));
            v.All.Add(A("view.fit", "Adatta", "vista"));
            return v;
        }

        private static Provider SharedInspect()
        {
            var i = new Provider();
            i.TabList.Add(new XrTab(ContextTabs.Inspect, "Ispeziona ▸"));
            i.TabList.Add(new XrTab("misura", "Misura"));
            i.TabList.Add(new XrTab("sezione", "Sezione"));
            i.TabList.Add(new XrTab("visibilita", "Visibilità"));
            i.TabList.Add(new XrTab("verifica", "Verifica"));
            i.TabList.Add(new XrTab(ActionCatalog.InspectExitTab, "◂"));
            i.All.Add(A("inspect.group", "Ispeziona", ContextTabs.Inspect));
            i.All.Add(A("inspect.measure", "Misura", "misura"));
            i.All.Add(A("inspect.section", "Sezione", "sezione"));
            i.All.Add(A("inspect.visibility.isolate", "Isola vista", "visibilita"));
            i.All.Add(A("inspect.verify.health", "Salute", "verifica"));
            i.All.Add(A("inspect.group.exit", "Indietro", ActionCatalog.InspectExitTab));
            return i;
        }

        private static StatefulProvider PartProvider()
        {
            var d = new StatefulProvider();
            foreach (var t in new[] { "schizzo", "vincoli", "feature", "opzioni", "parametri" }) d.TabList.Add(new XrTab(t, t));
            foreach (var t in new[] { "schizzo", "vincoli", "feature", "opzioni", "parametri" }) d.All.Add(A("d." + t, "Azione " + t, t));
            d.All.Add(A("d.model", "Vista modello", "vista"));
            return d;
        }

        private static ActionCatalog Composed(IActionProvider active, DocContext? ctx)
        {
            var c = new ActionCatalog(Spaces());
            c.AddShared(SharedInspect());
            c.AddShared(SharedView());
            c.ContextProbe = () => ctx;
            c.SetActive(active);
            return c;
        }

        [Fact]
        public void Part_tabs_follow_ContextTabs_with_one_vista_and_no_empty_tab()
        {
            var part = PartProvider();
            var c = Composed(part, DocContext.Part);
            Assert.Equal(new[] { "schizzo", "feature", "parametri", "ispeziona", "vista", "documento" }, c.Tabs.Select(t => t.Id));
            Assert.Equal(2, c.Palette("vista").Count);   // Adatta condiviso + azione di dominio dello stesso id
        }

        [Fact]
        public void Auto_tabs_appear_only_with_their_state()
        {
            var part = PartProvider();
            var c = Composed(part, DocContext.Part);
            part.State = new TabState { SketchOpen = true };
            Assert.Contains("vincoli", c.Tabs.Select(t => t.Id));
            Assert.DoesNotContain("opzioni", c.Tabs.Select(t => t.Id));
            part.State = new TabState { FeatureInProgress = true };
            Assert.Equal("opzioni", c.Tabs[0].Id);
        }

        [Fact]
        public void A_tab_without_actions_is_never_shown()
        {
            var lam = new StatefulProvider();
            lam.TabList.Add(new XrTab("lamiera", "Lamiera"));
            lam.TabList.Add(new XrTab("schizzo", "Schizzo"));
            lam.TabList.Add(new XrTab("sviluppo", "Sviluppo"));
            lam.All.Add(A("l.a", "Lamiera azione", "lamiera"));
            lam.All.Add(A("l.s", "Schizzo azione", "schizzo"));   // «sviluppo» resta senza azioni
            lam.State = new TabState { SketchOpen = true };     // «vincoli» non esiste in Lamiera
            var c = Composed(lam, DocContext.SheetMetal);
            Assert.Equal(new[] { "lamiera", "schizzo", "ispeziona", "vista", "documento" }, c.Tabs.Select(t => t.Id));
        }

        [Fact]
        public void Without_context_the_fallback_shows_local_tools_vista_and_documento()
        {
            var inspect = SharedInspect();
            var c = new ActionCatalog(Spaces());
            c.AddShared(SharedView());
            c.ContextProbe = () => null;
            c.SetActive(inspect);
            Assert.Equal(new[] { "misura", "sezione", "vista", ActionCatalog.DocumentTab }, c.Tabs.Select(t => t.Id));
            Assert.Single(c.Palette("misura"));   // il fornitore attivo non si conta due volte
        }

        [Fact]
        public void Voice_reaches_shared_actions_only_where_the_context_shows_them()
        {
            var part = PartProvider();
            var assembly = new StatefulProvider();
            assembly.TabList.Add(new XrTab("componenti", "Componenti"));
            assembly.All.Add(A("a.c", "Componenti", "componenti"));
            Assert.Equal(VoiceMatchKind.NotFound, Composed(part, DocContext.Part).ResolveVoice("isola vista").Kind);
            Assert.Equal(VoiceMatchKind.Ok, Composed(assembly, DocContext.Assembly).ResolveVoice("isola vista").Kind);
            Assert.Equal(VoiceMatchKind.Ok, Composed(part, DocContext.Part).ResolveVoice("misura").Kind);
        }

        [Fact]
        public void Shared_ids_colliding_with_the_active_provider_are_rejected()
        {
            var c = new ActionCatalog(Spaces());
            c.AddShared(SharedView());
            var clash = new Provider();
            clash.All.Add(A("view.fit", "Altro", "x"));
            Assert.Throws<InvalidOperationException>(() => c.SetActive(clash));
        }
    }
}
