# inventor-so-mcp — analisi rispetto alla specifica

Data: 11 settembre 2026. Fork: https://github.com/deadbringer17/inventor-so-mcp

Baseline NeonGlay: `3809700ed2e34164f3c45bec5a50aed347448383`.
Analisi del codice, non collaudo CAD: nessuna operazione eseguita dentro Inventor.

## Valutazione

Il progetto è una base piccola e utile per modellazione parametrica di parti e lamiera. Non è ancora il bridge semantico per assiemi e produzione descritto nella specifica. La distanza principale riguarda infrastruttura, identità delle entità, interrogazione e validazione, oltre al numero di comandi CAD.

Il codice registra **36 tool MCP**, contro i 34 dichiarati nel README. Usa Python >=3.12, FastMCP, pywin32 e COM esterno, con trasporto stdio. Non contiene un add-in C#, IPC, resources MCP o sottoscrizioni. Le dipendenze hanno solo limiti minimi; non c'è un lockfile né una suite di test nel repository.

Il fork conserva il codice upstream: questa analisi non implementa la specifica, non registra il server nel client e non abilita accesso ai modelli. Il nome del repository è `inventor-so-mcp`; il pacchetto upstream rimane `inventor-mcp-server` e l'entry point `inventor-mcp`.

## Inventario verificato dei 36 tool

Fonte: [src/server.py](../src/server.py), decoratori `@mcp.tool()`, verificati anche mediante AST.

| Gruppo | Tool |
|---|---|
| Sviluppo | execute_python, reload_api |
| Ispezione | inspect, list_edges, list_faces, find_edge, find_face |
| Transazioni | transaction |
| Connessione | connect, status |
| Documenti | create_part, save_document, export_document |
| Sketch | create_sketch, draw_rectangle, draw_circle, draw_line, draw_polygon, draw_closed_profile |
| Feature | extrude, revolve, fillet, chamfer, hole, hole_linear, circular_pattern |
| Parametri | get_parameters, set_parameter, add_parameter |
| Lamiera | set_sheet_metal_thickness, sheet_metal_face, flange, sheet_metal_cut |
| Gestione feature | list_features, delete_feature, suppress_feature |

Le funzioni in [inventor_api.py](../src/inventor_api.py) sono implementazioni COM, non solamente dichiarazioni di strumenti. Ciò ne conferma la presenza, non la correttezza su ogni versione, lingua o modello. L'esecuzione arbitraria di Python non viene conteggiata come implementazione di tutte le capacità accessibili attraverso COM.

## Matrice dei punti 1–80

P = copertura parziale; A = assente come capacità dedicata; D = difforme dalla specifica. La classificazione riguarda il requisito complessivo, non nega la presenza di singoli tool funzionanti. Non si ricava una percentuale di completamento: i requisiti hanno dimensioni e difficoltà molto diverse.

| # | Area | Stato | Riscontro e divario |
|---|---|---|---|
| 1 | Obiettivo semantico | P | Modellazione parti e snapshot; manca il workflow assieme→verifica→rilascio. |
| 2 | Architettura | D | server.py→inventor_api.py→COM; nessun add-in/IPC/state manager. |
| 3 | Modalità operative | D | Nessuna policy READ ONLY/ASSISTED/AUTONOMOUS/BATCH; transazione facoltativa. |
| 4 | MCP Resources | A | Nessun resource decorator/handler; solo tool. |
| 5 | Semantic CAD State | P | inspect restituisce JSON con volume, bbox e nomi; nessun grafo assieme. |
| 6 | Persistent Entity IDs | D | Indici di facce, spigoli, sketch e feature; nessun ReferenceKeyManager. |
| 7 | Application | P | connect/status, versione nel messaggio di connessione; lifecycle incompleto. |
| 8 | Document management | P | Creazione IPT, Save/SaveAs; mancano open/close, IAM/IDW, riferimenti e health. |
| 9 | Context/selection | A | Nessuna lettura o gestione SelectSet. |
| 10 | Geometry inspection | P | Facce, spigoli, area, bbox e conteggi; geometria perlopiù body 1, informazioni approssimate. |
| 11 | Measurement | P | Volume/area e diagnostica spigoli; nessuna misura tra entità né contratto value/units/refs/tolerance. |
| 12 | Parameters | P | Lista, creazione, assegnazione espressione; manca rename/delete/dependencies. |
| 13 | Dependency graph | A | Nessun grafo, tracing o rilevazione cicli. |
| 14 | Sketch inspection | P | Nomi in inspect e conteggi; nessuna ispezione entità/constraint/DOF. |
| 15 | Sketch creation | P | Piano/faccia/offset, linee, rettangoli centrati, cerchi, poligoni/profili; mancano archi, spline, slot e lifecycle. |
| 16 | Sketch constraints | P | Endpoint condivisi nei profili; nessuna API generale per imporre i vincoli richiesti. |
| 17 | Dimensional constraints | P | Diametro automatico del cerchio e quote ausiliarie dei fori; nessun set generale o garanzia fully constrained. |
| 18 | Work geometry | P | Piano offset dentro create_sketch; nessuna gestione completa piani/assi/punti. |
| 19 | Solid features | P | Extrude/cut, revolve, hole anche maschiato, fillet/chamfer; mancano sweep/loft/shell/rib/draft/split/thicken/emboss/decal e thread generale. |
| 20 | Patterns | P | Circular pattern di feature; mancano rectangular/body/mirror. |
| 21 | Feature management | P | Lista, cancellazione, soppressione/riattivazione; mancano edit/rename/reorder/dependencies/health completo. |
| 22 | Multi-body | P | Extrude/revolve consentono new/intersect; nessun CRUD corpi o selezione generalizzata. |
| 23 | Direct editing | A | Nessun tool per move/offset/delete/rotate/replace face. |
| 24 | Materials | A | Nessun catalogo, assegnazione o densità. |
| 25 | Appearance | A | Nessun tool dedicato. |
| 26 | Physical properties | P | Legge Volume da MassProperties; non espone massa, COM, inerzia o assi principali. |
| 27 | Assembly structure | A | Nessun tool IAM, albero o occurrence. |
| 28 | Positioning | A | Nessun transform/move/rotate/ground di componenti. |
| 29 | Assembly constraints | A | Nessuna gestione. |
| 30 | Joints | A | Nessuna gestione. |
| 31 | Degrees of freedom | A | Nessuna analisi DOF. |
| 32 | Interference | A | Nessun AnalyzeInterference o distanza minima tra componenti. |
| 33 | Clearance optimisation | A | Nessun solver/loop dedicato. |
| 34 | Collision-aware motion | A | Nessun campionamento cinematico. |
| 35 | Model States | A | Nessuna gestione. |
| 36 | Representations | A | Nessuna gestione. |
| 37 | Sheet Metal | P | Template lamiera, spessore, Face, Flange, Cut; nessun set completo di regole/pieghe/unfold/refold. |
| 38 | Flat Pattern | A | La geometria lamiera lo consente, ma non esistono tool per crearlo/ispezionarlo/esportarlo. |
| 39 | Manufacturing lamiera | A | Nessuna regola geometrica produttiva. |
| 40 | Drawing creation | A | Nessun modulo tavole. |
| 41 | Drawing views | A | Nessuno. |
| 42 | Drawing positioning | A | Nessuno. |
| 43 | Drawing dimensions | A | Nessuno; quote sketch non sono quote tavola. |
| 44 | Annotations | A | Nessuna annotazione tavola. |
| 45 | GD&T | A | Nessuna gestione né approvazione tolleranze. |
| 46 | Drawing validation | A | Nessuna validazione. |
| 47 | iProperties | A | Nessun tool dedicato. |
| 48 | BOM | A | Nessun accesso BOM. |
| 49 | BOM intelligence | A | Nessun controllo/classificazione/confronto. |
| 50 | Costing | A | Nessun modello costi. |
| 51 | Content Center | A | Nessuna ricerca/inserimento. |
| 52 | Fastener intelligence | A | Fori maschiati presenti, ma nessuna selezione/posa/verifica bulloneria. |
| 53 | Import | A | Nessun import tool; non confondere export IGES/SAT con import. |
| 54 | Export | P | STEP/STL/SAT/IGES; niente PDF/DXF/DWG/DWF/immagini dedicati. |
| 55 | Release package | A | Nessuna orchestrazione pacchetto. |
| 56 | Viewport | A | Nessuna cattura/camera. |
| 57 | Vision + CAD | A | Nessuno screenshot correlato allo stato CAD. |
| 58 | Highlight AI | A | Nessun highlight/isolate/focus. |
| 59 | User pointing | A | Nessun mapping selezione→ID. |
| 60 | Event system | A | Nessun listener CAD o flusso eventi. |
| 61 | Subscriptions | A | Nessuna sottoscrizione resource. |
| 62 | Transactions | P | begin/commit/abort reali tramite TransactionManager; manca preview e imposizione automatica. |
| 63 | Checkpoint | A | Nessun backup/checkpoint persistente. |
| 64 | Dry Run | A | Nessuna simulazione o stima effetti. |
| 65 | Change Plan | A | Nessun piano strutturato eseguibile. |
| 66 | CAD Diff | P | Alcune operazioni riportano delta volume/facce/spigoli; nessun diff persistente di documenti/BOM/massa. |
| 67 | Validation engine | A | Diagnostica volumetrica non equivale a validazione documenti/rilascio. |
| 68 | Feature health | P | Suppressed letto; manca HealthStatus healthy/warning/failed/missing-reference. |
| 69 | Constraint health | A | Nessun controllo sovra/sottovincolo. |
| 70 | Engineering rules | A | Nessun motore regole configurabili. |
| 71 | Rules resources | A | Nessuna resource regole. |
| 72 | Design intent | A | Nessun metadato semantico gestito. |
| 73 | Semantic relations | A | Nessun grafo di relazioni. |
| 74 | Assembly Knowledge Graph | A | Nessuna struttura funzionale. |
| 75 | Search | P | Ricerca geometrica per coordinate e parametro per nome; nessuna ricerca semantica multifiltro. |
| 76 | Apprentice indexing | A | Nessun backend Apprentice. |
| 77 | Library search | A | Nessun indice CAD. |
| 78 | Similarity search | A | Nessuna firma geometrica/ricerca similarità. |
| 79 | iLogic | A | Nessun tool dedicato. |
| 80 | Safe scripting | D | execute_python sempre registrato, exec senza sandbox/opt-in; suggerito nelle istruzioni server. |

## Rilievi tecnici che incidono sul progetto

Riferimenti alla baseline: [server.py](https://github.com/NeonGlay/inventor-mcp/blob/3809700ed2e34164f3c45bec5a50aed347448383/src/server.py), [inventor_api.py](https://github.com/NeonGlay/inventor-mcp/blob/3809700ed2e34164f3c45bec5a50aed347448383/src/inventor_api.py).

1. **Scripting e modalità** — server.py:28–68 esegue Python con namespace condiviso e oggetti COM. Nessuna autorizzazione applicativa, sandbox o isolamento. Una policy sui singoli tool è aggirabile attraverso questo comando. reload_api:73 sostituisce il wrapper preservando solo `_app`, perdendo il riferimento `_txn` in caso di transazione aperta. Da disabilitare in produzione.
2. **Identità** — create_sketch:213, fillet:505, circular_pattern:962, list_features:1060 e find_edge:1193 del wrapper dipendono da indici. Le coordinate aiutano a risolvere una geometria, ma non ne preservano l'identità dopo modifiche. find_edge senza coordinate seleziona il primo candidato; non segnala ambiguità.
3. **Misure approssimate** — list_edges:747 calcola la distanza fra vertici estremi: per curve è la corda, non la lunghezza dell'arco. list_faces:768/find_face:1222 usano la media dei vertici, non il centroide geometrico di superficie. Questi dati non vanno utilizzati come metrologia esatta.
4. **Unità** — get_parameters:1006 restituisce `p.Value` e `p.Units` senza conversione esplicita del valore interno. Il contratto va normalizzato tramite UnitsOfMeasure; la dichiarazione globale “millimetri” non basta.
5. **Transazioni** — wrapper:800 conserva una sola transazione in memoria sul documento attivo al begin. Le altre operazioni consultano nuovamente ActiveDocument: non c'è blocco sul documento, ownership della sessione, gestione cambi documento, rollback su disconnessione o garanzia multi-documento. Esportazioni e salvataggi su disco richiedono compensazioni separate.
6. **Parametricità** — draw_rectangle crea geometria senza imporre quote di larghezza/altezza. draw_circle tenta la quota diametro ma ne ignora l'errore. Le quote ausiliarie di `_add_hole_position_dims` sono in uno sketch separato: non equivalgono automaticamente a vincoli sulla posizione del foro. Distinguere le ricette suggerite dal comportamento garantito dei tool.
7. **Localizzazione/export** — template cercati con nomi inglesi; traduttori individuati con DisplayName inglese. Il fallback export usa SaveAs(path, False), che richiede verifica invece di essere considerato un export affidabile. Preferire identificatori dei translator e opzioni validate.
8. **COM** — connect chiama CoInitialize ma non c'è un dispatcher STA dedicato, message pump esplicita o serializzazione delle operazioni. La correttezza del threading va provata con la versione FastMCP effettivamente installata; non è dimostrato un errore runtime da questa sola lettura.
9. **Falsi positivi di capacità** — il parametro interno cut_across_bends non è usato; Flat Pattern è citato nelle descrizioni, ma non implementato come operazione. Snapshot e delta non controllano interferenze, feature fallite o riferimenti mancanti.

## Altri strumenti verificati

Ricerca GitHub e web con fonti primarie. I tool count altrui, salvo diversa indicazione, sono dichiarazioni dei rispettivi autori. Non sono stati eseguiti i loro test o caricate DLL.

### bimwright/ipt-mcp — candidato architetturale più vicino

[Repository](https://github.com/bimwright/ipt-mcp), Apache-2.0, HEAD `d539a2ee7295747c7ef3b44a64aa870e8889deac`, ramo master. Dichiara 58 tool, 59 con send_code. Server .NET 8 separato da add-in per versione, IPC locale autenticato, STA dispatcher, modalità read-only e opt-in scripting. Comprende parti, parametri, iProperties, massa, viewport, export, assiemi, BOM, DOF, vincoli e interferenze.

Verificati direttamente: CommandDispatcher.cs impone read-only/opt-in e normalizza errori; CheckInterferenceHandler.cs chiama AnalyzeInterference e restituisce volumi per coppia; EntityResolver.cs dichiara e usa **indici posizionali**, non reference keys persistenti. La struttura contiene handler, trasporti e test; il file AssemblyStubs.cs è ormai vuoto e non va interpretato come assenza dell'implementazione.

È più vicino alla specifica di NeonGlay come base C#/IPC. Non dimostra copertura dell'intero sistema semantico, delle tavole o delle transazioni richieste. Il server è soprattutto un gateway: concentrare la futura logica semantica nel server resta lavoro da fare. La release descritta distribuisce add-in 2025/2027, quindi il target 2026 richiede verifica/build dedicata. I filtri sul codice non sono prova di sandbox forte.

Fonti di codice: [dispatcher](https://github.com/bimwright/ipt-mcp/blob/d539a2ee7295747c7ef3b44a64aa870e8889deac/src/shared/Infrastructure/CommandDispatcher.cs), [interferenze](https://github.com/bimwright/ipt-mcp/blob/d539a2ee7295747c7ef3b44a64aa870e8889deac/src/shared/Handlers/Assembly/CheckInterferenceHandler.cs), [resolver](https://github.com/bimwright/ipt-mcp/blob/d539a2ee7295747c7ef3b44a64aa870e8889deac/src/shared/Handlers/EntityResolver.cs).

### xtylerai2026-oss/autodesk-inventor-mcp — confronto funzionale Python

[Repository](https://github.com/xtylerai2026-oss/autodesk-inventor-mcp), MIT, HEAD `91cee984d82e867feddc0e5ac342048c1234337e`. Dichiara 24 tool e test su Inventor 2027, stato alpha. Registrazioni verificate in server.py: perception/screenshot, misura, loft, booleani, assiemi, interferenze, BOM, import/export e iLogic. Dispatcher verso thread STA; stdio e HTTP. Gli autori escludono tavole e lamiera. Escape hatch Python esposta; le annotazioni readOnlyHint non costituiscono enforcement. Utile per studiare test e workflow inspect/modify, non prova di compatibilità 2026 o soddisfacimento del punto 80.

### hsavas/inventor-mcp — tavole, ma licenza da rispettare

[Repository](https://github.com/hsavas/inventor-mcp), HEAD `a8830f2d523a0e616398ce4533fe2503ce7b04a0`. Moduli Python per parti, assiemi, viste, proprietà e tavole. Verificati in drawing_tools.py comandi per fogli, viste base/proiettate, posizione, quote e parts list, con chiamate COM effettive. Copertura interessante dell'area mancante in NeonGlay; nessuna validazione runtime effettuata.

La [LICENSE](https://github.com/hsavas/inventor-mcp/blob/a8830f2d523a0e616398ce4533fe2503ce7b04a0/LICENSE) è **Non-Commercial**, non MIT: prevede autorizzazione scritta per uso commerciale e comprende le attività interne di aziende for-profit. Non incorporare quel codice in un prodotto aziendale senza risolvere questa condizione.

### Cadtastic-Solutions/Autodesk-Inventor-MCP — infrastruttura per sviluppatori

[Repository](https://github.com/Cadtastic-Solutions/Autodesk-Inventor-MCP), Apache-2.0, HEAD `d4cd3c05ed094284c4854b3c81c7c6ebca6053d7`. Il README di main dichiara esplicitamente uno scaffold .NET 8 con gestione sessioni/istanze. Le altre capacità sono rimandate ai branch di feature. Utile come riferimento per lifecycle COM; non conteggiare la roadmap come modellazione già disponibile su main. Verifica limitata a struttura e documentazione, non audit completo dei branch.

### Autodesk: esiste già un Assistant basato su MCP

[Autodesk Developer Blog, 22 aprile 2026](https://blog.autodesk.io/from-commands-to-conversations-exploring-autodesk-assistant-in-inventor-2027/) documenta Autodesk Assistant Technical Preview in Inventor 2027: lettura modello, struttura assiemi, proprietà e analisi. Usa MCP internamente, ma la fonte dichiara che non crea/modifica geometria automaticamente. Non dimostra che quel server sia pubblico o collegabile a un client esterno come Astra. Pertanto non è corretto affermare genericamente che “Autodesk non ha MCP per Inventor”; va distinto l'Assistant integrato da un bridge esterno pubblico.

[Autodesk Product Help MCP](https://aps.autodesk.com/blog/now-available-chatgpt-autodesk-product-help-plugin) è invece un servizio read-only di documentazione ufficiale, anche per Inventor. Complementare per consultazione, non controlla il CAD.

### Componenti complementari

- [Autodesk Automation API](https://aps.autodesk.com/): backend cloud ufficiale che include Inventor, adatto a BATCH. Richiede pipeline separata di app bundle/attività/job; non sostituisce selezione ed eventi desktop. [Supporto .NET 8](https://aps.autodesk.com/blog/net-8-now-available-inventor-design-automation).
- [Inventor API e Apprentice](https://aps.autodesk.com/developer/overview/inventor): Apprentice per query dei file, struttura, geometria e proprietà senza sessione completa; non presumere equivalenza a Inventor per modifica feature e rebuild.
- [InventorCode/InventorShims](https://github.com/InventorCode/InventorShims): libreria MIT di helper API, utile come componente di sviluppo, non server MCP completo. Compatibilità del pacchetto scelto con .NET 8/Inventor 2026 da collaudare.

Non è stato trovato, tra i candidati verificati, un prodotto che dimostri l'intera specifica di 80 punti. Questa è una conclusione della ricerca svolta, non prova di inesistenza globale.

## Correzioni e decisioni necessarie nella specifica

1. **ID persistenti con esiti espliciti.** Conservare document ID, occurrence path, reference key e key context; gestire unique/ambiguous/deleted/suppressed/unresolved. Autodesk documenta che il binding può restituire più candidati: non promettere persistenza perfetta dopo ogni modifica topologica. [BindKeyToObject](https://help.autodesk.com/cloudhelp/2025/ENU/Inventor-API/files/ReferenceKeyManager_BindKeyToObject.htm), [CanBindKeyToObject](https://help.autodesk.com/cloudhelp/2024/ENU/Inventor-API/files/ReferenceKeyManager_CanBindKeyToObject.htm).
2. **Stato versionato.** Ogni comando deve specificare document/entity ID e expected revision. Se utente o altro agente modifica il CAD, invalidare il piano; eventi coalescenti e nuove snapshot devono poter riallineare un client dopo disconnessione.
3. **Clearance e penetrazione sono dati diversi.** Definire distanza minima non negativa, overlap booleano e volume d'interferenza; una profondità negativa richiede una definizione e un algoritmo dedicati. Non dedurla dal volume. [MeasureTools](https://help.autodesk.com/cloudhelp/2025/ENU/Inventor-API/files/MeasureTools.htm).
4. **Dry run qualificato.** Distinguere preflight statico da prova su copia/checkpoint. Non promettere conteggio esatto dei rebuild senza esecuzione. Undo non annulla file esportati, job cloud o altri effetti esterni.
5. **Compatibilità per capacità.** Target iniziale 2026 x64/.NET 8; “2026+” richiede adapter e test per versione, non una garanzia indistinta. Alias STP/STEP; LOD da valutare rispetto a Model States nei modelli moderni.
6. **Ottimizzazione vincolata.** enforce_clearance deve ricevere parametri modificabili, limiti, obiettivi, budget iterazioni e criterio di commit. Il costing con rigidezza richiede anche analisi strutturale/materiali/carichi: la sola geometria COM non certifica quel vincolo.
7. **Contratti espliciti.** Misure con unità/tolleranze/provenienza, errori strutturati, output paginati, capability discovery, audit, timeout/cancellazione e gestione dei prompt modali di Inventor. Le conferme ASSISTED devono riferirsi a piano+revisione, non essere solo testo conversazionale.

## Percorso consigliato per inventor-so-mcp

Il fork richiesto è preservato come baseline e fonte di ricette geometriche. Per la destinazione C#/.NET, valuterei un nucleo nuovo separato ispirato all'architettura verificata di ipt-mcp; nessun cambio automatico di upstream o incorporazione di codice altrui è stato effettuato.

| Fase | Risultato | Criterio verificabile |
|---|---|---|
| 0 | Contratti, test harness e adapter Inventor 2026 | Connessione alla versione corretta, errori/unità stabili, COM sul thread corretto. |
| 1 | READ ONLY: documenti, assiemi, selection, ID, resources, screenshot, eventi | Selezionare una staffa, identificarla dopo rebuild e riapertura; ambiguità segnalate. |
| 2 | Transazioni/policy/checkpoint e modifiche limitate | Spostamento di 15 mm o parametro, validazione, rollback ripristina geometria/stato; cambio documento rifiutato. |
| 3 | Vincoli/DOF/interferenze/clearance, massa e BOM | Scenario motore-ruota con clearance >=5 mm, riferimenti risolti e vincoli sani. |
| 4 | Tavole e lamiera, export e release | STEP/PDF/DXF apribili, tavola e BOM aggiornate, manifest del pacchetto e gestione errori parziali. |
| 5 | Regole/knowledge graph/motion/library/cost/BATCH | Dataset e test specifici per ogni modulo; nessun commit autonomo fuori dai limiti definiti. |

Il primo MVP dovrebbe coprire lettura e modifiche controllate dell'assieme, non l'intero catalogo di feature. Lo scenario completo della richiesta diventa il test end-to-end alla fase 4: seleziona supporto → modifica → rebuild → verifica 5 mm → aggiorna tavola/BOM/massa → esporta → commit o ripristino.

## Verifiche effettuate e limiti

- GitHub conferma `isFork=true` e parent `NeonGlay/inventor-mcp`; clone locale con origin sul fork e upstream sull'originale.
- AST di tutti i file Python in src analizzato con successo; 36 registrazioni tool individuate.
- Non presenti test upstream. Nessun test live eseguito, nessun server avviato, nessun modello aperto o modificato.
- Il Python predefinito locale è 3.11.13, inferiore al requisito 3.12: verifica AST non equivale a installazione supportata. Nessuna dipendenza installata.
- Non risultava un processo Inventor attivo nel controllo effettuato; la presenza di componenti Autodesk sul PC non prova una sessione Inventor utilizzabile.
- Il codice dei concorrenti è stato campionato nei file citati; la copertura restante è attribuita alle rispettive dichiarazioni.
