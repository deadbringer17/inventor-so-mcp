# inventor-so-mcp — istruzioni per agenti

Gateway MCP per **Autodesk Inventor 2027 (Windows x64)** più il client
**Inventor XR SO** per Meta Quest 3. Questo file vale per tutto il repository;
`bridge/CLAUDE.md` aggiunge le regole di dettaglio del codice C# e ha la
precedenza dentro `bridge/`.

## Regola di lavoro: subagent Sonnet 5.5 per programmare

Se l'agente è **Claude Code**, i task di programmazione (implementazione,
refactoring, fix di bug, scrittura di test) vanno eseguiti lanciando
**subagent con modello Sonnet 5.5** (tool `Agent`, `model: "sonnet"`).

- L'agente principale orienta, pianifica, divide il lavoro e verifica il
  risultato; il codice lo scrivono i subagent.
- Ogni subagent riceve un compito circoscritto: file coinvolti, spec/piano di
  riferimento (percorso esatto), criterio di accettazione e comando di test da
  eseguire.
- Task indipendenti su file diversi possono girare in parallelo; task che
  toccano gli stessi file vanno in sequenza (o con `isolation: "worktree"`).
- L'agente principale rilegge il diff di ogni subagent e riesegue i test prima
  di committare: l'output di un subagent non è una prova.

Domande, lettura di codice e piccole modifiche a documentazione non richiedono
subagent.

## Mappa del repository

```
inventor-so-mcp/
├── bridge/            ← IMPLEMENTAZIONE DI PRODUZIONE (C#, Apache-2.0, da bimwright/ipt-mcp)
├── Inventor XR SO/    ← client Unity per Quest 3 (M1–M5)
├── docs/              ← stato, spec, piani, collaudi
├── scripts/           ← build/install del pacchetto e smoke test live (richiedono Inventor)
├── skills/            ← skill di modellazione Inventor (SKILL.md)
├── src/               ← server Python MIT originale: SOLO riferimento, non produzione
├── assets/            ← logo
├── README.md          ← installazione del pacchetto su Windows
├── llms-install.md    ← istruzioni di installazione per LLM
└── pyproject.toml     ← solo per src/ (Python)
```

### `bridge/` — gateway MCP C# (produzione)

Due processi: server MCP (.NET 8, nessun riferimento a Inventor) e add-in
dentro `Inventor.exe` (per 2027: .NET 10), collegati da Named Pipe/TCP con
NDJSON. Dettagli, pattern e decisioni: **`bridge/CLAUDE.md`** e
`bridge/ARCHITECTURE.md`.

| Percorso | Contenuto |
|---|---|
| `bridge/src/server/` | server MCP stdio: `Tools/` (tool `inventor_*` per dominio), `ToolContracts.cs` (contratto e tier di ogni tool), `Assets/` (GLB), `Planning/` (plan/commit), `Audit/`, `Events/` |
| `bridge/src/server-http/` | host HTTP Streamable (usato dal Quest): pairing, TLS, `/assets`, viewer WebXR |
| `bridge/src/shared/Contracts/` | contratti API-agnostici (envelope, error code, `CadBatchCommandCatalog.cs`) |
| `bridge/src/shared/Handlers/` | un handler per comando wire, per dominio (`Sketch/`, `Feature/`, `SheetMetal/`, `Assembly/`, …); `Experimental/` = tier non ancora collaudato |
| `bridge/src/shared/{Plugin,Transport,Infrastructure}/` | add-in, marshalling STA, listener pipe/TCP |
| `bridge/src/plugin-so27/` | **add-in Inventor SO 2027, il target reale** |
| `bridge/src/plugin-inv22…27/` | add-in upstream per anno (compatibilità) |
| `bridge/tests/Bimwright.Ipt.Tests/` | xUnit, senza Inventor (FakeAddIn) |
| `bridge/tests/*LiveProbe/` | sonde contro Inventor reale (M3, M4, generiche) |

Unità: l'API Inventor lavora in **cm**; ogni input/output dei tool è in **mm**
(`shared/Handlers/UnitConvert.cs`).

### `Inventor XR SO/` — client Quest 3 (Unity 6000.6.3f1, URP, Meta XR)

| Percorso | Contenuto |
|---|---|
| `Packages/com.occhipinti.inventorxrso.core/Runtime/` | logica senza Unity: `Mcp/`, `Net/`, `Pairing/`, `Session/`, `Selection/`, `Glb/`, `Backend/` |
| `Assets/XrSo/Runtime/` | codice Unity: `Net/` (UnityWebRequest, pinning), `Pairing/` (Keystore, QR), `Scene/` (mesh, highlight, sezione, misure, preview), `Ui/`, `Shaders/` |
| `Assets/XrSo/Xr/` | rig, ray controller, workspace (`InspectWorkspace`, `DesignWorkspace`, `AssemblyWorkspace`), `AppController` |
| `Assets/XrSo/Editor/` | setup progetto, build APK e generazione di `Scenes/Main.unity` (scena **generata**: non modificarla a mano) |
| `Assets/XrSo/Tests/EditMode/` | test Unity EditMode |
| `Assets/Plugins/` | vault Android (Java) e ZXing |
| `Tests~/` | test .NET del core (`XrSo.Core.Tests`) e `XrSo.TestHost` (server HTTPS con FakeAddIn) |
| `Tools~/Invoke-Unity.ps1` | Unity in batch |
| `inventor_meta_product.md` | **spec di prodotto** (§ numerati citati da tutte le spec di milestone) |
| `REALITY_CAPTURE_SPEC.md` | spec futura Reality Capture |

Build, test e primo collegamento: `Inventor XR SO/README.md`.

### `docs/` — dove cercare cosa

| Serve… | File |
|---|---|
| stato verificato del backend e gate aperti | `docs/DEVELOPMENT.md` |
| requisiti completi del backend | `docs/analisi-spec-inventor-so-mcp.md`, `docs/INVENTOR_SO_MCP_IMPLEMENTATION_PLAN.md` (`.original.md` = storico) |
| note sull'API Inventor | `docs/inventor-api-notes.md` |
| spec di design di una milestone | `docs/superpowers/specs/<data>-<tema>-design.md` |
| piano di implementazione | `docs/superpowers/plans/<data>-<tema>.md` |
| esito di verifica / collaudo | `docs/xr-mN-verification.md`, `docs/xr-mN-collaudo.md`, `docs/xr-m3-acceptance.md` |
| test automatici sul Quest (runner in-app, fixture, script ADB) | `docs/xr-quest-acceptance.md` |

Milestone XR: M1 visualizzazione e selezione · M2 ispezione · M3 schizzo/design
· M4 assembly · **M5 lamiera, sviluppo piano e voce** (implementazione
software presente, collaudo aperto: `docs/xr-m5-verification.md`) · **M6 UX
spaziale da seduto** (fasi 1–4 implementate: guscio UI, Progettazione, Lamiera, Assieme + Ispezione; fase 5, runner sul Quest per la fase 4 e prova fisica aperti: `docs/xr-m6-verification.md`; spec: `docs/superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md`) · **M7 Ispeziona, verifica ingegneristica degli assiemi** (visibilità, interferenze, distanza minima, salute e BOM; implementazione software presente, test EditMode, runner sul Quest e prova fisica aperti: `docs/xr-m7-verification.md`; spec: `docs/superpowers/specs/2026-10-03-inventor-xr-so-m7-inspect-verifica-design.md`). · **M9 navigazione per contesto** (contesto dal documento attivo, doppio Trigger, Torna, schede per contesto, tasti contestuali con legenda, modifica feature da faccia; implementazione software presente, runner Quest, sonda `face_feature` e prova fisica aperti: `docs/xr-m9-verification.md`; spec: `docs/superpowers/specs/2026-10-04-m9-navigazione-contesto-design.md`; piano: `docs/superpowers/plans/2026-10-04-m9-navigazione-contesto.md`). Non tutte le milestone hanno una spec
separata (M2 e M3 sono solo nei piani e nella spec di prodotto). Le spec
`impaginazione-disegni` e `template-idw-cartiglio` riguardano le tavole IDW.

## Build e test (sintesi)

```bash
dotnet test bridge/tests/Bimwright.Ipt.Tests                 # backend, gira anche su Linux
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"          # core XR
```

Su Linux falliscono per costruzione i test di semantica dei path Windows
(elenco in `bridge/CLAUDE.md`). Add-in, test Unity, APK e smoke test in
`scripts/` richiedono Windows, Inventor 2027 e/o Unity.

M8 **grapics** adotta il design system HIVE/EnerBot nella UI Unity e nel pairing
Windows: spec `docs/superpowers/specs/2026-10-04-m8-grapics-design.md`, piano
`docs/superpowers/plans/2026-10-04-m8-grapics.md`, esiti `docs/xr-m8-verification.md`.
Software implementato, verifica dispositivo/performance e prova fisica aperte.
Il runner M8 usa la fixture dedicata **M6** (guardia esplicita), con log M8.

M10 **icone Inventor**: migrazione software dei sei provider XR, **34 immagini
native/46 azioni**, tooltip e fallback, pack completo con provenienza. Runner
M10 su fixture M6, wrapper con ripristino, APK QA Development per il profilo
A/B/A testo M8/icone M10 e APK ordinario senza runner. Spec:
`docs/superpowers/specs/2026-10-06-m10-icone-inventor-design.md`; piano:
`docs/superpowers/plans/2026-10-06-m10-icone-inventor.md`; inventario:
`docs/xr-m10-action-inventory.md`; risultati e gate performance aperto:
`docs/xr-m10-verification.md`. Run Quest del 6 ottobre 2026: **PASS COMPLETE
sintetico** con Inventor reale; fixture ripristinata e APK ordinario verificato.
Prova fisica MR/VR confermata dall'utente il 6 ottobre; resta aperto il budget
GPU (plugin senza campioni).

## Regole trasversali

- Per ogni gate XR, porta nel runner automatico sul Quest tutti i passaggi
  riproducibili in modo deterministico: stato UI, chiamate al percorso input,
  preview, revisione, risultato nativo e cleanup. Esegui il runner contro una
  fixture Inventor dedicata, non contro i documenti dell'utente. Se un gesto
  viene simulato, chiamalo **sintetico** nel log e verifica comunque il backend
  reale quando il gate dipende da Inventor.
- Separa sempre tre esiti: test unitario/FakeAddIn, runner sul Quest con input
  sintetico, prova fisica con persona e controller. Un `PASS COMPLETE` del
  runner chiude solo i sottocasi effettivamente esercitati; non certifica
  ergonomia, leggibilità, tracking reale, microfono o gesti fisici. Registra
  `PASS [gate]` e `NOT COVERED [gate]` con il motivo nel log e nel verbale.
- Prima di chiedere una prova manuale, automatizza i sottocasi riproducibili e
  riduci la richiesta fisica al comportamento che il software non può
  osservare da solo. Dopo ogni run conserva manifest, log e screenshot,
  distingue un timeout prima dell'avvio da un fallimento del test e ripristina
  APK ordinario e documento Inventor precedente.
- Una prova fisica (Quest, Inventor reale, audio, tracking) non eseguita resta
  **aperta**: non segnarla mai come passata sulla base di test automatici o
  FakeAddIn. Registra gli esiti nel file di verifica/collaudo della milestone.
- Prima di implementare una milestone leggi spec **e** piano; i gate numerati
  (es. `M5-01…M5-12`) sono il criterio di consegna.
- Una capacità non collaudata dal vivo va nel tier sperimentale (regole in
  `bridge/CLAUDE.md`, sezione *Experimental tier*).
- La documentazione di prodotto e di milestone è in italiano; mantieni la lingua
  del file che modifichi.
- `src/` e `pyproject.toml` sono il riferimento Python: non aggiungere lì
  funzionalità di produzione.

## Collaudo autonomo senza visore — Meta XR Simulator

Procedura riutilizzabile dal branch `nuova_ux`, verificata nella sessione del
25–26 ottobre 2026. Verbale corrente e limiti:
**[docs/xr-simulator-full-collaudo.md](docs/xr-simulator-full-collaudo.md)**.
Configurazione iniziale e smoke precedenti:
[docs/xr-meta-simulator-verification.md](docs/xr-meta-simulator-verification.md).
Leggere i verbali prima di attribuire un PASS a un gate storico.

### Prerequisiti e isolamento

- Windows, PowerShell 7, Unity **6000.6.3f1**, SDK .NET 8 e 10.
  Unity locale: `C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe`.
- Meta CLI `metavr` nel PATH; installazione locale:
  `C:/Users/salva/.metavr/bin/metavr.exe`.
- Runtime ufficiale Meta XR Simulator **207.1**. Il wrapper cerca prima il
  registro `SOFTWARE/Classes/xrsim/shell/open/command`, poi
  `%LOCALAPPDATA%/MetaXRSimulatorPortable/PFiles/MetaXRSimulator/v207.1/meta_openxr_simulator.json`.
  Per un'altra installazione usare `-RuntimeManifest <percorso-assoluto>`.
- Per CAD nativo: una sola istanza Inventor 2027 con add-in SO sperimentale
  **compilato dal codice da collaudare** e avviato con
  `INVENTOR_SO_EXPERIMENTAL=1` nell'ambiente del processo. Confermare con
  `inventor_health` e identificare i documenti aperti. Non basta avere installato
  un vecchio pacchetto: il manifest può puntare a una DLL di una versione precedente.
- Eseguire una sola corsa Unity/fixture alla volta, con l'Editor del progetto
  chiuso. Non modificare script Unity mentre il runner è in Play Mode.
  Eseguire anche import/build Unity e sonde CAD/test di host in sequenza:
  importazioni pesanti concorrenti possono causare timeout e un esito CAD incerto.
- La simulazione usa OpenXR su **Standalone**, Oculus su Android. Il wrapper
  imposta `XR_RUNTIME_JSON` soltanto nell'ambiente del processo e lo ripristina:
  non cambiare il runtime OpenXR globale e non sostituire Quest Link.
- Serve un Editor GUI con **Game view** che produca frame XR. `-batchmode` può
  inizializzare OpenXR senza renderizzare: non usarlo per questi runner.
  Rimane corretto per EditMode e build APK.

### Comandi dalla radice del repository

Usare sempre una cartella evidenze nuova; non sovrascrivere una corsa precedente.

```powershell
# Solo configurazione Standalone/OpenXR; nessuna prova funzionale.
& 'Inventor XR SO/Tools~/Invoke-Simulator.ps1' -SetupOnly -Output 'artifacts/simulator/setup-nuovo'

# Smoke sintetico con host HTTPS FakeAddIn, senza Inventor.
& 'Inventor XR SO/Tools~/Invoke-Simulator.ps1' -Output 'artifacts/simulator/fake-nuovo'

# Smoke con Inventor reale e fixture M7 dedicata (49 controlli di base).
& 'Inventor XR SO/Tools~/Invoke-SimulatorLive.ps1' -Output 'artifacts/simulator/native-nuovo'

# Un runner completo; il wrapper sceglie/prepara/ripristina la fixture corretta.
& 'Inventor XR SO/Tools~/Invoke-SimulatorLive.ps1' -AcceptanceRunner qx -Output 'artifacts/simulator/qx-nuovo'

# Suite sequenziale dei runner storici migrati e QX.
& 'Inventor XR SO/Tools~/Invoke-SimulatorSuite.ps1' -Output 'artifacts/simulator/suite-nuova'

# Selezione di runner o ripetizione mirata dopo una correzione.
& 'Inventor XR SO/Tools~/Invoke-SimulatorSuite.ps1' -Runners @('m5','m7','qx') -Output 'artifacts/simulator/ripetizione-nuova'

# S1 è incrementale e separato dalla suite predefinita: ha bootstrap dedicato e pacing HTTP.
& 'Inventor XR SO/Tools~/Invoke-SimulatorLive.ps1' -AcceptanceRunner s1 -Output 'artifacts/simulator/s1-nuovo'

# Sonde native separate, soltanto fra le fixture e senza documenti aperti.
dotnet run --project bridge/tests/M3LiveProbe -- --face-feature
dotnet run --project bridge/tests/RealityReferenceLiveProbe
```

Runner disponibili: `m1,m2,m3,m4,m5,m6,m7,m8,m9,m9n,m9f,m9h,m10,qx,s1`.
M8/M9/M10 usano la fixture M6; QX usa M7; M4 usa `M4LiveProbe`;
gli altri usano la rispettiva fixture `QuestAcceptanceFixtures`.
Non passare documenti dell'utente né costruire credenziali a mano.
Il wrapper crea un host HTTPS di produzione su loopback/porta libera, certificato
P-256 e bearer temporanei, mantenendo il pinning SHA-256. Non cambia il pairing
persistente. Il bootstrap S1 in Editor non collauda il bootstrap privato Android.
La fixture S1 prepara `S1_Create`, `S1_TextRead` e i tre parametri nativi
`S1QuestLength73`, `S1QuestFlag73`, `S1QuestLabel73`: il vecchio fixture builder
con il solo schizzo vuoto non basta per gli incrementi testo/parametri.
La sonda RC4 usa una scansione sintetica e il vero plan/preview/commit; verifica
provenienza, geometria di riferimento, Undo/Redo, salva una nuova parte nel
workspace e la riapre. Chiude i propri documenti e conserva il file come prova.
`--diagnose` è una diagnosi COM in transazione annullata, non un PASS di accettazione.

### Esiti, errori e ripristino

- Leggere `progress.json` e `results.json` della suite; per ogni corsa conservare
  `runner-result.json`, `result.json`, `unity.log`, `*-acceptance.txt`, PNG,
  manifest fixture sotto `artifacts/<fixture>-verification/quest-fixture.json`,
  `native-before.log`, `native-after.log`, `native-cleanup.json` e log host.
- `pass.marker` ed exit code 0 attestano il completamento del runner. **Non
  attestano copertura totale**: leggere `verdict`, `passed_gates`, `not_covered`.
  `PARTIAL` resta parziale; un caso fisico non eseguito resta escluso/aperto.
  Contatori e frame del PC non certificano prestazioni GPU del Quest.
- Confrontare i due stati nativi nelle corse senza authoring. L'attivazione di
  documenti può avanzare `revision` senza modificare il CAD: controllare anche
  `visual_revision`, geometria, pose e dirty flag. Le corse authoring devono
  invece avanzare revisione al commit ed essere chiuse senza salvare la fixture.
- `native-cleanup.json` deve riportare `fixture_restored=true` quando
  `fixture_prepared=true`. La suite si arresta se il ripristino fallisce.
  Riparare quello stato prima di avviare un'altra fixture.
- Per fermare le corse **in coda**, creare `stop.request` nella cartella della
  suite: i figli successivi si fermano prima della preparazione. Lasciare finire
  la corsa attiva e il suo `finally`; non terminare tutto l'albero dei processi.
- `error CS...` è errore di compilazione, assenza di log è problema di avvio,
  `FAIL` nel runner è fallimento della prova. Non confonderli né contare come
  falliti funzionali i runner fermati prima di preparare la fixture.
- Il frontend è avviato tramite `metavr xrsim app launch --app-path ...`.
  Usare il wrapper: la GUI può ereditare le pipe del CLI e bloccare l'attesa EOF.
  Il wrapper attende il processo launcher con timeout e rileva gli errori CS.
- Dopo una terminazione forzata può comparire **Scene Backup Detected** prima
  del caricamento del progetto: `unity.log` resta sulle righe di licensing e
  il titolo della finestra identifica il dialogo. Usare Computer Use per
  **Keep Backups**, quindi verificare che il log riprenda. Non modificare la
  scena generata e non cancellare alla cieca `Temp/`. Conservare l'eventuale
  `Assets/_Recovery/` nelle evidenze prima di ripulire gli artefatti generati.
- Il `finally` chiude senza salvare i documenti fixture, riattiva quello
  precedente, ferma soltanto il proprio host ed elimina credenziali/certificato
  temporanei. Verificare il JSON, non presumere che il cleanup sia riuscito.

### Add-in temporaneo e controlli finali

Se l'add-in installato non corrisponde al branch, pubblicarne uno **in una nuova
cartella QA**, senza sovrascrivere DLL caricate:

```powershell
dotnet publish bridge/src/plugin-so27/Inventor.So.AddIn.csproj -c Debug -p:SoExperimental=true --self-contained false -o artifacts/simulator/qa-nuova/addin
```

Il manifest installato è normalmente
`%APPDATA%/Autodesk/Inventor 2027/Addins/InventorSO/Inventor.So.addin`.
Prima di cambiarne `Assembly`, conservarne una copia **byte per byte**, hash
SHA-256, percorso DLL originale e PID. Chiudere Inventor soltanto dopo aver
verificato che non abbia documenti dell'utente aperti; non chiudere documenti
non salvati per liberare la DLL. Puntare `Assembly` alla DLL QA assoluta e
riavviare una sola istanza con l'ambiente sperimentale limitato al processo.
La posizione di Inventor si ricava dal registro
`HKLM/SOFTWARE/Autodesk/Inventor/RegistryVersion31.0/InventorLocation`:
su questa macchina è `Z:/Installati/Inventor 2027/Bin/Inventor.exe`.
Attendere il caricamento dell'add-in prima di interpretare `NO_TARGET` come errore.
Se MCP resta agganciato al vecchio PID, usare `inventor_list_available_targets`
e `inventor_switch_target` sul target effettivamente riavviato, poi `inventor_health`.
Per chiudere via COM un'istanza vuota si può usare Windows PowerShell 5 con
`-NoProfile -STA`: `Marshal.GetActiveObject('Inventor.Application')`, controllo
`Documents.Count == 0`, quindi `Quit()`. Quel metodo Marshal non è disponibile
nello stesso modo in PowerShell 7/.NET moderno; le fixture C# usano il P/Invoke.
Non terminare un'istanza con documenti non verificati per forzare il riavvio.

Alla fine: verificare zero documenti fixture, chiudere l'istanza QA, ripristinare
il manifest originale dalla copia e verificarne l'hash, quindi riavviare
l'installazione precedente. Nella sessione iniziale lo stato di recupero è in
`artifacts/simulator-full/addin-qa-state.json` e la copia originale in
`artifacts/simulator-full/original-addin-manifest.xml`: **non sovrascriverli**.

Dopo eventuali fix eseguire Core, backend, Capture.Worker, Unity EditMode e
`XrSoBuild.BuildQxApksBatch` con `-qxOutput` nuovo. Ispezionare gli APK: ARM64
e metadati dei runner presenti nel QA, assenti nell'ordinario. `UNITY_EDITOR`
abilita i runner solo nell'Editor; nei player serve `XR_SO_ACCEPTANCE`.
Il gate di avvio Editor richiede richiesta runner, file credenziali e output.
I font Satoshi SDF possono accumulare glifi durante Play Mode: ripristinare
solo quelle modifiche generate dopo tutti i test; preservare modifiche utente.
Aggiornare verbale, conteggi, hash e limiti prima del commit. Non aggiornare a
mano `Scenes/Main.unity` e non dichiarare superata una prova fisica esclusa.

Esempio di verifica finale, in sequenza e con percorsi evidenze assoluti nuovi:

```powershell
dotnet test 'Inventor XR SO/Tests~/XrSo.Core.Tests'
dotnet test bridge/tests/Bimwright.Ipt.Tests
dotnet test bridge/tests/Capture.Worker.Tests
$evidence = Join-Path (Get-Location) 'artifacts/simulator/verifica-finale-nuova'
New-Item -ItemType Directory -Path $evidence
& 'Inventor XR SO/Tools~/Invoke-Unity.ps1' -Arguments @('-buildTarget','Android','-runTests','-testPlatform','EditMode','-assemblyNames','InventorXrSo.Tests.EditMode','-testResults',"$evidence/editmode.xml") -Log "$evidence/editmode.log"
& 'Inventor XR SO/Tools~/Invoke-Unity.ps1' -Arguments @('-buildTarget','Android','-executeMethod','InventorXrSo.Editor.XrSoBuild.BuildQxApksBatch','-qxOutput',"$evidence/apks") -Log "$evidence/apks.log"
& 'Inventor XR SO/Tools~/Inspect-SimulatorPackages.ps1' -QaApk "$evidence/apks/InventorXrSo-qx-acceptance.apk" -OrdinaryApk "$evidence/apks/InventorXrSo-qx-ordinary.apk" -OutputJson "$evidence/package-inspection.json"
```

Negli ZIP APK controllare `lib/arm64-v8a/libil2cpp.so` e
`assets/bin/Data/Managed/Metadata/global-metadata.dat`; cercare i nomi
`QuestAcceptanceRunner`, `QxQuestAcceptance`, `M1QuestAcceptance`…`S1QuestAcceptance`
nei metadati. Non usare la sola presenza di una stringa in tutto lo ZIP come
prova di esclusione delle classi. Registrare dimensioni e SHA-256 dei due APK.
