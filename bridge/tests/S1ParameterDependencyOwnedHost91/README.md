# Ricerca read-only Parameter.Dependents91

Clone isolato dei sorgenti89, senza bin/obj. Progetto/assembly:
S1ParameterDependencyOwnedHost91. Nessun HTTP o bootstrap viene avviato o
esposto dal dispatcher. Comandi: parameter-dependencies91 (una volta),
inspect (solo lettura owned), stop. Nessuna copia/aggiornamento parametro,
mutazione documento o SaveCopy durante la ricerca.

Program conserva startup/guardie/cleanup73: nuovo PID/start e HWND nativi,
protezione di tutti i processi preesistenti e PID storici, documenti/temp
owned, DLL staged/caricata e manifest verificati, no ROT/kill. La normale
preparazione fixture è una mutazione separata dalla fase di ricerca;
conserva Parte/Lamiera88, quindi native TextBox literal e campo13mm con path
assoluto locale. Gli oggetti target vengono registrati direttamente dai
creator del clone, mai attribuiti per coordinate o uguaglianza del testo.

ParameterDependencies91.cs legge ogni parametro della raccolta Parameters
reale, incluse le famiglie realmente presenti nella fixture. Registra key,
native type, nome/unità/espressione, parent SameCom e Dependents tramite API
pubblica typed ObjectCollection.Count/Item. Ogni membro viene confrontato
SameCom con ciascuno dei quattro TextBox target (due documenti con parametri
omonimi e valore13mm intenzionalmente uguale). Adattatori typed registrano
Type/GetReferenceKey per TextBox, SketchEntity, Sketch, Parameter,
PartFeature, DimensionConstraint e GeometricConstraint. Document espone
solo tipo, nessuna chiave inventata o lettura del contenuto di documenti
esterni. RCW non riconosciuti restano UNKNOWN_PUBLIC_TYPE. Nessun raw
IDispatch, vtable, unsafe o invocazione di type-info; GetIUnknown esistente
serve esclusivamente al confronto d'identità SameCom autorizzato.

Firme verificate offline sulla interop effettiva installata:
Z:/Installati/Inventor 2027/Bin/Autodesk.Inventor.Interop.dll,
31.0.19200.0. Le chiavi dei dipendenti sono lette solo attraverso firme
GetReferenceKey(ref byte[],int) tipizzate, senza dynamic. Il reader full-state
ereditato conserva il suo schema89; non viene usato per risolvere gli oggetti
dependents.

Budget:1024parametri/documento,1024dipendenti/parametro,8192membri totali,
90s di fase letture e15min lifetime worker. Guardie prima di ciascuna lettura
bounded; nessun retry COM o setter di parametro per scoprire il legame.
Prima/dopo: CopyFullNative88 esatto, inclusi Dirty/DatabaseRevision, stili,
parametri, key e glifi. Raw before/after vengono conservati nel report;
impossibilità di verifica o differenza ferma la sonda con
FAILED_READONLY_VERIFICATION.

Esiti osservativi: OBSERVATIONS_COMPLETE non è product PASS. Edge nativo
esatto produce OBSERVED_DIRECT_TEXTBOX_EDGE; un elenco vuoto, membri non
corrispondenti, tipi non adattati, eccezioni o budget mancanti producono
UNKNOWN (con stage/tipo/HResult), non assenza di dipendenza. Un edge è
aggregato parametro→TextBox: non identifica automaticamente ogni nodo XML
di testi multicampo. La validazione del profilo absolute local path usa
OwnedParameterRelocation89 soltanto per attestare questa fixture, mai come
resolver generico o autorizzazione a copiare parametri.

Report: parameter-dependencies91.json. Wrapper separato:
artifacts/s1-verification/run-parameter-dependencies91.ps1, richiede DLL
staged esplicita e binari già compilati; conserva output osservativo e
ripristino manifest/DLL come85/89. UNKNOWN può essere un risultato di
ricerca completata, ma non abilita capacità. Alias/unsaved/esterni, ulteriori
famiglie non instantiate, precisioni/multicampo, copie, aggiornamenti,
persistenza, Core/HTTPS e Quest richiedono matrici future.

Implementazione non compilata/eseguita dall'agente; nessun COM/build/run.
