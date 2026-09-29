using InventorXrSo.Core.Session;
using System;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Backend;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Every user-visible string (MVP language: Italian).</summary>
    public static class UiText
    {
        public const string AppTitle = "Inventor XR SO";
        public const string NotPaired = "Nessun PC associato";
        public const string NotPairedBody = "Scansiona il QR di associazione mostrato sul PC oppure inserisci il codice a 6 cifre.";
        public const string PairWithQr = "Scansiona QR";
        public const string PairWithCode = "Inserisci codice";
        public const string EnterPcAddress = "Indirizzo del PC";
        public const string EnterPcAddressHint = "Come mostrato sul PC, es. 192.168.1.20:8443";
        public const string EnterCode = "Codice di associazione";
        public const string EnterCodeHint = "Le 6 cifre mostrate sul PC";
        public const string CheckFingerprint = "Confronta il certificato";
        public const string CheckFingerprintBody = "Deve coincidere con quello mostrato sul PC:\n";
        public const string FingerprintMatches = "Coincide";
        public const string Cancel = "Annulla";
        public const string KeyOk = "OK";
        public const string KeyBack = "⌫";
        public const string Pairing = "Associazione in corso…";
        public const string ScanQr = "Inquadra il QR mostrato sul PC";
        public const string CameraDenied = "Permesso fotocamera negato: usa il codice.";
        public const string NoCamera = "Fotocamera non disponibile: usa il codice.";
        public const string QrTimeout = "QR non trovato: riprova o usa il codice.";
        public const string QrInvalid = "Questo QR non è un codice di associazione Inventor SO.";
        public const string BadAddress = "Indirizzo non valido.";
        public const string CertificateChanged = "Il PC presenta un certificato diverso da quello associato. Associa di nuovo il PC.";
        public const string TokenRevoked = "Il PC non riconosce più questo visore. Associa di nuovo il PC.";
        public const string EnterMixedReality = "Entra · Realtà mista";
        public const string EnterStudio = "Entra · Studio virtuale";
        public const string ForgetPc = "Dimentica PC";
        public const string Home = "Home";
        public const string Offline = "Offline · sola lettura";
        public const string OfflineNoSelection = "Offline: selezione non disponibile";
        public const string NoFaceHere = "Nessuna faccia in questo punto";
        public const string SelectionFailed = "Selezione non più valida: riprova sul modello aggiornato.";
        public const string ConnectionFailed = "Connessione non riuscita. Controlla indirizzo, rete e server sul PC.";
        public const string PairingExpired = "Codice scaduto. Genera una nuova associazione sul PC.";
        public const string PairingUsed = "Codice già utilizzato. Genera una nuova associazione sul PC.";
        public const string PairingInvalid = "Codice non valido. Controlla le cifre mostrate sul PC.";

        public static string Error(Exception error)
        {
            if (error is CertificateRejectedException) return CertificateChanged;
            if (error is PairingException pairing)
            {
                if (pairing.Code == "PAIRING_EXPIRED") return PairingExpired;
                if (pairing.Code == "PAIRING_USED") return PairingUsed;
                if (pairing.Code == "PAIRING_INVALID") return PairingInvalid;
            }
            return ConnectionFailed;
        }
        public const string Omitted = " componenti non mostrati (mesh troppo grandi o oltre il limite)";
        public const string PcLabel = "PC: ";
        public const string BackendLabel = "Backend: ";
        public const string InventorLabel = "Inventor: ";
        public const string DocumentLabel = "Documento: ";
        public const string ExperimentalOff = "Abilita il tier sperimentale sull'add-in (INVENTOR_SO_EXPERIMENTAL=1) e avvia il server con --enable-experimental.";

        public static string ReadinessHint(CapabilitiesInfo capabilities)
        {
            if (capabilities == null || capabilities.IsXrReady) return "";
            if (!capabilities.Reachable) return "Il server risponde, ma Inventor non è disponibile. Controlla che Inventor e il suo componente di collegamento siano avviati sul PC.";
            if (!capabilities.ServerExperimentalEnabled || !capabilities.AddInExperimentalEnabled) return ExperimentalOff;
            return "Il collegamento a Inventor non supporta tutte le funzioni XR richieste. Aggiorna il componente di collegamento sul PC.";
        }

        public static string VersionLabel(string version)
        {
            if (string.IsNullOrEmpty(version)) return "-";
            int metadata = version.IndexOf('+');
            return metadata >= 0 && version.Length > metadata + 8 ? version.Substring(0, metadata + 8) : version;
        }

        public static string DocumentKind(string kind)
        {
            switch (kind)
            {
                case "assembly": return "Assieme";
                case "part": return "Parte";
                case "drawing": return "Tavola";
                default: return "-";
            }
        }

        public static string Status(SessionStatus status)
        {
            switch (status)
            {
                case SessionStatus.Idle: return "In attesa";
                case SessionStatus.Connecting: return "Connessione…";
                case SessionStatus.NotReady: return "Inventor non pronto";
                case SessionStatus.NoDocument: return "Nessun documento aperto in Inventor";
                case SessionStatus.Online: return "Connesso";
                case SessionStatus.Offline: return Offline;
                case SessionStatus.NeedsPairing: return "Associazione richiesta";
                default: return status.ToString();
            }
        }
    }
}
