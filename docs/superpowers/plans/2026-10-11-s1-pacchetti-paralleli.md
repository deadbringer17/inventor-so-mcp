# S1 — Pacchetti di lavoro paralleli

11 ottobre 2026. Piano operativo aggiuntivo alla [specifica S1](../specs/2026-10-06-schizzo-parita-inventor-2027-design.md)
e al [piano F0–F7](2026-10-06-schizzo-parita-inventor-2027.md).
Il perimetro dei 77 comandi e i gate S1-01…18 restano integrali.

Questo piano divide il lavoro residuo, senza dichiarare complete le funzioni
già parzialmente presenti. Lo stato verificato resta nel [verbale](../../xr-sketch-2027-verification.md).
Ultimo incremento: spline 2–64 punti e metodo congelato, 804 Core/971 Unity/
1.247 bridge, 40 casi nativi e 18 rifiuti; APK compilata, non installata.

Prima ondata parallela verificata: C02-A/D04-A/F03-A, 102 nuovi casi Core;
suite finale 906 Core e 971 Unity PASS. Moduli ancora da integrare nei comandi
e nell'interfaccia, nessun nuovo APK. Dettagli e limiti nel
[rapporto dell'ondata](../../s1-parallel-wave32.md); gli stati dei pacchetti
complessivi sotto restano invariati.

Incremento B02 successivo: scelta dei tre metodi interpolanti integrata nella
raccolta punti Parte/Lamiera, default nativo e opzione congelata nella bozza.
987 Unity PASS (16 nuovi casi), 36 confronti API/batch Parte/Lamiera PASS.
Chiusura/editing e il pacchetto B02 complessivo rimangono parziali. Dettagli nel
[rapporto del metodo spline](../../s1-spline-method-choice.md).

Seconda ondata verificata: C02-B (intervalli Taglia/Dividi), D04-B1
(etichette planari misurabili) e F03-C1 (deposito risorse con lease).
Suite finali 941 Core, 1.268 bridge e 1.000 Unity PASS, 69 nuovi casi.
Corrette deduplicazione circolare, separazione dei metadati CLR e normale
del piano a scala ridotta. Moduli ancora da integrare nel prodotto, nessun
nuovo APK; [rapporto dell'ondata](../../s1-parallel-wave34.md). Stati dei
pacchetti complessivi e tutti i gate S1 invariati.

Terza ondata verificata: D04-B2/B3 integrato nel renderer e nella selezione
Parte/Lamiera, F03-C2 trasporto HTTP opt-in con inspector finto nei test.
1.024 Unity e 1.285 bridge PASS, 41 nuovi casi; Core invariato a 941.
C02: 60 osservazioni API Add/Delete, 24 controlli completati e 36 divergenze
sui centri, tutti i rollback esatti. [Rapporto e residui](../../s1-parallel-wave35.md).
I pacchetti complessivi e i gate mantengono lo stato aperto.

Quarta ondata verificata il 12 ottobre: D04 filtri per tipo e diagnostica
integrati, F03 inspector BMP reale e ricerca sulle immagini native, C02
grafo esplicito dei centri con due aggiornamenti. 1.030 Unity/1.337 bridge
PASS, 58 nuovi casi; Core invariato a 941. Su 24 casi C02 otto completano
i controlli e 16 divergono, tutti con rollback esatto. Sedici immagini
completano i controlli API: Abort lascia OLE senza filename, ripristino
esatto ottenuto solo con pulizia esplicita delle identità create da Add.
Zero documenti finali, APK cumulativa archiviata e non installata.
[Rapporto e residui](../../s1-parallel-wave36.md); pacchetti e gate aperti.

Quinta ondata, 12 ottobre: preview PNG delle risorse BMP integrata nel
servizio HTTP opt-in; 1.372 bridge PASS, 35 nuovi casi. C02: 12 osservazioni
H/V e politiche di replay, 24 aggiornamenti, rollback esatti. I getter pixel
nativi falliscono fuori processo: cattura add-in ancora da provare. F03 e
H01 hanno ora blueprint operativi d'integrazione. Nessuna nuova APK o
installazione; [rapporto e residui](../../s1-parallel-wave37.md). Tutti i gate aperti.

Sesta ondata, 12 ottobre: F03 catalogo/descrittore/preview collegati al client
e all'editor Parte/Lamiera, con download limitato e invalidazione callback.
990 Core/1.043 Unity PASS, 62 nuovi casi; bridge 1.372 PASS precedenti.
C02 otto osservazioni sulle dipendenze dalla linea intera, 16 aggiornamenti
e otto Abort esatti; necessità del replay Coincident osservata, non parità
desktop. H01 primo runner QA e fixture S1 reali: lifecycle nativo verificato,
primo subset Quest PARTIAL con otto PASS Parte/Lamiera; ripristino fixture e APK verificato. [Rapporto e residui](../../s1-parallel-wave38.md).
Pacchetti complessivi e tutti i 18 gate aperti.

Ricerca B04 successiva: 54 osservazioni native AddByFillet Parte/Lamiera,
108 aggiornamenti e 54 Abort esatti. L'espressione di raggio non crea una
RadiusDim e non segue il parametro: necessaria una quota esplicita da provare.
Nessuna capability prodotto o parità desktop dichiarata.
[Rapporto e limiti](../../s1-fillet-native-research39.md).

Incremento 40: RadiusDim esplicita segue il parametro in 36 aggiornamenti;
la matrice conserva otto casi divergenti su 24 per quote sorgenti scollegate
o eliminate, 72 aggiornamenti e 24 Abort esatti. Non è parità del Raccordo.
[Evidenze negative e limiti](../../s1-fillet-radius-policy40.md).

Incremento 41: punti planari originali e selezioni ordinate conservati nel
Core e nell'editor condiviso; coppie congelate con riferimenti versionati,
refresh/finestra senza soluzione inventata e riconferma dei soli riferimenti
bloccata quando trascinerebbe punti vecchi. **1.000 Core/1.049 Unity PASS**,
16 nuovi casi. Nessuna nuova capability/APK o prova Quest.
[Integrazione e residui](../../s1-curve-pick-contract41.md).
## Pacchetti e dipendenze

«Parziale» significa codice/prove mirate presenti, con requisiti residui.
«Da avviare» non esclude primitive o handler storici riutilizzabili: il nuovo
editor deve comunque soddisfare il contratto S1 e le prove del comando.

| ID | Pacchetto e residuo principale | Stato | Gate | Dipendenze principali |
|---|---|---|---|---|
| A01 | Inventario: 77 comandi, contesti, opzioni/default e indice di evidenze per variante | Parziale | 01 | Tutti i pacchetti aggiornano le proprie evidenze |
| A02 | Tavolozza/navigazione: dimensioni, gruppi, voce, picker, input UI isolato dal foglio | Parziale | 02/03 | Catalogo condiviso; H02/H03 per prestazioni e fisico |
| A03 | Editor/sessione: identità, sottoparti, storico, Concludi/Annulla/Torna alla lavorazione | Parziale | 04/13 | Contratti riferimenti; integrazione centralizzata |
| A04 | Resilienza: offline, stale, timeout, cambio documento, tracking e conflitti | Parziale | 14 | A03; ogni nuovo comando aggiunge casi |
| B01 | Linea continua, passaggi tangenti, cerchio/arco tangenti e inferenze persistenti | Parziale | 05 | D03; oracle desktop |
| B02 | Spline: scelta metodo, chiusura e opzioni di creazione XR | Parziale | 05 | Default congelato esistente; oracle delle opzioni |
| B03 | Spline: editing punti/maniglie, tensione e associazioni CV/poligono | Parziale | 04/05 | B02; ricerca sulle associazioni native |
| B04 | Raccordo/Smusso 2D: soluzioni, taglio e conservazione vincoli | Da avviare | 06 | Selezioni native; C02 per intersezioni |
| B05 | Testo/testo su geometria: creazione, stile, editing e dipendenze | Parziale | 06 | Input testuale; F02 per espressioni |
| B06 | Curva da equazione: grammatica, parametri, oggetto nativo e aggiornamenti | Da avviare | 05 | Ricerca API; F02 |
| C01 | Trasformazioni: Copia, Scala e opzioni residue Sposta/Ruota/Allunga | Parziale | 09 | Ricerca crash copia e divergenze scala; A03 |
| C02 | Taglia/Estendi/Dividi: intersezioni, topologia, relazioni e nuovi riferimenti | Da avviare | 09 | Motore geometrico puro; strategia API/ricostruzione verificata |
| C03 | Offset: editing e opzioni native residue, aggiornamenti associativi | Parziale | 09 | A03; oracle desktop |
| D01 | Quota generica: opzioni, espressioni/nomi XR e modifica completa | Parziale | 10 | F02; input testuale e selezioni |
| D02 | Quota automatica e diagnostica solver/DOF | Da avviare | 10/11 | Ricerca API/desktop; D01/D03 |
| D03 | Vincoli: completamento opzioni, compatibilità e inferenze | Parziale | 11 | A03; B01; D02 per diagnostica |
| D04 | Vincoli: simboli, filtri e selezione visuale mirata | Parziale | 11 | Associazioni native; nessun legame inventato se unavailable |
| E01 | Serie: definizione completa, direzioni/ruoli, istanze e soppressioni | Parziale | 08 | Strategia alternativa al Copy della definizione già fallito |
| E02 | Specchio nativo nel nuovo editor, relazioni e aggiornamento sorgente/asse | Da avviare | 08 | A03; oracle prima di scegliere algoritmo |
| E03 | Proiezioni associative: geometria, taglio, modello piatto e DWG | Da avviare | 07 | Riferimenti modello; F03 per risorse DWG |
| F01 | Formato: proprietà complete, Mostra formato e Quota non parametrica | Parziale | 12 | D01; oracle opzioni |
| F02 | Parametri: elenco, edit/add, espressioni/unità e dipendenze dal Quest | Parziale | 12 | Contratto preview documento; riuso handler da verificare |
| F03 | Importazioni: immagini, punti e ACAD, asset ID e selettore Quest | Da avviare | 12 | Contratto risorse e limiti verificati; oggetti/legami nativi |
| G01 | Scheletro: blocchi, parti/componenti e lifecycle documenti risultanti | Da avviare | 18 | Baseline contesti; A03 esteso ai documenti risultanti |
| G02 | Schizzo 3D, curva ponte e proiezione verso 3D | Da avviare | 18 | Baseline/contratti 3D separati dal reader 2D |
| H01 | Runner Quest S1 incrementale, fixture, ripristino e manifest | Da avviare | 16 | Percorsi prodotto disponibili; sottocasi aggiunti per pacchetto |
| H02 | Regressioni M6/M8/M9/M10 e benchmark CPU/GC/GPU | Parziale | 15 | Build/fixture congelate, A02 e H01 |
| H03 | Integrazione finale e prova fisica MR/Studio VR | Aperto | 17 e audit 01–18 | Versione cumulativa verificata; partecipazione dell'utente |

## Esecuzione

Fino a **tre agenti di sviluppo più un coordinatore**, limite della sessione
corrente. Un agente possiede un pacchetto circoscritto; quando serve, lo
divide in task di ricerca, contratto, implementazione e verifica. Non occorre
aprire una chat diversa per ogni task e non vengono creati goal concorrenti.

Prima ondata proposta, su file nuovi separati:

1. **C02 — Intersezioni Core.** Segmenti/cerchi/archi, ordine parametrico,
   tangenze, coincidenze, estremi e ambiguità con test geometrici indipendenti.
   È una dipendenza di Taglia/Estendi/Dividi, non quei comandi già conclusi.
2. **D04 — Modello/layout simboli.** Associazioni native esistenti, filtri,
   selezione, budget e stato indisponibile; renderer/glyph finali dopo oracle.
3. **F03 — Contratto risorse importabili.** Prima fissare i dati dalle opzioni
   native; poi validatore puro di tipo/unità/asset ID/dipendenze con limiti
   forniti da policy verificata, senza inventare i budget né esporre import
   non ancora operativo.

Il coordinatore può svolgere l'oracle e l'integrazione di B02 mentre questi
moduli procedono, e avviare H01 sui percorsi già disponibili. H01 cresce con
le funzioni: il runner non viene rimandato alla fine dell'intero sviluppo.
Le ondate successive scelgono il primo pacchetto senza dipendenze bloccanti;
ricerca e implementazione si alternano quando l'API non consente ancora una
strategia verificata. Tutti i pacchetti rimangono necessari, compresi G01/G02.

## Proprietà dei file e risorse condivise

Il coordinatore modifica in esclusiva `EditSketchHandler.cs`,
`SketchEditorXrHandler.cs`, `NativeSketchEditorUi.cs`, `NativeSketchSession.cs`,
registri/cataloghi/capability, dispatcher della sonda e verbale generale.
Gli agenti producono moduli nuovi, partial dedicati, test propri e indicano
gli agganci da integrare. Prima di ogni task si dichiarano i percorsi esatti;
due agenti non modificano lo stesso file. Modifiche di un agente sono subito
visibili agli altri nel checkout condiviso.

Inventor, documenti fixture, Unity batch/build, APK e Quest hanno un solo
operatore alla volta. I test .NET che condividono `obj` vanno in sequenza.
Nessuna modifica ad Assets/Packages mentre Unity usa il progetto. Sono
parallelizzabili lettura, codice su file distinti e preparazione delle prove;
le verifiche con risorse condivise entrano nella coda del coordinatore.

Incremento 43, F03: resolver collegato a PlanChange/AtomicBatch con lease
owner-bound e trasferimento al piano; staging chunk sigillato implementato,
adapter wire ancora da collegare. 1.451 bridge PASS, un SKIP; build 2027 e
legacy PASS, senza installazione. [Rapporto](../../s1-import-ingress-and-staging43.md).
Tutti i 18 gate restano aperti.

Incremento 44, F03: sender chunk integrato, staging add-in interno e
binding lease al batch; target fissato fra preview e commit. 1.506 bridge
PASS, un SKIP; build 2027/legacy PASS senza installazione.
[Rapporto](../../s1-import-transfer44.md). Add e rollback OLE ancora da
collegare; tutti i 18 gate restano aperti.

Incremento 45, F03: Add embedded e journal OLE del batch; 96 casi nativi
Parte/Lamiera PASS, 48 osservazioni API per isolare rotazione/ridimensionamento.
1.506 bridge PASS, un SKIP; build PASS. Variante ancora non pubblicata
nelle capability XR, senza deploy/APK. [Rapporto](../../s1-native-image-batch45.md).
Linked, rendering, Undo/Redo, Quest e tutti i 18 gate restano aperti.

Incremento 51, F03: pixel e frame nativo verificati in 64 casi Parte/Lamiera;
tool PNG autenticato e layer immagini condiviso dai due editor. 1.614 bridge
PASS (un SKIP), 1.078 Core PASS, 1.050 Unity generali e sette immagini PASS
in run separati. [Evidenze e limiti](../../s1-native-image-rendering51.md).
HTTP con Inventor reale, persistenza del frame live, Quest e gate aperti.

Incrementi 52–53, F03: sei SaveCopy/riaperture native PASS e due prove
complete MCP/HTTPS/Core PASS su Parte/Lamiera. PNG verificato su worker;
1.080 Core e 1.057 Unity PASS, due APK cumulative compilate e non installate.
[Persistenza](../../s1-native-image-savecopy52.md),
[consegna software e limiti](../../s1-image-pipeline-delivery53.md).
Selezione strutturale, editor/importazione completi e prove Quest ancora
aperti; tutti i 18 gate complessivi restano aperti.

Incremento 46, F03: payload BMP persistente sul modello, due prove
SaveCopyAs/riapertura PASS e 96 casi nativi di regressione PASS. Cronologia
PARTIAL: 32 casi/96 cicli verificano modello e Redo con payload, Undo conserva
OLE quindi baseline esatta 0/32. 1.506 bridge PASS, un SKIP.
[Rapporto](../../s1-image-persistence-and-history46.md). Tutti i 18 gate aperti,
senza installazione o APK; reader/rendering rimangono da integrare.

## Criterio di consegna di ogni pacchetto

Ogni task riporta requisito originale e gate, file posseduti, dipendenze,
contratto/input/default, risultato nativo e casi ancora non coperti.
Ogni pacchetto conserva quattro livelli separati:

1. Core/Unity/FakeAddIn: input, selezioni, invalidi, annullamento e resilienza.
2. Inventor Parte/Lamiera: oggetti/topologia/relazioni, preview/rollback/commit,
   modifiche successive e cleanup; il confronto API/API è etichettato tale.
3. Oracle desktop: opzioni/default, flusso ed esito di fixture equivalente.
4. Quest con Inventor reale, poi prova fisica quando necessaria: PASS e
   NOT COVERED per sottocaso, con log, screenshot e manifest.

«Modulo implementato» e «pacchetto completo» sono stati distinti. I gate
complessivi si chiudono soltanto dopo l'audit dei requisiti e delle evidenze.
L'APK è cumulativa: il lavoro parallelo confluisce in una build integrata;
nessuna installazione sul Quest è implicita nella preparazione dei pacchetti.

Incremento 42, F03: ownership delle risorse nei piani e controllo del budget
di trasporto implementati; 1.401 bridge PASS, un SKIP per host già attivo.
Ricerca OLE su Parte/Lamiera fallita sul riuso di sorgente linked protetta:
56 osservazioni ripristinate, due rifiuti, sei casi non eseguiti.
[Rapporto](../../s1-import-lifetime-and-ole42.md). Inserimento CAD e tutti
i 18 gate restano aperti; nessun aggiornamento APK in questo incremento.


