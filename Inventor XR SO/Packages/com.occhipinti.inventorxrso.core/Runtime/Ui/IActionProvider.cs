using System.Collections.Generic;

namespace InventorXrSo.Core.Ui
{
    /// <summary>Implementato da ogni workspace: dichiara schede, azioni, anello e barra di conferma.</summary>
    public interface IActionProvider
    {
        IReadOnlyList<XrTab> Tabs { get; }
        /// <summary>Tutte le azioni del workspace; ognuna dichiara la sua scheda.</summary>
        IEnumerable<XrAction> Actions { get; }
        IEnumerable<XrAction> ContextActions(SelectionKind selection);
        /// <summary>Null se il workspace non ha una barra di conferma.</summary>
        CommitBarState CommitBar { get; }
    }
}
