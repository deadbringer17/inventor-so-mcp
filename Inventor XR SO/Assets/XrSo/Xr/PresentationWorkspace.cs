using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Experience;
using InventorXrSo.Core.Ui;

namespace InventorXrSo.Xr
{
    /// <summary>Composition of shared inspection services; no authoring workspace or CAD draft.</summary>
    public sealed class PresentationWorkspace : IActionProvider, IComposedActionProvider
    {
        private readonly InspectWorkspace _inspection;
        public PresentationWorkspace(InspectWorkspace inspection) { _inspection = inspection; }
        public IActionProvider Source => _inspection;
        public IReadOnlyList<XrTab> Tabs => _inspection.Tabs;
        public IEnumerable<XrAction> Actions => _inspection.Actions.Where(a => ActivityPolicy.AllowsAction(Activity.Presentation, a.Id));
        public IEnumerable<XrAction> ContextActions(SelectionKind selection) => _inspection.ContextActions(selection)
            .Where(a => ActivityPolicy.AllowsAction(Activity.Presentation, a.Id));
        public CommitBarState CommitBar => null;
    }
}
