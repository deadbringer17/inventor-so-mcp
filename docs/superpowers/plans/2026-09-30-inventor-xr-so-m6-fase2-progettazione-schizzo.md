# M6 Fase 2 — Progettazione + Schizzo: piano di implementazione

> Esecuzione: subagent Sonnet 5.5 per il codice (regola `CLAUDE.md`); l'agente principale rilegge ogni diff e riesegue i test prima di committare.

**Goal:** migrare `DesignWorkspace` al guscio M6 (spec §«Fasi di consegna», fase 2): niente pannello uGUI fluttuante, azioni dichiarate nel catalogo, barra di conferma unica, chip valore + tastierino, anello contestuale, foglio schizzo orizzontale sul piano di lavoro con «Vista modello» ↔ «Foglio», penna = punta del controller destro.

**Spec:** `docs/superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md` (§Disposizione spaziale, §Mappatura dei controller, §Tavolozza/tastierino/chip/anello, §Barra di conferma). **Fase 1:** `docs/superpowers/plans/2026-09-29-inventor-xr-so-m6-fase1-fondamenta.md`.

## Vincoli

- Gli altri tre workspace (Lamiera, Assieme, Ispeziona) e il menù polso **non si toccano**: continuano col loro pannello.
- Nessuna capacità CAD nuova: le azioni sono quelle che `DesignWorkspace.Render()` offre oggi, redistribuite (max 8 per scheda, 2×4; niente «Precedenti/Successivi»; se serve si divide la scheda).
- Il CAD cambia solo con «Applica» della barra di conferma dopo un'anteprima valida. Le regole di abilitazione restano quelle di oggi (`_pendingMutations`, `RefreshRequired`, `CommitOutcomeUnknown`, online, revisione). La voce «Applica» non committa (M5-11).
- Il trascinamento maniglia passa da Grip+Trigger a solo Trigger tenuto sulla maniglia; rilascio, perdita di tracking, cambio workspace chiudono la cattura (M5-08).
- Coordinate CAD dello schizzo invariate: «Vista modello»/«Foglio» cambiano solo la trasformazione di visualizzazione.
- Un solo processo Unity alla volta sul progetto (batch): i task che lanciano Unity non girano in parallelo.
- Testi in italiano, nomi Inventor. Nessun `record` nel core (netstandard2.1, C# 9). `Scenes/Main.unity` è generata.
- Test core: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`. Test Unity: `& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"`.

## Task

### T1 — Core: trasformazione foglio schizzo (`SketchSheetLayout`)
File: `Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/SketchSheetLayout.cs`, test `Tests~/XrSo.Core.Tests/Ui/SketchSheetLayoutTests.cs`.
Funzione pura: dato `SketchFrame` (mm, modello), estensione dello schizzo (larghezza/altezza mm, ≥ 0), frame postazione `WorkbenchFrame`, produce la **rotazione** e la **traslazione** del modello (radice della scena) tali che il piano dello schizzo sia orizzontale sul piano di lavoro, X dello schizzo verso la destra del frame, Y dello schizzo allontanandosi dall'utente, normale verso l'alto, centro dello schizzo al centro del piano di lavoro (`PartDistance`), con scala `WorkbenchLayout.SheetScale`. Output come quaternione `(x,y,z,w)` + posizione + scala (struct `SheetPose`), senza tipi Unity. Test: X→destra, Y→avanti, normale→su, centro, scala, schizzo degenere (estensione 0 → scala 1), piani con normale arbitraria.

### T2 — Unity: viste `ChipView`, `RingView`, `SketchSheetView`, `Workbench`
Cartelle: `Assets/XrSo/Runtime/Ui/Shell/` (ChipView, RingView), `Assets/XrSo/Runtime/Scene/SketchSheetView.cs`, `Assets/XrSo/Xr/Workbench.cs`; test EditMode in `Assets/XrSo/Tests/EditMode/`.
- `ChipView`: canvas world-space billboard verso la testa; mostra `NumericEntry.Display` + unità + passo corrente (`NumericEntry.Step`); bordo blu se modificato non in anteprima; `Armed` (un solo chip armato: quello che riceve tastierino/thumbstick/dettatura); testo ≥ 14 mm; bersaglio ≥ 25 mm; un tocco chiama un callback (apre il tastierino nella tavolozza).
- `RingView`: anello di ≤ 6 azioni (`ActionCatalog.Context`), raggio ~6 cm attorno a un punto mondo, billboard; pulsanti ≥ 25 mm; si chiude con `Hide()`; disabilitate con lo stesso criterio dell'azione.
- `SketchSheetView`: applica `SketchSheetLayout` (T1) alla radice della scena con interpolazione ~250 ms (`Workbench.Tween`); stato `Sheet`/`Model`; `ProjectPen(Vector3 tip, Ray ray, out Vector2 sketchMm)`: proietta la punta sul piano dello schizzo se entro 2 cm, altrimenti usa il raggio.
- `Workbench`: calcola il frame postazione dalla testa (yaw), applica `WorkbenchLayout.Part` al modello con transizione ~250 ms, `Recenter()`, `Fit()`; nessun salto istantaneo; scala limitata [0,001; 10] con `Clamped` esposto per l'HUD.
Test EditMode: pose finali dopo la transizione, proiezione penna (entro/oltre 2 cm), `RingView` ≤ 6 azioni e disabilitate, `ChipView` stato armato/modificato.

### T3 — `DesignWorkspace` come `IActionProvider`
File: `Assets/XrSo/Xr/DesignWorkspace.cs` (+ nuovo `Assets/XrSo/Xr/DesignActions.cs` se conviene separare le dichiarazioni), `AppController.cs`.
- Schede: **Schizzo · Feature · Parametri · Vista · Spazi** (Spazi è del catalogo). Id stabili `design.*`. Contenuto: le azioni di `Render()` oggi (Crea schizzo, forme Linea/Rettangolo/Cerchio, Coordinate numeriche, Quota, Vincoli, Estrudi, Foro, Raccordo, Smusso, Parametri, Aggiorna riferimenti, Annulla/Ripeti modifica XR, Vista modello/Foglio, Adatta, Ricentra…). Piani e profili dell'elenco lungo (piani, schizzi, parametri): scelti da elenco nella scheda (dividere le schede in sotto-schede nascoste `_…`/dinamiche); nessuna paginazione.
- Barra di conferma: `CommitBarState` alimentata da `CommitBarInputs` derivati da `_session`, `_pendingMutations`, `_online`, `_busy`; azioni riservate `commit.preview|apply|cancel|recover` in `CommitTab`; **Applica solo da qui**.
- Anello: `ContextActions` per faccia piana (Schizzo · Estrudi · Foro · Misura), bordo (Raccordo · Smusso · Misura); si apre dopo la selezione con la penna, si chiude con X, selezione del vuoto, azione scelta.
- Chip valore + tastierino: dimensione, diametro, parametri, coordinate numeriche via `NumericEntry` + `PaletteView.ShowKeypad`; thumbstick destro ←/→ ± passo, ↑/↓ passo 10/1/0,1; `ArmedField`/`SetArmedField` (dettatura) continuano a funzionare sullo stesso campo. Sostituisce `Numbers()`/`HomePanel.PromptText`.
- Input solo da `XrInput` (eventi semantici): niente `OVRInput` nel workspace. Trigger tenuto su maniglia = trascinamento (M5-08); A = snap; precisione (trigger sinistro) = trascinamenti 10× più lenti; X = Indietro; Grip = solo vista.
- Rimuovere `Render()`, `Page()`, `_panel`, `_screen` testuali, paragrafi di istruzione, «Blocca pannello», paginazione. Mantenere l'API pubblica usata da voce/runner/test (`Active`, `CanEnter`, `ActiveChanged`, `RequiresCadReview`, `Bind`, `SetScene`, `SetDocumentState`, `SetOnline`, `SetVisible`, `Open`, `Close`, `IsEnabled`, `Invoke`, `ArmedField`, `SetArmedField`, `FieldDimension`, `VoicePanel` → `null` ammesso se `WorkspaceVoiceTarget` lo gestisce; verificare) o adeguarla con i chiamanti.
- Foglio: entrando in uno schizzo → `SketchSheetView` in `Sheet`; azione «Vista modello»/«Foglio»; penna: punta come penna, raggio come fallback; thumbstick sinistro zoom/scorrimento del foglio.
- Modello a riposo sul piano di lavoro (`Workbench.Part`); `Ricentra`/`Adatta` da `XrInput.Recenter/Fit`.
- `AppController`: registra `_design` come `IActionProvider` attivo nel catalogo all'`Open`, `null` alla `Close`; inserisce chip/anello/barra nel guscio; posa la barra con `WorkbenchLayout.CommitBarPosition`.

### T4 — Test e runner
- `DesignWorkspaceTests`, `DesignManipulatorTests`, `WorkspaceVoiceTargetTests`, `VoicePanelTests` (Unity EditMode): adeguare a «invoca l'azione con id X» e agli eventi sintetici di `XrInput` (`SyntheticInputSource`, marcati sintetici); nessun test cancellato senza sostituto equivalente.
- `M3QuestAcceptance` (`Assets/XrSo/Xr/Acceptance/`): passa da «premi il pulsante con l'etichetta X» a «invoca l'azione con id X» + eventi sintetici; log `PASS [gate]` / `NOT COVERED [gate]` con motivo; fixture dedicata come oggi. Il runner M5 che entra in Progettazione (`M5QuestAcceptance`) va tenuto verde.
- Nuovi test EditMode: catalogo di Progettazione (id unici, ≤ 8 per scheda, etichette senza ambiguità tra abilitate), barra di conferma per ogni stato, anello per faccia/bordo, foglio↔modello senza cambiare le coordinate CAD.

### T5 — Verifica e documentazione (agente principale)
Core + EditMode completi; build APK (`XrSoBuild.BuildApkBatch`); runner M1–M5 sul Quest **solo se** visore sveglio e Inventor 2027 disponibili (vedi memoria `quest-workstation-setup`); altrimenti restano **NOT COVERED** con il motivo. Aggiornare `docs/xr-m6-verification.md` con la sezione «Fase 2»; i gate M6-02, M6-03, M6-04, M6-05 restano **aperti** finché la prova fisica non è eseguita (sottocasi automatici registrati come sintetici).

## Ordine

T1 ∥ T2 (T1 senza Unity; T2 usa Unity ma dipende da T1 solo per `SketchSheetView`: T2 parte dopo T1) → T3 → T4 → T5. Un commit per task dopo rilettura del diff e test verdi.
