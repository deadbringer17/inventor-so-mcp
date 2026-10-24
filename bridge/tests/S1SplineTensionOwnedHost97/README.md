# Ricerca nativa Spline.Tension97

Sorgenti preparati, non compilati né eseguiti dall'agente. Il coordinatore
esegue build e run seriali dopo la conclusione delle altre prove native.

Il nuovo host clona guardie73/96, senza includere SplineClosure96, HTTP,
bootstrap o CopyTargets91. Conserva la fixture73/88 solo come baseline
owned nota e completa di geometrie, testi, stili e parametri invariati.
Il solo comando sperimentale stdin è `spline-tension97`; `inspect` e `stop`
sono i comandi di controllo. Non effettua richieste wire o di rete.

Candidato obbligatorio96:
`2AF7E77A8A34EA82052AD84445398CDFA6858A2E9BC2AF13A6B577B6A13D15E0`.
Il wrapper nuovo è `artifacts/s1-verification/run-spline-tension97.ps1`:
parametri `-Run r1 -AddinAssembly <percorso DLL frozen96>`.
Non caricare una DLL diversa per adattare la ricerca a un'altra capacità.

Matrice60: Parte/Lamiera × metodi26369/26370/26371 × aperta/chiusa ×
tensione0/25/50/75/100. I cinque input geometrici dichiarati sono costanti,
non fissati con Ground: metodi nativi possono approssimare i fit point.
Ogni caso crea una spline nuova in una transazione propria, legge dopo Add,
imposta Closed, legge prima del setter, imposta Tension, legge immediatamente,
poi chiama Solve e Update2 una volta ciascuno e legge il risultato.
Un rifiuto interrompe quel percorso: nessun retry o Solve riparativo.
Non modifica SketchOptions globali e non presume tensione iniziale50 o
conversione automatica a26370. Interop31 conferma Int32 per Tension.

Curve97 registra metodo/tensione/chiusura effettivi, identità e coordinate
ordinate dei fit point, dominio,101 campioni evaluator e strokes nativi.
Non asserisce che i fit point coincidano esattamente con gli input o che
un cambio metodo mantenga il locus. Il report conserva tutte le osservazioni,
inclusi tipo/HRESULT/stadio degli errori API; non serializza eccezioni raw.

Ogni caso ha un sidecar separato `spline-tension97-case-NNN.json`, pubblicato
già prima della mutazione. Dopo Abort contiene lo stato nativo catturato
prima/dopo, inclusi Dirty e DatabaseRevisionId, e i rispettivi SHA256 della
serializzazione compatta. Il report principale aggiunge SHA256 del file.
Il confronto è esatto senza rimozione di revisioni, riparazioni o Solve dopo
Abort. Lo schema baseline è quello73/88, esteso con i dati spline97: non è
una dichiarazione di enumerazione di ogni proprietà possibile di Inventor.
Qualunque errore di ownership, cattura strutturale o Abort esatto ferma la
matrice. Scadere il budget non impedisce il tentativo di Abort owned.

`OBSERVATION_ONLY` significa60 osservazioni raccolte e60 Abort esatti,
anche quando un'API rifiuta alcuni input. Non significa60 valori supportati,
comando prodotto PASS o parità desktop. I campi setter_returned,
tension_readback_matches_request, fit_method_changed_immediately,
update_returned ed eventuale failure permettono di distinguere gli esiti.

Budget suite15minuti, wrapper20minuti. Una chiamata COM bloccata non è
interrompibile con sicurezza: timeout viene conservato come fallimento,
senza kill/ROT né avvio concorrente successivo. Directory owned mantenute
per ispezione e recupero; documenti owned chiusi senza salvare, Quit e
preservazione PID/start preesistenti sono verificati dalle guardie clonate.
Staging/manifest/DLL vengono controllati e ripristinati. L'archivio usa
allowlist di sorgenti, binari runtime e report pubblici; niente token.

Non copre wire/prodotto, preview/commit, editor punti/maniglie, tutti i
parametri della spline, dipendenze, persistenza, storico, Quest o oracle
desktop. I18 gate S1 restano aperti.
