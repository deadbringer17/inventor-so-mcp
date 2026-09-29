using InventorXrSo.Core.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Colori di stato M6: anteprima blu, stale ambra, errore rosso, applicato verde.</summary>
    public static class UiStyle
    {
        public static readonly Color Hover = new Color(0.35f, 0.65f, 0.95f);
        public static readonly Color Disabled = new Color(0.22f, 0.24f, 0.28f, 0.45f);
        public static readonly Color Ghost = new Color(0.25f, 0.55f, 1f, 0.35f);

        public static Color For(CommitBarPhase phase)
        {
            switch (phase)
            {
                case CommitBarPhase.Draft: return new Color(0.22f, 0.24f, 0.28f, 0.95f);
                case CommitBarPhase.Previewing: return new Color(0.16f, 0.36f, 0.72f, 0.95f);
                case CommitBarPhase.Ready: return new Color(0.18f, 0.52f, 0.32f, 0.95f);
                case CommitBarPhase.Stale: return new Color(0.85f, 0.58f, 0.10f, 0.95f);
                case CommitBarPhase.Uncertain: return new Color(0.78f, 0.18f, 0.18f, 0.95f);
                case CommitBarPhase.Error: return new Color(0.62f, 0.14f, 0.20f, 0.95f);
                case CommitBarPhase.Offline: return new Color(0.40f, 0.40f, 0.42f, 0.95f);
                case CommitBarPhase.Applied: return new Color(0.20f, 0.70f, 0.35f, 0.95f);
                default: return UiFactory.Background;
            }
        }
    }
}
