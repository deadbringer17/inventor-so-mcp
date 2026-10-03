using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Ui;

namespace InventorXrSo.Xr
{
    /// <summary>Scheda fissa "Spazi" della tavolozza: cambio workspace e connessione.</summary>
    public sealed class SpacesActions : IActionProvider
    {
        private readonly XrTab[] _tabs = { new XrTab(ActionCatalog.SpacesTab, "Spazi") };
        private readonly XrAction[] _actions;

        public SpacesActions(Action openInspect, Action openDesign, Action openLamiera, Action openAssembly, Action openConnection,
            Func<bool> inSession, Func<bool> canDesign, Func<bool> canLamiera, Func<bool> canAssembly)
        {
            string Offline() => "Nessuna sessione con Inventor.";
            string Blocked() => inSession() ? "Controlla prima la modifica CAD non confermata." : Offline();
            _actions = new[]
            {
                new XrAction("spaces.inspect", "Ispeziona", ActionCatalog.SpacesTab, inSession, openInspect, Offline),
                new XrAction("spaces.design", "Progettazione", ActionCatalog.SpacesTab, () => inSession() && canDesign(), openDesign, Blocked, new[] { "progetta" }),
                new XrAction("spaces.lamiera", "Lamiera", ActionCatalog.SpacesTab, () => inSession() && canLamiera(), openLamiera, Blocked),
                new XrAction("spaces.assembly", "Assieme", ActionCatalog.SpacesTab, () => inSession() && canAssembly(), openAssembly, Blocked, new[] { "assemblaggio" }),
                new XrAction("spaces.connection", "Connessione", ActionCatalog.SpacesTab, () => true, openConnection),
            };
        }

        public IReadOnlyList<XrTab> Tabs => _tabs;
        public IEnumerable<XrAction> Actions => _actions;
        public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Enumerable.Empty<XrAction>();
        public CommitBarState CommitBar => null;
    }
}
