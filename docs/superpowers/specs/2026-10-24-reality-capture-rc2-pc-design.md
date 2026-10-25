# Reality Capture — RC2 sul PC

Data: 24 ottobre 2026. Stato: implementazione sperimentale della pipeline **sparsa**, verificata su dati sintetici. Dataset fisico controllato e ricostruzione densa aperti.

Riferimenti: [piano](../plans/2026-10-24-reality-capture.md), [specifica funzionale](../../../Inventor%20XR%20SO/REALITY_CAPTURE_SPEC.md), [verbale](../../xr-reality-capture-verification.md).

## Perimetro

Il PC riceve sessioni RC1 finalizzate e conserva i raw immutabili. MCP controlla sessioni, versioni di vincoli/artefatti e revisioni di solve. Un processo locale .NET 8 separato esegue ricostruzione RGB e ottimizzazione, senza Inventor, depth o accesso cloud. Il client Unity non viene ancora integrato con i nuovi comandi di solve: il piano richiede prima la prova della pipeline su un dataset fisico controllato.

La libreria `bridge/src/reality-capture` contiene contratti e qualità; `capture-worker` contiene OpenCV e il solver. Il processo HTTP non carica librerie native OpenCV. La registrazione MCP è limitata a host HTTP sperimentale, toolset `reality_capture`, escluso da `--read-only`.

## Pipeline

1. Copia locale sotto lease della sessione finalizzata, controllo di hash, sequenza, schema, formato RGB8, intrinseci e trasformazioni rigide.
2. Selezione temporale uniforme di massimo 60 frame; i raw originali restano conservati. Massimo 1500 feature SIFT per immagine e 2000 punti del solve.
3. Matching reciproco con ratio test, coppie vicine e coppie distanti di richiusura; triangolazione inizializzata dalle pose Quest, controlli di parallasse, cheiralità, riproiezione e volume selezionato.
4. Rilevamento marker ArUco `DICT_4X4_50`. I quattro corner sono riferimenti persistenti `marker:<artifact>:<id>:<corner>`. Le coordinate realmente misurate generano distanze interne al marker e distanze tra il corner 0 dei marker, incluse A–B/B–C/A–C della barra. Non si utilizza una dimensione nominale di stampa.
5. Bundle adjustment congiunto di pose camera e punti: intrinseci fissati, prior delle pose, residui di riproiezione e vincoli metrici. LM con equazioni normali sparse risolte tramite gradiente coniugato precondizionato; nessuna matrice densa proporzionale al numero totale di punti.
6. Report dei residui di calibrazione e delle quote indipendenti di validazione. I residui di validazione non entrano nell’obiettivo del solver.
7. Esportazione point cloud, triangoli osservati nelle immagini, GLB, PLY, OBJ e metadata. La superficie è sparsa, con possibili fori e zone deboli; non è stata implementata la ricostruzione densa MVS né la percentuale di copertura geometrica.

I prior attuali (20 mm / 0,03 rad, primo frame fortemente ancorato) sono euristiche dichiarate; non rappresentano covarianze calibrate del sensore. Il supporto alle coppie distanti non equivale a una certificazione di loop closure o di assenza di drift.

## Riferimenti metrici e qualità

Il vincolo `distance` usa due ID di punto; `diameter` usa tre punti non collineari di un bordo circolare e misura il circumdiametro. Cilindri, fitting di piani e diametri di tubi restano nel lavoro semantico RC3.

Ogni vincolo dichiara `Id`, `Type`, `PointIds`, `ValueMm`, `UncertaintyMm`, `Role`, `Enabled`, `Independent`, `Source` e, per la validazione indipendente, `MeasurementId`. La lista punti è disponibile come artefatto `points.json`; il controllo MCP restituisce URL autenticati, non la point cloud nel payload.

I conflitti oltre tre incertezze e i riferimenti irrisolti sono espliciti. Le misure disabilitate sono conservate nel report. Correggere una misura o aumentarne l’incertezza produce una nuova versione di vincoli, senza cambiare i solve già generati.

`VALIDATED` richiede evidenza fisica dichiarata, almeno tre ID di misure indipendenti distinti, non già usati per calibrare, e geometrie di validazione non riutilizzate come calibrazione. I dati sintetici non vengono promossi a `VALIDATED`. Il normale host genera richieste con `EvidenceKind=unverified`: una nuova misura non chiude automaticamente la prova fisica. Le distanze derivate dallo stesso artefatto condividono lo stesso ID di misura e non si contano come controlli indipendenti separati.

## Coordinate e artefatti

Il calcolo interno usa metri, assi CV destri con Y verso il basso; l’importazione converte esplicitamente entrambe le basi camera/mondo delle pose Unity. Punti PLY/OBJ sono in mm, nello spazio scan metric con assi Unity; GLB in metri con assi glTF destri Y-up. Il winding viene adeguato alle trasformazioni.

`units.json` conserva convenzione matriciale row-major/vettori colonna, scan metric→capture, capture→tracking e scan metric mm→glTF metri. La pipeline non cambia il sistema di riferimento del documento Inventor.

Artefatti pubblicati: `pointcloud.ply`, `scan.obj`, `scan.glb`, `points.json`, `quality_report.json`, `constraints.json`, `optimized_cameras.json`, `units.json`. Il marker `result.json` viene scritto per ultimo; il server valida identità, revisione, allowlist e dimensioni prima della pubblicazione.

## Isolamento, revisioni e limiti

La radice rimane della sessione del token originale. Un lease impedisce cancellazione/retention durante solve o lettura; i raw copiati per il worker vengono rimossi alla fine. Le revisioni conservano richiesta e vincoli congelati. Il riavvio dell’host marca come falliti i job rimasti queued/running, senza eseguire di nuovo implicitamente il solve.

L’host detiene un lock esclusivo della radice per tutta la propria vita: un secondo host non può recuperarne gli stessi job. Con un `AssetDirectory` personalizzato, la radice predefinita è `RealityCapture/v1` accanto alla cartella asset; la configurazione ordinaria mantiene `%LOCALAPPDATA%/InventorSO/RealityCapture/v1`. `INVENTOR_SO_CAPTURE_ROOT` prevale su entrambi i percorsi. Questo mantiene i test e gli host con dati isolati separati dalle scansioni operative.

Un solo worker per host, quattro revisioni per sessione; timeout processo cinque minuti, watchdog RSS 1 GiB, heap gestito massimo 512 MiB, output massimo 32 MiB/revisione. Output riservato: 256 MiB/token e 1 GiB complessivo; snapshot dei vincoli massimo 1 MiB ciascuno, 4 MiB/sessione e 256 versioni. I watchdog non costituiscono un sandbox del sistema operativo: l’eseguibile locale è scelto dall’amministratore, mai dal contenuto di una richiesta.

Il processo usa argomenti strutturati senza shell, output diagnostico drenato e limitato, codici di errore sanitizzati. Crash, deadline, annullamento o output incompleto non pubblicano artefatti né alterano i raw o le revisioni precedenti. Non vengono eseguite operazioni CAD.

## Esecuzione e verifica

```powershell
# Pacchetto standalone del worker; non installa o riavvia applicazioni.
& scripts/build-capture-worker.ps1

# Suite PC, worker Release pubblicato, dati e artefatti sintetici conservati.
& scripts/test-reality-capture-pc.ps1
```

Il pacchetto completo `build-inventor-so.ps1 -Experimental` include il worker sotto `server-http/capture-worker/`. In alternativa impostare `INVENTOR_SO_CAPTURE_WORKER` al percorso assoluto del DLL pubblicato prima di avviare l’host con `--enable-experimental`. Se manca il worker il controllo risponde `CAPTURE_WORKER_UNAVAILABLE`; non viene creata una scansione fittizia.

Gli endpoint binari aggiunti sono `GET /capture/v1/sessions/{id}/solutions/{revision}`, `/quality` e `/assets/{asset}`. Stessa autenticazione, ownership e rate limit del trasferimento. Il client deve passare `expected_version` ricevuto da `inventor_reality_capture_list_constraints` quando modifica i vincoli o avvia un solve.

La verifica software copre RGB sintetico senza depth, scala, BA, conflitti, marker/barra, validazione riservata, rilettura GLB dal core XR, flusso MCP/HTTP con worker reale, revisioni, token diverso, errore del processo, timeout, annullamento e recupero dopo riavvio. Il dataset fisico controllato e le condizioni industriali della specifica restano gate aperti.
