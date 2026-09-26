# inventor-so-mcp — Piano di implementazione evolutivo (rev. 2)

**Repository:** `deadbringer17/inventor-so-mcp`
**Target principale:** Autodesk Inventor 2027 x64
**Obiettivo:** evolvere `inventor-so-mcp` da bridge CAD sicuro a **API CAD semantica**, utilizzabile da client desktop, agenti AI e client XR (Meta Quest 3).

La versione originale è conservata in [`INVENTOR_SO_MCP_IMPLEMENTATION_PLAN.original.md`](INVENTOR_SO_MCP_IMPLEMENTATION_PLAN.original.md).
Questa revisione corregge incoerenze interne, parti non fattibili con l'API pubblica di Inventor e parti in conflitto con il codice e le regole di sicurezza già in produzione. Ogni correzione è elencata nella §0 con la motivazione; il resto del documento è già scritto nella forma corretta.

Legenda stato (colonna **Stato** nelle tabelle):

- **P** — production: esposto dalla `SoToolPolicy` di default. Solo codice server-side coperto da test L1/L2, oppure composto esclusivamente da handler già verificati live.
- **X** — experimental: implementato, ma dipende da handler add-in **non ancora verificati su Inventor reale**. Esposto solo con `--enable-experimental` (server) **e** compilato con `-p:SoExperimental=true` + `INVENTOR_SO_EXPERIMENTAL=1` (add-in). Vedi §26.3.
- **D** — differito: non implementato in questa iterazione, con motivazione.

---

## 0. Revisione: correzioni alla specifica originale

| # | Punto originale | Problema | Correzione |
|---|---|---|---|
| F1 | §33 vs. roadmap | La regola finale vieta di mettere in `SoToolPolicy` capability non testate live, ma la roadmap chiede di esporre subito decine di tool nuovi. Nessun ambiente di sviluppo senza Inventor può fare test L3/L4. | Introdotto il **tier experimental** (§26.3): il codice esiste, è compilato solo su richiesta e non è esposto finché non passa L3/L4. La promozione a P è un cambio di una riga nella policy. |
| F2 | §5 "senza modificare il kernel CAD" + `inventor_set_visibility` come tool | La visibilità di un'occurrence o di un corpo **è** una modifica del documento (sporca il file, entra nella design view, genera `OnDocumentChange` e quindi una nuova revision). Trattarla come stato di vista invaliderebbe in silenzio i piani degli altri client. | Tre classi distinte: **stato di vista** (highlight, camera, focus: nessuna revision, nessuna transazione), **stato documento** (visibilità, soppressione, attivazione rappresentazioni: comandi di `inventor_atomic_batch`, con revision e rollback), **CAD** (modellazione). La visibilità lato Quest si fa client-side sulla scena GLB; `set_visibility` in Inventor è un comando batch. |
| F3 | §5.1 `asset_url: "/assets/..."` | Un URL relativo esiste solo con l'host HTTP; in stdio non c'è nessun server di asset. | Ogni asset ha un `asset_id` content-addressed e **due** vie d'accesso: la risorsa MCP `inventor://assets/{asset_id}` (sempre) e `GET /assets/{asset_id}` (solo host HTTP). `asset_url` compare solo quando l'host HTTP è attivo. |
| F4 | §5.1 `revision: "218"`, `visual_revision: "vr_219"` | Le revision reali sono token `epoch:sequenza` del journal eventi, non interi; una visual revision "successiva" alla revision è priva di senso. | `revision` resta il token del journal. `visual_revision` è un token separato che avanza **solo** su `document_changed` (non su save/activate/selection). L'identità della geometria è l'hash SHA-256 della GLB (`asset_id`), quindi "hash cambia solo al cambio geometria" è garantito per costruzione. |
| F5 | §6 una mesh per documento | Un assieme con 200 istanze della stessa vite tassellerebbe 200 volte la stessa geometria e superebbe il limite di 5 MB della pipe. Impossibile "aggiornare solo la geometria modificata". | Mesh **per definizione** (documento parte) + scene graph con transform per occurrence (instancing). Il server compone anche una GLB unica dell'assieme per client semplici. Il refresh incrementale confronta gli `asset_id` per definizione. |
| F6 | §5.1 `units: "mm"` per la GLB | glTF 2.0 impone i **metri**. Una GLB in mm appare 1000 volte più grande in Unity/WebXR. | GLB in metri, destrorso, Y-up (asse Y di Inventor invariato). Le risposte JSON restano in mm; lo scene graph riporta entrambe le forme. Conversione righe→colonne della matrice Inventor documentata e testata. |
| F7 | §6.2 "idealmente ogni triangle group ... Face" | Requisito vago. | Tassellazione **per faccia** (`Face.CalculateFacets`); la GLB ha un range di indici per faccia in `extras.faces[] = {face_id, first_index, index_count}`. Triangolo → faccia è una ricerca binaria client-side senza round trip. `inventor_pick_entity` converte (occurrence, face) in un face proxy dell'assieme. |
| F8 | §5.1 `inventor_raycast_entity` | Senza mapping triangolo→faccia sarebbe l'unica via di selezione; con F7 diventa un fallback. | Mantenuto come fallback preciso (`FindUsingRay`) per client che non hanno la mesh. |
| F9 | §10 vs §17 | `inventor_get_parameter_dependencies`/`inventor_trace_parameter_dependency` duplicano `inventor_get_dependencies`/`inventor_trace_dependency`. | Unificati: `inventor_get_dependencies` (parametro o feature, un livello) e `inventor_trace_dependency` (chiusura transitiva limitata). |
| F10 | §10, §11, §13, §14 tool `*_safe` di scrittura dedicati | Contraddicono §8.1 ("le modifiche passano da `inventor_atomic_batch`") e §24 (superficie limitata). Ogni tool di scrittura dedicato duplica revision check, transazione e validazione. | Tutte le scritture nuove sono **comandi del catalogo** di `inventor_atomic_batch`. Il catalogo dichiara per ogni comando i tipi di documento ammessi (`part`/`assembly`/`drawing`) e la stabilità. Il batch accetta ora anche assiemi e tavole. I tool MCP nuovi sono query, stato di vista, pianificazione e rilascio. |
| F11 | §7.2 `trim`, `extend` | L'API pubblica di Inventor non espone trim/extend degli sketch. | Sostituiti da `move_sketch_point` (spostamento estremo) + `delete_sketch_entity` + `split` per curve. Trim/extend: **D**. |
| F12 | §8 `rib`, `emboss`, `copy_body`, `delete_body`, `body_pattern` | API poco documentate o con semantica diversa (delete body = `DeleteFace`, copy body = derive); scriverle senza verifica live è rischioso. | Implementati: sweep, loft, shell, draft, split, thicken, thread, mirror (feature e corpi), combine, move_body, rectangular_pattern (già esistente). **D**: rib, emboss, copy_body, delete_body, body_pattern. |
| F13 | §11.1 motion (`sample_joint_motion`, ...) | L'API per pilotare un joint non è uniforme tra i tipi; "traiettoria valida" richiede comunque di muovere il modello. | Un solo strumento generico `inventor_sample_parameter_motion`: campiona un **parametro** (offset/angolo di vincolo o joint, o parametro utente) dentro una transazione sempre annullata, e per ogni campione riporta rebuild, interferenze e clearance minima. Il modello non cambia mai (rollback obbligatorio, revision ripristinata). |
| F14 | §12 attivazione model state / rappresentazioni come tool | Attivare cambia lo stato persistente del documento. | Letture: `inventor_get_representations` (model state, design view, posizionali). Attivazioni e creazione model state: comandi batch. |
| F15 | §14 `set_part_number`, `set_bom_structure` sull'assieme | Scrivere il part number o la struttura BOM di un componente modifica **un altro documento**, fuori dalla transazione del documento attivo: il rollback non lo annullerebbe. | `set_bom_structure` usa l'override per occurrence (`ComponentOccurrence.BOMStructure`, salvato nell'assieme). Il part number si scrive solo sul **documento attivo** (`set_iproperty` batch). `inventor_compare_bom` e `inventor_validate_bom` sono server-side, sopra `inventor_get_assembly_bom` già verificato. |
| F16 | §16.1 `import_*_safe(path)` | §26.2 vieta i path arbitrari. | Un solo `import` (**D**) che accetterà solo nomi dentro il workspace host (`WorkspaceDocumentPolicy`, già esistente). Differito: l'import crea documenti nuovi fuori dalla transazione e richiede un ciclo di vita dedicato. |
| F17 | §16.2 export come nuovi tool | `inventor_save_artifact` copre già IPT/IDW/STEP/PDF/DXF con cartelle host-owned. | I formati mancanti entrano come valori di `format` di `inventor_save_artifact`; la GLB è prodotta dalla pipeline mesh (§6). SAT/IGES/DWG/DWF/STL-via-translator: **D** fino a verifica dei translator add-in su 2027. |
| F18 | §16.3 release package fisso | `flat.dxf` esiste solo per lamiera, `bom.csv` solo per assiemi, `drawing.pdf` solo se esiste una tavola aperta. | `inventor_build_release_package` compone gli artifact **applicabili** al documento, li elenca in `metadata.json` con i motivi degli esclusi, scrive `checksums.sha256` e `validation.json`. Nessun file viene sovrascritto (cartella nuova per pacchetto). |
| F19 | §18 `inventor_plan_change` da "intent" in linguaggio naturale | Il server non ha un modello linguistico: non può tradurre "aumenta il diametro a 25 mm" in operazioni. | L'agente produce le operazioni (vocabolario del batch); il server le valida, esegue una **preview reale** (transazione annullata), calcola l'impatto (feature dipendenti, validatori) e restituisce un `plan_id` legato a revision e hash delle operazioni. `inventor_commit_plan` esegue esattamente quel piano, rifiutandolo se revision o contenuto sono cambiati. L'approvazione utente avviene nel client tra i due passi. |
| F20 | §20 eventi `selection_changed`, `camera_changed` | Nel journal attuale **ogni** evento avanza la revision del documento: una selezione o un movimento di camera renderebbe stale tutti i piani. Inventor non ha un evento affidabile di cambio camera. | Gli eventi di vista sono registrati **senza** avanzare revision né visual revision. `selection_changed` da `UserInputEvents`. `camera_changed`: **D** (la camera si legge con `inventor_get_camera`; il client XR possiede la propria camera). `parameter_changed`, `assembly_changed`, `model_rebuilt` non sono distinguibili in modo affidabile dagli eventi applicativi: sono tutti `document_changed`. |
| F21 | §20 notifiche "quando supportato" | Non specificato. | Le risorse `inventor://events` e `inventor://active-document` supportano `resources/subscribe` (stdio e HTTP): il server interroga il journal dell'add-in con cursore e invia `notifications/resources/updated` ai soli sottoscrittori. |
| F22 | §21 host HTTP con tool condivisi | `PluginClient` tiene il target selezionato come stato di processo: con più client HTTP, `inventor_switch_target` di uno cambierebbe l'istanza Inventor di tutti. | In modalità HTTP il target è fissato da configurazione (o unico); `inventor_switch_target` non è registrato. |
| F23 | §21 HTTPS obbligatorio | Senza certificato l'host non partirebbe; con un certificato self-signed il browser del Quest mostra un avviso, Unity lo rifiuta. | HTTPS con certificato PFX fornito dall'utente. Senza certificato l'host accetta **solo** bind di loopback; un bind LAN in chiaro richiede l'opt-in esplicito `--http-allow-insecure-lan` (per reti di laboratorio) e viene segnalato in `inventor_get_capabilities`. |
| F24 | §23 `"client": "quest"` | `clientInfo.name` di MCP è autodichiarato e non autenticato. | L'identità nel log di audit è il **nome del token** HTTP (file token `nome:token`); in stdio è `stdio`. Il `clientInfo` dichiarato è registrato a parte come `declared_client`. |
| F25 | §22 "ownership della sessione" per gli asset | Unity/WebXR scaricano con un GET semplice, senza intestazione di sessione MCP. | Gli asset sono content-addressed e legati al **token** che li ha creati; il GET richiede lo stesso bearer token. TTL rinnovato a ogni riuso. |
| F26 | §25 `inventor_get_tool_schema` | `tools/list` di MCP fornisce già gli schemi JSON dei parametri. | `inventor_get_tool_schema` restituisce il **contratto semantico** (§33): tier, lettura/scrittura, revision richiesta, previewable, documenti ammessi, validatori; per `inventor_atomic_batch` anche il catalogo completo. |
| F27 | §25 capability statiche | Un client non deve assumere capability non presenti, ma l'elenco dell'esempio è fisso. | Capability calcolate: configurazione server (HTTP, experimental, read-only) + comandi effettivamente registrati nell'add-in collegato (`get_capabilities`). Se l'add-in non risponde, le capability CAD sono `false` con il motivo. |
| F28 | §19 `validate` per comando | I validatori girano sul documento nel suo insieme, non per singola operazione; eseguirli dopo ogni passo moltiplica i rebuild. | `validate` è un argomento **del batch** (una lista), eseguito prima del commit. `rebuild` e `feature_health` sono sempre attivi per le parti. Vocabolario chiuso, validato prima di aprire la transazione. |
| F29 | §30 punto 10 "Quest Unity client MVP" | Un progetto Unity non è compilabile né testabile in questo repository. | MVP Quest = **pagina WebXR** servita dall'host HTTP (`/viewer`), apribile dal browser del Quest senza sideload. Un client Unity resta possibile sugli stessi contratti (GLB + scene graph + MCP). |
| F30 | §15 Content Center / fasteners, §29-E Apprentice, ricerca semantica e per similarità | Richiedono librerie Content Center configurate, un database di regole ingegneristiche e un indicizzatore esterno (Apprentice è un server COM separato). Non verificabili qui. | **D**, con i contratti riservati in §15/§29. |
| F31 | §13.3–13.6 quote, GD&T, balloon su tavola | Richiedono un vocabolario per selezionare curve di vista (`DrawingCurve` → `GeometryIntent`) che non esiste ancora; senza, un agente non può indicare "questa quota". | Prima si introduce `inventor_list_drawing_curves` (**D**). Implementati ora: fogli, viste base/proiettate, spostamento/scala viste, note, parts list. Quote/GD&T/balloon: **D**. |
| F32 | §27 L3/L4 per "ogni capability" | Obiettivo corretto, ma la roadmap non dice chi li esegue. | L1/L2 girano in CI e in questo repository (anche su Linux). L3/L4 sono il **criterio di promozione** da X a P (§26.3) e si eseguono sulla postazione con Inventor 2027 (`scripts/smoke-*.py`, `Inventor.So.LiveProbe`). |

---

## 1. Obiettivo architetturale

Non si espone la COM API di Inventor tramite centinaia di tool. Si costruisce una superficie semantica stabile, sicura e adatta agli agenti:

```text
Client  (agenti AI · desktop · Meta Quest 3 / WebXR · batch)
   │  stdio MCP  |  Streamable HTTP MCP (+ /assets, /viewer)
   ▼
inventor-so-mcp server  (.NET 8, nessun riferimento a Inventor)
   ├── Query / Actions / Planning / XR / Release tools
   ├── Validation spec, change plans, audit, asset store, GLB builder
   └── PluginClient ── Named Pipe autenticata ──┐
                                                ▼
Inventor SO add-in  (.NET 10, dentro Inventor.exe)
   ├── STA dispatcher, CommandDispatcher (fail-closed)
   ├── atomic batch (part / assembly / drawing), validatori
   ├── entity resolver (ReferenceKeyManager), event journal
   └── adapter COM
                                                ▼
                                Autodesk Inventor 2027
```

Principi obbligatori (invariati): ID persistenti, revision check, selezione target fail-closed, preview prima del commit, transazione + rollback, validazione automatica, nessuna scrittura diretta non protetta, audit, separazione controllo CAD / trasferimento asset, compatibilità client XR e rete.

## 2. Stato da preservare

La superficie production preesistente (40 tool in `SoToolPolicy`) resta invariata. Gli handler legacy (sketch, extrude, ...) restano bloccati al confine dell'add-in con `ATOMIC_REQUIRED`: si raggiungono solo come comandi del batch.

## 3. Tre classi di operazione (F2)

| Classe | Esempi | Revision | Transazione | Read-only mode |
|---|---|---|---|---|
| Query | mesh, scene graph, sketch info, dipendenze | letta | no | ammessa |
| Stato di vista | highlight, focus, camera | non cambia | no | ammessa |
| Stato documento / CAD | visibilità, soppressione, feature, parametri, tavole | `expected_revision` obbligatoria | sì, con rollback e validatori | vietata |

## 4. Moduli

```text
bridge/src/
├── server/                 MCP server stdio (tool, risorse, policy)
│   ├── Tools/              ... + CapabilityTools, XrTools, PlanningTools, ReleaseTools, InsightTools
│   ├── Assets/             AssetStore, GlbBuilder, SceneComposer
│   ├── Planning/           ChangePlanStore
│   ├── Audit/              AuditLog + filtro tools/call
│   ├── Events/             EventSubscriptionService (resources/subscribe)
│   └── InventorMcpComposition.cs   composizione condivisa stdio/HTTP
├── server-http/            Inventor.So.Mcp.Http (Streamable HTTP, auth, TLS, CORS, rate limit, /assets, /viewer)
├── shared/Contracts/       catalogo batch, ValidationSpec, ToolContracts, MeshPayload, CadEventJournal
├── shared/Handlers/Experimental/   handler add-in tier X (compilati solo con SoExperimental)
└── tests/
```

## 5. Fase 1 — XR / Visualization

| Tool | Classe | Stato | Note |
|---|---|---|---|
| `inventor_get_display_mesh` | query | X | Per definizione; restituisce `asset_id`, `sha256`, `visual_revision`, conteggi, `units: "m"` nella GLB. Parametri: `document_id`, `tolerance_mm` (0.01–5, default 0.1), `max_triangles` (default 500 000). |
| `inventor_get_scene_graph` | query | X | Albero occurrence con `occurrence_id` persistente, `definition_document_id`, transform (mm riga-major e glTF colonna-major in metri), visibile, soppressa, bbox mm. Opzione `include_meshes` → GLB assieme composta. |
| `inventor_get_visual_revision` | query | X | Revision, visual revision e asset noti per documento. |
| `inventor_highlight_entity` | vista | X | `mode`: `highlight` \| `select` \| `clear`; colore RGB opzionale. |
| `inventor_focus_entity` | vista | X | Inquadra entità o occurrence. |
| `inventor_get_camera` / `inventor_set_camera` | vista | X | eye/target/up in mm, prospettiva, FOV in gradi. |
| `inventor_raycast_entity` | query | X | Raggio in mm nello spazio modello → primo `ent_*` colpito. |
| `inventor_pick_entity` | query | X | (occurrence_id, face_id di parte) → face proxy `ent_*` dell'assieme. |
| `set_visibility` | batch | X | Occurrence (assieme) o corpo (parte). |

## 6. Mesh pipeline (F3–F7)

```text
Face.CalculateFacets (per faccia, tolleranza in cm)
   → payload compatto: base64 float32 posizioni/normali (cm) + uint32 indici + tabella facce
   → pipe (limite di risposta rispettato; oltre max_triangles errore MESH_TOO_LARGE con suggerimento)
   → server: GlbBuilder (cm → m, indici 0-based, accessor min/max, extras.faces)
   → AssetStore (id = "a_" + sha256, TTL, limite dimensione, MIME allowlist)
   → inventor://assets/{id}  |  GET /assets/{id}
```

Ogni GLB porta in `asset.extras`: `document_id`, `visual_revision`, `units_source: "cm"`, e per ogni primitive `extras.faces`. Gli indici 1-based restituiti da Inventor sono normalizzati in modo difensivo (rilevando il minimo).

## 7. Fase 2 — Sketch

**Inspection** (un solo tool, F9/§24): `inventor_get_sketch_info` (X) con entità (tipo, coordinate mm, costruzione), vincoli geometrici, quote (nome, valore, driven), `fully_constrained`, DOF quando esposto, profili chiusi.

**Editing** (batch, X): `draw_ellipse`, `draw_spline`, `draw_slot`, `draw_polygon`, `offset_sketch_entities`, `mirror_sketch_entities`, `move_sketch_point`, `delete_sketch_entity`. Già P: `draw_line`, `draw_arc`, `draw_circle`, `draw_rectangle`, `draw_point`, `project_geometry`.

**Vincoli**: `add_sketch_constraint` (P per coincident, parallel, perpendicular, tangent, equal, horizontal, vertical, concentric, collinear, symmetric) esteso in X con `midpoint`, `fix`, `equal_radius`.

**Quote** (batch, X): `add_dimension` (`distance`, `horizontal_distance`, `vertical_distance`, `angle`, `radius`, `diameter`, `arc_length`; `name`, `driven`), `edit_dimension` (valore/espressione, nome, driven), `delete_dimension`.

## 8. Fase 3 — Feature solide (F12)

Batch, X: `sweep`, `loft`, `shell`, `draft`, `split`, `thicken`, `thread`, `mirror` (feature o corpi), `combine`, `move_body`. Già P: `extrude`, `revolve`, `fillet`, `chamfer`, `hole`, `circular_pattern`, `rectangular_pattern`. D: `rib`, `emboss`, `copy_body`, `delete_body`, `body_pattern`.

Regola: nessun write tool indipendente; solo catalogo dichiarativo.

## 9. Fase 4 — Work geometry

Batch: `create_work_plane`, `create_work_axis` (P); `create_work_point`, `create_ucs`, `rename_work_geometry`, `delete_work_geometry` (X). Le work feature restituiscono il nome; gli ID persistenti per work feature sono **D** (il `ReferenceKeyManager` le supporta ma l'adapter `EntityReferences` va esteso con verifica live).

## 10. Fase 5 — Parametri e dipendenze (F9, F10)

Batch: `set_parameter`, `create_parameter` (P); `rename_parameter`, `delete_parameter` (X, solo parametri utente senza dipendenti).
Query: `inventor_get_dependencies`, `inventor_trace_dependency` (X): tipo (model/user/reference), espressione, unità, `driven_by`, `dependents`.

## 11. Fase 6 — Assembly

Query (X): `inventor_get_assembly_health` — DOF per occurrence, salute dei vincoli e dei joint, occurrence non vincolate.
Batch (X): `suppress_component`, `set_visibility`, `replace_component` (solo file nel workspace host), `pattern_component` (rettangolare).
Motion (F13, X): `inventor_sample_parameter_motion(parameter, from, to, steps, checks)` con `checks` ⊂ {`rebuild`, `interference`, `min_clearance:<n>mm`}; risultato per campione + `first_failure` + `valid_range`. Sempre annullato.

## 12. Fase 7 — Model State e rappresentazioni (F14)

Query (X): `inventor_get_representations`. Batch (X): `activate_model_state`, `create_model_state`, `activate_design_view`, `activate_positional_representation`.

## 13. Fase 8 — Drawing (F31)

Base P esistente: `inventor_create_drawing_safe`, `inventor_list_drawing_templates`.
Batch su tavola attiva (X): `add_sheet`, `activate_sheet`, `delete_sheet`, `add_base_view`, `add_projected_view`, `move_drawing_view`, `set_view_scale`, `add_note`, `add_parts_list`.
Query (X): `inventor_validate_drawing` — riferimenti mancanti, viste senza modello, viste fuori foglio, sovrapposizioni tra viste, parts list mancante per assiemi, fogli vuoti.
D: sezioni, dettagli, quote, GD&T, balloon, revision table (servono `inventor_list_drawing_curves` e `GeometryIntent`).

## 14. Fase 9 — BOM (F15)

P (server-side su dati verificati): `inventor_validate_bom` (part number duplicati o vuoti, quantità, descrizioni mancanti), `inventor_compare_bom` (baseline JSON vs BOM attuale: aggiunti, rimossi, quantità cambiate).
Batch (X): `set_bom_structure` (override per occurrence: normal, purchased, phantom, reference, inseparable), `set_iproperty` (solo documento attivo).
D: numerazione item BOM (richiede `BOMView` strutturata verificata).

## 15. Fase 10 — Content Center e fasteners — D (F30)

Contratti riservati: `inventor_search_content_center`, `inventor_insert_content_center_part`, `inventor_find_fastener_for_hole`, `inventor_place_fastener_stack`, `inventor_validate_fastener_stack`. Prerequisiti: libreria Content Center configurata nel progetto, tabella regole di serraggio/imbocco, verifica live.

## 16. Fase 11 — Import / Export / Release (F16–F18)

Import: D. Export: `inventor_save_artifact` (P) + GLB via pipeline mesh (X).
`inventor_build_release_package` (X): cartella nuova host-owned con gli artifact applicabili (`model.step`, `drawing.pdf`, `flat.dxf`, `bom.csv`, `model.glb`), `metadata.json` (inclusi/esclusi con motivo), `validation.json`, `checksums.sha256`.

## 17. Fase 12 — Semantic CAD State

`inventor_get_semantic_state` (X): nodi `parameter`, `feature`, `sketch`, `body`, `work_feature`, `occurrence`, `constraint` con id, tipo, nome, documento, revision, metadati e relazioni (`drives`, `consumes`, `constrains`, `instance_of`). Relazioni faccia→vincolo: D (costo: una reference key per faccia di ogni vincolo).

## 18. Fase 13 — Change Plan (F19)

```text
inventor_plan_change(document_id, expected_revision, operations[], validate[], intent?)
   → validazione catalogo → preview reale annullata → impatto → plan_id (TTL 15 min)
USER APPROVAL (client)
inventor_commit_plan(plan_id, expected_revision)
   → stesso hash operazioni, stessa revision → commit atomico
```

Stato: P (usa solo `atomic_batch`, già verificato live). `inventor_list_plans` / scarto automatico a scadenza.

## 19. Fase 14 — Validation Engine (F28)

`ValidationSpec` in `shared/Contracts`: vocabolario chiuso `rebuild`, `feature_health`, `sketch_fully_constrained`, `constraint_health`, `interference`, `min_clearance:<n>mm`, `drawing_references`. Parsing e compatibilità con il tipo di documento verificati prima di aprire la transazione (errore `INVALID_ARGUMENT` con l'elenco ammesso). Esecuzione nell'add-in prima del commit; un fallimento produce `ROLLED_BACK` con `step_code = VALIDATION_FAILED` e il dettaglio del validatore.

## 20. Fase 15 — Eventi (F20, F21)

Eventi: `document_opened`, `document_closed`, `document_activated`, `document_changed`, `document_saved` (esistenti), `selection_changed` (X, senza avanzare revision). `inventor://events` e `inventor://active-document` sottoscrivibili. Sequenza Quest: subscribe → `notifications/resources/updated` → `inventor_get_visual_revision` → riscarica solo gli asset il cui `asset_id` è cambiato.

## 21. Fase 16 — Remote MCP (F22, F23)

`bridge/src/server-http` → `Inventor.So.Mcp.Http`: Streamable HTTP MCP su `/mcp`, bearer token (confronto a tempo costante, file `nome:token` o variabile d'ambiente), HTTPS da PFX, bind configurabile (loopback di default), CORS con allowlist esplicita, rate limiting per token, sessioni MCP del SDK, audit, `/healthz` senza dati. Il named pipe non è mai esposto.

## 22. Fase 17 — Asset server (F25)

`GET /assets/{asset_id}`: id validato con regex (`^a_[0-9a-f]{64}$`), nessun path, MIME allowlist (`model/gltf-binary`, `image/png`, `application/pdf`, `text/csv`, `application/json`), limite dimensione per asset e totale (evizione LRU), TTL, SHA-256 in `ETag` e verificato alla lettura da disco, proprietario = token.

## 23. Fase 18 — Audit (F24)

JSON Lines, un record per `tools/call` di scrittura (e per ogni rifiuto): `timestamp`, `session_id`, `client` (nome token o `stdio`), `declared_client`, `tool`, `document_id`, `revision_before`, `revision_after`, `preview`, `validation`, `result`, `error_code`, `duration_ms`. Mai token, mai argomenti completi (solo chiavi e hash SHA-256 degli argomenti).

## 24. Politica di esposizione

Tool MCP: ~70 dopo questa iterazione (P + X), sotto l'obiettivo 80–120. Catalogo batch: 36 comandi P + ~45 comandi X su part/assembly/drawing.

## 25. Discovery (F26, F27)

`inventor_get_capabilities` (P) e `inventor_get_tool_schema` (P).

```json
{
  "server": { "version": "…", "transport": "stdio|http", "read_only": false,
              "experimental_enabled": false, "full_access": false },
  "target": { "inventor_year": 2027, "target_id": "…", "reachable": true },
  "capabilities": { "atomic_batch": true, "drawings": true, "xr_mesh": false,
                    "scene_graph": false, "change_plans": true, "content_center": false, … },
  "batch_commands": { "available": […], "experimental_unavailable": […] }
}
```

## 26. Sicurezza

### 26.1 Sempre obbligatorio per le scritture
`document_id`, `expected_revision`, allowlist del catalogo, input limitati, ownership della transazione, rebuild, validatori, rollback.

### 26.2 Vietato
Path arbitrari, `execute_python`/`send_code` in production, invocazione COM grezza, editing iLogic, scritture filesystem dirette, fallback automatico d'istanza, commit senza revision check, esposizione di rete della pipe, caricamento DLL arbitrarie, **scritture su documenti diversi da quello della transazione** (F15).

### 26.3 Tier experimental (F1)
- Server: `--enable-experimental` / `INVENTOR_SO_EXPERIMENTAL=1` espone i tool X; senza, non sono nemmeno elencati.
- Add-in: gli handler X compilano solo con `dotnet build -p:SoExperimental=true` (simbolo `SO_EXPERIMENTAL`) e i comandi batch X sono ammessi solo se l'add-in gira con `INVENTOR_SO_EXPERIMENTAL=1`. Una build di default è identica alla production verificata.
- Promozione X → P: test L3 (LiveProbe) + L4 (smoke MCP) documentati in `docs/DEVELOPMENT.md`, poi spostamento del nome nella lista production della policy e del catalogo.

## 27. Test

L1 (unit, senza Inventor) e L2 (protocollo MCP + trasporto simulato) per tutto il codice server: asset store, GLB, trasformazioni, validation spec, catalogo, policy, piani, BOM, audit, host HTTP (auth, rate limit, CORS, traversal, TLS obbligatorio fuori loopback). L3/L4 come da F32.

## 28. Test XR aggiuntivi
Mesh: vertex count stabile, bbox equivalente, face mapping valido, unità (m), transform d'assieme, hash stabile a geometria invariata (L1 sul builder; L3 sull'estrazione). Interaction: triangolo→faccia, stale entity, faccia cancellata, occurrence soppressa (L3). Rete: auth, rate limit, dimensioni, restart (L2).

## 29. Roadmap

- **Milestone A — Quest foundation**: host HTTP ✔ P · auth ✔ P · asset server ✔ P · display mesh ✔ X · scene graph ✔ X · visual revision ✔ X · entity mapping ✔ X · highlight ✔ X · visibility ✔ X · camera ✔ X · event sync ✔ P (subscription) / X (selection) · viewer WebXR ✔ P.
- **Milestone B — Part authoring**: sketch inspection/DOF ✔ X · constraints/dimensions ✔ X · sweep/loft/shell/draft/split/thicken/thread ✔ X · multibody (mirror/combine/move) ✔ X · rib, emboss D.
- **Milestone C — Assembly**: replace/pattern/suppression ✔ X · DOF/constraint health ✔ X · model states/representations ✔ X · motion sampling ✔ X.
- **Milestone D — Drawings**: sheets/views/notes/parts list ✔ X · drawing validation ✔ X · release package ✔ X · quote/GD&T/balloon/revision table D.
- **Milestone E — Intelligence**: semantic state/dipendenze ✔ X · change plan ✔ P · validation engine ✔ P (parser) / X (validatori estesi) · Content Center, fasteners, Apprentice, ricerca semantica/similarità D.

## 30. Priorità successiva

1. Eseguire L3/L4 della Milestone A sulla postazione Inventor 2027 e promuovere a P.
2. `inventor_list_drawing_curves` → quote e balloon su tavola.
3. ID persistenti per work feature e sketch entity.
4. Content Center.

## 31. Definition of Done — MVP Quest

Invariata nell'elenco; il client è la pagina WebXR `/viewer` (F29). Raggiunta quando ogni punto è verificato L4 su Inventor 2027 reale.

## 32–33. Visione e regola finale

Invariate. La regola finale è applicata in modo meccanico dal tier experimental (§26.3): una capability che non risponde alle dieci domande **non** entra nella lista production della `SoToolPolicy`; `inventor_get_tool_schema` pubblica le risposte per ogni tool.
