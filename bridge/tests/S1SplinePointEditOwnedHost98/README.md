# S1 — modifica dei punti spline, sonda PC98

Sorgenti da revisionare e compilare dal coordinatore. Nessuna esecuzione è
compresa nella consegna del codice. Il candidato di produzione è fissato a
`D70976D527A541CE0940B0DF13CD318C9775C9D55F548961758D1BFAEC1B2877`;
nessuna sostituzione automatica con una build successiva.

La sonda usa il vero plugin Named Pipe nel nuovo Inventor posseduto e dedicato.
Non espone HTTP, bootstrap o credenziali; non usa ROT, finestre visibili, kill o
documenti dei processi preesistenti. Conserva le guardie di PID/start, documenti,
percorsi, moduli caricati e installazione della sonda96. Le API native restano
sulla STA; il thread che legge stdin non accede a COM.

## Matrice e confronto

128 casi: Parte/Lamiera × tre metodi fit oppure CV × aperta/chiusa × 2/5/64
punti iniziali. Due ruoli Start/End per due punti, tre Start/Interior/End per
5/64. Ogni caso prepara uno schizzo nuovo con una spline **nativa già esistente**;
sceglie un membro della raccolta FitPoint/ControlPoint effettiva, conserva la
chiave reale e prepara `spline_point_update` con due riferimenti tipizzati.
L'ordinale nella relazione è un'osservazione del fixture: non viene inviato al
prodotto come identificatore né usato per dedurre ownership dalle coordinate.

La destinazione è il punto nativo iniziale più `(3,-2)` mm. Preview richiede
rollback completo e token invariato. Dopo commit si legge lo stato documento e
**una sola** `get_sketch_editor_xr`, pubblicandola prima degli assert e prima di
qualsiasi getter geometrico/oracle nativo. Si controllano identità della spline
e di tutti i membri, punti/strokes/chiusura/metodo/conteggio nel primo snapshot.
Il reader attuale non pubblica `Reference`: l'assenza non viene convertita in
false. Il report registra `first_snapshot_reference_flag_exposed`; il valore
nativo effettivo rimane confrontato con l'oracle, separatamente dal readback.

Solo successivamente una transazione separata costruisce una spline oracle
nativa con stessa definizione iniziale e muove il medesimo ruolo con `MoveTo`.
Confronto geometrico a `1e-5 mm`, dominio a `1e-10`, proprietà native esatte;
gli input fit possono essere approssimati dall'API e non vengono presunti uguali
ai fit point risolti. La posizione effettiva di prodotto e oracle deve
corrispondere entro `1e-5 mm`; gli scarti di entrambi dalla richiesta vengono
registrati separatamente. Non si dichiara posizionamento esatto rispetto alla
richiesta e non si aumenta la tolleranza del confronto nativo. La chiusura risultante viene
confrontata con l'API; nessun setter la ripristina dopo il movimento.
Le righe espongono `initial_closed`, `actual_after_closed`, `oracle_after_closed`
e `closure_preserved`: equivalenza API non equivale a parità completa
dell'editor nativo per le spline chiuse. Endpoint
chiusi possono essere membri distinti o alias reali; devono conservare chiavi
e ordine, senza inventare identità.

Abort dell'oracle deve ripristinare esattamente stato completo e tutte le
definizioni spline osservate. `spline_definitions98` cattura per ogni spline
fit/CV chiavi, punti ordinati, proprietà, dominio, 101 campioni e strokes;
non è una dichiarazione di confronto completo dei coefficienti NURBS.
Cleanup cancella soltanto lo schizzo creato, verificandone identità COM/chiave.
Solo Dirty/DatabaseRevisionId del documento interessato sono esclusi nel
confronto dopo la scrittura legittima di preparazione/cleanup; preview e rifiuti
rimangono esatti anche per quei campi.

14 rifiuti: per ogni contesto punto di altra spline nello stesso schizzo,
riferimento stale, punto reale di altro documento, tipo punto errato,
destinazione malformata, ordine riferimenti invertito e operazione valida
seguita da arco invalido allo step1. Sono commit tentati, rifiutati atomicamente.
Si archiviano codice strutturato, step, token prima/dopo e stato nativo esatto.
Il riferimento straniero resta quello autentico, senza riscrivere revisione o
identità. Nessun riferimento fallito viene fabbricato da coordinate.

## Protocollo e artefatti

stdin: `spline-point-edit98`, `inspect`, `stop`. Suite consumabile una volta.
Marker fisso `SPLINE_POINT_EDIT98_VERIFIED` solo dopo tutta la matrice.
Report `spline-point-edit98.json`, stato finale
`PASS_PLUGIN_NATIVE_SPLINE_POINT_EDIT_SUBSET` oppure `FAILED`.
540 raw `spline-native98-NNN.json`: quattro per caso e due per rifiuto.
Descrittore `{file,native_state_sha256}` con hash JSON compatto; raw
`{schema,run_id,target_id,native_state}`. I confronti di cleanup dei due fixture
dei rifiuti sono incorporati nel report con i due stati, senza raw aggiuntivi.
Risultati preview/commit e stati di revisione effettivi sono conservati per caso;
il primo snapshot è scritto anche prima di validarlo.

Budget suite 30 minuti, wrapper 35 minuti incluse attese e stop normale.
Il wrapper archivia sorgenti, binari host, candidato esatto, report/raw e prove
ready/stopped; ripristina i byte del manifest e controlla il DLL installato.
Non ripete operazioni CAD, non forza chiusure e non modifica il DLL installato.

Comandi riservati al coordinatore, in slot seriale dopo le altre prove native:

```powershell
dotnet build bridge/tests/S1SplinePointEditOwnedHost98 -c Debug
pwsh -NoProfile -File scripts/run-s1-spline-point98.ps1 -OutputDirectory artifacts/simulator/punti98-nuovo -AddinAssembly artifacts/s1-verification/increment98/producer-r1/Inventor.So.AddIn.dll
pwsh -NoProfile -File scripts/verify-s1-spline-point98.ps1 -EvidenceDirectory artifacts/simulator/punti98-nuovo/worker
```

Il wrapper versionato conserva il manifest originale byte per byte e controlla
anche `stopped.json`: processo QA assente, zero documenti posseduti prima di
Quit e processi preesistenti preservati. Il verificatore offline controlla
combinazioni univoche, sidecar/hash, rollback, prima snapshot e confronto
con l'oracle registrato. Non esegue Inventor e non ricostruisce proprietà native
non presenti nelle evidenze. Esiti del nuovo collaudo: [incremento 102](../../../docs/s1-spline-point-verification102.md).

## Limiti aperti

Questa matrice copre i punti di spline libere. Ground, quote/vincoli espliciti,
maniglie tangenti, curve dipendenti/consumate e persistenza Save/Open richiedono
prove successive; non sono sostituiti da questi casi. Nessun percorso UI/Core,
HTTPS, Quest o prova fisica è coperto. Nessun gate S1 viene chiuso e nessuna
parità completa spline o solver viene dichiarata.
