# M5 — Evidenze di implementazione

Data: 28 settembre 2026. Stato: **implementazione software completa, M5 non
collaudata né accettata**. Contratto: [specifica M5](superpowers/specs/2026-09-28-inventor-xr-so-m5-design.md).
Sequenza: [piano M5](superpowers/plans/2026-09-28-inventor-xr-so-m5.md).
Decisioni tecniche aperte: [decisioni M5](xr-m5-decisioni.md).

Tutto il codice è stato scritto in un ambiente Linux senza Inventor, Unity né
Quest. Le evidenze sotto sono solo quelle eseguite davvero in quell'ambiente.

## Cosa è implementato

**Backend (`bridge/`)**
- `get_flat_pattern_mesh` / `inventor_get_flat_pattern_mesh` (tier
  sperimentale, sola lettura). Legge lo sviluppo esistente senza mai crearlo,
  registra e ripristina lo stato di edit e pubblica un GLB separato senza ID di
  faccia. Restituisce `flat_pattern_identity` come chiave di cache.
- `POST /voice/transcribe` sull'host HTTP. Usa lo stesso token e lo stesso rate
  limit di `/mcp`. Accetta PCM16 16 kHz mono fino a 10 s, una richiesta alla
  volta. È disattivato di default: il motore vocale è un comando locale
  configurabile (`--voice-command`). Audio e trascrizioni non vanno nei log e i
  buffer vengono azzerati.
- Probe live `bridge/tests/M5LiveProbe` per le decisioni D1 e D4.

**Core XR (`Packages/com.occhipinti.inventorxrso.core/Runtime/`)**
- `Backend/`:
  - `SheetMetalContext` legato a documento e revisione; una lettura stale non
    arma scritture.
  - `SheetMetalOperations` per regola/spessore, faccia, flangia, taglio e
    sviluppo.
  - `FlangeDraft`: manipolatore e campo numerico modificano la stessa bozza; il
    rilascio chiede una preview, mai un commit.
  - `SheetMetalMode`: Lamiera è primaria solo se `is_sheet_metal=true` e usa la
    `DesignSession` condivisa.
  - `FlatPatternView`: cache propria, scarto delle risposte tardive, Detach solo
    di vista.
- `Voice/`:
  - `CommandIds`, parser dei numeri italiani, router a vocabolario chiuso e
    `DictationTarget`.
  - `PushToTalkController` a 150 ms, con cattura limitata a 10 s.
  - `HttpSpeechRecognizer` sul canale già associato e pinnato.
  - `VoiceCommandBridge`: `Applica` detto a voce apre solo la conferma; Undo e
    valori dettati richiedono una conferma fisica.

**Unity (`Assets/XrSo/`)**
- `LamieraWorkspace`, con la tab Lamiera sul polso e il guard CAD condiviso con
  Design e Assembly.
- `FlangeManipulator` (Grip+Trigger solo con Flangia armata).
- `FlatPatternDisplay`: layer non selezionabile, etichetta
  `Sviluppo — sola vista`, Detach.
- Voce: `MicrophoneCapture`, permesso `RECORD_AUDIO`, `PushToTalkInput` sul
  pulsante B, `VoicePanel`, `VoiceRig`.
- `AppController` crea il rig vocale all'avvio della sessione, con lo stesso
  trasporto pinnato del backend, e lo distrugge alla chiusura. Chiude la
  cattura a ogni cambio di workspace e alla perdita di connessione.
- `WorkspaceVoiceTarget` instrada la voce ai pulsanti visibili dei quattro
  workspace CAD e alle schede del menu sul polso. L'azione usa lo stesso
  `onClick` del pulsante, con abilitazione verificata prima dell'esecuzione.

## Lacune note rispetto alla spec

- Il mirror vocale copre i pulsanti visibili di Lamiera, Progettazione, Assieme
  e Ispeziona, con sinonimi italiani controllati (per esempio «estrudi» e
  «estrusione»). Le etichette delle azioni CAD sono in italiano; gli ID wire
  del backend restano invariati. `Applica` richiede il pulsante fisico;
  Annulla/Ripeti modifica e le azioni di revisione richiedono conferma fisica.
  `Isola` resta indisponibile perché non ha ancora un pulsante e un percorso
  backend. La selezione di geometria nello spazio richiede ancora il controller.
- La dettatura numerica usa il campo armato in Lamiera e Progettazione e il
  tastierino modale in Assieme e Ispeziona, mantenendo unità e limiti del campo.
- La direzione del manipolatore flangia viene calibrata sulla preview
  calcolata da Inventor (`FlangeDirection`). Se la preview è ambigua (flange
  sotto circa 30°, forme a L con bracci simili all'altezza) resta la stima con
  l'avviso "direzione stimata". Il campo numerico resta autorevole. Da
  confermare su Inventor con datum esterno, interno e tangente.
- Spigoli per la flangia: dalla lettura del codice, `get_design_context_xr`
  restituisce tutti gli spigoli di ogni body della parte, lamiera compresa,
  fino a 2000. Manca la conferma dal vivo.
- Nella Home il push-to-talk è rifiutato (`AcceptsVoice`): nessun microfono né richiesta
  di permesso, avviso "Voce disponibile solo in sessione". Da provare su Quest.

## Evidenze eseguite

| Suite | Esito | Note |
|---|---|---|
| `bridge/tests/Bimwright.Ipt.Tests` | 941 passati, 10 falliti | i 10 sono i test di path Windows che falliscono su Linux per costruzione |
| `Inventor XR SO/Tests~/XrSo.Core.Tests` | 373 passati, 0 falliti | include il corpus di `stt-bench` e i test HTTPS reali con FakeAddIn |
| `Tools~/stt-bench` | 70 passati | smoke end-to-end solo con motore finto |

## Non eseguito alla stesura iniziale

- Compilazione dell'add-in sperimentale con l'interop reale di Inventor 2027.
- `M5LiveProbe` su Inventor.
- Compilazione Unity e test EditMode; qualche file è stato controllato solo con
  Roslyn contro stub scritti a mano.
- Build dell'APK e runner sul Quest.
- Motori STT reali, microfono e permesso sul Quest, prova del pulsante B.

## Matrice dei gate alla stesura iniziale

Questa matrice precede il run Windows/Quest documentato in fondo al file.
La colonna *Software* indica la parte automatizzabile; i gate fisici restano
aperti dove non verificati.

| ID | Software | Stato |
|---|---|---|
| M5-01 | core: modalità primaria solo per parti lamiera | aperto |
| M5-02 | core: contesto legato a documento/revisione/connessione | aperto |
| M5-03 | core: bozza unica manipolatore/numero; gesto non testato | aperto |
| M5-04 | core: faccia/taglio/regola via preview/Apply/Annulla | aperto |
| M5-05 | core: esiti distinti; multi-body solo sul mapping errori | aperto |
| M5-06 | core: Detach senza chiamate backend | aperto |
| M5-07 | core: stale/tardivo/offline non abilitano Applica | aperto |
| M5-08 | core: rilascio e interruzioni chiudono la cattura | aperto |
| M5-09 | core: vocabolario finito, comandi disabilitati senza mutazione | aperto |
| M5-10 | core: dettatura solo nel campo armato | aperto |
| M5-11 | core: `Applica` vocale non committa | aperto |
| M5-12 | — | aperto |

## Prossimi passi sulla postazione

1. Aprire il progetto Unity, correggere eventuali errori di compilazione ed
   eseguire i test EditMode.
2. Compilare l'add-in con `-p:SoExperimental=true` ed eseguire `M5LiveProbe`
   (D1, D4).
3. Registrare il corpus ed eseguire `stt-bench` (D2); configurare
   `--voice-command` con il motore scelto.
4. Build dell'APK e collaudo sul Quest (D3, M5-01…M5-12), con le regressioni
   M1–M4.

## Runner automatico sul Quest

Dal 29 settembre 2026 la milestone ha un runner in-app e una fixture dedicata secondo lo standard M4: vedi [test automatici sul Quest](xr-quest-acceptance.md). Il 29 settembre, dopo aggiornamento di add-in sperimentale e host HTTPS, `artifacts/m5-verification/quest-acceptance-run-20260929-104230.json` è **PASS COMPLETE** su Quest 3 con Inventor 2027 reale. Copre modalità Lamiera, regola/spessore nativi, preview/Cancel di flangia e Cut, testo vocale iniettato, Apply e Undo/Redo della flangia, stale dopo Undo, sviluppo piano 100 × 79,131 mm e Detach senza chiamata backend. Fixture chiusa senza salvare, APK ordinario reinstallato. Non prova gesto fisico, microfono/audio, rete o percorso completo M5-12; i `NOT COVERED` sono nel log.

Il probe nativo `artifacts/m5-live-probe/m5-live-probe-report.json` ha 19 PASS, 0 FAIL e 1 NOT_RUN (rifiuto multi-body: la fixture non ha prodotto due corpi). Il primo run Quest M5 con l'host precedente (`quest-acceptance-run-20260929-103553.json`) era fallito perché mancava `inventor_get_flat_pattern_mesh`; il run aggiornato supera quel punto.

Il test EditMode `MeshesAndMaterialsAreReleasedWithTheDisplay` è stato corretto:
in EditMode Unity non richiama `OnDestroy` del componente runtime dopo
`DestroyImmediate`, quindi il test ora esercita direttamente quella routine e
controlla root, mesh e materiale. Il test isolato e l'intera suite Unity EditMode
passano: 177/177 (`%TEMP%/xrso-editmode-m5-final.xml`). Nessuna modifica al
comportamento runtime del display.

## Estensione push-to-talk ai workspace CAD — 29 settembre 2026

Il pulsante B destro instrada ora il parlato a tutti i pulsanti disponibili
nel workspace CAD visibile e alle schede del menu sul polso. Il riconoscimento
usa l'etichetta italiana corrente oppure un sinonimo controllato; quando due
azioni hanno lo stesso nome, il comando viene rifiutato. La frase «estrudi»
apre «Estrusione» nella pagina strumenti oppure «Estrudi schizzo» nella pagina
schizzo. «Mostra sviluppo» resta distinguibile da «Crea sviluppo».

La prova fisica precedente sul Quest ha confermato microfono, Vosk italiano,
push-to-talk e apertura di «Flangia». L'estensione agli altri comandi è
verificata da 375 test core e 182 test Unity EditMode; **la pronuncia e
l'usabilità fisica di ogni comando sul Quest restano da collaudare**. I comandi
che richiedono una scelta spaziale attendono la selezione con il controller.

L'APK ordinario finale è stato compilato con esito `Build Successful`, installato
su Quest 3 (`2G0YC1ZFB407P1`) e avviato; il processo dell'app è presente.
SHA-256 dell'APK installato: `4B0249CF7E508322780E9696BCDA70A3C1F6DB3DEF07532F02A01211843D02C5`.
La suite EditMode finale è `182/182` (`%TEMP%/xrso-voice-italian-final2.xml`).
