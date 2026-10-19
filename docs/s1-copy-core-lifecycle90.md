# S1 — Copia, prima lettura Core dopo modifiche e riapertura

La sonda90 estende le prove PC88/89 usando lo stesso candidato p3
`29BC225DDCB0609071B8C9A28D6D33EF66A79BEB95D39C1B7DFECE1875BC279A`.
Non introduce ulteriori modifiche di produzione. Il server HTTPS rimane
attivo durante quattro modifiche native delle sorgenti e due riaperture,
in Parte e Lamiera. Il client deve verificare la prima snapshot Core prima
che il worker esegua la lettura indipendente di geometria e glifi.

## Stato

Host e client dedicati sono compilati. `increment90/native-r1` è **FAILED**:
la matrice88 passa, ma il confronto della prima snapshot Core dopo il primo
edit Parte richiedeva erroneamente annotazioni immobili per i due vincoli
intrinseci della linea sorgente. Le loro sole coordinate `annotation_mm`
seguono lo StartSketchPoint della linea, coerentemente con il reader.
La prima snapshot fallita è conservata; nessun retry CAD o oracle successivo.
Cleanup normale, processo dedicato terminato e installazione ripristinata.

Il client corretto verifica solo quei due vincoli tramite identità e quattro
relazioni ordinate esatte: permette esclusivamente il movimento delle
annotazioni verso lo start attuale della sorgente. Le altre proprietà,
vincoli e copie rimangono confrontati esattamente. Build client senza errori
o warning; nuova esecuzione e verifica indipendente ancora da completare.
Il riferimento operativo è
`artifacts/s1-verification/increment90/ongoing-native-state.json`.

**R2 PASS_PC_NATIVE_CORE_LIFECYCLE_SCOPED_EVIDENCE**, confermato dal
verificatore indipendente: sei workflow iniziali, otto coppie native,
dieci rifiuti con venti checkpoint raw esatti; quattro aggiornamenti delle
sorgenti, due riaperture e sei prime snapshot Core; quattordici fasi/ack
con identità e ordine verificati. Quattro IPT archiviati e hash confrontati.
I sette test del verificatore rifiutano alterazioni di copia, owner parametro,
nonce, ordine, proprietà della sorgente, annotazione e bytes IPT.

Cleanup normale e ripristino esatto di manifest/DLL. L'inventario del worker
conserva un'entrata del processo già segnalato come terminato durante il
rilascio degli handle; la verifica OS separata dopo l'uscita del wrapper
conferma l'assenza del PID posseduto. Entrambe le osservazioni sono conservate,
senza confondere un'entrata d'inventario con un processo ancora in esecuzione.
Il report indipendente è `native-r2/verification-core-lifecycle90-r1.json`;
r1 rimane FAILED e il controllo offline corretto non ne cambia l'esito.

La sequenza chiusa di14 richieste usa nonce univoci, identità del processo,
run, scadenza e percorsi posseduti. Nessuna prima lettura dopo modifica o
riapertura viene ripetuta. Le attese per il limite HTTP precedono le
modifiche, senza inserirsi tra completamento nativo e prima snapshot.

Prima del lifecycle deve passare la matrice88: sei workflow, otto coppie
confrontate nativamente, dieci rifiuti e venti checkpoint. Il lifecycle
richiede quattro modifiche delle sorgenti e due SaveCopy/chiusure/riaperture,
con sei prime snapshot Core, riferimenti correnti e copie invariate.

Il campo parametro non copiato della fixture consente esclusivamente la
relocation del proprio percorso verso il documento posseduto riaperto:
stesso documento, chiave parametro, valore13mm e markup chiuso. Non è
supporto alla copia del testo parametrico. Quattro IPT e i rispettivi hash,
report client/nativo, quattordici ack e cleanup devono essere conservati.

## Limiti

Le sorgenti vengono modificate mediante transazioni native della fixture;
la creazione delle copie avviene attraverso il prodotto e Core/HTTPS88.
Questa prova distingue i due percorsi. Restano obbligatori gli altri profili
di Copia, testo parametrico, geometrie e relazioni, confronto desktop, UI,
Quest e input fisico. Tutti i18 gate complessivi S1 restano aperti.
