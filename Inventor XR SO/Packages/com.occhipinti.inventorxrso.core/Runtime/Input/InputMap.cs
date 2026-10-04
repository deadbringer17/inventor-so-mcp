using System.Collections.Generic;

namespace InventorXrSo.Core.Input
{
    /// <summary>Stato dei controller. Priorita: Keypad > ArmedOrHandle > SketchOpen > ComponentSelected > Rest.</summary>
    public enum InputState { Rest, ComponentSelected, SketchOpen, ArmedOrHandle, Keypad }

    public enum Key { Trigger, Grip, StickRightH, StickRightV, A, B, TriggerLeft, StickLeftH, StickLeftV, X, Y, TwoGrips }

    public enum InputAction
    {
        None, Select, DoubleSelect, Point, Drag, PressKeys, MoveView, RotateView, StepChange, StepSize,
        Isolate, SnapToggle, OpenKeypad, KeypadOk, Speak, Precision, Tabs, Zoom, Suggest, Back, BackHold,
        KeypadCancel, Fit, TwoHands
    }

    public sealed class InputBinding
    {
        public InputAction Action;
        /// <summary>Etichetta di legenda, al massimo 12 caratteri; "" se senza etichetta.</summary>
        public string Label;
        /// <summary>Azione a soglia temporale (doppio Trigger, X tenuto); None se assente.</summary>
        public InputAction Secondary;

        public InputBinding(InputAction action, string label, InputAction secondary = InputAction.None)
        {
            Action = action;
            Label = label ?? "";
            Secondary = secondary;
        }
    }

    /// <summary>Unica tabella stato -> tasto -> (azione, etichetta); spec M9 §3 «Mappatura».</summary>
    public static class InputMap
    {
        private static readonly InputState[] AllStates =
            { InputState.Rest, InputState.ComponentSelected, InputState.SketchOpen, InputState.ArmedOrHandle, InputState.Keypad };

        // Colonne: Rest, ComponentSelected, SketchOpen, ArmedOrHandle, Keypad. null = "—".
        private static readonly Dictionary<Key, InputBinding[]> Table = Build();

        private static InputBinding B(InputAction a, string label, InputAction sec = InputAction.None)
            => new InputBinding(a, label, sec);

        private static Dictionary<Key, InputBinding[]> Build()
        {
            var sel = B(InputAction.Select, "Seleziona", InputAction.DoubleSelect);
            var move = B(InputAction.MoveView, "Sposta vista");
            var rot = B(InputAction.RotateView, "Ruota vista");
            var tabs = B(InputAction.Tabs, "Schede");
            var zoom = B(InputAction.Zoom, "Zoom");
            var back = B(InputAction.Back, "Indietro");
            var fit = B(InputAction.Fit, "Adatta");
            var speak = B(InputAction.Speak, "Parla");
            var two = B(InputAction.TwoHands, "Due mani");
            var prec = B(InputAction.Precision, "Precisione");

            InputBinding[] Row(InputBinding rest, InputBinding comp, InputBinding sketch, InputBinding armed, InputBinding keypad)
                => new[] { rest, comp, sketch, armed, keypad };
            InputBinding[] Same4(InputBinding b) => Row(b, b, b, b, null);

            return new Dictionary<Key, InputBinding[]>
            {
                //                     Rest   ComponentSelected  SketchOpen                              ArmedOrHandle                        Keypad
                [Key.Trigger]     = Row(sel,   sel,              B(InputAction.Point, "Punto"),          B(InputAction.Drag, "Trascina"),     B(InputAction.PressKeys, "Tasti")),
                [Key.Grip]        = Same4(move),
                [Key.StickRightH] = Row(rot,   rot,              rot,                                    B(InputAction.StepChange, "Passo +/-"), null),
                [Key.StickRightV] = Row(null,  null,             null,                                   B(InputAction.StepSize, "Passo"),    null),
                [Key.A]           = Row(null,  B(InputAction.Isolate, "Isola"), B(InputAction.SnapToggle, "Snap"), B(InputAction.OpenKeypad, "Tastierino"), B(InputAction.KeypadOk, "OK")),
                [Key.B]           = Row(speak, speak,            speak,                                  speak,                               speak),
                [Key.TriggerLeft] = Row(null,  null,             prec,                                   prec,                                null),
                [Key.StickLeftH]  = Same4(tabs),
                [Key.StickLeftV]  = Row(zoom,  zoom,             B(InputAction.Zoom, "Zoom foglio"),     zoom,                                null),
                [Key.X]           = Row(B(InputAction.Suggest, "Suggerisci", InputAction.BackHold), back, back, back, B(InputAction.KeypadCancel, "Annulla")),
                [Key.Y]           = Same4(fit),
                [Key.TwoGrips]    = Same4(two),
            };
        }

        public static InputState Resolve(bool keypad, bool armedOrHandle, bool sketch, bool componentSelected)
        {
            if (keypad) return InputState.Keypad;
            if (armedOrHandle) return InputState.ArmedOrHandle;
            if (sketch) return InputState.SketchOpen;
            if (componentSelected) return InputState.ComponentSelected;
            return InputState.Rest;
        }

        public static InputBinding Lookup(InputState state, Key key)
            => Table.TryGetValue(key, out var row) ? row[(int)state] : null;

        public static IEnumerable<(Key key, InputBinding binding)> Active(InputState state)
        {
            foreach (var kv in Table)
            {
                var b = kv.Value[(int)state];
                if (b != null) yield return (kv.Key, b);
            }
        }
    }
}
