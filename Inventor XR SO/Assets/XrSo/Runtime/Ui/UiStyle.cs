using InventorXrSo.Core.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Colori di stato M6: anteprima blu, stale ambra, errore rosso, applicato verde.</summary>
    public static class UiStyle
    {
        public static readonly Color Hover = UiTheme.Signal;
        public static readonly Color Disabled = UiTheme.Disabled;
        public static readonly Color Ghost = UiTheme.Ghost;

        public static Color For(CommitBarPhase phase)
        {
            return UiTheme.Status(phase);
        }
    }
}
