# Ricerca abortita copia curve92 — non verifica prodotto

Clone sorgenti91 con guardie ownership/start/HWND/manifest/DLL/cleanup73,
senza HTTP o bootstrap. Nuovo comando chiuso `curve-copy92`, una sola
esecuzione. Nessuna build, COM o prova nativa eseguita dall'agente.
Wrapper nuovo `artifacts/s1-verification/run-curve-copy92.ps1`, DLL staged
esplicita, binari già compilati, reader metadata ShareReadWrite|Delete,
output increment92/native-rN nuovo, ripristino manifest/DLL e nessun kill/ROT.

## Matrice approvata dal coordinatore

Parte/Lamiera × circle, arc0/90, arc170/90, arc30/270, arc30/-90:
10casi. Seed nativi in5sketch dedicati per documento, prima del salvataggio
baseline e prima delle transazioni di ricerca. Cerchio/arco sorgente:
centro(17,-11)mm, raggio9mm. Negativo costruito con
AddByCenterStartEndPoint(...,counterclockwise=false): si registrano
start/end/StartAngle/SweepAngle nativi reali, senza inventare un getter
signed sweep. Companion dell'altro tipo e linea libera obliqua hanno punti
separati. Testo88 resta sentinella nello stesso documento.

Ogni caso: una transazione owned non annidata, prima Copy di sola curva con
delta(12,-8), poi Copy della raccolta primary+companion+freeLine con
delta(34,17), quindi Abort. Sono20Copy observations e10Abort attesi. La
prima copia diventa unrelated per la seconda fase e deve restare esatta.
Solo curve whole nella ObjectCollection; mai TextBox o punti aggiunti alla
selezione. Nessun costruttore alternativo sostituisce la copia nativa.

## Oracle e condizioni

Readback typed verificato staticamente sulla interop reale31.0.19200.0 in
`Z:/Installati/Inventor 2027/Bin/Autodesk.Inventor.Interop.dll`.
Cerchi: Radius e CenterSketchPoint. Archi: Radius, StartAngle/SweepAngle,
Arc2d start/sweep e Center/Start/End nativi. Linea: estremi. Curve2dEvaluator
GetStrokes e101campioni completano il confronto; raggio e angoli non sono
inferiti dalla tessellazione. Unione esplicita di SketchEntities e
SketchPoints per key, con inventari originali distinti registrati.

Nuove curve abbinate alle sorgenti per tipo univoco della raccolta chiusa e
inventario differenziale di key; nessuna proprietà o owner attribuiti da
prossimità. Key parent sketch verificato, center/endpoints tutti disgiunti
da ogni key precedente. Tutte le nuove entità devono essere curve copiate
o punti raggiunti dai ruoli nativi di quelle curve. Enumerator confrontato
con inventario nuovo, senza presumere che conti soltanto curve.

Traslazione di punti/strokes/evaluator entro1e-5mm, raggio1e-8mm e angoli
1e-10rad; parametrizzazione nativa e ordine dei campioni conservati. Locus
evaluator verificato analiticamente sulla retta o sul cerchio nativo.
Source/copy start/end e angoli impediscono di nascondere inversioni d'arco.
Solver sketch/curve non Over/Unknown.

Grafo letto da GeometricConstraints reali: key/type/Deletable e ruoli typed
Coincident, Concentric, Tangent, Ground. Conteggi e membri intrinseci non
hardcoded: si proietta il sottografo sorgente sui ruoli/key nuovi e si
richiede isomorfismo esatto, inclusi ruoli ordinati e flag. Qualsiasi
relazione esterna o tipo non adattato è UNKNOWN, non falso supporto.
Vincoli nuovi non possono toccare source/unrelated/prior-copy keys.

Originali, copie precedenti, vincoli e geometria unrelated vengono confrontati
esattamente per key. Full native ereditato include text glyphs/styles,
parametri, dimensioni, file hash, Dirty e DatabaseRevisionId; nel confronto
durante la mutazione si escludono soltanto il CaseSketch modificato e Dirty/
DBRevision del suo documento. Dopo Abort nessuna esclusione: full native
prima/dopo deve essere JSON esatto. Un Abort non verificabile ferma tutto.

Flags catturati: Construction, Reference, HasReferenceComponent,
ConstraintStatus, OwnedBy, Centerline dove esiste, line type/weight/scale/
definition space, OverrideColor, layer name/internalName, block-path count,
AttributeSets.Count. Getter opzionali non disponibili restano UNKNOWN con
stage/type/HResult; non valgono come copertura. Attributi non vuoti non
sono dichiarati preservati: i valori sono fuori da questa prima fixture.
Le asserzioni sui known flags non equivalgono a prova di proprietà non lette.

## Evidenza e limiti

`curve-copy92.json` conserva casi effettivamente tentati, prima/dopo di
ciascuna fase, mapping, grafo, flags e full-state Abort. Baseline non leggibile
viene marcata senza fingere una transazione/rollback. UNKNOWN, APIERROR o
assert fallito arrestano la matrice, senza retry o saltare al caso successivo.
Journal JSONL write-through/Flush(true) prima/dopo Copy, enumerazione,
Solve, Update, readback e Abort, con soli stage/type/HResult sanitizzati.

Terminale positivo potenziale `OBSERVATIONS_COMPLETE_SCOPED` significa
soltanto osservazioni di ricerca abortita completate; product_pass=false.
Deadline operazioni6min, lifetime worker15min, wrapper8min piùcleanup;
Abort e mandatory rollback read non vengono impediti dalla scadenza della
fase, pur mantenendo guardie process/doc. Snapshot limitate a2000entità e
4000vincoli/sketch, strokes8192vertici/curva,101samples e returned1024.

Non provati: edit della copia/sorgente, persistenza/reopen, Core/HTTPS,
preview/Apply prodotto, semantica desktop, Quest, piani nonXY/3D, geometria
vincolata/proiettata/blocchi, ellissi/spline/immagini/testo/mixed completi,
remove-constraints=true o valori/default differenti. Sono fasi successive,
non requisiti cancellati da S1. I template e gli altri processi rimangono
protetti dalle guardie73 e dai controlli finali del wrapper.

Le snapshot full native vengono conservate una sola volta in file pubblici curve-state92-NNN.json, con run_id e native_state. Il report usa descriptor file/file_sha256 per before/after/Abort; il confronto runtime continua a usare tutti i dati raw. Questo evita di duplicare decine di volte i glifi nel report e mantiene il limite64MiB per ciascun file. Il wrapper archivia tutte le snapshot anche sui fallimenti. Nessun dato nativo viene omesso dal confronto.

