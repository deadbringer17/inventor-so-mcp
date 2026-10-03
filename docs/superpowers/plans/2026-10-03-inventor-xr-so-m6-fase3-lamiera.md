# M6 Fase 3 — Lamiera: piano di implementazione

> Esecuzione: subagent Sonnet 5.5 per il codice (regola `CLAUDE.md`); l'agente principale rilegge ogni diff e riesegue i test prima di committare. Modello da seguire: la Fase 2 ([piano](2026-09-30-inventor-xr-so-m6-fase2-progettazione-schizzo.md), codice in `Assets/XrSo/Xr/DesignWorkspace.cs` + `DesignActions.cs`).

**Goal:** migrare `LamieraWorkspace` al guscio M6 (spec, «Fasi di consegna» punto 3): niente pannello uGUI, azioni nel catalogo, barra di conferma unica, chip + tastierino, flangia trascinata con il solo Trigger tenuto sulla maniglia (contratto M5-08), sviluppo piano posato sul piano di lavoro.

**Spec:** `docs/superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md`. Schede Lamiera: **Lamiera · Schizzo · Sviluppo · Vista · Spazi**. Spec M5: `docs/superpowers/specs/2026-09-28-inventor-xr-so-m5-design.md`.

## Vincoli

- Assieme, Ispeziona e il menù polso non si toccano. Design (Fase 2) non si tocca se non per compile fix.
- Nessuna capacità CAD nuova: le azioni sono quelle che `LamieraWorkspace.Render()` offre oggi, redistribuite (max 8 per scheda; nessun «Precedenti/Successivi»; elenchi lunghi come schede di scelta da ≤ 8 voci con `PaletteView.ShowTabGroup`, come in Design).
- Il CAD cambia solo con «Applica» della barra di conferma dopo un'anteprima valida; regole di abilitazione invariate (`_pendingMutations`, `RefreshRequired`, `CommitOutcomeUnknown`, online, revisione). Voce «Applica» non committa (M5-11).
- Trascinamento flangia: solo Trigger tenuto sulla maniglia; rilascio, perdita di tracking, `Close()`/`SetVisible(false)` chiudono la cattura (M5-08); modifica solo la bozza.
- Comportamenti M5 da preservare: modalità primaria Lamiera per parti lamiera (`IsPrimary`, `PrimaryChanged`), regola/spessore, Face/Flange/Cut, Sviluppo piano (`FlatPattern`, Detach: la mesh piana si sposta, non il modello), campi numerici armati (`FieldFlangeHeight`, `FieldFlangeAngle`, `FieldThickness`, `ArmedField`, `TryArmField`, `SetArmedField`, `LastFieldError`), `DesignRequested`.
- Un solo processo Unity batch alla volta. Testi in italiano con i nomi Inventor. Nessun `OVRInput` in `LamieraWorkspace` a fine fase (solo `XrInput`).
- Test: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"` (baseline 439) e Unity EditMode (baseline 279): `& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"` poi ripristinare eventuali modifiche sotto `Assets/TextMesh Pro`.

## Task

### T1 — `LamieraWorkspace` come `IActionProvider` (parte A)
Come la Fase 2 parte A, su `Xr/LamieraWorkspace.cs` (+ nuovo `Xr/LamieraActions.cs` partial): `IActionProvider` con schede Lamiera · Schizzo · Sviluppo · Vista, id `lamiera.*`, `CommitBarState` + azioni `commit.*`, `ContextActions` (faccia piana: Flangia/Faccia/Taglio secondo oggi; bordo: Flangia), chip + tastierino (altezza/angolo flangia, spessore regola) con `NumericEntry`, notice/errore su HUD, rimozione di `Render()`/`Page()`/pannello. Mantenere l'API pubblica (vedi Vincoli), `VoicePanel` → null con `WorkspaceVoiceTarget` null-safe (già predisposto per Design via `IWorkspaceActionVoiceSurface`: riusarlo). `AppController`: `SetActive(_lamiera)` all'apertura, `Attach(shell, bench, sheet, input)`; modello sul piano di lavoro con `Workbench.ApplyPart`. Test EditMode di Lamiera migrati a «invoca azione per id»; nuovi test: id univoci, ≤ 8 per scheda in ogni stato, fasi della barra, Applica solo da Ready, ring. `M5QuestAcceptance` (e `M4`/altri che aprono Lamiera) invoca le azioni per id con log «sintetico».

### T2 — Input e sviluppo piano (parte B)
Solo `XrInput`: Trigger tenuto sulla maniglia della flangia = trascinamento (altezza e angolo), precisione 10×, tracking perso → ultimo valore valido + impulso di errore, rilascio → anteprima; Grip = solo vista; A snap dove serve; X = indietro (tastierino, anello, elenco, ultimo passo); stick destro ± passo / passo 10-1-0,1; Y Adatta/Ricentra; aptica come in Design. Sviluppo piano: la mesh dello sviluppo (`FlatPatternDisplay`) si posa sul piano di lavoro davanti all'utente (`WorkbenchLayout`), con «Distacca»/«Riaggancia» che spostano solo la mesh; azione Vista «Piegato»/«Sviluppo». `FlangeManipulator` adattato (niente `OVRInput`). Test sintetici `XrInput` come in Design (cattura solo sulla maniglia, rilascio/tracking/Close, precisione, back-chain).

### T3 — Verifica e documentazione (agente principale)
Core + EditMode; APK ordinario e di collaudo; runner M1–M5 sul Quest **se** visore sveglio, host con IP corrente (`--pair-host`) e Inventor aperto; altrimenti NOT COVERED con motivo. Sezione «Fase 3» in `docs/xr-m6-verification.md`; M6-02…M6-05 restano aperti fino alla prova fisica.

## Ordine

T1 → T2 → T3, un commit per task dopo rilettura del diff e test verdi.
