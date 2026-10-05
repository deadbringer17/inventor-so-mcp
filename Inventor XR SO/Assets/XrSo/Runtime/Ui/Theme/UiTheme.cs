using InventorXrSo.Core.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>HIVE/EnerBot sRGB tokens. Unity's UI shader handles the Linear project conversion.</summary>
    public static class UiTheme
    {
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var color); return color; }
        public static readonly Color Navy = Hex("#0d2030");
        public static readonly Color Surface = Hex("#1a3a4a");
        public static readonly Color Paper = Hex("#f4f8fb");
        public static readonly Color Ink = Hex("#102235");
        public static readonly Color MutedInk = Hex("#4f6477");
        public static readonly Color Teal = Hex("#326975");
        public static readonly Color TealHover = Hex("#285660");
        public static readonly Color Signal = Hex("#fdd11b");
        public static readonly Color Text = Paper;
        public static readonly Color SecondaryText = Hex("#c0d2de");
        public static readonly Color Border = Hex("#6d929d");
        public static readonly Color Disabled = Hex("#304653");
        public static readonly Color Success = Hex("#2ee08e");
        public static readonly Color Error = Hex("#f0637c");
        public static readonly Color Preview = Hex("#70b8ff");
        public static readonly Color Ghost = new Color(0.25f, 0.55f, 1f, 0.35f);
        // 3D selection (M9 feedback): magenta is the one hue no other overlay uses (blue = preview/ghost, red = interference,
        // yellow/cyan/green = references and DOF, orange = review, teal = sketch), so a selected body is never mistaken for them.
        // Selection fills the selected body or face; SelectionOutline is the rim and the face boundary (>= 3:1 on dark and light
        // backgrounds, non-text WCAG); SelectionHover is the lighter, thinner rim under the ray before anything is selected.
        public static readonly Color Selection = Hex("#d946ef");
        public static readonly Color SelectionOutline = Hex("#c72fdf");
        public static readonly Color SelectionHover = Hex("#f0abfc");
        /// <summary>Alpha of the selected face fill over the body; the outline and the rims are opaque.</summary>
        public const float SelectionFaceAlpha = 0.72f;
        /// <summary>Rim widths as angular size (m of offset per m of distance): selected is about twice the hover rim.</summary>
        public const float SelectionRimWidth = 0.006f, SelectionHoverRimWidth = 0.0025f;
        /// <summary>Width (m) of the selected face boundary and edge lines.</summary>
        public const float SelectionLineWidth = 0.004f;
        public const float MicroSeconds = 0.18f;
        public const float SmallRadiusMm = 2f, MediumRadiusMm = 4f, LargeRadiusMm = 6f;

        public static Color Status(CommitBarPhase phase)
        {
            switch (phase)
            {
                case CommitBarPhase.Draft: return SecondaryText;
                case CommitBarPhase.Previewing: return Preview;
                case CommitBarPhase.Ready: return Success;
                case CommitBarPhase.Stale: return Signal;
                case CommitBarPhase.Uncertain: return Hex("#ff98a9");
                case CommitBarPhase.Error: return Error;
                case CommitBarPhase.Offline: return Hex("#b8c0c8");
                case CommitBarPhase.Applied: return Hex("#8aefbc");
                default: return Navy;
            }
        }

        public static float Contrast(Color foreground, Color background)
        {
            float L(Color c) => 0.2126f * Mathf.GammaToLinearSpace(c.r) +
                0.7152f * Mathf.GammaToLinearSpace(c.g) + 0.0722f * Mathf.GammaToLinearSpace(c.b);
            float a = L(foreground), b = L(background);
            return (Mathf.Max(a, b) + 0.05f) / (Mathf.Min(a, b) + 0.05f);
        }
    }
}
