using TMPro;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Stable hit target with a visible ray/keyboard focus border, no hover motion.</summary>
    public sealed class ThemedButton : UnityEngine.UI.Button
    {
        public UnityEngine.UI.Image Border;
        public TextMeshProUGUI Label;
        public bool Primary;

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            if (Border != null) Border.CrossFadeColor(state == SelectionState.Highlighted || state == SelectionState.Selected
                ? UiTheme.Signal : state == SelectionState.Pressed ? UiTheme.Text : UiTheme.Border,
                instant ? 0 : UiTheme.MicroSeconds, true, true);
            if (Label != null) Label.color = state == SelectionState.Disabled ? UiTheme.Text : Primary ? UiTheme.Ink : UiTheme.Text;
        }
    }
}
