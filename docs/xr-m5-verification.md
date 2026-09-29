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
- `WorkspaceVoiceTarget` collega la voce solo a Lamiera. In Design, Assembly e
  Ispeziona ogni comando vocale risponde "Comando non disponibile in questa
  modalità".

## Lacune note rispetto alla spec

- Il mirror vocale copre ora Lamiera, Design (`Crea schizzo`, `Raccordo`, `Smusso`,
  Annulla/Ripeti, `Annulla comando`, `Applica` solo come avviso), Assembly
  (Annulla/Ripeti, `Annulla comando`, `Applica` solo come avviso) e Ispeziona
  (`Misura`). Resta aperto `Isola`: in Ispeziona non esiste alcun pulsante né
  percorso backend, quindi il comando è disabilitato. Il codice è verificato
  solo per lettura (Unity non disponibile): compilazione e collaudo su Quest
  restano aperti.
- La dettatura numerica in Design vale solo per il campo "Dimensione" della
  pagina Estrusione/Raccordo/Smusso/Foro cieco; Assembly e Ispeziona usano
  solo il tastierino modale.
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

## Non eseguito

- Compilazione dell'add-in sperimentale con l'interop reale di Inventor 2027.
- `M5LiveProbe` su Inventor.
- Compilazione Unity e test EditMode; qualche file è stato controllato solo con
  Roslyn contro stub scritti a mano.
- Build dell'APK e runner sul Quest.
- Motori STT reali, microfono e permesso sul Quest, prova del pulsante B.

## Matrice dei gate

Nessun gate è `PASS`: la colonna *Software* dice solo quale parte automatizzabile
è coperta da test eseguiti.

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
