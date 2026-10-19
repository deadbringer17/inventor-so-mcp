# S1 — ricerca nativa Copia di cerchi e archi

La sonda PC92 completa in r2 dieci casi e venti chiamate native Copy,
con dieci Abort e stato completo prima/dopo esatto. Parte e Lamiera
esercitano cerchio, arco di90°, attraversamento angolare, arco maggiore
di270° e costruzione clockwise. Ogni caso copia prima la curva singola,
poi una selezione di curva, curva complementare e linea libera.

La prima copia deve rimanere invariata durante la seconda fase. Il controllo
usa chiavi native, ruoli espliciti di centro/estremi, geometria analitica,
campioni dell'evaluator e grafo dei vincoli osservato sulle sorgenti.
Le copie hanno punti distinti dagli originali e dalle copie precedenti.
Nessun TextBox entra nelle raccolte passate a MoveSketchObjects.

R1 rimane FAILED: TypeMismatch nella prima lettura della baseline, prima
di qualsiasi transazione o copia. R2 inizializza gli array out di GetStrokes
come il reader già verificato e registra fasi di lettura più precise.
La correzione riguarda soltanto la sonda; il candidato del plugin è ancora
p3 dell'incremento88.

Le sessanta snapshot complete vengono conservate separatamente in
curve-state92-NNN.json, con hash referenziati dal report. Non vengono
eliminati dati dal confronto per ridurre la dimensione del report.
Il wrapper termina normalmente e ripristina manifest e DLL. Una verifica
OS dopo l'uscita conferma l'assenza del PID posseduto, distinta dall'entrata
transitoria del processo terminato nell'inventario del worker.

Il verificatore indipendente conferma venti fasi, dieci Abort esatti e
sessanta stati raw. Cinque alterazioni semantiche vengono rifiutate anche
aggiornando gli hash dei file alterati: raggio, punto strutturale originale
riutilizzato, vincolo verso la sorgente, parametro estraneo e Dirty dopo Abort.
Il confronto sorgente/copia usa tolleranza1e-10 per dominio/angoli,
1e-8mm per raggio e1e-5mm per coordinate. Il dominio può differire di circa
1e-16 tra oggetti distinti; l'uguaglianza flottante di Json.NET non equivale
a identità dei bit. Sorgenti, copie precedenti, dati estranei e stato dopo
Abort sono invece confrontati mediante serializzazione esatta, senza
questa tolleranza. Report: native-r2/verification-curve-copy92-r1.json.
Questa prova osserva API native in transazioni successivamente abortite:
non dimostra ancora copia tramite il prodotto/Core, commit, aggiornamenti
delle sorgenti, persistenza, Undo/Redo, confronto desktop o Quest.
I formati della fixture sono quelli nativi di default, senza layer,
override colore o attributi aggiuntivi; nessun getter opzionale è risultato
UNKNOWN nei venti casi. Altri formati, geometrie e relazioni restano obblighi
della specifica completa. Tutti i18 gate complessivi restano aperti.
