using System;
using System.Threading;
using InventorXrSo.Core.Voice;
using UnityEngine;

namespace InventorXrSo.Xr.Voice
{
    /// <summary>Stato del pulsante push-to-talk e del controller, letto ad ogni frame (sostituibile nei test).</summary>
    public interface IVoiceButtonSource
    {
        bool Connected { get; }
        /// <summary>Posizione del controller tracciata: senza, il push-to-talk si interrompe.</summary>
        bool Tracked { get; }
        bool Down { get; }
        bool Held { get; }
    }

    /// <summary>OVRInput.Button.Two (B sul controller destro) sul controller configurato.</summary>
    public sealed class OvrVoiceButtonSource : IVoiceButtonSource
    {
        private readonly Func<OVRInput.Controller> _controller;
        public OvrVoiceButtonSource(Func<OVRInput.Controller> controller) { _controller = controller; }
        public bool Connected => OVRInput.IsControllerConnected(_controller());
        public bool Tracked => OVRInput.GetControllerPositionTracked(_controller());
        public bool Down => OVRInput.GetDown(OVRInput.Button.Two, _controller());
        public bool Held => OVRInput.Get(OVRInput.Button.Two, _controller());
    }

    /// <summary>
    /// Push-to-talk (decisione D3): tasto B (OVRInput.Button.Two) sul controller dominante (RTouch), tenuto
    /// premuto ~150 ms (soglia nel controller). Chiama Tick ad ogni frame. Tracking perso mentre si parla =
    /// interruzione: il pulsante deve essere rilasciato prima di una nuova pressione. Il permesso microfono si
    /// chiede alla prima pressione; se negato i comandi manuali restano intatti.
    /// </summary>
    public sealed class PushToTalkInput : MonoBehaviour
    {
        [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;

        private PushToTalkController _controller;
        private IVoiceButtonSource _source;
        private IMicrophonePermission _permission;
        private bool _pressing, _latched, _requesting;
        private int _permissionResult;   // 0 nessuno, 1 concesso, 2 negato (scritto anche da thread non principali)

        public OVRInput.Controller Controller { get => controller; set => controller = value; }
        public bool Pressing => _pressing;

        public void Bind(PushToTalkController pushToTalk, IVoiceButtonSource source = null, IMicrophonePermission permission = null)
        {
            _controller = pushToTalk;
            _source = source ?? new OvrVoiceButtonSource(() => controller);
            _permission = permission ?? new AndroidMicrophonePermission();
            _pressing = false; _latched = false; _requesting = false; _permissionResult = 0;
        }

        private void Update() { Step(); }

        /// <summary>Un frame di input. Pubblico per i test.</summary>
        public void Step()
        {
            if (_controller == null || _source == null) return;
            int result = Interlocked.Exchange(ref _permissionResult, 0);
            if (result != 0)
            {
                _requesting = false;
                if (result == 2) _controller.Interrupt(VoiceInterruption.PermissionDenied);
                // concesso: nessuna pressione retroattiva; l'utente preme di nuovo.
            }

            bool connected = _source.Connected;
            bool tracked = connected && _source.Tracked;
            bool held = connected && _source.Held;
            bool down = connected && _source.Down;

            if (_pressing)
            {
                if (!tracked) { _controller.Interrupt(VoiceInterruption.TrackingLost); _pressing = false; _latched = held; }
                else if (!held) { _controller.Release(); _pressing = false; }
            }
            else
            {
                if (_latched && !held) _latched = false;
                if (down && !_latched && tracked && !_requesting)
                {
                    if (!_permission.IsGranted)
                    {
                        _requesting = true;
                        _permission.Request(granted => Interlocked.Exchange(ref _permissionResult, granted ? 1 : 2));
                    }
                    else { _controller.Press(); _pressing = true; }
                }
            }
            _controller.Tick();
        }

        /// <summary>Cambio di modalita/workspace: chiude subito il microfono.</summary>
        public void NotifyModeChanged() { Abort(VoiceInterruption.ModeChanged); }

        /// <summary>Disconnessione dal PC.</summary>
        public void NotifyDisconnected() { Abort(VoiceInterruption.Disconnected); }

        private void Abort(VoiceInterruption reason)
        {
            if (_controller == null) return;
            _controller.Interrupt(reason);
            _pressing = false;
            _latched = _source != null && _source.Connected && _source.Held;   // serve un nuovo rilascio prima di riparlare
        }

        // Il visore perde il focus (menu di sistema, sospensione): il microfono si chiude.
        private void OnApplicationPause(bool paused) { if (paused) Abort(VoiceInterruption.TrackingLost); }
        private void OnDisable() { Abort(VoiceInterruption.ModeChanged); }
    }
}
