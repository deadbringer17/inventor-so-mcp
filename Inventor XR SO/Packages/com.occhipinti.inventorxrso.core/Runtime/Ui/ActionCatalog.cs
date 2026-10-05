using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Voice;

namespace InventorXrSo.Core.Ui
{
    public enum VoiceMatchKind { NotFound, Ok, Ambiguous, Disabled }

    public sealed class VoiceMatch
    {
        internal VoiceMatch(VoiceMatchKind kind, XrAction action, IReadOnlyList<XrAction> conflicts, string reason)
        { Kind = kind; Action = action; Conflicts = conflicts; Reason = reason ?? ""; }
        public VoiceMatchKind Kind { get; }
        public XrAction Action { get; }
        public IReadOnlyList<XrAction> Conflicts { get; }
        public string Reason { get; }
    }

    /// <summary>Opzionale: il workspace attivo dichiara cosa e vero adesso (schizzo aperto, feature in costruzione).</summary>
    public interface ITabStateSource
    {
        TabState TabState { get; }
    }

    /// <summary>
    /// Azioni del workspace attivo, dei fornitori condivisi (Ispeziona, Vista) e della scheda fissa "Documento".
    /// Con <see cref="ContextProbe"/> le schede si compongono dal contesto (<see cref="ContextTabs"/>, M9 §2): una scheda
    /// compare solo se ha azioni. Senza sonda resta l'elenco dei fornitori (test e avvio).
    /// </summary>
    public sealed class ActionCatalog
    {
        public const string DocumentTab = "documento";
        public const string CommitTab = "_commit";
        public const int MaxPalette = 8;
        public const int MaxContext = 6;

        /// <summary>Id dell'azione Misura del fornitore Ispeziona: compare in coda all'anello di facce e bordi.</summary>
        public const string MeasureId = "inspect.measure";

        /// <summary>Scheda di uscita dal gruppo Ispeziona («◂»): interna, mai tra le principali.</summary>
        public const string InspectExitTab = "_ispeziona_esci";

        /// <summary>
        /// ContextTabs -> id reale della scheda dei provider. «vincoli_schizzo» e «opzioni_feature» sono le schede che
        /// compaiono da sole; in Progettazione sono «vincoli» e «opzioni», in Assieme «opzioni» ospita le opzioni dello spostamento.
        /// In Lamiera «vincoli» non esiste: la scheda resta nascosta (nessuna scheda senza azioni).
        /// </summary>
        public static string RealTabId(string contextTabId)
        {
            switch (contextTabId)
            {
                case ContextTabs.SketchConstraints: return "vincoli";
                case ContextTabs.FeatureOptions: return "opzioni";
                default: return contextTabId;
            }
        }

        /// <summary>Senza contesto (nessun workspace di authoring): solo strumenti locali e Documento.</summary>
        private static readonly string[] FallbackTabs = { "misura", "sezione", "esplora", "vista", DocumentTab };

        private readonly IActionProvider _document;
        private readonly List<IActionProvider> _shared = new List<IActionProvider>();
        private IActionProvider _active;

        public ActionCatalog(IActionProvider document) { _document = document ?? throw new ArgumentNullException(nameof(document)); }

        public event Action Changed;
        public IActionProvider Active => _active;

        /// <summary>Contesto del workspace aperto (Assieme, Parte, Lamiera); null senza workspace di authoring. Attiva la composizione.</summary>
        public Func<DocContext?> ContextProbe { get; set; }
        public DocContext? CurrentContext => ContextProbe?.Invoke();

        /// <summary>Fornitore incluso in ogni contesto (Ispeziona, Vista). I suoi id non devono collidere.</summary>
        public void AddShared(IActionProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (!_shared.Contains(provider)) _shared.Add(provider);
            Changed?.Invoke();
        }

        public void SetActive(IActionProvider provider)
        {
            if (provider != null)
            {
                var ids = provider.Actions.Concat(Others(provider).SelectMany(p => p.Actions)).Select(a => a.Id).ToList();
                var dup = ids.GroupBy(i => i).FirstOrDefault(g => g.Count() > 1);
                if (dup != null) throw new InvalidOperationException("Id azione duplicato: " + dup.Key);
            }
            _active = provider;
            Changed?.Invoke();
        }

        /// <summary>Da chiamare quando il workspace cambia abilitazioni o contenuto.</summary>
        public void NotifyChanged() => Changed?.Invoke();

        public IReadOnlyList<XrTab> Tabs
        {
            get
            {
                var probe = ContextProbe;
                if (probe == null)
                    return Providers().SelectMany(p => p.Tabs).Where(t => !t.Hidden).GroupBy(t => t.Id).Select(g => g.First()).ToArray();
                var context = probe();
                IEnumerable<string> ids = context == null || _active == null
                    ? FallbackTabs
                    : ContextTabs.Main(context.Value, (_active as ITabStateSource)?.TabState).Select(RealTabId);
                var tabs = new List<XrTab>();
                foreach (var id in ids)
                {
                    var tab = FindTab(id);
                    if (tab == null || tab.Hidden || tabs.Any(t => t.Id == id) || !HasActions(id)) continue;
                    tabs.Add(tab);
                }
                return tabs;
            }
        }

        /// <summary>Scheda per id, anche se nascosta (schede di elenco dinamiche del workspace).</summary>
        public XrTab FindTab(string id) => Providers().SelectMany(p => p.Tabs).FirstOrDefault(t => t.Id == id);

        private bool HasActions(string tabId) => All().Any(a => a.Tab == tabId);

        private IEnumerable<IActionProvider> Providers()
        {
            if (_active != null) yield return _active;
            foreach (var p in _shared) if (!ReferenceEquals(p, _active)) yield return p;
            yield return _document;
        }

        private IEnumerable<IActionProvider> Others(IActionProvider active)
        {
            foreach (var p in _shared) if (!ReferenceEquals(p, active)) yield return p;
            yield return _document;
        }

        public IReadOnlyList<XrAction> Palette(string tabId)
        {
            var list = All().Where(a => a.Tab == tabId).ToArray();
            if (list.Length > MaxPalette) throw new InvalidOperationException($"Scheda {tabId}: {list.Length} azioni, massimo {MaxPalette}.");
            return list;
        }

        public IReadOnlyList<XrAction> Context(SelectionKind selection)
        {
            if (_active == null || selection == SelectionKind.None) return Array.Empty<XrAction>();
            var list = _active.ContextActions(selection).ToList();
            // M9 §2: Misura e trasversale. Sulle facce e sui bordi l'anello la prende dal fornitore Ispeziona condiviso
            // (stessa azione della palette); un componente non la riceve (in Assieme l'anello e Isola·Sposta·Vincola·Apri).
            if (selection != SelectionKind.Component && list.All(a => a.Id != MeasureId))
            {
                var measure = _shared.SelectMany(p => p.Actions).FirstOrDefault(a => a.Id == MeasureId);
                if (measure != null) list.Add(measure);
            }
            if (list.Count > MaxContext) throw new InvalidOperationException($"Anello {selection}: {list.Count} azioni, massimo {MaxContext}.");
            return list.ToArray();
        }

        public XrAction Find(string id) => All().FirstOrDefault(a => a.Id == id);

        public bool TryInvoke(string id) => Find(id)?.TryInvoke() == true;

        public VoiceMatch ResolveVoice(string transcript)
        {
            string said = ItalianTextNormalizer.Normalize(transcript);
            if (said.Length == 0) return new VoiceMatch(VoiceMatchKind.NotFound, null, Array.Empty<XrAction>(), "");
            var hits = Reachable().Where(a => Names(a).Contains(said)).ToArray();
            var enabled = hits.Where(a => a.Enabled).ToArray();
            if (enabled.Length == 1) return new VoiceMatch(VoiceMatchKind.Ok, enabled[0], Array.Empty<XrAction>(), "");
            if (enabled.Length > 1) return new VoiceMatch(VoiceMatchKind.Ambiguous, null, enabled, "Comando ambiguo.");
            if (hits.Length > 0) return new VoiceMatch(VoiceMatchKind.Disabled, hits[0], Array.Empty<XrAction>(), hits[0].DisabledReason);
            return new VoiceMatch(VoiceMatchKind.NotFound, null, Array.Empty<XrAction>(), "");
        }

        private IEnumerable<XrAction> All() => Providers().SelectMany(p => p.Actions);

        /// <summary>
        /// La voce vede solo cio che e raggiungibile nel contesto: le azioni dei fornitori condivisi in una scheda che il
        /// contesto non mostra (p. es. Verifica in Parte) non rispondono.
        /// </summary>
        private IEnumerable<XrAction> Reachable()
        {
            var visible = new HashSet<string>(Tabs.Select(t => t.Id));
            foreach (var p in Providers())
            {
                bool filter = ContextProbe != null && !ReferenceEquals(p, _active) && !ReferenceEquals(p, _document);
                foreach (var a in p.Actions)
                    if (!filter || visible.Contains(a.Tab) || a.Tab.StartsWith("_", StringComparison.Ordinal) || IsGroupTab(a.Tab)) yield return a;
            }
        }

        // Le schede del gruppo Ispeziona si raggiungono solo dal gruppo aperto in un contesto che le prevede.
        private bool IsGroupTab(string tab)
        {
            var context = CurrentContext;
            return context != null && ContextTabs.InspectGroup(context.Value).Contains(tab);
        }

        private static HashSet<string> Names(XrAction a)
        {
            var names = new HashSet<string>(new[] { a.Label }.Concat(a.Synonyms).Select(ItalianTextNormalizer.Normalize));
            foreach (var alias in LabelAliases(a.Label)) names.Add(alias);
            return names;
        }

        /// <summary>
        /// Etichette con un valore ("Distanza: 5 mm", "Scala da tavolo • 60 cm", "Asse 2") si dicono anche senza il valore:
        /// il prefisso prima di ':', di '•' o della prima cifra. Resta un vocabolario finito derivato dalle azioni dichiarate.
        /// </summary>
        private static IEnumerable<string> LabelAliases(string label)
        {
            var found = new List<string>();
            int colon = label.IndexOf(':');
            if (colon > 0) found.Add(ItalianTextNormalizer.Normalize(label.Substring(0, colon)));
            int detail = label.IndexOf('•');
            if (detail > 0) found.Add(ItalianTextNormalizer.Normalize(label.Substring(0, detail)));
            string full = ItalianTextNormalizer.Normalize(label);
            int digit = full.IndexOfAny("0123456789".ToCharArray());
            if (digit > 0) found.Add(full.Substring(0, digit).TrimEnd());
            return found.Where(n => n.Length > 0);
        }
    }
}
