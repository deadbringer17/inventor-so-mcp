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
            var t = new WorkspaceVoiceTarget(s);
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
            var t = new WorkspaceVoiceTarget(null);
            Assert.IsFalse(t.IsEnabled(CommandIds.Undo));
            Assert.IsFalse(t.Invoke(CommandIds.Undo));
        }

        [Test]
        public void ActiveWorkspaceRoutesCommandsFieldsAndApplyConfirmation()
        {
            var s = new Surface { Active = true, ArmedField = new DictationField("f", QuantityUnit.Degrees, 0, 90) };
            var t = new WorkspaceVoiceTarget(s);
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
            var t = new WorkspaceVoiceTarget(new Surface { Active = true, Enabled = false });
            Assert.IsFalse(t.IsEnabled(CommandIds.Flange));
            Assert.AreEqual(WorkspaceVoiceTarget.UnavailableReason, t.DisabledReason(CommandIds.Flange));
        }
    }
}
