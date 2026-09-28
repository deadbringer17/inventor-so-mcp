using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Voice
{
    /// <summary>Errore di riconoscimento con messaggio italiano leggibile; Code e il codice del server o locale.</summary>
    public sealed class VoiceRecognitionException : Exception
    {
        public VoiceRecognitionException(string code, string message) : base(message) { Code = code; }
        public string Code { get; }
    }

    /// <summary>
    /// Riconoscitore che invia il PCM16 16 kHz mono a POST /voice/transcribe del PC associato, attraverso lo
    /// stesso trasporto (canale con pinning del certificato) e lo stesso token Bearer delle altre chiamate.
    /// Mai un altro host. Audio e trascrizione non sono loggati; il buffer di byte e azzerato a fine richiesta.
    /// Del server si usa solo il codice d'errore: il testo del server non arriva mai all'utente.
    /// </summary>
    public sealed class HttpSpeechRecognizer : ISpeechRecognizer
    {
        public const string Route = "/voice/transcribe";
        public const string ContentType = "audio/L16;rate=16000";
        public const int MaxSamples = 16000 * 10;   // limite del server: 10 s
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

        private readonly IHttpTransport _transport;
        private readonly PairedServer _server;
        private readonly TimeSpan _timeout;

        public HttpSpeechRecognizer(IHttpTransport transport, PairedServer server, TimeSpan? timeout = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _timeout = timeout ?? DefaultTimeout;
        }

        public async Task<string> RecognizeAsync(short[] pcm16, CancellationToken cancellationToken)
        {
            if (pcm16 == null || pcm16.Length == 0) throw new VoiceRecognitionException("VOICE_EMPTY", Describe("VOICE_EMPTY"));
            if (pcm16.Length > MaxSamples) throw new VoiceRecognitionException("VOICE_TOO_LARGE", Describe("VOICE_TOO_LARGE"));

            var body = new byte[pcm16.Length * 2];
            for (int i = 0; i < pcm16.Length; i++)
            {
                body[2 * i] = (byte)(pcm16[i] & 0xFF);
                body[2 * i + 1] = (byte)((pcm16[i] >> 8) & 0xFF);
            }
            var request = new TransportRequest("POST", _server.BaseUrl + Route) { Body = body, Timeout = _timeout };
            request.Headers["Content-Type"] = ContentType;
            request.Headers["Authorization"] = "Bearer " + _server.Token;

            TransportResponse response;
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(_timeout + TimeSpan.FromSeconds(2));
                try { response = await _transport.SendAsync(request, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new VoiceRecognitionException("TIMEOUT", Describe("TIMEOUT")); }
                catch (CertificateRejectedException) { throw new VoiceRecognitionException("CERTIFICATE_CHANGED", Describe("CERTIFICATE_CHANGED")); }
                catch (TransportException) { throw new VoiceRecognitionException("NETWORK", Describe("NETWORK")); }
                finally { Array.Clear(body, 0, body.Length); }
            }

            if (response.IsSuccess)
            {
                string transcript;
                try { transcript = (string)JObject.Parse(response.Text)["transcript"]; }
                catch (Exception) { throw new VoiceRecognitionException("BAD_RESPONSE", Describe("BAD_RESPONSE")); }
                return transcript ?? "";
            }
            string code = ServerCode(response);
            throw new VoiceRecognitionException(code, Describe(code));
        }

        /// <summary>Solo un codice noto del server; il resto del corpo e ignorato.</summary>
        private static string ServerCode(TransportResponse response)
        {
            if (response.Status == 401) return "UNAUTHORIZED";
            try
            {
                var code = (string)JObject.Parse(response.Text)["code"];
                if (code != null && Known(code)) return code;
            }
            catch (Exception) { /* corpo non JSON: si usa lo stato HTTP */ }
            if (response.Status == 429) return "VOICE_BUSY";
            if (response.Status == 504) return "TIMEOUT";
            if (response.Status == 503) return "VOICE_ENGINE_UNAVAILABLE";
            return "VOICE_ENGINE_FAILED";
        }

        private static bool Known(string code)
        {
            switch (code)
            {
                case "VOICE_DISABLED": case "VOICE_ENGINE_UNAVAILABLE": case "VOICE_ENGINE_TIMEOUT": case "VOICE_ENGINE_FAILED":
                case "VOICE_BUSY": case "VOICE_UNSUPPORTED_FORMAT": case "VOICE_EMPTY": case "VOICE_BAD_AUDIO": case "VOICE_TOO_LARGE":
                    return true;
                default: return false;
            }
        }

        public static string Describe(string code)
        {
            switch (code)
            {
                case "VOICE_DISABLED": return "Il riconoscimento vocale non e abilitato sul PC. I comandi manuali restano disponibili.";
                case "VOICE_ENGINE_UNAVAILABLE": return "Il motore vocale sul PC non e disponibile. I comandi manuali restano disponibili.";
                case "TIMEOUT":
                case "VOICE_ENGINE_TIMEOUT": return "Il riconoscimento ha impiegato troppo tempo. Riprova.";
                case "VOICE_BUSY": return "Un altro riconoscimento e in corso. Riprova tra un attimo.";
                case "VOICE_TOO_LARGE": return "Frase troppo lunga: massimo 10 secondi.";
                case "VOICE_EMPTY": return "Nessun audio registrato.";
                case "VOICE_BAD_AUDIO":
                case "VOICE_UNSUPPORTED_FORMAT": return "Formato audio non accettato dal PC.";
                case "UNAUTHORIZED": return "Il PC non riconosce piu questo visore. Associa di nuovo il PC.";
                case "CERTIFICATE_CHANGED": return "Il PC presenta un certificato diverso da quello associato. Associa di nuovo il PC.";
                case "NETWORK": return "PC non raggiungibile. I comandi manuali restano disponibili.";
                default: return "Riconoscimento non riuscito. I comandi manuali restano disponibili.";
            }
        }
    }
}
