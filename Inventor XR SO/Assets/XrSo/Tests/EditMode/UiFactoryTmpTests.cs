using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class UiFactoryTmpTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        [Test]
        public void TextUsesTheDefaultSdfFontAndTheRequestedCapHeight()
        {
            var canvas = UiFactory.WorldCanvas(null, "T", new Vector2(100, 100));
            _root = canvas.gameObject;
            var text = UiFactory.Text(canvas.transform, "Estrusione", 14);
            Assert.IsNotNull(text.font, "TMP Essential Resources non importate");
            Assert.AreEqual("Estrusione", text.text);
            Assert.AreEqual(14f, UiTypography.CapHeight(text), 0.01f);
        }

        [Test]
        public void TextButtonInvokesItsAction()
        {
            var canvas = UiFactory.WorldCanvas(null, "T", new Vector2(100, 100));
            _root = canvas.gameObject;
            int clicks = 0;
            var button = UiFactory.TextButton(canvas.transform, "Applica", UiFactory.Accent, 14, () => clicks++);
            button.onClick.Invoke();
            Assert.AreEqual(1, clicks);
            Assert.AreEqual("Applica", button.GetComponentInChildren<TextMeshProUGUI>().text);
        }

        [Test]
        public void EveryCommitPhaseHasADistinctColourExceptEmpty()
        {
            var seen = new System.Collections.Generic.HashSet<Color>();
            foreach (CommitBarPhase p in System.Enum.GetValues(typeof(CommitBarPhase)))
                if (p != CommitBarPhase.Empty) Assert.IsTrue(seen.Add(UiStyle.For(p)), p.ToString());
        }
    }
}
