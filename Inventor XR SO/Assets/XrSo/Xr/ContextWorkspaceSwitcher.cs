using System;
using InventorXrSo.Core.Navigation;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// M9: the active document decides the workspace. Wraps <see cref="ContextRouter"/> and turns its result into
    /// "open this context's workspace" through delegates, so the choice is testable without an <see cref="AppController"/>.
    /// Assembly -> Assieme, part -> Progettazione, sheet-metal part -> Lamiera. Inspect tools are not a context.
    /// </summary>
    public sealed class ContextWorkspaceSwitcher
    {
        private readonly ContextRouter _router;
        private readonly NavigationStack _stack;
        private readonly Func<DocContext, bool> _open;
        private readonly Action _closeAll;
        private readonly Action<string> _hud;
        private readonly Func<bool> _inSession;
        private string _jumpTarget, _sheetHintDocument;

        /// <param name="open">Closes the other workspaces and opens the one of the context; true if it is now open.</param>
        /// <param name="closeAll">Closes every authoring workspace.</param>
        public ContextWorkspaceSwitcher(NavigationStack stack, Func<DocContext, bool> open, Action closeAll,
            Action<string> hud, Func<bool> inSession)
        {
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));
            _router = new ContextRouter(stack);
            _open = open ?? throw new ArgumentNullException(nameof(open));
            _closeAll = closeAll ?? (() => { });
            _hud = hud ?? (_ => { });
            _inSession = inSession ?? (() => true);
        }

        public NavigationStack Navigation => _stack;
        /// <summary>Context of the active document as last routed; null when none/unsupported.</summary>
        public DocContext? Context { get; private set; }
        /// <summary>Context whose workspace is currently open (null when none).</summary>
        public DocContext? Opened { get; private set; }

        /// <summary>Entering a definition from an occurrence: the next document change pushes a level instead of resetting.</summary>
        public void ExpectEntry(string documentId, string occurrenceId, float[] pose, bool sheetMetal)
        {
            _router.ExpectEntry(documentId, occurrenceId, pose);
            _sheetHintDocument = sheetMetal ? documentId : null;
        }

        /// <summary>The user picked a listed document: the stack restarts on it (no push, no pop, no PC-change notice).</summary>
        public void ExpectJump(string documentId)
        {
            _jumpTarget = _stack.Top != null && _stack.Top.DocumentId == documentId ? null : documentId;
        }

        public void CancelJump() => _jumpTarget = null;

        /// <summary>True when the entry that was requested for this document asked for the sheet-metal workspace.</summary>
        public bool SheetMetalHint(string documentId) => _sheetHintDocument != null && _sheetHintDocument == documentId;

        /// <summary>The active document was (re)observed: reconcile the stack and open the matching workspace.</summary>
        public RouteResult OnDocument(DocInfo doc)
        {
            RouteResult result;
            var ctx = ContextRouter.ContextOf(doc);
            if (doc != null && ctx != null && _jumpTarget != null && doc.DocumentId == _jumpTarget)
            {
                _jumpTarget = null;
                _stack.Reset(new NavLevel(doc.DocumentId, ctx.Value, doc.Name));
                result = new RouteResult { Change = RouteChange.Reset, Context = ctx };
            }
            else
            {
                result = _router.OnActiveDocument(doc);
                if (result.Change != RouteChange.None) _jumpTarget = null;
            }
            if (!string.IsNullOrEmpty(result.Message)) _hud(result.Message);
            Context = result.Context;
            Sync();
            return result;
        }

        /// <summary>Opens the workspace of <see cref="Context"/> when in session and it is not already the open one.</summary>
        public void Sync()
        {
            if (!_inSession() || Context == null || Opened == Context) return;
            if (_open(Context.Value)) Opened = Context;
            else _hud("Operazione CAD da verificare: non cambio contesto finché non è risolta.");
        }

        /// <summary>Leaving the session or the PC: authoring workspaces close, the next Sync reopens the right one.</summary>
        public void Leave()
        {
            Opened = null;
            _closeAll();
        }

        /// <summary>No document at all (session stopped).</summary>
        public void Clear()
        {
            Context = null; _jumpTarget = null; _sheetHintDocument = null;
            _stack.Clear();
            Leave();
        }
    }
}
