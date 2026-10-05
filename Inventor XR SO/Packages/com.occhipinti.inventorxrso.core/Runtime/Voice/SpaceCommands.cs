using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Voice
{
    /// <summary>
    /// Comandi che cambiavano spazio («vai in progettazione», «lamiera», «assieme», «ispeziona»). Dalla M9 gli spazi non si
    /// scelgono piu: la frase non esegue nulla e riceve una spiegazione. Il contesto lo decide il componente aperto.
    /// </summary>
    public static class SpaceCommands
    {
        public const string Explanation =
            "Gli spazi non si scelgono più: apri un componente con il doppio Trigger o di' 'apri <componente>', 'torna' per risalire.";

        private static readonly string[] Names = { "progettazione", "design", "lamiera", "assieme", "assemblaggio", "ispeziona", "ispezione" };
        private static readonly string[] Prefixes =
            { "", "vai in ", "vai a ", "vai nell ", "vai nello ", "passa a ", "passa in ", "modalita ", "spazio " };

        /// <summary>Frasi normalizzate riconosciute come cambio di spazio.</summary>
        public static readonly IReadOnlyCollection<string> Phrases = Build();

        private static IReadOnlyCollection<string> Build()
        {
            var set = new HashSet<string>();
            foreach (var n in Names) foreach (var p in Prefixes) set.Add(ItalianTextNormalizer.Normalize(p + n));
            return set.ToArray();
        }

        /// <summary>Vero se la frase (gia normalizzata o no) e un comando di cambio spazio.</summary>
        public static bool IsSpaceCommand(string transcript) =>
            Phrases.Contains(ItalianTextNormalizer.Normalize(transcript));
    }

    public enum VoiceOpenStatus { Unavailable, NotFound, Ambiguous, Found }

    /// <summary>Esito della ricerca di un componente per nome parlato («apri staffa»).</summary>
    public sealed class VoiceOpenResult
    {
        public VoiceOpenResult(VoiceOpenStatus status, string occurrenceId, string message)
        { Status = status; OccurrenceId = occurrenceId; Message = message ?? ""; }
        public VoiceOpenStatus Status { get; }
        public string OccurrenceId { get; }
        public string Message { get; }
        public bool Found => Status == VoiceOpenStatus.Found;
        public static readonly VoiceOpenResult UnavailableHere =
            new VoiceOpenResult(VoiceOpenStatus.Unavailable, null, "Apri non è disponibile qui: si apre un componente solo da un assieme.");
    }

    /// <summary>Cerca un'occorrenza unica per nome normalizzato (esatto, poi senza il suffisso d'istanza «:1»).</summary>
    public static class OccurrenceNameMatcher
    {
        public static VoiceOpenResult Match(IEnumerable<KeyValuePair<string, string>> idAndName, string spoken)
        {
            string said = ItalianTextNormalizer.Normalize(spoken);
            if (said.Length == 0)
                return new VoiceOpenResult(VoiceOpenStatus.NotFound, null, "Di' 'apri' seguito dal nome del componente.");
            var items = (idAndName ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .Select(kv => new { kv.Key, Full = ItalianTextNormalizer.Normalize(kv.Value), Base = StripInstance(ItalianTextNormalizer.Normalize(kv.Value)) })
                .ToArray();
            var hits = items.Where(i => i.Full == said).ToArray();
            if (hits.Length == 0) hits = items.Where(i => i.Base == said).ToArray();
            if (hits.Length == 1) return new VoiceOpenResult(VoiceOpenStatus.Found, hits[0].Key, "");
            if (hits.Length > 1)
                return new VoiceOpenResult(VoiceOpenStatus.Ambiguous, null, "Più componenti si chiamano così: dillo con il numero o toccalo con il puntatore.");
            return new VoiceOpenResult(VoiceOpenStatus.NotFound, null, "Componente non trovato: " + said + ".");
        }

        // "staffa 1" (da "Staffa:1") -> "staffa": toglie il solo numero finale d'istanza.
        private static string StripInstance(string normalized)
        {
            int space = normalized.LastIndexOf(' ');
            if (space <= 0) return normalized;
            string tail = normalized.Substring(space + 1);
            return tail.All(c => c >= '0' && c <= '9') ? normalized.Substring(0, space) : normalized;
        }
    }
}
