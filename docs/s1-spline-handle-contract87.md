# S1 — Identità e lettura delle maniglie spline, incremento87

16 ottobre2026. Lavoro PC/Inventor; Quest rimandato su richiesta dell'utente.
Tutti18 gate complessivi rimangono aperti.

Il reader sperimentale distingue ora `SketchSplineHandle` dalla sua
interfaccia base `SketchLine`. Il payload conserva tipo nativo e chiave
reale della maniglia. Il vincolo di tangenza riporta maniglia+arco, mentre
il collegamento alla spline proprietaria è rappresentato separatamente.

Il contratto `spline_handle` usa soltanto slot locali nella raccolta dello
snapshot: spline, FitPoint, estremi Start/End e ruolo del FitPoint. Non sono
riferimenti CAD portabili. La preview conserva questi legami senza esportare
identità persistenti temporanee. Reader e guardie verificano appartenenza
nativa e identità COM; non deducono la proprietà dalla vicinanza geometrica.

La maniglia e i suoi estremi sono protetti dalle mutazioni generiche. Il
FitPoint della spline è un'identità distinta e resta selezionabile. I due
validatori condivisi bridge/Core rifiutano tipi travestiti da linee,
ownership incompleta, slot errati, collisioni e stati di selezione incoerenti.

Validazione software:1.289 test Core PASS (`core-full-r2`),1.680 bridge PASS
(`bridge-full-r1`). Add-in sperimentale e client compilati senza errori o
warning; host di prova compilato con il warning NU1903 preesistente.

## Esiti nativi conservati

- `increment83/supplement-native-r10`: otto casi ellittici completati,
  primo snapshot spline completo e sottovincolato. Il client si ferma dopo
  la lettura; il run resta FAILED.
- r11: la diagnostica esclude la perdita di entità/vincoli preesistenti.
  Anche questo run resta FAILED.
- r12: revisioni commit/prima lettura identiche; il fallimento è isolato
  alla scelta dell'arco nuovo. L'attivazione della maniglia aggiunge un
  arco di costruzione oltre all'arco richiesto: la precedente asserzione
  `Single` su tutti gli archi nuovi non distingue i due ruoli.

Tutti e tre i worker terminano normalmente, con manifest ripristinato e
DLL ordinario invariato. Nessuna installazione Quest o promozione stabile.

Il client aggiornato identifica l'arco richiesto attraverso il vincolo di
tangenza reale, senza ignorare gli altri oggetti aggiunti. La sonda di
ricerca85 viene estesa con il grafo nativo `OwnedBy` e i membri dei vincoli
prima/dopo attivazione, tangenza e Solve: la classificazione e protezione
degli oggetti ausiliari richiede questa evidenza indipendente.

Restano da completare la matrice52, le guardie dell'intera struttura
ausiliaria, gli aggiornamenti delle dipendenze, persistenza, oracle desktop,
interfaccia e Quest. Il reader corretto non basta a certificare il comando.

La sonda identità `increment85/native-r2` ha superato il limite di osservazione
del wrapper. Il wrapper è FAILED, il manifest è ripristinato e il DLL
ordinario invariato; il worker dedicato resta attivo con Stop già accodato.
Non è cleanup riuscito. Identità dei processi e prossimo controllo sono in
`increment87/ongoing-native-state.json`; vietato sovrapporre un'altra prova
nativa finché questi processi non terminano. I primi grafi osservati
confermano che `OwnedBy` da solo non collega l'arco ausiliario alla spline:
quel legame non viene quindi inventato nel contratto di prodotto.

Aggiornamento successivo: il worker r2 termina tutte20 osservazioni con20
rollback esatti. I processi dedicati risultano terminati; attestazioni
pubbliche terminali archiviate separatamente in `worker-terminal-r1`, senza
riscrivere il wrapper FAILED. Il controllo finale dell'installazione nel
worker fallisce perché il wrapper aveva già ripristinato il manifest.

La successiva sonda r3 completa quattro casi di identità Parte/Lamiera
Start/End, tutti con Abort esatto e cleanup normale. Rimangono però
letture native non disponibili: `Constraints` sui punti e il membro
`Point` di un vincolo fit speciale. Non sono associazioni vuote provate.

Il tentativo r4 con un adattatore COM IDispatch aggiunto alla sola sonda
termina il worker con access violation prima del primo caso. L'adattatore
è stato rimosso. L'istanza Inventor invisibile orfana è stata terminata
usando l'autorizzazione esplicita dell'utente, dopo controllo di PID/start,
modulo staged e hash dei due file temporanei identici alla baseline.
`forced-recovery-r1.json` documenta il recupero: non è una prova di rollback
o di cleanup nativo. Manifest e DLL ordinario sono ripristinati.

Una [review dell'ownership ausiliaria](../artifacts/s1-verification/increment87/auxiliary-owner-review.md)
valuta una registrazione esplicita degli oggetti creati da SetHandleStatus.
Quella provenienza sarebbe gestita dall'applicazione, distinta da OwnedBy
nativo; non deve occultare i membri nativi non leggibili o chiudere i gate.
