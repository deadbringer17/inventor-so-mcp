# Test automatici sul Quest 3 — standard M1–M7

Standard nato con M4 ([collaudo M4](xr-m4-collaudo.md)) ed esteso il 29 settembre
2026 a M1, M2, M3 e M5 ([piano](superpowers/plans/2026-09-29-quest-acceptance-runners.md)).
Il runner M6 (Fase 5 della [spec M6](superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md))
è stato scritto il 3 ottobre 2026. Un runner per milestone gira **dentro l'app sul Quest**, contro Inventor 2027
reale, su un documento di prova dedicato, e scrive log e screenshot come evidenza.

## Pezzi

| Pezzo | Percorso |
|---|---|
| Base comune (intent Android, log, attese, riflessione, screenshot, guardia fixture) | `Inventor XR SO/Assets/XrSo/Xr/Acceptance/QuestAcceptanceRunner.cs` |
| Runner M1, M2, M3, M5, M6, M7 | `Inventor XR SO/Assets/XrSo/Xr/Acceptance/M{1,2,3,5,6,7}QuestAcceptance.cs` |
| Runner M4 (stessi passi del collaudo del 28 settembre, ora sulla base comune) | `Inventor XR SO/Assets/XrSo/Xr/M4QuestAcceptance.cs` |
| Fixture Inventor M1, M2, M3, M5, M6, M7 | `bridge/tests/QuestAcceptanceFixtures/` |
| Fixture Inventor M4 | `bridge/tests/M4LiveProbe -- --prepare-quest / --restore-quest` |
| Orchestrazione ADB | `scripts/run-quest-acceptance.ps1` |
| Test di contratto (campi/metodi letti per riflessione, runner esclusi dall'APK ordinario) | `Inventor XR SO/Assets/XrSo/Tests/EditMode/QuestAcceptanceContractTests.cs` |

Regole:

- I runner compilano solo con `XR_SO_ACCEPTANCE`
  (`InventorXrSo.Editor.XrSoBuild.BuildAcceptanceApkBatch`); l'APK ordinario
  non li contiene.
- Partono solo con l'extra Android `xr_mN_acceptance=true`.
- Prima di ogni mutazione verificano che il documento attivo inizi con
  `XR_MN_Quest_Acceptance`. Dopo ogni preview controllano, tramite il backend,
  che documento e revisione di Inventor non siano cambiati.
- Usano gli oggetti reali costruiti da `AppController`: workspace, `DesignSession`,
  backend su TLS pinnato. I percorsi sono gli stessi dei pulsanti UI.
- Evidenza nell'area persistente dell'app: `mN-acceptance.txt` e
  `mN-acceptance-*.png`. Ogni riga ha la forma `PASS [gate] ...`,
  `NOT COVERED [gate] motivo` oppure `Screenshot: ...`. L'ultima riga è
  `PASS COMPLETE; ...` oppure `FAIL; ...`.
- Il runner **non** esercita controller fisici, leggibilità, tracking, microfono
  né audio. Un `PASS COMPLETE` chiude solo i sottocasi programmatici elencati
  sotto. I gate fisici restano aperti finché non vengono provati sul visore.

## Procedura sulla postazione Windows

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

La workstation non ha PowerShell 7: si usa la copia per PS 5.1, come nelle run M6; `adb` non è
nel PATH; il visore deve essere sveglio. Tutte le azioni sono invocate per id sul catalogo
(input **sintetico**); le risposte di Inventor sono reali. Il runner M7 è scritto, **non ancora
eseguito** sul Quest (stato NOT RUN). Restano NOT COVERED M7-06 (offline e documenti parte,
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
