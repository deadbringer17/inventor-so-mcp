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
spaziale da seduto** (fasi 1–4 implementate: guscio UI, Progettazione, Lamiera, Assieme + Ispezione; fase 5, runner sul Quest per la fase 4 e prova fisica aperti: `docs/xr-m6-verification.md`; spec: `docs/superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md`). Non tutte le milestone hanno una spec
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
