using UnityEngine.EventSystems;

namespace InventorXrSo.Xr
{
    /// <summary>Keep UI presses on the same right controller as the visible ray.</summary>
    public sealed class ControllerUiInputModule : OVRInputModule
    {
        public const OVRInput.Controller PointerController = OVRInput.Controller.RTouch;
        public RaycastResult CurrentHit { get; private set; }

        protected override MouseState GetMouseStateFromRaycast(UnityEngine.Transform rayOrigin)
        {
            var state = base.GetMouseStateFromRaycast(rayOrigin);
            CurrentHit = state.GetButtonState(PointerEventData.InputButton.Left).eventData.buttonData.pointerCurrentRaycast;
            return state;
        }

        protected override PointerEventData.FramePressState GetGazeButtonState()
        {
            // Controller.Active can resolve to Touch, where PrimaryIndexTrigger is LEFT.
            var pressed = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, PointerController)
                && !OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, PointerController);
            var released = OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, PointerController);
            if (pressed && released) return PointerEventData.FramePressState.PressedAndReleased;
            if (pressed) return PointerEventData.FramePressState.Pressed;
            if (released) return PointerEventData.FramePressState.Released;
            return PointerEventData.FramePressState.NotChanged;
        }
    }
}
