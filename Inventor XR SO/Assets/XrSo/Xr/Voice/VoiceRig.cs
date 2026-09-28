using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Voice;
using UnityEngine;

namespace InventorXrSo.Xr.Voice
{
    /// <summary>
    /// Assembla la voce: HttpSpeechRecognizer (PC associato) + PushToTalkController + VoiceCommandBridge +
    /// microfono + pulsante B + pannello. Non e collegato a nessun workspace: l'integratore lo crea con
    /// <see cref="Create"/> passando un <see cref="IVoiceCommandTarget"/> che usa gli stessi percorsi dei pulsanti.
    /// </summary>
    public sealed class VoiceRig : MonoBehaviour
    {
        public VoiceCommandBridge Bridge { get; private set; }
        public PushToTalkInput PushToTalk { get; private set; }
        public MicrophoneCapture Capture { get; private set; }
        public VoicePanel Panel { get; private set; }

        public static VoiceRig Create(Transform parent, Transform head, Camera eye, IHttpTransport transport, PairedServer server,
            IVoiceCommandTarget target, OVRInput.Controller dominant = OVRInput.Controller.RTouch)
        {
            var go = new GameObject("Voice");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<VoiceRig>();
            var recognizer = new HttpSpeechRecognizer(transport, server);
            rig.Bridge = new VoiceCommandBridge(target, recognizer);
            rig.Capture = go.AddComponent<MicrophoneCapture>();
            rig.PushToTalk = go.AddComponent<PushToTalkInput>();
            rig.PushToTalk.Controller = dominant;
            rig.PushToTalk.Bind(rig.Bridge.Controller);
            rig.Panel = VoicePanel.Create(parent, head);
            rig.Panel.Bind(rig.Bridge);
            if (eye != null) XrUi.MakeInteractive(rig.Panel.Canvas, eye);

            var controller = rig.Bridge.Controller;
            rig.Capture.Sink = controller.AppendAudio;
            controller.StartCapture += rig.OpenMicrophone;
            controller.StopCapture += rig.Capture.Close;
            rig.Capture.OpenFailed += () =>
            {
                controller.Interrupt(VoiceInterruption.Disconnected);   // chiude tutto senza messaggi fuorvianti
                rig.Panel.ShowNotice("Microfono non disponibile. I comandi manuali restano disponibili.");
            };
            return rig;
        }

        private void OpenMicrophone() { Capture.Open(); }

        private void LateUpdate() { Bridge?.Pump(); }

        /// <summary>Da chiamare quando cambia workspace/modalita.</summary>
        public void NotifyModeChanged() { PushToTalk.NotifyModeChanged(); Bridge.NotifyModeChanged(); }

        /// <summary>Da chiamare alla disconnessione dal PC.</summary>
        public void NotifyDisconnected() { PushToTalk.NotifyDisconnected(); Bridge.NotifyDisconnected(); }

        private void OnDestroy()
        {
            Bridge?.Dispose();
            if (Panel != null) Destroy(Panel.gameObject);
        }
    }
}
