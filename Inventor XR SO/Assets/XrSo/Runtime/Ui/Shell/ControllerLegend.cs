using System;
using System.Collections.Generic;
using InventorXrSo.Core.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Key = InventorXrSo.Core.Input.Key;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Stato di una etichetta della legenda (letto dai test e dal runner).</summary>
    public sealed class LegendLabel
    {
        public Key Key { get; internal set; }
        /// <summary>true = controller destro, false = sinistro.</summary>
        public bool Right { get; internal set; }
        public string Text { get; internal set; } = "";
        /// <summary>Opacità applicata: bassa di base, piena col controller nel cono di sguardo.</summary>
        public float Opacity { get; internal set; }
        /// <summary>0..1 per le azioni a soglia temporale (doppio Trigger, X tenuto); 0 altrimenti.</summary>
        public float Progress { get; internal set; }
        /// <summary>false con legenda spenta o fuori sessione.</summary>
        public bool Visible { get; internal set; }
        /// <summary>Azione a soglia temporale dell'etichetta (None se assente).</summary>
        public InputAction Secondary { get; internal set; }
    }

    /// <summary>
    /// Legenda 3D (spec M9 §3): un'etichetta per ogni tasto attivo nello stato (da <see cref="InputMap.Active"/>), ancorata al modello
    /// del controller. Opacità bassa di base, piena entro 25° dall'asse della testa; anellino di avanzamento per doppio Trigger e X tenuto.
    /// Nessuna animazione: lo stato e l'opacità si applicano nello stesso frame.
    /// </summary>
    public sealed class ControllerLegend : MonoBehaviour
    {
        /// <summary>Stessa chiave di ViewActions.LegendPrefKey (Runtime non vede Xr; un test le tiene allineate).</summary>
        public const string PrefKey = "xrso.legend";
        public const float LabelWidthMm = 46f, LabelHeightMm = 11f, TextCapMm = 5f, RingMm = 8f;

        /// <summary>
        /// Offset locali (m) di ogni etichetta rispetto all'ancora del controller (x destra, y su, z avanti del controller).
        /// APPROSSIMATI: le etichette stanno in colonna sul lato esterno, ordinate come i tasti (stick, tasti facciali, trigger, grip),
        /// per non sovrapporsi. Da tarare con la prova fisica (M9-07 resta aperto sul visore).
        /// </summary>
        public static readonly IReadOnlyDictionary<Key, Vector3> RightOffsets = new Dictionary<Key, Vector3>
        {
            [Key.StickRightH] = new Vector3(0.075f, 0.070f, 0.015f),
            [Key.StickRightV] = new Vector3(0.075f, 0.056f, 0.015f),
            [Key.A] = new Vector3(0.075f, 0.042f, 0.010f),
            [Key.B] = new Vector3(0.075f, 0.028f, 0.010f),
            [Key.Trigger] = new Vector3(0.075f, 0.010f, 0.040f),
            [Key.Grip] = new Vector3(0.075f, -0.020f, -0.020f),
        };

        public static readonly IReadOnlyDictionary<Key, Vector3> LeftOffsets = new Dictionary<Key, Vector3>
        {
            [Key.StickLeftH] = new Vector3(-0.075f, 0.070f, 0.015f),
            [Key.StickLeftV] = new Vector3(-0.075f, 0.056f, 0.015f),
            [Key.X] = new Vector3(-0.075f, 0.042f, 0.010f),
            [Key.Y] = new Vector3(-0.075f, 0.028f, 0.010f),
            [Key.TriggerLeft] = new Vector3(-0.075f, 0.010f, 0.040f),
            [Key.TwoGrips] = new Vector3(-0.075f, -0.020f, -0.020f),
        };

        private sealed class View
        {
            public LegendLabel Label;
            public Transform Anchor;
            public GameObject Root;
            public CanvasGroup Group;
            public TextMeshProUGUI Text;
            public Image Ring;
            public GameObject RingRoot;
        }

        private static Sprite _ringSprite;

        private readonly Dictionary<Key, View> _views = new Dictionary<Key, View>();
        private readonly List<LegendLabel> _labels = new List<LegendLabel>();
        private readonly List<GameObject> _roots = new List<GameObject>();
        private Transform _head, _left, _right;
        private InputState _state = InputState.Rest;
        private bool _stateSet, _enabled = true, _shown = true;

        /// <summary>Etichette dello stato corrente (= <see cref="InputMap.Active"/>), anche se nascoste (vedi <see cref="LegendLabel.Visible"/>).</summary>
        public IReadOnlyList<LegendLabel> Labels => _labels;
        public InputState State => _state;

        /// <summary>Avanzamento 0..1 del doppio Trigger (es. AssemblyWorkspace.DoubleTriggerProgress); null = nessuno.</summary>
        public Func<float> TriggerProgress { get; set; }
        /// <summary>Avanzamento 0..1 di X tenuto (XrInput.BackHoldProgress); null = nessuno.</summary>
        public Func<float> BackProgress { get; set; }

        /// <summary>Legenda accesa (scheda Vista); la scelta resta in PlayerPrefs (ViewActions.LegendPrefKey, default acceso).</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Tick();
            }
        }

        /// <summary>false a Home / fuori sessione: nasconde tutto senza toccare la preferenza.</summary>
        public bool Shown
        {
            get => _shown;
            set { if (_shown == value) return; _shown = value; Tick(); }
        }

        public static ControllerLegend Create(Transform head, Transform leftAnchor, Transform rightAnchor)
        {
            var legend = new GameObject("ControllerLegend").AddComponent<ControllerLegend>();
            legend._head = head; legend._left = leftAnchor; legend._right = rightAnchor;
            legend._enabled = PlayerPrefs.GetInt(PrefKey, 1) != 0;
            legend.SetState(InputState.Rest);
            return legend;
        }

        /// <summary>Applica lo stato subito (stesso frame). Stesso stato = nessun lavoro.</summary>
        public void SetState(InputState state)
        {
            if (_stateSet && state == _state) return;
            _state = state; _stateSet = true;
            _labels.Clear();
            foreach (var v in _views.Values) v.Root.SetActive(false);
            foreach (var (key, binding) in InputMap.Active(state))
            {
                var view = GetView(key);
                view.Label.Text = LegendVisibility.Fit(binding.Label);
                view.Label.Secondary = binding.Secondary;
                view.Text.text = view.Label.Text;
                _labels.Add(view.Label);
            }
            Tick();
        }

        /// <summary>Aggiorna opacità, anellini e visibilità; chiamato ogni frame e dopo ogni cambio.</summary>
        public void Tick()
        {
            bool show = _enabled && _shown;
            foreach (var label in _labels)
            {
                var view = _views[label.Key];
                label.Visible = show;
                view.Root.SetActive(show);
                if (!show) continue;
                label.Opacity = LegendVisibility.Opacity(AngleToHead(view.Anchor));
                label.Progress = ProgressOf(label);
                view.Group.alpha = label.Opacity;
                bool ring = label.Progress > 0f;
                view.RingRoot.SetActive(ring);
                if (ring) view.Ring.fillAmount = label.Progress;
                if (_head != null)
                {
                    var away = view.Root.transform.position - _head.position;
                    if (away.sqrMagnitude > 1e-8f) view.Root.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
                }
            }
        }

        private float ProgressOf(LegendLabel label)
        {
            if (label.Secondary == InputAction.None) return 0f;
            Func<float> probe = label.Key == Key.Trigger ? TriggerProgress : label.Key == Key.X ? BackProgress : null;
            return probe == null ? 0f : Mathf.Clamp01(probe());
        }

        /// <summary>Angolo (gradi) tra l'asse della testa e la direzione verso l'ancora; 180 se non calcolabile.</summary>
        public float AngleToHead(Transform anchor)
        {
            if (_head == null || anchor == null) return 180f;
            var dir = anchor.position - _head.position;
            return dir.sqrMagnitude < 1e-8f ? 0f : Vector3.Angle(_head.forward, dir);
        }

        private void Update() => Tick();

        private void OnDestroy()
        {
            foreach (var root in _roots)
            {
                if (root == null) continue;
                if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
            }
            _roots.Clear();
        }

        // --- viste ---

        private View GetView(Key key)
        {
            if (_views.TryGetValue(key, out var existing)) return existing;
            bool right = RightOffsets.ContainsKey(key);
            var anchor = right ? _right : _left;
            var offsets = right ? RightOffsets : LeftOffsets;
            offsets.TryGetValue(key, out var offset);

            var canvas = UiFactory.WorldCanvas(anchor, "Legend." + key, new Vector2(LabelWidthMm, LabelHeightMm));
            canvas.transform.localPosition = offset;
            _roots.Add(canvas.gameObject);
            var group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            var bg = UiFactory.Panel(canvas.transform, "Sfondo", new Color(UiTheme.Navy.r, UiTheme.Navy.g, UiTheme.Navy.b, 0.85f));
            UiFactory.Stretch(bg);

            var ringRoot = new GameObject("Anello", typeof(RectTransform), typeof(Image));
            ringRoot.transform.SetParent(bg, false);
            var ringRect = (RectTransform)ringRoot.transform;
            ringRect.anchorMin = ringRect.anchorMax = new Vector2(0f, 0.5f);
            ringRect.pivot = new Vector2(0f, 0.5f);
            ringRect.sizeDelta = new Vector2(RingMm, RingMm);
            ringRect.anchoredPosition = new Vector2(2f, 0f);
            var ring = ringRoot.GetComponent<Image>();
            ring.sprite = RingSprite();
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.color = UiTheme.Signal;
            ring.raycastTarget = false;
            ringRoot.SetActive(false);

            var text = UiFactory.Text(bg, "", TextCapMm, FontStyles.Bold);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(RingMm + 4f, 0f); text.rectTransform.offsetMax = new Vector2(-2f, 0f);

            var view = new View
            {
                Label = new LegendLabel { Key = key, Right = right },
                Anchor = anchor, // l'angolo di sguardo si misura sul controller, non sull'etichetta
                Root = canvas.gameObject, Group = group, Text = text, Ring = ring, RingRoot = ringRoot,
            };
            _views[key] = view;
            return view;
        }

        private static Sprite RingSprite()
        {
            if (_ringSprite != null) return _ringSprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01((r - 0.55f) * 16f) * Mathf.Clamp01((1f - r) * 16f);
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            _ringSprite.hideFlags = HideFlags.HideAndDontSave;
            return _ringSprite;
        }
    }
}
