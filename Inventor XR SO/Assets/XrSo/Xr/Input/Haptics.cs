using System;
using System.Collections;
using UnityEngine;

namespace InventorXrSo.Xr.Input
{
    public enum HapticPulse { Tick, Hover, Success, Error }

    /// <summary>Profili aptici M6. Sink e sostituibile nei test; di default vibra il controller.</summary>
    public static class Haptics
    {
        public static Action<HapticPulse, bool> Sink = PlayOnController;
        private static HapticsRunner _runner;

        public static void Play(HapticPulse pulse, bool pen = true) => Sink?.Invoke(pulse, pen);

        private static void PlayOnController(HapticPulse pulse, bool pen)
        {
            if (!Application.isPlaying) return;
            if (_runner == null) _runner = new GameObject("Haptics").AddComponent<HapticsRunner>();
            _runner.Play(pulse, pen ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch);
        }

        private sealed class HapticsRunner : MonoBehaviour
        {
            public void Play(HapticPulse pulse, OVRInput.Controller c) { StopAllCoroutines(); StartCoroutine(Run(pulse, c)); }

            private static IEnumerator Run(HapticPulse pulse, OVRInput.Controller c)
            {
                switch (pulse)
                {
                    case HapticPulse.Tick: yield return Buzz(c, 0.8f, 0.25f, 0.015f); break;
                    case HapticPulse.Hover: yield return Buzz(c, 0.5f, 0.15f, 0.02f); break;
                    case HapticPulse.Success:
                        yield return Buzz(c, 0.6f, 0.5f, 0.05f);
                        yield return new WaitForSecondsRealtime(0.06f);
                        yield return Buzz(c, 0.6f, 0.5f, 0.05f);
                        break;
                    case HapticPulse.Error: yield return Buzz(c, 0.3f, 0.8f, 0.3f); break;
                }
            }

            private static IEnumerator Buzz(OVRInput.Controller c, float frequency, float amplitude, float seconds)
            {
                OVRInput.SetControllerVibration(frequency, amplitude, c);
                yield return new WaitForSecondsRealtime(seconds);
                OVRInput.SetControllerVibration(0, 0, c);
            }
        }
    }
}
