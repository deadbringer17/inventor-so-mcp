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
        /// <summary>Posizione mondo del centro dell'etichetta (aggiornata a ogni Tick con la legenda visibile).</summary>
        public Vector3 WorldPosition { get; internal set; }
        /// <summary>Posizione mondo del tasto fisico a cui l'etichetta si riferisce (inizio della linea guida).</summary>
        public Vector3 ButtonWorldPosition { get; internal set; }
        /// <summary>Estremo della linea guida sul bordo dell'etichetta.</summary>
        public Vector3 LeaderEnd { get; internal set; }
        /// <summary>Posizione nella colonna della mano, 0 = in alto; le etichette inattive non occupano slot.</summary>
        public int Slot { get; internal set; }
    }

    /// <summary>
    /// Legenda 3D (spec M9 §3): un'etichetta per ogni tasto attivo nello stato (da <see cref="InputMap.Active"/>), posizionata accanto al
    /// tasto reale (baricentro dei tasti, lato esterno rispetto alla testa), sempre rivolta verso l'utente, con linea guida fino al tasto. Opacità bassa di base, piena entro 25° dall'asse della testa; anellino di avanzamento per doppio Trigger e X tenuto.
    /// Nessuna animazione: lo stato e l'opacità si applicano nello stesso frame.
    /// </summary>
    public sealed class ControllerLegend : MonoBehaviour
    {
        /// <summary>Stessa chiave di ViewActions.LegendPrefKey (Runtime non vede Xr; un test le tiene allineate).</summary>
        public const string PrefKey = "xrso.legend";
        public const float LabelWidthMm = 46f, LabelHeightMm = 11f, TextCapMm = 5f, RingMm = 8f;

        /// <summary>
        /// Posizione (m) di ogni tasto nello spazio locale dell'ancora del controller destro. Misurata sui nodi FBX del Meta Quest Touch Plus
        /// (artifacts/m9-verification/controller-touchplus-nodes.txt, in cm, qui divisi per 100). I due assi dello stick condividono il punto.
        /// </summary>
        public static readonly IReadOnlyDictionary<Key, Vector3> RightButtonPositions = new Dictionary<Key, Vector3>
        {
            [Key.A] = new Vector3(-0.0049f, 0.0044f, -0.0097f),
            [Key.B] = new Vector3(-0.0154f, 0.0065f, 0.0028f),
            [Key.StickRightH] = new Vector3(0.0060f, -0.0022f, 0.0089f),
            [Key.StickRightV] = new Vector3(0.0060f, -0.0022f, 0.0089f),
            [Key.Trigger] = new Vector3(-0.0157f, -0.0014f, 0.0243f),
            [Key.Grip] = new Vector3(-0.0116f, -0.0214f, 0.0126f),
        };

        /// <summary>Come <see cref="RightButtonPositions"/>, controller sinistro.</summary>
        public static readonly IReadOnlyDictionary<Key, Vector3> LeftButtonPositions = new Dictionary<Key, Vector3>
        {
            [Key.X] = new Vector3(0.0054f, 0.0043f, -0.0096f),
            [Key.Y] = new Vector3(0.0156f, 0.0060f, 0.0032f),
            [Key.StickLeftH] = new Vector3(-0.0063f, -0.0019f, 0.0087f),
            [Key.StickLeftV] = new Vector3(-0.0063f, -0.0019f, 0.0087f),
            [Key.TriggerLeft] = new Vector3(0.0149f, -0.0020f, 0.0247f),
            [Key.TwoGrips] = new Vector3(0.0103f, -0.0218f, 0.0129f),
        };

        /// <summary>Ordine della colonna dall'alto in basso (dal davanti al dietro del controller).</summary>
        public static readonly IReadOnlyList<Key> RightOrder = new[] { Key.Trigger, Key.Grip, Key.StickRightH, Key.StickRightV, Key.B, Key.A };
        public static readonly IReadOnlyList<Key> LeftOrder = new[] { Key.TriggerLeft, Key.TwoGrips, Key.StickLeftH, Key.StickLeftV, Key.Y, Key.X };

        /// <summary>Larghezza della linea guida (m).</summary>
        public const float LeaderWidth = 0.0012f;

        private sealed class View
        {
            public LegendLabel Label;
            public Transform Anchor;
            public GameObject Root;
            public CanvasGroup Group;
            public TextMeshProUGUI Text;
            public Image Ring;
            public GameObject RingRoot;
            public LineRenderer Leader;
            public int Slot;
            public Vector3 ButtonLocal;
        }

        private static Material _leaderMaterial;

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
            AssignSlots(true); AssignSlots(false);
            Tick();
        }

        private void AssignSlots(bool right)
        {
            int slot = 0;
            foreach (var key in right ? RightOrder : LeftOrder)
            {
                var label = _labels.Find(l => l.Key == key);
                if (label == null) continue;
                var view = _views[key];
                view.Slot = label.Slot = slot++;
            }
        }

        private int ActiveCount(bool right)
        {
            int n = 0;
            foreach (var l in _labels) if (l.Right == right) n++;
            return n;
        }

        /// <summary>Aggiorna opacità, anellini, visibilità e geometria (etichette verso la testa, linee guida); chiamato ogni frame e dopo ogni cambio.</summary>
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
                Layout(view);
            }
        }

        private void Layout(View view)
        {
            var label = view.Label;
            bool right = label.Right;
            var anchor = view.Anchor;
            var buttons = right ? RightButtonPositions : LeftButtonPositions;
            var centroidWorld = anchor.TransformPoint(LegendGeometry.Centroid(buttons.Values));
            var outward = LegendGeometry.Outward(_head, right);
            var up = LegendGeometry.HeadUp(_head);
            var pos = LegendGeometry.LabelPosition(centroidWorld, outward, up, view.Slot, ActiveCount(right));
            var tr = view.Root.transform;
            tr.position = pos;
            if (_head != null)
            {
                var away = pos - _head.position;
                if (away.sqrMagnitude > 1e-8f) tr.rotation = Quaternion.LookRotation(away, up);
            }
            label.WorldPosition = pos;
            var button = anchor.TransformPoint(view.ButtonLocal);
            label.ButtonWorldPosition = button;
            var end = LegendGeometry.NearestEdgePoint(pos, tr.right, tr.up, LabelWidthMm * 0.0005f, LabelHeightMm * 0.0005f, button);
            label.LeaderEnd = end;
            if (view.Leader != null)
            {
                view.Leader.SetPosition(0, button);
                view.Leader.SetPosition(1, end);
                var c = new Color(UiTheme.Signal.r, UiTheme.Signal.g, UiTheme.Signal.b, label.Opacity);
                view.Leader.startColor = view.Leader.endColor = c;
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

        private void LateUpdate() => Tick();

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
            bool right = RightButtonPositions.ContainsKey(key);
            var anchor = right ? _right : _left;
            (right ? RightButtonPositions : LeftButtonPositions).TryGetValue(key, out var buttonLocal);

            var canvas = UiFactory.WorldCanvas(anchor, "Legend." + key, new Vector2(LabelWidthMm, LabelHeightMm));
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

            var leaderGo = new GameObject("Guida", typeof(LineRenderer));
            leaderGo.transform.SetParent(canvas.transform, false);
            var leader = leaderGo.GetComponent<LineRenderer>();
            leader.useWorldSpace = true;
            leader.positionCount = 2;
            leader.startWidth = leader.endWidth = LeaderWidth;
            leader.numCapVertices = 0;
            leader.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            leader.receiveShadows = false;
            leader.sharedMaterial = LeaderMaterial();

            var view = new View
            {
                Leader = leader, ButtonLocal = buttonLocal,
                Label = new LegendLabel { Key = key, Right = right },
                Anchor = anchor, // l'angolo di sguardo si misura sul controller, non sull'etichetta
                Root = canvas.gameObject, Group = group, Text = text, Ring = ring, RingRoot = ringRoot,
            };
            _views[key] = view;
            return view;
        }

        /// <summary>Materiale unlit condiviso delle linee guida (colore da vertex color): stesso fallback degli altri overlay runtime.</summary>
        private static Material LeaderMaterial()
        {
            if (_leaderMaterial != null) return _leaderMaterial;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("XrSo/HighlightOverlay");
            _leaderMaterial = new Material(shader) { name = "Legend leader", hideFlags = HideFlags.HideAndDontSave };
            return _leaderMaterial;
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
