using InventorXrSo.Core.Voice;
using InventorXrSo.Xr.Voice;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class WorkspaceVoiceTargetTests
    {
        private sealed class Surface : IWorkspaceVoiceSurface, IWorkspacePanelVoiceSurface, IWorkspaceWristVoiceSurface
        {
            public bool Active { get; set; }
            public bool Enabled { get; set; } = true;
            public string LastInvoked, LastField; public double LastValue;
            public bool IsEnabled(string id) => Enabled;
            public bool Invoke(string id) { LastInvoked = id; return true; }
            public DictationField ArmedField { get; set; }
            public bool SetArmedField(string id, double v) { LastField = id; LastValue = v; return true; }
            public HomePanel VoicePanel { get; set; }
            public string LastWrist;
            public bool IsWristEnabled(string label) => Active && label == "Progettazione";
            public bool InvokeWrist(string label) { LastWrist = label; return IsWristEnabled(label); }
        }

        // Workspace M6 senza pannello: VoicePanel null, le etichette vengono dalle azioni dichiarate.
        private sealed class ActionSurface : IWorkspaceVoiceSurface, IWorkspacePanelVoiceSurface, IWorkspaceActionVoiceSurface
        {
            public bool Active { get; set; } = true;
            public bool IsEnabled(string id) => true;
            public bool Invoke(string id) => true;
            public DictationField ArmedField => null;
            public bool SetArmedField(string id, double v) => false;
            public HomePanel VoicePanel => null;
            public readonly System.Collections.Generic.List<string> Invoked = new System.Collections.Generic.List<string>();
            public System.Collections.Generic.IEnumerable<(string label, bool enabled)> VoiceActions { get; set; } =
                new[] { ("Estrusione", true), ("Foro", true), ("Applica", true), ("Raccordo", false), ("Linea", true), ("Linea", true) };
            public bool InvokeVoiceAction(string label) { Invoked.Add(label); return true; }
        }

        [Test]
        public void Panelless_workspace_resolves_labels_from_its_actions_and_never_voices_apply()
        {
            var surface = new ActionSurface();
            var target = new WorkspaceVoiceTarget(null, surface) { InSession = true };
            Assert.IsTrue(target.TryResolveAction("estrudi", out var extrude));
            Assert.IsTrue(target.IsEnabled(extrude.Id));
            Assert.IsTrue(target.Invoke(extrude.Id));
            Assert.AreEqual("Estrusione", surface.Invoked[0]);
            Assert.IsTrue(target.TryResolveAction("crea foro", out _));
            Assert.IsFalse(target.TryResolveAction("applica", out _), "M5-11: Apply is physical only");
            Assert.IsFalse(target.TryResolveAction("linea", out _), "homonymous labels need the pointer");
            Assert.IsTrue(target.TryResolveAction("raccordo", out var disabled));
            Assert.IsFalse(disabled.Enabled);
            Assert.IsFalse(target.Invoke(disabled.Id));
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

        [Test]
        public void Visible_panel_actions_are_contextual_and_ambiguous_or_apply_actions_are_refused()
        {
            var root = new GameObject("voice-test");
            try
            {
                var panel = HomePanel.Create(root.transform);
                int invoked = 0;
                panel.ShowMessage("Assembly", "");
                panel.SetActions(("Componenti", () => invoked++), ("Applica", () => invoked += 100),
                    ("Apri ›", () => invoked += 10), ("Apri ›", () => invoked += 10));
                var target = new WorkspaceVoiceTarget(new Surface { Active = true, VoicePanel = panel }) { InSession = true };
                Assert.IsTrue(target.TryResolveAction("componenti", out var action));
                Assert.IsTrue(action.Enabled);
                Assert.IsTrue(target.Invoke(action.Id));
                Assert.AreEqual(1, invoked);
                Assert.IsFalse(target.TryResolveAction("applica", out _));
                Assert.IsFalse(target.TryResolveAction("apri", out _));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Numeric_prompt_uses_the_same_submit_path_after_a_physical_voice_confirmation()
        {
            var root = new GameObject("number-test");
            try
            {
                var panel = HomePanel.Create(root.transform);
                string submitted = null;
                panel.PromptText("Gradi", "", "0", value => submitted = value, () => { }, "deg", -360, 360);
                var target = new WorkspaceVoiceTarget(new Surface { Active = true, VoicePanel = panel }) { InSession = true };
                var field = target.ArmedField;
                Assert.AreEqual(QuantityUnit.Degrees, field.Unit);
                target.SetField(field.Id, 12.5);
                Assert.AreEqual("12.5", submitted);
                Assert.IsNull(target.ArmedField);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Workspace_mode_can_be_selected_through_the_existing_wrist_action()
        {
            var inspect = new Surface { Active = true };
            var target = new WorkspaceVoiceTarget(null, null, null, inspect) { InSession = true };
            Assert.IsTrue(target.TryResolveAction("progettazione", out var action));
            Assert.AreEqual("wrist:Progettazione", action.Id);
            Assert.IsTrue(target.Invoke(action.Id));
            Assert.AreEqual("Progettazione", inspect.LastWrist);
        }

        [Test]
        public void Extrude_synonyms_use_the_current_visible_button()
        {
            var root = new GameObject("extrude-voice-test");
            try
            {
                var panel = HomePanel.Create(root.transform);
                int opened = 0;
                panel.ShowMessage("Progettazione", "");
                panel.SetActions(("Estrusione", () => opened++));
                var target = new WorkspaceVoiceTarget(new Surface { Active = true, VoicePanel = panel }) { InSession = true };
                Assert.IsTrue(target.TryResolveAction("estrudi", out var verb));
                Assert.IsTrue(target.TryResolveAction("estrusione", out var noun));
                Assert.AreEqual(verb.Id, noun.Id);
                Assert.IsTrue(target.Invoke(verb.Id));
                Assert.AreEqual(1, opened);
                panel.SetActions(("Estrudi schizzo", () => opened++));
                Assert.IsTrue(target.TryResolveAction("estrudi", out var sketch));
                Assert.IsTrue(target.Invoke(sketch.Id));
                Assert.AreEqual(2, opened);
                panel.SetActions(("Foro", () => opened++));
                Assert.IsFalse(target.TryResolveAction("estrudi", out _));
                Assert.IsTrue(target.TryResolveAction("crea foro", out _));
                panel.SetActions(("Mostra sviluppo", () => opened++));
                Assert.IsTrue(target.TryResolveAction("mostra sviluppo", out var show));
                Assert.IsTrue(target.Invoke(show.Id));
                Assert.AreEqual(3, opened);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
