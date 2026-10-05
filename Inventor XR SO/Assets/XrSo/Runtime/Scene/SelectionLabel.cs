using InventorXrSo.Unity.Ui;
using TMPro;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    public enum SelectionLabelKind { None, Part, Subassembly, Face, Edge }

    /// <summary>Texts of the selection label (Italian, like the rest of the UI): pure, so the format is unit-tested.</summary>
    public static class SelectionLabelText
    {
        public const int MaxNameLength = 24;

        public static string Kind(SelectionLabelKind kind, int faceOrdinal = 0)
        {
            switch (kind)
            {
                case SelectionLabelKind.Part: return "Parte";
                case SelectionLabelKind.Subassembly: return "Sottoassieme";
                case SelectionLabelKind.Face: return faceOrdinal > 0 ? "Faccia " + faceOrdinal : "Faccia";
                case SelectionLabelKind.Edge: return "Spigolo";
                default: return "";
            }
        }

        /// <summary>«Livello 2/3»; empty when the navigation stack is unavailable (total &lt; 1) or the level is out of range.</summary>
        public static string Level(int level, int total) => total < 1 || level < 1 || level > total ? "" : "Livello " + level + "/" + total;

        /// <summary>Second line: kind, then the level when known («Parte · Livello 2/3»).</summary>
        public static string Detail(string kindText, string levelText)
        {
            if (string.IsNullOrEmpty(levelText)) return kindText ?? "";
            return string.IsNullOrEmpty(kindText) ? levelText : kindText + " · " + levelText;
        }

        /// <summary>First line: the name, cut to <see cref="MaxNameLength"/> characters with an ellipsis; blank names show «(senza nome)».</summary>
        public static string Name(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "(senza nome)";
            name = name.Trim();
            return name.Length <= MaxNameLength ? name : name.Substring(0, MaxNameLength - 1) + "…";
        }
    }

    /// <summary>Where the label goes and how big it is: pure geometry.</summary>
    public static class SelectionLabelPlacement
    {
        /// <summary>Gap (m) between the top of the highlight and the label, and how far (m) the label is pulled toward the user.</summary>
        public const float LiftMeters = 0.04f, TowardUserMeters = 0.08f;
        /// <summary>Distance (m) at which the cap height equals <see cref="SelectionLabel.CapMm"/> (the HUD distance).</summary>
        public const float ReferenceDistance = 1.2f;
        public const float MinScale = 0.6f, MaxScale = 2.5f;

        /// <summary>Half the extent of <paramref name="bounds"/> along <paramref name="up"/>.</summary>
        public static float HalfHeightAlong(Bounds bounds, Vector3 up)
        {
            var u = up.normalized; var e = bounds.extents;
            return Mathf.Abs(u.x) * e.x + Mathf.Abs(u.y) * e.y + Mathf.Abs(u.z) * e.z;
        }

        /// <summary>Above the highlight (along the head's up), shifted toward the head, so the model never covers the label.</summary>
        public static Vector3 Position(Bounds bounds, Vector3 headPosition, Vector3 headUp, float labelHeightMeters)
        {
            var up = headUp.sqrMagnitude < 1e-8f ? Vector3.up : headUp.normalized;
            var toward = headPosition - bounds.center;
            toward = toward.sqrMagnitude < 1e-8f ? Vector3.zero : toward.normalized;
            return bounds.center + up * (HalfHeightAlong(bounds, up) + LiftMeters + labelHeightMeters * 0.5f) + toward * TowardUserMeters;
        }

        /// <summary>Canvas scale factor: keeps the apparent size of the text roughly constant between 0.7 m and 3 m.</summary>
        public static float ScaleAt(float distance) => Mathf.Clamp(distance / ReferenceDistance, MinScale, MaxScale);
    }

    /// <summary>
    /// What is selected, in words, next to the highlight: a billboard canvas with the name (line 1) and the kind and level (line 2).
    /// A thin presenter: <see cref="Show"/> takes the texts and the world bounds of the highlight, so the sources of selection
    /// (inspect, assembly, design, sheet metal) stay outside and the label is unit-testable.
    /// </summary>
    public sealed class SelectionLabel : MonoBehaviour
    {
        /// <summary>Cap height (mm) of both lines, same as the HUD (<c>HudView.TextMm</c>) at <see cref="SelectionLabelPlacement.ReferenceDistance"/>.</summary>
        public const float CapMm = 14f;
        public const float WidthMm = 280f, HeightMm = 62f, AccentMm = 7f;

        private Transform _head;
        private GameObject _root;
        private Canvas _canvas;
        private TextMeshProUGUI _name, _detail;
        private Bounds _bounds;

        public bool Visible => _root != null && _root.activeSelf;
        public string NameText => _name == null ? "" : _name.text;
        public string DetailText => _detail == null ? "" : _detail.text;
        public Vector3 WorldPosition => _root == null ? Vector3.zero : _root.transform.position;
        public Transform CanvasTransform => _root == null ? null : _root.transform;
        public Bounds Bounds => _bounds;
        /// <summary>Cap height in canvas units of the name line (mm).</summary>
        public float NameCapMm => UiTypography.CapHeight(_name);
        public float DetailCapMm => UiTypography.CapHeight(_detail);

        public static SelectionLabel Create(Transform head)
        {
            var label = new GameObject("SelectionLabel").AddComponent<SelectionLabel>();
            label._head = head;
            label.Build();
            label.Hide();
            return label;
        }

        private void Build()
        {
            _canvas = UiFactory.WorldCanvas(transform, "Etichetta", new Vector2(WidthMm, HeightMm));
            _root = _canvas.gameObject;
            var bg = UiFactory.Panel(_canvas.transform, "Sfondo", new Color(UiTheme.Navy.r, UiTheme.Navy.g, UiTheme.Navy.b, 0.92f));
            UiFactory.Stretch(bg);
            // The bar in the selection colour ties the words to the highlight on the model.
            var accent = UiFactory.Panel(bg, "Accento", UiTheme.Selection);
            accent.anchorMin = Vector2.zero; accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.sizeDelta = new Vector2(AccentMm, 0f);
            accent.anchoredPosition = Vector2.zero;
            _name = Line(bg, FontStyles.Bold, UiTheme.Text, new Vector2(0f, 0.5f), Vector2.one);
            _detail = Line(bg, FontStyles.Normal, UiTheme.SecondaryText, Vector2.zero, new Vector2(1f, 0.5f));
        }

        private static TextMeshProUGUI Line(Transform parent, FontStyles style, Color color, Vector2 anchorMin, Vector2 anchorMax)
        {
            var text = UiFactory.Text(parent, "", CapMm, style);
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.rectTransform.anchorMin = anchorMin; text.rectTransform.anchorMax = anchorMax;
            text.rectTransform.offsetMin = new Vector2(AccentMm + 5f, 0f); text.rectTransform.offsetMax = new Vector2(-4f, 0f);
            return text;
        }

        /// <summary>Shows the label for a selection whose highlight occupies <paramref name="worldBounds"/>.</summary>
        public void Show(string name, string kindText, string levelText, Bounds worldBounds)
        {
            _name.text = SelectionLabelText.Name(name);
            _detail.text = SelectionLabelText.Detail(kindText, levelText);
            _bounds = worldBounds;
            _root.SetActive(true);
            Tick();
        }

        /// <summary>The highlight moved (isolation tween, scene placement): follow it without touching the texts.</summary>
        public void SetBounds(Bounds worldBounds)
        {
            _bounds = worldBounds;
            if (Visible) Tick();
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        /// <summary>Position above the highlight, canvas turned to the head (same orientation rule as the controller legend), size by distance.</summary>
        public void Tick()
        {
            if (!Visible) return;
            var tr = _root.transform;
            var up = _head == null ? Vector3.up : _head.up;
            float distance = _head == null ? SelectionLabelPlacement.ReferenceDistance : Vector3.Distance(_head.position, _bounds.center);
            float scale = SelectionLabelPlacement.ScaleAt(distance);
            tr.localScale = Vector3.one * (0.001f * scale);
            var pos = _head == null ? _bounds.center + Vector3.up * (_bounds.extents.y + SelectionLabelPlacement.LiftMeters)
                : SelectionLabelPlacement.Position(_bounds, _head.position, up, HeightMm * 0.001f * scale);
            tr.position = pos;
            if (_head != null)
            {
                var away = pos - _head.position;
                if (away.sqrMagnitude > 1e-8f) tr.rotation = Quaternion.LookRotation(away, up);
            }
        }

        private void LateUpdate() => Tick();

        private void OnDestroy()
        {
            if (_root != null) { if (Application.isPlaying) Destroy(_root); else DestroyImmediate(_root); }
        }
    }
}
