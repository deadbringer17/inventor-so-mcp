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

    /// <summary>Azioni del workspace attivo piu la scheda fissa "Spazi".</summary>
    public sealed class ActionCatalog
    {
        public const string SpacesTab = "spazi";
        public const string CommitTab = "_commit";
        public const int MaxPalette = 8;
        public const int MaxContext = 6;

        private readonly IActionProvider _spaces;
        private IActionProvider _active;

        public ActionCatalog(IActionProvider spaces) { _spaces = spaces ?? throw new ArgumentNullException(nameof(spaces)); }

        public event Action Changed;
        public IActionProvider Active => _active;

        public void SetActive(IActionProvider provider)
        {
            if (provider != null)
            {
                var ids = provider.Actions.Concat(_spaces.Actions).Select(a => a.Id).ToList();
                var dup = ids.GroupBy(i => i).FirstOrDefault(g => g.Count() > 1);
                if (dup != null) throw new InvalidOperationException("Id azione duplicato: " + dup.Key);
            }
            _active = provider;
            Changed?.Invoke();
        }

        /// <summary>Da chiamare quando il workspace cambia abilitazioni o contenuto.</summary>
        public void NotifyChanged() => Changed?.Invoke();

        public IReadOnlyList<XrTab> Tabs =>
            (_active?.Tabs ?? Array.Empty<XrTab>()).Concat(_spaces.Tabs).Where(t => !t.Hidden).ToArray();

        /// <summary>Scheda per id, anche se nascosta (schede di elenco dinamiche del workspace).</summary>
        public XrTab FindTab(string id) =>
            (_active?.Tabs ?? Array.Empty<XrTab>()).Concat(_spaces.Tabs).FirstOrDefault(t => t.Id == id);

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

        private IEnumerable<XrAction> All() => (_active?.Actions ?? Enumerable.Empty<XrAction>()).Concat(_spaces.Actions);

        private static HashSet<string> Names(XrAction a) =>
            new HashSet<string>(new[] { a.Label }.Concat(a.Synonyms).Select(ItalianTextNormalizer.Normalize));
    }
}
