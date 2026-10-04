using System;

namespace InventorXrSo.Core.Navigation
{
    /// <summary>Documento attivo osservato (da document info).</summary>
    public sealed class DocInfo
    {
        public DocInfo(string documentId, string name, string kind, bool isSheetMetal)
        {
            DocumentId = documentId; Name = name; Kind = kind; IsSheetMetal = isSheetMetal;
        }

        public string DocumentId { get; }
        public string Name { get; }
        /// <summary>"assembly" | "part" | "drawing" | ...</summary>
        public string Kind { get; }
        public bool IsSheetMetal { get; }
    }

    public enum RouteChange { None, Reset, Pushed, Popped }

    public sealed class RouteResult
    {
        public RouteChange Change { get; set; }
        public DocContext? Context { get; set; }
        public string Message { get; set; }
    }

    /// <summary>Documento attivo -> contesto; riconcilia la pila di navigazione.</summary>
    public sealed class ContextRouter
    {
        private readonly NavigationStack _stack;
        private string _pendingId;
        private string _pendingOccurrence;
        private float[] _pendingPose;

        public ContextRouter(NavigationStack stack)
        {
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        }

        /// <summary>null se il tipo non e supportato (disegno ecc.).</summary>
        public static DocContext? ContextOf(DocInfo doc)
        {
            if (doc == null) return null;
            switch ((doc.Kind ?? "").ToLowerInvariant())
            {
                case "assembly": return DocContext.Assembly;
                case "part": return doc.IsSheetMetal ? DocContext.SheetMetal : DocContext.Part;
                default: return null;
            }
        }

        /// <summary>Da chiamare quando il client richiede un ingresso, prima che il documento attivo cambi.</summary>
        public void ExpectEntry(string documentId, string fromOccurrenceId, float[] pose)
        {
            _pendingId = documentId; _pendingOccurrence = fromOccurrenceId; _pendingPose = pose;
        }

        /// <summary>Da chiamare a ogni cambio di documento attivo (dal Quest o dal PC).</summary>
        public RouteResult OnActiveDocument(DocInfo doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            var ctx = ContextOf(doc);
            if (ctx == null)
                return new RouteResult { Change = RouteChange.None, Context = null,
                    Message = "Documento non supportato: " + doc.Name };

            var top = _stack.Top;
            if (top == null)
            {
                ClearPending();
                _stack.Reset(new NavLevel(doc.DocumentId, ctx.Value, doc.Name));
                return new RouteResult { Change = RouteChange.Reset, Context = ctx };
            }

            if (doc.DocumentId == top.DocumentId)
                return new RouteResult { Change = RouteChange.None, Context = ctx };

            if (_pendingId != null && _pendingId == doc.DocumentId)
            {
                var level = new NavLevel(doc.DocumentId, ctx.Value, doc.Name, _pendingOccurrence, _pendingPose);
                ClearPending();
                _stack.Push(level);
                return new RouteResult { Change = RouteChange.Pushed, Context = ctx };
            }

            var parent = _stack.Parent;
            if (parent != null && doc.DocumentId == parent.DocumentId)
            {
                ClearPending();
                _stack.Pop();
                return new RouteResult { Change = RouteChange.Popped, Context = ctx };
            }

            ClearPending();
            _stack.Reset(new NavLevel(doc.DocumentId, ctx.Value, doc.Name));
            return new RouteResult { Change = RouteChange.Reset, Context = ctx,
                Message = "Documento cambiato dal PC: " + doc.Name };
        }

        private void ClearPending() { _pendingId = null; _pendingOccurrence = null; _pendingPose = null; }
    }
}
