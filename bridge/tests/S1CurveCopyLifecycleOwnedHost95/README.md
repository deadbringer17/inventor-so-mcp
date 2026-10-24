# Host95 — lifecycle delle copie curve, draft da collaudare

Nuovo clone94 con guardie73/HTTPS e protocollo95, candidato93 obbligatorio
SHA256 `31680B0110529BBA8E20B5ECF9023E286D2E3385943509C459634C8C9FA7A8D8`.
Nessuna modifica a90/93/94 o produzione. L'agente non esegue build/COM.

Il coordinatore compila in serie questo progetto e il client95, conserva
fonti/assembly e avvia il nuovo `artifacts/s1-verification/run-copy-core95.ps1`.
CLI host/base-bootstrap identica73. Client:
`S1CopyCoreHttpProbe95.dll runid bootstrap.json copy-client95.json curve-copy-sources94.json`.

Baseline94 invariata:18workflow/28coppie/10rifiuti/74CP. Il client pubblica
copy-client94.json PASS e prerequisite94-ready.json chiuso{run_id}; solo
allora wrapper invia verify-copy94, esige PASS28/74 e pubblica atomicamente
prerequisite94-verified.json chiuso{run_id}. Nessuna95prima del prerequisito.

Comando stdin `copy-core95`. Request file copy-core-request95.json:
schema/run_id/nonce/ordinal/action/context/pass, sette campi esatti.
Sequenza14 identica90: prepare(all), edit/inspect Part1, edit/inspect Part2,
reopen/inspect_reopen Part, edit/inspect Sheet1, edit/inspect Sheet2,
reopen/inspect_reopen Sheet, complete(all).
ACK univoco copy-core-ack95-{nonce}.json con13campi90; documenti11campi:
context/document_id/internal_name/native_path/display_name/subtype/sketch_id/
parameter_key/native_database_revision_id/source_ids/source_curves.

source_ids8 corrisponde al catalogo94. source_curves5 rimane congelato da
prepare, anche dopo End/Open. Ogni curva ha11campi:
entity_id/entity_key/point_ids/point_keys/point_mm/constraint_keys/
constraint_anchors/initial_center_mm/radius_mm/angles/domain.
constraint_anchors deriva da membri ordinati nativi92 e regola reader:
first circle→Center, arc→Start, SketchPoint→Point. Ogni nodo contiene
constraint_key/native_type/members/anchor_mm; niente owner per prossimità.

Edit1 trasla cinque curve di(5,7)mm e porta Radius del cerchio a1.2cm.
Edit2 trasla di(-11,-3)mm e porta Radius a0.6cm. Gli archi conservano raggio9,
extent/domain entro1e-10rad. Solo MoveSketchObjects(false,false), Radius,
Solve e Update2 una volta prima di End. Nessun Ground/MoveTo/Rebuild/repair.
Dal r2 ogni move include anche i punti nativi della curva: centro del cerchio,
centro/start/end dell'arco, secondo NativeSketchTransforms. Il r1 è fallito
per deformazione degli estremi con la selezione della sola curva ed è conservato.
ACK post-End/Open legge esclusivamente metadati documento: nessun getter
geometria/glifi. L'oracle parte solo con inspect, dopo ONE firstCore client.

Report copy-native-lifecycle95.json PASS_NATIVE_CURVE_COPY_LIFECYCLE_SUBSET:
4update,2reopen,14azioni, raw separati12 copy-native-state95-NNN.json con
digest compatto native_state_sha256 e descriptor file/file_sha256/
native_state_sha256. Ogni trace protocollo collega anche il SHA del suo ACK.
Tutte28copie, punti, relazioni, glifi, linea/testi, stili e parametri sono
immutati. Letture95 ripetute/rollback usano uguaglianza serializzata esatta;
solo coordinate modificate hanno tolleranze esplicite1e-5mm, radius1e-8mm,
domain/angles1e-10rad.

SaveCopy usa destinazione nuova posseduta e adozione dopo verifica esatta
InternalName/path/subtype/inventario; nessuna lettura semantica dopo Open
prima della prima Core. Relocation ammessa solo dalla grammatica89 per
Bound13mm NONcopiato e owner locale assoluto esatto. Il wrapper archivia4IPT
con SHA,74CP,12raw95,14ACK, report/catalogo/fonti/binari; nessun token/bootstrap.
Cleanup normale posseduto, ripristino byte manifest e verifica DLL originale.

Client28min, bootstrap30min, wrapper30.5min, limite HTTP ordinario invariato.
È una consegna da rivedere/compilare/collaudare dal coordinatore. Non prova
arc resize/extent, profili Copy completi, UI/Quest/fisico o chiusura18gateS1.
