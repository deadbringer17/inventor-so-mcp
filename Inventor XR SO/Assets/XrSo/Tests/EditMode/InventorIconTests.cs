using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public class InventorIconTests
    {
        private sealed class Provider : IActionProvider
        {
            public IReadOnlyList<XrTab> Tabs { get; } = new[] { new XrTab("t", "Schizzo") };
            public List<XrAction> Items { get; } = new List<XrAction>();
            public IEnumerable<XrAction> Actions => Items;
            public CommitBarState CommitBar => null;
            public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Items;
        }
        private Canvas _canvas;

        [Serializable] private class Manifest { public IconEntry[] icons; }
        [Serializable] private class IconEntry { public string key; public string[] actions; }
        private readonly List<GameObject> _roots = new List<GameObject>();
        [SetUp] public void SetUp() { _canvas = UiFactory.WorldCanvas(null, "test", new Vector2(400, 400)); _roots.Add(_canvas.gameObject); }
        [TearDown] public void TearDown() { foreach (var root in _roots) if (root != null) Object.DestroyImmediate(root); _roots.Clear(); }

        private static XrAction Action(string icon, bool enabled = true, System.Action invoke = null, XrActionKind kind = XrActionKind.Command)
            => new XrAction("test", "Crea schizzo", "t", () => enabled, invoke ?? (() => { }),
                () => "Offline: anteprima e modifiche CAD disabilitate.", kind: kind, icon: icon, isOn: () => true);

        private ThemedButton Button(XrAction action, float textMm = 7, float iconMm = 16)
        {
            var button = (ThemedButton)UiFactory.ActionButton(_canvas.transform, action, textMm, iconMm, () => action.TryInvoke());
            ((RectTransform)button.transform).sizeDelta = new Vector2(75.5f, 20);
            return button;
        }

        [TestCase(null)] [TestCase("")] [TestCase("missing-m10-icon")] [TestCase("../sketch")]
        public void MissingIconKeepsCompleteTextAndCallback(string key)
        {
            int calls = 0;
            var button = Button(Action(key, invoke: () => calls++));
            Assert.IsNull(button.Icon);
            Assert.IsTrue(button.Label.gameObject.activeSelf);
            Assert.AreEqual("Crea schizzo", button.Label.text);
            button.onClick.Invoke();
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void AllSelectedIconsAreLocalSpritesAndSharedAcrossRebuilds()
        {
            var sprites = Resources.LoadAll<Sprite>("InventorIcons");
            Assert.AreEqual(34, sprites.Length);
            foreach (string key in sprites.Select(s => s.name))
            {
                Assert.IsTrue(InventorIcons.TryGet(key, out var first), key);
                Assert.AreEqual(new Vector2(32, 32), first.rect.size);
                for (int i = 0; i < 100; i++)
                {
                    var button = Button(Action(key));
                    Assert.AreSame(first, button.Icon.sprite);
                    Assert.IsFalse(button.Icon.raycastTarget);
                    Assert.IsTrue(button.Icon.preserveAspect);
                    Assert.IsFalse(button.Label.gameObject.activeSelf);
                    Assert.AreEqual(new Vector2(75.5f, 20), ((RectTransform)button.transform).sizeDelta);
                    Object.DestroyImmediate(button.gameObject);
                }
            }
        }

        [Test]
        public void CompleteMappingMatchesActualProvidersAndNumericCommitValuesKeepText()
        {
            var root = new GameObject("M10 actual providers"); _roots.Add(root);
            var providers = new IActionProvider[] {
                root.AddComponent<DesignWorkspace>(), root.AddComponent<LamieraWorkspace>(),
                root.AddComponent<AssemblyWorkspace>(), root.AddComponent<InspectWorkspace>(),
                new DocumentActions(new NavigationStack(), () => { }, () => { }, () => { }, () => { }, () => { }, () => { },
                    () => false, () => true, () => Array.Empty<OpenDocument>(), id => { }),
                new ViewActions(() => true, () => { }, () => true, on => { })
            };
            var actions = providers.SelectMany(p => p.Actions).ToArray();
            var manifest = JsonUtility.FromJson<Manifest>(Resources.Load<TextAsset>("InventorIconsManifest").text);
            Assert.AreEqual(34, manifest.icons.Length);
            Assert.AreEqual(46, manifest.icons.Sum(i => i.actions.Length));
            foreach (var entry in manifest.icons)
                foreach (var id in entry.actions)
                {
                    var action = actions.Single(a => a.Id == id);
                    Assert.AreEqual(entry.key, action.Icon, id);
                    Assert.IsTrue(InventorIcons.TryGet(action.Icon, out _), id);
                }
            Assert.IsTrue(actions.Where(a => a.Kind == XrActionKind.Numeric || a.Tab == ActionCatalog.CommitTab).All(a => string.IsNullOrEmpty(a.Icon)));
            Assert.IsEmpty(actions.Single(a => a.Id == DocumentActions.IdCalibrate).Icon);
            Assert.IsEmpty(actions.Single(a => a.Id == ViewActions.IdLegend).Icon);
            Assert.IsTrue(Resources.Load<TextAsset>("InventorIconsNotice").text.Contains("Autodesk"));
            // A selected CAD name is information, so both scoped checks must keep the visible label.
            var inspect = (InspectWorkspace)providers[3];
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var node = SceneNode.FromJson(new JObject { ["name"] = "Piastra A", ["occurrence_id"] = "part-a" });
            typeof(InspectWorkspace).GetField("_scopeSelection", flags).SetValue(inspect, true);
            typeof(InspectWorkspace).GetField("_selected", flags).SetValue(inspect, node);
            typeof(InspectWorkspace).GetField("_distanceA", flags).SetValue(inspect, node);
            typeof(InspectWorkspace).GetField("_actions", flags).SetValue(inspect, null);
            foreach (string id in new[] { InspectWorkspace.IdInterference, InspectWorkspace.IdDistance })
            {
                var scoped = inspect.Actions.Single(a => a.Id == id);
                var button = Button(scoped);
                Assert.IsNull(button.Icon, id);
                Assert.IsTrue(button.Label.gameObject.activeSelf, id);
                StringAssert.Contains("Piastra A", button.Label.text, id);
            }
        }

        [TestCase(7f, 16f)] [TestCase(14f, 32f)]
        public void DisabledIconHoverExplainsReasonWithoutInvokingAndTooltipFits(float textMm, float iconMm)
        {
            int calls = 0;
            var button = Button(Action("sketch", false, () => calls++), textMm, iconMm);
            Assert.IsFalse(button.interactable);
            Assert.Less(button.Icon.color.a, 1);
            var hint = button.GetComponent<ActionTooltip>();
            hint.OnPointerEnter(null);
            Assert.IsTrue(hint.Visible);
            StringAssert.Contains("Crea schizzo", hint.Text);
            StringAssert.Contains("Offline", hint.Text);
            button.onClick.Invoke();
            Assert.AreEqual(0, calls);
            var text = _canvas.GetComponentsInChildren<TextMeshProUGUI>(false).Single();
            Canvas.ForceUpdateCanvases(); text.ForceMeshUpdate();
            Assert.GreaterOrEqual(UiTypography.CapHeight(text), textMm - 0.01f);
            Assert.IsFalse(text.enableAutoSizing);
            Assert.IsFalse(text.isTextTruncated);
            Assert.LessOrEqual(text.textBounds.size.x, text.rectTransform.rect.width + 0.01f);
            Assert.LessOrEqual(text.textBounds.size.y, text.rectTransform.rect.height + 0.01f);
            Assert.IsTrue(text.transform.parent.GetComponentsInChildren<Graphic>().All(g => !g.raycastTarget));
            hint.OnPointerExit(null);
            Assert.IsFalse(hint.Visible);
            hint.OnPointerEnter(null);
            button.gameObject.SetActive(false);
            Assert.IsFalse(hint.Visible);
            Object.DestroyImmediate(button.gameObject);
            Assert.AreEqual(0, _canvas.GetComponentsInChildren<TextMeshProUGUI>(true).Length);
        }

        [Test]
        public void IconToggleKeepsSelectedState()
        {
            var button = Button(Action("line", kind: XrActionKind.Toggle));
            Assert.IsTrue(button.Primary);
            Assert.AreEqual(UiFactory.Accent, button.colors.normalColor);
            var surface = button.Icon.transform.parent.GetComponent<Image>();
            Assert.AreEqual(UiTheme.Navy, surface.color);
            Assert.IsFalse(surface.raycastTarget);
        }

        [Test]
        public void PaletteIconAndNumericFallbackSurviveRebuildAndKeepActionIdentity()
        {
            int calls = 0;
            var provider = new Provider();
            var action = Action("sketch", invoke: () => calls++);
            provider.Items.Add(action);
            provider.Items.Add(new XrAction("height", "Altezza: 12,5 mm", "t", () => true, () => { }, kind: XrActionKind.Numeric));
            var palette = PaletteView.Create(null); _roots.Add(palette.gameObject);
            var catalog = new ActionCatalog(provider);
            palette.Render(catalog);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)palette.Canvas.transform);
            var buttons = palette.GetComponentsInChildren<ThemedButton>();
            Assert.AreEqual(2, buttons.Length);
            Assert.AreEqual(new Vector2(75.5f, 20), ((RectTransform)buttons[0].transform).sizeDelta);
            Assert.AreEqual(new Vector2(16, 16), buttons[0].Icon.rectTransform.sizeDelta);
            Assert.IsNull(buttons[1].Icon);
            Assert.AreEqual("Altezza: 12,5 mm", buttons[1].Label.text);
            Assert.IsTrue(buttons[1].Label.gameObject.activeSelf);
            var hint = buttons[0].GetComponent<ActionTooltip>();
            hint.OnPointerEnter(null);
            Assert.IsTrue(hint.Visible);
            palette.Render(catalog);
            Assert.AreEqual(0, palette.GetComponentsInChildren<ActionTooltip>().Count(t => t.Visible));
            palette.GetComponentsInChildren<ThemedButton>().First().onClick.Invoke();
            Assert.AreEqual(1, calls);
            Assert.AreSame(action, catalog.Palette("t")[0]);
            Assert.AreEqual("Crea schizzo", action.Label);
        }

        [Test]
        public void RingPreservesHitAreaAndClosesAfterIconInvocation()
        {
            int calls = 0;
            var ring = RingView.Create(null); _roots.Add(ring.gameObject);
            ring.Show(Vector3.zero, new[] { Action("sketch", invoke: () => calls++) }, null);
            var button = ring.GetComponentInChildren<ThemedButton>();
            Assert.AreEqual(RingView.ButtonMm, ((RectTransform)button.transform).sizeDelta);
            Assert.AreEqual(new Vector2(32, 32), button.Icon.rectTransform.sizeDelta);
            button.onClick.Invoke();
            Assert.AreEqual(1, calls);
            Assert.IsFalse(ring.Visible);
        }
    }
}
