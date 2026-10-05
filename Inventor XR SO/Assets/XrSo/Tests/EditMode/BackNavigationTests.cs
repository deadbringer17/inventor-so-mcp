using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Xr;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    /// <summary>M9-04: Torna (X tenuto / scheda Documento), suggerimento del tocco breve, nessun salvataggio, «●».</summary>
    public class BackNavigationTests
    {
        private sealed class Backend : IInspectionBackend
        {
            public readonly List<string> Activated = new List<string>();
            public Exception Fail;
            public Task<InspectionInfo> InspectAsync(DocumentState state, string occurrenceId, CancellationToken ct) => throw new NotImplementedException();
            public Task<IReadOnlyList<OpenDocument>> ListOpenAsync(CancellationToken ct) => throw new NotImplementedException();
            public Task<FaceFeatureInfo> GetFaceFeatureAsync(DocumentState state, string faceId, CancellationToken ct) => throw new NotImplementedException();
            public Task ActivateOpenAsync(string documentId, CancellationToken ct)
            {
                if (Fail != null) throw Fail;
                Activated.Add(documentId);
                return Task.CompletedTask;
            }
        }

        private NavigationStack _stack;
        private Backend _backend;
        private List<string> _hud;
        private int _resets;
        private bool _review, _online;
        private BackNavigator _nav;

        [SetUp]
        public void SetUp()
        {
            _stack = new NavigationStack();
            _stack.Reset(new NavLevel("asm", DocContext.Assembly, "Telaio.iam"));
            _stack.Push(new NavLevel("part", DocContext.Part, "Staffa.ipt", "occ1"));
            _backend = new Backend(); _hud = new List<string>(); _resets = 0; _review = false; _online = true;
            _nav = new BackNavigator(_stack, () => _review, () => _online, () => _backend, () => CancellationToken.None,
                _hud.Add, () => _resets++);
        }

        [Test]
        public void ShortTapAtRestOnlyHintsTheParentName()
        {
            _nav.Tapped();
            CollectionAssert.AreEqual(new[] { "Tieni X per tornare a Telaio.iam" }, _hud);
            Assert.AreEqual(0, _backend.Activated.Count);
            Assert.AreEqual(2, _stack.Levels.Count);
        }

        [Test]
        public void ShortTapAtTheRootSaysThereIsNoHigherLevel()
        {
            _stack.Reset(new NavLevel("asm", DocContext.Assembly, "Telaio.iam"));
            _nav.Tapped();
            CollectionAssert.AreEqual(new[] { BackNavigator.AtTop }, _hud);
        }

        [Test]
        public async Task GoBackActivatesTheParentAndLetsTheRouterPopTheStack()
        {
            await _nav.GoBackAsync();
            CollectionAssert.AreEqual(new[] { "asm" }, _backend.Activated);
            Assert.AreEqual(2, _stack.Levels.Count, "the stack is reconciled by the router when the parent scene arrives");
            Assert.AreEqual(0, _resets);
            var result = new ContextRouter(_stack).OnActiveDocument(new DocInfo("asm", "Telaio.iam", "assembly", false));
            Assert.AreEqual(RouteChange.Popped, result.Change);
            Assert.AreEqual("asm", _stack.Top.DocumentId);
        }

        [Test]
        public async Task GoBackIsBlockedWhileACadReviewIsPending()
        {
            _review = true;
            await _nav.GoBackAsync();
            Assert.AreEqual(0, _backend.Activated.Count);
            CollectionAssert.AreEqual(new[] { BackNavigator.ReviewBlocked }, _hud);
            Assert.AreEqual(2, _stack.Levels.Count);
        }

        [Test]
        public async Task GoBackAtTheRootDoesNothing()
        {
            _stack.Reset(new NavLevel("asm", DocContext.Assembly, "Telaio.iam"));
            await _nav.GoBackAsync();
            Assert.AreEqual(0, _backend.Activated.Count);
            CollectionAssert.AreEqual(new[] { BackNavigator.AtTop }, _hud);
        }

        [Test]
        public async Task ParentClosedFromTheDesktopResetsTheStackOnTheActiveDocument()
        {
            _backend.Fail = new McpToolException("inventor_activate_open_document_xr", "NOT_FOUND", "documento non aperto", new JObject());
            await _nav.GoBackAsync();
            Assert.AreEqual(1, _resets);
            Assert.AreEqual(1, _hud.Count);
            StringAssert.Contains("Telaio.iam", _hud[0]);
            StringAssert.Contains("non è più aperto", _hud[0]);
        }

        [Test]
        public async Task ConnectionLostLeavesTheStackUntouched()
        {
            _backend.Fail = new InvalidOperationException("rete");
            await _nav.GoBackAsync();
            Assert.AreEqual(0, _resets);
            Assert.AreEqual(2, _stack.Levels.Count);
            StringAssert.Contains("Connessione persa", _hud[0]);

            _backend.Fail = null; _online = false; _hud.Clear();
            await _nav.GoBackAsync();
            Assert.AreEqual(0, _backend.Activated.Count);
            StringAssert.Contains("Connessione", _hud[0]);
            Assert.AreEqual(2, _stack.Levels.Count);
        }

        [Test]
        public async Task ACancelledActivationChangesNothing()
        {
            _backend.Fail = new OperationCanceledException();
            await _nav.GoBackAsync();
            Assert.AreEqual(0, _resets); Assert.AreEqual(0, _hud.Count); Assert.AreEqual(2, _stack.Levels.Count);
        }

        [Test]
        public void ARevisionChangeMarksTheLevelDirtyAndTheBreadcrumbShowsTheDot()
        {
            var dirty = new DirtyTracker();
            dirty.Observe(new DocumentState("part", "r1", "v1"));
            Assert.IsFalse(dirty.Apply(_stack));
            Assert.AreEqual("Telaio.iam › Staffa.ipt", _stack.Breadcrumb);
            dirty.Observe(new DocumentState("part", "r2", "v2"));
            Assert.IsTrue(dirty.Apply(_stack));
            Assert.IsTrue(_stack.Top.Dirty);
            Assert.AreEqual("Telaio.iam › Staffa.ipt ●", _stack.Breadcrumb);
            dirty.MarkSaved("part");
            dirty.Apply(_stack);
            Assert.IsFalse(_stack.Top.Dirty);
        }

        [Test]
        public void AnActivationOnlyRevisionChangeIsNotDirty()
        {
            var dirty = new DirtyTracker();
            dirty.Observe(new DocumentState("part", "r1", "v1"));
            dirty.Observe(new DocumentState("part", "r2", "v1"));   // activation bumps Revision, VisualRevision unchanged
            Assert.IsFalse(dirty.IsDirty("part"));
            Assert.IsFalse(dirty.Apply(_stack));
            Assert.AreEqual("Telaio.iam › Staffa.ipt", _stack.Breadcrumb);
        }

        [Test]
        public void AVisualRevisionChangeIsDirtyAndMarkSavedResets()
        {
            var dirty = new DirtyTracker();
            dirty.Observe(new DocumentState("part", "r1", "v1"));
            dirty.Observe(new DocumentState("part", "r2", "v2"));
            Assert.IsTrue(dirty.IsDirty("part"));
            dirty.MarkSaved("part");
            Assert.IsFalse(dirty.IsDirty("part"));
            dirty.Observe(new DocumentState("part", "r3", "v3"));
            Assert.IsTrue(dirty.IsDirty("part"));
        }

        [Test]
        public void AnXrUndoRestoringTheVisualRevisionIsNotDirty()
        {
            var dirty = new DirtyTracker();
            dirty.Observe(new DocumentState("part", "r1", "v1"));
            dirty.Observe(new DocumentState("part", "r2", "v2"));
            Assert.IsTrue(dirty.IsDirty("part"));
            dirty.Observe(new DocumentState("part", "r3", "v1"));   // TryRestoreRevision: visual back to the baseline
            Assert.IsFalse(dirty.IsDirty("part"));
        }

        [Test]
        public void ANullVisualRevisionFallsBackToTheRevision()
        {
            var dirty = new DirtyTracker();
            dirty.Observe(new DocumentState("part", "r1", null));
            Assert.IsFalse(dirty.IsDirty("part"));
            dirty.Observe(new DocumentState("part", "r2", null));
            Assert.IsTrue(dirty.IsDirty("part"));
        }

        [Test]
        public void DirtyFollowsTheDocumentWhenTheStackIsRebuilt()
        {
            var dirty = new DirtyTracker();
            dirty.Observe(new DocumentState("part", "r1", "v1")); dirty.Observe(new DocumentState("part", "r2", "v2"));
            _stack.Pop();
            _stack.Push(new NavLevel("part", DocContext.Part, "Staffa.ipt", "occ1"));
            dirty.Apply(_stack);
            Assert.IsTrue(_stack.Top.Dirty);
            Assert.IsFalse(_stack.Levels[0].Dirty);
        }
    }
}
