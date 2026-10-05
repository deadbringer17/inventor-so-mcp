# Test automatici sul Quest 3 — standard M1–M9

Standard nato con M4 ([collaudo M4](xr-m4-collaudo.md)) ed esteso il 29 settembre
2026 a M1, M2, M3 e M5 ([piano](superpowers/plans/2026-09-29-quest-acceptance-runners.md)).
Il runner M6 (Fase 5 della [spec M6](superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md))
è stato scritto il 3 ottobre 2026. Un runner per milestone gira **dentro l'app sul Quest**, contro Inventor 2027
reale, su un documento di prova dedicato, e scrive log e screenshot come evidenza.

## Pezzi

| Pezzo | Percorso |
|---|---|
| Base comune (intent Android, log, attese, riflessione, screenshot, guardia fixture) | `Inventor XR SO/Assets/XrSo/Xr/Acceptance/QuestAcceptanceRunner.cs` |
| Runner M1, M2, M3, M5, M6, M7, M8, M9 | `Inventor XR SO/Assets/XrSo/Xr/Acceptance/M{1,2,3,5,6,7,8,9}QuestAcceptance.cs` |
| Runner M4 (stessi passi del collaudo del 28 settembre, ora sulla base comune) | `Inventor XR SO/Assets/XrSo/Xr/M4QuestAcceptance.cs` |
| Fixture Inventor M1, M2, M3, M5, M6, M7 (M8 e M9 riusano la M6) | `bridge/tests/QuestAcceptanceFixtures/` |
| Fixture Inventor M4 | `bridge/tests/M4LiveProbe -- --prepare-quest / --restore-quest` |
| Orchestrazione ADB | `scripts/run-quest-acceptance.ps1` (M8: `scripts/run-m8-acceptance.ps1`, M9: `scripts/run-m9-acceptance.ps1`) |
| Test di contratto (campi/metodi letti per riflessione, runner esclusi dall'APK ordinario) | `Inventor XR SO/Assets/XrSo/Tests/EditMode/QuestAcceptanceContractTests.cs` |

Regole:

- I runner compilano solo con `XR_SO_ACCEPTANCE`
  (`InventorXrSo.Editor.XrSoBuild.BuildAcceptanceApkBatch`); l'APK ordinario
  non li contiene.
- Partono solo con l'extra Android `xr_mN_acceptance=true`.
- Prima di ogni mutazione verificano che il documento attivo inizi con
  `XR_MN_Quest_Acceptance`. Dopo ogni preview controllano, tramite il backend,
  che documento e revisione di Inventor non siano cambiati.
  M8 e M9 riusano esplicitamente la fixture **M6** (guardia `XR_M6_Quest_Acceptance`), non un documento M8, M9 o dell'utente.
- Usano gli oggetti reali costruiti da `AppController`: workspace, `DesignSession`,
  backend su TLS pinnato. I percorsi sono gli stessi dei pulsanti UI.
- Evidenza nell'area persistente dell'app: `mN-acceptance.txt` e
  `mN-acceptance-*.png`. Ogni riga ha la forma `PASS [gate] ...`,
  `NOT COVERED [gate] motivo` oppure `Screenshot: ...`. L'ultima riga è
  `PASS COMPLETE; ...` oppure `FAIL; ...` (il runner M9 può chiudere anche con
  `PARTIAL; NOT COVERED: ...`, vedi sotto).
- Il runner **non** esercita controller fisici, leggibilità, tracking, microfono
  né audio. Un `PASS COMPLETE` chiude solo i sottocasi programmatici elencati
  sotto. I gate fisici restano aperti finché non vengono provati sul visore.

## Procedura sulla postazione Windows

M8 grapics: `scripts/run-m8-acceptance.ps1 -Apk <qa.apk> -OrdinaryApk <ordinary.apk>
-Serial <seriale>` verifica che il Quest sia sveglio prima di creare la fixture
M6, esegue il runner M8 in un processo figlio e ripristina documento e APK nel
`finally`. Richiede PowerShell 7, ADB sul PATH e Inventor 2027 con host connesso.
Il runner M8 riusa i controlli nativi/input M6 (id M6 nel log) e aggiunge tema,
font, contrasto, fit e 100 rebuild. Non certifica performance, reale DPI Windows,
scansione QR o sessione fisica. Stato/evidenze in [verbale M8](xr-m8-verification.md).

### Runner M9 annidato (sottoassiemi, fixture `m9n`)

`Inventor XR SO/Assets/XrSo/Xr/Acceptance/M9NestedQuestAcceptance.cs` (extra Android `xr_m9n_acceptance`, log `m9n-acceptance.txt`),
orchestrato da `scripts/run-m9-nested-acceptance.ps1` (prepara/ispeziona/ripristina la fixture `m9n`, mai un documento dell'utente).
Guardia propria `XR_M9N_Quest_Acceptance` (le guardie degli altri runner non cambiano). Fixture: Assieme3 contiene Assieme1 (PartA,
PartC) e Assieme2 (PartB), tutti salvati su disco e tenuti aperti. Scenario con input **sintetico** e Inventor reale: raggio su un corpo
di Assieme1 = selezione dell'occorrenza **diretta** del sottoassieme (senza errore); doppio Trigger = nuovo livello Assieme (pila
Assieme3 > Assieme1, contesto Assembly, fantasma del solo padre diretto, senza le parti appena aperte); doppio Trigger su PartA (pila a tre
livelli, Progettazione, fantasma = Assieme1); modifica feature (chip 10 -> 12 mm, Applica, riletto da Inventor, Undo XR); primo Torna con X
tenuto, secondo dalla scheda Documento; Assieme3 elenca di nuovo i due sottoassiemi; Assieme2 entrata e uscita con revisione invariata; marcatore «●»
confrontato con la revisione. Chiude `M9-02-subassembly` (nel runner M6 il gate si chiama `M9-02-subassembly-nested` e non rende parziale il verdetto).

### Runner M9 flessibile (sottoassiemi flessibili su scena grande, fixture `m9f`)

`Inventor XR SO/Assets/XrSo/Xr/Acceptance/M9FlexQuestAcceptance.cs` (extra Android `xr_m9f_acceptance`, log `m9f-acceptance.txt`, timeout 960 s),
orchestrato da `scripts/run-m9-flex-acceptance.ps1` (prepara/ispeziona/ripristina la fixture `m9f`, mai un documento dell'utente). Guardia propria
`XR_M9F_Quest_Acceptance`. Fixture: Robot contiene PartL1, AsmFixed (a terra, non flessibile; AsmInner normale e PartF) e AsmFlex (libero,
`ComponentOccurrence.Flexible = True`, a z = -5000 mm: AsmFlexInner flessibile con PartX e PartY, e PartG); nessun vincolo di assieme su AsmFlex (non
verificabile senza Inventor: `NOT COVERED M9F-constraint-probe`). Scenario con input **sintetico** e Inventor reale: raggio su un corpo di AsmFlex a 5 m
dall'origine (il runner verifica prima di premere che il raggio raggiunga il corpo, prova distanze 0.3/0.15/0.6/1.0 m e registra scala e distanze);
registra nel log il testo che l'utente legge (riepilogo, notice, HUD) e verifica la dicitura italiana (Sottoassieme flessibile…, Doppio Trigger o Apri per
entrare, tutte le istanze, nessun «flexible» grezzo); Sposta, Vincola, Giunto e Isola disabilitati con motivo e Apri abilitato; doppio Trigger ->
Robot > AsmFlex > AsmFlexInner > PartX (Progettazione, stessa modifica feature e Undo XR del runner annidato); tre Torna (X tenuto, scheda Documento, X tenuto)
con pila, fantasma del solo padre diretto, nulla salvato e marcatore coerente con la revisione visuale; poi Robot > AsmFixed > AsmInner (con l'Apri dell'anello) e ritorno con
revisione visuale invariata. Se l'HUD non mostra la nuova dicitura il gate `M9F-hud-text` e NOT COVERED (verdetto PARTIAL).

### Runner M9 hidden (definizioni caricate senza finestra, fixture `m9h`)

`Inventor XR SO/Assets/XrSo/Xr/Acceptance/M9HiddenQuestAcceptance.cs` (sottoclasse di `M9FlexQuestAcceptance`; extra Android `xr_m9h_acceptance`, log
`m9h-acceptance.txt`, timeout 960 s), orchestrato da `scripts/run-m9-hidden-acceptance.ps1`. Guardia propria `XR_M9H_Quest_Acceptance`. Fixture: la struttura di
`m9f`, ma dopo il salvataggio la preparazione chiude tutto e ricarica ogni definizione con `Documents.Open(path, false)` (caricata, **senza finestra**) e solo
Robot con `Documents.Open(path, true)`; rilegge `Documents.VisibleDocuments` e fallisce se una definizione ha una finestra. E la situazione reale dell'utente
(solo l'assieme di livello superiore ha una finestra), che le altre fixture non coprono. `--inspect-quest m9h` stampa per ogni documento `has_window`
(e `visible_documents`); lo script lo salva prima (`native-fixture-inspection-before.log`) e dopo la corsa. Scenario: lo stesso di m9f (Robot > AsmFlex >
AsmFlexInner > PartX e ritorno con Torna, poi AsmFixed > AsmInner), che passa solo se le definizioni senza finestra si possono attivare (`M9H-hidden-entry`).
Se l'ingresso fallisce il runner registra il testo esatto del notice del workspace Assieme e dell'HUD (oltre alla diagnostica dello scenario). Il percorso
Torna attiva il padre: i documenti attraversati hanno una finestra dopo l'ingresso. Il Quest non puo osservare le finestre: l'evidenza e `--inspect-quest`.

### Runner M9 (navigazione per contesto)

M9 (spec `docs/superpowers/specs/2026-10-04-m9-navigazione-contesto-design.md`, piano Task 19):
`scripts/run-m9-acceptance.ps1 -Apk <qa.apk> -OrdinaryApk <ordinario.apk> -Serial <seriale>` ha la stessa
struttura dello script M8: verifica che il Quest sia sveglio, prepara la fixture **M6** (il runner M9 non ha una
fixture propria, come M8), lancia `run-quest-acceptance.ps1 -Milestone m9` in un processo figlio (timeout 960 s) e
nel `finally` ispeziona e ripristina la fixture, riattiva il documento dell'utente e reinstalla l'APK ordinario.
Richiede PowerShell 7, ADB sul PATH e Inventor 2027 con host connesso. Intent `xr_m9_acceptance`; log
`m9-acceptance.txt`; screenshot `m9-acceptance-ghost-context.png`, `-legend-rest.png`, `-legend-armed.png`,
`-legend-component.png`; ogni riga ha prefisso `[M9Quest]` e i gate `M9-01`...`M9-12`.

Che cosa esercita (input **sintetico** e risposte di Inventor reali; nessun comando CAD diverso da quelli sotto):

| Sottocaso | Gate | Come |
|---|---|---|
| Assieme aperto dal documento attivo, scheda Documento (percorso, Torna, Salva, documenti aperti, Ricentra), nessuna azione `spaces.*` | M9-01 | stato della pila, catalogo e palette |
| Il documento cambia dal PC e dalla scheda Documento (elenco documenti aperti) | M9-01 | `ActivateOpenAsync` diretto (come farebbe il PC) e azione `doc.open.*` |
| Doppio Trigger su componente: parte e lamiera nel contesto giusto, livello nella pila con occorrenza e posa; ingresso rifiutato con motivo a revisione CAD pendente (guardia sintetica) e durante Sposta | M9-02 | due pressioni sintetiche sul corpo nel modello, con l'orologio del rilevatore pilotato dal runner (finestra di 350 ms, nessuno sleep) |
| Sottoassieme a due livelli e doppio Torna | M9-02 | solo se la fixture ha un'occorrenza di assieme (la M6 non ce l'ha: NOT COVERED) |
| Fantasma: presente, semitrasparente, non selezionabile (nessun collider né `CadBody`, layer Ignore Raycast, niente ombre), posato all'occorrenza (distanza dall'origine della parte), etichetta e revisione, coerente dopo Adatta, screenshot | M9-03 | `GhostContext` letto per riflessione dall'`AppController` |
| Torna: X breve = solo suggerimento, X tenuto 1 s (Poll sintetico con timestamp) e `doc.back` dalla scheda; «●» dopo una modifica; nessun salvataggio (l'unica chiamata di Torna è l'attivazione del padre) | M9-04 | `XrInput.Poll` con tempi espliciti |
| Schede per contesto (Assieme, Parte, Lamiera) senza schede inerti, gruppo Ispeziona scorso con lo stick e lasciato con X, Vista unica, Opzioni e Vincoli che compaiono da soli, Misura e Sezione attive durante la modifica e sospese solo da una revisione CAD | M9-05 | catalogo reale e palette |
| Ogni riga di `InputMap` per ogni stato (12 tasti x 5 stati): l'azione attesa, le righe senza voce restano mute, secondarie (doppio Trigger, X tenuto), rotazione di 15 gradi a riposo; stati Rest, SketchOpen, ArmedOrHandle, Keypad osservati anche dal vivo | M9-06 | frame sintetici su `XrInput` e `InputDispatcher` con sonda di stato forzata |
| Legenda 3D per stato: etichette = `InputMap.Active`, al massimo 12 caratteri, applicate entro 150 ms, anellini di X tenuto e doppio Trigger, interruttore Vista, screenshot | M9-07 | `ControllerLegend.Labels` |
| Doppio Trigger su faccia: scheda «Feature: nome», chip di distanza 10 mm, un solo `set_parameter` in anteprima, Applica dalla barra, parametro riletto da Inventor (12 mm), Undo XR di pulizia; tipo non supportato sulla lamiera | M9-09 | backend reale (`inventor_face_feature`, `inventor_plan_change`, commit) |
| Voce: comandi di spazio rifiutati con la spiegazione, vocabolario del contesto, Applica mai a voce, «apri <componente>» come il doppio Trigger, «torna» | M9-10 | testo iniettato nel `PushToTalkController` reale |

Esiti. `PASS COMPLETE` solo se nessun sottocaso del runner è rimasto NOT COVERED; altrimenti l'ultima riga è
`PARTIAL; NOT COVERED: <elenco>` e `run-quest-acceptance.ps1` esce con codice **4** (`outcome` `PARTIAL` nel
manifest). Le prove che stanno fuori dal runner sono dichiarate NOT COVERED con suffisso `-physical`, `-probe` o
`-suite` e non rendono il verdetto parziale, ma **restano aperte**: M9-08-probe (sonda live di `face_feature` su
ogni tipo), M9-11-suite (suite core/EditMode/bridge e runner M1-M8 migrati da rieseguire), M9-12-physical,
M9-07-physical (posizione e leggibilità delle etichette sui controller reali), M9-06-B-physical (tasto B passa da
`PushToTalkInput`), M9-10-physical (microfono). Con la fixture M6 il runner chiude oggi `PARTIAL` per costruzione:
`M9-02-subassembly` (la fixture non ha un sottoassieme), `M9-09-handle` (la maniglia di estrusione non è collegata
alla modifica feature: solo chip) e `M9-09-highlight` (`face_feature` non restituisce le facce: si evidenzia solo
quella scelta). Un sottoassieme annidato nella fixture e il collegamento della maniglia li chiuderebbero.

Il run **non è di sola lettura**: un Apply reale di `set_parameter` sull'estrusione del blocco (10 mm -> 12 mm)
e l'Undo XR; poi riattiva l'assieme. Dopo un run interrotto riattivare l'assieme a mano ed eseguire
`--restore-quest m6` prima di ripetere. Scritto e compilato (`XR_SO_ACCEPTANCE`) senza visore: **non ancora
eseguito** sul Quest. Gli esiti andranno in `docs/xr-m9-verification.md`.

Migrazione dei runner M1-M8 alla navigazione M9 (il Quest non era disponibile: restano da **rieseguire**, M9-11):
la scelta manuale dello spazio (`spaces.*`) non esiste più e il documento attivo apre da solo il workspace. M1 e M2
parcheggiano il workspace di authoring con `AppController.CloseAuthoring` (i loro gate sono sul percorso Ispeziona e
sulla scena 1:1) e M2 lo riparcheggia dopo ogni cambio documento; M3, M4 e M5 attendono l'apertura del router e
usano `Open()`/`Close()` dove prima c'era il ritorno a Ispeziona; M6 entra nel blocco con un doppio Trigger
sintetico, torna con `doc.back` dalla scheda Documento e controlla le schede del contesto; M7 seleziona i componenti
dalla lista Componenti di Assieme (la selezione su cui agiscono Visibilità e Verifica) al posto di Esplora. M4, M5 e
M6 spostano l'orologio del rilevatore a ogni pressione sintetica, così due tocchi sullo stesso corpo non diventano un
doppio Trigger involontario. Il test di contratto controlla che non restino azioni `spaces.*`.

```powershell
# 1. Test EditMode (include il contratto dei runner) e APK di collaudo
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode.xml`"" -Log "$env:TEMP\xrso-editmode.log"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoBuild.BuildAcceptanceApkBatch" -Log "$env:TEMP\xrso-acceptance-build.log"
Copy-Item "Inventor XR SO/Builds/InventorXrSo.apk" artifacts/InventorXrSo-acceptance.apk
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoBuild.BuildApkBatch" -Log "$env:TEMP\xrso-build.log"
Copy-Item "Inventor XR SO/Builds/InventorXrSo.apk" artifacts/InventorXrSo.apk

# 2. Per ogni milestone: fixture -> runner -> ripristino (host HTTPS e Quest già associati)
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest m3
pwsh scripts/run-quest-acceptance.ps1 -Milestone m3 -Apk artifacts/InventorXrSo-acceptance.apk -OrdinaryApk artifacts/InventorXrSo.apk
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --inspect-quest m3
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --restore-quest m3
```

Lo script installa l'APK e verifica l'hash sul visore. Avvia l'app con l'extra,
attende l'esito, scarica log e screenshot in `artifacts/mN-verification/` e
scrive un manifest `quest-acceptance-run-*.json`. Codici di uscita: 0 PASS,
1 FAIL, 2 TIMEOUT, 3 errore di setup. Con `-OrdinaryApk` reinstalla alla fine
l'APK ordinario. Horizon OS può mostrare «Passa ai controller» se il visore non
è indossato: il runner parte solo dopo che quel dialogo di sistema è stato
superato. Lo script non modifica le proprietà di test Meta.

Il manifest contiene anche `checks` estratti dalle righe `PASS [gate]` e
`NOT COVERED [gate]`, `runnerStarted`, `deviceWakefulnessAtLaunch` e
`timeoutPhase` (`before_runner_start` oppure `runner`). Un timeout senza log
interno non è un fallimento del caso CAD: indica che il runner non è partito.
Questi campi applicano a M1–M5 la distinzione fra prova sintetica sul Quest e
prova fisica prevista dai `CLAUDE.md`.

Per M6 la fixture è un assieme con due componenti (un blocco e una lamiera, parti
tenute aperte): `--prepare-quest m6`, poi `-Milestone m6` (l'esecuzione dura fino a
13 minuti: lo script usa 780 s di timeout se non si passa `-TimeoutSeconds`) e
`--restore-quest m6`. Il runner M6 **non è di sola lettura**: esegue un `Apply`
reale di estrusione sul blocco e lo annulla con l'Undo XR; alla fine riattiva
l'assieme. Dopo un run interrotto, riattivare l'assieme a mano prima del restore.

Per M7 (Ispeziona, verifica ingegneristica) la fixture è `XR_M7_Quest_Acceptance.iam` (`m7`):
quattro cubi `M7_A`…`M7_D`, A e B in interferenza (2000 mm³), C a 30 mm da A, D libero, un
vincolo in errore `M7_Sick` e una descrizione BOM vuota (`DESCRIPTION_MISSING`). Il runner è di sola lettura (nessuna
transazione); lo script usa 480 s di timeout se non si passa `-TimeoutSeconds`. Sequenza:

```bash
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest m7
# run-quest-acceptance.ps1 -Milestone m7 -Apk <apk QA> -OrdinaryApk <apk ordinario>
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --inspect-quest m7
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --restore-quest m7
```

PowerShell 7 è disponibile nel runtime Codex; `adb` non è nel PATH e viene
configurato dal wrapper `artifacts/m7-verification/run-m7.ps1`. Il visore deve
essere sveglio. Tutte le azioni sono invocate per id sul catalogo (input
**sintetico**); le risposte di Inventor sono reali. Il runner M7 ha raggiunto
**PASS COMPLETE** il 4 ottobre 2026, 14:57 Europe/Rome (manifest
`quest-acceptance-run-20261004-145650.json`). Restano NOT COVERED M7-06 (offline e documenti parte,
coperti da EditMode), M7-07 (tempi su un assieme reale: sonda PC `--probe-active`) e M7-08
(leggibilità fisica da seduto).

Per M4 la fixture si prepara con
`dotnet run --project bridge/tests/M4LiveProbe -- --prepare-quest` (e
`--restore-quest`); il resto è identico con `-Milestone m4`.

Non usare Inventor né il visore durante un run. M3, M5 e M6 lasciano la fixture
modificata (estrusione, flangia, sviluppo): va sempre eseguito
`--restore-quest` prima di ripetere il run.

## Copertura per milestone

| Milestone | Coperto dal runner | Resta fisico / non coperto |
|---|---|---|
| M1 | DoD 1–2 sessione Online con pairing salvato; DoD 3 Home con PC, versioni, documento e tipo; DoD 4 due occurrence, lato lungo 0,100 m a 1:1, luce 50 mm; DoD 5/7 pick di occurrence e highlight accettato da Inventor; DoD 6 triangolo → faccia e selezione faccia; MR ↔ Studio | DoD 8 cambio documento dal desktop, DoD 9 perdita rete, pairing QR/codice, raggio e trigger fisici |
| M2 | Inspect dal polso, Browser, proprietà (volume 120000 mm³ come lettura diretta, «—» per i valori assenti), breadcrumb, misura tra centri 150 mm e pin, sezione con offset numerico, Table/Fit/1:1, MR/Studio, attivazione documento dal Browser e invalidazione di misure, selezione e sezione | cambio revisione dello stesso documento, grab di pannello e piano, raggio, leggibilità |
| M3 | casi 1, 2, 9, 10, 11, 12, 15 del [collaudo M3](xr-m3-quest-collaudo.md): preview di estrusione con revisione invariata, Annulla, Applica con volume 12000 + π·25·20 mm³, Undo/Redo XR, piano stale rifiutato, foro cieco, raccordo, errore di validazione correggibile, parametro, ritorno a Inspect | disegno di schizzo e vincoli, drag del manipolatore, pick di facce e spigoli col raggio (impostati per riflessione), casi 3–8, 13, 14 |
| M4 | A/B e vincoli compatibili, preview Move, Cancel, Apply, centro nativo, Undo/Redo, stale, riapertura; runner esteso con A/B dopo CAD Move, gesture sintetiche a 0,25×, blocco UI e perdita tracking | gesto reale, leggibilità, click-through e tracking fisico di A02/A07/A14 |
| M5 | M5-01, M5-02, M5-03 (campo numerico; gesto **sintetico** Grip+Trigger sul pomello a 1:1 e a 0,25× con +10 mm e preview nativa a revisione invariata, Grip semplice che non scrive la bozza, rilascio del Trigger e perdita tracking che chiudono il drag senza CAD, Trigger sintetico su un bordo reale), M5-04 (Cut preview/Annulla/Applica/Undo), M5-05, M5-06 (Detach senza chiamate al backend; Grip **sintetico** che sposta solo la mesh piana, non il modello), M5-07 (solo stale), M5-09, M5-10, M5-11 con testo iniettato nel push-to-talk reale senza microfono | `M5-03-physical` e `M5-06-physical`: controller e tracking reali, sensazione a scala ridotta, leggibilità, confronto visivo dello sviluppo col piegato; Face e regola/spessore, rete/preview tardiva/commit incerto, M5-08 microfono e pulsante B, M5-12 |
| M6 | Input **sintetico** (frame di `SyntheticInputSource` sull'`XrInput` dell'app, azioni invocate per id sull'`ActionCatalog` reale). M6-01 tavolozza figlia del controller sinistro, schede e scheda Spazi su Ispeziona, Progettazione, Lamiera e Assieme, rotazione delle schede con lo stick sinistro, nessun pannello fluttuante accanto alla testa; M6-02 schizzo sul foglio orizzontale all'altezza del piano, linea con la punta penna e linea col raggio, «Vista modello» ↔ «Foglio» senza variare elementi e piano di schizzo, stesso punto fisico → stesse coordinate CAD; M6-03 chip e tastierino, passi dello stick 1/10/0,1, modalità precisione (drag della maniglia della flangia: +10 mm normale, +1 mm con Trigger sinistro), dettatura nel campo armato; M6-04 anello su componente, bordo e faccia piana, chiusura con X e col vuoto, azione dell'anello; M6-05 barra Empty, Draft, Previewing, Ready, Error, Offline, Uncertain, Applied (un Apply reale e il suo Undo), Applica solo dalla barra, tabella pura con Stale; M6-06 Assieme sollevato, isolamento solo visivo (revisione e occorrenze di Inventor invariate), «Apri in Progettazione» e «Apri in Lamiera» dal componente isolato; M6-07 una e due mani solo vista, Adatta, Ricentra; M6-08 `ResolveVoice` (disabilitato e frase ignota rifiutati, «applica» non committa, ambiguità su un catalogo controllato) con testo iniettato | M6-10 prova fisica da seduto (leggibilità, comfort, precisione della penna, aptica, trascinamento a Trigger); ergonomia e tracking reali; microfono e audio; calibrazione dell'altezza del piano (M6-07: nessun percorso nell'app la usa); Stale dal vivo; anello di Ispeziona; M6-09 (i runner M1–M5 migrati vanno rieseguiti) |
| M9 | vedi la tabella dei sottocasi nella sezione «Runner M9»: pila e contesto che seguono il documento (Quest e PC), doppio Trigger su componente, fantasma, Torna, schede per contesto, ogni riga di `InputMap`, legenda per stato, modifica feature da faccia con parametro riletto da Inventor, voce per contesto; input **sintetico** | M9-12 prova fisica da seduto; posizione e leggibilità della legenda sui controller; tasto B e microfono; sonda live `face_feature` su ogni tipo (M9-08); regressioni M1-M8 (M9-11); con la fixture M6: sottoassieme, maniglia e evidenziazione di tutte le facce (PARTIAL) |

## Stato

Runner, fixture, script e test di contratto sono stati scritti il 29 settembre
2026 in ambiente Linux. I punti dell'API Inventor previsti da verificare dal
vivo sono elencati nel README delle fixture. Gli esiti sono registrati nei
documenti di verifica delle singole milestone.

Aggiornamento Windows/Quest del 29 settembre 2026: fixture compilate con l'interop
2027 e provate in Inventor reale; runner eseguiti sul Quest 3. M1, M2, M4 e M5
hanno `PASS COMPLETE` programmatico; M3 resta incompleto sul caso del raccordo da
100 mm (`PreviewReady` inatteso). I manifest e i log sono in
`artifacts/mN-verification/quest-acceptance-run-20260929-10*.json` e i dettagli
nei rispettivi verbali. Unity EditMode: 176/177, con un fallimento M5 sul test
di rilascio della mesh `FlatPatternDisplayTests.MeshesAndMaterialsAreReleasedWithTheDisplay`.
Il `PASS COMPLETE` non copre i gate fisici indicati nella tabella precedente.

Ritest M3 dello stesso giorno: la sonda CAD ha dimostrato che 100 mm su un solo
spigolo è valido; C11 usa ora il gruppo degli spigoli rettilinei, per cui 1 mm è
valido e 100 mm causa un vero rollback. M3 ha raggiunto `PASS COMPLETE` sul Quest 3
(`artifacts/m3-verification/quest-acceptance-run-20260929-105608.json`). Tutte e
cinque le milestone hanno quindi superato i sottocasi programmatici dei rispettivi
runner; restano distinti i gate fisici e il fallimento EditMode M5 indicati sopra.

Correzione successiva del test M5: il caso `MeshesAndMaterialsAreReleasedWithTheDisplay`
invocava `DestroyImmediate` in EditMode, dove Unity non esegue `OnDestroy` per il
componente runtime. Il test richiama ora esplicitamente la routine di pulizia e
verifica la distruzione di root, mesh e materiale. Suite EditMode completa:
**177/177**, zero fallimenti (`%TEMP%/xrso-editmode-m5-final.xml`). Il codice runtime
del display non è stato modificato.

Estensione M4 del 29 settembre: il runner verifica anche che A/B selezionate
dopo CAD Move cancellino la vecchia preview e non abilitino Applica; simula
traslazione e rotazione del controller a scala 0,25× e controlla la preview
nativa con revisione invariata; simula un hit della UI e la perdita di tracking,
poi verifica la riapertura pulita di Assembly. Queste chiamate sintetiche al
percorso di input non misurano il controller fisico né la leggibilità. L'APK di
collaudo è stato compilato con successo (SHA-256
`E248D889FF02351E21DB5E67C6EEA83D97BF928195F872241FA9F4D0663977BE`).
Il primo avvio ha raggiunto `TIMEOUT` prima della creazione del log interno
perché il Quest era in standby: manifest
`artifacts/m4-verification/quest-acceptance-run-20260929-133351.json`.
I nuovi sottocasi non sono ancora marcati passati sul Quest.

Lo standard comune è ora scritto in `CLAUDE.md` e `bridge/CLAUDE.md`: ogni
gate distingue unit test/FakeAddIn, runner Quest con input sintetico e prova
fisica. I runner M1 e M3 dichiarano esplicitamente i rispettivi sottocasi
fisici non coperti; M4 dichiara A01/A02/A07/A14 fisici. La build QA successiva
è riuscita (SHA-256
`853C72E81A26D1B3EA0C99AB8191369A09BC6C384C608F1A2EA0EC0A2366286B`).
Il manifest `quest-acceptance-run-20260929-143512.json` conferma
`runnerStarted=false`, `deviceWakefulnessAtLaunch=Asleep`,
`timeoutPhase=before_runner_start` e nessun check eseguito. L'APK ordinario è
stato reinstallato con hash verificato. Un comando ADB di wake ha portato
brevemente il Quest ad `Awake`, poi il visore è tornato in standby senza
prossimità; nessuna proprietà Meta di test è stata modificata.

Run completo Windows/Quest del 29 settembre: suite backend **951/951**, core XR
**373/373**, Unity EditMode **178/178**; build delle fixture e dell'APK QA
riuscite. Dopo il fix del limite di paginazione nel runner M2 e della preview
residua nel passaggio da CAD Move ad A/B, i cinque runner hanno concluso con
`PASS COMPLETE` programmatico:

| Milestone | Manifest del run riuscito | Esito |
|---|---|---|
| M1 | `artifacts/m1-verification/quest-acceptance-run-20260929-173053.json` | PASS |
| M2 | `artifacts/m2-verification/quest-acceptance-run-20260929-174042.json` | PASS |
| M3 | `artifacts/m3-verification/quest-acceptance-run-20260929-174315.json` | PASS |
| M4 | `artifacts/m4-verification/quest-acceptance-run-20260929-175112.json` | PASS |
| M5 | `artifacts/m5-verification/quest-acceptance-run-20260929-175218.json` | PASS |

M5 ora verifica anche Cut Apply e Undo contro la revisione nativa. M4 ha avuto
un timeout transitorio sulla cronologia XR dopo Apply; la ripetizione da una
fixture nuova è passata. Ogni fixture è stata ispezionata e chiusa senza
salvare, il documento dell'utente è stato riattivato e l'APK ordinario
reinstallato. I gate fisici elencati nella tabella restano da collaudare.
La build ordinaria finale, che include il fix M4, è in
`artifacts/InventorXrSo-full-ordinary.apk`: SHA-256
`DCD5846A42040E1AEFDDE6BB6053EF403400A45585A31A16EC79CE5140D509FF`,
uguale all'hash dell'APK installato sul Quest.

Runner M6 (3 ottobre 2026): scritto e compilato con `XR_SO_ACCEPTANCE`, **non ancora
eseguito** sul Quest con Inventor reale (stato NOT RUN). Gli esiti andranno in
[xr-m6-verification.md](xr-m6-verification.md), sezione «Fase 5 — Collaudo».

Runner M9 (5 ottobre 2026): scritto e compilato con `XR_SO_ACCEPTANCE`, coperto dal test di contratto (gate
M9-01...M9-12 nel log, guardia sulla fixture M6, `PASS COMPLETE` solo senza sottocasi NOT COVERED), **non ancora
eseguito** sul Quest con Inventor reale (stato NOT RUN). I runner M1-M8 migrati alla navigazione M9 compilano ma non
sono stati rieseguiti. Gli esiti andranno in `docs/xr-m9-verification.md`.
