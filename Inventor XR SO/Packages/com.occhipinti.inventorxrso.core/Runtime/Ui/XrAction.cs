using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Ui
{
    public enum XrActionKind { Command, Toggle, Numeric }

    /// <summary>Cosa ha selezionato l'utente: decide le azioni dell'anello contestuale.</summary>
    public enum SelectionKind { None, PlanarFace, Face, Edge, Component }

    /// <summary>Scheda della tavolozza. Gli id che iniziano con "_" sono interni e non si mostrano.</summary>
    public sealed class XrTab
    {
        public XrTab(string id, string label)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id scheda vuoto");
            Id = id; Label = label ?? id;
        }
        public string Id { get; }
        public string Label { get; }
        public bool Hidden => Id.StartsWith("_", StringComparison.Ordinal);
    }

    /// <summary>
    /// Un comando dichiarato da un workspace. Tavolozza, anello, barra di conferma, voce e runner Quest
    /// passano tutti da qui: stessa abilitazione, stesso Invoke.
    /// </summary>
    public sealed class XrAction
    {
        private readonly Func<bool> _enabled;
        private readonly Action _invoke;
        private readonly Func<string> _reason;
        private readonly Func<bool> _isOn;

        public XrAction(string id, string label, string tab, Func<bool> enabled, Action invoke,
            Func<string> disabledReason = null, IEnumerable<string> synonyms = null,
            XrActionKind kind = XrActionKind.Command, string icon = null, Func<bool> isOn = null, bool voiceInvokes = true)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id azione vuoto");
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("etichetta vuota: " + id);
            if (string.IsNullOrWhiteSpace(tab)) throw new ArgumentException("scheda vuota: " + id);
            _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            _reason = disabledReason;
            _isOn = isOn;
            Id = id; Label = label; Tab = tab; Kind = kind; Icon = icon ?? "";
            Synonyms = (synonyms ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            VoiceInvokes = voiceInvokes;
        }

        public string Id { get; }
        public string Label { get; }
        public string Tab { get; }
        public string Icon { get; }
        public XrActionKind Kind { get; }
        public IReadOnlyList<string> Synonyms { get; }
        /// <summary>False per "Applica": la voce la trova ma non la esegue (M5-11).</summary>
        public bool VoiceInvokes { get; }
        public bool Enabled => _enabled();
        public bool IsOn => _isOn != null && _isOn();
        public string DisabledReason => Enabled ? "" : (_reason?.Invoke() ?? "");

        public bool TryInvoke()
        {
            if (!Enabled) return false;
            _invoke();
            return true;
        }
    }

    /// <summary>Id riservati della barra di conferma, dichiarati dal workspace nella scheda CommitTab.</summary>
    public static class CommitIds
    {
        public const string Preview = "commit.preview";
        public const string Apply = "commit.apply";
        public const string Cancel = "commit.cancel";
        public const string Recover = "commit.recover";
    }
}
