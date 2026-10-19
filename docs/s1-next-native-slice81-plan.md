# S1 — Prossimo slice nativo81: Arco centro/punto

15 ottobre 2026. Piano preparato in lettura; nessun codice, build, COM o ADB
eseguito per81. Tutti i 77 comandi restano nel perimetro e i 18 gate restano
OPEN. Questo file non modifica baseline, manifest o evidenze congelate.

## Scelta e stato reale

Scegliere **un solo comando**, #14 `SketchCenterPointArcCmd`, variante
`arc_center`: centro, raggio, angolo iniziale e sweep firmato. È il più piccolo
slice di curve già collegato al prodotto che permette di verificare un flusso
diverso dalla linea, con orientamento e arco maggiore/minore significativi.

La precisazione sul livello di prova è essenziale: questo comando **ha già
prove native31 e campioni desktop**. Non è corretto presentarlo come privo di
L3. Manca invece una prova integrata attuale HTTPS/Core/UI/Quest della sua
intera interazione, delle letture immediate dopo commit e delle successive
mutazioni.81 deve colmare questo sottoinsieme, senza rifare indiscriminatamente
la ricerca geometrica31 né dichiarare la parità completa del comando.

Cerchio tangente (#10 `SketchTangentCircleCmd`), arco tangente (#13
`SketchTangentArcCmd`) e Schizzo3D (#2 `AppSketch3DWrapperCmd`) **non soddisfano
il prerequisito “già implementato”**: hanno ID nel catalogo ma nessuna variante
di creazione corrispondente in `EditSketchHandler.SupportedVariants` né un
percorso UI nativo completo. `constraint_tangent` e `arc_center` sono primitive
riutilizzabili, non implementano da sole Arco tangente. Questi candidati
richiedono un incremento di programmazione e baseline separati.

Fonti normative: [spec §3, §5.1–5.2 e gate](superpowers/specs/2026-10-06-schizzo-parita-inventor-2027-design.md),
[piano F2/F3/F7](superpowers/plans/2026-10-06-schizzo-parita-inventor-2027.md),
[audit56 righe12–14](s1-current-command-status56.md), inventario immutabile
`docs/xr-sketch-2027-gap-inventory.md`. L'audit56 è storico:75–80 aggiungono
prove di linee, lettura testi e parametri, non una nuova attestazione degli archi.
Alla lettura, i rapporti75/80 sono incrementali; non estendere i loro PASS a81.

## Percorsi e prove esistenti da riusare

| Livello | File o artefatto | Significato e limite |
|---|---|---|
| Catalogo77 | `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/SketchCommandCatalog.cs` | ID14, famiglia Arco; presenza nel catalogo non prova parità. |
| UI condivisa | `Inventor XR SO/Assets/XrSo/Runtime/Ui/Shell/NativeSketchEditorUi.cs` | `Creations` associa ID14 a `arc_center`, default numerici centro0/0, raggio10, start0, sweep90; prompt e raccolta centro/start/end con `NativeSketchArcInput`. |
| Geometria input | `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Backend/NativeSketchArcInput.cs` | Sweep firmato, passaggio ±180°, sospensione e ripresa; unità mm/gradi. |
| Sessione | `...core/Runtime/Backend/NativeSketchSession.cs`, `DesignSession.cs` | Riferimenti correnti, bozza congelata, preview/render confirmation/applica; refresh esplicito dopo commit. |
| Mutazione | `bridge/src/shared/Handlers/Experimental/EditSketchHandler.cs` | `arc_center` → `SketchArcs.AddByCenterStartSweepAngle`; raggio positivo, sweep nonzero e modulo<360; solver e transazione batch posseduta. |
| Contratto | `bridge/src/shared/Contracts/CadBatchCommandCatalog.cs` | `edit_sketch` sperimentale; le opzioni dell'arco sono dichiarate. Nessuna promozione di tier prevista. |
| Lettura | `bridge/src/shared/Handlers/Experimental/SketchEditorXrHandler.cs` | Chiavi opache, geometria arco, estremi/centro e strokes nativi; preview senza riferimenti temporanei portabili. |
| Native31 | `bridge/tests/S1LiveProbe/NativeEllipseUiProbe.cs`, `RunEllipseBatches(..., family="arc")` | Oracle API versus batch per arco centro; helper desktop della stessa famiglia. Vecchio runner non va lanciato automaticamente contro un'istanza utente. |
| Archi3punti | `bridge/tests/S1LiveProbe/NativeThreePointArcProbe.cs` | Riferimento per normalizzazione/oracle/rollback; è altra variante, non prova di ID14. |
| Artefatti31 | `artifacts/s1-verification/arc-{part,sheet}-batch31.json`, `arc-ui-comparison31.json`, `arc-pen31-manifest.json` | Prove native e campioni desktop precedenti da leggere prima di ampliare la matrice; nessuna nuova verifica d'integrità dei payload eseguita da81. |
| Unit esistenti | `Inventor XR SO/Tests~/XrSo.Core.Tests/Backend/NativeSketchArcInputTests.cs`, `NativeSketchSessionTests.cs` | Geometria input e lifecycle simulato, separati da CAD reale. |

## File proposti e dipendenze

1. Aggiungere solo `bridge/tests/S1ImageHttpProbe/ArcCreationProbe81.cs`
   (partial Program) per il percorso HTTPS/Core reale e l'oracle nativo su
   documento posseduto. Il coordinatore possiede l'eventuale piccolo hook CLI
   in `Program.cs`; non modificare host73 o artefatti congelati. Utilizzare
   transazioni, PID/start e guardie documenti del runner più recente, dopo
   review: non copiare ciecamente i vecchi avvii31 che possono acquisire ROT.
2. Aggiungere test Core/UI **solo per gap o difetti concreti** emersi:
   estendere `NativeSketchArcInputTests.cs` o `NativeSketchSessionTests.cs`;
   eventuale `Assets/XrSo/Tests/EditMode/NativeSketchArcWorkflow81Tests.cs`
   per catalogo reale, tastierino, annulla e riapertura. Non introdurre test
   che ripetano soltanto la formula dell'implementazione.
3. Solo dopo PASS PC aggiungere
   `Assets/XrSo/Xr/Acceptance/S1QuestAcceptance.Arc81.cs` e relativo `.meta`;
   hook nel runner centrale di proprietà del coordinatore. Il componente
   deve usare le azioni del prodotto e lo stesso bootstrap owned, non una
   creazione COM alternativa. Riutilizzare wrapper75 dopo revisione, senza
   cambiare i suoi percorsi di ripristino implicitamente.

Nessuna modifica di produzione preventiva a handler, reader, Core o UI.
Un eventuale difetto va riprodotto, isolato e corretto in un incremento
esplicito prima di continuare; stesso SHA add-in per prove positive/negative.
Slot .NET, Unity, Inventor e Quest gestiti serialmente dal coordinatore.
Attendere chiusura del run Quest corrente e snapshot del risultato76/80 prima
di cambiare la DLL usata dal dispositivo.

## Matrice PC nativa Parte/Lamiera

Minimo proposto: **16 workflow positivi** = due contesti × due start
(0°,170°) × quattro sweep (+90°,-90°,+270°,-270°). Se31 copre già una cella,
riutilizzare il suo oracle geometrico; il nuovo requisito è l'intero percorso
HTTPS/Core e la lettura immediata, non moltiplicare i medesimi confronti API.

Per workflow: fissare baseline completa e revision-bound; creare nello stesso
sketch esistente tramite bozza/Preview; verificare tipo nativo SketchArc,
centro/raggio/start/sweep, estremi, numero entità e relazioni realmente create;
verificare preview e Abort esatti di identità, geometria, quote, vincoli,
parametri, stili, Dirty e revisioni. Solo dopo conferma render sintetica PC
eseguire Apply e leggere lo snapshot **prima di qualunque getter COM/oracle**.
Confrontare poi geometria con l'oracle nativo separato e annullato, con
tolleranza dichiarata1e-5 mm sulle sole coordinate floating point; chiavi,
tipi, flag, cardinalità e relazioni restano esatti.

Dimostrare due modifiche successive della stessa entità senza ricreare lo
sketch: usare le rotte native già pubblicate `dimension_radius` e
`dimension_update` per raggio12→18 mm, verificando identità arco/centro/estremi,
solver e aggiornamento della geometria/strokes. Questo prova editing via
quota, **non** un editor completo di sweep, inferenze o dipendenze desktop.
Includere SaveCopy/reopen su almeno uno sweep maggiore per contesto e
riapplicare due modifiche al raggio dopo il riavvio della fixture.

Rifiuti da provare sui due contesti: raggio≤0, sweep0/±360, nonfinite,
documento/revisione errati, owner sketch errato e batch con operazione
successiva invalida. Il rollback post-creazione deve preservare anche altri
testi/parametri presenti nella fixture. Non considerare un semplice errore
di trasporto equivalente a rifiuto atomico verificato.

Misurare default, ordine centro/start/end, segno e orientamento contro i
campioni desktop31; se il campione pertinente manca, acquisire nuova baseline
desktop in fixture propria prima di dichiarare equivalenza di quel caso.
Non invocare ControlDefinition come implementazione del prodotto XR.

## Quest e limiti di consegna

Dopo PC: almeno quattro workflow, Parte/Lamiera × sweep+90/-270, usando
catalogo e tastierino reali; Preview/Cancel preserva baseline, Preview/Apply
mostra arco nativo e snapshot attuale, secondo comando conserva sessione e
ID, Finish non applica una bozza pendente. Il renderer va confrontato con
strokes/centro/estremi restituiti dal bridge; screenshot isolato non basta.
Se si esercita anche la penna, frame sintetici verificano routing e geometria,
non gesto fisico, tracking o usabilità. Bootstrap, target e documenti devono
essere quelli posseduti del run; nessun fallback al pairing ordinario.

Esito atteso: un verbale di sottoinsieme con PASS/FAIL/NOT COVERED per cella,
sorgenti e assembly effettivamente eseguiti, snapshot prima/dopo, log del
runner e ripristino manifest/APK/processo utente. Restano aperti Arco tangente,
linea↔arco continuo, inferenze complete, editing sweep e default/opzioni non
campionati, Quest fisico e tutti i 18 gate complessivi. Il piano81 non è una
prova eseguita e non autorizza dichiarazioni di completamento S1.
