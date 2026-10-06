using System;
using System.Collections.Generic;
using InventorXrSo.Core.Ui;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// M9 «Vista unica»: one shared Vista tab for every context (spec §2). It declares the tab and the actions that do not
    /// depend on the document: Adatta and the key legend yes/no (kept on the headset). Scale and MR/VR environment are the
    /// Ispeziona actions of the same tab; the domain actions of a context (Aggiorna, Annulla, Vista modello, Vista sviluppo)
    /// stay with their provider under the same tab id, so the palette shows a single «Vista» tab.
    /// </summary>
    public sealed class ViewActions : IActionProvider
    {
        public const string TabView = "vista";
        public const string IdFit = "view.fit", IdLegend = "view.legend";
        /// <summary>PlayerPrefs key of the legend preference (1 = on, the default).</summary>
        public const string LegendPrefKey = "xrso.legend";

        private static readonly XrTab[] StaticTabs = { new XrTab(TabView, "Vista") };

        private readonly Func<bool> _canFit;
        private readonly Action _fit;
        private readonly Func<bool> _getLegend;
        private readonly Action<bool> _setLegend;

        /// <param name="canFit">True when the active workspace can fit the model to the work plane.</param>
        /// <param name="fit">Fits the model of the active workspace.</param>
        /// <param name="getLegend">Reads the legend preference; default PlayerPrefs <see cref="LegendPrefKey"/>, on.</param>
        /// <param name="setLegend">Stores the legend preference; default PlayerPrefs.</param>
        public ViewActions(Func<bool> canFit, Action fit, Func<bool> getLegend = null, Action<bool> setLegend = null)
        {
            _canFit = canFit ?? throw new ArgumentNullException(nameof(canFit));
            _fit = fit ?? throw new ArgumentNullException(nameof(fit));
            _getLegend = getLegend ?? (() => PlayerPrefs.GetInt(LegendPrefKey, 1) != 0);
            _setLegend = setLegend ?? (on => { PlayerPrefs.SetInt(LegendPrefKey, on ? 1 : 0); PlayerPrefs.Save(); });
        }

        /// <summary>Tells the catalog that labels changed (wire to <see cref="ActionCatalog.NotifyChanged"/>).</summary>
        public Action Changed { get; set; }

        /// <summary>Raised when the legend is switched; the controller legend follows it.</summary>
        public event Action<bool> LegendChanged;

        public bool LegendOn => _getLegend();

        public void SetLegend(bool on)
        {
            if (on == LegendOn) return;
            _setLegend(on);
            LegendChanged?.Invoke(on);
            Changed?.Invoke();
        }

        public IReadOnlyList<XrTab> Tabs => StaticTabs;

        public IEnumerable<XrAction> Actions => new[]
        {
            new XrAction(IdFit, "Adatta", TabView, _canFit, _fit, () => "Postazione non disponibile.", new[] { "adatta alla postazione" }, icon: "fit"),
            new XrAction(IdLegend, "Legenda tasti: " + (LegendOn ? "sì" : "no"), TabView, () => true, () => SetLegend(!LegendOn),
                null, new[] { "legenda", "legenda tasti" }, XrActionKind.Toggle, isOn: () => LegendOn),
        };

        public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Array.Empty<XrAction>();

        public CommitBarState CommitBar => null;
    }
}
