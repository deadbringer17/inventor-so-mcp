using System.Collections.Generic;
using System.Reflection;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InventorXrSo.Tests
{
    public class ControllerUiInputModuleTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void TrackingLossCancelsTheRealPendingPointerAndHeldRecoveryRequiresAFreshPress(bool losePen)
        {
            var root = new GameObject("UI tracking test", typeof(EventSystem));
            var target = new GameObject("Button", typeof(RectTransform), typeof(Button));
            try
            {
                var events = root.GetComponent<EventSystem>();events.sendNavigationEvents = false;
                var module = root.AddComponent<ControllerUiInputModule>();
                typeof(BaseInputModule).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(module, null);
                var source = new SyntheticInputSource();module.Source = source;
                source.Next = new XrInputFrame { PenTracked = true, PaletteTracked = true };module.Process();
                int clicks = 0;target.GetComponent<Button>().onClick.AddListener(() => clicks++);
                var pointer = new OVRPointerEventData(events) { pointerPress = target, rawPointerPress = target, eligibleForClick = true };
                var field = typeof(OVRInputModule).GetField("m_VRRayPointerData", BindingFlags.Instance | BindingFlags.NonPublic);
                var pointers = (Dictionary<int, OVRPointerEventData>)field.GetValue(module);pointers.Add(-1, pointer);
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
                source.Next = new XrInputFrame { PenTracked = !losePen, PaletteTracked = losePen, PenTrigger = true };module.Process();
                Assert.False(pointer.eligibleForClick);Assert.IsNull(pointer.pointerPress);Assert.IsNull(pointer.rawPointerPress);
                Assert.Zero(pointers.Count);Assert.Zero(clicks);Assert.False(module.CurrentHit.isValid);
                source.Next = new XrInputFrame { PenTracked = true, PaletteTracked = true, PenTrigger = true };module.Process();Assert.Zero(clicks);
                var press = typeof(ControllerUiInputModule).GetMethod("GetGazeButtonState", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.AreEqual(PointerEventData.FramePressState.NotChanged, press.Invoke(module, null));
                source.Next = new XrInputFrame { PenTracked = true, PaletteTracked = true };module.Process();Assert.Zero(clicks);
                Assert.AreEqual(PointerEventData.FramePressState.NotChanged, press.Invoke(module, null));
                source.Next = new XrInputFrame { PenTracked = true, PaletteTracked = true, PenTrigger = true };module.Process();
                Assert.AreEqual(PointerEventData.FramePressState.Pressed, press.Invoke(module, null));
                source.Next = new XrInputFrame { PenTracked = true, PaletteTracked = true };module.Process();
                Assert.AreEqual(PointerEventData.FramePressState.Released, press.Invoke(module, null));
                Assert.Zero(clicks, "The cancelled pointer must never receive Click on recovery.");
            }
            finally { Object.DestroyImmediate(target);Object.DestroyImmediate(root); }
        }
    }
}
