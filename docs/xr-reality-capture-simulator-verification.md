# Reality Capture — collaudo nel simulatore

26 ottobre 2026, branch `codex/reality-capture`, base `0e81660` con le modifiche
locali di questa consegna. **Collaudo software concluso con i limiti sotto.**
Le corse precedenti di `nuova_ux` non vengono attribuite a questo branch.
L’accettazione completa di tutte le milestone storiche rimane parziale.

## Integrazione della PR dopo la UX a quattro attività

Il checkpoint `ea4c68c` conserva i risultati completi del collaudo sul ramo
Capture prima dell’integrazione. La PR #14 di `nuova_ux` risulta già integrata
in `main` (`87bb820`): questo branch incorpora quel main, preserva le quattro
attività e aggiunge le correzioni alla preview e ai runner. I conteggi e gli
APK delle tabelle seguenti appartengono al checkpoint Capture; non vengono
riattribuiti automaticamente alla versione integrata.

Verifica della versione integrata `8dee978`, con evidenze separate in
`artifacts/reality-capture/pr-integration-20261026-112241/`:

| Livello | Esito | Evidenza |
|---|---|---|
| Core XR | 1.355 PASS | `core.trx` |
| Backend, corsa finale | 1.926 PASS | `backend-final.trx` |
| Capture.Worker | 16 PASS | `pc/worker.trx` |
| Unity EditMode Android | 1.165 PASS, zero fail/skip | `editmode.xml` |
| Smoke con quattro attività e Capture offline | 38 controlli PASS, exit 0 | `fake-offline/result.json`, `pass.marker`, PNG e log |

Le quattro suite totalizzano **4.462 PASS** nella selezione finale. Il backend
ha avuto prima due fallimenti distinti: cancellazione del piano non propagata
(`pc/backend.trx`) e timeout di 20 secondi dell’host pairing
(`backend-repeat.trx`). Entrambi passano nelle ripetizioni mirate
(`backend-cancellation-targeted.trx`, `backend-pairing-targeted.trx`) e nella
successiva corsa completa. I sorgenti backend coincidono con `main`; la causa
dell’intermittenza resta da isolare e i fallimenti originali sono conservati.
Non viene introdotto un fix backend in questa PR.

Lo smoke usa input sintetico e HTTPS FakeAddIn, arresta realmente il proprio
host e verifica che Capture rimanga utilizzabile offline. Non certifica COM
nativo, acquisizione camera, Android/Keystore, ergonomia o GPU Quest.
Il log conserva gli avvisi JobTempAlloc, senza attribuirne la causa ai fix.

### APK della versione integrata

`BuildQxApksBatch` e `Inspect-SimulatorPackages.ps1` terminano con exit 0;
`package-inspection.json` conferma ARM64 IL2CPP per entrambi, tutti i **16**
nomi dei runner (QX incluso) nei metadati QA e **zero** nell’ordinario.
È un’ispezione del pacchetto, non un’esecuzione sul dispositivo.

| APK | Byte | SHA-256 |
|---|---:|---|
| QA Development | 95.733.591 | `28BD4F936C839BF5DDB64C5C5EA98E7B2DCF0CB3D7729DBE8DA586A8D518BFB6` |
| Ordinario | 58.004.712 | `24D4C6546635005D203BD278816729EEE59BDAAC927AFA267940C7AFA272F69C` |

Log: `apks.log`; pacchetti: `apks/`, nella radice post-integrazione.
`integration-summary.json` e `source-manifest.json` conservano conteggi e hash
delle sorgenti. Le modifiche generate a font/settings sono ripristinate a fine
verifica; le istruzioni locali dell’utente rimangono escluse dai commit.

## Evidenze finali del checkpoint Capture precedente

Radice: `artifacts/reality-capture/simulator-20261026-065832/`.
`summary.json` seleziona le corse finali e ricava i gate anche dai log testuali:
alcuni runner storici scrivevano esclusioni con `Record` senza inserirle nel
JSON. Il collector comune ora conserva anche quelle righe. Il log originale
resta la fonte per il perimetro di ogni asserzione.

| Livello | Esito | Evidenza relativa alla radice |
|---|---|---|
| Core XR | 1.337 PASS | `pc-final/core.trx` |
| Backend | 1.926 PASS | `pc-final/backend.trx` |
| Pipeline PC / Capture.Worker | 16 PASS | `pc-final/worker.trx` |
| Unity EditMode Android | 1.152 PASS, zero fail/skip | `editmode-final04.xml` |
| 14 runner nel simulatore | 9.393 asserzioni, tutti exit 0; M9 e S1 PARTIAL | `summary.json`, serie sotto |
| Capture con perdita reale dell’host FakeAddIn | 10 controlli PASS, exit 0 | `fake-offline/result.json`, log e tre PNG |
| Riferimento RC4 nativo | 37 asserzioni PASS | `rc4-native.log`, `rc4-reference-file.json` |
| `face_feature` nativo | estrusione, raccordo, smusso, foro e caso con espressione PASS | `face-feature.log` |

I test automatici Core/backend/worker/EditMode totalizzano **4.431 PASS**.
I contatori dei runner includono polling e regressioni ripetute (M6 dentro
M8/M10): non sono 9.393 funzioni distinte e non chiudono i `NOT COVERED`.

## Corse nel simulatore

| Runner | Asserzioni | Verdetto nel proprio perimetro | Corsa finale |
|---|---:|---|---|
| M1 | 80 | PASS nei casi eseguiti | `suite02/m1-20261026-072158/` |
| M2 | 173 | PASS nei casi eseguiti | `suite02/m2-20261026-072314/` |
| M3 | 248 | PASS nei casi eseguiti | `suite03/m3-20261026-100045/` |
| M4 | 142 | PASS nei casi eseguiti | `suite02/m4-20261026-072510/` |
| M5 | 308 | PASS nei casi eseguiti | `suite04/m5-20261026-102118/` |
| M6 | 626 | PASS nei casi eseguiti | `suite03/m6-20261026-100450/` |
| M7 | 96 | PASS nei casi eseguiti | `suite02/m7-20261026-072825/` |
| M8 | 685 | PASS nei casi eseguiti | `suite03/m8-20261026-100605/` |
| M9 | 549 | PARTIAL | `suite02/m9-20261026-073029/` |
| M9N | 123 | PASS nei casi eseguiti | `suite02/m9n-20261026-073146/` |
| M9F | 202 | PASS nei casi eseguiti | `suite02/m9f-20261026-073249/` |
| M9H | 202 | PASS nei casi eseguiti | `suite02/m9h-20261026-073356/` |
| M10 | 1.746 | PASS nei casi eseguiti | `suite03/m10-20261026-100730/` |
| S1 | 4.213 | PARTIAL | `suite03/s1-20261026-100903/` |

Ogni corsa finale conserva `runner-result.json`, `result.json`, log, PNG,
manifest della fixture, ispezioni native prima/dopo e `native-cleanup.json`.
Tutte le fixture preparate, comprese quelle delle corse fallite, risultano
ripristinate. Le corse authoring hanno modifiche native attese e vengono
chiuse senza salvare; quelle di sola ispezione confrontano lo stato nativo.

Unity 6000.6.3f1, Meta XR Simulator 207.1 / OpenXR Standalone; Android conserva
Oculus. Input sintetici, una sola corsa per volta, Inventor 2027 con add-in
sperimentale pubblicato dal branch in una cartella QA nuova. Verificato il
percorso del modulo effettivamente caricato, non soltanto il manifest.
Host HTTPS di produzione su loopback, certificato e bearer temporanei con
pinning SHA-256; pairing persistente e runtime globale OpenXR invariati.

## Copertura Reality Capture

| Fase | Evidenza software | Limiti distinti |
|---|---|---|
| RC0 | M1: ingresso/uscita, sospensione CAD/input/voce, refresh; FakeAddIn arrestato realmente mentre Capture resta navigabile; EditMode senza pairing/documento | Nessuna prova fisica di passthrough o controller; riconnessione dell’host non esercitata dalla corsa Fake |
| RC1 | Storage RGB/pose/qualità sintetici, hash, riapertura, pausa/ripresa, esaurimento quota; contratti core e lifecycle Unity | Camera reale, permessi Android e sincronizzazione immagine/posa non verificati nel simulatore |
| RC2 | Worker Release esterno, RGB sintetico senza depth, ricostruzione sparsa, quote/CAS, isolamento token, revisioni, errori/cancellazione e artefatti conservati in `pc-final/synthetic-plane/` | Dataset fisico e MVS denso aperti; nessuna accuratezza metrologica certificata |
| RC3 | Viewer M1 indipendente, picking di ID originali, misure in mm, supporto debole, incertezza distinta da RMS; review/fitting/export HTTPS/MCP con worker reale | Il viewer M1 usa una fixture in memoria; il flusso UI completo dalla scansione al PC non è una singola prova end-to-end |
| RC4 | Base LH/RH e inverse M1/Core, copia CAD senza picking; sonda nativa plan/preview/rollback/commit, punti, cinque primitive, quote e provenienza, Undo/Redo, save/reopen | La sonda backend non esercita il pulsante UI di creazione riferimento; allineamento e overlay fisici restano aperti |

La parte sintetica RC4 è conservata nel workspace. Il suo hash coincide con
quello restituito dal salvataggio nativo (`rc4-reference-file.json`). Non
contiene B-Rep; punti/work geometry e quote note restano distinti dalle stime.
Nessun risultato sintetico viene promosso a VALIDATED.

## Difetti e correzioni

- RC4: gli attributi del documento con `CopyWithOwner=true` venivano rifiutati
  da Inventor; usato il set persistente con `false`, verificato anche dopo
  riapertura nativa.
- Preview dopo un refresh della scena: il ghost poteva sparire mentre Applica
  restava abilitato. Il piano viene ridisegnato soltanto se sopravvive ai
  controlli di documento/revisione; un errore di resa disabilita Applica.
- Editor nativo parte/lamiera: un refresh della stessa revisione cancellava
  il piano dopo la sua visualizzazione. Conservati operazione e piano per
  quel caso, con nuova resa effettiva; una revisione diversa continua a
  invalidarli. Test mirati e ripetizione S1 nativa passati.
- Lamiera: la chiusura del workspace inattivo cancellava la scoperta asincrona
  del sottotipo; riavviata la lettura senza attendere un nuovo evento CAD,
  mantenendo la guardia contro risposte di generazioni precedenti.
- Shutdown: cleanup di sezione/cursore dopo distruzione delle radici Unity;
  protetti gli oggetti già distrutti, con regressione e verifiche runtime.
- Runner: Home M1 aggiornata a TextMeshPro e al contesto Assieme; M10 verifica
  pagine/dimensioni del layout corrente e mantiene l’inventario congelato
  34 risorse/46 azioni separato. M5 completa la posa prima del puntamento e
  acquisisce l’invariante del modello dopo il preflight asincrono, subito
  prima del gesto. Il test continua a verificare che Grip muova solo lo sviluppo.
- Harness: runner disponibili nell’Editor soltanto con richiesta esplicita,
  credenziali della fixture e output; evidenze isolate, fixture S1 con testo
  e parametri, APK QA/ordinario separati, esclusioni lette anche dai log storici.

## Tentativi precedenti e limiti ancora aperti

`m1-01` conserva il contratto M1 non aggiornato e l’eccezione di shutdown.
`suite01` conserva M3 (ghost) e M5 (scoperta Lamiera); i figli fermati in coda
prima di preparare la fixture non sono fallimenti funzionali.
`suite02` conserva M5 (ray prima dello Snap), M10 (dimensione fissa del vecchio
layout), S1 (piano cancellato dal refresh) e un crash nativo di Unity in
chiusura dopo le asserzioni M3. Quella corsa M3 non vale come PASS completo:
exit `0xc0000005`, evento Windows in `unity-shutdown-events.json`.
La ripetizione M3 in `suite03` termina regolarmente; la causa del crash non è
isolata. Alcuni log riportano anche `JobTempAlloc`: nessun budget prestazionale
viene certificato da queste corse.
`suite03` conserva il confronto M5 con posa precedente all’attesa di rete;
`suite04` è la ripetizione M5 valida. Le corse fallite non sono cancellate né
sommano i loro contatori agli esiti finali.

Il test del ghost è stato riprodotto rosso in `preview-regression-before.xml`.
I ricontrolli intermedi conservano anche contratti reflection da aggiornare,
un vecchio test che invalidava qualunque refresh e un riferimento di fixture
errato; `native-preview-targeted.xml` passa i tre casi mirati e
`editmode-final04.xml` passa l’intera suite.

| Voce non coperta dal singolo runner | Esito complessivo |
|---|---|
| RC2 transfer / RC3 PC / RC4 native reference esclusi da M1 | Test HTTPS/worker e sonda RC4 separati; nessun PASS end-to-end UI implicito |
| M2/M6/M8 stale nativo | Rifiuto nativo esercitato da M3/M5; esclusioni del singolo runner conservate |
| M7 offline/parte | EditMode, non un run nativo di riconnessione su questo branch; la perdita host Capture è FakeAddIn |
| M9 annidati e `face_feature` | Varianti n/f/h e sonda nativa separata; non equivalgono al Quest hardware |
| M9 maniglia feature e highlight di tutte le facce | Lacune software storiche aperte; M9 rimane PARTIAL |
| Anello Inspect M6, assieme flessibile vincolato M9f/h, feature soppressa | Non chiusi da queste fixture; non classificati come soli limiti fisici |
| S1 | Sottoinsieme incrementale: altre opzioni/comandi, cambio Units, commit Boolean/Text nell’editor, stale/offline nativo e parità desktop/wireless aperti |
| M10 A/B/A | Campioni CPU/GC/frame PC conservati in `m10-acceptance-performance.json`; GPU assente, nessun budget Quest certificato |
| Hardware/metrologia | Camera, tracking/recenter, controller, microfono, overlay 1:1 sul reale, leggibilità/ergonomia e validazione dimensionale non eseguiti |

## Pacchetti Android

Ispezione ARM64/IL2CPP: **PASS**. Nessuna installazione o esecuzione hardware.

| Pacchetto | Byte | Nomi runner nei metadati | SHA-256 |
|---|---:|---:|---|
| qa | 95.720.472 | 15 | `262828E606C6C5D81BEDDF0C730A2C298B37A3AD48EE9A6FF660E98A0116629E` |
| ordinary | 57.984.852 | 0 | `B7224648561886C9FDEE479F87914747F3798C317F8D9A38C253BC049AC15AD8` |


L’ispezione cerca nomi esatti delimitati da null in
`assets/bin/Data/Managed/Metadata/global-metadata.dat` e la libreria
`lib/arm64-v8a/libil2cpp.so`, non semplici stringhe sparse nello ZIP.

## Ripristino e riproduzione

L’add-in QA è stato rimosso dal manifest attivo e l’installazione originale
riavviata; modulo originale verificato e target MCP riallineato. Manifest
ripristinato byte per byte, SHA-256 `807DAC44FCB8D333647D171482C016078885881E6C4AA4B6050A4C63CE7F98C9`.
Stato e hash della DLL QA in `addin-qa-state.json`. Nessun documento fixture
viene mantenuto aperto; la sola parte RC4 salvata resta come evidenza.
Ripristinati i soli font SDF, preloaded assets e impostazioni locali del debugger
Meta rigenerati durante i test/build. Conservata la configurazione OpenXR
Standalone necessaria al simulatore. La scena Main generata non è stata modificata.
Le modifiche locali preesistenti a `CLAUDE.md` e `.claude/` sono preservate.

Comandi e prerequisiti nel [README Unity](../Inventor%20XR%20SO/README.md#collaudo-desktop-con-meta-xr-simulator).
Usare cartelle nuove e una sola corsa Unity/fixture per volta. L’add-in per le
corse native deve corrispondere al codice collaudato ed essere sperimentale.
Per interrompere la coda creare `stop.request` e lasciare terminare la corsa
attiva con il proprio `finally`. Non usare documenti dell’utente come fixture.
