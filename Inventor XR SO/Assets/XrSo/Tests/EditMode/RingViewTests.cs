using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    /// <summary>Stand-in for the shared Ispeziona provider: only the transversal Misura action (M9 ring table).</summary>
    internal sealed class MeasureStubProvider : IActionProvider
    {
        public IReadOnlyList<XrTab> Tabs { get; } = new[] { new XrTab("misura", "Misura") };
        public IEnumerable<XrAction> Actions { get; } = new[] { new XrAction(ActionCatalog.MeasureId, "Misura", "misura", () => true, () => { }) };
        public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Array.Empty<XrAction>();
        public CommitBarState CommitBar => null;
    }

    internal static class RingTable
    {
        /// <summary>The ring of (active context, selection) is exactly the ordered list of the M9 §2 table and fits MaxContext.</summary>
        public static void Assert(ActionCatalog catalog, SelectionKind selection, string[] expected)
        {
            var ids = catalog.Context(selection).Select(a => a.Id).ToArray();
            CollectionAssert.AreEqual(expected, ids, selection.ToString());
            NUnit.Framework.Assert.LessOrEqual(ids.Length, ActionCatalog.MaxContext);
        }
    }

    public class RingViewTests
    {
        [Test]
        public void M9TableRingsRenderOneButtonPerActionAndFitTheRing()
        {
            var ring = Ring();
            foreach (var ids in new[] { new[] { "a", "b", "c", "d" }, new[] { "a", "b", "c" }, new[] { "a", "b" } })
            {
                ring.Show(Vector3.zero, ids.Select(i => A(i)).ToArray(), _head.transform);
                Assert.AreEqual(ids.Length, Buttons(ring).Length);
                ring.Hide();
            }
        }

        private readonly List<GameObject> _roots = new List<GameObject>();
        private GameObject _head;

        [SetUp]
        public void SetUp() { _head = new GameObject("head"); _head.transform.position = new Vector3(0, 1.5f, 0); _roots.Add(_head); }

        [TearDown]
        public void TearDown() { foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r); _roots.Clear(); }

        private RingView Ring() { var r = RingView.Create(null); _roots.Add(r.Canvas.gameObject); return r; }

        private static XrAction A(string id, Action run = null, bool enabled = true)
            => new XrAction(id, "Az " + id, "t", () => enabled, run ?? (() => { }), () => "spento");

        private static Button[] Buttons(RingView r) => r.Canvas.GetComponentsInChildren<Button>(false);

        [Test]
        public void OperationalLabelsFitAtFourteenMillimetresWithoutOverlappingHitTargets()
        {
            var ring = Ring();
            var labels = new[] { "Progettazione", "Crea schizzo", "Estrusione", "Raccordo", "Smusso", "Proprietà" };
            ring.Show(Vector3.zero, labels.Select(s => new XrAction(s, s, "t", () => true, () => { })).ToArray(), null);
            Canvas.ForceUpdateCanvases();
            var buttons = Buttons(ring);
            foreach (var button in buttons)
            {
                var text = button.GetComponentInChildren<TextMeshProUGUI>();
                text.ForceMeshUpdate();
                Assert.GreaterOrEqual(UiTypography.CapHeight(text), 14f - 0.01f);
                Assert.IsFalse(text.enableAutoSizing);
                if (text.text == "Progettazione") Assert.AreEqual(1, text.textInfo.lineCount, "avoid breaking a workspace name inside a word");
                Assert.LessOrEqual(text.textBounds.size.x, text.rectTransform.rect.width + 0.01f, text.text);
                Assert.LessOrEqual(text.textBounds.size.y, text.rectTransform.rect.height + 0.01f, text.text);
                var rect = (RectTransform)button.transform;
                var bounds = new Rect(rect.anchoredPosition - rect.rect.size / 2, rect.rect.size);
                foreach (var other in buttons.Where(b => b != button))
                {
                    var otherRect = (RectTransform)other.transform;
                    Assert.IsFalse(bounds.Overlaps(new Rect(otherRect.anchoredPosition - otherRect.rect.size / 2, otherRect.rect.size)), "ring hit targets overlap");
                }
            }
        }

        [Test]
        public void ShowsUpToSixActionsAroundThePoint()
        {
            var ring = Ring();
            var list = Enumerable.Range(0, 6).Select(i => A("a" + i)).ToArray();
            ring.Show(new Vector3(0, 1, 0.5f), list, _head.transform);
            Assert.IsTrue(ring.Visible);
            var buttons = Buttons(ring);
            Assert.AreEqual(6, buttons.Length);
            foreach (var b in buttons)
            {
                var rect = (RectTransform)b.transform;
                Assert.GreaterOrEqual(Mathf.Min(rect.sizeDelta.x, rect.sizeDelta.y), 25f);
                Assert.AreEqual(RingView.RadiusMm, rect.anchoredPosition.magnitude, 0.01f);
            }
            Assert.AreEqual(0.5f, ring.Canvas.transform.position.z, 1e-5f);
        }

        [Test]
        public void MoreThanSixActionsIsRejected()
        {
            var ring = Ring();
            var list = Enumerable.Range(0, 7).Select(i => A("a" + i)).ToArray();
            Assert.Throws<ArgumentException>(() => ring.Show(Vector3.zero, list, _head.transform));
        }

        [Test]
        public void DisabledActionsAreNotInteractableAndDoNotRun()
        {
            var ring = Ring();
            int runs = 0;
            ring.Show(Vector3.zero, new[] { A("on"), A("off", () => runs++, false) }, _head.transform);
            var buttons = Buttons(ring);
            Assert.IsTrue(buttons.First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Az on").interactable);
            var off = buttons.First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Az off");
            Assert.IsFalse(off.interactable);
            off.onClick.Invoke();
            Assert.AreEqual(0, runs);
        }

        [Test]
        public void ClickInvokesTheActionAndHides()
        {
            var ring = Ring();
            int runs = 0;
            ring.Show(Vector3.zero, new[] { A("go", () => runs++) }, _head.transform);
            Buttons(ring)[0].onClick.Invoke();
            Assert.AreEqual(1, runs);
            Assert.IsFalse(ring.Visible);
        }

        [Test]
        public void HideClosesTheRing()
        {
            var ring = Ring();
            ring.Show(Vector3.zero, new[] { A("a") }, _head.transform);
            ring.Hide();
            Assert.IsFalse(ring.Visible);
        }
    }
}
