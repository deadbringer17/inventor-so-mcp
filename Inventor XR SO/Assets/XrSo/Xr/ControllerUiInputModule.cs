using UnityEngine.EventSystems;
using InventorXrSo.Xr.Input;
using System.Linq;

namespace InventorXrSo.Xr
{
    /// <summary>Keep UI presses on the same right controller as the visible ray.</summary>
    public sealed class ControllerUiInputModule : OVRInputModule
    {
        public const OVRInput.Controller PointerController = OVRInput.Controller.RTouch;
        public RaycastResult CurrentHit { get; private set; }
        public IXrInputSource Source { get; set; } = new OvrInputSource();
        private bool _lastTrigger, _waitForRelease, _trackingLost, _lastPaletteTracked;
        private PointerEventData.FramePressState _pressState;

        public override void Process()
        {
            var frame = Source.Read();
            bool paletteLost = _lastPaletteTracked && !frame.PaletteTracked;
            _lastPaletteTracked = frame.PaletteTracked;
            if (paletteLost)
            {
                CancelPendingPresses(); // La tavolozza può cambiare ancoraggio in questo frame.
                _waitForRelease = true;
            }
            if (!frame.PenTracked)
            {
                if (!_trackingLost) CancelPendingPresses();
                _trackingLost = true;
                _waitForRelease = true;
                _lastTrigger = frame.PenTrigger;
                _pressState = PointerEventData.FramePressState.NotChanged;
                return;
            }
            _trackingLost = false;
            if (_waitForRelease)
            {
                _lastTrigger = frame.PenTrigger;
                _pressState = PointerEventData.FramePressState.NotChanged;
                if (frame.PenTrigger) return;
                _waitForRelease = false; // Il rilascio dopo la perdita non è un click.
            }
            else
            {
                _pressState = frame.PenTrigger && !_lastTrigger && !frame.PenGrip ? PointerEventData.FramePressState.Pressed
                    : !frame.PenTrigger && _lastTrigger ? PointerEventData.FramePressState.Released
                    : PointerEventData.FramePressState.NotChanged;
                _lastTrigger = frame.PenTrigger;
            }
            base.Process();
        }

        private void CancelPendingPresses()
        {
            // PointerUp restituisce il controllo allo stato normale; nessun Click o Drop.
            foreach (var pointer in m_VRRayPointerData.Values.Cast<PointerEventData>().Concat(m_PointerData.Values).Distinct().ToArray())
            {
                pointer.eligibleForClick = false;
                if (pointer.pointerPress != null) ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
                if (pointer.dragging && pointer.pointerDrag != null) ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
                pointer.pointerPress = null; pointer.rawPointerPress = null; pointer.pointerDrag = null; pointer.dragging = false;
            }
            CurrentHit = default;
            ClearSelection();
            m_VRRayPointerData.Clear();
        }

        public override void DeactivateModule()
        {
            CancelPendingPresses();
            _waitForRelease = true;
            base.DeactivateModule();
        }

        protected override MouseState GetMouseStateFromRaycast(UnityEngine.Transform rayOrigin)
        {
            var state = base.GetMouseStateFromRaycast(rayOrigin);
            CurrentHit = state.GetButtonState(PointerEventData.InputButton.Left).eventData.buttonData.pointerCurrentRaycast;
            return state;
        }

        protected override PointerEventData.FramePressState GetGazeButtonState()
        {
            return _pressState;
        }
    }
}
