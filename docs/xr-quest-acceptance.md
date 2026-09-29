# Test automatici sul Quest 3 — standard M1–M5

Standard nato con M4 ([collaudo M4](xr-m4-collaudo.md)) ed esteso il 29 settembre
2026 a M1, M2, M3 e M5 ([piano](superpowers/plans/2026-09-29-quest-acceptance-runners.md)).
Un runner per milestone gira **dentro l'app sul Quest**, contro Inventor 2027
reale, su un documento di prova dedicato, e scrive log e screenshot come evidenza.

## Pezzi

| Pezzo | Percorso |
|---|---|
| Base comune (intent Android, log, attese, riflessione, screenshot, guardia fixture) | `Inventor XR SO/Assets/XrSo/Xr/Acceptance/QuestAcceptanceRunner.cs` |
| Runner M1, M2, M3, M5 | `Inventor XR SO/Assets/XrSo/Xr/Acceptance/M{1,2,3,5}QuestAcceptance.cs` |
| Runner M4 (stessi passi del collaudo del 28 settembre, ora sulla base comune) | `Inventor XR SO/Assets/XrSo/Xr/M4QuestAcceptance.cs` |
| Fixture Inventor M1, M2, M3, M5 | `bridge/tests/QuestAcceptanceFixtures/` |
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

Per M4 la fixture si prepara con
`dotnet run --project bridge/tests/M4LiveProbe -- --prepare-quest` (e
`--restore-quest`); il resto è identico con `-Milestone m4`.

Non usare Inventor né il visore durante un run. M3 e M5 lasciano la fixture
modificata (estrusione, flangia, sviluppo): va sempre eseguito
`--restore-quest` prima di ripetere il run.

## Copertura per milestone

| Milestone | Coperto dal runner | Resta fisico / non coperto |
|---|---|---|
| M1 | DoD 1–2 sessione Online con pairing salvato; DoD 3 Home con PC, versioni, documento e tipo; DoD 4 due occurrence, lato lungo 0,100 m a 1:1, luce 50 mm; DoD 5/7 pick di occurrence e highlight accettato da Inventor; DoD 6 triangolo → faccia e selezione faccia; MR ↔ Studio | DoD 8 cambio documento dal desktop, DoD 9 perdita rete, pairing QR/codice, raggio e trigger fisici |
| M2 | Inspect dal polso, Browser, proprietà (volume 120000 mm³ come lettura diretta, «—» per i valori assenti), breadcrumb, misura tra centri 150 mm e pin, sezione con offset numerico, Table/Fit/1:1, MR/Studio, attivazione documento dal Browser e invalidazione di misure, selezione e sezione | cambio revisione dello stesso documento, grab di pannello e piano, raggio, leggibilità |
| M3 | casi 1, 2, 9, 10, 11, 12, 15 del [collaudo M3](xr-m3-quest-collaudo.md): preview di estrusione con revisione invariata, Annulla, Applica con volume 12000 + π·25·20 mm³, Undo/Redo XR, piano stale rifiutato, foro cieco, raccordo, errore di validazione correggibile, parametro, ritorno a Inspect | disegno di schizzo e vincoli, drag del manipolatore, pick di facce e spigoli col raggio (impostati per riflessione), casi 3–8, 13, 14 |
| M4 | come nel collaudo del 28 settembre: A/B e vincoli compatibili, preview Move, Cancel, Apply, centro nativo, Undo/Redo, stale, riapertura | gate A01, A02, A07, A14 fisici |
| M5 | M5-01, M5-02, M5-03 (solo campo numerico), M5-04 (Cut preview/Annulla), M5-05, M5-06, M5-07 (solo stale), M5-09, M5-10, M5-11 con testo iniettato nel push-to-talk reale senza microfono | gesto flangia, Face e regola/spessore, rete/preview tardiva/commit incerto, M5-08 microfono e pulsante B, M5-12 |

## Stato

Runner, fixture, script e test di contratto sono stati scritti il 29 settembre
2026 in ambiente Linux, senza Unity, dotnet, Inventor né Quest: **non sono
ancora stati compilati né eseguiti**. Prima dell'uso servono la compilazione
Unity dei due APK, il test di contratto EditMode e la compilazione di
`QuestAcceptanceFixtures` con l'interop di Inventor 2027. I punti dell'API di
Inventor da confermare dal vivo sono elencati nel README delle fixture. Nessun
gate è stato segnato come passato sulla base di questi file. Gli esiti vanno
registrati nei documenti di verifica delle singole milestone.
