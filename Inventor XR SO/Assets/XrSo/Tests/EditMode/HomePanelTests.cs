using System;
using System.Linq;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using InventorXrSo.Xr;
using System.Collections.Generic;

namespace InventorXrSo.Tests
{
    public class HomePanelTests
    {
        private GameObject _root;

        [Test]
        public void AnUnavailableInventorDoesNotAskToChangeExperimentalFlags()
        {
            var caps = InventorXrSo.Core.Backend.CapabilitiesInfo.FromJson(
                Newtonsoft.Json.Linq.JObject.Parse("{\"server\":{\"experimental_enabled\":true},\"target\":{\"reachable\":false}}"));
            StringAssert.DoesNotContain("INVENTOR_SO_EXPERIMENTAL", UiText.ReadinessHint(caps));
            StringAssert.Contains("Inventor non è disponibile", UiText.ReadinessHint(caps));
        }

        [TearDown]
        public void TearDown() { if (_root != null) UnityEngine.Object.DestroyImmediate(_root); }

        [Test]
        public void TheKeypadBuildsTheEntryAndSubmitsIt()
        {
            _root = new GameObject("root");
            var home = HomePanel.Create(_root.transform);
            string submitted = null;
            home.PromptText(UiText.EnterPcAddress, UiText.EnterPcAddressHint, "192.168.", s => submitted = s, () => { });
            foreach (var key in new[] { "1", ".", "2", "0", "⌫", "5", UiText.KeyOk }) home.Press(key);
            Assert.AreEqual("192.168.1.25", submitted);
        }

        [Test]
        public void ActionsBecomeButtons()
        {
            _root = new GameObject("root");
            var home = HomePanel.Create(_root.transform);
            int clicks = 0;
            home.SetActions((UiText.PairWithQr, () => clicks++), (UiText.PairWithCode, () => { }));
            var buttons = home.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ToArray();
            Assert.AreEqual(2, buttons.Length);
            buttons[0].onClick.Invoke();
            Assert.AreEqual(1, clicks);
        }

        [Test]
        public void KeyboardButtonsDispatchClicksIncludingBackspaceAndOk()
        {
            _root = new GameObject("root");
            var eventsObject = new GameObject("events", typeof(EventSystem));
            eventsObject.transform.SetParent(_root.transform, false);
            var home = HomePanel.Create(_root.transform);
            string submitted = null;
            home.PromptText(UiText.EnterPcAddress, UiText.EnterPcAddressHint, "192.168.", s => submitted = s, () => { });
            var buttons = home.GetComponentsInChildren<Button>().ToDictionary(b => b.name);
            var data = new PointerEventData(eventsObject.GetComponent<EventSystem>()) { button = PointerEventData.InputButton.Left };
            foreach (var key in new[] { "1", ".", "2", "2", "8", UiText.KeyBack, "7", ":", "8", "4", "4", "3", UiText.KeyOk })
                ExecuteEvents.Execute(buttons[key].gameObject, data, ExecuteEvents.pointerClickHandler);
            Assert.AreEqual("192.168.1.227:8443", submitted);
        }
        [Test]
        public void EveryStatusHasItalianText()
        {
            foreach (SessionStatus status in Enum.GetValues(typeof(SessionStatus)))
                Assert.IsFalse(string.IsNullOrEmpty(UiText.Status(status)), status.ToString());
        }

        [Test]
        public void AddressKeyboardAndFingerprintActionsStayInsideThePanel()
        {
            _root = new GameObject("root");
            var home = HomePanel.Create(_root.transform);
            home.PromptText(UiText.EnterPcAddress, UiText.EnterPcAddressHint, "[fe80::1234]:8443", _ => { }, () => { });
            AssertButtonsInside(home);
            home.ShowMessage(UiText.CheckFingerprint, UiText.CheckFingerprintBody +
                InventorXrSo.Core.Net.CertificatePin.Display(new string('a', 64)));
            home.SetActions((UiText.FingerprintMatches, () => { }), (UiText.Cancel, () => { }));
            AssertButtonsInside(home);
        }

        private static void AssertButtonsInside(HomePanel home)
        {
            var canvas = (RectTransform)home.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(canvas);
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            foreach (var button in home.GetComponentsInChildren<Button>())
            {
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = canvas.InverseTransformPoint(corner);
                    Assert.That(point.x, Is.InRange(canvas.rect.xMin - 1, canvas.rect.xMax + 1), button.name);
                    Assert.That(point.y, Is.InRange(canvas.rect.yMin - 1, canvas.rect.yMax + 1), button.name);
                }
            }
        }
    }
}
