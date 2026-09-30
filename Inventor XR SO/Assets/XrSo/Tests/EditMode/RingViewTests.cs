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
    public class RingViewTests
    {
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
