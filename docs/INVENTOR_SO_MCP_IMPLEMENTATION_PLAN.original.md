# inventor-so-mcp — Piano di implementazione evolutivo

**Repository:** `deadbringer17/inventor-so-mcp`  
**Target principale:** Autodesk Inventor 2027 x64  
**Obiettivo:** evolvere `inventor-so-mcp` da bridge CAD sicuro a **API CAD semantica completa**, utilizzabile da client desktop, agenti AI e client XR come Meta Quest 3.

---

## 1. Obiettivo architetturale

L'obiettivo non è esporre direttamente tutta la COM API di Inventor tramite centinaia di tool MCP.

L'obiettivo è costruire una superficie semantica stabile, sicura e adatta agli agenti:

```text
Client
├── ChatGPT / Claude / altri agenti
├── Desktop CAD assistant
├── Meta Quest 3 / XR
└── Automazioni / batch
        │
        ▼
inventor-so-mcp
├── Query layer
├── Action layer
├── XR / Visualization layer
├── Validation engine
├── Semantic CAD state
└── Atomic execution engine
        │
        ▼
Inventor SO Add-in
        │
        ▼
Autodesk Inventor 2027
```

Principi obbligatori:

1. **Persistent entity IDs**
2. **Document revision checking**
3. **Fail-closed target selection**
4. **Preview prima del commit**
5. **Transaction + rollback**
6. **Validazione automatica**
7. **Nessuna scrittura diretta non protetta**
8. **Audit delle operazioni**
9. **Separazione tra CAD control e trasferimento asset**
10. **Compatibilità futura con client XR e rete**

---

# 2. Stato attuale da preservare

La superficie production attuale espone circa 40 tool tramite `SoToolPolicy`.

Le capacità già presenti e da mantenere come base includono:

- selezione esplicita dell'istanza Inventor;
- health check;
- gestione documenti safe;
- persistent entity references;
- lettura parametri;
- proprietà fisiche;
- topologia;
- `inventor_atomic_batch`;
- preview / rollback;
- checkpoint;
- diff checkpoint;
- gestione workspace;
- sheet metal;
- vincoli di assieme;
- joint;
- BOM;
- interference check;
- min distance;
- export sicuro;
- drawing safe;
- template aziendali.

Esistono inoltre tool legacy/non ancora production per:

- sketch;
- extrude;
- revolve;
- fillet;
- chamfer;
- hole;
- pattern;
- work plane;
- work axis;
- material;
- viewport;
- export.

Queste capacità non devono essere semplicemente aggiunte alla allowlist: vanno migrate nella superficie safe.

---

# 3. Architettura target

```text
                    ┌──────────────────────┐
                    │      CLIENTS         │
                    │                      │
                    │ AI / Desktop / XR    │
                    └──────────┬───────────┘
                               │
                     stdio / HTTPS MCP
                               │
                               ▼
                  ┌─────────────────────────┐
                  │ Inventor SO MCP Server  │
                  │                         │
                  │ Query                   │
                  │ Actions                 │
                  │ Planning                │
                  │ Validation              │
                  │ Semantic State          │
                  │ XR Gateway              │
                  └────────────┬────────────┘
                               │
                         Named Pipe
                               │
                               ▼
                  ┌─────────────────────────┐
                  │ Inventor SO Add-in      │
                  │                         │
                  │ STA dispatcher          │
                  │ COM adapters            │
                  │ transactions            │
                  │ entity resolver         │
                  └────────────┬────────────┘
                               │
                               ▼
                        Autodesk Inventor
```

---

# 4. Nuovi moduli consigliati

Struttura proposta:

```text
bridge/src/
│
├── server/
│   ├── Tools/
│   ├── Resources/
│   ├── Planning/
│   ├── Validation/
│   ├── Semantics/
│   └── Audit/
│
├── server-http/
│   └── Inventor.So.Mcp.Http
│
├── plugin-so27/
│   ├── Handlers/
│   ├── Visualization/
│   ├── Semantics/
│   └── Validation/
│
├── shared/
│   ├── Contracts/
│   ├── Transport/
│   ├── Semantics/
│   └── Validation/
│
└── tests/
```

---

# 5. Fase 1 — XR / Visualization Foundation

Questa fase abilita Meta Quest 3 senza modificare il kernel CAD esistente.

## 5.1 Nuovi tool MCP

### `inventor_get_display_mesh`

Restituisce un riferimento a una mesh visualizzabile.

Output indicativo:

```json
{
  "document_id": "doc_...",
  "revision": "218",
  "visual_revision": "vr_219",
  "asset_id": "mesh_b71...",
  "asset_url": "/assets/mesh_b71.glb",
  "sha256": "...",
  "units": "mm"
}
```

### `inventor_get_scene_graph`

Restituisce:

- struttura assieme;
- occurrence;
- transform;
- visibility;
- suppression state;
- document identity;
- mesh asset associato;
- bounding box.

### `inventor_get_visual_revision`

Permette al client di capire se deve aggiornare il modello.

### `inventor_highlight_entity`

Input:

```json
{
  "entity_id": "ent_...",
  "mode": "highlight"
}
```

### `inventor_set_visibility`

Supportare:

- entity;
- body;
- occurrence;
- subtree.

### `inventor_focus_entity`

Porta la camera Inventor sulla selezione.

### `inventor_get_camera`

Legge:

- eye;
- target;
- up;
- projection;
- field of view.

### `inventor_set_camera`

Scrittura protetta della camera.

### `inventor_raycast_entity`

Converte un ray del client XR in una entità Inventor.

---

# 6. Mesh pipeline per Quest

Il trasferimento geometrico non deve passare dentro le response MCP principali.

Separazione proposta:

```text
MCP
│
├── document state
├── entity IDs
├── transforms
└── asset URL

HTTP Assets
│
└── GLB / glTF
```

## 6.1 Pipeline

```text
Inventor SurfaceBody
        │
        ▼
CalculateFacets()
        │
        ├── vertices
        ├── normals
        ├── indices
        └── face ownership
        │
        ▼
Mesh builder
        │
        ▼
GLB
        │
        ▼
Quest / Unity
```

## 6.2 Requisiti mesh

Ogni mesh deve mantenere una relazione con:

- `document_id`;
- `occurrence_id`;
- `body_id`;
- `face_id`;
- `visual_revision`.

Idealmente ogni triangle group deve poter essere ricondotto a una `Face`.

---

# 7. Fase 2 — Sketch completo

Il sistema deve poter creare e modificare sketch senza richiedere scripting arbitrario.

## 7.1 Inspection

Nuovi tool/capability:

- `inventor_get_sketch_info`
- `inventor_list_sketch_entities`
- `inventor_list_sketch_constraints`
- `inventor_get_sketch_dof`
- `inventor_find_closed_profiles`

Output minimo:

- entity IDs;
- geometry type;
- coordinates;
- constraints;
- dimensions;
- driving/driven;
- fully constrained state;
- profile loops.

## 7.2 Editing

Comandi supportati in `inventor_atomic_batch`:

```text
draw_line
draw_arc
draw_circle
draw_ellipse
draw_spline
draw_slot
draw_polygon
trim
extend
offset
mirror_sketch_entities
delete_sketch_entity
```

## 7.3 Constraint

```text
coincident
parallel
perpendicular
tangent
equal
horizontal
vertical
concentric
symmetry
midpoint
fix
```

## 7.4 Dimensional constraints

```text
distance
horizontal_distance
vertical_distance
angle
radius
diameter
arc_length
```

Tutte le quote devono poter essere:

- nominate;
- lette;
- modificate;
- eliminate;
- impostate driving/driven.

---

# 8. Fase 3 — Feature solide

Aggiungere al catalogo interno di `inventor_atomic_batch`:

```text
sweep
loft
shell
rib
draft
split
thicken
thread
emboss
mirror_feature
mirror_body
rectangular_pattern
body_pattern
combine
move_body
copy_body
delete_body
```

## 8.1 Regola

Le feature di modifica non devono essere esposte come write tool indipendenti se evitabile.

Preferire:

```text
inventor_atomic_batch
```

con catalogo dichiarativo di operazioni.

---

# 9. Fase 4 — Work geometry

Implementare CRUD sicuro di:

- work plane;
- work axis;
- work point;
- UCS / coordinate system.

Comandi:

```text
create_work_plane
create_work_axis
create_work_point
rename_work_geometry
delete_work_geometry
```

Ogni work feature deve avere persistent ID.

---

# 10. Fase 5 — Parametri e dipendenze

Aggiungere:

```text
inventor_create_parameter_safe
inventor_edit_parameter_safe
inventor_rename_parameter_safe
inventor_delete_parameter_safe
inventor_get_parameter_dependencies
inventor_trace_parameter_dependency
```

Il sistema deve distinguere:

- model parameter;
- user parameter;
- reference parameter;
- expression;
- unit;
- dependencies;
- dependents.

---

# 11. Fase 6 — Assembly avanzato

Funzioni da aggiungere:

```text
inventor_replace_component_safe
inventor_duplicate_component_safe
inventor_pattern_component_safe
inventor_suppress_component_safe
inventor_set_component_visibility
inventor_get_degrees_of_freedom
inventor_get_constraint_health
inventor_get_joint_health
```

## 11.1 Motion

Nuovi tool:

```text
inventor_sample_joint_motion
inventor_check_motion_collision
inventor_find_collision_free_range
```

La validazione deve distinguere:

- stato finale valido;
- traiettoria valida;
- interferenza;
- clearance minima.

---

# 12. Fase 7 — Model States e Representations

Aggiungere:

```text
inventor_list_model_states
inventor_create_model_state_safe
inventor_activate_model_state
inventor_edit_model_state_safe

inventor_list_design_views
inventor_activate_design_view
inventor_list_positional_representations
inventor_activate_positional_representation
```

---

# 13. Fase 8 — Drawing completo

Il tool attuale `inventor_create_drawing_safe` è una buona base.

Va esteso con tool semantici dedicati.

## 13.1 Sheets

```text
inventor_list_sheets
inventor_add_sheet_safe
inventor_remove_sheet_safe
inventor_activate_sheet
```

## 13.2 Views

```text
inventor_add_base_view
inventor_add_projected_view
inventor_add_section_view
inventor_add_detail_view
inventor_move_drawing_view
inventor_set_view_scale
```

## 13.3 Dimensions

```text
inventor_add_linear_dimension
inventor_add_angular_dimension
inventor_add_radius_dimension
inventor_add_diameter_dimension
inventor_add_ordinate_dimension
```

## 13.4 Annotation

```text
inventor_add_note
inventor_add_leader_note
inventor_add_hole_note
inventor_add_thread_note
inventor_add_center_mark
inventor_add_centerline
```

## 13.5 GD&T

```text
inventor_add_datum
inventor_add_feature_control_frame
inventor_add_surface_texture
```

## 13.6 Parts list

```text
inventor_add_parts_list
inventor_add_balloon
inventor_edit_item_number
```

## 13.7 Validation

```text
inventor_validate_drawing
```

Controlli:

- reference health;
- drawing update status;
- overlap;
- missing model references;
- dangling annotations;
- missing parts list;
- missing balloons;
- empty views;
- sheet bounds.

---

# 14. Fase 9 — BOM intelligence

Espandere `inventor_get_assembly_bom`.

Nuove operazioni:

```text
inventor_set_bom_structure
inventor_set_part_number_safe
inventor_set_item_number_safe
inventor_compare_bom
inventor_validate_bom
```

Aggiungere:

- structured BOM;
- parts-only BOM;
- purchased;
- phantom;
- reference;
- inseparable;
- quantity overrides;
- duplicate part-number detection.

---

# 15. Fase 10 — Content Center e fasteners

Implementare:

```text
inventor_search_content_center
inventor_insert_content_center_part
inventor_find_fastener_for_hole
inventor_place_fastener_stack
inventor_validate_fastener_stack
```

La selezione bulloneria dovrebbe usare:

- hole diameter;
- thread;
- grip length;
- available standard lengths;
- washer;
- nut;
- interference;
- minimum engagement.

---

# 16. Fase 11 — Import / Export / Release

## 16.1 Import

```text
inventor_import_step_safe
inventor_import_iges_safe
inventor_import_sat_safe
inventor_import_stl_safe
```

## 16.2 Export

Completare:

- STEP;
- STL;
- SAT;
- IGES;
- DXF;
- DWG;
- PDF;
- DWF;
- image;
- GLB/glTF.

## 16.3 Release package

Nuovo tool:

```text
inventor_build_release_package
```

Output:

```text
release/
├── model.step
├── drawing.pdf
├── flat.dxf
├── bom.csv
├── metadata.json
├── checksums.sha256
└── validation.json
```

---

# 17. Fase 12 — Semantic CAD State

Nuovi tool:

```text
inventor_get_semantic_state
inventor_get_entity_relations
inventor_get_dependencies
inventor_trace_dependency
```

## 17.1 Grafo semantico

```text
parameter
    ↓ drives
feature
    ↓ generates
face
    ↓ participates in
constraint
    ↓ connects
occurrence
```

Ogni nodo deve avere:

- stable ID;
- type;
- name;
- document;
- revision;
- metadata;
- incoming relations;
- outgoing relations.

---

# 18. Fase 13 — Change Plan

Aggiungere:

```text
inventor_plan_change
```

Esempio output:

```json
{
  "document_revision": "218",
  "intent": "increase shaft diameter to 25 mm",
  "operations": [
    {
      "command": "set_parameter",
      "parameter": "ShaftDiameter",
      "old": "20 mm",
      "new": "25 mm"
    }
  ],
  "affected_features": [
    "Extrusion1",
    "Fillet3"
  ],
  "validation": [
    "rebuild",
    "feature_health",
    "clearance"
  ]
}
```

Workflow:

```text
PLAN
 ↓
PREVIEW
 ↓
DIFF
 ↓
USER APPROVAL
 ↓
COMMIT
```

---

# 19. Fase 14 — Validation Engine comune

Creare un modulo:

```text
Inventor.So.Validation
```

Validator:

```text
RebuildValidator
FeatureHealthValidator
SketchConstraintValidator
ReferenceValidator
AssemblyConstraintValidator
InterferenceValidator
ClearanceValidator
DrawingValidator
SheetMetalValidator
ReleaseValidator
```

Ogni comando write può dichiarare:

```json
{
  "validate": [
    "rebuild",
    "feature_health",
    "interference",
    "min_clearance:2mm"
  ]
}
```

---

# 20. Fase 15 — Eventi e subscription

L'attuale journal eventi va esteso.

Eventi:

```text
document_opened
document_closed
document_activated
document_changed
document_saved
selection_changed
camera_changed
model_rebuilt
parameter_changed
assembly_changed
revision_changed
```

Per HTTP MCP implementare notifiche/subscription quando supportato dal transport.

Il client Quest deve poter fare:

```text
Quest
  ↓
subscribe document
  ↓
Inventor changes
  ↓
new revision
  ↓
refresh only changed assets
```

---

# 21. Fase 16 — Remote MCP / Quest

Aggiungere un secondo host:

```text
bridge/src/server-http/
```

con:

```text
Inventor.So.Mcp.Http
```

Supporto:

- MCP Streamable HTTP;
- HTTPS;
- authentication;
- access token;
- configurable bind address;
- CORS ristretto;
- rate limiting;
- session management;
- audit.

Non esporre direttamente il named pipe sulla rete.

Architettura:

```text
Quest
   │
 HTTPS
   ▼
Inventor.So.Mcp.Http
   │
shared server services
   ▼
PluginClient
   │
Named Pipe
   ▼
Inventor Add-in
```

---

# 22. Fase 17 — Asset server XR

Aggiungere endpoint:

```text
GET /assets/{asset_id}
```

Tipi asset:

```text
.glb
.png
.pdf
.csv
.json
```

Controlli:

- token;
- ownership della sessione;
- expiry;
- no arbitrary file path;
- no directory traversal;
- MIME allowlist;
- size limit;
- SHA-256.

---

# 23. Fase 18 — Audit log

Ogni modifica deve registrare:

```json
{
  "timestamp": "...",
  "session_id": "...",
  "client": "quest",
  "document_id": "...",
  "revision_before": "...",
  "revision_after": "...",
  "operation": "...",
  "preview": false,
  "validation": "...",
  "result": "committed"
}
```

Non memorizzare token di autenticazione.

---

# 24. Politica di esposizione tool

Non puntare a centinaia di MCP tool atomici.

Target consigliato:

```text
MCP visible surface
├── Query          ~30
├── Actions        ~30
├── Planning       ~10
├── XR             ~10
└── Release        ~10
```

Totale indicativo:

```text
80–120 tool MCP semantici
```

Sotto:

```text
atomic_batch
├── 40+ part commands
├── 30+ assembly commands
├── 30+ drawing commands
└── sheet-metal commands
```

---

# 25. Tool discovery

Aggiungere:

```text
inventor_get_capabilities
inventor_get_tool_schema
```

Output capability:

```json
{
  "inventor_version": 2027,
  "capabilities": {
    "sheet_metal": true,
    "drawings": true,
    "xr_mesh": true,
    "content_center": false,
    "motion_validation": false
  }
}
```

Questo permette al client di non assumere capability non presenti.

---

# 26. Regole di sicurezza

## 26.1 Sempre obbligatorio per write

- `document_id`;
- `expected_revision`;
- operation allowlist;
- bounded input;
- transaction ownership;
- rebuild;
- health validation;
- rollback on failure.

## 26.2 Vietato

- path arbitrari;
- `execute_python` in production;
- raw COM invocation;
- unrestricted iLogic editing;
- direct filesystem writes;
- automatic instance fallback;
- commit senza revision check;
- network exposure del pipe;
- arbitrary DLL loading.

---

# 27. Test strategy

Ogni capability deve avere quattro livelli di test.

## L1 — Unit

Nessun Inventor.

## L2 — Protocol

MCP server + mocked add-in transport.

## L3 — LiveProbe

COM reale contro fixture posseduta dal test.

## L4 — End-to-end MCP

```text
MCP client
   ↓
MCP server
   ↓
Named Pipe
   ↓
installed add-in
   ↓
Inventor 2027
```

Ogni test deve:

- creare solo documenti test-owned;
- non modificare documenti utente;
- non salvare fuori dal workspace test;
- ripristinare il numero iniziale di documenti aperti;
- verificare preview;
- verificare rollback;
- verificare stale revision;
- verificare health.

---

# 28. Test XR aggiuntivi

Per la mesh:

- vertex count stabile;
- bounding box equivalente;
- face mapping valido;
- units corrette;
- assembly transforms corrette;
- mesh hash cambia solo al cambio geometria.

Per interaction:

- triangle → face ID;
- occurrence selection;
- stale entity handling;
- deleted face handling;
- suppressed occurrence handling.

Per networking:

- auth failure;
- token expiry;
- rate limit;
- max upload/download size;
- reconnect;
- stale session;
- server restart.

---

# 29. Roadmap consigliata

## Milestone A — Quest foundation

- [ ] HTTP MCP host
- [ ] authentication
- [ ] asset server
- [ ] display mesh
- [ ] scene graph
- [ ] visual revision
- [ ] entity mapping
- [ ] highlight
- [ ] visibility
- [ ] camera
- [ ] event sync

**Risultato:** il Quest può visualizzare e selezionare il CAD reale.

---

## Milestone B — Full part authoring

- [ ] sketch inspection
- [ ] sketch DOF
- [ ] complete constraints
- [ ] complete dimensions
- [ ] sweep
- [ ] loft
- [ ] shell
- [ ] rib
- [ ] draft
- [ ] split
- [ ] thicken
- [ ] thread
- [ ] multibody

**Risultato:** progettazione parti quasi completa tramite MCP.

---

## Milestone C — Assembly authoring

- [ ] component replace
- [ ] pattern
- [ ] suppression
- [ ] DOF analysis
- [ ] constraint health
- [ ] model states
- [ ] representations
- [ ] motion sampling
- [ ] collision-aware motion

**Risultato:** progettazione assiemi completa.

---

## Milestone D — Drawings and manufacturing

- [ ] sheets
- [ ] drawing views
- [ ] dimensions
- [ ] annotations
- [ ] GD&T
- [ ] parts list
- [ ] balloons
- [ ] revision table
- [ ] drawing validation
- [ ] release package

**Risultato:** workflow CAD → documentazione → produzione.

---

## Milestone E — Intelligence layer

- [ ] semantic state
- [ ] dependency graph
- [ ] change plan
- [ ] validation engine
- [ ] engineering rules
- [ ] Content Center
- [ ] fastener intelligence
- [ ] Apprentice indexing
- [ ] semantic search
- [ ] similarity search

**Risultato:** Inventor SO diventa un sistema CAD agentico completo.

---

# 30. Priorità immediata proposta

Implementare nell'ordine:

```text
1. inventor_get_capabilities
2. HTTP MCP host
3. asset server
4. inventor_get_display_mesh
5. inventor_get_scene_graph
6. entity ↔ triangle mapping
7. inventor_highlight_entity
8. inventor_set_visibility
9. event sync
10. Quest Unity client MVP
```

Dopo questo punto sarà possibile realizzare un primo client Quest con:

```text
Open Inventor document
        ↓
Display in Quest
        ↓
Select component / face
        ↓
Read parameters
        ↓
Preview parameter change
        ↓
Commit
        ↓
Receive new visual revision
        ↓
Reload only modified geometry
```

---

# 31. Definition of Done — MVP Quest

Il primo MVP è considerato completo quando:

- il Quest si connette al PC tramite HTTPS;
- il client identifica una sessione Inventor;
- legge il documento attivo;
- riceve lo scene graph;
- scarica la mesh;
- visualizza l'assieme;
- seleziona un occurrence;
- seleziona una face;
- recupera il persistent entity ID;
- legge i parametri;
- modifica un parametro in preview;
- visualizza il risultato;
- effettua commit;
- riceve la nuova revision;
- aggiorna solamente la geometria modificata;
- gestisce correttamente stale revision e rollback.

---

# 32. Visione finale

```text
                 INVENTOR SO
                     │
    ┌────────────────┼────────────────┐
    │                │                │
   AI              DESKTOP           XR
    │                │                │
    └────────────────┼────────────────┘
                     │
               Semantic MCP
                     │
       ┌─────────────┼─────────────┐
       │             │             │
      PART        ASSEMBLY      DRAWING
       │             │             │
       └─────────────┼─────────────┘
                     │
                VALIDATION
                     │
                TRANSACTIONS
                     │
                INVENTOR API
```

Il risultato finale non deve essere un wrapper generico della COM API, ma una **piattaforma CAD semantica, transazionale, verificabile e multi-client**.

Il Meta Quest 3 rappresenta uno dei client principali, ma la stessa architettura deve poter supportare:

- agenti AI;
- applicazioni desktop;
- servizi batch;
- browser;
- integrazioni industriali;
- digital twin;
- teleassistenza;
- revisione collaborativa.

---

## 33. Regola progettuale finale

Ogni nuova feature deve rispondere a queste domande prima di entrare in production:

1. Qual è il suo contratto semantico?
2. Quali documenti può modificare?
3. Come verifica la revision?
4. Quali entity ID accetta?
5. È previewable?
6. Come avviene il rollback?
7. Quali validator esegue?
8. Quali effetti esterni produce?
9. Come viene testata live?
10. Come viene rappresentata a un client remoto/XR?

Se una capability non soddisfa questi requisiti, non deve essere aggiunta direttamente alla `SoToolPolicy`.
