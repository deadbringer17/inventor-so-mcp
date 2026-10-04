using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Ui;
using InventorXrSo.Xr;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    /// <summary>Scheda Documento senza dipendenze: i test dei workspace la usano come scheda fissa del catalogo.</summary>
    internal static class TestDocs
    {
        public static DocumentActions Create(NavigationStack stack = null, Action goBack = null, Action save = null,
            Func<bool> inSession = null, Func<bool> canNavigate = null, Func<IReadOnlyList<OpenDocument>> openDocs = null,
            Action<string> jumpTo = null, Action openConnection = null, Action leaveSession = null)
        {
            Action nop = () => { };
            return new DocumentActions(stack ?? new NavigationStack(), goBack ?? nop, save ?? nop, nop, nop,
                openConnection ?? nop, leaveSession ?? nop, inSession ?? (() => true), canNavigate ?? (() => true),
                openDocs ?? (() => new OpenDocument[0]), jumpTo ?? (_ => { }));
        }
    }

    public class DocumentActionsTests
    {
        private static NavigationStack Stack(int levels)
        {
            var s = new NavigationStack();
            s.Reset(new NavLevel("d0", DocContext.Assembly, "Telaio.iam"));
            for (int i = 1; i < levels; i++) s.Push(new NavLevel("d" + i, DocContext.Part, "Parte" + i + ".ipt", "occ" + i));
            return s;
        }

        [Test]
        public void DocumentTabHoldsTheFixedActionsWithinThePaletteLimit()
        {
            var d = TestDocs.Create();
            Assert.AreEqual(ActionCatalog.DocumentTab, d.Tabs.Single().Id);
            var catalog = new ActionCatalog(d);
            var items = catalog.Palette(ActionCatalog.DocumentTab);
            Assert.LessOrEqual(items.Count, ActionCatalog.MaxPalette);
            CollectionAssert.AreEquivalent(
                new[] { "doc.path", "doc.back", "doc.save", "doc.documents", "doc.recenter", "doc.calibrate", "doc.connection", "doc.exit" },
                items.Select(a => a.Id).ToArray());
            Assert.AreEqual(d.Actions.Count(), d.Actions.Select(a => a.Id).Distinct().Count(), "action ids are unique");
        }

        [Test]
        public void BackIsDisabledAtTheTopLevelWithAReason()
        {
            int back = 0;
            var stack = Stack(1);
            var catalog = new ActionCatalog(TestDocs.Create(stack, goBack: () => back++));
            var action = catalog.Find("doc.back");
            Assert.IsFalse(action.Enabled);
            Assert.AreEqual("Sei già al livello più alto.", action.DisabledReason);
            Assert.IsFalse(catalog.TryInvoke("doc.back"));
            stack.Push(new NavLevel("p", DocContext.Part, "P.ipt", "o"));
            Assert.IsTrue(action.Enabled);
            Assert.IsTrue(catalog.TryInvoke("doc.back"));
            Assert.AreEqual(1, back);
        }

        [Test]
        public void BackFollowsTheNavigationGuard()
        {
            bool can = false;
            var catalog = new ActionCatalog(TestDocs.Create(Stack(2), canNavigate: () => can));
            Assert.IsFalse(catalog.Find("doc.back").Enabled);
            Assert.IsFalse(string.IsNullOrEmpty(catalog.Find("doc.back").DisabledReason));
            can = true;
            Assert.IsTrue(catalog.Find("doc.back").Enabled);
        }

        [Test]
        public void SaveAndWorkbenchActionsNeedASession_ConnectionAndExitDoNot()
        {
            bool session = false; int saved = 0;
            var catalog = new ActionCatalog(TestDocs.Create(Stack(1), save: () => saved++, inSession: () => session));
            Assert.IsFalse(catalog.TryInvoke("doc.save"), "no session");
            Assert.IsFalse(catalog.Find("doc.recenter").Enabled);
            Assert.IsFalse(catalog.Find("doc.calibrate").Enabled);
            Assert.IsTrue(catalog.Find("doc.connection").Enabled, "connection always reachable");
            Assert.IsTrue(catalog.Find("doc.exit").Enabled);
            session = true;
            Assert.IsTrue(catalog.TryInvoke("doc.save"));
            Assert.AreEqual(1, saved);
        }

        [Test]
        public void ConnectionAndExitInvokeTheirCallbacks()
        {
            int conn = 0, exit = 0;
            var catalog = new ActionCatalog(TestDocs.Create(openConnection: () => conn++, leaveSession: () => exit++));
            Assert.IsTrue(catalog.TryInvoke("doc.connection"));
            Assert.IsTrue(catalog.TryInvoke("doc.exit"));
            Assert.AreEqual(1, conn); Assert.AreEqual(1, exit);
        }

        [Test]
        public void BreadcrumbIsAHiddenTabWithOneActionPerLevel()
        {
            var jumped = new List<string>();
            var d = TestDocs.Create(Stack(3), jumpTo: jumped.Add);
            var catalog = new ActionCatalog(d);
            Assert.IsTrue(catalog.TryInvoke("doc.path"));
            Assert.IsTrue(d.ListOpen);
            Assert.IsTrue(catalog.Tabs.All(t => !t.Id.StartsWith("_")), "the list tabs are hidden from the palette");
            var tab = catalog.FindTab(DocumentActions.CrumbTabPrefix + 0);
            Assert.IsNotNull(tab); Assert.IsTrue(tab.Hidden);
            var items = catalog.Palette(tab.Id);
            Assert.AreEqual(3 + 1, items.Count, "one action per level plus close");
            Assert.IsTrue(catalog.TryInvoke("doc.crumb.0"));
            CollectionAssert.AreEqual(new[] { "d0" }, jumped);
            Assert.IsFalse(d.ListOpen, "choosing closes the list");
        }

        [Test]
        public void OpenDocumentsArePagedWithinThePaletteLimit()
        {
            var jumped = new List<string>();
            var docs = Enumerable.Range(0, 14).Select(i => new OpenDocument("id" + i, "Doc" + i + ".ipt", "kPartDocumentObject")).ToArray();
            var d = TestDocs.Create(Stack(1), openDocs: () => docs, jumpTo: jumped.Add);
            var catalog = new ActionCatalog(d);
            Assert.IsTrue(catalog.TryInvoke("doc.documents"));
            var pages = d.Tabs.Where(t => t.Id.StartsWith(DocumentActions.OpenTabPrefix)).ToArray();
            Assert.AreEqual(3, pages.Length);
            foreach (var page in pages) Assert.LessOrEqual(catalog.Palette(page.Id).Count, ActionCatalog.MaxPalette);
            Assert.AreEqual(DocumentActions.PageSize + 1, catalog.Palette(pages[0].Id).Count);
            var ids = d.Actions.Select(a => a.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
            Assert.IsTrue(catalog.TryInvoke("doc.open.7"));
            CollectionAssert.AreEqual(new[] { "id7" }, jumped);
            Assert.IsFalse(d.ListOpen);
        }

        [Test]
        public void EmptyDocumentListDoesNotOpenAnything()
        {
            var d = TestDocs.Create();
            new ActionCatalog(d).TryInvoke("doc.documents");
            Assert.IsFalse(d.ListOpen);
        }

        [Test]
        public void CloseListRestoresTheStaticTabOnly()
        {
            var d = TestDocs.Create(Stack(2));
            var catalog = new ActionCatalog(d);
            catalog.TryInvoke("doc.path");
            Assert.IsTrue(catalog.TryInvoke("doc.list.close.0"));
            Assert.IsFalse(d.ListOpen);
            Assert.IsNull(catalog.FindTab(DocumentActions.CrumbTabPrefix + 0));
        }
    }
}
