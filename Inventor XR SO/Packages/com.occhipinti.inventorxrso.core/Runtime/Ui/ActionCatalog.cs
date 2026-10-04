using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Azioni del workspace attivo piu la scheda fissa "Documento".</summary>
    public sealed class ActionCatalog
    {
        public const string DocumentTab = "documento";
        public const string CommitTab = "_commit";
        public const int MaxPalette = 8;
        public const int MaxContext = 6;

        private readonly IActionProvider _document;
        private IActionProvider _active;

        public ActionCatalog(IActionProvider document) { _document = document ?? throw new ArgumentNullException(nameof(document)); }

        public event Action Changed;
        public IActionProvider Active => _active;

        public void SetActive(IActionProvider provider)
        {
            if (provider != null)
            {
                var ids = provider.Actions.Concat(_document.Actions).Select(a => a.Id).ToList();
                var dup = ids.GroupBy(i => i).FirstOrDefault(g => g.Count() > 1);
                if (dup != null) throw new InvalidOperationException("Id azione duplicato: " + dup.Key);
            }
            _active = provider;
            Changed?.Invoke();
        }

        /// <summary>Da chiamare quando il workspace cambia abilitazioni o contenuto.</summary>
        public void NotifyChanged() => Changed?.Invoke();

        public IReadOnlyList<XrTab> Tabs =>
            (_active?.Tabs ?? Array.Empty<XrTab>()).Concat(_document.Tabs).Where(t => !t.Hidden).ToArray();

        /// <summary>Scheda per id, anche se nascosta (schede di elenco dinamiche del workspace).</summary>
        public XrTab FindTab(string id) =>
            (_active?.Tabs ?? Array.Empty<XrTab>()).Concat(_document.Tabs).FirstOrDefault(t => t.Id == id);

        public IReadOnlyList<XrAction> Palette(string tabId)
        {
            var list = All().Where(a => a.Tab == tabId).ToArray();
            if (list.Length > MaxPalette) throw new InvalidOperationException($"Scheda {tabId}: {list.Length} azioni, massimo {MaxPalette}.");
            return list;
        }

        public IReadOnlyList<XrAction> Context(SelectionKind selection)
        {
            if (_active == null || selection == SelectionKind.None) return Array.Empty<XrAction>();
            var list = _active.ContextActions(selection).ToArray();
            if (list.Length > MaxContext) throw new InvalidOperationException($"Anello {selection}: {list.Length} azioni, massimo {MaxContext}.");
            return list;
        }

        public XrAction Find(string id) => All().FirstOrDefault(a => a.Id == id);

        public bool TryInvoke(string id) => Find(id)?.TryInvoke() == true;

        public VoiceMatch ResolveVoice(string transcript)
        {
            string said = ItalianTextNormalizer.Normalize(transcript);
            if (said.Length == 0) return new VoiceMatch(VoiceMatchKind.NotFound, null, Array.Empty<XrAction>(), "");
            var hits = All().Where(a => Names(a).Contains(said)).ToArray();
            var enabled = hits.Where(a => a.Enabled).ToArray();
            if (enabled.Length == 1) return new VoiceMatch(VoiceMatchKind.Ok, enabled[0], Array.Empty<XrAction>(), "");
            if (enabled.Length > 1) return new VoiceMatch(VoiceMatchKind.Ambiguous, null, enabled, "Comando ambiguo.");
            if (hits.Length > 0) return new VoiceMatch(VoiceMatchKind.Disabled, hits[0], Array.Empty<XrAction>(), hits[0].DisabledReason);
            return new VoiceMatch(VoiceMatchKind.NotFound, null, Array.Empty<XrAction>(), "");
        }

        private IEnumerable<XrAction> All() => (_active?.Actions ?? Enumerable.Empty<XrAction>()).Concat(_document.Actions);

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
