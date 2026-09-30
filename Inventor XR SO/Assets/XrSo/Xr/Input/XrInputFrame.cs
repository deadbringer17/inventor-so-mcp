using UnityEngine;

namespace InventorXrSo.Xr.Input
{
    /// <summary>Stato di entrambi i controller in un frame. Pen = destro, Palette = sinistro.</summary>
    public struct XrInputFrame
    {
        public bool PenTrigger, PenGrip, A, B, PaletteTrigger, PaletteGrip, X, Y;
        public Vector2 PenStick, PaletteStick;
        public bool PenTracked, PaletteTracked;
    }

    public interface IXrInputSource
    {
        XrInputFrame Read();
        /// <summary>True per le sorgenti dei runner: i log devono chiamare "sintetico" l'input.</summary>
        bool Synthetic { get; }
    }

    public sealed class OvrInputSource : IXrInputSource
    {
        public bool Synthetic => false;

        public XrInputFrame Read()
        {
            const OVRInput.Controller r = OVRInput.Controller.RTouch, l = OVRInput.Controller.LTouch;
            return new XrInputFrame
            {
                PenTrigger = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, r),
                PenGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, r),
                A = OVRInput.Get(OVRInput.Button.One, r),
                B = OVRInput.Get(OVRInput.Button.Two, r),
                PaletteTrigger = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, l),
                PaletteGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, l),
                X = OVRInput.Get(OVRInput.Button.One, l),
                Y = OVRInput.Get(OVRInput.Button.Two, l),
                PenStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, r),
                PaletteStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, l),
                PenTracked = Tracked(r),
                PaletteTracked = Tracked(l),
            };
        }

        private static bool Tracked(OVRInput.Controller c) => OVRInput.IsControllerConnected(c)
            && OVRInput.GetControllerPositionTracked(c) && OVRInput.GetControllerOrientationTracked(c);
    }

    public sealed class SyntheticInputSource : IXrInputSource
    {
        public XrInputFrame Next;
        public bool Synthetic => true;
        public XrInputFrame Read() => Next;
    }
}
