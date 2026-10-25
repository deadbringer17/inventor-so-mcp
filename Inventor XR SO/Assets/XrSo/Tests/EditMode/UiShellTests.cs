using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Tests
{
    public class UiShellTests
    {
        private sealed class Provider : IActionProvider
        {
            public List<XrTab> TabList = new List<XrTab>();
            public List<XrAction> All = new List<XrAction>();
            public CommitBarState Bar = new CommitBarState();
            public IReadOnlyList<XrTab> Tabs => TabList;
            public IEnumerable<XrAction> Actions => All;
            public IEnumerable<XrAction> ContextActions(SelectionKind s) => Enumerable.Empty<XrAction>();
            public CommitBarState CommitBar => Bar;
        }

        private readonly List<GameObject> _roots = new List<GameObject>();
        private GameObject _anchor, _head;

        [SetUp]
        public void SetUp()
        {
            _anchor = new GameObject("left"); _head = new GameObject("head");
            _roots.Add(_anchor); _roots.Add(_head);
        }

        [TearDown]
        public void TearDown() { foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r); _roots.Clear(); }

        private static XrAction A(string id, string label, string tab, System.Action run = null, bool enabled = true)
            => new XrAction(id, label, tab, () => enabled, run ?? (() => { }), () => "spento");

        private UiShell Shell(ActionCatalog catalog)
        {
            var shell = UiShell.Create(_anchor.transform, _head.transform, catalog);
            _roots.Add(shell.gameObject); _roots.Add(shell.CommitBar.Canvas.gameObject); _roots.Add(shell.Hud.Canvas.gameObject);
            return shell;
        }

        private static ActionCatalog Catalog(out Provider spaces)
        {
            spaces = new Provider();
            spaces.TabList.Add(new XrTab(ActionCatalog.DocumentTab, "Documento"));
            spaces.All.Add(A("spaces.inspect", "Ispeziona", ActionCatalog.DocumentTab));
            return new ActionCatalog(spaces);
        }

        private static string[] Labels(Component root) =>
            root.GetComponentsInChildren<Button>(false).Select(b => b.GetComponentInChildren<TextMeshProUGUI>().text).ToArray();
        [Test]
        public void TextKeyboardPagesCharactersAndUsesTheSameExplicitCommitPath()
        {
            var shell=Shell(Catalog(out _));var entry=new TextEntry("name","",128);int committed=0;entry.Committed+=()=>committed++;
            shell.Palette.ShowTextKeypad(entry,"Nome parametro");
            void PressText(string label)=>shell.Palette.GetComponentsInChildren<Button>(false).Single(b=>b.GetComponentInChildren<TextMeshProUGUI>().text==label).onClick.Invoke();
            Assert.AreEqual(PaletteProfile.Sketch,shell.Palette.Layout.Profile);Assert.LessOrEqual(Labels(shell.Palette).Length,16);
            PressText("a");PressText("b");PressText("◀");PressText("c");Assert.AreEqual("acb",entry.Buffer);Assert.AreEqual(0,committed);
            PressText("tasti ›");Assert.Contains("i",Labels(shell.Palette));Assert.LessOrEqual(Labels(shell.Palette).Length,16);
            PressText("OK");Assert.AreEqual("acb",entry.Value);Assert.AreEqual(1,committed);Assert.False(shell.Palette.KeypadVisible);
        }

        [Test]
        public void PaletteFollowsTheLeftControllerAndShowsSpacesByDefault()
        {
            var shell = Shell(Catalog(out _));
            Assert.AreSame(_anchor.transform, shell.Palette.Canvas.transform.parent);
            Assert.AreEqual(ActionCatalog.DocumentTab, shell.Palette.CurrentTab);
            CollectionAssert.Contains(Labels(shell.Palette), "Ispeziona");
        }

        [Test]
        public void PaletteSitsAboveTheControllerFaceInTheSpecPose()
        {
            var shell = Shell(Catalog(out _));
            var t = shell.Palette.Canvas.transform;
            // Spec position (M6, Tavolozza): on the controller face, tilted 45 degrees toward the eyes. The wrist menu that used to
            // force it below the controller is gone, so nothing may move it from here (see AppController).
            Assert.AreEqual(new Vector3(0, 0.05f, 0.02f), t.localPosition);
            Assert.AreEqual(45f, t.localEulerAngles.x, 1e-3f);
            var source = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "XrSo/Xr/AppController.cs"));
            StringAssert.DoesNotContain("Palette.Canvas.transform.localPosition", source, "AppController must not override the palette pose");
        }

        [Test]
        public void TabsCycleAndButtonsInvokeActions()
        {
            var catalog = Catalog(out _);
            int runs = 0;
            var design = new Provider();
            design.TabList.Add(new XrTab("sketch", "Schizzo"));
            design.All.Add(A("design.sketch", "Crea schizzo", "sketch", () => runs++));
            var shell = Shell(catalog);
            catalog.SetActive(design);
            Assert.AreEqual("sketch", shell.Palette.CurrentTab);
            shell.Palette.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Crea schizzo").onClick.Invoke();
            Assert.AreEqual(1, runs);
            shell.Palette.SelectTab(+1);
            Assert.AreEqual(ActionCatalog.DocumentTab, shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1);
            Assert.AreEqual("sketch", shell.Palette.CurrentTab);
        }

        [Test]
        public void DisabledActionsAreNotInteractable()
        {
            var catalog = Catalog(out _);
            var p = new Provider();
            p.TabList.Add(new XrTab("t", "T"));
            p.All.Add(A("off", "Spento", "t", enabled: false));
            var shell = Shell(catalog);
            catalog.SetActive(p);
            Assert.IsFalse(shell.Palette.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Spento").interactable);
        }

        [Test]
        public void KeypadReplacesTheTabAndCommitsIntoTheEntry()
        {
            var shell = Shell(Catalog(out _));
            var entry = new NumericEntry("h", QuantityUnit.Millimeters, 20, 0.01, 1000);
            shell.Palette.ShowKeypad(entry);
            Assert.IsTrue(shell.Palette.KeypadVisible);
            Button Key(string t) => shell.Palette.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == t);
            Key("1").onClick.Invoke(); Key("2").onClick.Invoke(); Key(",").onClick.Invoke(); Key("5").onClick.Invoke();
            Key("OK").onClick.Invoke();
            Assert.AreEqual(12.5, entry.Value, 1e-9);
            Assert.IsFalse(shell.Palette.KeypadVisible);
        }

        [Test]
        public void HiddenCadShellStaysHiddenWhenAnAsyncPreviewOrCatalogUpdateArrives()
        {
            var catalog = Catalog(out _);
            var provider = new Provider();
            provider.All.Add(A(CommitIds.Apply, "Applica", ActionCatalog.CommitTab));
            var shell = Shell(catalog);
            catalog.SetActive(provider);
            shell.Palette.ShowKeypad(new NumericEntry("h", QuantityUnit.Millimeters, 20, 1, 1000));
            shell.SetVisible(false);
            Assert.IsFalse(shell.Palette.KeypadVisible);
            provider.Bar.Update(new CommitBarInputs(true, false, false, false, true, false, null), 0);
            Assert.IsFalse(shell.CommitBar.gameObject.activeSelf, "a delayed preview cannot reopen the confirm bar");
            catalog.NotifyChanged();
            Assert.IsFalse(shell.Palette.gameObject.activeSelf);
            Assert.IsFalse(shell.CommitBar.gameObject.activeSelf);
            shell.SetVisible(true);
            Assert.IsTrue(shell.Palette.gameObject.activeSelf);
            Assert.IsTrue(shell.CommitBar.gameObject.activeSelf);
        }

        [Test]
        public void CommitBarShowsOnlyTheAllowedButtonsAndTheRecovery()
        {
            var catalog = Catalog(out _);
            var p = new Provider();
            int applied = 0;
            p.All.Add(A(CommitIds.Preview, "Anteprima", ActionCatalog.CommitTab));
            p.All.Add(A(CommitIds.Apply, "Applica", ActionCatalog.CommitTab, () => applied++));
            p.All.Add(A(CommitIds.Cancel, "Annulla", ActionCatalog.CommitTab));
            p.All.Add(A(CommitIds.Recover, "Recupera", ActionCatalog.CommitTab));
            var shell = Shell(catalog);
            catalog.SetActive(p);

            Assert.IsFalse(shell.CommitBar.Canvas.gameObject.activeSelf, "Empty hides the bar");
            p.Bar.Update(new CommitBarInputs(true, false, false, false, false, true, null), 0);
            CollectionAssert.AreEquivalent(new[] { "Anteprima", "Annulla" }, Labels(shell.CommitBar));
            p.Bar.Update(new CommitBarInputs(true, false, false, false, true, false, null), 0);
            CollectionAssert.AreEquivalent(new[] { "Applica", "Annulla" }, Labels(shell.CommitBar));
            shell.CommitBar.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Applica").onClick.Invoke();
            Assert.AreEqual(1, applied);
            p.Bar.Update(new CommitBarInputs(true, false, true, false, true, false, null), 0);
            CollectionAssert.AreEquivalent(new[] { "Aggiorna documento" }, Labels(shell.CommitBar));
            Assert.AreEqual(UiStyle.For(CommitBarPhase.Stale), shell.CommitBar.transform.Find("Sfondo/Indicatore stato").GetComponent<Image>().color);
        }

        private static void AssertPaletteFits(PaletteView palette)
        {
            Canvas.ForceUpdateCanvases();
            var root = (RectTransform)palette.Canvas.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            var bounds = root.rect;
            var buttons = palette.GetComponentsInChildren<Button>(false);
            Assert.IsNotEmpty(buttons);
            foreach (var b in buttons)
            {
                var r = (RectTransform)b.transform;
                Assert.GreaterOrEqual(r.rect.height, 15f - 1e-3f, b.name + " height");
                var c = new Vector3[4];
                r.GetWorldCorners(c);
                foreach (var w in c)
                {
                    var l = root.InverseTransformPoint(w);
                    Assert.GreaterOrEqual(l.x, bounds.xMin - 1e-2f, b.name + " left");
                    Assert.LessOrEqual(l.x, bounds.xMax + 1e-2f, b.name + " right");
                    Assert.GreaterOrEqual(l.y, bounds.yMin - 1e-2f, b.name + " bottom");
                    Assert.LessOrEqual(l.y, bounds.yMax + 1e-2f, b.name + " top");
                }
            }
            Assert.LessOrEqual(bounds.width, 160f + 1e-3f);
            Assert.LessOrEqual(bounds.height, 110f + 1e-3f);
        }

        [Test]
        public void PaletteFitsTheCanvasWithMinimumTargetsInTabAndKeypadModes()
        {
            var catalog = Catalog(out var spaces);
            for (int i = 0; i < 7; i++) spaces.All.Add(A("s" + i, "Azione " + i, ActionCatalog.DocumentTab));
            var shell = Shell(catalog);
            catalog.NotifyChanged();
            Assert.AreEqual(8, shell.Palette.GetComponentsInChildren<Button>(false).Length);
            AssertPaletteFits(shell.Palette);
            shell.Palette.ShowKeypad(new NumericEntry("h", QuantityUnit.Millimeters, 20, 0.01, 1000));
            Assert.AreEqual(15, shell.Palette.GetComponentsInChildren<Button>(false).Length);
            AssertPaletteFits(shell.Palette);
        }

        private static void AssertLabelsNeverTruncated(PaletteView palette, int expectedButtons)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)palette.Canvas.transform);
            var buttons = palette.GetComponentsInChildren<Button>(false);
            Assert.AreEqual(expectedButtons, buttons.Length);
            foreach (var b in buttons)
            {
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                t.ForceMeshUpdate();
                var br = (RectTransform)b.transform;
                Assert.IsFalse(t.isTextTruncated, "truncated: " + t.text);
                Assert.IsFalse(t.GetParsedText().Contains("…"), "ellipsis: " + t.text);
                Assert.LessOrEqual(t.textInfo.lineCount, 2, "lines: " + t.text);
                var c = new Vector3[4];
                ((RectTransform)t.transform).GetWorldCorners(c);
                foreach (var w in c)
                {
                    var l = br.InverseTransformPoint(w);
                    Assert.GreaterOrEqual(l.x, br.rect.xMin - 1e-2f, t.text + " label left");
                    Assert.LessOrEqual(l.x, br.rect.xMax + 1e-2f, t.text + " label right");
                    Assert.GreaterOrEqual(l.y, br.rect.yMin - 1e-2f, t.text + " label bottom");
                    Assert.LessOrEqual(l.y, br.rect.yMax + 1e-2f, t.text + " label top");
                }
                // Il testo reso deve stare nel bottone, non solo il rettangolo dell'etichetta.
                Assert.LessOrEqual(t.textBounds.size.y, br.rect.height + 1e-2f, t.text + " text height");
                Assert.LessOrEqual(t.textBounds.size.x, br.rect.width + 1e-2f, t.text + " text width");
            }
        }

        [Test]
        public void PaletteLabelsAreNeverTruncated()
        {
            var catalog = Catalog(out var spaces);
            foreach (var l in new[] { "Progettazione", "Lamiera", "Assieme", "Connessione", "Crea schizzo", "Aggiorna riferimenti", "Estrusione" })
                spaces.All.Add(A("x" + l, l, ActionCatalog.DocumentTab));
            var shell = Shell(catalog);
            catalog.NotifyChanged();
            AssertLabelsNeverTruncated(shell.Palette, 8);
            AssertPaletteFits(shell.Palette);
            shell.Palette.ShowKeypad(new NumericEntry("h", QuantityUnit.Millimeters, 20, 0.01, 1000));
            AssertLabelsNeverTruncated(shell.Palette, 15);
            CollectionAssert.Contains(Labels(shell.Palette), "Annulla");
            AssertPaletteFits(shell.Palette);
        }

        [Test]
        public void SketchProfilesFitEveryPageAndKeepAllCommandsAtEveryOfferedSize()
        {
            var catalog = Catalog(out _);
            var p = new Provider();
            p.TabList.Add(new XrTab("schizzo", "Schizzo", PaletteProfile.Sketch));
            p.TabList.AddRange(SketchCommandCatalog.Tabs);
            p.TabList.AddRange(SketchRibbon.Tabs.Where(t => !p.TabList.Any(x => x.Id == t.Id)));
            PaletteView palette = null;
            p.All.AddRange(SketchRibbon.Actions(() => palette));
            var shell = Shell(catalog); palette = shell.Palette;
            catalog.SetActive(p);
            foreach (var size in new[] { new Vector2(280, 190), new Vector2(320, 220), new Vector2(360, 250) })
            {
                palette.SetSketchSize(size.x, size.y);
                palette.ShowTabGroup(SketchCommandCatalog.Tabs.Select(t => t.Id).ToArray(), true);
                var seen = new HashSet<string>();
                foreach (var tab in SketchCommandCatalog.Tabs)
                {
                    palette.ShowTab(tab.Id);
                    for (int page = 0; page < palette.PageCount; page++)
                    {
                        foreach (var a in catalog.PalettePage(tab.Id, palette.Layout, palette.Page)) seen.Add(a.Id);
                        Canvas.ForceUpdateCanvases();
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)palette.Canvas.transform);
                        var root = (RectTransform)palette.Canvas.transform;
                        foreach (var button in palette.GetComponentsInChildren<Button>(false))
                        {
                            var rect = (RectTransform)button.transform;
                            Assert.GreaterOrEqual(rect.rect.height, 15 - .01f, button.name);
                            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                            foreach (var corner in corners)
                            {
                                var local = root.InverseTransformPoint(corner);
                                Assert.True(root.rect.Contains(new Vector2(local.x, local.y)), button.name + " outside canvas");
                            }
                            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                            label.ForceMeshUpdate();
                            Assert.False(label.enableAutoSizing, label.text);
                            Assert.LessOrEqual(label.textBounds.size.x, rect.rect.width + .1f, label.text);
                            Assert.LessOrEqual(label.textBounds.size.y, rect.rect.height + .1f, label.text);
                        }
                        palette.SelectPage(1);
                    }
                }
                Assert.True(SketchCommandCatalog.Commands.All(c => seen.Contains(c.Id)), "Every native command stays reachable");
                palette.ClearTabGroup();
            }
        }

        [Test]
        public void NestedGroupsRestoreTheParentTabAndSizeChangesDoNotChangeTheHandPose()
        {
            var catalog = Catalog(out _);
            var p = new Provider();
            p.TabList.Add(new XrTab("schizzo", "Schizzo", PaletteProfile.Sketch));
            p.TabList.AddRange(SketchCommandCatalog.Tabs);
            var shell = Shell(catalog); catalog.SetActive(p);
            var palette = shell.Palette;
            palette.ShowTabGroup(new[] { SketchCommandCatalog.TabPrefix + "Crea" }, true);
            palette.ShowTabGroup(new[] { SketchCommandCatalog.FamilyPrefix + "Arco" }, true);
            Assert.True(palette.TryLeaveGroup());
            Assert.AreEqual(SketchCommandCatalog.TabPrefix + "Crea", palette.CurrentTab);
            Assert.True(palette.TryLeaveGroup()); Assert.AreEqual("schizzo", palette.CurrentTab);
            var pose = palette.transform.localPosition;
            palette.SetSketchSize(360, 250); Assert.AreEqual(pose, palette.transform.localPosition);
            var world = palette.transform.position;
            palette.PinToBench(); _anchor.transform.position += Vector3.right;
            Assert.AreEqual(world, palette.transform.position);
            palette.ReattachToHand(); Assert.AreSame(_anchor.transform, palette.transform.parent);
            Assert.AreEqual(pose, palette.transform.localPosition);
        }

        [Test]
        public void LostLeftTrackingUsesTheBenchFrameAndPreservesTextInputAndHandPreferences()
        {
            var shell = Shell(Catalog(out _));
            var palette = shell.Palette;
            var frame = WorkbenchFrame.FromHead(new InventorXrSo.Core.Backend.CadPoint(1, 1.6, 2), 90, 0.9);
            palette.SetHandPose(new Vector3(-0.08f, 0.07f, 0.03f), Quaternion.Euler(30, 0, 0));
            var offset = palette.HandOffset; var rotation = palette.HandRotation;
            var text = new TextEntry("expression", "d1 + 2 mm", 2048);int commits = 0;text.Committed += () => commits++;
            palette.ShowTextKeypad(text, "Espressione");text.Type('+');
            string buffer = text.Buffer;string tab = palette.CurrentTab;
            palette.SetHandTracking(false, frame);
            Assert.True(palette.TrackingFallback);Assert.False(palette.Pinned);Assert.IsNull(palette.transform.parent);
            Assert.AreEqual(0.965f, palette.transform.position.y, 1e-5);
            Assert.AreEqual(1.4f, palette.transform.position.x, 1e-5);
            Assert.AreEqual(2.405f, palette.transform.position.z, 1e-5);
            foreach (var size in new[] { new Vector2(280,190), new Vector2(320,220), new Vector2(360,250) })
            {
                palette.SetSketchSize(size.x,size.y);
                var corners = new Vector3[4];((RectTransform)palette.transform).GetWorldCorners(corners);
                Assert.True(corners.All(p => p.y >= frame.DeskY + .00999), "No palette corner may intersect the calibrated bench.");
            }
            var world = palette.transform.position;var orientation = palette.transform.rotation;
            _anchor.transform.SetPositionAndRotation(Vector3.one * 10, Quaternion.Euler(80, 40, 20));
            palette.SetHandTracking(false, frame);
            Assert.AreEqual(world, palette.transform.position);Assert.AreEqual(orientation, palette.transform.rotation);
            Assert.AreEqual(buffer, text.Buffer);Assert.AreEqual(tab, palette.CurrentTab);Assert.True(palette.KeypadVisible);Assert.AreEqual(0, commits);
            palette.SetHandTracking(true, null);
            Assert.False(palette.TrackingFallback);Assert.AreSame(_anchor.transform, palette.transform.parent);
            Assert.AreEqual(offset, palette.transform.localPosition);Assert.Less(Quaternion.Angle(rotation, palette.transform.localRotation), 1e-4);
            Assert.AreEqual(buffer, text.Buffer);Assert.AreEqual(0, commits);
        }

        [Test]
        public void ExplicitBenchPinSurvivesTrackingChangesAndReattachWaitsForTheHand()
        {
            var shell = Shell(Catalog(out _));var palette = shell.Palette;
            var frame = WorkbenchFrame.FromHead(new InventorXrSo.Core.Backend.CadPoint(0, 1.6, 0), 0, null);
            palette.PinToBench();var world = palette.transform.position;
            palette.SetHandTracking(false, frame);_anchor.transform.position = Vector3.one * 5;
            Assert.True(palette.Pinned);Assert.False(palette.TrackingFallback);Assert.AreEqual(world, palette.transform.position);
            palette.ReattachToHand();Assert.False(palette.Pinned);Assert.True(palette.TrackingFallback);Assert.IsNull(palette.transform.parent);
            palette.SetHandPose(new Vector3(-0.08f, 0.06f, 0.02f), Quaternion.Euler(60, 0, 0));
            world = palette.transform.position;palette.PinToBench();palette.SetHandTracking(true, null);
            Assert.True(palette.Pinned);Assert.AreEqual(world, palette.transform.position);
            palette.ReattachToHand();Assert.AreSame(_anchor.transform, palette.transform.parent);
            Assert.AreEqual(palette.HandOffset, palette.transform.localPosition);
        }

        [Test]
        public void ShellFallbackBeforeRecenterDoesNotFollowTheHeadAndUsesARecenteredFrameWhenAvailable()
        {
            var shell = Shell(Catalog(out _));var input = _anchor.AddComponent<InventorXrSo.Xr.Input.XrInput>();
            WorkbenchFrame frame = null;_head.transform.position = new Vector3(0, 1.6f, 0);
            bool held = false;
            shell.BindTracking(() => input.PaletteTracked, _head.transform, () => frame, () => held);
            shell.UpdatePaletteTracking();var fallback = shell.Palette.transform.position;
            _head.transform.position += Vector3.right;_head.transform.rotation = Quaternion.Euler(0, 80, 0);
            shell.UpdatePaletteTracking();Assert.AreEqual(fallback, shell.Palette.transform.position);
            frame = WorkbenchFrame.FromHead(new InventorXrSo.Core.Backend.CadPoint(0, 1.6, 0), 0, .8);
            shell.UpdatePaletteTracking();Assert.AreEqual(.8375f, shell.Palette.transform.position.y, 1e-5);
            input.Poll(new InventorXrSo.Xr.Input.XrInputFrame { PaletteTracked = true, PenTracked = true }, 0);
            held = true;shell.UpdatePaletteTracking();Assert.True(shell.Palette.TrackingFallback, "Recovery must not move the palette during a press.");
            held = false;
            shell.UpdatePaletteTracking();Assert.AreSame(_anchor.transform, shell.Palette.transform.parent);
        }

        [Test]
        public void UnchangedShellRefreshDoesNotRecreateButtonsButEnablementStillUpdates()
        {
            var catalog = Catalog(out var p);
            bool enabled = true;
            p.All.Add(new XrAction("dynamic", "Dinamica", ActionCatalog.DocumentTab, () => enabled, () => { }, () => "Spenta"));
            var shell = Shell(catalog);
            int count = shell.Palette.RebuildCount;
            var before = shell.Palette.GetComponentsInChildren<Button>(false);
            for (int i = 0; i < 100; i++) shell.Refresh();
            Assert.AreEqual(count, shell.Palette.RebuildCount);
            CollectionAssert.AreEqual(before, shell.Palette.GetComponentsInChildren<Button>(false));
            enabled = false; shell.Refresh();
            Assert.Greater(shell.Palette.RebuildCount, count);
            Assert.False(shell.Palette.GetComponentsInChildren<Button>(false).Single(b => b.name == "Dinamica").interactable);
        }

        [Test]
        public void HudKeepsTheStatusBadgeApi()
        {
            var shell = Shell(Catalog(out _));
            shell.Hud.SetStatus("Online · rev 42");
            shell.Hud.Flash("3 corpi omessi");
            var texts = shell.Hud.GetComponentsInChildren<TextMeshProUGUI>().Select(t => t.text).ToArray();
            CollectionAssert.Contains(texts, "Online · rev 42");
            CollectionAssert.Contains(texts, "3 corpi omessi");
        }

        [Test]
        public void HudShowsLongestMessagesInFullInsideTheCanvas()
        {
            var shell = Shell(Catalog(out _));
            shell.Hud.SetStatus("Online · rev 42");
            foreach (var msg in new[] { 12 + UiText.Omitted, UiText.SelectionFailed })
            {
                shell.Hud.Flash(msg);
                Canvas.ForceUpdateCanvases();
                var root = (RectTransform)shell.Hud.Canvas.transform;
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                var bounds = root.rect;
                foreach (var t in shell.Hud.GetComponentsInChildren<TextMeshProUGUI>(false))
                {
                    t.ForceMeshUpdate();
                    Assert.IsFalse(t.isTextTruncated, "truncated: " + t.text);
                    Assert.IsFalse(t.text.Contains("…"), "ellipsis: " + t.text);
                    var c = new Vector3[4];
                    ((RectTransform)t.transform).GetWorldCorners(c);
                    foreach (var w in c)
                    {
                        var l = root.InverseTransformPoint(w);
                        Assert.GreaterOrEqual(l.x, bounds.xMin - 1e-2f); Assert.LessOrEqual(l.x, bounds.xMax + 1e-2f);
                        Assert.GreaterOrEqual(l.y, bounds.yMin - 1e-2f); Assert.LessOrEqual(l.y, bounds.yMax + 1e-2f);
                    }
                }
            }
        }
    }
}
