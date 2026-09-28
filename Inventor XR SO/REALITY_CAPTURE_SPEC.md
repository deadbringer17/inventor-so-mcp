# Inventor XR SO
## Reality Capture & Metric Constraints
**Specifica funzionale e architetturale — v0.1**

### 1. Obiettivo

Aggiungere a Inventor XR SO una modalità **Reality Capture** per Meta Quest 3 che permetta all'utente di:

1. indossare il visore;
2. delimitare un oggetto, un macchinario o un'area reale;
3. acquisirla camminando e osservandola da più angolazioni;
4. visualizzare in tempo reale quali zone sono state acquisite e quali richiedono ulteriori punti di vista;
5. utilizzare riferimenti dimensionali fisici conosciuti per aumentare l'affidabilità metrica della ricostruzione;
6. aggiungere manualmente dimensioni note, ad esempio:
   - diametro tubo;
   - diametro foro;
   - distanza tra due punti;
   - distanza tra due piani;
   - larghezza di un profilato;
   - dimensione nota di un componente;
7. ricostruire point cloud, mesh e primitive geometriche;
8. ottenere un report di affidabilità dimensionale;
9. trasferire il risultato al workflow Inventor come riferimento geometrico e dimensionale.

Il sistema non deve presentare Quest 3 come scanner metrologico certificato. Deve invece produrre una ricostruzione **metricamente vincolata e verificabile**, indicando sempre qualità, residui e incertezza della soluzione.

---

# 2. Posizionamento nell'architettura esistente

L'architettura corrente viene preservata.

```text
                    INVENTOR XR SO

          ┌─────────────────────────────┐
          │        Meta Quest 3         │
          │                             │
          │ Inventor XR Unity Client    │
          │                             │
          │ ┌─────────────────────────┐ │
          │ │ CAD / Inventor XR       │ │
          │ └─────────────────────────┘ │
          │                             │
          │ ┌─────────────────────────┐ │
          │ │ REALITY CAPTURE         │ │
          │ │                         │ │
          │ │ Scan Area               │ │
          │ │ Scan Object             │ │
          │ │ Calibration             │ │
          │ │ Constraints             │ │
          │ │ Measurements            │ │
          │ │ Scan Review             │ │
          │ └─────────────────────────┘ │
          └─────────────┬───────────────┘
                        │
            HTTPS / authenticated
                        │
                        ▼
        ┌──────────────────────────────┐
        │ inventor-so-mcp HTTP Host    │
        │                              │
        │ /mcp                         │
        │ /assets                      │
        │ /capture                     │
        │                              │
        │ Session / auth / assets      │
        └──────────────┬───────────────┘
                       │
             Reconstruction Worker
                       │
        ┌──────────────▼───────────────┐
        │ Multi-view reconstruction   │
        │ Bundle adjustment           │
        │ Metric constraints          │
        │ Depth fusion optional       │
        │ Primitive fitting           │
        │ Mesh generation             │
        │ Quality estimation          │
        └──────────────┬───────────────┘
                       │
              ScanAsset / GLB / PLY
                       │
                       ▼
           inventor-so-mcp / Inventor
```

Il nuovo sistema **non sostituisce** l'attuale pipeline CAD→Quest.

La estende introducendo la direzione opposta:

```text
attuale:

Inventor
   ↓
MeshPayload
   ↓
GLB
   ↓
Quest


nuovo:

Quest
   ↓
Reality Capture
   ↓
Metric Reconstruction
   ↓
ScanAsset
   ↓
Inventor
```

---

# 3. Modalità di acquisizione

La prima versione dovrà prevedere almeno due modalità.

### Scan Object

Destinata a:

- componenti;
- motori;
- staffe;
- tubazioni;
- supporti;
- piccoli macchinari;
- parti di impianto.

L'utente deve muoversi attorno all'oggetto.

### Scan Area

Destinata a:

- macchinari industriali;
- porzioni di tetto;
- strutture FV;
- linee produttive;
- docking;
- canaline;
- aree di passaggio del robot;
- layout di installazione.

La ricostruzione può comprendere più metri e richiede maggiore attenzione a drift e riferimenti metrici distribuiti.

---

# 4. Pipeline di acquisizione Quest

Per ogni frame utilizzabile il sistema registra almeno:

```text
CaptureFrame
 ├── RGB image
 ├── camera timestamp
 ├── camera intrinsics
 ├── camera extrinsics
 ├── camera world pose
 ├── headset pose
 ├── tracking quality
 ├── optional depth information
 └── capture quality metadata
```

Il sistema non deve dipendere obbligatoriamente dalla disponibilità simultanea delle due camere RGB.

L'MVP deve essere in grado di funzionare con:

```text
1 RGB camera
+
camera pose
+
intrinsics
+
multi-view reconstruction
+
metric constraints
```

Depth, Scene API e ulteriori dati sensoriali devono essere considerati **fonti aggiuntive di informazione**, non prerequisite.

---

# 5. Quality Guidance durante la scansione

L'utente non deve semplicemente registrare un video.

Inventor XR deve guidare attivamente l'acquisizione.

Durante la scansione viene costruita una rappresentazione della copertura:

```text
VERDE     copertura adeguata
GIALLO    copertura debole
ROSSO     zona non acquisita
GRIGIO    ricostruzione non affidabile
```

La qualità deve considerare almeno:

- numero di osservazioni;
- variazione dell'angolo di vista;
- distanza dalla superficie;
- motion blur;
- esposizione;
- quantità di texture/features visive;
- tracking quality;
- baseline rispetto ai frame precedenti.

UI indicativa:

```text
SCAN OBJECT

Coverage             76 %
Metric anchors       3
Known dimensions     2
Tracking             GOOD
Image quality        GOOD

Missing views:
 → rear
 → bottom-right

[ FINISH SCAN ]
```

---

# 6. Calibration Objects

## 6.1 Principio

Viene introdotto un **Inventor XR Calibration Kit**.

Gli oggetti possono essere prodotti mediante stampa 3D, ma non devono fare affidamento esclusivamente sulla precisione nominale della stampa.

Ogni calibration object possiede:

```text
CalibrationArtifact
 ├── artifact_id
 ├── geometry_version
 ├── nominal_dimensions
 ├── measured_dimensions
 ├── fiducial geometry
 ├── fiducial IDs
 ├── point coordinates
 ├── uncertainty
 └── calibration date
```

La dimensione realmente misurata dell'oggetto ha priorità sulla dimensione nominale CAD.

Esempio:

```text
SO-CAL-001

Nominal:
100.000 mm

Measured:
99.82 mm

Measurement method:
digital caliper
```

Il solver utilizza **99.82 mm**, non 100 mm.

---

# 7. Geometria dei calibration object

Un semplice cubo monocromatico non è sufficiente.

Il riferimento deve permettere di determinare chiaramente:

- posizione;
- orientamento;
- scala;
- identificazione dell'oggetto;
- punti geometrici noti.

Configurazione consigliata:

```text
          ┌──────────────┐
         /   TAG 03     /|
        /              / |
       ┌──────────────┐  |
       │              │  |
       │    TAG 01    │  │
       │              │ /
       │              │/
       └──────────────┘
          TAG 02
```

Tre facce ortogonali consentono di osservare l'oggetto da diversi punti di vista.

Ogni marker ha coordinate note nel sistema dell'artefatto.

Il marker può utilizzare una tecnologia fiduciale tipo AprilTag/ArUco o equivalente.

---

# 8. Scale Bar

Per scansioni più grandi deve essere disponibile anche una barra dimensionale.

Esempio:

```text
●──────────────●──────────────●

A              B              C

A-B = 250.00 mm
B-C = 250.00 mm
A-C = 500.00 mm
```

I tre punti devono essere riconoscibili visivamente mediante marker.

La ridondanza A-B / B-C / A-C permette anche di rilevare errori del riferimento stesso.

Per scansioni di macchinari di diversi metri è preferibile distribuire più riferimenti nello spazio anziché utilizzare un unico riferimento in un angolo della scena.

---

# 9. Metric Constraints

Questa è la parte fondamentale del sistema.

Ogni informazione metrica viene convertita in un **MetricConstraint**.

Tipologie iniziali:

```text
KnownDistance
KnownDiameter
KnownRadius
KnownHoleDiameter
KnownCylinderDiameter
PlaneToPlaneDistance
CalibrationArtifact
ScaleBar
```

Ogni constraint contiene:

```text
constraint_id
type
value_mm
geometry_reference
source
uncertainty_mm
enabled
residual
status
```

---

# 10. Inserimento manuale di una distanza

L'utente può selezionare due punti:

```text
A ●────────────────────────────● B

       valore ricostruito
          502.8 mm
```

e dichiarare:

```text
KNOWN DISTANCE

500.00 mm
```

Il sistema non si limita a modificare il numero visualizzato.

La distanza entra nella successiva ottimizzazione geometrica.

---

# 11. Vincolo diametro tubo

Workflow:

```text
Add Constraint
   ↓
Known Diameter
   ↓
Select Tube
```

L'utente indica una porzione del tubo.

Il sistema raccoglie i punti appartenenti alla superficie e calcola un fitting cilindrico.

Viene mostrato:

```text
Detected cylinder

Estimated:
Ø 49.2 mm

Known diameter:
[ 48.3 ] mm

[ APPLY CONSTRAINT ]
```

Viene creato:

```text
KnownCylinderDiameter
value_mm = 48.3
```

Il solver utilizza il diametro per correggere la soluzione globale.

La geometria risultante fornisce inoltre:

```text
axis
radius
centre
length
confidence
```

che possono successivamente essere convertiti in geometria di riferimento Inventor.

---

# 12. Vincolo diametro foro

Workflow analogo:

```text
Select rim
   ↓
Fit plane
   ↓
Fit circle
   ↓
Known diameter
```

Output:

```text
Hole_004

centre:
X 1482.4
Y 322.1
Z 941.2 mm

normal:
0.002
0.999
0.014

diameter:
12.00 mm

source:
manual constraint
```

Il foro diventa quindi un'entità semantica della scansione e non soltanto un gruppo di triangoli.

---

# 13. Metric Constraint Solver

Le dimensioni note non devono essere applicate mediante una deformazione arbitraria della mesh.

Pipeline:

```text
Quest camera poses
       +
Visual feature matching
       +
Calibration artifacts
       +
Manual metric constraints
       ↓
Constrained Bundle Adjustment
       ↓
Optimised camera poses
       ↓
Metric point cloud
       ↓
Dense reconstruction
```

In particolare, il solver può correggere:

- scala;
- camera poses;
- pose graph;
- loop closure;
- piccoli errori accumulati.

Non deve invece deformare localmente una superficie soltanto per costringerla a soddisfare una quota.

---

# 14. Conflitto tra misure

Esempio:

```text
Constraint A
500.00 mm

Constraint B
501.20 mm

Constraint C
499.95 mm
```

Il sistema deve rilevare l'incompatibilità.

Non deve scegliere silenziosamente un valore.

UI:

```text
METRIC CONSTRAINT CONFLICT

Constraint B produces a high residual.

Measured         501.20 mm
Solved           500.06 mm
Residual           1.14 mm

[DISABLE]
[EDIT]
[KEEP AS SOFT CONSTRAINT]
```

---

# 15. Reconstruction Pipeline

Pipeline proposta:

```text
RAW CAPTURE
   │
   ├── RGB frames
   ├── intrinsics
   ├── extrinsics
   ├── camera poses
   ├── tracking quality
   └── optional depth
   │
   ▼
FRAME SELECTION
   │
   ├── blur rejection
   ├── exposure rejection
   └── redundant frame removal
   │
   ▼
FEATURE EXTRACTION
   │
   ▼
MULTI-VIEW MATCHING
   │
   ▼
QUEST POSE INITIALIZATION
   │
   ▼
LOOP CLOSURE
   │
   ▼
CALIBRATION ARTIFACT DETECTION
   │
   ▼
METRIC CONSTRAINT SOLVER
   │
   ▼
BUNDLE ADJUSTMENT
   │
   ▼
OPTIONAL DEPTH FUSION
   │
   ▼
DENSE POINT CLOUD
   │
   ▼
SURFACE RECONSTRUCTION
   │
   ▼
PRIMITIVE DETECTION
   │
   ├── planes
   ├── cylinders
   ├── circles
   ├── holes
   └── axes
   │
   ▼
MESH OPTIMIZATION
   │
   ├── cleanup
   ├── decimation
   └── normals
   │
   ▼
QUALITY REPORT
```

---

# 16. Depth

Depth API deve essere utilizzata come supporto per:

- inizializzazione della geometria;
- disambiguazione;
- foreground/background;
- miglioramento dell'anteprima;
- occlusione;
- eventuale depth fusion.

Non deve essere considerata automaticamente una misura metrologica.

La ricostruzione deve funzionare anche quando il contributo depth viene disabilitato.

---

# 17. Primitive Reconstruction

Una funzione importante per l'integrazione CAD è trasformare parti della point cloud in primitive geometriche.

Tipi iniziali:

```text
Plane
Cylinder
Circle
Axis
Point
BoundingBox
```

Esempio:

```text
DetectedPrimitive

type: cylinder
diameter_mm: 48.30
axis_origin_mm:
  [123.2, 840.5, 1820.4]

axis_direction:
  [0.001, 0.999, -0.002]

length_mm: 724.2

source:
  geometry_fit + KnownDiameterConstraint

confidence:
  calibrated
```

Questo è molto più utile in Inventor rispetto a una sola mesh triangolare.

---

# 18. Scan coordinate systems

Devono essere distinti chiaramente:

```text
QuestTrackingSpace
CaptureSpace
ScanMetricSpace
InventorModelSpace
GLTFSpace
```

Convenzioni:

```text
Capture / CAD metadata → millimetri
GLB                   → metri
```

come già avviene nell'attuale pipeline GLB del progetto.

Ogni trasformazione tra coordinate deve essere memorizzata esplicitamente mediante matrice 4×4.

---

# 19. Scan Session

Ogni acquisizione costituisce una sessione immutabile nei raw data.

```text
ScanSession
 ├── session_id
 ├── capture_mode
 ├── device
 ├── application_version
 ├── created_at
 ├── frames
 ├── calibration observations
 ├── constraints
 ├── solve revisions
 └── generated assets
```

I frame originali non vengono modificati dopo l'acquisizione.

Una modifica dei constraint genera invece:

```text
SolveRevision 1
SolveRevision 2
SolveRevision 3
```

senza richiedere una nuova scansione.

---

# 20. Asset risultanti

Ogni soluzione può produrre:

```text
scan.glb
scan.ply
scan.obj
pointcloud.*
primitives.json
constraints.json
quality_report.json
capture_manifest.json
```

`scan.glb` viene utilizzata principalmente da XR.

Gli altri formati vengono utilizzati dal workflow CAD/reality capture.

OBJ e altri formati privi di unità intrinseche devono essere sempre accompagnati da metadata che dichiarino esplicitamente l'unità.

---

# 21. Quality Report

Ogni scansione deve produrre un rapporto dimensionale.

Esempio:

```text
REALITY CAPTURE QUALITY

Frames used             482
Rejected frames          71

Calibration objects       3
Metric constraints        5

Loop closure             YES

Constraint RMS           1.2 mm
Maximum residual         2.8 mm

Coverage                  94 %

Weak areas:
 - machine rear lower section
 - pipe P04 behind bracket

Scale:
 constrained

Accuracy claim:
 CALIBRATED
```

Il software non deve trasformare automaticamente il valore RMS in una certificazione metrologica.

---

# 22. Accuracy Status

Ogni scansione deve avere uno stato esplicito:

```text
UNSCALED
METRIC
CALIBRATED
VALIDATED
```

### UNSCALED

Ricostruzione senza sufficiente riferimento metrico.

### METRIC

Scala derivata dal tracking Quest e/o depth.

### CALIBRATED

Sono presenti calibration artifacts o quote note e il solver converge correttamente.

### VALIDATED

La scansione è stata confrontata con ulteriori dimensioni di controllo indipendenti non utilizzate nel solver.

Solo `VALIDATED` permette di dichiarare un errore dimensionale misurato rispetto a ground truth.

---

# 23. Calibration vs Validation

È importante non utilizzare tutte le quote note per calibrare il modello.

Alcune devono rimanere **validation dimensions**.

Esempio:

```text
Known measurements = 8

Used for calibration = 5
Reserved validation  = 3
```

Dopo la soluzione:

```text
Validation #1
true     500.00
scan     500.82
error      0.82 mm

Validation #2
true      48.30
scan      48.51
error      0.21 mm
```

Questo consente di misurare realmente la qualità della scansione.

---

# 24. UI Reality Capture

Menu principale:

```text
INVENTOR XR SO

Inventor
Reality Capture
Projects
Settings
```

Reality Capture:

```text
New Scan
 ├── Object
 └── Area

Open Scan

Calibration Kit

Measurements
```

Durante acquisizione:

```text
┌────────────────────────────┐
│ SCAN AREA                  │
│                            │
│ Coverage          82 %     │
│ Calibration       3/3      │
│ Tracking          GOOD     │
│                            │
│ 🔴 missing geometry        │
│ 🟢 captured geometry       │
│                            │
│ + ADD KNOWN DIMENSION      │
│                            │
│ PAUSE       FINISH         │
└────────────────────────────┘
```

---

# 25. Measurement Mode

Dopo la ricostruzione:

```text
MEASURE

Point to Point
Diameter
Radius
Cylinder
Hole
Plane Distance
Angle
```

Una misura proveniente esclusivamente dalla scansione deve essere visualizzata insieme alla sua affidabilità.

Esempio:

```text
842.6 mm
± estimated 2.1 mm
```

Una quota derivata da un `KnownConstraint` deve essere chiaramente identificata:

```text
48.30 mm
CALIBRATION CONSTRAINT
```

---

# 26. Inventor Integration

L'integrazione deve essere sviluppata su due livelli.

## Livello 1 — Reality Reference

Importazione del risultato come riferimento:

```text
RealityScan
 ├── mesh
 ├── point cloud
 └── measurements
```

Serve per:

- ingombri;
- layout;
- interferenze;
- adattamenti;
- progettazione intorno ad oggetti reali.

## Livello 2 — Semantic Geometry

Le primitive riconosciute vengono convertite in geometria CAD di riferimento.

Esempio:

```text
Scanned Plane
      ↓
Inventor Work Plane

Cylinder Axis
      ↓
Inventor Work Axis

Hole Center
      ↓
Inventor Work Point

Known Diameter
      ↓
Inventor Parameter
```

L'attuale infrastruttura `inventor-so-mcp` possiede già il concetto di work geometry e operazioni atomiche; questa funzione deve riutilizzarlo.

---

# 27. Reality Reference Part

Workflow futuro consigliato:

```text
Reality Scan
     ↓
[ CREATE INVENTOR REFERENCE ]
     ↓
workspace_new_document
     ↓
Reality_<scan_id>.ipt
     ↓
Work planes
Work axes
Work points
Parameters
     ↓
Optional mesh reference
```

Il risultato non deve essere presentato come B-Rep ricostruita automaticamente.

È un **as-built reference model**.

---

# 28. Allineamento Scan ↔ CAD

Se esiste già un modello Inventor, l'utente deve poter registrare la scansione rispetto al CAD.

Metodi:

```text
3 Point Alignment
Plane + Axis
Known Feature Matching
Calibration Artifact
```

Esempio:

```text
SCAN                              CAD

Plane A  ───────────────────────► WorkPlane1
Axis B   ───────────────────────► ShaftAxis
Point C  ───────────────────────► HoleCentre
```

Il sistema calcola:

```text
T_scan_to_inventor
```

e conserva sempre questa matrice.

---

# 29. Overlay CAD sul mondo reale

Lo stesso allineamento consente anche la direzione inversa:

```text
Inventor CAD
    ↓
Quest
    ↓
overlay sul macchinario reale
```

Una volta registrato ScanMetricSpace ↔ InventorModelSpace, il modello CAD può essere visualizzato sopra l'oggetto fisico.

Questo consente:

- verifica installazione;
- confronto as-designed / as-built;
- controllo ingombri;
- visualizzazione di componenti futuri;
- progettazione di staffe e adattatori;
- posizionamento robot e docking.

---

# 30. Trasporto dati

I frame della camera non devono attraversare MCP come grandi JSON/base64.

L'attuale limite e filosofia del server devono essere preservati.

MCP gestisce:

```text
session control
constraints
status
metadata
results
```

Un endpoint HTTP dedicato gestisce i dati binari.

Esempio:

```text
POST /capture/v1/sessions
PUT  /capture/v1/sessions/{id}/chunks/{n}
POST /capture/v1/sessions/{id}/finalize
GET  /capture/v1/sessions/{id}/assets/{asset}
```

Ogni chunk contiene:

```text
sha256
sequence
size
session_id
```

e viene validato prima della pubblicazione.

---

# 31. Sicurezza

Le immagini possono contenere ambienti industriali sensibili.

Default:

```text
local processing
no cloud upload
authenticated client
token-bound ownership
HTTPS on LAN
content hashes
explicit retention policy
```

Le sessioni di due token HTTP differenti non devono poter leggere reciprocamente:

- raw frames;
- scansioni;
- constraint;
- asset.

La policy segue quindi la stessa filosofia dell'attuale `AssetStore`.

---

# 32. Nuovi moduli Unity

Nel repository Unity:

```text
Assets/
└── InventorXR/
    └── RealityCapture/
        ├── Capture/
        │   ├── RealityCaptureController.cs
        │   ├── QuestCameraSource.cs
        │   ├── FrameRecorder.cs
        │   └── CaptureQualityEvaluator.cs
        │
        ├── Calibration/
        │   ├── CalibrationArtifact.cs
        │   ├── FiducialDetector.cs
        │   ├── ArtifactDatabase.cs
        │   └── ArtifactPoseSolver.cs
        │
        ├── Constraints/
        │   ├── MetricConstraint.cs
        │   ├── DistanceConstraint.cs
        │   ├── DiameterConstraint.cs
        │   ├── CylinderConstraint.cs
        │   └── ConstraintEditor.cs
        │
        ├── Coverage/
        │   ├── CoverageMap.cs
        │   └── CoverageRenderer.cs
        │
        ├── Network/
        │   ├── CaptureSessionClient.cs
        │   ├── CaptureUploader.cs
        │   └── InventorMcpClient.cs
        │
        ├── Review/
        │   ├── ScanViewer.cs
        │   ├── MeasurementTool.cs
        │   └── PrimitiveViewer.cs
        │
        └── UI/
            ├── RealityCaptureMenu.cs
            ├── ScanHud.cs
            └── ConstraintPanel.cs
```

---

# 33. Backend modules

Nel progetto principale:

```text
bridge/src/server/
└── RealityCapture/
    ├── CaptureSessionStore
    ├── CaptureAssetStore
    ├── ConstraintStore
    ├── ReconstructionCoordinator
    ├── ReconstructionResult
    └── RealityCaptureTools
```

Il calcolo pesante deve essere separato dal processo MCP:

```text
ReconstructionCoordinator
        │
        ▼
Local Reconstruction Worker
```

Il crash o il consumo elevato di memoria del reconstruction worker non deve compromettere il processo che controlla Inventor.

---

# 34. MCP surface proposta

Nuovi tool iniziali:

```text
inventor_reality_capture_create_session
inventor_reality_capture_get_session
inventor_reality_capture_add_constraint
inventor_reality_capture_remove_constraint
inventor_reality_capture_list_constraints
inventor_reality_capture_start_solve
inventor_reality_capture_get_solution
inventor_reality_capture_get_quality
inventor_reality_capture_export
```

Successivamente:

```text
inventor_reality_capture_align_to_cad
inventor_reality_capture_create_reference
```

Le operazioni che modificano Inventor devono seguire le regole già esistenti:

- document identity;
- expected revision;
- workspace;
- preview;
- transaction;
- rollback;
- validation.

---

# 35. Primitive output contract

Formato indicativo:

```json
{
  "primitive_id": "prim_17",
  "type": "cylinder",
  "coordinate_space": "scan_metric",
  "axis_origin_mm": [123.2, 840.5, 1820.4],
  "axis_direction": [0.001, 0.999, -0.002],
  "diameter_mm": 48.3,
  "length_mm": 724.2,
  "constraint_ids": ["constraint_04"],
  "fit_rms_mm": 0.84,
  "status": "calibrated"
}
```

---

# 36. Validation bench

Prima di attribuire qualsiasi accuratezza al sistema deve essere costruito un test bench.

Elementi:

```text
calibration bars
cubes / rectangular blocks
known hole plate
pipes of known diameter
planes at known distance
angled surfaces
large reference frame
```

Le dimensioni di ground truth devono essere ottenute indipendentemente dalla scansione.

Testare almeno:

- differenti distanze;
- differenti illuminazioni;
- superfici matte;
- superfici metalliche;
- superfici poco texturizzate;
- geometria parzialmente occlusa;
- scansioni ripetute dallo stesso operatore;
- scansioni ripetute da operatori diversi.

Metriche:

```text
absolute dimensional error
relative dimensional error
RMS
P95
repeatability
loop drift
constraint residual
validation residual
```

---

# 37. Superfici problematiche

Il sistema deve segnalare esplicitamente condizioni che degradano la ricostruzione:

```text
reflective
transparent
uniform / textureless
very dark
overexposed
moving object
insufficient viewpoints
```

In ambiente industriale può essere prevista la possibilità di applicare marker temporanei removibili per migliorare il tracking di superfici uniformi.

---

# 38. MVP

L'MVP non deve cercare subito di diventare uno scanner industriale completo.

### MVP 1 — Capture

- camera Quest;
- posa/intrinseci;
- registrazione frame;
- Scan Object;
- Scan Area;
- coverage indicator;
- upload sessione.

### MVP 2 — Metric reconstruction

- multi-view reconstruction;
- calibration artifact;
- known point-to-point distance;
- known diameter;
- bundle adjustment;
- mesh;
- point cloud;
- quality report.

### MVP 3 — CAD reference

- primitive fitting;
- plane;
- axis;
- cylinder;
- circle/hole;
- export;
- visualizzazione mesh nel workflow Inventor XR.

### MVP 4 — Inventor integration

- scan ↔ CAD alignment;
- creazione reference part;
- work planes;
- work axes;
- work points;
- parametri;
- overlay CAD/as-built.

---

# 39. Definition of Done per il primo MVP utile

La funzione è considerata utilizzabile quando un operatore può:

```text
1. indossare Quest 3;

2. avviare:
   Reality Capture → New Scan;

3. posizionare almeno un calibration artifact;

4. acquisire un oggetto girandoci intorno;

5. vedere la coverage map;

6. aggiungere almeno una dimensione nota;

7. terminare l'acquisizione;

8. ottenere:
   - point cloud;
   - mesh;
   - scale metric;
   - quality report;

9. misurare due punti sulla ricostruzione;

10. visualizzare la scansione nel workflow Inventor XR.
```

---

# 40. Regola fondamentale del modulo

Il sistema deve distinguere sempre:

```text
GEOMETRIA OSSERVATA
GEOMETRIA STIMATA
MISURA CONOSCIUTA
MISURA VALIDATA
```

Non deve mai trasformare una misura stimata in una misura certa soltanto perché è visualizzata all'interno di Inventor.

La forza del sistema deve derivare dalla combinazione:

```text
Quest tracking
+
multi-view vision
+
calibration objects
+
manual metric constraints
+
independent validation
```

e non da una singola sorgente sensoriale.

---

# 41. Visione finale

Il modulo Reality Capture trasforma Inventor XR SO da semplice interfaccia XR per Inventor in un sistema bidirezionale:

```text
DIGITALE ───────────────► REALE

Inventor CAD
     ↓
Quest
     ↓
visualizzazione / modifica / overlay


REALE ──────────────────► DIGITALE

macchinario
     ↓
Quest
     ↓
Reality Capture
     ↓
Metric constraints
     ↓
As-built geometry
     ↓
Inventor
```

L'obiettivo non è sostituire uno scanner metrologico professionale.

L'obiettivo è rendere il Quest uno strumento di **engineering reality capture**, capace di portare rapidamente nel CAD geometria reale, riferimenti, ingombri e misure controllate, con un livello di affidabilità dichiarato e verificabile.
