using System.Collections.Generic;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Xr;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    /// <summary>M9-01/02: il documento attivo sceglie il workspace; la pila segue gli ingressi.</summary>
    public sealed class ContextNavigationTests
    {
        private NavigationStack _stack;
        private ContextWorkspaceSwitcher _sw;
        private DocContext? _active;
        private readonly List<string> _hud = new List<string>();
        private bool _allowOpen, _inSession;
        private int _opens;

        [SetUp] public void SetUp()
        {
            _stack = new NavigationStack(); _active = null; _hud.Clear(); _allowOpen = true; _inSession = true; _opens = 0;
            _sw = new ContextWorkspaceSwitcher(_stack,
                c => { if (!_allowOpen) return false; _active = c; _opens++; return true; },
                () => _active = null, _hud.Add, () => _inSession);
        }

        private static DocInfo Doc(string id, string kind, bool sm = false) => new DocInfo(id, id + "." + kind, kind, sm);

        [Test] public void AssemblyOpensOnlyAssembly()
        {
            _sw.OnDocument(Doc("a", "assembly"));
            Assert.AreEqual(DocContext.Assembly, _active); Assert.AreEqual(DocContext.Assembly, _sw.Opened);
        }

        [Test] public void PartThenSheetMetalPartSelectDesignAndLamiera()
        {
            _sw.OnDocument(Doc("p", "part")); Assert.AreEqual(DocContext.Part, _active);
            _sw.OnDocument(Doc("s", "part", true)); Assert.AreEqual(DocContext.SheetMetal, _active);
        }

        [Test] public void SameDocumentDoesNotReopen()
        {
            _sw.OnDocument(Doc("a", "assembly")); _sw.OnDocument(Doc("a", "assembly"));
            Assert.AreEqual(1, _opens);
        }

        [Test] public void SheetMetalDetectedLaterSwitchesContextOnSameDocument()
        {
            _sw.OnDocument(Doc("p", "part")); _sw.OnDocument(Doc("p", "part", true));
            Assert.AreEqual(DocContext.SheetMetal, _active); Assert.AreEqual(1, _stack.Levels.Count);
        }

        [Test] public void NotInSessionRoutesButOpensNothingUntilSync()
        {
            _inSession = false;
            _sw.OnDocument(Doc("a", "assembly"));
            Assert.IsNull(_active); Assert.AreEqual(DocContext.Assembly, _sw.Context);
            _inSession = true; _sw.Sync();
            Assert.AreEqual(DocContext.Assembly, _active);
        }

        [Test] public void EntryPushesLevelAndSwitchesWorkspace()
        {
            _sw.OnDocument(Doc("a", "assembly"));
            _sw.ExpectEntry("p", "occ1", new float[16], false);
            _sw.OnDocument(Doc("p", "part"));
            Assert.AreEqual(2, _stack.Levels.Count); Assert.AreEqual("occ1", _stack.Top.FromOccurrenceId);
            Assert.AreEqual(DocContext.Part, _active); Assert.IsEmpty(_hud);
        }

        [Test] public void SheetMetalEntryHintOpensLamieraBeforeDetection()
        {
            _sw.ExpectEntry("s", "occ", null, true);
            Assert.True(_sw.SheetMetalHint("s")); Assert.False(_sw.SheetMetalHint("x"));
        }

        [Test] public void IncoherentPcChangeResetsStackAndAnnounces()
        {
            _sw.OnDocument(Doc("a", "assembly"));
            _sw.ExpectEntry("p", "o", null, false); _sw.OnDocument(Doc("p", "part"));
            _sw.OnDocument(Doc("z", "part"));
            Assert.AreEqual(1, _stack.Levels.Count); Assert.AreEqual("z", _stack.Top.DocumentId);
            Assert.AreEqual(1, _hud.Count); StringAssert.StartsWith("Documento cambiato dal PC", _hud[0]);
        }

        [Test] public void JumpResetsStackWithoutPcNotice()
        {
            _sw.OnDocument(Doc("a", "assembly"));
            _sw.ExpectEntry("p", "o", null, false); _sw.OnDocument(Doc("p", "part"));
            _sw.ExpectJump("a"); _sw.OnDocument(Doc("a", "assembly"));
            Assert.AreEqual(1, _stack.Levels.Count); Assert.IsEmpty(_hud); Assert.AreEqual(DocContext.Assembly, _active);
        }

        [Test] public void RefusedOpenKeepsPreviousStateAndExplains()
        {
            _sw.OnDocument(Doc("a", "assembly")); _allowOpen = false;
            _sw.OnDocument(Doc("z", "part"));
            Assert.AreEqual(DocContext.Assembly, _sw.Opened);
            StringAssert.Contains("CAD", _hud[_hud.Count - 1]);
        }

        [Test] public void UnsupportedDocumentAnnouncesAndKeepsWorkspace()
        {
            _sw.OnDocument(Doc("a", "assembly")); _sw.OnDocument(Doc("d", "drawing"));
            Assert.AreEqual(DocContext.Assembly, _active); StringAssert.Contains("non supportato", _hud[0]);
        }

        [Test] public void ClearEmptiesStackAndClosesWorkspaces()
        {
            _sw.OnDocument(Doc("a", "assembly")); _sw.Clear();
            Assert.IsNull(_stack.Top); Assert.IsNull(_active); Assert.IsNull(_sw.Context);
        }
    }
}
