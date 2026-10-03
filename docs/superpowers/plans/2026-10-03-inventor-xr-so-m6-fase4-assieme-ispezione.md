# M6 Fase 4 — Assieme + Ispezione: piano di implementazione

> Esecuzione: subagent Sonnet 5.5 per il codice (regola `CLAUDE.md`); l'agente principale rilegge ogni diff e riesegue i test prima di committare. Modello da seguire: Fase 3 ([piano](2026-10-03-inventor-xr-so-m6-fase3-lamiera.md), codice in `Assets/XrSo/Xr/LamieraWorkspace.cs` + `LamieraActions.cs`) e Fase 2 (`DesignWorkspace.cs` + `DesignActions.cs`).

**Goal:** migrare `AssemblyWorkspace` e `InspectWorkspace` al guscio M6 (spec «Fasi di consegna» punto 4): assieme sollevato, isolamento componente (`ComponentIsolation`), scheda Spazi al posto del menù polso, tavolozza riportata sul controller sinistro nella posizione della spec, voce sul catalogo, runner migrati agli id azione.

**Spec:** `docs/superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md` (Disposizione spaziale «Assieme», «Tavolozza», «Anello contestuale»). Schede: Assieme = **Componenti · Vincoli · Vista · Spazi**; Ispeziona = **Misura · Sezione · Vista · Spazi**. Gate: M6-01, M6-06, M6-08 (+ contributo a M6-04/05/07/09).

## Vincoli

- Core già pronto: `WorkbenchLayout.Assembly(frame, extentM)` e `WorkbenchLayout.Isolated(frame, componentPosition, componentScale)` (verificare firma e test in `Tests~/XrSo.Core.Tests`; estendere solo se manca qualcosa).
- Nessuna capacità CAD nuova: le azioni sono quelle che `Render()` offre oggi, redistribuite (max 8 per scheda; niente «Precedenti/Successivi»; elenchi lunghi come schede di scelta da ≤ 8 voci con `PaletteView.ShowTabGroup`). Offline le azioni locali di Ispeziona restano disponibili, quelle backend no.
- Assieme: il CAD cambia solo con «Applica» della barra di conferma dopo un'anteprima valida; regole invariate (`_mutations`, `RequiresCadReview`, `RefreshRequired`, `CommitOutcomeUnknown`, online, revisione). Voce «Applica» non committa (M5-11). Trascinamento dello spostamento componente: solo Trigger tenuto sulla maniglia/asse (non più Grip+Trigger); rilascio, perdita di tracking, `Close()`/`SetVisible(false)` chiudono la cattura (M5-08); modifica solo la bozza.
- Isolamento: **solo visivo**, non muove l'occorrenza in Inventor (spec). «Isola» dall'anello contestuale su componente; da isolato «Apri in Progettazione» e, per parti lamiera, «Apri in Lamiera» (percorsi esistenti: `ActivateOpenAsync` + `DesignRequested`/`LamieraRequested`); «Rilascia» o X riporta il componente al suo posto.
- Ispeziona: resta la modalità di default; nessun `OVRInput` in `InspectWorkspace`/`AssemblyWorkspace` a fine fase (solo `XrInput`). Presa a una mano / due mani = solo vista (come Design). Il menù polso (`_wrist`), il pannello `_panel`, breadcrumb e `_compact` spariscono: breadcrumb e contesto passano alla scheda/HUD, la selezione compatta al chip/HUD.
- Tavolozza: tolta la posizione «sotto il controller» di `AppController` (riga con `localPosition = (0,-0.07f,0.02f)`), riportata alla posizione della spec; aggiornare i test della Fase 1 che la citano.
- Voce: `WorkspaceVoiceTarget` interroga `ActionCatalog` (`ResolveVoice`), non scandisce più i `Button`; regole M5 invariate (vocabolario finito, ambiguità rifiutata, disabilitato = nessuna mutazione).
- `HomePanel` resta per pairing/connessione (raggiunto da Spazi → Connessione).
- Testi in italiano con i nomi Inventor. Un solo processo Unity batch alla volta.
- Test: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"` (baseline 439) e Unity EditMode (baseline 325): `& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"`, poi ripristinare eventuali modifiche sotto `Assets/TextMesh Pro`.

## Task

### T1 — `AssemblyWorkspace` sul guscio + isolamento
`Xr/AssemblyWorkspace.cs` + nuovo `Xr/AssemblyActions.cs` (partial) come `IActionProvider` (id `assembly.*`, `CommitBarState` + `commit.*`, `ContextActions(Component)` = Isola · Sposta · Vincola · Apri), chip + tastierino (distanza/angolo/gioco minimo) con `NumericEntry`, errori su HUD, rimozione di `Render()`/`Page()`/pannello e di `OVRInput`. Nuova `Runtime/Scene/ComponentIsolation.cs` (avanza il componente a metà strada, resto al 20 % opacità, rilascio) e `Workbench.ApplyAssembly`/isolamento con transizione ~250 ms. `AppController`: `Attach(shell, bench, sheet, input)`, `SetActive(_assembly)`, `HudMessage`. Test EditMode migrati/nuovi (id univoci, ≤ 8 per scheda, fasi barra, Applica solo da Ready, ring, isolamento solo visivo, cattura trascinamento). `M4QuestAcceptance` invoca azioni per id (log «sintetico»).

### T2 — `InspectWorkspace` sul guscio, Spazi, voce
`Xr/InspectWorkspace.cs` + `Xr/InspectActions.cs`: schede Misura · Sezione · Vista (scala, ambiente, Esplora/proprietà/documenti come schede di scelta) · Spazi; rimozione di menù polso/pannello/breadcrumb/compact; `ActionCatalog.SetActive(_inspect)` quando nessun altro workspace è attivo (il catalogo non resta mai senza scheda); sezione e misura via `XrInput`. `AppController`: tavolozza nella posizione spec, `_inspect` senza `left`/wrist. `WorkspaceVoiceTarget` → catalogo (`ResolveVoice`), test aggiornati. Runner M1, M2, M3, M5 (e M4 se serve) invocano azioni per id; `QuestAcceptanceContractTests` aggiornati.

### T3 — Verifica e documentazione (agente principale)
Core + EditMode; APK ordinario e di collaudo; runner M1–M5 sul Quest se visore sveglio, host con IP corrente (`--pair-host`) e Inventor aperto; altrimenti NOT COVERED con motivo. Sezione «Fase 4» in `docs/xr-m6-verification.md`, `CLAUDE.md` aggiornato; M6-01…M6-10 restano aperti dove manca la prova fisica; M6-09 chiudibile solo per i runner M1–M5 (il runner M6 è Fase 5).

## Ordine
T1 → T2 → T3, un commit per task dopo rilettura del diff e test verdi.
