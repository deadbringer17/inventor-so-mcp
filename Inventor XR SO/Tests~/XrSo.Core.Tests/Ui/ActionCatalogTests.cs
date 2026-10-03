using System;
using System.Collections.Generic;
using System.Linq;
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
            p.TabList.Add(new XrTab(ActionCatalog.SpacesTab, "Spazi"));
            p.All.Add(A("spaces.inspect", "Ispeziona", ActionCatalog.SpacesTab));
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
            Assert.Equal(new[] { "sketch", ActionCatalog.SpacesTab }, c.Tabs.Select(t => t.Id));
        }

        [Fact]
        public void Without_active_provider_only_spaces_is_shown()
        {
            var c = new ActionCatalog(Spaces());
            Assert.Equal(new[] { ActionCatalog.SpacesTab }, c.Tabs.Select(t => t.Id));
            Assert.Single(c.Palette(ActionCatalog.SpacesTab));
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
            p.All.Add(A("spaces.inspect", "Altro", "t"));
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
    }
}
