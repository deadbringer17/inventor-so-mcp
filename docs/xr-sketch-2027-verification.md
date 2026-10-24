# S1 — Schizzo XR: implementazione e verifica

Aggiornamento: 24 ottobre 2026, Europe/Rome.
Spec: [S1](superpowers/specs/2026-10-06-schizzo-parita-inventor-2027-design.md).
Piano: [fasi F0–F7](superpowers/plans/2026-10-06-schizzo-parita-inventor-2027.md).

**Implementazione parziale in corso. Nessuna dichiarazione di parità completa.**
L'[audit PC dei 77 comandi](s1-pc-command-status95.md) conta 54 implementazioni
parziali e 23 assenti; questi conteggi non sono percentuali di completamento.
La [chiusura delle spline 96](s1-spline-closure96.md) ha superato la matrice
nativa 64 casi/14 rifiuti e la verifica offline con otto alterazioni rifiutate.
Il [lifecycle delle copie curve 95](s1-copy-core-lifecycle95.md) r3 passa
nel producer, ma il verificatore rileva osservazioni mancanti dal report
del secondo aggiornamento. Corretta la registrazione nella sonda; nuova
matrice completa da eseguire, nessun PASS offline r3 attribuito.
La [modifica dei punti spline 98](s1-spline-point-edit98.md) è implementata nel
plugin sperimentale; prova nativa r2 terminata incompleta: 98 casi passati,
arresto al caso 99 per UnauthorizedAccessException, rifiuti non eseguiti.
Cleanup e ripristino dell'installazione ordinaria verificati. Il precedente
r1 si fermava sul confronto richiesta/punto risolto di una spline approssimante.
La [ricerca tensione 97](s1-spline-tension97.md) completa 60 osservazioni,
60 Abort esatti e sette alterazioni rifiutate dal verificatore offline.
La [modifica tensione 99](s1-spline-tension-edit99.md) è implementata nel plugin:
38 test specifici e 1.908 bridge passati, build riuscite; percorso prodotto
ancora da collaudare in Inventor.

La tavolozza ampliata e le basi dell'editor nativo sono presenti; le nuove
creazioni sono sperimentali e verificate direttamente sul 2027. I 77 comandi
del catalogo XR hanno destinazioni raggiungibili. Diciannove creazioni sono
collegate al tastierino, alla penna per i punti e alla barra Anteprima/Applica su parte e lamiera;
tre comandi Formato, dodici vincoli espliciti e tredici varianti della Quota
generica operano sulle selezioni native;
le funzioni mancanti restano disabilitate con motivo esplicito.
Le precedenti azioni M3/M5 rimangono disponibili secondo il loro contratto.

Ultima prova PC verificata (94): Copia di cerchi/archi liberi con formato
default e selezioni miste linea/testo letterale. Parte/Lamiera:18workflow,
28coppie,10rifiuti,74checkpoint; client Core e oracle nativo PASS.
Verificatore offline indipendente PASS e otto alterazioni rifiutate.
Cleanup normale, installazione ripristinata; nessuna nuova capability/UI.
Un arresto d'anteprima del run r1 non riprodotto resta da chiarire.
Aggiornamenti/riaperture delle curve, altri profili e tutti i18gate restano
aperti. [Rapporto94](s1-copy-core-curves94.md).

Prova PC verificata (90): copia di linee libere e testo letterale,
quattro aggiornamenti delle sorgenti e due SaveCopy/riaperture Parte/Lamiera,
sei prime snapshot Core, copie invariate e quattro IPT archiviati. Verificatore
indipendente PASS, sette alterazioni avversariali rifiutate e cleanup normale.
È il profilo sperimentale88, senza chiudere Copia completa o altri gate S1.
[Rapporto](s1-copy-core-lifecycle90.md).

Ricerca91: venti parametri e dodici dipendenti osservati; nessun edge diretto
TextBox e ownership ancora UNKNOWN. Stato nativo prima/dopo esatto,
installazione ripristinata. [Rapporto](s1-parameter-dependencies91.md).

Ricerca92: venti copie native di cerchi/archi, singole e miste con linea,
Parte/Lamiera; dieci Abort esatti e sessanta stati raw. Verificatore
indipendente e cinque controlli di alterazione PASS. Integrazione nel
prodotto/Core, commit, aggiornamenti e persistenza ancora da collaudare.
[Rapporto](s1-curve-copy-research92.md).

Fondazione produttiva93: Copia sperimentale di cerchi e archi liberi con
formato default, punti distinti e grafo intrinseco mappato. Novantasei test
mirati e1.776 test bridge PASS; build2027 e net48 riuscite. Il candidato
congelato non è installato. Collaudo Core/HTTPS94: r1 FAILED nell'anteprima
di un arco Lamiera dopo dieci workflow riusciti; cleanup normale e41 raw
conservati. R2 PASS scoped, con verifica offline e otto alterazioni rifiutate;
la causa r1 resta aperta. [Fondazione](s1-copy-curves-foundation93.md),
[collaudo](s1-copy-core-curves94.md).

Incremento software verificato (49): chiavi persistenti dei quattro
angoli dell'immagine e metadati nel reader/Core; stato immagine ancora
partial per texture non verificata. **32 casi nativi PASS**, trasformazioni
e due copie salvate/riaperte; **1.046 Core**, **1.049 Unity** e **1.547 bridge
PASS**, uno SKIP bridge. **96/96 regressioni native inserimento/rollback PASS**.
[Rapporto](s1-image-corner-bindings49.md). Nessuna DLL/APK installata.

Incremento software verificato 48: query interna di download
del payload immagine persistente, binding documento/revisione/selezione,
deposito download separato e adattatore server con target fissato e hash.
**32/32 casi nativi PASS**, 320 rifiuti e stato CAD invariato;
**27 test dedicati PASS**, **1.547 bridge PASS**, zero FAIL e un SKIP.
Reader/rendering/UV, endpoint di prodotto e Quest restano aperti.
Nessuna installazione DLL/APK. [Rapporto](s1-image-download48.md).

Incremento software verificato 47: lettura a blocchi delle lease
sigillate, con limite prima dell'allocazione, range senza overflow e copie
indipendenti. **29 test mirati PASS**, 14 nuovi; **1.520 bridge PASS**, zero
FAIL e un SKIP. Build experimental 2027 e compatibilità net48 PASS.
Primitiva per il trasferimento inverso: reader, download e rendering XR
ancora da collegare. Nessuna installazione/APK o nuova prova nativa/Quest.
[Rapporto](s1-image-read-chunks47.md).

Incremento software verificato 46: BMP originali persistenti
negli attributi binari dell'immagine nativa, con hash e budget documentali.
**Due prove SaveCopyAs/riapertura PASS**, originali invariati; **96/96 casi
nativi di regressione PASS**. Cronologia **PARTIAL**: 32 casi, tre cicli
Undo/Redo ciascuno, modello ripristinato e payload conservato; Undo conserva
i descrittori OLE del commit, quindi baseline OLE esatta 0/32 e questione
ancora aperta. **1.506 bridge PASS, zero FAIL, un SKIP**.
Reader/rendering e Quest ancora da collegare, nessuna installazione/APK.
[Rapporto e limiti](s1-image-persistence-and-history46.md).

Incremento software 45: inserimento nativo embedded
da risorse sigillate e journal OLE, con protezione dei riferimenti preesistenti
e fallimento persistente se repair incompleto. Rotazione dell'oggetto tramite
RotateSketchObjects dopo aver isolato la divergenza di RotateLeft.
**96/96 casi nativi PASS**, 48 su Parte e 48 su Lamiera, con preview, commit
ed errore dopo Add; 48 osservazioni API aggiuntive e ripristino esatto.
**1.506 bridge PASS, zero FAIL, un SKIP**; build add-in e sonda PASS.
Variante ancora non dichiarata nelle capability XR; immagini linked,
reader/rendering, preview visiva, Undo/Redo e Quest restano aperti.
Nessuna installazione o nuova APK. [Rapporto e diagnostici](s1-native-image-batch45.md).

Incremento software 44: trasferimento chunk collegato
ai due ingressi, staging interno add-in e binding delle risorse al batch.
Target fissato fra preview e commit; cleanup su errore/cancellazione.
**1.506 bridge PASS, zero FAIL, un SKIP**; build 2027 e legacy PASS.
Nessuna nuova capability di inserimento CAD, installazione, APK o run Quest.
[Risultati e limiti](s1-import-transfer44.md).

Incremento software 43: risoluzione owner-bound delle
risorse immagine collegata a pianificazione e batch diretto; file mantenuti
nel piano oltre la scadenza del catalogo e rilasciati al termine. Deposito
chunk sigillato implementato, ancora da collegare al trasporto/add-in.
**1.451 bridge PASS, zero FAIL, un SKIP**; build add-in 2027 e legacy PASS.
Nessuna nuova capability CAD, installazione, APK o prova Quest.
[Risultati e limiti](s1-import-ingress-and-staging43.md).

Incremento software 42: ownership degli allegati dei piani,
rilascio su cancellazione/errori e limite UTF-8 del trasporto prima della
connessione; target congelato per futuri trasferimenti. **1.401 bridge PASS,
zero FAIL, un SKIP** (host già attivo). Build add-in PASS, non installata.
Ricerca OLE Parte/Lamiera **FAIL**: 58/64 casi avviati, 56 ripristini esatti,
due rifiuti per file linked condiviso con la baseline e sei casi non eseguiti.
Fixture ripristinate e documento Quest riaperto attivo con revisione invariata.
Nessuna capability CAD abilitata, nuova APK o prova Quest.
[Risultati e limiti](s1-import-lifetime-and-ole42.md).

Incremento software 41: punti planari originali conservati
nelle selezioni dirette, coppie ordinate/versionate congelabili per le future
soluzioni Raccordo/Smusso, invalidazione su refresh/finestra e blocco della
riconferma che riutilizzerebbe punti vecchi. **1.000 Core e 1.049 Unity PASS**,
16 nuovi casi; nessuna capability CAD aggiunta, APK o prova Quest nuova.
[Contratto e limiti](s1-curve-pick-contract41.md).
Ricerca nativa 40: 24 casi/72 aggiornamenti, 16 completati e otto divergenti
per quote sorgenti; 24 Abort esatti. La RadiusDim esplicita segue il parametro
in 36 aggiornamenti, senza dimostrare la parità dell'intero comando.
[Risultati e fallimenti conservati](s1-fillet-radius-policy40.md).

Incremento 38: F03 collega catalogo e preview risorse BMP
al client e all'editor condiviso Parte/Lamiera, con limite durante il download,
validazione/hash separati e revoca delle risposte tardive. L'inserimento CAD
rimane disabilitato. **990 Core e 1.043 Unity PASS**, 62 nuovi casi complessivi;
bridge invariato ai 1.372 PASS precedenti, non rieseguito in questo incremento.
C02 aggiunge otto osservazioni sulle dipendenze Coincident dall'intera linea,
16 aggiornamenti parametrici e otto Abort esatti. Senza replay il punto
dipendente non segue la sorgente; con replay esplicito segue entrambi gli
aggiornamenti. Non dimostra Dividi desktop.
Primo subset Quest completato con Inventor reale: **PARTIAL, otto PASS**,
apertura/preview-annullamento/commit linea/Concludi su Parte e Lamiera.
Due fallimenti iniziali del runner conservati e corretti: Coincident strutturali
non eliminabili e doppio riferimento Entity/Point. Fixture ripristinate senza
salvare, ritorno a zero documenti e APK ordinaria ripristinata/hash verificato.
Log, manifest e sette screenshot conservati; le catture con visore appoggiato
non provano visibilità sul display o leggibilità. Nessuna prova fisica.
DLL aggiornata effettivamente caricata, APK QA e ordinaria compilate/installate.
[Rapporto, hash e limiti](s1-parallel-wave38.md).
Tutti i 18 gate restano aperti; i paragrafi successivi conservano la cronologia
delle verifiche.

## Incremento spline a punti variabili — 11 ottobre

L'editor condiviso Parte/Lamiera raccoglie da 3 a 64 punti per spline
interpolanti e CV. Trigger aggiunge punti senza concludere al terzo;
Concludi spline prepara la bozza, con Anteprima/Applica ancora espliciti.
Sono disponibili rimozione dell'ultimo punto, annullamento e input X/Y
alternabile alla penna. La pagina dei comandi resta raggiungibile senza
tastierino; annullamento numerico e cambio revisione revocano i callback.
Passano 967 test Unity e 1.247 bridge. I dodici nuovi casi Unity coprono
entrambe le varianti e i workspace, conteggio, ordine, limite, input misto,
annullamento e revisione; la regressione dei gesti sintetici usa la
conclusione esplicita delle spline. Core invariato rispetto ai 787 precedenti.

La nuova sonda prepara 32 casi nativi Parte/Lamiera, 3/4/5/64 punti e due
rotazioni. Il run su Parte passa gli otto casi interpolanti, ma fallisce sul
primo CV: `SketchEntity.Constraints` restituisce `E_UNEXPECTED` su spline,
punti e linee del poligono di controllo, rendendo lo snapshot parziale.
Anche gli accessi dinamico e tramite interfacce specifiche falliscono;
i tentativi sono stati rimossi dal reader. Nessun PASS della matrice
completa e nessuna prova Lamiera dichiarati. Nei fit point il solve nativo
sposta leggermente le coordinate anche nell'API diretta: la sonda registra
lo scostamento e confronta esattamente i punti reali con l'oracle, senza
allargare una tolleranza per nascondere divergenze. I diagnostici iniziali
confrontavano erroneamente il risultato ai punti richiesti con tolleranze
1e-7/1e-5 mm; restano conservati come fallimenti, non come prove valide.

Fixture chiusa senza salvare; `spline-points-post-state31.json` conferma
zero documenti e zero fixture. Manifest `spline-points31-manifest.json`.
Restano aperti correzione snapshot CV, oracle desktop/minimo nativo,
chiusura, metodi di interpolazione, editing e collaudi Quest. Tutti i 18
gate S1 rimangono aperti. APK compilata, non installata.

## Correzione snapshot spline senza serie — 11 ottobre

Il reader interroga `SketchEntity.Constraints` per l'indice inverso delle
associazioni solo se la collezione globale dello schizzo contiene almeno
un `PatternConstraint`. Geometrie, quote e tutti i vincoli globali vengono
comunque letti. Questo evita un getter COM non necessario sulle CV negli
schizzi privi di serie, senza trasformare un errore di associazione in
un elenco vuoto quando le serie sono effettivamente presenti.

Passano ora tutti i 32 casi spline nativi: interpolante/CV, 3/4/5/64 punti,
due rotazioni, Parte/Lamiera. Confronti esatti dei punti nativi effettivi,
geometria/vincoli, preview annullata e commit; lo scostamento del solve
dai punti richiesti viene registrato separatamente. Passano anche i 24
casi di regressione poligono 3/65/120 lati e gli otto rifiuti di input su
Parte/Lamiera, mantenendo complete le associazioni dei vincoli poligono.
Il bridge passa nuovamente 1.247 test. Client/APK invariati rispetto ai
967 test Unity e alla build spline a punti variabili del precedente incremento.

La prova mista nello stesso schizzo passa quattro casi interpolanti con
poligono e poi fallisce sulla prima CV: il getter COM rende ancora parziale
lo snapshot quando è necessario l'indice delle serie. Questo caso resta
aperto; non è corretto dichiarare risolto l'accesso Constraints in generale.
Il report `spline-constraints-mixed-part-batch31.json` conserva il fallimento.
Il primo build della sonda mista è fallito per DLL ancora utilizzata dalla
regressione precedente; nessun processo interrotto. Ricompilazione riuscita
dopo la fine del processo, diagnostico preservato.

Cleanup e lettura indipendente `spline-constraints-post-state31.json`:
zero documenti e zero fixture. Manifest `spline-constraints31-manifest.json`.
Nessuna DLL distribuita, nessuna installazione Quest. Restano aperti caso
misto CV/serie, minimo e oracle desktop, opzioni/editing spline e tutti i
18 gate S1.

## Diagnostica Constraints delle CV — 11 ottobre

La sonda dedicata `arc-ui-spline-constraints` osserva 16 combinazioni:
poligono presente/assente, schizzo in modifica/fuori modifica, CV libera
oppure fissaggio della spline, di un punto di controllo o di un lato del
poligono di controllo. Tutte le transazioni sono annullate. I fallimenti
COM sono dati della ricerca, non PASS del prodotto.

`SketchEntity.Constraints` fallisce su tutte le sei entità della CV in
ogni combinazione. La collezione globale resta leggibile e contiene due
vincoli coincidenti; fissare la spline aggiunge tre GroundConstraint,
fissare punto/lato ne aggiunge uno. Anche con questi vincoli reali e con
schizzo in modifica il getter fallisce: non equivale a una collezione
vuota e il workaround di ingresso in modifica non risolve. `OwnedBy` è
leggibile ma non identifica tutte le parti della CV: i punti estremi sono
posseduti dalla spline, mentre punto interno, lati e spline non espongono
un proprietario. Non basta quindi filtrare le entità in base al proprietario.
I getter sulle sette entità del poligono indipendente restano leggibili.

Il primo tentativo di compilazione usava conversioni implicite tra
interfacce COM; corretto con cast espliciti, senza cambiare il prodotto.
Il primo report da otto combinazioni è diagnostico preliminare; il report
finale `spline-access-native-edit31.json` comprende tutte le 16 combinazioni.
La sonda non risolve il caso misto CV/PatternConstraint: occorre un percorso
alternativo verificato per le associazioni, senza ignorare getter falliti o
vincoli esistenti. Nessun cambiamento al reader, al client o all'APK in
questo incremento; nessuna capacità promossa o gate chiuso.
Cleanup e lettura indipendente `spline-access-post-state31.json` confermano
zero documenti/fixture. Evidenze nel manifest `spline-access31-manifest.json`.

## Snapshot parziali e blocco Applica — 11 ottobre

Il reader distingue ora la geometria leggibile dalle associazioni non
verificabili. Se il getter Constraints restituisce E_UNEXPECTED durante
l'indice inverso, conserva l'entità geometrica come supportata e registra
`pattern_membership_state=unavailable` con motivo. Lo snapshot resta
parziale; tutti i PatternConstraint hanno `binding_state=unavailable` e
motivo, senza pubblicare come completa la parte di associazioni ottenuta
dalle altre entità. Restano intatte le guardie per altri errori e budget.
Non è una soluzione dell'editing misto CV/poligono.

Il renderer disegnava già i tratti contenuti negli oggetti parzialmente
letti: non viene dichiarata una nuova capacità di visualizzazione. La
correzione riguarda classificazione e completezza dei dati. Il client
mantiene lo schizzo parziale in sola lettura. Inoltre `SketchSnapshot`
conserva ora `NativePartial` nelle anteprime e DesignSession nega Applica
anche dopo conferma del rendering. Per anteprime native lo stato completo
e il supporto di ogni entità devono essere espliciti; l'assenza dei flag
non abilita Applica. Le anteprime legacy senza native_entities conservano
il contratto precedente.

Passano 793 test Core, sei nuovi rispetto ai 787: geometria CV valida con
associazioni indisponibili e cinque casi anteprima completa/parziale/flag
assenti/oggetti non supportati. Verificano zero chiamate di commit nei
rifiuti e commit nel controllo positivo. Passano nuovamente 967 Unity e
1.247 bridge. Il backend rifiutava già le anteprime native parziali; la
prima sonda nuova che ne attendeva una rappresentazione visualizzabile è
fallita e resta conservata. La verifica finale attende invece correttamente
il rifiuto ROLLED_BACK, con snapshot e revisione identici.

Sedici scenari nativi finali su Parte/Lamiera, 3/5 punti e due rotazioni:
otto fit con oracle, preview/rollback e commit completi; otto CV con
snapshot parziale correttamente classificato e rifiuto dell'anteprima,
rollback esatto e nessun commit CV dichiarato. Report finali
`spline-partial-refusal-part-batch31.json` e controparte sheet.
I precedenti report `spline-partial-part-batch31.json`/sheet verificavano
solo lo snapshot parziale: non aggiungono altri casi alla matrice finale.
Il nuovo test Core inizialmente mancava il nome dello schizzo nella
fixture; il successivo guard Any richiedeva l'import System.Linq. Entrambi
i diagnostici sono conservati, con esecuzioni finali verdi.

Cleanup indipendente `spline-partial-final-state31.json`: zero documenti
e zero fixture. Manifest `spline-partial31-manifest.json`. Editing completo
del caso misto, opzioni/oracle desktop spline e tutti i 18 gate S1 rimangono
aperti; nessuna installazione o distribuzione DLL. APK ordinaria compilata con il guard NativePartial, non installata; i quattro assembly Unity eseguiti sono archiviati prima della build APK.

## Software presente

- `PaletteLayout`: profilo compatto 160 × 110 mm e Schizzo 320 × 220 mm,
  regolabile fra 280 × 190 e 360 × 250. Griglia dinamica, pagine, gruppi,
  breadcrumb, ritorno al gruppo precedente, posa e fissaggio/riaggancio.
  Picker da otto elementi e anello da sei mantengono limiti indipendenti.
  Alla perdita del tracking sinistro il fallback condiviso la posiziona
  sul bordo sinistro del banco, usando il frame di Ricentra e l'altezza
  calibrata. L'altezza del centro tiene conto dell'inclinazione e del formato,
  lasciando il bordo inferiore 10 mm sopra il piano. Prima del primo Ricentra congela il frame della testa al
  momento della perdita; non segue i successivi movimenti della testa.
  Restano conservati gruppo, input testuale/numerico e posa preferita della
  mano; al recupero si riaggancia dopo il rilascio del Trigger, evitando
  spostamenti durante un click o gesto in corso. Una posa fissata esplicitamente al banco
  non viene spostata dal tracking. Riagganciare senza tracking usa il
  fallback fino al ritorno del controller.
  Il modulo UI annulla pressioni/trascinamenti pendenti senza Click/Drop
  quando perde il tracking destro o il sinistro. Il percorso dei gesti CAD
  riceve la perdita prima del rilascio, preservando l'ultimo valore valido
  senza nuova anteprima; dopo la perdita occorre rilasciare il Trigger prima
  di un nuovo gesto. Il raycast UI del frame precede gli eventi semantici
  di `XrInput`. Questa protezione non applica o cancella bozze CAD.
- Catalogo condiviso parte/lamiera: 77 identità native distinte, dieci gruppi
  e tredici famiglie Crea. Le varianti future rifiutano l'invocazione: non
  chiamano strumenti CAD diversi e non hanno callback vuote.
- `get_sketch_editor_xr`: snapshot schema 1, frame, tessellazioni native,
  punti, quote, vincoli e testo; riferimenti opachi, documento e revisione.
  Tipi sconosciuti/immagini non rappresentate rendono lo snapshot parziale.
  Limiti: 2.000 oggetti per raccolta, 8.192 punti per curva, 100.000 punti
  complessivi; fino a 2.000 associazioni per vincolo e 100.000 per snapshot.
  Il client verifica identità duplicate e stato atteso.
  `supported_variants` dichiara le operazioni disponibili. Se manca, lo
  snapshot resta leggibile ma creazioni, Formato e vincoli sono disabilitati;
  la sessione rifiuta anche la bozza non dichiarata e la relativa riconferma.
  Gli ID di schizzo usano chiavi native senza contesto B-Rep, secondo il
  [contratto Autodesk 2027](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchEntity_GetReferenceKey.htm).
  La precedente serializzazione del contesto vuoto ha causato errori
  `E_OUTOFMEMORY` nella sonda, anche dopo la fine della build Unity; rimuoverla
  ha permesso di completare nuovamente tutti i casi. Il contratto accetta
  contesto vuoto solo per i sei tipi di riferimento di schizzo previsti;
  i riferimenti B-Rep continuano a richiedere il proprio contesto.
- `NativeSketchSession`: stato condiviso per parte e lamiera, operazione
  conservata dopo perdita della connessione/cambio revisione, riconferma
  esplicita dopo refresh. Anteprima/Applica usa `DesignSession`; esito incerto
  richiede refresh e revisione dell'esito prima di proseguire. Entrambi i
  workspace usano lo stesso editor UI e la propria `DesignSession` esistente.
  Il checkpoint conserva la bozza durante `Bind(null)` e successiva riconnessione,
  senza ripristinare un piano eseguibile. Cambio scena e chiusura del workspace
  revocano la prova di rendering; lo schizzo rimane aperto dopo ogni Apply.
- Selezione di geometrie native: filtro per tipo, selezione multipla e scelta
  esplicita fra candidati sovrapposti. La scelta conserva documento/revisione
  dello snapshot e scade dopo refresh, disconnessione o avvio di un comando.
  Non seleziona automaticamente il primo candidato. I due workspace espongono
  lo schizzo nativo aperto al contesto M9 e alla mappa dei controller.
  Filtri separati distinguono geometria intera, estremo iniziale/finale,
  centro e punto. Curve e sottoparti della stessa entità possono essere
  selezionate insieme; gli estremi selezionati hanno indicatori propri.
  La selezione multipla conserva l'ordine dei clic.
  Restano da completare segmenti fra intersezioni, preselezione,
  quote e vincoli. Le sottoparti disponibili provengono dai dati nativi,
  senza dedurre un centro da una tessellazione.
- Selezione a finestra condivisa Parte/Lamiera: azione nel gruppo Schizzo,
  Trigger tenuto sul foglio e conferma locale al rilascio. Trascinamento
  verso destra rispetto alla vista: geometrie contenute; verso sinistra:
  geometrie contenute o attraversate. Il contorno blu/verde usa proprietà
  del renderer indipendenti dal materiale condiviso. La regione è allineata
  agli assi del piano di schizzo; il verso considera la riflessione X CAD/XR.
  Nei filtri si sceglie sostituzione, aggiunta senza duplicati o inversione;
  Grip destro all'inizio sceglie inversione per quel gesto. Tipi e sottoparti
  mantengono i filtri correnti, con limite di 2.000 riferimenti verificato
  prima di sostituire la selezione. Il trascinamento aggiorna solo il contorno,
  senza ricostruire lo schizzo o interrogare il CAD a ogni frame.
  X, tracking perso, disconnessione, refresh/cambio revisione, chiusura,
  manipolazione a due mani e rilascio sulla UI annullano il gesto pendente;
  nessuna anteprima o mutazione CAD è implicita. La selezione precedente
  resta durante il gesto; cambio revisione o disconnessione la invalidano
  secondo il ciclo di vita dell'editor. Cerchi con raggio nativo usano test
  analitici sulla curva, senza selezionare il solo interno del disco;
  altre curve usano i segmenti dello snapshot nativo (corda 0,001 mm).
  Non è ancora un confronto con l'oracle desktop: casi al bordo delle curve,
  finestra prospettica, testo/immagini e selezione a finestra di quote/vincoli
  restano da completare/verificare. Runner Quest e prova fisica aperti.
- `edit_sketch`: una operazione semantica sullo schizzo esistente nella
  transazione batch posseduta. Varianti presenti: linea, linea punto medio, quattro rettangoli,
  poligono inscritto/circoscritto (3–120 lati), cerchio centro/raggio, punto, arco a tre punti e centro/angoli,
  ellisse, spline interpolante/CV, cinque asole. Nessun aumento del cap 32.
- Linea punto medio (`SketchMidpointLineWrapperCmd`, `line_midpoint`): usa
  [AddByMidEndPoints](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchLines_AddByMidEndPoints.htm),
  API introdotta nel 2025.2 e confermata nell'interop 31 installata. Parte e
  Lamiera condividono quattro modalità: tutte coordinate, punto medio nativo
  selezionato, estremo nativo selezionato o entrambi. I riferimenti possono
  essere punti isolati o sottoparti native; i campi numerici chiedono soltanto
  le coordinate mancanti. Il centro selezionato crea il vincolo nativo
  Punto medio, incluso nelle associazioni/annotazioni dello snapshot;
  l'estremo selezionato viene riutilizzato. La bozza congela coordinate,
  ruoli e riferimenti senza anteprima implicita; cambio revisione annulla
  l'input pendente e richiede riselezione esplicita mantenendo i ruoli.
  Sonda `midpoint-native31.json`: 24 scenari Parte/Lamiera, quattro modalità
  su punti isolati, estremi di linee e centri di cerchi; confronto API diretto
  di geometria/vincoli, anteprima annullata, identità sorgenti conservate,
  48 aggiornamenti sorgente e rifiuto di input invalidi. Ripristino confermato
  anche da lettura indipendente: zero documenti/fixture aperti come prima.
  È evidenza API nativa, non oracle desktop completo o prova Quest.
- Poligono S1 inscritto/circoscritto: intervallo intero 3–120 lati nella UI
  e in `edit_sketch`, conforme al desktop installato e alla
  [API 2027 AddAsPolygon](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchLines_AddAsPolygon.htm).
  `polygon-ui-comparison31.json` verifica 120 linee native regolari chiuse
  e il rifiuto di 121 senza variazioni della geometria. Screenshot e
  letture conservati; ripristinato il precedente valore desktop di sei lati.
  I vincoli `PatternConstraint` del poligono non appartengono alle collezioni
  di serie rettangolari/circolari: lo snapshot legge i membri effettivi da
  `SketchEntity.Constraints`, conserva riferimenti e associazioni, e dichiara
  `pattern_definition_state=unavailable` quando Inventor non espone tale
  definizione. Non li converte in una serie editabile; restano disponibili
  le operazioni consentite dal vincolo nativo. Budget di associazioni
  mantenuti. Ventiquattro confronti API/batch Parte/Lamiera passati per
  3/65/120 lati, due modalità e due orientamenti, inclusi vincoli e associazioni,
  rollback esatto e commit; otto rifiuti mantengono snapshot e revisione.
  I primi due run diagnostici rilevavano lo snapshot parziale dei vincoli
  del triangolo; rapporti conservati. Il run Parte intermedio precede
  l'ultima guardia sul budget globale delle associazioni; i due rapporti
  `polygon-*-batch-clearance31.json` riguardano il codice finale archiviato.
  Sedici casi di regressione delle serie rettangolari/circolari passati in
  Parte/Lamiera, incluse definizioni, soppressioni e aggiornamenti sorgente.
  `polygon-12031-manifest.json` identifica sorgenti, assembly eseguiti,
  rapporti, screenshot, test e APK; hash verificati dopo l'archiviazione.
  Editing dei poligoni, inferenze, guida dinamica e Quest aperti.
- Arco a tre punti, 11 ottobre: il desktop installato raccoglie inizio,
  fine e passaggio, mentre `SketchArcs.AddByThreePoints` richiede
  StartPoint/MidPoint/EndPoint. Corretto il riordino nel bridge; il default
  locale conserva il semicerchio e le istruzioni nominano il ruolo di ogni
  punto, anche per le asole a tre punti. Un terzo punto tramite penna allineato,
  coincidente o con raggio oltre 100.000 mm mantiene il campo pendente.
  Tastierino alternativo e Anteprima/Applica espliciti conservati.
  Un campione desktop con screenshot/readback verifica il semicerchio
  superiore e il mapping (`threepoint-arc-ui-comparison31.json`).
  Trentadue confronti API/batch Parte/Lamiera passano per ±45°/±90°/±180°/±270°,
  due orientamenti, controllo indipendente di centro/raggio/ampiezza/estremi,
  snapshot completo, geometria/vincoli, preview/rollback esatti e commit
  (`threepoint-arc-*-batch-clearance31.json`). I due rapporti iniziali
  precedono la verifica indipendente degli estremi, senza fallimenti CAD.
  955 test Unity passati, inclusi quattro nuovi casi penna/numeri e rifiuto
  dei punti degeneri in Parte/Lamiera; 1.247 test bridge passati.
  Core invariato rispetto ai 787 test del precedente incremento.
  Fixture chiuse, zero documenti confermati da lettura indipendente.
  `threepoint-arc31-manifest.json` identifica sorgenti, assembly eseguiti,
  rapporti, screenshot e APK ordinario compilato senza installazione.
  Inferenze, editing, aggiornamenti sorgente e Quest restano aperti.
- Asole ad arco tramite penna, 11 ottobre: Arco mediante centro e punto
  raccoglie centro, inizio, estremo e larghezza; l'ampiezza firmata segue
  il movimento della penna anche oltre 180°, conservando il raggio iniziale.
  Arco mediante tre punti raccoglie inizio, fine e punto di passaggio,
  poi la larghezza. Entrambe usano due volte la distanza dall'arco centrale
  finito, incluse le estremità arrotondate, con tastierino alternativo.
  Due campioni desktop su Parte, screenshot/readback e rapporti dei pixel
  (`arcslot-ui-comparison31.json`, tolleranza 0,005 distinta da quella CAD)
  verificano ordine e distanza oltre gli estremi. Il primo confronto API/batch
  era coerente con sé stesso ma non con l'arco richiesto: un controllo
  geometrico indipendente rileva 315° al posto di 90°. L'interop installata
  espone StartPoint/MidPoint/EndPoint, mentre il desktop raccoglie
  inizio/fine/passaggio. Corretto il riordino nel bridge; diagnostici conservati.
  I due rapporti `arcslot-*-batch-corrected31.json` passano 64 casi complessivi
  Parte/Lamiera: due varianti, ±90°/±270°, due orientamenti, larghezze 5/20 mm.
  Verificati cinque archi nativi, centro/raggio/ampiezza dell'arco di costruzione,
  snapshot completo, geometrie/vincoli, preview/rollback esatti e commit.
  Zero documenti/fixture finali confermati da `arcslot-post-state31.json`.
  Quindici nuovi casi Core e ventotto Unity condivisi Parte/Lamiera coprono
  geometria, percorso firmato, input misto, larghezza nulla/oltre budget,
  UI e callback annullati. Anteprima/Applica restano espliciti.
  `arcslot-pen31-manifest.json` identifica sorgenti, assembly eseguiti, test,
  diagnostici, screenshot e APK ordinario compilato senza installazione.
  Il primo test Core collocava due punti nel settore invece che oltre il
  cappuccio: corretti i dati del test, senza cambiare algoritmo o tolleranze.
  Inferenze, editing/aggiornamento sorgente, opzioni desktop complete,
  runner Quest e prova fisica restano aperti; nessun gate S1 complessivo chiuso.
- Asole lineari tramite penna: Punti d'interasse, Punti estremi e Punto
  centrale acquisiscono anche la larghezza dal terzo punto, oppure dal
  tastierino. Punti d'interasse usa due volte la distanza dal segmento dei
  centri: oltre un centro conta la distanza dall'estremità arrotondata.
  Punto centrale usa il segmento esteso simmetricamente verso l'altro
  centro. Punti estremi usa invece due volte la distanza perpendicolare
  dall'asse, anche vicino alle estremità; non forza il terzo punto sulla curva.
  Tre campioni desktop con screenshot/readback e rapporti dei pixel
  (`slot-ui-comparison31.json`, tolleranza 0,005), di cui Punto centrale in
  Lamiera. Ventiquattro casi API/batch (tre varianti × due larghezze × due
  orientamenti × Parte/Lamiera) verificano geometria/vincoli, anteprima
  annullata esatta e commit. Asse nullo, larghezza nulla/oltre 100.000 mm
  rifiutati durante la raccolta; UI e callback del campo chiuso protetti.
  Anteprima/Applica rimangono espliciti. Inferenze, guida dinamica delle asole,
  editing, asole ad arco e collaudo Quest restano aperti. Stadi ripetibili
  `slot-ui-prepare`/`slot-ui-prepare-sheet`, `slot-ui-read`, `slot-ui-batch`,
  `slot-ui-cleanup`, con le guardie delle fixture dedicate della sonda.
  `slot-pen31-manifest.json` identifica sorgenti, assembly eseguiti, risultati,
  screenshot e APK ordinario compilato; hash verificati dopo l'archiviazione.
- Arco dal centro tramite penna: centro, punto iniziale e movimento fino
  all'estremo finale. Il primo punto fissa raggio e angolo iniziale; il
  percorso successivo accumula l'ampiezza firmata, anche oltre 180°, senza
  modificare il raggio in base alla distanza dell'estremo. Una guida locale
  persistente segue il movimento, senza interrogare o ricostruire il CAD.
  La UI la distingue dall'Anteprima CAD esplicita. Tastierino completo e
  combinazioni penna/numeri restano disponibili. UI, punto sul centro e
  perdita di tracking sospendono il percorso; la ripresa conserva il verso
  già scelto. Arco nullo e giro completo sono rifiutati: parità desktop
  dei giri completi e inferenze automatiche ancora aperta.
  Quattro campioni desktop con Computer Use e readback: Parte ±90° e +270°,
  Lamiera −270°. Il campione −90° e quello Lamiera usano un estremo a distanza
  diversa dal raggio iniziale. L'API conserva gli archi orari in forma
  antioraria scambiando gli estremi: il confronto usa geometria e ampiezza,
  senza presumere che `SketchArc.SweepAngle` resti negativo.
  Sedici casi API/batch (±90°/±270° × inizio 0°/170° × Parte/Lamiera)
  verificano geometrie/vincoli, anteprima/rollback esatti e commit.
  Stadi ripetibili `arc-ui-prepare`/`arc-ui-prepare-sheet`, `arc-ui-read`,
  `arc-ui-batch`, `arc-ui-cleanup` nello stesso wrapper della sonda ellisse,
  con le stesse guardie della fixture dedicata. Manifest `arc-pen31-manifest.json`;
  Quest, prova fisica, editing e parità completa della famiglia restano aperti.
- Ellisse tramite penna: centro, estremo del primo semiasse e punto sulla
  curva. Il terzo punto viene trasformato nel frame del primo asse e determina
  il secondo semiasse con `b=|v|/sqrt(1-u²/a²)`. L'ordine degli assi è conservato
  anche se il secondo è maggiore del primo: verificato nel comando desktop e
  nell'API 2027, rimossa la restrizione XR non equivalente. Tastierino completo
  alternativo; dopo il primo asse indicato con penna, l'input numerico del secondo
  conserva l'angolo già acquisito. Punti degeneri/fuori dominio/oltre limite
  rifiutati senza terminare la raccolta. Nessuna preview o mutazione implicita.
  Tre campioni desktop (due Parte, uno Lamiera ruotato), screenshot e readback
  nativi in `ellipse-ui-comparison31.json`; confronto dei rapporti con tolleranza
  0,01 per l'arrotondamento dei pixel, distinta dalla tolleranza CAD.
  Diciotto casi API contro batch (tre coppie semiassi × tre rotazioni × due
  contesti): geometrie/vincoli, anteprima/rollback esatti e commit. Il desktop
  orizzontale aggiunge un vincolo inferito non ancora generato dal percorso XR;
  inferenze, anteprima dinamica, editing, Quest e prova fisica restano aperti.
  Ripetizione: wrapper `scripts/run-s1-native-probe.ps1 -Variant ellipse-ui-prepare`
  (oppure `ellipse-ui-prepare-sheet`), click nel comando desktop, poi stadi
  `ellipse-ui-read`, `ellipse-ui-api`, `ellipse-ui-batch` e `ellipse-ui-cleanup`.
  Prepare richiede zero documenti; gli altri stadi accettano solo la fixture
  dedicata singola, non salvata. Nessuna DLL viene installata. Il wrapper non
  simula i click e gli stadi desktop non equivalgono al runner Quest.
  Manifest `ellipse-pen31-manifest.json`; nessun gate complessivo S1 chiuso.
- Punti di creazione tramite penna: durante la richiesta di una coordinata
  X/Y, Trigger sul foglio acquisisce la coppia del punto corrente nel piano
  nativo. Funziona nelle 19 creazioni disponibili, inclusa Linea punto medio
  con coordinate mancanti; i riferimenti già scelti non vengono sostituiti.
  Il tastierino resta un'alternativa e un click sul foglio sostituisce tutta
  la coppia X/Y, anche se una coordinata era già stata confermata. Il cursore
  è un indicatore del punto, non un'anteprima CAD del risultato. Il cerchio
  centro/raggio accetta ora un secondo punto sulla circonferenza oppure il
  raggio numerico; ellisse e arco dal centro hanno i gesti descritti sopra.
  Nel primo incremento dei punti, raggi, angoli, lati e larghezze restavano numerici;
  gli incrementi successivi sopra descritti aggiungono i gesti di cerchio, ellisse, archi e asole.
  Quel primo incremento non certificava i gesti geometrici completi
  per ricavare tali valori dal puntamento.
  La sequenza centro/punto sul raggio segue la [guida Autodesk 2027](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-Help/files/GUID-D489CE6D-7299-4211-A43A-F3580A4BA357.htm);
  inferenze tangenti e anteprima dinamica restano aperte. Il tastierino chiuso
  non può sovrascrivere il raggio ricavato; raggio nullo/oltre 100.000 mm rifiutato.
  Nessuna anteprima/commit automatico al termine dei punti. Il raycast UI
  precede la cattura; X e cambio revisione revocano il callback pendente.
  La perdita di tracking nasconde il cursore e conserva i punti accettati;
  il recupero con Trigger tenuto non acquisisce un altro punto, serve
  rilascio e nuova pressione. Un campo numerico chiuso non può modificare
  l'operazione tramite un callback conservato. Il contorno del cursore usa
  il renderer persistente senza ricostruire lo schizzo a ogni movimento.
  Verifica EditMode con input sintetico e tavolozza collegata; percorso
  dispositivo/Inventor e prova fisica restano aperti.
- Formato: Costruzione su geometrie intere, Linea d'asse sulle linee native,
  Punto centrale sui punti nativi. Selezione multipla entro una sola operazione
  atomica; valore booleano esplicito, controllo di tipo, sottoparte, identità,
  revisione e appartenenza COM allo schizzo. Nessuna entità viene ricreata.
  Le proprietà sono incluse nello snapshot e visualizzate nell'anteprima con
  annotazioni dedicate. Un riferimento conservato di una revisione precedente
  richiede nuova selezione, anche dopo riconferma delle opzioni. Il percorso
  «Conferma nuove selezioni» conserva il valore di formato già scelto,
  aggiorna i riferimenti soltanto dopo l'azione esplicita e richiede una
  nuova anteprima prima di Applica.
- Modifica: Sposta e Copia usano un vettore in mm; Ruota usa centro in mm e
  angolo firmato in gradi, con copia opzionale. Operano su oggetti interi
  selezionati nella revisione attuale, usando le API native
  [MoveSketchObjects](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/PlanarSketch_MoveSketchObjects.htm)
  e [RotateSketchObjects](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/PlanarSketch_RotateSketchObjects.htm).
  «Consenti rimozione vincoli» corrisponde a `RemoveConstraints`: consente
  all'API di rimuovere alcuni vincoli verso altra geometria, senza promettere
  la cancellazione di tutti i vincoli. Il default conserva i vincoli;
  «Copia durante Ruota» è disattivato inizialmente. Le opzioni si congelano
  all'apertura del tastierino, producono una bozza e richiedono Anteprima e
  Applica espliciti. Refresh impone riselezione senza perdere i parametri.
  Il collaudo ha richiesto l'inclusione dei punti nativi che definiscono le
  curve: curve isolate potevano deformarsi. Le spline CV vengono trasformate
  tramite il loro poligono di controllo nativo. Due scenari Sposta su parte
  passano. La successiva matrice con guardie completa 16 esecuzioni native
  di Sposta/Ruota su parte/lamiera, otto scenari copia rifiutati e otto
  rifiuti anteprima/commit per rotazioni contenenti testo. La geometria
  dell'anteprima e del commit è confrontata con l'API diretta; gli ID restano
  stabili e il rollback è esatto. Sposta verifica anche i vertici dei glifi.
  Il confronto dei vertici nativi (`TextBox.GetFacetsInfo`) rileva che la
  rotazione sposta l'origine ma lascia orizzontali i glifi. La proprietà
  `Rotation` è un override e non misura l'orientamento del riquadro;
  assegnarle l'angolo arbitrario viene rifiutato. Neppure il
  [workaround Autodesk di spostamento avanti/indietro](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Rotating-a-textbox-with-iLogic-in-inventor-leads-to-the-textbox-being-rotated-but-not-the-text.html)
  ha risolto la fixture 2027. Ruota con testo selezionato è quindi
  temporaneamente rifiutato in backend e client; Sposta mantiene il testo.
  Durante l'oracle diretto di copia Inventor si è chiuso autonomamente,
  come confermato dall'utente; cleanup finale non verificabile in quel run.
  Copia e Ruota con copia sono temporaneamente rifiutate prima delle chiamate
  native e non pubblicate nelle capacità. La UI disabilita le relative azioni
  e impedisce l'anteprima/riselezione delle bozze con copia già conservate.
  Il blocco è temporaneo: non soddisfa la parità S1 della copia.
  Le immagini restano parziali nel client; Scala è ancora assente.
- Allunga: la variante `transform_stretch` usa una raccolta di oggetti nativi
  e `MoveSketchObjects` senza copia. Accetta curve intere, estremi disponibili,
  centri e punti; distingue le sottoparti della stessa curva e deduplica i
  punti nativi condivisi o sovrapposti prima della chiamata API. Le curve
  intere includono i punti che le definiscono; una sottoparte sposta il suo
  punto e lascia al solver la geometria collegata. Testo e immagini sono
  rifiutati. Selezione, vettore in mm e rimozione consentita dei vincoli si
  congelano nel tastierino; refresh richiede riselezione esplicita senza
  perdere le opzioni. Non sostituisce Scala o Taglia/Estendi/Dividi. La sonda
  passa 64 scenari su parte/lamiera, con punti condivisi, sovrapposizioni,
  spline, geometrie libere/fissate/quotate, oracle API e rollback esatto;
  `stretch-native-live31.json` conferma anteprima/commit equivalenti all'API,
  identità stabili, rollback esatto e ripristino precedente. Il manifest
  `stretch-live31-manifest.json` conserva assembly eseguiti, hash e test.
  La finestra locale di selezione è ora condivisa con l'editor; equivalenza
  desktop dei bordi, opzioni complete e collaudo Quest restano aperti.
- Offset: due modalità esplicite, per distanza positiva/direzione oppure
  per punto sullo schizzo. Curve intere selezionate nell'ordine di connessione;
  l'inclusione automatica richiede una sola curva base. `include_connected`
  è inizialmente falso e `create_constraints` vero, secondo i
  [default API Autodesk](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/PlanarSketch_OffsetSketchEntitiesUsingDistance.htm).
  Le chiamate native creano geometrie e vincoli nella transazione esistente;
  non ricostruiscono approssimazioni delle curve. Riferimenti, tipi, proprietario
  COM, opzioni e sottoparti sono validati; budget e solver restano quelli del
  comando semantico. La UI condivisa congela selezione/opzioni prima dell'input,
  conserva la bozza dopo refresh e impone riselezione esplicita. Gli 80 scenari
  nativi passano con 72 creazioni e otto rifiuti equivalenti all'API; modifica
  dedicata della definizione e interazione Quest restano aperte.
- Serie rettangolare/circolare: definizioni native tramite le collezioni
  `RectangularPatterns` e `CircularPatterns`, senza usare serie di feature.
  La selezione contiene prima le sorgenti, poi una/due linee direzione o un
  punto/arco/cerchio asse. Le opzioni condivise espongono associatività,
  distribuzione simmetrica, verso e distanza/angolo totale oppure passo;
  i [default API della serie circolare](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchCircularPatterns_CreateDefinition.htm)
  includono associatività e angolo totale. Il vincolo nativo della serie
  conserva una definizione nello snapshot con parametri, espressioni,
  soppressioni e numero di elementi. La UI consente aggiornamento numerico
  ed eliminazione della serie con Anteprima/Applica, anche quando il vincolo
  gestito non è eliminabile singolarmente. La tastiera XR condivisa ora
  raccoglie espressioni letterali per conteggi, distanze X/Y e angoli, in
  creazione e modifica: l'API ne verifica unità, parametri e limiti solo
  all'Anteprima. L'editing parte dall'espressione nativa, conserva opzioni
  e testo dopo refresh e richiede riselezione esplicita. La conferma della
  riselezione di modifica/eliminazione usa ora il vincolo della serie,
  correggendo il precedente passaggio erroneo alla selezione delle geometrie.
  Verso e simmetria X/asse e Y hanno ora controlli indipendenti; i controlli
  Y richiedono due direzioni e non entrano nelle bozze a una sola direzione.
  L'elenco degli indici soppressi è disponibile in creazione e modifica,
  con riattivazione esplicita di tutti gli elementi. L'input conserva fino
  al budget di 2.000 indici senza troncarlo; campi di espressione ancora
  limitati a 2.048 caratteri. Errori, annullamento e cambio revisione non
  applicano soppressioni. Il backend modifica la proprietà nativa
  [SketchPatternElement.Suppressed](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchPatternElement_Suppressed.htm)
  preservando definizione e parametri; l'array vuoto riattiva gli elementi.
  Boundary, blocchi, selezione separata dei ruoli e modifica completa delle
  altre opzioni di una definizione esistente restano da completare. Parità
  desktop e collaudo Quest restano aperti.
  La matrice separata delle espressioni passa 48 scenari, con 96 cicli di
  aggiornamento su parametri sorgente e definizioni; include serie statiche
  e associative, input errati, soppressione ed eliminazione. Il primo run
  si ferma su una differenza di circa `1e-13 mm` nella posizione testo quota:
  l'oracle risolveva sorgente e definizione insieme, il prodotto in due passi.
  Allineato l'ordine dei solve dell'oracle, il confronto resta esatto e passa
  sia il filtro circolare da 16 casi sia la matrice completa senza filtro.
  Entrambi confermano il ripristino precedente. I parametri temporanei sono
  scartati chiudendo le fixture senza salvare. Il manifest
  `pattern-expressions31-manifest.json` distingue i diagnostici dai PASS.
- Dodici vincoli espliciti: coincidente, parallelo, tangente, collineare,
  perpendicolare, levigato G2, concentrico, orizzontale, simmetrico, fissaggio,
  verticale e uguale. Cardinalità distinte: uno per Fissaggio, uno o due
  punti per H/V, tre per Simmetrico con asse intero selezionato per ultimo;
  gli altri richiedono due selezioni compatibili e ordinate. I riferimenti
  a estremi/centri risolvono punti nativi, non coordinate approssimate.
  Due punti usano `SketchPoint.Merge`: il comando desktop condivide un punto
  e non aggiunge un vincolo Coincidente fra estremi di linea, come documentato
  da [Autodesk](https://blog.autodesk.io/addcoincident-with-two-sketch-points/).
  Punto-curva usa `AddCoincident`; Levigato usa `AddSmooth`, che crea un
  [vincolo G2](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/GeometricConstraints_AddSmooth.htm).
  La bozza conserva assi maggiore/minore delle ellissi e punto vicino per
  Tangente/G2. Il pannello XR di queste opzioni è distinto dal dialogo nativo
  Opzioni vincolo, la cui inferenza resta da implementare. Ogni comando usa
  Anteprima/Applica e rifiuta il solver sovravincolato; simboli, tutte le
  opzioni e oracle desktop restano aperti.
  La rimozione mirata è ora collegata a un elenco dedicato di vincoli esistenti,
  condiviso da Parte/Lamiera: la scelta usa l'identità nativa, poi «Elimina
  vincolo» prepara una sola operazione `constraint_delete`. Il bridge controlla
  revisione, documento, schizzo proprietario reale, tipo, sottoparte intera e
  `Deletable`; rifiuta opzioni aggiuntive. Dopo refresh la bozza richiede una
  nuova scelta esplicita del vincolo prima dell'anteprima. Questa aggiunta
  resta sperimentale. Il confronto nativo mirato su Fissaggio/Levigato passa
  su parte e lamiera (`constraints-native-smooth-oracle31.json`, filtro
  Levigato con fixture Fissaggio obbligatoria). Eliminando G2 il solver può
  cambiare la forma delle spline: la differenza fino a circa 0,34 mm si
  riproduce nell'eliminazione API diretta. La sonda confronta perciò snapshot
  e anteprima con l'oracle nativo, anziché presumere una forma invariata;
  rollback e confronto restano esatti. La matrice completa passa 30 scenari
  e 28 eliminazioni (`constraints-native-complete31.json`, filtro nullo),
  identificati da `constraints-complete31-manifest.json`; Quest resta aperto.
  Le associazioni sono ora lette dalle proprietà native dei dodici tipi di
  vincolo, incluse coppie di punti e asse di simmetria. Gli estremi/centri
  condivisi sono associati per chiave nativa alle rispettive sottoparti,
  senza ricavarli dalla tessellazione. `binding_state=complete/unavailable`
  distingue i collegamenti disponibili; un tipo non mappato non riceve
  collegamenti inventati. Il client valida ogni associazione rispetto alle
  entità e sottoparti dello stesso snapshot e conserva riferimenti immutabili.
  Selezionare un vincolo evidenzia la geometria associata e il suo punto di
  annotazione; «Mostra vincoli» cambia solo la vista. Il vincolo selezionato
  rimane visibile anche quando gli altri sono nascosti. Sono annotazioni
  testuali XR; simboli nativi, filtri completi e oracle desktop restano aperti.
  Le annotazioni compaiono anche nella preview abortita, senza ID o riferimenti
  selezionabili. I collegamenti mancanti sono segnalati nella UI. Geometrie
  native di tipo non supportato rendono esplicitamente parziale lo snapshot.
- Quota generica: distanze allineate/H/V, angoli fra linee o tre punti,
  raggio, diametro, lunghezza arco, raggi ellisse, offset, distanza tangente
  e offset spline tramite `DimensionConstraints` native. Il menu condiviso
  conserva selezioni ordinate, testo in mm, valore in mm/gradi, modalità
  driving/driven, diametro lineare, lato ellisse e punti vicini nella bozza.
  Non invia anteprima o commit alla fine del tastierino. Il bridge accetta
  anche espressioni validate nelle unità native e nome del parametro.
  Le quote esistenti sono selezionabili da un elenco dedicato a otto voci
  per pagina: valore, posizione testo, modalità driving/driven ed eliminazione
  mirata usano la stessa bozza e barra Anteprima/Applica. Evidenzia la quota
  scelta; cambio revisione richiede scelta e riconferma esplicite, conservando
  le opzioni. Il bridge modifica la quota esistente e verifica il suo schizzo
  proprietario anche se il riferimento dichiara un'appartenenza contraffatta.
  Una tastiera XR a pagine permette ora di inserire espressioni e nomi per
  nuove quote e modificare quelli delle quote esistenti. Conserva il testo
  letterale, con cursore, spazio, cancellazione e annullamento; il limite è
  2.048 caratteri per espressione e 128 per nome. OK prepara soltanto la bozza:
  unità, sintassi e dipendenze vengono validate da Inventor durante Anteprima.
  Le nuove quote con espressione saltano l'input del valore numerico;
  posizione e punti vicini restano espliciti. Il nome di una nuova quota
  viene consumato dalla preparazione, evitando di riutilizzarlo sulla seguente.
  Un cambio revisione revoca l'input testuale pendente. Parte e Lamiera
  condividono il percorso; durante la tastiera testuale la voce numerica è
  disarmata e torna disponibile sul tastierino numerico successivo anche dopo
  Annulla. I campi cancellati non vengono riesposti come attivi. Dettatura
  testuale, quotatura automatica, dipendenze complete
  e oracle desktop delle opzioni restano aperti.
  Le varianti offset seguono il [contratto Autodesk 2027](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/DimensionConstraints_AddOffset.htm).
- Anteprima con tessellazioni native, punti e annotazioni, senza riferimenti
  selezionabili per entità che verranno abortite. Il vecchio lettore provocava
  un errore COM sulla spline CV: il percorso XR ora usa il lettore nativo.
  Uno snapshot incompleto durante la preview impedisce il commit.

L'intestazione usa 16 mm per navigazione e ritorno; la fascia inferiore da
8 mm contiene solo lo stato. I gruppi usano quattro colonne e due righe:
con più di otto gruppi, sette destinazioni e «Altri» permettono il cambio
pagina senza ridurre testo/hit target. Questa scelta è coperta dai test
geometrici e rimane da provare fisicamente sul Quest.

## Evidenze locali e native

Checkout iniziale: `4edba3202f2dd5e140087b7813855b8495c8b52a`, già modificato.
Patch e stato iniziali conservati in `artifacts/s1-verification/checkout-before.*`.
Le modifiche preesistenti di hardening non sono attribuite a S1.
Le verifiche del 7 e 8 ottobre sono state eseguite sul checkout successivo
`84075bae9298b790082e24506018e53d6f81d955`, con le modifiche S1 ancora non
committate. Il manifest conserva separatamente HEAD iniziale e HEAD verificato.

| Verifica | Esito | Limite |
|---|---|---|
| Core .NET | 941 test PASS l'11 ottobre | 35 nuovi casi C02-B (`core-parallel34.trx`).  102 nuovi casi C02-A/D04-A/F03-A (core-parallel32-final.trx), oltre agli 804 precedenti. Undici nuovi casi sul metodo spline congelato e validazione preferenza (`core-spline-minimum31.trx`), oltre ai 793 precedenti.  Sei nuovi casi snapshot/anteprima parziale (`core-spline-partial-preview.trx`), oltre ai 787 precedenti: geometrie leggibili con associazioni indisponibili, rifiuto Applica anche dopo rendering e controllo positivo. Quindici nuovi casi asole ad arco (`core-arcslot-pen-final.trx`): arco finito, cappucci, rotazioni, ordine dei tre punti, ampiezze e degeneri; oltre ai 772 precedenti. Quattordici nuovi casi larghezza asole, incluso cambio verso/rotazione, estremità arrotondate, asse/ampiezza nulli e budget (`core-slot-input.trx`); oltre ai 758 precedenti. Dodici nuovi casi del percorso firmato dell'arco: ±90°/±270°, attraversamento ±180°, inversione, sospensione/ripresa, centro e giro completo (`core-arc-input.trx`); oltre ai 746 precedenti. Non certificano le API CAD |
| Bridge .NET | 1.372 PASS | 35 nuovi casi renderer PNG/preview HTTP (`bridge-preview37.trx`), pixel/CRC/budget e owner/TTL/originale invariato. 52 nuovi casi inspector BMP reale (`bridge-bmp36.trx`), inclusi host loopback e rifiuti. 17 nuovi casi F03-C2 (`bridge-import35.trx`), host loopback e inspector finto. 21 nuovi casi F03-C1 (`bridge-parallel34-final.trx`), incluse regressioni sui metadati CLR.  `bridge-threepoint-arc.trx`: codice finale del riordino API di arco e asola a tre punti; include limite poligono e snapshot dei vincoli; superficie MCP e chiavi senza contesto aggiornate. Warning preesistenti nei test |
| Nuova sonda Arco a tre punti | 32 casi API/batch PASS e un campione desktop | `threepoint-arc-part-batch-clearance31.json`, `threepoint-arc-sheet-batch-clearance31.json`, `threepoint-arc-ui-comparison31.json`: ±45°/±90°/±180°/±270°, due orientamenti, punti in ordine desktop e controllo geometrico indipendente, rollback esatti. Inferenze, editing e Quest aperti |
| Nuova sonda Asole ad arco | 64 casi API/batch PASS e 2 campioni desktop | `arcslot-part-batch-corrected31.json`, `arcslot-sheet-batch-corrected31.json`, `arcslot-ui-comparison31.json`: due varianti, ±90°/±270°, due larghezze/orientamenti, geometria indipendente del centro e confronto completo API/batch. Inferenze, editing e Quest aperti |
| Nuova sonda Asole lineari | 24 casi API/batch PASS e 3 campioni desktop l'11 ottobre | `slot-part-batch31.json`, `slot-sheet-batch31.json`, `slot-ui-comparison31.json`: tre varianti, larghezze 5/20 mm, asse 0°/53,13°, Parte/Lamiera; geometrie/vincoli, anteprima/rollback esatti e commit. Campioni desktop su estremità arrotondate, rapporti dei pixel con tolleranza 0,005; inferenze e Quest aperti |
| Nuova sonda Poligono | 24 casi API/batch e 8 rifiuti PASS l'11 ottobre | `polygon-part-batch-clearance31.json`, `polygon-sheet-batch-clearance31.json`: 3/65/120 lati, inscritto/circoscritto, 0°/53,13°, Parte/Lamiera. Snapshot completi, vincoli e associazioni native, preview/rollback esatti e commit. `polygon-ui-comparison31.json`: un campione desktop di 120 lati regolari chiusi e rifiuto di 121; non certifica tutte le opzioni desktop |
| Nuova sonda Arco dal centro | 16 casi API/batch PASS e 4 campioni desktop l'11 ottobre | `arc-part-batch31.json`, `arc-sheet-batch31.json`, `arc-ui-comparison31.json`: ±90°/±270° e inizio 0°/170°, Parte/Lamiera, geometria/vincoli e rollback esatto. Fixture chiuse senza salvare; giro completo, inferenze e Quest aperti |
| Nuova sonda Ellisse | 18 casi API/batch PASS e 3 campioni desktop l'11 ottobre | `ellipse-part-batch-final31.json`, `ellipse-sheet-batch31.json`, `ellipse-ui-comparison31.json`: semiassi in entrambi gli ordini/equivalenti, rotazioni, confronto nativo geometrie/vincoli e rollback esatto. Fixture chiuse senza salvare, stato indipendente finale zero documenti. Inferenze desktop e Quest aperti |
| Nuova sonda Linea punto medio | 24 scenari e 48 aggiornamenti sorgente PASS l'11 ottobre | `midpoint-native31.json`, raccolte `contexts[].midpoint_commands`: coordinate/centro/estremo/due punti, punti isolati/estremi linee/centri cerchi su Parte/Lamiera, confronto API diretto, vincoli/identità, anteprima/rollback esatti e invalidi. `previous_restored=true` e lettura indipendente pre/post con zero documenti aperti. Manifest `midpoint31-manifest.json`; non certifica il workflow desktop o Quest |
| Unity EditMode | 1.030 PASS | Sei nuovi casi filtri/diagnostica nei workspace Parte/Lamiera (`unity-visibility36-final.xml`); diagnostico iniziale con due fallimenti di cap conservato. 24 nuovi casi layer/input/renderer D04 (`unity-constraint35-r2.xml`), diagnostici iniziali conservati. 13 nuovi casi D04-B1 (`unity-parallel34-final.xml`); normale del piano corretto dopo cinque fallimenti iniziali, diagnostico conservato.  Sedici nuovi casi scelta metodo spline, default, input, annullamento e refresh Parte/Lamiera (unity-spline-method33-final.xml), oltre ai 971 precedenti. Quattro nuovi casi spline a due punti, undo e input misto nei due workspace (`unity-spline-frozen-method31.xml`), oltre ai 967 precedenti.  Regressione completa ripetuta con il guard NativePartial (`unity-spline-partial-preview.xml`). Dodici nuovi casi spline (`unity-spline-points.xml`), oltre ai 955 precedenti: punti variabili, conclusione esplicita, undo, limite 64, input misto, callback annullati e revisione Parte/Lamiera. Quattro nuovi casi arco a tre punti (`unity-threepoint-arc.xml`): inizio/fine/passaggio, punti degeneri e alternativa numerica Parte/Lamiera; oltre ai 951 precedenti. Ventotto nuovi casi asole ad arco (`unity-arcslot-pen.xml`): due varianti, ±90°/±270°, larghezza con penna/numeri, input UI, rifiuti e callback annullati Parte/Lamiera; oltre ai 923 precedenti. Dodici nuovi casi poligono (`unity-polygon-120.xml`): 3/65/120 lati, inscritto/circoscritto, rifiuto 2/121 mantenendo il campo pendente, wire intero e coordinate originali, Parte/Lamiera; oltre ai 911 precedenti. Ventiquattro nuovi casi asole lineari (`unity-slot-pen.xml`): tre varianti, punti interni/estremità, tastierino alternativo, input UI, larghezza nulla/oltre budget e callback chiusi su Parte/Lamiera; oltre agli 887 precedenti. Venti nuovi casi arco (`unity-arc-pen.xml`): percorso ±90°/±270°, input misto, rifiuti e pausa sopra UI su Parte/Lamiera; renderer persistente e guida locale senza ricostruzione. Oltre agli 867 precedenti. Include dodici nuovi casi ellisse in Parte/Lamiera (`unity-ellipse-pen-final.xml`): punto sulla curva, rotazione, semiassi in entrambi gli ordini, rifiuti e input numerico misto; oltre agli 855 precedenti. Include quattro nuovi casi centro/raggio del cerchio in Parte/Lamiera (`unity-circle-pen-final.xml`): distanza CAD, rifiuto raggio nullo/oltre limite e callback del campo chiuso; oltre agli 851 precedenti. Include i punti tramite penna nelle 19 creazioni in entrambi i workspace con tavolozza collegata, coordinate nel piano CAD, scalarità numerica, UI/cancellazione/revisione, tracking destro/sinistro con rilascio obbligatorio e sostituzione della coppia X/Y (`unity-native-point-input-clearance.xml`), oltre agli 807 precedenti. Frame sintetici/EditMode; non è una prova Quest |
| Add-in experimental | build PASS, zero warning/errori | Non distribuito all'Inventor già avviato |
| Add-in default | build PASS, zero warning/errori | Artefatto separato: capacità S1 escluse dal tier stabile |
| Native probe | 38 casi creazione, 12 Formato, 30 vincoli, 78 quote e 26 scenari di modifica quote PASS + guardie PASS | Ogni scenario sulle quote esistenti verifica cinque mutazioni; non è un oracle desktop completo |
| Nuova sonda dipendenze quote | 78 creazioni e 26 cicli PASS il 10 ottobre | `dimensions-native-fixture-cleanup31.json`: 130 mutazioni e 52 aggiornamenti reali sorgente su parte/lamiera, anteprima/rollback esatti, geometrie/valori/unità/posizionamento e ripristino. Parametri temporanei scartati chiudendo le fixture senza salvare; il precedente dependencies31 fallisce solo nel tentativo di cancellare il parametro sorgente |
| Nuova sonda rimozione vincoli | 30 scenari e 28 eliminazioni PASS il 10 ottobre | `constraints-native-complete31.json`, filtro nullo, parte/lamiera, associazioni e guardie. Eliminazioni confrontate con API diretta: snapshot/anteprima e rollback esatti, ripristino confermato. Due eliminazioni di G2 cambiano le spline come nell'oracle; il precedente aligned31 resta un fallimento della presunzione di forma invariata |
| Nuova sonda trasformazioni | 16 esecuzioni native e 8 scenari copia rifiutati PASS il 10 ottobre | `transforms-native-guarded-complete31.json`, parte/lamiera, otto rifiuti aggiuntivi anteprima/commit della rotazione testo, geometria/identità/rollback/oracle. Copia e rotazione testo restano aperte; il precedente crash in `transforms-native-text-origin31.json` non ha cleanup verificabile |
| Nuova sonda Allunga | 64 scenari PASS il 10 ottobre | `stretch-native-live31.json`, 16 fixture × rimozione vincoli sì/no × parte/lamiera. Spostamento dei punti liberi, identità, quote/vincoli, confronto API di anteprima/commit, rollback e ripristino; finestra/crossing e Quest ancora aperti |
| Nuova sonda Offset | 80 scenari PASS il 10 ottobre | 72 creazioni con due cambi sorgente e identità stabili, otto rifiuti geometrici equivalenti all'API diretta. Parte/lamiera, anteprima/rollback e guardie; `offsets-native-complete31.json`. Quest ed editing dedicato della definizione restano aperti |
| Spline a punti variabili senza serie | 32 casi nativi PASS su Parte/Lamiera | `spline-constraints-part-batch31.json` e `spline-constraints-sheet-batch31.json`: interpolante/CV, 3/4/5/64 punti, due rotazioni, punti nativi effettivi e confronto esatto snapshot/preview/rollback/commit. Caso misto CV con poligono ancora FAIL, conservato separatamente; nessun gate chiuso |
| Nuova sonda Serie | 48 scenari PASS il 10 ottobre | Parte/lamiera, confronto API, soppressione, anteprima/rollback, due aggiornamenti sorgente/conteggi ed eliminazione associativa; `patterns-native-aligned31.json`. Non è un oracle desktop completo né una prova Quest |
| Nuova sonda espressioni Serie | 48 scenari PASS il 10 ottobre | `patterns-native-expressions-complete31.json`, filtro nullo: 24 serie associative e 24 statiche su parte/lamiera, espressioni per conteggi/distanze/angoli, 96 cicli sorgente/definizione, rifiuti, confronto esatto, anteprima/rollback e ripristino. Manifest `pattern-expressions31-manifest.json`; oracle desktop e Quest aperti |
| Nuova sonda soppressioni Serie | 16 scenari e 24 modifiche PASS il 10 ottobre | `patterns-native-suppression31.json`, parte/lamiera, rettangolare a due direzioni, statica/associativa e fitted/non-fitted. Soppressione multipla, cambio/riattivazione/ripristino set, confronto API di anteprima/commit, identità sorgenti, rollback e ripristino |
| Nuova matrice opzioni/soppressioni Serie | 48 scenari PASS il 10 ottobre | `patterns-native-options-suppression-complete31.json`: tutte le combinazioni verso/simmetria X/Y e asse, parte/lamiera, 144 modifiche delle soppressioni, 96 cicli sorgente/definizione, 48 rifiuti con rollback anche dopo cambio conteggio. Serie associative e fitted ai default: non è il prodotto cartesiano di tutte le opzioni. Manifest `pattern-options31-manifest.json` |
| Regressione Serie dopo correzione vincoli poligono | 16 scenari PASS l'11 ottobre | `polygon-pattern-rectangular-regression31.json`, `polygon-pattern-circular-regression31.json`: quattro combinazioni indipendenti verso/simmetria per famiglia su Parte/Lamiera, soppressioni, due aggiornamenti sorgente/definizione e rifiuti; definizioni delle serie esposte conservate. Ripristino confermato; non sostituisce l'oracle desktop completo |
| Stato Inventor dopo le sonde | PASS, nessun documento aperto | Lettura indipendente aggiornata `spline-frozen-final-state31.json`: documento attivo vuoto come prima, zero documenti e zero fixture residue; Inventor resta aperto |
| APK Android ordinario | build PASS aggiornata l'11 ottobre, 53.897.214 byte | Conservata in `artifacts/s1-verification/InventorXrSo-s1-spline-method.apk`; SHA-256 `EDD3C74A0727BB1405B04DC4B6CE335A19718652F3B79546B633113332F969B8`. Non installata sul Quest, senza runner S1. Le APK dei precedenti manifest restano congelate |
| Quest sintetico S1 | NON ESEGUITO | Runner S1 e APK dedicato ancora da completare |
| Quest fisico S1 | NON ESEGUITO | Ergonomia, tracking, occlusione e gesti aperti |

`bridge/tests/S1LiveProbe` si collega all'Inventor aperto, crea esclusivamente
documenti temporanei dedicati di parte/lamiera, censisce ribbon fuori/dentro
schizzo e raccoglie firme interop. Per ogni variante verifica preview con
geometria nativa, Abort senza residui/revisione modificata, commit incrementale
senza ricreare lo schizzo e mantenimento della linea originale. Verifica anche
rollback dell'intero batch dopo l'errore nella seconda operazione, solver,
vincoli e revisione invariati; rifiuto snapshot stale e scrittura diretta.
Chiude le fixture senza salvare e riattiva il documento precedente.
L'ultimo rapporto conferma `previous_restored=true`.
Per ogni snapshot incrementale risolve gli ID opachi e confronta gli estremi,
centri e punti disponibili con gli oggetti nativi, con tolleranza 10⁻⁷ mm.
Uno snapshot parziale causa il fallimento della sonda, oltre al rifiuto del
client. Il rapporto conserva il numero di ancore verificate per caso.
L'ultimo run verifica 4.252 ancore complessive e il resolver generico su ID
di schizzo/entità senza contesto, in entrambi i tipi di documento.
I 12 casi Formato verificano attivazione/disattivazione delle tre proprietà,
snapshot prima/dopo preview e commit, assenza di entità/vincoli ricreati e
rifiuto di riferimenti scaduti, tipi incompatibili e appartenenza contraffatta
a uno schizzo diverso. L'oracle delle opzioni e delle dipendenze desktop resta
aperto per questi comandi, come il loro percorso sul Quest.
I 30 casi di vincoli coprono i dodici tipi su parte/lamiera, più Coincidente
punto-curva e H/V fra punti. Verificano snapshot/revisione identici dopo
Abort, tipo di vincolo nativo dopo commit, punti condivisi dopo Merge e
`kSmoothConstraintObject` per G2 su spline collegate. Le guardie rifiutano
revisioni scadute, appartenenza a uno schizzo diverso e opzioni asse/punto
vicino malformate, senza lasciare modifiche. Gli schizzi di prova completati
vengono eliminati dalla fixture prima dei casi successivi.
I 78 casi di quote coprono tredici varianti su Parte/Lamiera, ciascuna driving
numerica, driven e driving con espressione/nome del parametro. Verificano
tipo nativo, valore in mm/gradi, nome/espressione, posizione del testo,
anteprima senza ID temporanei e snapshot/revisione identici dopo Abort.
Le quote driven conservano identità e proprietà esatte della geometria;
coordinate e tessellazioni ammettono al massimo 10⁻⁵ mm di deriva del solver
spline, contro la tolleranza del tessellatore di 10⁻³ mm. La stessa tolleranza
di 10⁻⁵ mm vale per la posizione del testo delle quote; l'annullamento della
preview richiede comunque uguaglianza esatta. Il testo richiesto viene posto
dopo la soluzione del valore driving, che inizialmente lo sposta in Inventor.
Questo collaudo non copre tutte le combinazioni delle opzioni né un secondo
aggiornamento delle dipendenze parametrizzate.
I 26 scenari sulle quote esistenti coprono ogni famiglia su Parte/Lamiera:
valore numerico, posizione testo, conversione a driven, ritorno a driving
con espressione e nome, eliminazione mirata. Sono 130 mutazioni, ciascuna
preceduta da preview con annullamento e confronto esatto di snapshot/revisione.
Le modifiche mantengono l'ID della quota; eliminazione e modifica non ricreano
la geometria. Su entrambi i documenti le guardie rifiutano revisione scaduta,
tipo/sottoparte/documento errati, valore prescritto su driven, unità sbagliata,
espressione/posizione malformate e proprietario contraffatto, senza residui.
Per la lunghezza arco Inventor normalizza il testo dopo l'aggiornamento: la
posizione richiesta (35, 42) mm diventa circa (34,63515, 40,84990) mm nella
fixture. La sonda confronta questo risultato con una transazione API diretta,
verificata e annullata, invece di dichiarare una posizione arbitraria esatta.
Questo è un confronto API; non chiude il confronto completo con il gesto
desktop. L'Anteprima XR mostra la posizione nativa effettiva.
Il run dell'8 ottobre usa un'istanza Inventor temporanea posseduta dalla sonda:
nessuna istanza utente era aperta, nessun documento utente è stato caricato.
Il rapporto conferma anche `owned_application_closed=true`.

Rapporto locale: `artifacts/s1-verification/native-probe.json`.
XML/log Unity del 10 ottobre: `artifacts/s1-verification/unity-text-entry.*`.
La sonda è stata estesa per modificare due volte un parametro sorgente di
ogni famiglia di quota e controllare identità, espressione e aggiornamento.
Queste nuove asserzioni non hanno ancora un esito CAD: il 10 ottobre entrambi
i tentativi sono terminati nell'attivazione COM, prima di creare le fixture.
Rapporti: `text-dependencies-native.json` e
`text-dependencies-native-retry.json` nella stessa cartella degli artefatti.
Il rapporto nativo riuscito dell'8 ottobre non è stato sovrascritto; i suoi
PASS non coprono le nuove asserzioni. Il manifest
`artifacts/s1-verification/text-entry-manifest.json` del 10 ottobre conserva
separatamente hash e date delle nuove verifiche. `manifest.json` resta il
manifest storico delle prove native e della build dell'8 ottobre. L'APK della
tastiera è conservato separatamente come `InventorXrSo-s1-text-entry.apk`.
La verifica successiva della rimozione vincoli usa
`artifacts/s1-verification/unity-constraint-delete.xml` e relativo log;
`constraint-delete-native.json` conserva il fallimento nell'attivazione COM,
prima dell'esecuzione CAD. La sonda compilata confronta eliminazione mirata,
vincoli/quote/geometrie conservati, preview annullata senza residui e rifiuto
di riferimenti stale, tipo/sottoparte/documento errati, proprietario
contraffatto e opzioni aggiuntive. Queste asserzioni native restano da eseguire.
Il manifest precedente `artifacts/s1-verification/constraint-delete-manifest.json`
conserva sorgenti, risultati locali e hash dell'APK ordinario aggiornato,
con l'esito CAD marcato NON ESEGUITO. Non estende i PASS nativi dell'8 ottobre
al nuovo handler di cancellazione.
Le successive prove delle associazioni sono conservate in
`unity-constraint-bindings.xml` e relativo log. La sonda nativa compilata
richiede associazioni complete per i vincoli aggiunti, confronto con le
selezioni native originali e assenza di identità nelle annotazioni di preview.
Queste nuove asserzioni CAD non sono state eseguite: dopo il fallimento COM
già registrato non è stato ripetuto l'avvio senza un cambiamento dello stato
esterno. La disponibilità del target Inventor rimane da ripristinare.
Il manifest precedente `artifacts/s1-verification/constraint-bindings-manifest.json`
identifica sorgenti, test e APK delle associazioni, conservato come
`InventorXrSo-s1-constraint-bindings.apk`. La build precedente di
rimozione vincoli è conservata come `InventorXrSo-s1-constraint-delete.apk`.
Il nuovo reader e le associazioni rimangono sperimentali, senza esito CAD;
nessun PASS storico viene esteso a queste aggiunte.
La verifica successiva di Sposta/Copia/Ruota è conservata in
`unity-transforms.xml`, `bridge-transforms.trx` e relativi log/build.
`transforms-manifest.json` identifica sorgenti, assembly della sonda e APK.
La nuova sonda include curve native, punto e testo, confronto di campioni
geometrici, raggio, testo/rotazione, conservazione degli originali nelle copie
e delle identità nelle modifiche, rifiuti di riferimenti/opzioni errati e
rollback esatto dell'anteprima. I casi con fissaggio confrontano l'handler
con la stessa API chiamata direttamente in una transazione annullata.
Si tratta di asserzioni compilate, ancora NON ESEGUITE: non estendono i PASS
CAD dell'8 ottobre. Il wrapper accetta `-Variant transforms` e scrive un
rapporto distinto `native-transforms-probe.json`.
L'APK delle trasformazioni è conservato come `InventorXrSo-s1-transforms.apk`.
Il successivo `transforms-guarded31-manifest.json` conserva sorgenti, assembly
effettivamente eseguiti, test 733/1.247/750, APK con guardie e report completo
con 16 esecuzioni native e otto scenari copia rifiutati. Non promuove Copia o
Ruota testo: i rispettivi rifiuti restano limitazioni temporanee. I report
diagnostici `transforms-native-text-override31.json` e
`transforms-native-text-normalized31.json` non certificano una rotazione
riuscita; il primo etichettava PASS soltanto rifiuti equivalenti e il secondo
si è fermato sull'override nativo. Le prove dei glifi e del workaround sono
conservate separatamente e restano fallimenti di orientamento.
I manifest `constraints-complete31-manifest.json` e
`dimensions-cleanup31-manifest.json` identificano separatamente le sonde
successive, i rispettivi assembly realmente eseguiti e le dipendenze delle
quote. Non sostituiscono i manifest storici e non sommano casi ripetuti come
copertura aggiuntiva. Nessun test Quest o gate completo è chiuso da queste
prove native.
La verifica successiva di Offset usa `core-offsets.trx`, `bridge-offsets.trx`,
`unity-offsets.xml` e relativi log/build, identificati da `offsets-manifest.json`.
Il primo run Unity ha rilevato un identificativo errato nel nuovo test del
filtro; corretto il test, il run completo ha passato tutti i 742 casi.
La sonda compilata copre cinque configurazioni (linea, cerchio, spline,
catena manuale e automatica), due modalità, due lati e vincoli attivi/inattivi
su parte e lamiera. Confronta il risultato dell'handler con una chiamata API
diretta annullata, conservazione delle curve originali, rollback esatto e
due aggiornamenti sorgente con identità stabili. Normalizza solo gli ID
temporanei per il confronto delle due creazioni, mantenendo geometria,
tipi, vincoli e associazioni; il controllo delle identità dopo gli aggiornamenti
usa gli ID reali. `offsets-native-movement31.json` passa 32 casi su parte e
si ferma sulla sorgente spline. Il confronto dei soli punti di visualizzazione
varia di circa `8.95e-7 mm`: per la conservazione delle sorgenti la sonda usa
ora una tolleranza numerica di `1e-5 mm`, mantenendo identità, flag, topologia
e dimensioni degli array esatte. Il confronto del risultato con l'oracle
nativo e quello del rollback restano esatti. I primi tentativi
`offsets-native-live31.json` e `offsets-native-trace31.json` si fermano
nell'oracle prima dell'handler: la sonda pretendeva una traslazione tangenziale
della linea che `MoveSketchObjects` non produce. L'asserzione ora richiede
una variazione finita e reale di ogni curva sorgente, mantenendo il confronto
completo del risultato risolto con la stessa API nativa.
`offsets-native-spline-coded31.json`, con filtro esplicito `spline` nel rapporto,
passa 16 scenari su parte/lamiera: otto creazioni con due aggiornamenti e
otto rifiuti geometrici equivalenti all'API diretta, senza offset creato.
Questi ultimi verificano anteprima e applicazione con rollback esatto e
`step_code=API_ERROR`; l'handler conserva l'HRESULT nativo nei dettagli.
Il run senza filtro `offsets-native-complete31.json` passa tutti gli 80
scenari, 40 per contesto: 72 creazioni con due aggiornamenti e otto rifiuti
nativi equivalenti, fixture chiuse e stato precedente ripristinato.
Il manifest distinto `offsets-live31-manifest.json` identifica sorgenti,
assembly effettivamente eseguiti e rapporti; non si sommano i casi ripetuti
dei rapporti diagnostici. Il wrapper
`-Variant offsets` usa il rapporto distinto `native-offsets-probe.json`.
La build Offset è conservata come `InventorXrSo-s1-offsets.apk`.
Le verifiche successive delle serie sono identificate da `patterns-manifest.json`,
`core-patterns.trx`, `bridge-patterns.trx` e `unity-patterns.xml`, con i log delle
build. `NativePatternProbe` prepara serie rettangolari a una/due direzioni e
circolari, associative/statiche, fitted/passo e simmetriche/non simmetriche;
la direzione naturale è invertita nei casi simmetrici. I 48 scenari compilati
confrontano la chiamata API diretta con creazione, anteprima e due aggiornamenti
del raggio sorgente e dei conteggi, più eliminazione per le serie associative.
I primi tentativi non hanno eseguito casi CAD: `offsets-native-interop31.json` conserva
il fallimento COM `0x80080005` dopo l'autorizzazione all'avvio, prima delle fixture.
L'avvio normale successivo ha creato il processo 14684, ma il collegamento alla
sola istanza esistente è terminato con `MK_E_UNAVAILABLE` (`offsets-native-normal31.json`).
L'utente ha poi aperto normalmente Inventor (PID 44532): la sonda si collega
all'istanza esistente. Nessun processo Inventor è stato terminato o riavviato.
`previous_restored=true` in questi rapporti significa che nessun documento
precedente è stato acquisito; non prova chiusura dell'applicazione o prove CAD.
Le sonde successive `patterns-native-*.json` contengono esecuzioni CAD reali
su fixture dedicate. La prima ha rilevato che riassegnare una definizione
copiata non permette di aggiornare il conteggio; l'handler modifica ora i
`Parameter` nativi per aggiornamenti di soli conteggi/distanze/angoli.
L'oracle rigenera il documento e modifica una quota radiale sorgente:
assegnare direttamente il raggio viene annullato dal risolutore associativo.
`patterns-native-driving-fixed31.json` registra cinque casi completi su parte
e una divergenza nel confronto della creazione associativa simmetrica:
l'oracle impostava anche i flag Y pur non selezionando una seconda direzione.
Allineati i default API, `patterns-native-aligned31.json` passa tutti i 48
scenari su parte e lamiera. Fixture chiuse e stato precedente ripristinato;
nessun documento utente era aperto. L'assembly della sonda effettivamente
eseguito è conservato come `S1LiveProbe-patterns-aligned31.dll`.
I rapporti precedenti restano conservati come diagnostica. Il rapporto
`patterns-native-driving31.json` deriva da un binario precedente dopo un errore
di compilazione: escluso dall'accettazione. La matrice non copre tutte le
combinazioni indipendenti di direzioni, blocchi, boundary, editing dei flag,
espressioni, trasporto e Quest: i relativi sottocasi restano aperti.
Le prove native caricano l'assembly appena compilato fuori processo: non
certificano trasporto HTTP, add-in distribuito, storico o input Quest.

Ripetizione delle prove con Inventor 2027 aperto:

```powershell
& ./scripts/run-s1-native-probe.ps1
# Solo quando Inventor è chiuso: istanza temporanea creata e chiusa dalla sonda.
& ./scripts/run-s1-native-probe.ps1 -Isolated
```

Il wrapper compila il tier sperimentale senza distribuirlo. Il rapporto
include stato prima/dopo del documento precedente; controllare sempre anche
`previous_restored`, oltre all'esito delle prove.

Lettura interop offline del 10 ottobre, senza avviare Inventor:
`artifacts/s1-verification/interop-modify-methods.json` conserva firme e
parametri per i tipi di schizzo esaminati. `PlanarSketch.MoveSketchObjects`
e `RotateSketchObjects` espongono `Copy` e `RemoveConstraints`;
`OffsetSketchEntitiesUsingDistance/Point` espongono inclusione della catena e
creazione dei vincoli offset. È soltanto evidenza della superficie tipizzata,
non dell'esecuzione o dell'equivalenza desktop. Nei tipi esaminati non sono
state trovate firme Trim/Extend/Split/Scale: la strategia per questi comandi
resta da provare e non viene sostituita con movimento di punti o stile linea.

Controllo esteso dell'11 ottobre sull'interop `31.0.19200.0` registrata:
`interop-topology31-methods.json` enumera i metodi pertinenti dei tipi
schizzo/curva/evaluator. Espone `BSplineCurve2d.Split`/`BSplineCurve.Split`
su geometrie transitorie e `SketchSpline3D.Split` sullo schizzo 3D; non
risolve Taglia/Estendi/Dividi 2D con quote, vincoli e dipendenze. La superficie
di [SketchLine 2027](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchLine.htm)
conferma l'assenza di un metodo dedicato per queste operazioni sulla linea.
Ricostruzione e trasferimento delle relazioni richiedono ancora una strategia
e un confronto desktop: questo controllo offline non chiude S1-09.

Verifica interop successiva: il file di `Extensions 2027/Framework/Interop`
riporta versione assembly `29.0.0.0`, mentre la `Bin` dell'installazione 2027
contiene `31.0.19200.0` e i tipi delle serie di schizzo introdotti nel 2025.1.
`bridge/Inventor2027.Interop.props` risolve la `Bin` dal registro, conserva
l'override `InventorInteropDir` e rifiuta versioni diverse da 31.x. È condiviso
fra add-in SO 2027 e sonda S1. Il test di build con override verso la versione
29 fallisce intenzionalmente; log `old-interop-refusal.log`. Non prova che la
versione vecchia causasse gli errori di attivazione COM, che persistono anche
con la sonda compilata contro la versione 31.

## Fattibilità di Scala — primitive native, non equivalenza desktop

Il 10 ottobre `scale-api-strategies31.json` raccoglie **144 osservazioni**:
nove fixture × due strategie (`SketchPoint.MoveTo` oppure
`MoveSketchObjects` sui singoli punti) × risoluzione differita sì/no ×
fattori 0,5/2 × parte/lamiera. Le destinazioni sono congelate prima di ogni
prova, attorno alla base (5, 6) mm; i punti condivisi vengono deduplicati.
Per cerchi/ellissi vengono modificati anche i raggi nativi, senza ricreare
le curve. Ogni prova viene annullata, verificando il numero di schizzi;
conteggi dei vincoli e stato `DeferUpdates` restano invariati.

Ottanta osservazioni corrispondono alla scala uniforme sui campioni CAD:
linea libera, due linee con estremo condiviso, cerchio, ellisse e spline a
vertici di controllo. Le altre 64 non corrispondono: arco, spline
interpolante, linea fissata e linea quotata. Otto osservazioni sulla linea
fissata producono anche un errore nella lettura della geometria risultante,
conservato nel rapporto prima del rollback. Differire il solver e cambiare
la primitiva di movimento non risolvono questi casi. Il primo run
`scale-api-native31.json` si era interrotto proprio su tale errore; resta
diagnostico. Il run intermedio di 72 osservazioni prova solo `MoveTo`.

Il `PASS feasibility collection` certifica raccolta e cleanup, **non Scala**.
Non vengono esposti handler, capability o azioni XR nuovi. I cinque campioni
lungo ogni curva e il confronto del raggio del cerchio sono una verifica di
fattibilità; non certificano dipendenze, selezione parziale, opzioni sui
vincoli o oracle desktop. Servono una strategia differente per gli archi e
le spline interpolanti e controlli prima di mutare geometrie vincolate.

La documentazione API conferma che
[MoveTo può fermarsi entro i vincoli](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchPoint_MoveTo.htm),
che [DeferUpdates differisce la risoluzione](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/PlanarSketch_DeferUpdates.htm)
e che [SketchArc.Radius è di sola lettura](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchArc_Radius.htm).
Il comportamento delle primitive non viene equiparato al comando Scala.

`previous_restored=true`; la lettura indipendente
`scale-post-run-state31.json` conferma documento attivo vuoto e zero
documenti/fixture aperti. Build della sonda senza warning/errori, nessuna
DLL distribuita o APK modificata. Sorgenti/assembly effettivamente eseguiti,
rapporti, log e stato finale sono identificati da `scale-api31-manifest.json`.

## Gate e lavoro restante

Incremento minimo/default spline, 11 ottobre: quattro creazioni nel desktop
installato (interpolante/CV × Parte/Lamiera) terminano con due punti tramite
doppio click sul secondo. Screenshot e readback `spline-desktop-*31` confermano
gli oggetti nativi e due punti, senza conversione in linee. Le interpolanti
create nei due contesti usano 26369; il menu osservato mostra la prima voce
«Norme», AutoCAD ed Energia minima. Le due modifiche desktop lette confermano
rispettivamente AutoCAD=26371 ed Energia minima=26370. Questi campioni non
certificano chiusura, tensione, maniglie o editing completo.

Editor XR condiviso e bridge ora accettano 2–64 punti. Lo snapshot pubblica
`spline_fit_method` dalla preferenza Inventor; il Core lo congela come
`options.fit_method` nella bozza, preservandolo anche dopo refresh della
preferenza. Il bridge richiede il metodo esplicito per le interpolanti:
non rilegge un'impostazione globale durante Applica. Se il backend non
pubblica la preferenza, il client non abilita la spline interpolante; le CV
restano indipendenti. I valori sono interi dei tre metodi installati,
non stringhe o enum sconosciuti.

Verifica finale: 804 Core, 971 Unity EditMode e 1.247 bridge PASS. I quattro
nuovi casi Unity coprono due famiglie nei due workspace, minimo, rimozione
ultimo punto e input penna/numerico; gli undici Core coprono congelamento,
refresh e validazione della preferenza. Il primo run Unity aveva due
aspettative ancora legate al minimo di tre punti; corrette e conservate
come diagnostico. Il run intermedio di 971 precede il congelamento del metodo;
quello finale è `unity-spline-frozen-method31.xml`.

Quaranta casi finali API/batch (`spline-frozen-part-batch31.json` e
`spline-frozen-sheet-retry-batch31.json`): fit/CV, 2/3/4/5/64 punti, due
orientamenti, Parte/Lamiera. Verificano metodo nativo richiesto, punti,
geometrie/vincoli, preview abortita e commit. Diciotto rifiuti verificano
0/1/65 punti e metodo mancante/stringa/sconosciuto, con snapshot e revisione
identici dopo il rollback. Il primo tentativo finale Lamiera si è fermato
alla sottoscrizione eventi COM (`RPC_E_CANTCALLOUT_ININPUTSYNCCALL`), prima
dei casi: conservato come diagnostico, seguito da lettura della fixture e
nuova esecuzione riuscita. Le matrici intermedie non si sommano ai 40 casi
finali. `spline-frozen-final-state31.json` conferma zero documenti aperti.
Il limite superiore di 64 resta un budget S1, non un limite desktop provato.
Chiusura, editing, vincoli misti CV/poligono, Quest e tutti i gate restano aperti.

Sonda opzioni spline, 11 ottobre: `spline-options-part31.json` e
`spline-options-sheet31.json` raccolgono 48 creazioni native annullate,
24 per contesto. Spline interpolanti: 2/3/5 punti, aperte/chiuse e tutti e
tre i valori `SplineFitMethodEnum` installati (26369/26370/26371).
Spline CV: 2/3/5 punti, senza/con ripetizione del primo punto in coda.
Tutte le creazioni sono leggibili dopo Solve/Update; il metodo interpolante
letto coincide con quello richiesto. `Closed=true` per le interpolanti e
la ripetizione del punto per le CV producono chiusura nativa e un punto
aggiuntivo, coincidente con il primo. L'audit separato dei rapporti verifica
48 risultati, flag, conteggi, metodi, estremi coincidenti e ripristino del
numero di schizzi dopo ogni Abort. La lettura finale indipendente conferma
zero documenti e nessuna fixture aperta.

Questa è evidenza sulle primitive API, non equivalenza con il comando
desktop: il minimo desktop, i nomi/default delle opzioni, la geometria lungo
la curva, le maniglie, l'editing e le dipendenze devono ancora essere
confrontati. Al momento di quella sonda il minimo XR era ancora tre punti; il successivo incremento sopra lo porta a due. In quella ricerca nessuna capacità di produzione
è cambiata e nessun nuovo APK è stato generato o installato. La sonda e il
wrapper espongono lo stage riproducibile `arc-ui-spline-options`, da usare
solo sulla fixture dedicata preparata con `arc-ui-prepare` oppure
`arc-ui-prepare-sheet`, seguita da `arc-ui-cleanup`.
Sorgenti, assembly eseguiti, rapporti e build sono congelati in
`spline-options31-manifest.json`. Tutti i gate S1 restano aperti.

Prima ondata parallela, 11 ottobre: introdotti i moduli Core C02-A
(intersezioni segmenti/cerchi/archi finiti), D04-A (layout annotazioni vincoli)
e F03-A (contratto/validazione metadata importabili). Suite finale **906 Core
PASS**, inclusi 102 nuovi casi; **971 Unity EditMode PASS**, processo concluso
con codice zero. Il primo run Core conserva quattro fallimenti diagnostici,
corretti senza ampliare le tolleranze geometriche. Nessun nuovo comando CAD,
integrazione renderer o importatore viene certificato da questi test.
Il [rapporto dell'ondata](s1-parallel-wave32.md) distingue risultati,
dipendenze e integrazioni residue. Assembly verificati conservati; nessun
nuovo APK generato/installato. `parallel32-final-state.json` conferma zero
documenti Inventor aperti. Tutti i gate complessivi restano aperti.

Scelta metodo spline, 11 ottobre: l'editor Parte/Lamiera espone Norme
(26369), Energia minima (26370) e AutoCAD (26371) durante la raccolta punti.
Default dalla preferenza dello snapshot, conservazione punti, blocco durante
input numerico, opzione congelata nella bozza e annullamento senza trascinare
la scelta al nuovo comando. Le CV non inviano il metodo. **987 Unity PASS**,
16 nuovi casi; primo diagnostico con tre fixture Lamiera senza scena
aggiornata dopo cambio revisione, corrette prima del run finale. **36 casi
nativi API/batch PASS**, tre metodi × tre conteggi × due rotazioni × due
contesti, con confronto esatto, rollback e commit. Zero documenti finali.
Core 906 e bridge 1.247 restano le verifiche precedenti: nessun sorgente di
produzione Core/bridge modificato in questo incremento. Il
[rapporto dedicato](s1-spline-method-choice.md) separa prove e limiti;
chiusura/editing spline, caso misto CV/poligono, runner Quest e tutti i gate
complessivi restano aperti.

Tutti i gate S1 restano aperti fino al completamento dei relativi sottocasi.

Immagini native, 14 ottobre, incrementi 51–52: query sperimentale PNG con
asset autenticato, frame UV dai ruoli dei bordi e layer Unity condiviso da
Parte/Lamiera. **64 casi nativi** confrontano colori e orientamento dopo
trasformazioni; **sei casi SaveCopy/riapertura** conservano bitmap/frame e
preservano l'originale. Suite 51: 1.614 bridge PASS (un SKIP), 1.078 Core
PASS, 1.050 Unity generali più sette immagini in run separati. Manifest e
DLL installati ripristinati; processi dedicati terminati. Nessuna nuova APK
installata. [Rendering e limiti](s1-native-image-rendering51.md),
[persistenza nativa](s1-native-image-savecopy52.md). HTTP con Inventor reale,
Quest, trasparenza, linked e cronologia OLE restano aperti; questi incrementi
non chiudono alcun gate complessivo.

Incrementi 53–55, 14 ottobre: il percorso HTTP autenticato con Inventor reale
è passato su Parte/Lamiera, confrontando i sei colori PNG e il frame corrente;
le APK QA/ordinaria 53 sono compilate e conservate, **non installate**.
Il successivo reader 54 conserva la proprietà nativa esatta dei bordi immagine
e rifiuta 38 tentativi di modifica strutturale, con rollback e immagini
preservate. Il ciclo PC 55 di 1.000 sincronizzazioni dello stesso snapshot
immutabile passa da 1.025.994 a zero eventi di allocazione osservati; non è
una misura del frame completo o del budget Quest/GPU. Evidenze e limiti nei
[rapporti HTTP](s1-native-image-http53.md),
[struttura](s1-image-structure54.md) e [refresh](s1-image-refresh55.md).
L'[audit corrente dei 77 comandi](s1-current-command-status56.md) distingue
implementazione parziale, ricerca API e prove di prodotto: **18 gate OPEN**.

Incrementi 56–58: il [contratto strutturale](s1-image-completeness56.md)
consente Anteprima/Applica sulle altre geometrie con immagini opache di
sfondo protette, verificato via HTTP/Core e add-in reale su Parte/Lamiera.
Passano 1.150 Core, 1.615 bridge e 1.066 Unity; il
[lifecycle 58](s1-image-lifecycle58.md) esercita disattivazione/distruzione
effettive nel Play Mode del PC. La [ricerca Skeleton 57](s1-skeleton-native57.md)
verifica definizioni/istanze parametriche, 14 Abort esatti, quattro commit e
due SaveCopy; non implementa ancora gli strumenti Skeleton nel prodotto.
DLL/manifest installati preservati; nessuna nuova installazione Quest.

Testo, incrementi 59–60: [ricerca nativa](s1-native-text-research59.md)
con matrice estesa 16 PASS/sei FAIL e persistenza separata verificata su due
copie, quattro aggiornamenti dei parametri e originale preservato. La
[fondazione backend 60](s1-native-text-foundation60.md) passa otto
anteprime/otto commit, quattro aggiornamenti sorgente con confronto dei
glifi e 14 rifiuti via HTTP/Core/add-in reali su Parte/Lamiera. La
rigenerazione necessaria per i nuovi legami avviene nella transazione
posseduta. Passano 1.631 test bridge. Le capacità testo restano nascoste:
renderer ed editor XR, testo su geometria, opzioni complete e collaudi
restano aperti; nessun nuovo gate complessivo chiuso.

Glifi, incrementi 61–62: [36 catture native](s1-native-text-glyph-frames61.md)
stabiliscono coordinate modello già ruotate e RangeBox non ruotato.
Il [renderer 62](s1-native-text-rendering62.md) porta i triangoli nativi nella
vista corrente e nella preview, con validazione Core e risorse GPU possedute.
Passano 1.163 Core, 1.631 bridge e 1.070 Unity; percorso HTTP nativo verificato
su Parte/Lamiera. Editor testo completo, selezione, testo su geometria e
Quest restano aperti; nessuna nuova APK o chiusura di gate complessivo.

Selezione testo, [incremento 63](s1-native-text-selection63.md): il reader
esporta il rettangolo ruotato in coordinate locali; Core esegue pick e
finestre anche negli spazi fra i glifi senza inventare sottoparti CAD.
Otto verifiche HTTP/native Parte/Lamiera a 0/90/180/270° confermano limiti,
identità selezionata e letture senza mutazione. Passano 1.177 Core,
1.643 bridge e 1.070 Unity. Evidenze congelate; editor completo, runner
Quest e prove fisiche restano aperti, con tutti i 18 gate complessivi OPEN.

Ricerca [64](s1-native-text-rotation-research64.md): RotateSketchObjects
non ruota i glifi del TextBox in 12 casi, con 12 Abort esatti. Il blocco
della rotazione testo resta attivo. Fondazione [editor testo66](s1-native-text-editor66.md):
14 test UI mirati e 1.084 Unity complessivi PASS; composer e bozze
fitted/rettangolo/update/delete presenti ma subordinati alle capacità
backend ancora nascoste. [Parametri67](s1-parameter-contracts67.md): mapper
bool/testo verificato con get/list HTTPS nativi Parte/Lamiera, builder Core
limitati per espressioni e operazioni esistenti; 1.190 Core e 1.661 bridge PASS.
UI Parametri completa, opzioni testo complete, runner e dispositivo restano
aperti; nessuna nuova APK o chiusura dei 18 gate complessivi.

[Copia isolata65](s1-native-copy-isolation65.md): una linea libera copiata
in Parte e Lamiera, ciascuna in un processo dedicato, supera quattro edit
successivi complessivi e due Abort esatti. Journal persistenti distinguono
API di copia, enumerazione, Solve, Update e Abort. La copia mista originale
e l'associatività non sono provate; il blocco produttivo resta attivo.

[Editor Parametri68](s1-native-parameter-editor68.md): espressioni/CRUD in
Progettazione con 1.104 Unity PASS e callback revocati al cambio contesto;
restano aperti Parametri dentro lo schizzo e UI Lamiera. [Workflow69](s1-native-parameter-workflows69.md):
dieci preview/commit reali, quattro aggiornamenti di consumatori
linea/quota/testo/glifi e otto rifiuti nativi PASS. [Crash70](s1-native-text-copy-crash70.md):
un singolo TextBox fitted termina il processo dedicato durante Copy=true;
Abort impossibile, processo utente e installazione preservati. È un FAIL
nativo conservato, non un gate superato; il blocco della copia resta attivo.

Il 14 ottobre il Quest è tornato connesso via USB, ma addormentato al
controllo: hash dell'APK installata identico all'ordinaria38. Nessuna nuova
APK installata e nessuna nuova prova Quest oltre quelle già registrate38.
[Ricerca71](s1-native-text-open-api71.md) conserva i riferimenti Autodesk
per i percorsi di rotazione/testo su geometria ancora da completare.

[Clonazione72](s1-native-text-constructor-clone72.md): il costruttore nativo
del testo fitted supera Parte e Lamiera, quattro Abort esatti, quattro
spostamenti della copia e quattro aggiornamenti parametrici con oracle dei
glifi. Non usa `MoveSketchObjects.Copy=true`; non è ancora integrato nel
comando Copia. Il profilo provato resta single-line, rotazione zero e stile
locale esplicito; non certifica tutte le varianti del testo.

Il 15 ottobre il bootstrap QA73 supera 1.247 test Core e il runner74 supera
1.107 test Unity EditMode, senza fallimenti o test saltati. L'host dedicato73
supera una prova nativa di apertura fixture Parte/Lamiera, verifica della DLL
caricata, bootstrap privato monouso, ispezione readonly e chiusura normale
del solo processo dedicato. Una precedente prova fallita è conservata con
recupero esplicito del processo di fixture: non è una prova Quest. Manifest,
DLL installata e identità del processo Inventor personale sono preservati.
La [QA74](s1-quest-text-parameters-runner74.md) e l'ordinaria74 sono compilate;
i tentativi75 hanno isolato errori di avvio nel wrapper, nel percorso Android
del bootstrap e nella firma JNI di removeExtra, prima dei casi CAD. I
fallimenti e i ripristini sono conservati; non sono nuove prove Quest PASS.
La correzione78 supera il bootstrap negativo e positivo sul Quest. Nel
[collaudo75 fino a r8](s1-owned-quest-acceptance75-78.md) passano dodici
sottocasi (otto linea, due testo readonly, due parametri); r6 fallisce sui
glifi dopo Apply20. La [rigenerazione80](s1-parameter-glyph-regeneration80.md)
corregge la prima lettura nativa Parte/Lamiera20/35 e supera regressioni
native con rollback, incluso errore dopo rigenerazione. Quest/r8 supera il
controllo immediato20, poi fallisce offline con rate limiting osservato:
R9 supera20→35 ma fallisce su un confronto numerico esatto nel CRUD. La
[QA81/r11](s1-native-parameter-quest81.md), con tolleranza1e-6mm e pause
fra fasi, termina con22 sottocasi PASS e cleanup interamente verificato:
copre anche parametri dentro lo schizzo Parte/Lamiera. Tutti i18 gate
complessivi restano aperti.

[Parametri dentro lo schizzo76](s1-sketch-parameter-editor76.md): pannello
condiviso Parte/Lamiera, bozze tipizzate e vincolo alla revisione, con 1.253
Core e1.119 Unity PASS. Il sottoinsieme nativo/Quest81 verifica lettura,
Cancel, Apply20→35 e CRUD inutilizzato nei due contesti; Apply Boolean/Text,
altre opzioni e proprietà Units esistente restano da verificare. Le unità
esistenti sono readonly, senza setter simulati.

[Arco centro/punto82](s1-arc-center-http82.md):16 workflow integrati
HTTPS/Core Parte/Lamiera e14 rifiuti con rollback rappresentato verificato.
Conferma rendering sintetica PC, confronto geometrico analitico: restano
oracolo desktop/nativo indipendente, due editing del raggio, persistence
e Quest. Arco tangente non è implementato da questa prova.

[Arco tangente83](s1-tangent-arc-http83.md): backend sperimentale con
endpoint condiviso e vincolo tangente nativo, 1.679 bridge PASS e uno skip
preesistente. La matrice PC HTTPS/Core supera16 casi Parte/Lamiera e14
rifiuti con rollback rappresentato; un'ispezione nativa indipendente
conferma geometria, chiavi degli endpoint condivisi e binding dei vincoli.
Installazione ripristinata e processo personale preservato. Altre famiglie
di curve accettate, target nativi opzionali, aggiornamenti parametrici,
persistenza, parità desktop e Quest restano aperti: azione XR indisponibile
e nessun gate complessivo chiuso.

[Bozza Arco tangente84](s1-tangent-arc-editor84.md): riferimenti tipizzati
obbligatori e attuali, endpoint libero o agganciato a un punto nativo e
riconferma esplicita integrati nel Core. Suite1.275 Core PASS,22 nuovi casi.
La UI è ancora una bozza separata, non integrata o collaudata; nessuna nuova
APK e nessuna nuova evidenza Quest.

[Dipendenze/persistenza Arco tangente86](s1-tangent-association-persistence86.md):
sei checkpoint nativi Core/HTTPS Parte/Lamiera, quattro aggiornamenti
parametrici e due SaveCopy/reopen riusciti. Geometria analitica, punti
condivisi e tutte nove le identità dei vincoli conservate, quattro IPT
archiviate con hash e installazione ripristinata. Profilo sorgente linea
con target nativo fissato; il [difetto spline85](s1-tangent-spline-investigation85.md)
resta aperto. Quest rimandato dall'utente, UI tangente ancora indisponibile;
nessun gate complessivo chiuso.

[Contratto maniglie spline87](s1-spline-handle-contract87.md): reader e guardie
distinguono la maniglia dalla linea; ownership tramite slot locali validata
anche nelle preview.1.289 Core e1.680 bridge PASS. La prima lettura nativa
è ora completa; i tentativi r10–12 restano FAILED e isolano un secondo arco
di costruzione aggiunto dall'attivazione della maniglia. Classificazione
strutturale degli oggetti ausiliari e matrice nativa completa ancora in corso.
Installazione ripristinata; Quest rimandato e tutti18 gate aperti.

[Fondazione Copia88](s1-copy-foundation88.md): preflight dell'intera selezione,
traslazione di linee libere e ricostruzione nativa del testo letterale fitted
con stile locale. Variante sperimentale non pubblicata;1.756 bridge PASS e
build ordinaria/sperimentale senza warning. I tentativi r3–4 verificano tre
workflow e cinque rollback esatti nella Parte, poi falliscono alla prima
lettura Lamiera per revisione cambiata durante l'inizializzazione. Conservati
come FAILED, con cleanup normale e installazione ripristinata. La matrice88
passa nel run r6 sul candidato p2: sei workflow Parte/Lamiera,
otto coppie COM e dieci rifiuti con venti checkpoint nativi esatti. Verificatore
degli artefatti PASS; anche r7 passa separatamente sul candidato p3 con
l'ultima guardia del testo e cleanup normale. Dipendenze, persistenza e opzioni residue ancora aperte;
nessuna nuova prova Quest e nessun gate generale chiuso.

[Sonda lifecycle Copia89](s1-copy-lifecycle89.md): due edit nativi delle
sorgenti per documento e SaveCopy/chiusura/riapertura, dopo l'intera matrice88
nello stesso processo. Il primo tentativo fallisce sul confronto WidthScale
della fixture, con Abort completo esatto e cleanup normale. Il run r6 passa
quattro edit delle sorgenti e due riaperture native Parte/Lamiera, con copie
invariate e quattro IPT archiviati. È accettata soltanto la relocation nativa
verificata del percorso nel campo parametro non copiato. Verificatore
indipendente PASS, installazione ripristinata. Letture Core/HTTPS dopo edit
e riapertura ancora da verificare nel nuovo percorso90; nessuna prova Quest.

[Prima lettura Core Copia90](s1-copy-core-lifecycle90.md): r1 FAILED conservato,
confronto delle sole annotazioni intrinseche corretto e verificato offline.
R2 PASS sullo stesso candidato p3: quattro modifiche sorgenti, due riaperture,
sei prime snapshot Core e quattordici fasi/ack. Verificatore indipendente PASS,
sette alterazioni rifiutate, quattro IPT archiviati e cleanup normale.
Quest rinviato dall'utente e tutti18 gate complessivi ancora aperti.

F1 ha tre attività software complete nel piano; F0, F2, F3, F4 e F5 hanno evidenze parziali.

Restano: matrice opzioni/dialoghi e oracle desktop completi; budget e benchmark;
selezione/sottoparti e input XR completi; editing e inferenze del poligono; varianti
Crea mancanti; modifiche geometriche native di entità esistenti; completamento di vincoli, espressioni e quotatura automatica;
opzioni Formato/Parametri residue; Modifica, Serie e proiezioni associative; importazioni e
contesti avanzati; storico; runner dedicato e prove Quest. I 38 casi di creazione
native non chiudono i gate di equivalenza geometrica/topologica/parametrica:
servono confronti dell'oggetto nativo e un secondo aggiornamento delle dipendenze.
