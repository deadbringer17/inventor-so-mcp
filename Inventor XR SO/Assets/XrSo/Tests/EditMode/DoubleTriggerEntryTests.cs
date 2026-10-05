using System.Collections.Generic;
using System.Threading.Tasks;
using InventorXrSo.Xr;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    /// <summary>M9-02: double Trigger on a component enters it through the existing activation path, with the same guards.</summary>
    public sealed partial class AssemblyWorkspaceTests
    {
        private List<(string doc, string occ, float[] pose, bool sheet)> WatchEntries()
        {
            var entries = new List<(string doc, string occ, float[] pose, bool sheet)>();
            _workspace.EntryRequested += (d, o, p, m) => entries.Add((d, o, p, m));
            return entries;
        }

        [Test] public async Task DoubleTriggerEntry_SelectedPartFiresEntryRequestedOnce()
        {
            var entries = WatchEntries();
            await Select();
            Assert.True(_workspace.TryEnterSelected(out var reason), reason);
            Assert.IsNull(reason);
            Assert.AreEqual(1, entries.Count); Assert.AreEqual("doc_bolt", entries[0].doc); Assert.AreEqual("ent_a", entries[0].occ); Assert.False(entries[0].sheet);
            Assert.AreEqual(1, _backend.Activations);
            Assert.AreEqual(0, _backend.Commits); Assert.AreEqual(0, _backend.Previews);
        }

        [Test] public void DoubleTriggerEntry_NoSelectionSaysSoAndDoesNothing()
        {
            var entries = WatchEntries();
            Assert.False(_workspace.TryEnterSelected(out var reason));
            StringAssert.Contains("Seleziona", reason); StringAssert.Contains("Seleziona", AllHud());
            Assert.AreEqual(0, entries.Count); Assert.AreEqual(0, _backend.Activations);
        }

        [Test] public async Task DoubleTriggerEntry_BlockedWhileACommandIsInProgress()
        {
            var entries = WatchEntries();
            await Select(); _workspace.BeginMove();
            Assert.False(_workspace.TryEnterSelected(out var reason));
            StringAssert.Contains("Comando in corso", reason); StringAssert.Contains("Comando in corso", AllHud());
            Assert.AreEqual(0, entries.Count); Assert.AreEqual(0, _backend.Activations);
        }

        [Test] public async Task DoubleTriggerEntry_BlockedWhilePreviewing()
        {
            var entries = WatchEntries();
            await Select(); _workspace.BeginMove(); await _workspace.PreviewAsync();
            Assert.False(_workspace.TryEnterSelected(out var reason));
            StringAssert.Contains("Anteprima", reason); StringAssert.Contains("Anteprima", AllHud());
            Assert.AreEqual(0, entries.Count); Assert.AreEqual(0, _backend.Activations);
        }

        [Test] public async Task DoubleTriggerEntry_BlockedDuringHandleCapture()
        {
            var entries = WatchEntries();
            UseInput(); await Select(); _workspace.BeginMove(); AimAtSelectedBody();
            PressTrigger(); Assert.True(Dragging);
            Assert.False(_workspace.TryEnterSelected(out var reason));
            StringAssert.Contains("Maniglia", reason); StringAssert.Contains("Maniglia", AllHud());
            Assert.AreEqual(0, entries.Count); Assert.AreEqual(0, _backend.Activations);
        }

        [Test] public async Task DoubleTriggerEntry_BlockedWhenCadReviewIsRequired()
        {
            var entries = WatchEntries();
            await Select(); _workspace.BeginMove(); await _workspace.PreviewAsync();
            _backend.Mutation = new TaskCompletionSource<InventorXrSo.Core.Backend.DocumentState>();
            var apply = _workspace.ApplyAsync();
            Assert.True(_workspace.RequiresCadReview);
            Assert.False(_workspace.TryEnterSelected(out var reason));
            StringAssert.Contains("Revisione CAD", reason); StringAssert.Contains("Revisione CAD", AllHud());
            Assert.AreEqual(0, entries.Count); Assert.AreEqual(0, _backend.Activations);
            _backend.Mutation.SetResult(_backend.State); await apply;
        }

        [Test] public async Task DoubleTriggerEntry_SubassemblyEntersAndRemindsThatAllInstancesChange()
        {
            _backend.KindOfB = "assembly";
            var entries = WatchEntries();
            await Select("ent_b");
            Assert.True(_workspace.TryEnterSelected(out var reason), reason);
            Assert.AreEqual(1, entries.Count); Assert.AreEqual("ent_b", entries[0].occ);
            Assert.AreEqual(1, _backend.Activations);
            StringAssert.Contains("tutte le sue istanze", AllHud());
        }

        [Test] public async Task DoubleTriggerEntry_TwoQuickPressesOnTheComponentEnterIt()
        {
            double now = 10; _workspace.DoubleTriggerClock = () => now;
            var entries = WatchEntries();
            UseInput(); await Select(); AimAtSelectedBody();
            PressTrigger(); ReleaseTrigger();
            Assert.AreEqual(0, entries.Count, "the first press only selects");
            now += 0.2; PressTrigger(); ReleaseTrigger();
            Assert.AreEqual(1, entries.Count); Assert.AreEqual(1, _backend.Activations);
            now += 0.1; PressTrigger(); ReleaseTrigger();
            Assert.AreEqual(1, entries.Count, "the third press is a first press again");
        }

        [Test] public async Task DoubleTriggerEntry_SlowPressesDoNotEnter()
        {
            double now = 10; _workspace.DoubleTriggerClock = () => now;
            var entries = WatchEntries();
            UseInput(); await Select(); AimAtSelectedBody();
            PressTrigger(); ReleaseTrigger();
            now += 0.6; PressTrigger(); ReleaseTrigger();
            Assert.AreEqual(0, entries.Count); Assert.AreEqual(0, _backend.Activations);
        }

        [Test] public async Task DoubleTriggerEntry_RingApriStaysAvailable()
        {
            await Select();
            Do(AssemblyWorkspace.IdOpen);
            Assert.True(_workspace.Isolation.Active);
            Assert.True(Enabled(AssemblyWorkspace.IdOpenDesign));
        }
    }
}
