using InventorXrSo.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Barra di conferma unica sul bordo vicino del piano: l'unico posto da cui si applica.</summary>
    public sealed class CommitBarView : MonoBehaviour
    {
        public const float TextMm = 14f;
        private Image _background;
        private TextMeshProUGUI _message;
        private RectTransform _buttons;

        public Canvas Canvas { get; private set; }

        public static CommitBarView Create(Transform parent)
        {
            var canvas = UiFactory.WorldCanvas(parent, "Barra di conferma", new Vector2(360, 60));
            var view = canvas.gameObject.AddComponent<CommitBarView>();
            view.Canvas = canvas;
            var bg = UiFactory.Panel(canvas.transform, "Sfondo", UiFactory.Background);
            UiFactory.Stretch(bg);
            view._background = bg.GetComponent<Image>();
            var row = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 6, 6);
            row.spacing = 6;
            row.childControlWidth = row.childControlHeight = true;
            view._message = UiFactory.Text(bg, "", TextMm);
            view._message.alignment = TextAlignmentOptions.MidlineLeft;
            view._message.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            view._buttons = UiFactory.Panel(bg, "Pulsanti", Color.clear);
            var buttons = view._buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttons.spacing = 6;
            buttons.childControlWidth = buttons.childControlHeight = true;
            view._buttons.gameObject.AddComponent<LayoutElement>().preferredWidth = 230;
            canvas.gameObject.SetActive(false);
            return view;
        }

        public void Render(CommitBarState state, ActionCatalog catalog)
        {
            bool visible = state != null && state.Phase != CommitBarPhase.Empty;
            Canvas.gameObject.SetActive(visible);
            if (!visible) return;
            _background.color = UiStyle.For(state.Phase);
            _message.text = Message(state);
            UiFactory.ClearChildren(_buttons);
            if (state.RecoveryLabel != null) Add(catalog, CommitIds.Recover, state.RecoveryLabel);
            if (state.CanPreview) Add(catalog, CommitIds.Preview, null);
            if (state.CanApply) Add(catalog, CommitIds.Apply, null);
            if (state.CanCancel) Add(catalog, CommitIds.Cancel, null);
        }

        private void Add(ActionCatalog catalog, string id, string labelOverride)
        {
            var action = catalog.Find(id);
            if (action == null) return;
            var b = UiFactory.TextButton(_buttons, labelOverride ?? action.Label, UiFactory.Key, TextMm, () => action.TryInvoke());
            b.interactable = action.Enabled;
        }

        private static string Message(CommitBarState s)
        {
            switch (s.Phase)
            {
                case CommitBarPhase.Draft: return "Bozza";
                case CommitBarPhase.Previewing: return "Anteprima in corso…";
                case CommitBarPhase.Ready: return "Anteprima pronta";
                case CommitBarPhase.Stale: return "Documento cambiato";
                case CommitBarPhase.Uncertain: return "Esito non confermato";
                case CommitBarPhase.Error: return s.Message;
                case CommitBarPhase.Offline: return "Offline";
                case CommitBarPhase.Applied: return "Applicato";
                default: return "";
            }
        }
    }
}
