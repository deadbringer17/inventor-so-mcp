# H01 — Integrazione del runner Quest S1

12 ottobre 2026. Blueprint operativo, verificato per lettura del checkout dopo
[ondata 36](s1-parallel-wave36.md). Non è un'esecuzione sul dispositivo né
una chiusura dei gate. Riferimenti: [specifica S1](superpowers/specs/2026-10-06-schizzo-parita-inventor-2027-design.md),
[piano F0–F7](superpowers/plans/2026-10-06-schizzo-parita-inventor-2027.md),
[pacchetti paralleli](superpowers/plans/2026-10-11-s1-pacchetti-paralleli.md),
[procedura Quest](xr-quest-acceptance.md). Il perimetro resta 77 comandi,
18 gate aperti. L'APK ordinaria dell'ondata 36 è compilata e archiviata,
non installata e priva del runner di accettazione.

## 1. Punto di aggancio e percorso da conservare

I percorsi sotto sono relativi alla radice del repository; `Assets/` e
`Packages/` sono sotto `Inventor XR SO/`.

Creare `Assets/XrSo/Xr/Acceptance/S1QuestAcceptance.cs`, derivato da
`QuestAcceptanceRunner`, interamente sotto `#if XR_SO_ACCEPTANCE`.
Aggancio proposto: `AfterSceneLoad` →
`StartIfRequested<S1QuestAcceptance>("xr_s1_acceptance")`, `Milestone => "s1"`,
`FixtureMilestone => "s1"`. Questi nomi nuovi sono una proposta da implementare;
il runner S1 non esiste ancora. Riutilizzare `WaitForFixture`, `RequireFixture`,
`WaitUntil`, `WaitFor`, `CaptureScreenshot`, `Pass`, `NotCovered` e gli helper
reflection della base. Sovrascrivere `Completion`: la base restituisce PASS
COMPLETE anche se sono stati emessi NOT COVERED; S1 deve restituire
`PARTIAL; NOT COVERED: ...` quando mancano sottocasi sintetici pianificati,
seguendo il criterio di M9. I suffissi `-physical`, `-probe`, `-suite` possono
restare evidenza esterna aperta come in M9; dichiarare esplicitamente questa
regola nel manifest, senza nascondere i residui sintetici con quei suffissi.

Il runner si collega all'`AppController` già creato dalla scena generata:
`_session`, `_catalog`, `_design`, `_lamiera`, `_input`, `_shell`, `ray`,
`sceneView`, `head`; chiama `EnterSession(EnvironmentMode.StudioVr)` solo se
`_inSession` è falso. Non crea workspace, editor o backend alternativi.
`DesignWorkspace.NativeEditor` e `LamieraWorkspace.NativeEditor` espongono la
`NativeSketchSession` reale; `_nativeEditor` consente di osservare
`NativeSketchEditorUi` tramite reflection, dichiarata in `ReflectedMembers`.

Catena da esercitare per una mutazione:

1. `ActionCatalog.Find(id).TryInvoke()` per aprire editor, variante/opzione,
   tastierino, Anteprima e Applica; stesso catalogo usato dalla tavolozza.
2. `XrInput.Source = SyntheticInputSource`, frame `XrInputFrame` tramite
   `XrInput.Poll(frame, Time.unscaledTime)`: Trigger/rilascio, Grip, X, stick,
   tracking. Copiare il piccolo harness di `M6QuestAcceptance.BeginSynthetic`,
   `Send`, `TriggerTap`, `AimAt`, `EndSynthetic`, senza ereditare il suo Run.
   Salvare e ripristinare source, `UiHitOverride`, `DoubleTriggerClock`.
3. Il Trigger del workspace raggiunge `NativeSketchEditorUi.PickConstraint`
   oppure `Pick`; la raccolta coordinate passa da `CoordinateRay`,
   `CadCoordinates.SketchRay` e `PickCoordinate`. Trasformare il punto CAD con
   `model.TransformPoint(CadCoordinates.ToLocal(snapshot.Frame.ToModel(p)))`;
   non costruire coordinate dal mondo ignorando il frame dello schizzo.
4. `NativeSketchEditorUi` raccoglie gli input e chiama
   `NativeSketchSession.SetOperation`; `PreviewAsync`/`ApplyAsync` passano alla
   `DesignSession` condivisa. Le azioni della barra sono `CommitIds.Preview`,
   `CommitIds.Apply`, `CommitIds.Cancel`, `CommitIds.Recover`.
5. `InventorBackend` reale, implementato anche dai partial `DesignBackend.cs`
   e `NativeSketchSnapshot.cs`, usa `PreviewDesignWithChecksAsync`,
   `CommitDesignAsync`, `GetNativeSketchAsync` sul canale MCP/TLS della sessione.
   `GetNativeSketchAsync` chiama `inventor_get_sketch_editor_xr` con documento,
   revisione e sketch ID. Anteprima usa `inventor_plan_change` e verifica
   `preview.status=preview_rolled_back`; Applica usa `inventor_commit_plan`
   e verifica `status=committed`. Il rendering deve abilitare realmente `CanApply`:
   non chiamare direttamente `MarkRendered` dal runner.

L'invocazione di un'azione per ID verifica il catalogo, non il puntamento fisico
del suo pulsante. Input numerico/testuale iniettato in `ActiveEntry` o
`ActiveTextEntry` va registrato separatamente come SYNTHETIC KEYPAD/TEXT.
Almeno un caso per contesto deve colpire la UI con il raggio e verificare che
non venga aggiunto anche un punto CAD. `UiHitOverride=false` usato per i casi
geometrici non dimostra questa intercettazione.

Non riutilizzare il helper `NativeConstraintVisibilityFixture` dei test
EditMode: sostituisce l'editor/backend con snapshot costruiti. Non usare
`Tests~/XrSo.TestHost`, `FakeAddIn` o `NativeSketchSession.SetOperation`
direttamente per dichiarare passato il flusso prodotto. Le sonde
`bridge/tests/S1LiveProbe` confrontano handler/API sul PC; il loro dispatcher
locale non va incorporato nel runner Quest.

## 2. Fixture e readback indipendente

Estendere `bridge/tests/QuestAcceptanceFixtures/Program.cs` per ammettere
`s1`; oggi accetta m1/m2/m3/m5/m6/m7/m9n/m9f/m9h. Implementare preparazione,
ispezione e ripristino nel progetto esistente, con modulo dedicato anziché
duplicare un secondo gestore documenti. Manifest proposto:
`artifacts/s1-verification/quest-fixture.json`.

Preparare due documenti posseduti, salvati e tenuti aperti:
`XR_S1_Quest_Acceptance_Part.ipt` e
`XR_S1_Quest_Acceptance_Sheet.ipt`, con un piano di schizzo noto; la Lamiera
usa il template Sheet Metal nativo. Il prefisso soddisfa la guardia della base
anche durante il cambio documento. Nomi degli schizzi e ruoli proposti:

| Schizzo, in entrambi i documenti | Baseline da preparare e misurare con COM |
|---|---|
| `S1_Create` | Vuoto, non consumato; creazioni isolate, una baseline salvata per ogni scenario |
| `S1_Dimensions` | Linee libere separate H/V/oblique, due linee angolate, tre punti, cerchio, arco, ellisse, coppia linea/cerchio; spline offset solo se il tipo/relazione richiesto esiste davvero |
| `S1_Constraints` | Coppie separate di linee/cerchi/ellissi, punti ed estremi, asse di simmetria; coppia spline con estremi compatibili per G2, costruita e verificata nativamente |
| `S1_Transforms` | Linea libera, linea connessa, cerchio, arco, ellisse e spline; sottoinsieme distinto vincolato/quotato per i rifiuti |
| `S1_Patterns` | Sorgente circolare con quota radiale e parametri nominati; linee direzione distinte e punti centro; nessuna serie iniziale |
| `S1_Offset` | Linea, catena aperta/chiusa, cerchio e spline compatibile in regioni separate |
| `S1_Visibility` | Vincoli di almeno due tipi nativi; >256 voci per il budget solo dopo lettura effettiva del reader; almeno una coppia di rettangoli di etichetta realmente sovrapposti |

Le coordinate/configurazioni della fixture devono essere congelate nel
manifest con attesi numerici e unità; la tabella non fissa ancora dati oracle
non misurati. Per i vincoli D04 verificare le annotazioni/associazioni lette
dal reader: non sintetizzare `annotation` per ottenere una sovrapposizione.
Se il CAD non fornisce un'ancora, atteso `Unpositionable`, non un simbolo inventato.

Manifest: percorsi completi creati, `previous_document`, ID nativi, documento
attivo, baseline Dirty/DatabaseRevisionId, contenuto atteso per schizzo,
versione Inventor/interoperabilità, PID/target del gateway. La sola guardia
per prefisso non prova proprietà: il wrapper confronta ID e percorsi con il
manifest prima di abilitare i test; ogni mutazione riverifica anche
`RequireFixture` e documento/sketch/revisione correnti.

Readback Quest: `NativeEditor.Snapshot`, `Selected`, `Draft`,
`Mutation.Status`, `Mutation.Preview`, `Mutation.CanApply`, `LastCommit`,
`CommitOutcomeUnknown`; letture fresche con il backend reale e
`GetDocumentStateAsync`. Il token di revisione del gateway non è
`DatabaseRevisionId`: conservare entrambi, senza confrontarli tra loro.

Readback PC: aggiungere a `--inspect-quest s1` geometria analitica, tipo nativo,
ReferenceKey/ruoli delle relazioni, dimensioni/parametri/espressioni, dati
serie/istanze/soppressioni e offset. Riutilizzare la semantica di confronto
di `NativeDimensionProbe`, `NativeTransformProbe`, `NativeStretchProbe`,
`NativeOffsetProbe`, `NativePatternProbe`, senza eseguirne i cicli di mutazione
autonomi durante il run Quest. Gli helper attuali sono privati della partial
`Program` di S1LiveProbe, non una libreria pronta: occorre estrarre/adattare
solo la lettura. L'ispezione finale da sola non documenta un rollback intermedio:
il wrapper deve acquisire readback anche ai checkpoint o il runner deve
salvare lo snapshot nativo fresco a ogni fase.

## 3. Protocollo comune per ogni caso mutante

Ogni riga della matrice produce un ID sottocaso `S1-xx-Q-<contesto>-<variante>`
e una traccia in ordine, con baseline e riferimenti dello snapshot corrente.

1. Aprire con `sketch.editor.open`, scegliere il nome nel picker prodotto;
   attendere snapshot non Loading, revisione corrente, `CanEdit`, nessuna bozza.
   Se la capability attesa manca, registrare NOT COVERED con motivo; non saltare
   silenziosamente e non promuovere una capacità sperimentale.
2. Selezionare tramite raggio/Trigger e filtri/sottoparti reali, completare
   campi/opzioni. Salvare la bozza solo come evidenza: nessuna sostituzione
   con JSON assemblato dal runner. Revisione e contenuto devono restare baseline.
3. `CommitIds.Preview`: attendere PreviewReady e rendering; confronto del
   risultato previsto e `CanApply`, lettura fresca CAD uguale a baseline e
   revisione invariata. Questo prova rollback della simulazione nativa.
4. `CommitIds.Cancel`: nessuna bozza/piano applicabile, snapshot ancora baseline.
   Rifare l'operazione dagli stessi input; Anteprima → Applica; attendere
   refresh completato, `NeedsRefresh=false`, nuovo snapshot e nuova revisione.
   Confrontare contenuto nativo, non solo mesh/screenshot. Riacquisire ID dopo
   ogni revisione; le chiavi restano opache, non derivarle da indici.
5. Eseguire le modifiche successive indicate sotto, con lo stesso protocollo.
   Per gli invalidi: Applica disabilitato, errore esplicito e CAD invariato.
6. Ripristinare la baseline del caso tramite Undo del prodotto dove disponibile
   e verificare semanticamente. Se non torna esatta, interrompere quel ramo:
   chiusura senza salvataggio e riapertura della fixture posseduta è un reset
   dichiarato del wrapper, non un PASS di Undo/rollback. Chiudere infine tutti
   i documenti creati senza salvare e riattivare quello precedente.

Non confrontare l'intero JSON byte per byte dopo un commit/Undo: revisione e
ID rigenerati possono cambiare. Confrontare oggetti/ruoli/relazioni con la
baseline congelata, riportando identità preservate o invalidate. Durante
Anteprima, invece, snapshot e token della baseline devono restare invariati.

## 4. Matrice di esecuzione incrementale

Tutte le righe si ripetono su Parte e Lamiera. I nomi `sketch.native.<NativeId>`
sono reali: `SketchCommand.Id` li deriva da `SketchCommandCatalog`.
Una voce censita nel catalogo non è automaticamente implementata;
`SupportsVariant` e `XrAction.Enabled` vanno verificati prima del caso.

| ID e percorso disponibile | Input/azioni e agganci reali | Atteso nativo e ripristino specifico |
|---|---|---|
| Q01 sessione, S1-04/13/14 | `DesignNativeSketch.cs` / `LamieraNativeSketch.cs`: `OpenNativePicker`, `OpenNativeSketch`, `RefreshNativeSketch`; `sketch.editor.refresh`, `.confirm`, `.reselect.confirm`, `.finish`; `NativeSketchSession.Checkpoint/Restore` attraverso rebind del workspace | Letture non mutanti; bozza conservata quando previsto, riferimenti scaduti inapplicabili; finish senza bozza chiude editor senza commit. Con bozza verificare il comportamento effettivo e registrare il residuo della semantica desktop, senza chiamarlo completo |
| Q02 linea/rettangoli/cerchio/punto, S1-05 | `NativeSketchEditorUi.Creations`, `Prompt`; NativeId `SketchLineCmd`, quattro `Sketch*RectangleCmd`, `SketchCenterPointCircleCmd`, `SketchHoleCenterPointCmd`; almeno un caso raggio e uno tastierino. `SketchMidpointLineWrapperCmd` usa `PromptMidpoint` e partial `NativeSketchMidpointSelection.cs` | Tipo/numero curve, estremi/centro/raggio, hole_center, vincoli creati e riuso punti nativi. Casi degeneri rifiutati; nessuna inferenza persistente dedotta dallo snap visivo |
| Q03 arco/ellisse/poligono/asole, S1-05 | NativeId `SketchThreePointArcCmd`, `SketchCenterPointArcCmd`, `SketchEllipseCmd`, `SketchPolygonCmd`, `SketchCentertoCenterSlotCmd`, `SketchOverallLengthSlotCmd`, `SketchCenterPointSlotCmd`, `SketchThreePointArcSlotCmd`, `SketchCenterPointArcSlotCmd`; specializzazioni Prompt e input parziali esistenti | Sweep firmato/lato, semiassi/orientamento, inscritto/circoscritto e chiusura/topologia asole. Usare attesi dalle sonde/oracle congelati; non identificare arco solo dalla tessellazione. Variante/opzione non attraversata è NOT COVERED |
| Q04 spline, S1-05 | `SketchSplineCmd`, `SketchCPSplineCmd` → `StartSpline`, `NativeSketchSplineInput.cs`: `sketch.spline.numeric`, `.undo`, `.cancel`, `.finish`, `.method.<valore>` generati da `SplineActions` | 2 e 64 punti, ordine, tipo fit/CV, metodo dalla snapshot e congelato nella bozza; cambio metodo conserva punti, annullamento non crea CAD, 65° punto rifiutato. Editing maniglie/chiusura non implementati non si deducono da questa creazione |
| Q05 quote, S1-10 | `SketchGeneralDimensionCmd` apre `DimensionTab`; `sketch.dimension.<kind>`: aligned/horizontal/vertical/angle_two_lines/angle_three_points/radius/diameter/arc_length/ellipse_major/ellipse_minor/offset/tangent_distance/offset_spline; `NativeSketchDimensionRules.Variants`, `PromptDimension` | Fixture compatibile per ogni kind, valore/unità, posizione testo, tipo driving/driven e geometria risultante. Selezione incompatibile disabilitata. Attesi di `NativeDimensionProbe`, confrontati con readback reale |
| Q06 editing quote, S1-10/12 | `sketch.dimension.existing`, `.pick.<id>`, `.edit.value`, `.edit.position`, `.edit.driven`, `.edit.expression`, `.edit.name`, `.edit.delete`; nuove quote `.text.use`, `.text.expression`, `.text.name`, `.text.name.auto` | Nome/espressione nativi con unità, driven senza pilotare geometria, modifica successiva e Delete mirata. Espressione invalida rollback; riferimento vecchio dopo refresh rifiutato. Quota automatica/DOF non è coperta |
| Q07 vincoli, S1-11 | Le dodici azioni native `Sketch*ConstraintCmd` sono prodotte dal loop di `Actions`; `ConstraintSelection`, `SetConstraint`, `NativeSketchConstraintRules.Accepts`; `sketch.constraint.axis.1/2`, `.proximity.auto/.point`; `.existing`, `.pick.<id>`, `.edit.delete` | Cardinalità e sottoparti effettive: Ground/H/V unary, coincidenza punto-curva, coppie/asse simmetria, G2 spline. Leggere tipi/EntityOne/Two/ruoli, non contare soltanto vincoli. Due aggiornamenti della sorgente provano la relazione; conflitto rifiutato atomicamente |
| Q08 D04 vista/selezione, S1-11/14 | `NativeSketchConstraintVisibility.cs`: `sketch.constraint.filters`, `.filter.all`, `.filter.<NativeType>`; `ConstraintVisibilitySummary`; `DesignGeometryView` → `NativeSketchConstraintLayer.Layout/PickTargets` e `NativeSketchConstraintLabelView.TryHit`; workspace Trigger → `PickConstraint` | Tutti → escludi A → escludi B (vuoto) → riaggiungi A → Tutti; selezionato escluso resta se posizionabile. Zero preview/commit e revisione invariata. Summary distingue mostrate/filtrate/oltre limite/senza posizione/non supportate/associazioni non disponibili. Ray hit/miss e scelta `sketch.constraint.candidate.<generazione>.<indice>`; filtro e refresh invalidano vecchie callback. Zoom/rotazione aggiornano footprint e leader. Caso >256 misura budget reale, senza snapshot finto |
| Q09 trasformazioni integrate, S1-09 | `SketchMoveCmd`, `SketchCopyCmd`, `SketchRotateCmd`, `SketchStretchCmd` → `PromptTransform`; `sketch.transform.remove_constraints`, `.rotate_copy`; `NativeSketchStretchRules.Accepts`, selezione sottoparti e finestra reali | Delta (10,0), centro (0,0), angolo +90°/-90°, estremi Allunga distinti da intera curva; geometria e gestione vincoli secondo opzione realmente supportata. Copia solo se capability pubblicata e crash risolto nel target congelato; altrimenti NOT COVERED, senza rilanciare indiscriminatamente. Scala, Taglia/Estendi/Dividi non entrano come successi perché esistono moduli geometrici/sonde |
| Q10 serie, S1-08 | `SketchRectangularPatternCmd`, `SketchCircularPatternCmd` → `PromptPattern`; `PatternTab`, `sketch.pattern.expressions`, `.suppressed`, `.suppressed.clear`; `.edit.parameters`, `.edit.expressions`, `.edit.suppressed`, `.edit.unsuppress`, `.edit.delete`; selezione serie dal vincolo nativo con `pattern_definition` | Rettangolare una/due direzioni, circolare, conteggi/passi/angoli numerici e espressioni, associative/statiche, fitted, verso/simmetria X/Y e soppressioni. Non fare un cartesiano implicito: loggare combinazione esatta. Due aggiornamenti quota sorgente/parametro definizione e soppressione/riattivazione, readback istanze/native parameters; oracle di `NativePatternProbe`. Modifica completa ruoli/direzioni/boundary e Specchio restano residui |
| Q11 offset, S1-09 | `SketchOffsetCmd` apre `OffsetTab`; `sketch.offset_distance`, `sketch.offset_point`, `.offset.connected`, `.offset.constraints`, `.offset.natural`; `PromptOffset`, `NativeSketchOffsetRules.Accepts` | Catena sì/no, distanza o punto/lato, relazione offset se richiesta, chiusura e aggiornamento sorgente due volte. Attesi `NativeOffsetProbe`; modifica completa definizione non dimostrata dalla sola creazione |
| Q12 formato limitato, S1-12 | `SketchConstructionCmd`, `SketchCenterlineCmd`, `SketchHoleCenterCmd` → `SetFormat` | Proprietà native prima/dopo, toggle e Undo; nessun successo esteso a colore/tipo/spessore/Mostra formato/Parametri completo |

Per Q01/Q08 conservare anche una callback vecchia prima di refresh o cambio
filtro e verificarne `Enabled=false` / `TryInvoke=false` senza CAD mutato.
Per stale reale tra Preview e Apply serve un cambiamento controllato della
fixture dal lato PC; attendere il nuovo evento/token e verificare che Applica
non usi il piano precedente. Non basta assegnare uno stato fittizio alla
sessione. Disconnessione e outcome commit ignoto richiedono fault injection
del canale reale con handshake controllato, non un backend FakeAddIn.

## 5. Wrapper, build e ripristino

Nuovo wrapper proposto `scripts/run-s1-acceptance.ps1`, sul modello concreto di
`run-m10-acceptance.ps1`. Quest sveglio/worn prima della preparazione; due APK
obbligatorie con hash congelati: QA e ordinaria. Il wrapper generico
`run-quest-acceptance.ps1` ha `ValidateSet` senza s1: estenderlo, così il suo
meccanismo log `s1-acceptance.txt`, intent `xr_s1_acceptance`, pull screenshot,
hash APK installata, `runnerStarted`, `timeoutPhase` e codici 0/1/2/3/4 rimane
unico. Verificare anche `run-quest-acceptance.ps51.ps1` prima di offrirlo come
alternativa: non presumere che le due copie si aggiornino automaticamente.

La sequenza prevista, dopo implementazione e verifica, è:

1. Preflight pairing/TLS, target e capability experimental reali; fixture
   project già compilato, build QA e ordinaria congelate; backup dati necessari.
2. `--prepare-quest s1`; acquisire manifest/baseline e assicurare che il gateway
   serva lo stesso processo Inventor della fixture COM.
3. Eseguire il generico in processo figlio `pwsh`, perché contiene `exit`;
   `-Milestone s1 -Apk <qa> -OrdinaryApk <ordinary> -Serial <serial>`.
   Il nuovo runner può attraversare i due documenti con attivazione prodotto;
   in alternativa due invocazioni esplicitamente separate per contesto con
   manifest distinti, senza fingere che una esecuzione copra entrambi.
4. Nel `finally` esterno, indipendente da timeout/failure: ispezione prima di
   restore, `--restore-quest s1`, verifica precedente documento e assenza di
   documenti fixture; reinstalla ordinaria e verifica hash **anche** se il
   generico è terminato prima del proprio ripristino.
5. Manifest con esito runner e cleanup separati; cleanup fallito impedisce
   esito complessivo PASS. Conservare log, snapshot JSON per checkpoint,
   readback COM, screenshot e hash dei sorgenti/assembly/APK eseguiti.

`Assets/XrSo/Editor/Setup/XrSoBuild.cs` ha già
`XrSoBuild.BuildAcceptanceApkBatch` e `BuildApkBatch`; il primo passa
`XR_SO_ACCEPTANCE` tramite `extraScriptingDefines`, il secondo no. Non
aggiungere il define alle PlayerSettings persistenti e non modificare la
scena Main generata per avviare S1. L'APK ordinaria mantiene stesso prodotto,
senza codice runner compilato. Estendere l'elenco/tabelle di
`QuestAcceptanceContractTests` con il nuovo tipo e tutti i membri reflection:
oggi la suite controlla nomi espliciti e non scoprirebbe automaticamente S1.

## 6. Lacune che bloccano implementazione/esecuzione

| Lacuna concreta | Intervento minimo e criterio di uscita |
|---|---|
| S1 runner/intent non esistenti | Nuova subclass e prove contratto/reflection; QA compilata include S1, ordinaria lo esclude |
| Fixture s1 non ammessa e readback insufficiente | Program/Fixtures con lifecycle dedicato, manifest e assert nativi per tutti i tipi della tranche; restore misurato anche precedente documento nullo |
| Checkpoint PC durante run assenti | Protocollo di richiesta/ack con timeout tra runner e wrapper oppure salvataggio snapshot backend freschi in-app; per assert COM intermedi serve coordinamento esterno esplicito. Nessun uso di sleep come sincronizzazione |
| Attivazione dei due documenti e reset casi da definire | Riutilizzare `IInspectionBackend.ListOpenAsync` / `ActivateOpenAsync(id, ct)` di `M6QuestAcceptance.ActivateAssemblyDocumentAsync`, individuando il documento dal manifest e attendendo `SessionStatus.Online`/Graph.DocumentId corretto. Fixture conserva entrambi aperti. Freeze baseline per caso, Undo verificato o reset da wrapper con esito distinto |
| Q08 fixture nativa non ancora misurata | Dimostrare tipi/ancore/budget/overlap dal reader reale. I 261 vincoli dei test Unity sono JSON costruito e non garantiscono che Inventor produca le medesime condizioni |
| Target/API sperimentali distribuiti non attestati | Controllare add-in caricato, host `--enable-experimental`, capability/varianti realmente esposte. La build locale o l'APK 36 non dimostrano la DLL caricata nel processo |
| Copia crash e varianti residuali | Capability mancante o rischio ancora aperto → esclusione esplicita NOT COVERED; non eseguire la sonda indipendente durante una run Quest. Ricerca/risoluzione separata prima di ammettere la variante |
| Attesi completi non estratti dalle sonde | Congelare scenari/campi/tolleranze pertinenti nel manifest fixture, distinguendo API/API da oracle desktop; non adottare tutti i PASS di una sonda come attesi senza stesso input/opzioni |
| Stale/offline/outcome ignoto live non pilotati | Handshake di mutazione posseduta o fault injection limitata al target del test; niente cambi nei documenti dell'utente né doppio commit dopo timeout |
| Wrapper s1 e cleanup robusto assenti | Nuovo wrapper esterno finally + allow-list generico; test di setup fallito/timeout/runner fallito con reinstall/hash e restore verificati |

Si può implementare subito il primo incremento Q01 + Q02 minimo + Q08 vista
su una fixture semplice, mantenendo NOT COVERED per overlap/budget non ancora
riproducibili. Non occorre attendere Scala, importazioni, Taglia o tutte le
opzioni serie per avviare H01; serve però il lifecycle reale sopra prima di
eseguire qualsiasi mutazione sul Quest. Nessuno di questi agganci autorizza
chiamare completo S1-16 o l'intero S1.

## 7. Esiti sintetici e prova fisica

Loggare `PASS [S1-xx-Q-...] SYNTHETIC; ...` solo dopo le asserzioni del caso;
`NOT COVERED [S1-xx-...]` per varianti/opzioni assenti e parti fisiche.
Input iniettato verifica instradamento e conversione CAD; snapshot/readback
verificano Inventor reale. Screenshot provano uno stato visuale catturato,
non leggibilità nell'occhio o equivalenza dei glyph desktop.

Prova fisica successiva e separata: tavolozza 320×220 e dimensioni offerte,
posa/occlusione mano/avambraccio, testo 7 mm ed etichette D04 su modelli
complessi, precisione penna/raggio, selezione sovrapposti con controller,
tracking perso/riacquisito e catture, comfort MR/Studio VR da seduto,
input tastierino/testo e microfono se usato. CPU/GC/GPU richiedono campioni
misurati e budget dichiarati; un contatore non disponibile resta NOT COVERED.
PASS del runner incrementale chiude solo i sottocasi registrati, senza
estendere l'evidenza M3/M10 a questo editor.
