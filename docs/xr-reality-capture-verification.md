# Reality Capture — verifiche

Aggiornamento **26 ottobre 2026**, branch `codex/reality-capture`: eseguiti i
14 runner disponibili in Meta XR Simulator, con input sintetico e fixture
Inventor dedicate. Le corse finali selezionate totalizzano 9.393 asserzioni;
M9 e S1 conservano il verdetto PARTIAL. Suite Core/backend/worker/EditMode:
4.431 PASS. Riferimento RC4 nativo: 37 asserzioni PASS con preview/rollback,
commit, Undo/Redo e salvataggio/riapertura; corretto il set di attributi del
documento (`CopyWithOwner=false`). La perdita reale dell’host HTTPS FakeAddIn
lascia Capture navigabile, con CAD nascosto. Camera, tracking, metrologia e
overlay fisici restano aperti. Copertura, correzioni, crash Unity di una corsa
precedente, esclusioni software e ripristino sono distinti nel
[verbale del simulatore](xr-reality-capture-simulator-verification.md).
Le sezioni seguenti conservano gli esiti storici; non attribuiscono un PASS
Quest hardware alle nuove corse desktop.



Aggiornamento 26 ottobre 2026: nel [collaudo esteso](xr-simulator-full-collaudo.md)
M1 esegue i casi locali/review sintetici in Meta XR Simulator e QX verifica
offline/riconnessione e guardie Capture. La sonda RC4 su Inventor reale passa
37 asserzioni: preview/rollback, commit, cinque primitive, provenienza e quote,
Undo/Redo, salvataggio e riapertura. Evidenza: `artifacts/simulator-full/rc4-native-06.log`.
Corretto `CopyWithOwner=true` sugli attributi del documento, rifiutato nativamente
da Inventor 2027; il set persistente usa `false`. Restano esclusi acquisizione,
tracking, metrologia e overlay fisici; nessun PASS Quest hardware è implicito.

Data: 24 ottobre 2026. Perimetro iniziale: **RC0, sezione indipendente**; avanzamento RC1 e trasferimento RC2 riportato in fondo.

Riferimenti: [specifica funzionale](../Inventor%20XR%20SO/REALITY_CAPTURE_SPEC.md),
[design RC0](superpowers/specs/2026-10-24-reality-capture-independent-section-design.md),
[piano RC0–RC4](superpowers/plans/2026-10-24-reality-capture.md).

## Implementazione

- Ingresso dalla Home MR/VR e dalla schermata senza pairing, anche senza documento o connessione.
- Pannello autonomo, passthrough, ritorno alla Home mediante pulsante o menu.
- Soppressione persistente di tavolozza e barra CAD durante refresh e risposte asincrone; chiusura tastierini, strumenti, maniglie, ghost e legenda.
- Input CAD e voce sospesi, compreso il pannello vocale separato; puntatore UI disponibile.
- Ritorno al CAD con contesto attuale e richiesta di rilascio dei controlli prima di ricevere nuovi gesti CAD.
- Cambi di connessione e richiesta di pairing non interrompono la sezione.
- RC0 non acquisisce immagini e non produce mesh, point cloud, copertura o misure.

## Verifiche software

| Verifica | Esito |
|---|---|
| Compilazione Unity ordinaria | PASS nel run EditMode |
| Nuovi test di integrazione RC0 | 6 PASS |
| Suite Unity EditMode completa prima dell'ultima protezione sul rilascio input | 1125 PASS, 0 falliti |
| Ricontrollo finale mirato dopo protezione sul rilascio input e test barra asincrona | 120 PASS, 0 falliti |
| Compilazione C# del client e runner con `XR_SO_ACCEPTANCE` | PASS, 0 errori; un warning esistente per `FindObjectOfType` nella base del runner |
| `git diff --check` | PASS |

La compilazione del runner è stata eseguita con `dotnet build` sul progetto Unity generato, conservando i simboli del progetto e aggiungendo `XR_SO_ACCEPTANCE`. Non equivale a una build APK o a una prova sul visore.

I test RC0 compongono la UI reale attraverso `AppController.InitializeUi`, senza leggere credenziali e senza avviare connessioni. Verificano ingresso senza PC, strumenti nascosti, refresh del catalogo, cambio contesto CAD, stato offline, richiesta di pairing, rientro nel CAD, navigazione ripetuta e contenimento dei pulsanti nel canvas.

Evidenze conservate in `artifacts/reality-capture/2026-10-24-rc0/`: XML e log del run completo (`xrso-rc0-all-*`), XML e log del ricontrollo finale (`xrso-rc0-final-*`), log compilazione QA e manifest con hash dei sorgenti verificati. La cartella degli artefatti è esclusa da Git.

## Runner sintetico sul Quest

I controlli RC0-01…05 sono integrati nel runner M1 (`M1QuestAcceptance.CheckRealityCaptureSection`) e utilizzano la sua **fixture dedicata M1**. Il percorso riusa Home→Reality Capture→Home→CAD, verifica la sospensione della voce e degli strumenti, sollecita refresh del catalogo e conserva uno screenshot `m1-acceptance-reality-capture.png`.

I callback UI sono **sintetici**. Il caso senza PC e i guasti di connessione sono coperti dai test EditMode, non dal run M1 online. Il runner conserva questa distinzione nei messaggi PASS. Acquisizione e prova fisica sono registrate come NOT COVERED.

**Run Quest RC0: NON ESEGUITO.** Nessun gate viene chiuso sul dispositivo sulla base della sola compilazione. Per eseguirlo occorrono APK QA aggiornato, fixture M1 e il normale wrapper con ripristino di documento e APK ordinario; nessun documento dell'utente va usato come fixture.

## Prova fisica

**APERTA / NON ESEGUITA**: puntamento e trigger, pulsante menu fisico, leggibilità e postura, passthrough reale, tracking e rientro nel CAD. Nessun APK installato sul Quest da questo intervento.

## Pipeline di scansione

Lo stato iniziale RC0 non certifica scansioni o accuratezza dimensionale. L’avanzamento seguente aggiorna il perimetro software senza chiudere i gate fisici.

## Avanzamento RC1 e trasferimento RC2

Questa sezione registra la consegna precedente di acquisizione e trasferimento. La pipeline PC aggiunta successivamente è descritta nella sezione **RC2 PC — ricostruzione sparsa e controllo MCP** in fondo al verbale.

### API camera e convenzioni

Il progetto aveva soltanto Meta XR Core 207.0.0 e il percorso QR basato su `WebCamTexture`. È stato aggiunto **MRUK 207.0.0**, dalla stessa registry Meta e senza cambiare la versione del Core. Sono stati letti i sorgenti effettivi di `PassthroughCameraAccess`: `Timestamp`, `Intrinsics.LensOffset`, `GetCameraPose`, proiezione/crop e ordine dell’aggiornamento della texture. Il codice installato restituisce la posa nel tracking space: il contratto registra esplicitamente la trasformazione capture→tracking. Il componente non applica la trasformazione del rig Unity.

Riferimenti primari: [guida Meta PCA](https://developers.meta.com/vr/documentation/unity/unity-pca-documentation/), [contratto della camera](https://developers.meta.com/vr/reference/mruk/v83/class_meta_x_r_passthrough_camera_access/); per firme e comportamento fa fede il sorgente **207.0.0 installato**.

- Camera RGB sinistra, richiesta 1280×960 e 15 fps; lettura CPU limitata a un tentativo ogni 0,5 s, senza bloccare il render thread.
- Timestamp UTC in microsecondi, intrinseci della risoluzione effettiva, posa camera e offset camera→testa catturati prima della richiesta di readback; posa testa ricavata dagli stessi dati, senza usare la posa corrente dopo l’attesa.
- Immagine RGB8 con origine in alto a sinistra. Pose Unity con X destra, Y alto, Z avanti; matrici row-major per vettori colonna, traslazioni in mm. Nessun GLB viene prodotto in questa fase.
- Token di generazione scartano callback GPU tardivi dopo stop/pausa/uscita; tracking perso e cambio origine interrompono la sessione. Il riavvio non riutilizza il tracking della sessione precedente.
- La sincronizzazione effettiva di immagine e posa del runtime Meta, l’orientamento dei pixel e la disponibilità dei metadata devono ancora essere verificati sul dispositivo: la lettura del sorgente e la compilazione non costituiscono questa prova.

### Persistenza, UI e trasferimento

Il volume azzurro è una delimitazione esplicita dell’operatore, non geometria rilevata. La camera parte soltanto con **Avvia acquisizione** dopo la conferma del volume. La UI mostra frame salvati/scartati e motivi osservabili di rifiuto; non mostra percentuali di copertura, superfici ricostruite o precisioni dimensionali.

I raw risiedono in `Application.persistentDataPath/RealityCapture/v1/`: metadata sessione immutabili, directory frame pubblicate per rename solo dopo immagine e metadata completi, hash delle immagini e journal append-only degli stati. Quota complessiva locale 2 GiB, riserva libera 256 MiB, massimo 5000 frame per sessione. Un’interruzione conserva i frame già pubblicati. La riapertura verifica i raw; il recupero dopo crash marca la sessione interrotta e permette il trasferimento, senza riprendere acquisizioni in un’origine non più valida.

Il trasferimento è esplicito verso il PC associato, con lo stesso certificato pinnato e bearer token. La chiusura della sezione cancella l’upload; una nuova richiesta riprende dai file mancanti. Le immagini viaggiano come binario, mai come JSON/base64.

Il backend espone il trasferimento soltanto con `--enable-experimental`:

```text
POST   /capture/v1/sessions
GET    /capture/v1/sessions/{id}
PUT    /capture/v1/sessions/{id}/chunks/{sequence}
POST   /capture/v1/sessions/{id}/finalize
DELETE /capture/v1/sessions/{id}
```

Ogni chunk corrisponde a un file del manifest, massimo 4 MiB. Ownership per SHA-256 del token, quote riservate prima dell’upload (2 GiB e 16 sessioni per token, 8 GiB e 128 sessioni complessive), pubblicazione dopo verifica di dimensione e hash, replay idempotente, finalizzazione immutabile. Retention sette giorni dalla creazione, pulizia anche senza richieste ogni 15 minuti. Root PC configurabile con `INVENTOR_SO_CAPTURE_ROOT`, altrimenti `%LOCALAPPDATA%/InventorSO/RealityCapture/v1`. Nessun dato va al cloud e nessun documento Inventor viene modificato.

### Stato dei gate

| Gate | Software | Quest sintetico | Prova fisica |
|---|---|---|---|
| Contratti, unità, validazione trasformazioni | Test core | Non eseguito | Non applicabile per la sola algebra |
| Raw, hash, pausa/ripresa, quota e recupero | Test core e sandbox runner implementata | Non eseguito | Storage e interruzione reale aperti |
| Oggetto/Area, delimitazione, navigazione | EditMode | Runner implementato, non eseguito | Aperta |
| Camera, permessi, posa/frame, tracking | Codice e contratto SDK ispezionati | Non simulati come prova camera | **Aperta** |
| Upload, ripresa e isolamento token | Test HTTPS reale con sorgenti sintetiche | Non eseguito | Collegamento dal Quest aperto |
| Ricostruzione e solver RC2 | **Non implementati** | Non eseguito | Aperta |
| Revisione/primitive RC3 e CAD RC4 | **Non implementati** | Non eseguito | Aperta |

Il runner M1 usa la propria fixture CAD per la regressione RC0 e un sandbox temporaneo dedicato per i raw RC1 sintetici, con cleanup. Non apre camere né trasmette immagini. I log distinguono `PASS RC1-raw-synthetic` / `PASS RC1-lifecycle-synthetic` da `NOT COVERED RC1-camera`, `RC2-transfer`, `RC2-RC4-reconstruction` e dalla prova fisica.

L’intero piano RC0–RC4 resta **incompleto**. Non è stato eseguito alcun test, installazione APK o acquisizione sul Quest durante questo intervento, su richiesta dell’utente di completare prima lo sviluppo.

### Risultati software conservati

| Verifica | Esito |
|---|---|
| Suite core .NET finale | **1314 PASS**, 0 falliti |
| Sottocasi Reality Capture nel core | **25 PASS**: 22 core/persistenza e 3 end-to-end HTTPS |
| Suite backend .NET | **1918 PASS**, 0 falliti |
| Storage trasferimento backend | **10 PASS**, inclusi restart, ripresa, hash, cancellazione, retention, quote e isolamento token |
| Suite Unity EditMode dopo integrazione iniziale | **1128 PASS**, 0 falliti |
| Ricontrollo Unity finale dopo hardening di lifecycle e UI | **98 PASS**, 0 falliti: RealityCaptureWorkspace, UiShell, XrInput, QuestAcceptanceContract |
| Compilazione C# runner con `XR_SO_ACCEPTANCE` | **PASS**, 0 errori; warning preesistente `FindObjectOfType` e warning sul campo camera assegnato soltanto nel ramo Android, escluso dall’Editor |
| APK ordinario finale Android ARM64 / IL2CPP | **PASS**, `Build Finished, Result: Success`, exit 0 |
| `git diff --check` | **PASS** |
| Installazione/run Quest e prova fisica | **NON ESEGUITI** |

Evidenze in `artifacts/reality-capture/2026-10-24-rc1-transfer/` (escluse da Git): XML/log Unity completo e finale, TRX della suite core finale, log del compilatore QA, log build Android, APK ordinario e manifest degli hash dei sorgenti e dell’APK. La compilazione QA non equivale alla produzione di un APK QA né a un run sul dispositivo. La cache del font dinamico modificata automaticamente dall’Editor durante i test è stata ripristinata, senza modificare la scena generata.

## RC2 PC — ricostruzione sparsa e controllo MCP

Avanzamento del 24 ottobre 2026, senza eseguire run o installazioni sul Quest. [Design e contratti RC2 PC](superpowers/specs/2026-10-24-reality-capture-rc2-pc-design.md).

### Software presente

- Libreria di contratti e qualità, worker .NET 8 separato con OpenCvSharp4 4.13.0.20260627, matching RGB reciproco, triangolazione inizializzata dalle pose e BA congiunto di camere/punti, intrinseci fissati e vincoli metrici pesati.
- Marker ArUco e barra con riferimenti a corner misurati; distanza tra due punti e diametro circolare tra tre punti. Quote di validazione escluse dal solver, conflitti e riferimenti irrisolti espliciti.
- Point cloud, superficie sparsa, GLB/PLY/OBJ, report, lista punti, camere ottimizzate e trasformazioni/unità esplicite. La rilettura del GLB è verificata usando il parser del core XR.
- Undici controlli MCP sperimentali, ownership sul token originale, versioni dei vincoli con controllo `expected_version`, revisioni di solve immutabili, annullamento, recupero di job interrotti e pubblicazione soltanto dopo verifica del marker finale e dell’allowlist degli artefatti.
- Worker unico per host con deadline, watchdog di memoria e output, heap limitato, limite dei frame/punti e delle revisioni; lease sui raw durante elaborazione e lettura, pulizia della copia temporanea prima della pubblicazione e mantenimento delle revisioni precedenti in caso di errore.
- Script di build standalone e batteria PC riproducibile; il pacchetto completo `-Experimental` include il worker accanto al server HTTP. Nessun componente è stato installato o riavviato sul PC operativo da questo intervento.

### Evidenza software

La batteria `scripts/test-reality-capture-pc.ps1` è stata eseguita con il **worker Release pubblicato**, avviato dal server come processo reale, e un host HTTP/MCP locale senza target Inventor. Il dataset è sintetico: piano texturizzato a Z=2000 mm, tre camere con pose note, immagini RGB senza depth. Un caso aggiunge tre marker e una barra con 21 distanze derivate dai corner misurati. Le prove matematiche includono geometria non planare e un vincolo di validazione intenzionalmente incompatibile che non modifica il solve.

| Verifica | Esito |
|---|---|
| Nuova suite `Capture.Worker.Tests` | **13 PASS**, 0 falliti |
| Suite backend completa | **1919 PASS**, 0 falliti |
| Suite core XR completa | **1314 PASS**, 0 falliti |
| Pubblicazione worker Release win-x64 | **PASS** |
| Raw RGB senza depth, scala e GLB riletto dal core | **PASS sintetico** |
| Quote incompatibili, diametro, misura non finita/degenerata | **PASS software** |
| Marker ArUco e distanze ridondanti della barra | **PASS sintetico** |
| Distinzione calibrazione/validazione e riutilizzo di misure | **PASS software** |
| MCP/HTTP → worker reale → artefatti, due revisioni congelate | **PASS sintetico** |
| Token diverso, modifica concorrente dei vincoli, percorsi artefatto estranei | **PASS software** |
| Errore effettivo del worker, deadline, annullamento e ripartenza host | **PASS software** |
| Secondo host sulla stessa radice | **Rifiutato**, senza recuperare o cancellare job dell’host proprietario |
| Dataset fisico con dimensioni indipendenti | **NON ESEGUITO** |
| Run sintetico Quest e prova fisica | **NON ESEGUITI** |

Durante i test è emersa una lettura intermittente dello stato queued dopo la conclusione del job. È stato introdotto lo stato concorrente in memoria, aggiornato insieme alla persistenza atomica; le verifiche controllano anche lo stato terminale su disco. Le copie raw temporanee vengono rimosse prima di pubblicare un successo; un errore di persistenza rimane esplicito, senza promuovere output parziale.

Evidenze finali conservate in `artifacts/reality-capture/2026-10-24-rc2-pc-delivery/`: TRX/log delle tre suite, log e manifest della pubblicazione del worker, hash dei sorgenti, input e output del piano sintetico, richiesta di replay e dichiarazione esplicita di evidenza sintetica. `synthetic-plane/replay-request.json` permette di rieseguire lo stesso caso verso una nuova cartella output. Le precedenti batterie sono conservate nelle cartelle `rc2-pc-final` e `rc2-pc-verified` della stessa data. Nessun file dell’utente è stato impiegato come scansione o fixture.

### Gate ancora aperti

RC2 non è chiuso integralmente: restano ricostruzione densa MVS, calibrazione dei prior del tracking, dataset fisico controllato e valutazione di superfici/ripetibilità reali. La percentuale di copertura geometrica rimane non disponibile. I residui sintetici non stabiliscono un errore dimensionale sul mondo reale.

Il normale percorso MCP usa evidenza `unverified` e non promuove i risultati a `VALIDATED`. Per una validazione fisica occorrono misure indipendenti con ID distinti, non usate per la calibrazione, e il relativo verbale di banco. Le distanze dello stesso artefatto sono correlate e non contano come tre controlli indipendenti.

Alla consegna RC2 l’integrazione del solve nella UI Unity era rinviata alla prova su dataset fisico. La successiva richiesta dell’utente di sviluppare RC3 prima dei test sul Quest autorizza l’avanzamento software descritto sotto; non chiude i gate fisici. Lo stato del Quest non è stato modificato nella fase RC2 PC.

## RC3 — viewer, fitting e revisione

Sviluppo del 24 ottobre 2026 su richiesta «RC3», mantenendo il vincolo «prima
tutto il codice, poi i test sul visore». [Design, limiti e gate](superpowers/specs/2026-10-24-reality-capture-rc3-review-design.md).

### Implementazione

- `Revisioni PC` collega le sessioni locali al PC associato: elenco, nuovo
  solve, stato/annullamento e apertura delle revisioni riuscite. Client HTTPS
  con pinning, download limitati, verifica SHA-256 e URL costruiti localmente.
- Viewer Unity con mesh GLB e punti propri, senza corpi/collider CAD. Cambio
  metri/glTF → coordinate Unity; la scala di visualizzazione non modifica le
  misure in mm. Selezione per ID originali, regione da 100 mm, riferimenti e
  punti osservati in sole due immagini. Quest’ultimo indicatore non è copertura.
- Fitting manuali di punto, asse, piano, cerchio e cilindro. Test su geometria
  obliqua, degenerazioni, archi insufficienti e cilindri con scarso supporto
  assiale. I fitting non segmentano automaticamente regioni miste. RMS del
  supporto esplicito; incertezza della misura non stimata, mai sostituita dall’RMS.
- Misure punto-punto, punto-piano, separazione all’origine tra piani con
  normali entro 1°, angolo acuto, raggio/diametro ed estensione assiale osservata.
  Quote note, incertezza esterna, residui assoluti/relativi e RMS/P95 del solve
  sono distinti. Il controllo indipendente resta una prova fisica separata.
- Annotazioni con CAS e supporti legati alla revisione, tre nuovi tool
  sperimentali e persistenza separata dagli otto artefatti RC2. Modifica,
  disattivazione/rimozione delle quote correnti e nuovo solve conservano i raw
  e le revisioni precedenti. Le revisioni RC2 esistenti restano leggibili.
- Export GLB in metri, PLY/OBJ in mm, con unità, report, vincoli congelati,
  annotazioni e manifest di hash. Pubblicazione della cartella dopo tutte le
  scritture; export precedenti conservati. DTO preservati per il linker IL2CPP.
- Home, chiusura e sospensione annullano la revisione UI e rimuovono la
  geometria; risposte tardive di successo/errore non riaprono schermate.
  Il picking usa le stesse guardie nel percorso fisico e nei test sintetici:
  tracking, Trigger, Grip, hit UI, stato della sezione e richiesta in corso.

### Evidenza software

| Verifica | Esito |
|---|---|
| Worker Release pubblicato, suite worker e integrazione HTTPS/MCP | **13 PASS** |
| Backend completo, tier sperimentale e contratti dei 14 tool capture | **1919 PASS** |
| Core XR completo, inclusi fitting, download ed export | **1333 PASS** |
| Unity EditMode completo | **1134 PASS** |
| Unity mirati finali, inclusi sospensione, risposta tardiva di errore e click-through | **16 PASS** |
| Core mirati fitting/download/export | **18 PASS** |
| Compilazione C# con `XR_SO_ACCEPTANCE` | **PASS**, nessun run del runner |
| APK Android ordinario ARM64/IL2CPP | **PASS**, 57.083.832 byte; ABI verificata `arm64-v8a` |
| Quest sintetico, prova fisica e dataset di oggetti reali | **NON ESEGUITI** |

La suite Unity completa precede gli ultimi aggiustamenti di lifecycle e
routing, poi coperti dai 16 casi mirati. Il core completo comprende i quattro
casi di scrittura export e i relativi hash; un ulteriore caso controlla che i
vincoli derivati dai marker possano superare i 256 vincoli manuali, mantenendo
un limite esplicito sui riferimenti delle annotazioni. I test di integrazione usano un
host HTTPS reale, un worker locale Release e immagini interamente sintetiche:
apertura nel client, fitting su punti ricostruiti, CAS, token estraneo, nuova
revisione, immutabilità dei vincoli precedenti ed export dei tre formati.

Evidenza in `artifacts/reality-capture/2026-10-24-rc3-software/`: log/TRX,
XML/log Unity, worker pubblicato, dataset sintetico riproducibile, compilazione
QA, APK ordinario, `source-manifest.json` e `delivery.json`. SHA-256 dell’APK:
`022ecc548fd4949fe88d9fe8c20cb8646903d8436a38a24bdcd73a460753b9de`.
È stata ripristinata soltanto la cache dinamica del font modificata automaticamente
dai test; nessuna scena generata è stata modificata. Il runner M1 include la fixture RC3 in memoria: scala/assi, picking sintetico
con blocco UI, selezione debole, RMS distinto da incertezza e cleanup; dichiara
esplicitamente i casi non coperti. Non esercita acquisizione RGB o solve sul
dispositivo. Il codice fixture è escluso dall’APK ordinario.

### Gate ancora aperti

RC3 è una consegna software sperimentale. Restano **aperti** run Quest,
leggibilità/performance, controller/tracking fisici, superfici opache,
riflettenti, poco texturizzate, occlusioni, errori assoluti/relativi e RMS/P95
contro misure indipendenti e ripetibilità. Nessun risultato di questa batteria
costituisce accuratezza dimensionale reale o autorizza `VALIDATED`.

RC2 mantiene aperti MVS denso, calibrazione dei prior e dataset fisico. Alla
consegna RC3, RC4 era la fase successiva; l’avanzamento software è riportato sotto. Non
sono stati eseguiti installazione APK, comandi ADB o cambiamenti sul Quest.

## RC4 — allineamento, parte di riferimento e overlay

Data: 25 ottobre 2026. Richiesta dell’utente: procedere con RC4 e continuare lo
sviluppo prima del collaudo sul visore. [Design e gate](superpowers/specs/2026-10-25-reality-capture-rc4-inventor-design.md).

### Implementazione software

- Registrazione a tre punti oppure piano/asse/origine, senza adattamento di
  scala, con cambio esplicito Scan LH → Inventor RH. Matrice, coordinate di
  controllo, residui e identità/revisione CAD salvati in snapshot con CAS.
  Quote note e annotazioni del solve precedente restano congelate.
- Tre tool capture aggiunti, per un totale di 17: lettura allineamento,
  registrazione e preparazione delle operazioni da dati di proprietà del token.
  Il payload include hash dei punti, unità e report; le quote esterne provengono
  dalla richiesta immutabile del solve, anche con sorgenti personalizzate come
  `caliper`. Non vengono confuse con i vincoli derivati dai marker.
- Nuova parte nel workspace tramite il ciclo esistente. Comando nativo
  sperimentale `create_reality_reference`, ammesso solo in una transazione
  atomica posseduta e in una parte workspace vuota. Point cloud 3D nativa,
  work point/assi/piani, parametri stimati e noti distinti, BOM Reference e
  provenienza persistente. Conversione mm→cm alla frontiera Inventor.
- Preview nativa eseguita/annullata e verificata dal client; applicazione
  esplicita, piano consumato una volta e nessun retry del commit incerto.
  In caso di risposta persa la UI dichiara che il comando potrebbe essere
  stato eseguito e richiede di controllare Inventor prima di una nuova importazione.
  Salvataggio separato. Annullare lascia la parte vuota già creata: non viene
  cancellato automaticamente un documento. Nessun documento dell’utente è
  stato aperto, salvato, chiuso o modificato durante queste verifiche software.
- Confronto CAD ridotto e overlay 1:1 da snapshot completi, con copia grafica
  priva di collider e `CadBody`. Mesh condivise tra occorrenze ripetute, limite
  di un milione di vertici unici e 2000 occorrenze. Nessuna modifica della scena
  CAD operativa o della scena Unity generata.
- Tre riferimenti fisici nuovi obbligatori per l’overlay; mai riuso automatico
  di vecchie pose raw. Perdita tracking/recenter invalidano anche registrazioni
  non visualizzate. Cambio PC, documento/revisione/visual revision, Home e pausa
  rimuovono la vista o la registrazione. L’origine controller non è una punta
  calibrata: offset, accuratezza e usabilità sono ancora da collaudare.

La parte contiene punti e work geometry, **non una mesh nativa né una B-Rep**.
GLB/PLY/OBJ restano artefatti separati. La preview utilizza rollback nativo e
prove strutturate; non dichiara una nuova superficie B-Rep quando i dati sono
soltanto punti. Nessun parametro stimato viene promosso a misura certificata.

### Risultati verificati

| Verifica | Esito |
|---|---|
| Suite worker/host capture finale | **16 PASS**, inclusi allineamenti tre punti/piano-asse, CAS, token, snapshot precedenti e quote esterne |
| Suite backend completa | **1926 PASS** |
| Suite core XR completa | **1337 PASS** |
| Unity EditMode completo | **1145 PASS** |
| Unity mirati finali | **25 PASS**, inclusi mesh condivise, errore preview, cambio documento/PC, commit incerto e tracking |
| Add-in ordinario e sperimentale contro interop Inventor 2027 | **PASS**, zero warning/error nelle build registrate |
| Compilazione C# runner con `XR_SO_ACCEPTANCE` | **PASS**, non eseguito |
| APK ordinario Android ARM64/IL2CPP | **PASS**, 57.132.248 byte; ABI `arm64-v8a` verificata |
| Installazione add-in/APK, run Quest, prova fisica | **NON ESEGUITI** |
| Esecuzione nativa Inventor su fixture, save/reopen e Undo | **NON ESEGUITA**, rimane un gate distinto |

Le prove di transazione usano un backend simulato: verificano rollback di
modifiche parziali, ripristino revisione e rifiuto della destinazione estranea
o obsoleta. I test UI usano il client MCP reale con risposte simulate per
preview/commit/save; il test di ownership/allineamento passa anche dal server
HTTPS reale, con worker Release e raw sintetici. Queste evidenze non sono
esecuzione COM nativa. Il controllo completo Unity precede gli ultimi aggiustamenti
di riuso delle mesh e messaggio del commit incerto, poi verificati nella batteria mirata finale.

Il runner M1 è predisposto per registrazione matematica, inversione su punto
fuori piano e copia grafica della sua fixture CAD dedicata. Dichiara
`NOT COVERED` per importazione nativa e overlay fisico; non viene presentato
come `PASS COMPLETE` di RC4 e non è stato eseguito sul Quest.

Evidenze finali: `artifacts/reality-capture/2026-10-25-rc4-delivery/`, con
worker pubblicato, dataset sintetico, TRX/log, XML Unity, log build, add-in
sperimentale compilato (non installato), APK ordinario e manifest di hash.
SHA-256 APK: `7b39c20744f66ae42ef771b82212cba817b7d182ad700b2524728ca1449ada23`.
Le prove preliminari restano in `2026-10-25-rc4-software/`. La sola cache
dinamica del font modificata dai test è stata ripristinata.

### Gate ancora aperti

RC4 è implementata a livello software **sperimentale**. Restano collaudo nativo
della parte di riferimento (geometria, attributi, save/reopen, Undo e rollback
reale), run sintetico Quest, posizionamento fisico del controller, perdita
tracking/recenter, leggibilità/performance e qualità dell’overlay su oggetti
reali. Non è stato distribuito alcun add-in o APK. Rimangono inoltre i gate
fisici RC1–RC3, MVS denso e dataset controllato RC2. Il piano completo non è
una certificazione metrologica e nessuna misura è stata promossa a VALIDATED.
