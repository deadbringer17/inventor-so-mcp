using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Experience;
using InventorXrSo.Core.Ui;

namespace InventorXrSo.Xr
{
    /// <summary>M7 and local scene browser composed in their own activity, without CAD authoring.</summary>
    public sealed class VerificationWorkspace : IActionProvider, IComposedActionProvider
    {
        private readonly InspectWorkspace _inspection;
        public VerificationWorkspace(InspectWorkspace inspection) { _inspection = inspection; }
        public IActionProvider Source => _inspection;
        public IReadOnlyList<XrTab> Tabs => _inspection.Tabs;
        public IEnumerable<XrAction> Actions => _inspection.Actions.Where(a => ActivityPolicy.AllowsAction(Activity.Verification, a.Id));
        public IEnumerable<XrAction> ContextActions(SelectionKind selection) => _inspection.ContextActions(selection)
            .Where(a => ActivityPolicy.AllowsAction(Activity.Verification, a.Id));
        public CommitBarState CommitBar => null;
    }
}
