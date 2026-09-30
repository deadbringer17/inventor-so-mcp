using InventorXrSo.Core.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>
    /// Guscio UI M6: tavolozza sul controller sinistro, barra di conferma, HUD. Si disegna solo dal
    /// catalogo; i workspace dichiarano azioni e stato, non costruiscono pannelli.
    /// </summary>
    public sealed class UiShell : MonoBehaviour
    {
        private CommitBarState _bar;

        public ActionCatalog Catalog { get; private set; }
        public PaletteView Palette { get; private set; }
        public CommitBarView CommitBar { get; private set; }
        public HudView Hud { get; private set; }

        public static UiShell Create(Transform paletteAnchor, Transform head, ActionCatalog catalog)
        {
            var shell = new GameObject("UiShell").AddComponent<UiShell>();
            shell.Catalog = catalog;
            shell.Palette = PaletteView.Create(paletteAnchor);
            shell.CommitBar = CommitBarView.Create(null);
            shell.Hud = HudView.Create(head);
            catalog.Changed += shell.Refresh;
            shell.Refresh();
            return shell;
        }

        public void Refresh()
        {
            var bar = Catalog.Active?.CommitBar;
            if (bar != _bar)
            {
                if (_bar != null) _bar.Changed -= RenderBar;
                _bar = bar;
                if (_bar != null) _bar.Changed += RenderBar;
            }
            Palette.Render(Catalog);
            RenderBar();
        }

        public void PlaceCommitBar(Vector3 position, Quaternion rotation) => CommitBar.transform.SetPositionAndRotation(position, rotation);

        private void RenderBar() => CommitBar.Render(_bar, Catalog);

        private void OnDestroy()
        {
            if (Catalog != null) Catalog.Changed -= Refresh;
            if (_bar != null) _bar.Changed -= RenderBar;
            if (CommitBar != null) Destroy(CommitBar.gameObject);
            if (Hud != null) Destroy(Hud.gameObject);
            if (Palette != null)
            {
                Palette.HideKeypad();
                if (Palette.Canvas != null) Destroy(Palette.Canvas.gameObject);
            }
        }
    }
}
