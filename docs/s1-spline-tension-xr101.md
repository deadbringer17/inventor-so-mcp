# S1 — Tensione delle spline nell'editor XR, incremento 101

28 ottobre 2026. Integrazione sperimentale del comando nativo
[99](s1-spline-tension-edit99.md) nel percorso condiviso Parte/Lamiera;
estende la creazione aperta/chiusa dell'[incremento 100](s1-spline-closure-xr100.md).
Perimetro S1-04/05/13/14/16, senza chiudere la parità complessiva S1.

## Contratto e comportamento

Selezionando una sola spline interpolata intera, **Modifica tensione spline**
apre il tastierino con il valore nativo corrente. Sono richiesti capacità
backend, snapshot completa e riferimento attuale a documento/schizzo/revisione.
Spline CV, altre geometrie, sottoparti e selezioni multiple non sono ammesse.
Proiezioni, blocchi e geometrie derivate restano inoltre soggette alle guardie
di ownership del plugin; il client non deduce il flag Reference quando assente.

Il campo accetta interi da 0 a 100, senza arrotondare valori frazionari.
Tastiera, dettatura e passi rispettano lo stesso dominio. La conferma prepara
soltanto una bozza; Anteprima e Applica sono espliciti. Cancel/revisione nuova
revocano i callback pendenti. Dopo refresh la bozza conserva la tensione e
richiede selezione e conferma dei nuovi riferimenti. Il Core valida di nuovo
tipo, cardinalità, dominio e binding prima di pubblicare la bozza o confermarla.

Il backend conserva il contratto sperimentale esistente: un setter nativo
`SketchSpline.Tension` nella transazione atomica posseduta, senza sostituire
la curva, impostare FitMethod o modificare preferenze globali.

## Verifica

- Core: valori interi/malformati, riferimenti estranei/scaduti, capacità e tipo,
  congelamento della bozza e riselezione. Campo numerico intero: estremi,
  frazioni, non finiti, passi e conservazione del buffer non valido.
- EditMode Parte/Lamiera: input dal valore nativo, bozza senza commit,
  cancellazione, revisione, refresh/riselezione, selezioni errate/multiple.
- Runner S1: regressione delle otto creazioni 100, poi tensioni 0/50/100 su
  spline interpolate aperte/chiuse in Parte/Lamiera. Picking dal prodotto,
  rifiuti nel tastierino, preview/rollback, Cancel, commit e prima snapshot
  corrente con identità/chiusura/metodo/cardinalità conservati. Renderer
  confrontato con gli stroke esatti del piano nativo.
- Modalità mirata Editor `XR_SO_S1_SPLINE_TENSION_ONLY=1`: non attribuisce
  PASS ai casi storici saltati. Input sintetici con Inventor reale, fixture
  dedicate e ripristino separato dal verdetto del runner.

## Esiti

Evidenze in `artifacts/s1-verification/increment101/20261028-tension-xr/`.

- Suite complete: Core **1.393**, backend **1.926**, Capture.Worker **16**,
  Unity EditMode **1.193 PASS**: **4.528** casi complessivi, zero falliti.
  Dopo il caching immutabile della tensione nella snapshot: Core completo
  ripetuto (**1.393 PASS**) e **57 EditMode mirati PASS**, conteggiati come
  ripetizioni e non sommati. Il primo mirato aveva un errore nella preparazione
  Lamiera del test; corretta la fixture con refresh della capacità dichiarata,
  quindi suite completa e mirati verdi. XML/log iniziali conservati.
- Meta XR Simulator 207.1 + Inventor 2027, `owned-simulator-r1`:
  **20 casi PASS, 1.392 asserzioni**: otto creazioni e dodici modifiche tensione.
  `runner-result.json`: `success=true`, verdetto **PARTIAL**; i casi storici
  testo/parametri/linea sono esclusi esplicitamente da questa corsa.
  Conservati ventuno screenshot, readback prima/dopo e confronto renderer/stroke.
  `readback-consistency.json` verifica offline le dodici combinazioni univoche
  e le invarianti registrate; non è un nuovo run CAD né un oracle indipendente.
- `native-cleanup.json`: fixture ripristinate, PID QA 136992 assente,
  host terminato con exit 0, credenziali private rimosse, manifest originale
  ripristinato e DLL ordinaria immutata. `host-evidence/stopped.json` attesta
  HTTP fermato e zero documenti posseduti prima di Quit. PID originale
  120244 conservato; nessun riavvio o chiusura del disegno dell'utente.
  Il preflight della procedura ordinaria aveva rilevato documenti dell'utente
  e si era fermato prima di preparare fixture o chiudere Inventor; per questo
  la corsa usa il percorso a istanza posseduta descritto sotto.
- `BuildQxApksBatch` terminato con exit 0; ispezione ARM64 IL2CPP **PASS**:
  16 nomi runner esatti nel QA, zero nell'ordinario. Nessuna esecuzione hardware.
  QA: 95.770.547 byte, SHA256
  `9E423EEC8CC175C78552891AEA6C5FAF7D8AF7BAF1995571D6A4D4AD987456AC`.
  Ordinario: 58.008.692 byte, SHA256
  `DDC952C6403A10364805D0625A6A795B2469CC3CD61E38058DC6491D720BE6E7`.
  `package-inspection.json` contiene percorsi, dimensioni e metadati controllati.
- Font Satoshi generati durante Play Mode archiviati in `generated-fonts/`,
  poi ripristinati selettivamente; modifiche utente preesistenti conservate.
  `git diff --check` passa. Sorgenti e consegna congelati nei manifest locali.

Controllo finale dopo le build (`final-installation.json`, 15:11 UTC):
manifest e DLL originali corrispondono agli hash iniziali. Non risultano processi
Inventor e MCP risponde `NO_TARGET`. L'host aveva attestato PID originale
120244 presente al cleanup, circa diciannove minuti prima; la causa della
chiusura successiva non è stata osservata. Nessun comando della corsa ha chiuso
o riavviato quel processo. Il controllo finale non attribuisce uno stato salvato
al disegno dell'utente e non riapre documenti.

Il campo descrittivo `cad` del wrapper generico in `simulator/result.json`
riporta ancora la dicitura storica «native M7 fixture». Per questa corsa fanno
fede `fixture=s1` in `runner-result.json` e le identità Parte/Lamiera attestate
dall'host S1; nessuna fixture M7 è stata preparata. Il runner lascia la verifica
del ripristino all'host: il suo `NOT COVERED S1-16-fixture-restoration` resta
nel log, accompagnato dall'esito separato di cleanup, senza riscrivere il risultato.

## Simulatore con Inventor dell'utente aperto

`scripts/run-s1-owned-simulator.ps1` riusa l'host di fixture
`bridge/tests/S1QuestOwnedHost73`, senza ricercare il documento attivo nella ROT.
Il controllo avviene sul PID e sull'ora di avvio di una nuova istanza COM:
un processo preesistente viene rifiutato prima di accedere a Documents o Quit.
L'host attesta DLL caricata, documenti posseduti e target HTTPS esatto.
La corsa usa il wrapper GUI `Invoke-Simulator.ps1`, pin TLS e bootstrap generato
dall'host in directory privata; le credenziali non entrano nelle evidenze.
Non eseguire contemporaneamente altre fixture, importazioni o build Unity.

```powershell
& scripts/run-s1-owned-simulator.ps1 `
  -OutputDirectory artifacts/simulator/s1-tensione-nuovo `
  -AddinDirectory artifacts/simulator/addin-qa-nuovo -Scope tension
```

Compilare prima l'add-in sperimentale nella cartella QA e
`bridge/tests/S1QuestOwnedHost73`. Il finally chiude soltanto i documenti
posseduti, ferma l'host e il relativo processo Inventor, verifica la presenza
dei processi precedenti e ripristina il manifest byte per byte. Conserva
`native-cleanup.json` e le attestazioni pubbliche dell'host. Il disegno aperto
dall'utente nella sessione originale resta fuori dal perimetro della fixture.

## Limiti

La matrice del plugin 99 resta evidenza separata e usa il suo candidato
congelato. La UI non completa modifica punti/maniglie, dipendenze/vincoli,
persistenza/riapertura, tutte le opzioni o l'oracle desktop. Il simulatore
non certifica input fisici, ergonomia, tracking o GPU Quest.
