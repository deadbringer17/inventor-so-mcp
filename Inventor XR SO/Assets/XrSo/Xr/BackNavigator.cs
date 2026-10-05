using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Navigation;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// M9 «Torna»: sale di un livello della pila riattivando il documento padre in Inventor. Non salva mai. La pila
    /// non si tocca qui: quando il padre diventa il documento attivo il <see cref="ContextRouter"/> la riconcilia (Popped),
    /// l'assieme fantasma segue la pila. Unica eccezione: padre non piu attivabile (chiuso dal PC) -> pila azzerata
    /// sul documento attivo tramite <c>resetOnActive</c>.
    /// </summary>
    public sealed class BackNavigator
    {
        private readonly NavigationStack _stack;
        private readonly Func<bool> _reviewPending, _online;
        private readonly Func<IInspectionBackend> _backend;
        private readonly Func<CancellationToken> _token;
        private readonly Action<string> _hud;
        private readonly Action _resetOnActive;
        private bool _busy;

        public BackNavigator(NavigationStack stack, Func<bool> reviewPending, Func<bool> online, Func<IInspectionBackend> backend,
            Func<CancellationToken> token, Action<string> hud, Action resetOnActive)
        {
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));
            _reviewPending = reviewPending ?? (() => false);
            _online = online ?? (() => true);
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _token = token ?? (() => CancellationToken.None);
            _hud = hud ?? (_ => { });
            _resetOnActive = resetOnActive ?? (() => { });
        }

        public const string ReviewBlocked = "Revisione CAD in sospeso: controlla prima la modifica non confermata, poi torna.";
        public const string AtTop = "Sei già al livello più alto.";

        /// <summary>Suggerimento del tocco breve di X a riposo.</summary>
        public void Tapped() => _hud(_stack.CanPop ? "Tieni X per tornare a " + _stack.Parent.Name : AtTop);

        public async Task GoBackAsync()
        {
            if (_busy) return;
            if (_reviewPending()) { _hud(ReviewBlocked); return; }
            if (!_stack.CanPop) { _hud(AtTop); return; }
            var backend = _backend();
            if (backend == null || !_online()) { _hud("Connessione con Inventor assente: resto su " + _stack.Top.Name + "."); return; }
            var parent = _stack.Parent;
            _busy = true;
            try
            {
                await backend.ActivateOpenAsync(parent.DocumentId, _token());
                _hud("Torno a " + parent.Name + ". Attendo la scena da Inventor…");
            }
            catch (OperationCanceledException) { }
            catch (McpToolException)
            {
                // L'host ha risposto ma rifiuta: il padre non e piu aperto (chiuso dal PC). La pila riparte dal documento attivo.
                _resetOnActive();
                _hud(parent.Name + " non è più aperto in Inventor: la navigazione riparte da " + (_stack.Top?.Name ?? "il documento attivo") + ".");
            }
            catch (Exception ex)
            {
                _hud("Connessione persa: resto su " + _stack.Top.Name + ". " + ex.Message);
            }
            finally { _busy = false; }
        }
    }

    /// <summary>
    /// «●» del percorso: un documento e modificato se la sua revisione differisce da quella di riferimento (la prima osservata
    /// o l'ultima salvata dal visore). Il backend client non espone un segnale di "modificato" ne un salvataggio, quindi si
    /// deduce dal cambio di revisione (modifiche dal visore e dal PC).
    /// </summary>
    public sealed class DirtyTracker
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _baseline = new System.Collections.Generic.Dictionary<string, string>();
        private readonly System.Collections.Generic.Dictionary<string, string> _latest = new System.Collections.Generic.Dictionary<string, string>();

        public void Observe(DocumentState state)
        {
            if (state == null || string.IsNullOrEmpty(state.DocumentId)) return;
            if (!_baseline.ContainsKey(state.DocumentId)) _baseline[state.DocumentId] = state.Revision;
            _latest[state.DocumentId] = state.Revision;
        }

        public bool IsDirty(string documentId) =>
            documentId != null && _baseline.TryGetValue(documentId, out var b) && _latest.TryGetValue(documentId, out var l) && b != l;

        /// <summary>Dopo un salvataggio: la revisione corrente diventa il riferimento.</summary>
        public void MarkSaved(string documentId)
        {
            if (documentId != null && _latest.TryGetValue(documentId, out var l)) _baseline[documentId] = l;
        }

        public void Clear() { _baseline.Clear(); _latest.Clear(); }

        /// <summary>Allinea <see cref="NavLevel.Dirty"/> dei livelli della pila; true se qualcosa e cambiato.</summary>
        public bool Apply(NavigationStack stack)
        {
            bool changed = false;
            foreach (var level in stack.Levels)
            {
                bool dirty = IsDirty(level.DocumentId);
                if (level.Dirty != dirty) { level.Dirty = dirty; changed = true; }
            }
            return changed;
        }
    }
}
