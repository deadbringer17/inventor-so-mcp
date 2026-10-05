using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Voice
{
    /// <summary>Stato di abilitazione di un comando, lo stesso del pulsante manuale corrispondente.</summary>
    public sealed class CommandAvailability
    {
        public bool Enabled { get; }
        /// <summary>Motivo italiano quando disabilitato.</summary>
        public string Reason { get; }
        private CommandAvailability(bool enabled, string reason) { Enabled = enabled; Reason = reason ?? string.Empty; }
        public static readonly CommandAvailability Available = new CommandAvailability(true, "");
        public static CommandAvailability Disabled(string reason) => new CommandAvailability(false, reason);
    }

    /// <summary>Fornita dalla UI, alimentata dallo stesso stato di abilitazione dei pulsanti.
    /// Non deve rientrare nel controller vocale.</summary>
    public interface ICommandAvailability
    {
        CommandAvailability GetAvailability(string commandId);
    }

    public sealed class DelegateCommandAvailability : ICommandAvailability
    {
        private readonly Func<string, CommandAvailability> _fn;
        public DelegateCommandAvailability(Func<string, CommandAvailability> fn) { _fn = fn ?? throw new ArgumentNullException(nameof(fn)); }
        public CommandAvailability GetAvailability(string commandId) => _fn(commandId);
    }

    public enum VoiceRouteKind
    {
        /// <summary>Fuori vocabolario, vuoto o ambiguo: nessuna azione.</summary>
        Rejected,
        /// <summary>Comando riconosciuto; eseguibile solo se Enabled.</summary>
        Recognized,
        /// <summary>"Annulla" senza bozza attiva: chiedere se si intende Undo, senza eseguirlo.</summary>
        NeedsConfirmation,
        /// <summary>"Applica": mostra la conferma del piano; il commit richiede la pressione fisica di Applica.</summary>
        ShowApplyConfirmation,
    }

    public enum VoiceRejectReason { None, Empty, NotInVocabulary, SpaceCommand }

    public sealed class VoiceRouteResult
    {
        public VoiceRouteKind Kind { get; private set; }
        public string CommandId { get; private set; }
        public bool Enabled { get; private set; }
        /// <summary>Spiegazione italiana per la UI (disabilitato, rifiutato o conferma).</summary>
        public string Reason { get; private set; } = string.Empty;
        public VoiceRejectReason RejectReason { get; private set; }
        public string Normalized { get; private set; } = string.Empty;

        /// <summary>True solo se la UI puo invocare subito il comando come se premesse il pulsante.
        /// Mai vero per Applica ne per le conferme.</summary>
        public bool CanInvoke => Kind == VoiceRouteKind.Recognized && Enabled;
        /// <summary>True se serve una pressione fisica (Applica, conferma Undo).</summary>
        public bool RequiresPhysicalConfirmation => Kind == VoiceRouteKind.ShowApplyConfirmation || Kind == VoiceRouteKind.NeedsConfirmation;

        private VoiceRouteResult() { }

        internal static VoiceRouteResult Rejected(VoiceRejectReason why, string normalized, string reason)
            => new VoiceRouteResult { Kind = VoiceRouteKind.Rejected, RejectReason = why, Normalized = normalized, Reason = reason };
        internal static VoiceRouteResult Recognized(string id, CommandAvailability a, string normalized)
            => new VoiceRouteResult
            {
                Kind = VoiceRouteKind.Recognized, CommandId = id, Enabled = a.Enabled, Normalized = normalized,
                Reason = a.Enabled ? "" : (string.IsNullOrEmpty(a.Reason) ? "Comando non disponibile ora." : a.Reason),
            };
        internal static VoiceRouteResult Confirm(VoiceRouteKind kind, string id, string normalized, string message)
            => new VoiceRouteResult { Kind = kind, CommandId = id, Enabled = true, Normalized = normalized, Reason = message };
    }

    /// <summary>
    /// Router a vocabolario finito: corrispondenza esatta dopo normalizzazione, nessun testo libero.
    /// Non esegue nulla: restituisce cosa la UI deve proporre.
    /// </summary>
    public sealed class VoiceCommandRouter
    {
        // command ID -> alias italiani (il primo e il nome del pulsante nel mirror).
        private static readonly KeyValuePair<string, string[]>[] Vocabulary =
        {
            V(CommandIds.Flange, "flangia", "fai una flangia", "crea una flangia", "aggiungi flangia"),
            V(CommandIds.FlatPatternCreate, "crea sviluppo", "sviluppo piano", "fai lo sviluppo", "crea lo sviluppo"),
            V(CommandIds.SheetMetalFace, "faccia lamiera", "crea faccia", "aggiungi faccia", "crea una faccia"),
            V(CommandIds.SheetMetalCut, "taglio lamiera", "fai un taglio", "crea un taglio", "taglio"),
            V(CommandIds.SheetMetalRule, "regola lamiera", "regola spessore", "spessore lamiera", "imposta spessore"),
            V(CommandIds.Chamfer, "smusso", "fai uno smusso", "crea uno smusso", "smussa lo spigolo"),
            V(CommandIds.Fillet, "raccordo", "fai un raccordo", "crea un raccordo", "aggiungi raccordo"),
            V(CommandIds.Measure, "misura", "misurazione", "fai una misura", "avvia misura"),
            V(CommandIds.Isolate, "isola", "isola componente", "isola il pezzo", "fai isolamento"),
            V(CommandIds.CancelDraft, "annulla", "annulla bozza", "annulla operazione", "torna indietro"),
            V(CommandIds.Apply, "applica", "conferma", "applica modifica", "applica operazione"),
            V(CommandIds.Undo, "annulla ultima modifica", "annulla ultima operazione", "annulla l ultima modifica", "undo"),
            V(CommandIds.Redo, "ripeti", "rifai", "ripristina", "ripeti ultima modifica"),
            V(CommandIds.CreateSketch, "crea schizzo", "nuovo schizzo", "fai uno schizzo", "crea uno schizzo"),
        };

        // Alias di "annulla" generico: dipendono dal contesto (bozza attiva oppure Undo).
        private static readonly HashSet<string> ContextualCancel = new HashSet<string> { "annulla", "annulla operazione", "torna indietro" };

        private static readonly Dictionary<string, string> Index = BuildIndex();

        private static KeyValuePair<string, string[]> V(string id, params string[] aliases) => new KeyValuePair<string, string[]>(id, aliases);

        private static Dictionary<string, string> BuildIndex()
        {
            var index = new Dictionary<string, string>();
            foreach (var kv in Vocabulary)
            {
                if (!CommandIds.IsKnown(kv.Key)) throw new InvalidOperationException("command ID sconosciuto: " + kv.Key);
                foreach (var a in kv.Value)
                {
                    string key = ItalianTextNormalizer.Normalize(a);
                    if (index.TryGetValue(key, out var other) && other != kv.Key) throw new InvalidOperationException("alias ambiguo: " + key);
                    index[key] = kv.Key;
                }
            }
            return index;
        }

        /// <summary>Alias di un comando (il primo e il nome del pulsante). Vuoto se sconosciuto.</summary>
        public IReadOnlyList<string> AliasesOf(string commandId)
        {
            foreach (var kv in Vocabulary) if (kv.Key == commandId) return Array.AsReadOnly(kv.Value);
            return Array.Empty<string>();
        }

        /// <summary>Tutte le frasi normalizzate del vocabolario (per grammatiche vincolate del motore STT).</summary>
        public IReadOnlyCollection<string> GrammarPhrases => Index.Keys.Concat(SpaceCommands.Phrases).ToArray();

        public VoiceRouteResult Route(string transcript, ICommandAvailability availability)
        {
            if (availability == null) throw new ArgumentNullException(nameof(availability));
            string key = ItalianTextNormalizer.Normalize(transcript);
            if (key.Length == 0)
                return VoiceRouteResult.Rejected(VoiceRejectReason.Empty, key, "Nessun comando riconosciuto.");
            if (SpaceCommands.IsSpaceCommand(key))   // M9: gli spazi non si scelgono piu; nessuna azione, solo la spiegazione
                return VoiceRouteResult.Rejected(VoiceRejectReason.SpaceCommand, key, SpaceCommands.Explanation);
            if (!Index.TryGetValue(key, out string id))
                return VoiceRouteResult.Rejected(VoiceRejectReason.NotInVocabulary, key, "Comando non riconosciuto. Usa i pulsanti oppure ripeti.");

            if (id == CommandIds.CancelDraft && ContextualCancel.Contains(key))
            {
                var draft = availability.GetAvailability(CommandIds.CancelDraft) ?? CommandAvailability.Disabled("");
                if (draft.Enabled) return VoiceRouteResult.Recognized(id, draft, key);
                var undo = availability.GetAvailability(CommandIds.Undo) ?? CommandAvailability.Disabled("");
                if (undo.Enabled)
                    return VoiceRouteResult.Confirm(VoiceRouteKind.NeedsConfirmation, CommandIds.Undo, key,
                        "Nessuna bozza attiva. Annullare l'ultima modifica? Conferma con il pulsante.");
                return VoiceRouteResult.Recognized(id, CommandAvailability.Disabled("Nessuna bozza attiva e nulla da annullare."), key);
            }

            var av = availability.GetAvailability(id) ?? CommandAvailability.Disabled("");
            if ((id == CommandIds.Undo || id == CommandIds.Redo) && av.Enabled)
                return VoiceRouteResult.Confirm(VoiceRouteKind.NeedsConfirmation, id, key,
                    "Conferma " + (id == CommandIds.Undo ? "Annulla modifica" : "Ripeti modifica") + " con il pulsante.");
            if (id == CommandIds.Apply && av.Enabled)
                return VoiceRouteResult.Confirm(VoiceRouteKind.ShowApplyConfirmation, id, key,
                    "Conferma il piano premendo il pulsante Applica: la voce non esegue il commit.");
            return VoiceRouteResult.Recognized(id, av, key);
        }
    }
}
