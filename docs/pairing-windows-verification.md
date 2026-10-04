# Pairing Windows e Inventor — verifica del 4 ottobre 2026

Spec: `superpowers/specs/2026-10-04-pairing-windows-inventor-design.md`.
Piano: `superpowers/plans/2026-10-04-pairing-windows-inventor.md`.

## Implementazione

- `bridge/src/pairing-control`: protocollo locale v1, timeout e limite 32 KiB,
  ACL riservata al proprietario e negazione dei logon di rete. Nessuna route HTTP amministrativa.
- `bridge/src/server-http/Pairing`: finestra con id, stato e annullamento condizionato;
  QR e codice condividono consumo e TTL. Riscatto/persistenza serializzati con rigenerazione.
  Il file dei token viene sostituito atomicamente prima di attivare la credenziale;
  un lock su file protegge anche da scritture di altri processi.
- `bridge/src/pairing-desktop`: WinForms, singola istanza, attivazione da altre aperture,
  scelta LAN e target, riuso/avvio nascosto host, QR in memoria e ingrandimento,
  codice, indirizzo con porta, impronta e conto alla rovescia. Chiusura con annullamento
  e server indipendente. Connessione MCP indicata come non disponibile.
- `bridge/src/plugin-so27/PairingRibbon.cs`: scheda Inventor SO, pulsante Associa visore
  su ribbon disponibili, callback rimossi in disattivazione, ripristino dopo reset ribbon.
  Avvio processo su worker senza accesso COM fuori dallo STA.
  SO27 aggiorna il documento del descriptor tramite timer WinForms sullo STA ogni
  due secondi e scrive solo quando titolo o percorso cambiano. Il descriptor
  precedente restava fermo al documento presente all'attivazione.
- Build/install: desktop self-contained win-x64, server HTTP, collegamento Start,
  conservazione manifest e collegamento precedenti. L'host HTTPS richiede ASP.NET Core Runtime 8 x64.

## Evidenza automatica

Comandi:

```powershell
# Il test della configurazione di default richiede tier sperimentale disattivato nel processo di test.
$env:INVENTOR_SO_EXPERIMENTAL = '0'
dotnet test bridge/tests/Bimwright.Ipt.Tests
dotnet test bridge/tests/Pairing.Desktop.Tests
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
dotnet build bridge/src/plugin-so27
dotnet build bridge/src/plugin-so27 -p:SoExperimental=true
./scripts/build-inventor-so.ps1 -Experimental
```

Il flag è stato isolato nella shell del test e ripristinato; nessuna modifica alla
configurazione persistente dell'utente. La prima esecuzione completa ha trovato
solo il test preesistente che presumeva il flag disattivato; la ripetizione isolata è passata.

Esiti conclusivi: **985/985 test backend**, **3/3 test desktop Windows** e
**459/459 test core XR**, tutti senza test saltati. Build SO27 default ed
experimental riuscite senza warning; pubblicazione completa riuscita. La suite
backend conserva i warning preesistenti in FakeAddIn e ExperimentalSourceTests.
Log TRX conservati in `artifacts/pairing-verification/{backend,desktop,xr-core}.trx`.

Il test processo
Windows avvia un host separato con certificato/registro temporanei e target
inesistente, verifica PID prima di amministrarlo e termina soltanto il processo
creato dal test. Si dichiara saltato se un host dell'utente è già attivo o manca la LAN;
non deve essere contato come evidenza in quei casi. Nessun documento CAD viene aperto o modificato.

| Gate | Evidenza e limite |
|---|---|
| P01 | Build desktop/ribbon/installer; installazione reale e collegamento Start verificati; attivazione singola istanza e ribbon live aperti |
| P02 | HTTP HTTPS reale, pin del certificato, token persistente, riapertura senza riavvio; Quest fisico aperto |
| P03 | Form reale mostra sei cifre raggruppate, IP/porta e impronta completa; percorso manuale fisico aperto |
| P04 | Unitari TTL, consumo e cinque errori; form reale rimuove QR e codice alla scadenza senza rigenerarli |
| P05 | Annullo con id, rigenerazione, perdita indirizzo; test form reale via pipe e concorrenza backend |
| P06 | HTTP reale e test processo aprono un secondo pairing nello stesso host; credenziale precedente conservata |
| P07 | Validazione indirizzi e perdita rete con indirizzi sintetici; UI richiede scelta se ambigua; più schede fisiche aperto |
| P08 | Stato senza Inventor, target fail-closed e avviso UI da istanza chiamante diversa verificati automaticamente; ribbon da più istanze fisiche aperto |
| P09 | Test errore scrittura restituisce 503 senza token attivo; riscatto concorrente e rigenerazione serializzati |
| P10 | Test pipe locale, frame invalidi, protocollo e ACL directory; ACL pipe esplicita nega Network SID; prova da secondo account/rete aperta |
| P11 | Form reale e immagine renderizzata nel test Windows; DPI reali 150%/200%, tastiera e ribbon aperti |
| P12 | NOT COVERED: prova fisica con Quest e persona non eseguita |

Immagine del form da fixture temporanea: `artifacts/pairing-verification/pairing-window.png`.
Codice e QR raffigurati sono stati invalidati nel test e non riguardano il profilo utente.

## Consegna e limiti

Pacchetto installato su richiesta dell'utente il 4 ottobre 2026. La sessione Inventor aperta non è stata chiusa,
riavviata o aggiornata: il manifest punta al nuovo pacchetto per il prossimo avvio. Non sono state modificate regole firewall. Non è stato
installato un APK diverso e non è stato eseguito un runner sul Quest.

Pacchetto consegnato: `artifacts/inventor-so-mcp-20261004-112654/`, con tier
sperimentale incluso per le funzionalità XR esistenti. Il server avviato dalla
finestra abilita quel tier; l'add-in conserva il gate runtime già previsto dal progetto.
Contenuti del pacchetto verificati, incluso runtime desktop x64 e assenza della
DLL interop Autodesk redistribuibile. L'aggiornamento del descriptor è stato
ricompilato in entrambe le configurazioni e ripubblicato nel pacchetto finale.

Installazione verificata in
`C:\Users\salva\AppData\Local\InventorSO\packages\20261004-113408-a2751720`.
Il manifest precedente è conservato come `previous-manifest.xml` nello stesso
pacchetto. Hash di add-in, server HTTP e applicazione installati uguali alla build;
collegamento Start **Inventor SO → Connessione visore** verificato. Runtime ASP.NET
Core 8 x64 già disponibile. Evidenza: `artifacts/pairing-verification/installation.json`.

Installazione dalla radice del repository:

```powershell
./scripts/install-inventor-so.ps1 -Package ./artifacts/inventor-so-mcp-20261004-112654
```

Dopo installazione e riavvio normale di Inventor, usare **Inventor SO → Associa
visore**; dal menu Start usare **Inventor SO → Connessione visore**. Per avviare
solo la finestra prima dell'installazione si può eseguire
`artifacts/inventor-so-mcp-20261004-112654/pairing-desktop/Inventor.So.Pairing.exe`.

L'applicazione gestisce un solo profilo host per utente. Un host già attivo conserva
il proprio target. Se la porta 8443 è usata da un host CLI senza controllo locale,
la UI lo segnala senza avviarne un duplicato. Il pulsante **Scollega altri server**
permette di selezionare e fermare i backend HTTP Inventor SO del proprio utente,
incluso un host gestito se si vuole ripartire con un altro target. Gestione/revoca dei
dispositivi e indicatore di sessione MCP restano fuori dalla prima versione.

## Correzione conflitto porta e scollegamento server — 4 ottobre 2026

La porta 8443 era occupata dal PID 24896, un host CLI avviato con il pacchetto
`inventor-so-mcp-20260930-150215` e il profilo temporaneo `xrso-quest-live`.
Il controllo locale non era presente in quel vecchio host. Era presente una sola
istanza Inventor, successivamente chiusa dall'utente durante la correzione.

Il nuovo pulsante elenca solo gli eseguibili `Inventor.So.Mcp.Http.exe` oppure
`dotnet.exe` il cui punto di ingresso effettivo è il file assoluto esistente
`Inventor.So.Mcp.Http.dll`, appartenenti al SID Windows corrente. Prima di fermare
un processo ricontrolla PID, data avvio, eseguibile, command line e SID,
mantenendo il relativo handle. Non termina alberi di processi né `Inventor.exe`.
La lista mostra PID e percorso, senza argomenti che potrebbero contenere
credenziali. Nessuna selezione iniziale; la conferma interrompe solo i backend
selezionati e riprova l'avvio della connessione.

Verifica automatica: **7/7 test desktop**, comprendenti la finestra WinForms,
riconoscimento del punto di ingresso, esclusione di programmi estranei,
rifiuto di un'identità di avvio diversa e arresto di un host HTTP isolato reale.
Evidenza: `artifacts/pairing-verification/desktop-server-disconnect.trx`.

Aggiornamento della sola applicazione desktop installata, con backup completo
`pairing-desktop-before-disconnect-20261004-115139` nello stesso pacchetto.
L'add-in non è stato sostituito. Il vecchio PID 24896 è stato terminato dopo
verifica di proprietario, command line, data avvio e possesso della porta.
Finestra riaperta: stato **Server pronto**, pulsante **Scollega altri server**
presente, host gestito sulla porta 8443 con stato locale `ready`. Nessun codice generato
automaticamente e nessun processo Inventor terminato dalla correzione.
Evidenza: `disconnect-installation.json` e `disconnect-status.json`.

## Correzione selezione rete durante aggiornamento stato — 4 ottobre 2026

Il polling ogni secondo attraversava lo stesso blocco UI dei comandi espliciti,
disabilitando brevemente il combo della rete e chiudendone il menu. Ora il
polling mantiene attivi i controlli; il semaforo continua a serializzare le
richieste. Gli indirizzi vengono confrontati per valore e il loro elenco viene
aggiornato solo quando il menu è chiuso. Stato e scadenza continuano ad aggiornarsi
anche con il menu aperto. La selezione viene conservata per indirizzo IP.

**8/8 test desktop passati**, incluso un test WinForms con risposta di stato
rallentata e comparsa di una terza scheda: menu aperto e abilitato per più tick,
nessun aggiornamento dell'elenco durante la scelta, successivo aggiornamento
dell'elenco con mantenimento della rete selezionata. Le verifiche di pairing,
annullamento, scadenza e arresto sicuro dei backend continuano a passare.
Evidenza: `artifacts/pairing-verification/desktop-network-dropdown.trx`.

Aggiornata la sola applicazione desktop del collegamento Start, conservando un
backup nello stesso pacchetto; dettagli e hash in `network-fix-installation.json`.
La finestra viene riaperta, senza sostituire l'add-in né fermare il server HTTP.
