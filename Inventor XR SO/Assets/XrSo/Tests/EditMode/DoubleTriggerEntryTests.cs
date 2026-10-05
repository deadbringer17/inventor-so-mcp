using InventorXrSo.Core.Session;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using UnityEngine;
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

        /// <summary>The scene draws leaf parts only: Assieme3 { Bolt, Sub { Inner } }. A ray on Inner hits the nested leaf id.</summary>
        private void ShowNestedScene()
        {
            _backend.KindOfB = "assembly";
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""assembly"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Test"",""definition_kind"":""assembly"",""children"":[
                {""name"":""Bolt"",""occurrence_id"":""ent_a"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",""children"":[]},
                {""name"":""Sub"",""occurrence_id"":""ent_b"",""definition_document_id"":""doc_sub"",""definition_kind"":""assembly"",""children"":[
                  {""name"":""Inner"",""occurrence_id"":""ent_nested_inner"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",
                   ""matrix_gltf"":[1,0,0,0,0,1,0,0,0,0,1,0,0.03,0,0,1],""children"":[]}]}]}}"));
            _scene = new LoadedScene(graph, new Dictionary<string, GlbModel> { ["doc_bolt"] = _backend.Model }, new Dictionary<string, string> { ["doc_bolt"] = "a_test" }, new List<string>());
            _view.Show(_scene); _workspace.SetScene(_scene);
        }

        private void AimAtNestedBody()
        {
            Physics.SyncTransforms();
            var body = _view.GetComponentsInChildren<CadBody>().First(b => b.Instance.OccurrenceId == "ent_nested_inner");
            var center = body.GetComponent<Collider>().bounds.center;
            var position = center + Vector3.back * 0.5f;
            Hand.SetPositionAndRotation(position, Quaternion.LookRotation(center - position));
            Physics.SyncTransforms();
            Assert.True(CadRaycaster.TryPick(new Ray(Hand.position, Hand.forward), 20, out var hit, out _, out _));
            Assert.AreSame(body, hit);
        }

        [Test] public async Task DoubleTriggerEntry_TwoPressesOnABodyOfASubassemblySelectAndEnterTheSubassembly()
        {
            double now = 10; _workspace.DoubleTriggerClock = () => now;
            var entries = WatchEntries();
            ShowNestedScene(); UseInput(); AimAtNestedBody();
            PressTrigger(); ReleaseTrigger();
            for (int i = 0; i < 50 && (Field<AssemblyOccurrence>("_occurrence") == null || Field<bool>("_busy")); i++) await Task.Delay(10);
            Assert.AreEqual("ent_b", Field<AssemblyOccurrence>("_occurrence")?.Id, "the ray selects the direct sub-assembly occurrence, not the nested leaf; hud: " + AllHud());
            Assert.AreEqual(0, entries.Count, "the first press only selects");
            now += 0.2; PressTrigger(); ReleaseTrigger();
            Assert.AreEqual(1, entries.Count, "the second press enters the sub-assembly; hud: " + AllHud());
            Assert.AreEqual("ent_b", entries[0].occ); Assert.AreEqual(1, _backend.Activations);
        }

        [Test] public async Task SubassemblyBodyPickSelectsTheDirectOccurrenceWithoutAnError()
        {
            ShowNestedScene(); UseInput(); AimAtNestedBody();
            PressTrigger(); ReleaseTrigger();
            for (int i = 0; i < 50 && (Field<AssemblyOccurrence>("_occurrence") == null || Field<bool>("_busy")); i++) await Task.Delay(10);
            Assert.AreEqual("ent_b", Field<AssemblyOccurrence>("_occurrence")?.Id);
            StringAssert.DoesNotContain("direct occurrence", AllHud());
        }

        // ---- flexible sub-assembly (user's model: AsmFlex is ComponentOccurrence.Flexible): entering never depends on the occurrence being editable

        [Test] public async Task FlexibleSubassembly_SummaryIsExplicitAndActionableInItalian()
        {
            _backend.KindOfB = "assembly"; _backend.FlexibleB = true;
            _hud.Clear(); await Select("ent_b");
            var hud = AllHud();
            StringAssert.Contains("Sottoassieme flessibile: Sposta e Vincola non sono disponibili.", hud);
            StringAssert.Contains("Doppio Trigger (o Apri) per entrare e modificarne i componenti; le modifiche riguardano tutte le istanze.", hud);
            StringAssert.DoesNotContain("non modificabile: flexible", hud); StringAssert.DoesNotContain("flexible", hud);
            StringAssert.DoesNotContain("Attivare la definizione", hud);
        }

        [Test] public async Task NormalSubassembly_SummaryTellsHowToEnter()
        {
            _backend.KindOfB = "assembly";
            _hud.Clear(); await Select("ent_b");
            var hud = AllHud();
            StringAssert.Contains("Doppio Trigger (o Apri) per entrare e modificarne i componenti; le modifiche riguardano tutte le istanze.", hud);
            StringAssert.DoesNotContain("flessibile", hud); StringAssert.DoesNotContain("Sposta e Vincola non sono disponibili", hud);
        }

        [Test] public async Task FlexibleSubassembly_DoubleTriggerEntersExactlyLikeANormalOne()
        {
            _backend.KindOfB = "assembly"; _backend.FlexibleB = true;
            var entries = WatchEntries();
            await Select("ent_b");
            Assert.True(_workspace.TryEnterSelected(out var reason), reason);
            Assert.IsNull(reason);
            Assert.AreEqual(1, entries.Count); Assert.AreEqual("ent_b", entries[0].occ); Assert.AreEqual("doc_bolt", entries[0].doc); Assert.False(entries[0].sheet);
            Assert.AreEqual(1, _backend.Activations); Assert.AreEqual("doc_bolt", _backend.LastActivated);
            Assert.AreEqual(0, _backend.Commits); Assert.AreEqual(0, _backend.Previews);
            StringAssert.Contains("tutte le sue istanze", AllHud());
        }

        [Test] public async Task FlexibleSubassembly_MoveConstrainJointAndIsolateAreDisabledWithAReasonButOpenStaysEnabled()
        {
            _backend.KindOfB = "assembly"; _backend.FlexibleB = true;
            await Select("ent_b");
            foreach (var id in new[] { AssemblyWorkspace.IdMove, AssemblyWorkspace.IdConstrain, AssemblyWorkspace.IdJoint, AssemblyWorkspace.IdIsolate })
            { Assert.False(Enabled(id), id); Assert.IsNotEmpty(Act(id).DisabledReason, id); }
            StringAssert.Contains("flessibile", Act(AssemblyWorkspace.IdMove).DisabledReason);
            StringAssert.Contains("Sposta", Act(AssemblyWorkspace.IdMove).DisabledReason);
            StringAssert.Contains("Vincola", Act(AssemblyWorkspace.IdConstrain).DisabledReason);
            StringAssert.Contains("Apri", Act(AssemblyWorkspace.IdIsolate).DisabledReason);
            StringAssert.DoesNotContain("flexible", Act(AssemblyWorkspace.IdMove).DisabledReason);
            Assert.True(Enabled(AssemblyWorkspace.IdOpen)); Assert.True(Enabled(AssemblyWorkspace.IdActivate));
        }

        [Test] public async Task FlexibleSubassembly_ApriEntersAndPushesALevelLikeTheDoubleTrigger()
        {
            _backend.KindOfB = "assembly"; _backend.FlexibleB = true;
            var entries = WatchEntries();
            await Select("ent_b");
            Do(AssemblyWorkspace.IdOpen);
            Assert.AreEqual(1, entries.Count, "Apri on a sub-assembly requests the entry (a new level), not a silent activation"); Assert.AreEqual("ent_b", entries[0].occ);
            Assert.AreEqual(1, _backend.Activations);
        }

        [Test] public async Task NormalSubassembly_ApriEntersAndPushesALevel()
        {
            _backend.KindOfB = "assembly";
            var entries = WatchEntries();
            await Select("ent_b");
            Assert.True(Enabled(AssemblyWorkspace.IdConstrain), "a normal sub-assembly does not block Vincola");
            Do(AssemblyWorkspace.IdOpen);
            Assert.AreEqual(1, entries.Count); Assert.AreEqual(1, _backend.Activations);
        }

        [Test] public async Task SelectingAnotherComponentAfterAFlexibleSubassemblyEnablesVincolaAgain()
        {
            _backend.KindOfB = "assembly"; _backend.FlexibleB = true;
            await Select("ent_b"); Assert.False(Enabled(AssemblyWorkspace.IdConstrain));
            await Select("ent_a"); Assert.True(Enabled(AssemblyWorkspace.IdConstrain)); Assert.True(Enabled(AssemblyWorkspace.IdMove));
        }

        [Test] public void ReasonLabelsAreShortItalian()
        {
            Assert.AreEqual("flessibile", AssemblyWorkspace.ReasonLabel("flexible")); Assert.AreEqual("adattivo", AssemblyWorkspace.ReasonLabel("adaptive"));
            Assert.AreEqual("soppresso", AssemblyWorkspace.ReasonLabel("suppressed")); Assert.AreEqual("virtuale", AssemblyWorkspace.ReasonLabel("virtual"));
            Assert.AreEqual("altro", AssemblyWorkspace.ReasonLabel("altro"));
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
