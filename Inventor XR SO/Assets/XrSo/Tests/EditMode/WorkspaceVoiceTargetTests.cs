using InventorXrSo.Core.Voice;
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
