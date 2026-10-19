using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Backend;
using System;
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
        private Func<bool> _paletteTracked;
        private Func<bool> _penHeld;
        private Transform _head;
        private Func<WorkbenchFrame> _benchFrame;
        private WorkbenchFrame _initialFallbackFrame;

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

        public void BindTracking(Func<bool> paletteTracked, Transform head, Func<WorkbenchFrame> benchFrame, Func<bool> penHeld = null)
        {
            _paletteTracked = paletteTracked;
            _head = head;
            _benchFrame = benchFrame;
            _penHeld = penHeld;
        }

        public void UpdatePaletteTracking()
        {
            if (_paletteTracked == null || _head == null || Palette == null) return;
            bool tracked = _paletteTracked();
            // Il ritorno del controller non sposta la tavolozza durante un click
            // o un gesto già in corso. Si riaggancia dopo il rilascio intenzionale.
            if (tracked && Palette.TrackingFallback && _penHeld?.Invoke() == true) return;
            var frame = _benchFrame?.Invoke();
            if (!tracked && frame == null)
            {
                // Prima del primo Ricentra il fallback è congelato al momento della perdita;
                // non segue il movimento della testa mentre si seleziona sulla tavolozza.
                if (_initialFallbackFrame == null || Palette.HandTracked)
                    _initialFallbackFrame = WorkbenchFrame.FromHead(new CadPoint(_head.position.x, _head.position.y, _head.position.z), _head.eulerAngles.y, null);
                frame = _initialFallbackFrame;
            }
            Palette.SetHandTracking(tracked, frame);
        }

        private void LateUpdate() => UpdatePaletteTracking();

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
