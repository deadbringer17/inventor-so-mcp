# S1 — Modifiche con immagini native di sfondo, incremento 56

14 ottobre 2026. Implementazione sperimentale e prova HTTP/Core con Inventor
reale su Parte/Lamiera; **18 gate S1 OPEN**.

`Partial` continua a dichiarare che l’immagine non è modificabile. Il nuovo
contratto `mutation_completeness` distingue questa limitazione da una lettura
incompleta che deve impedire qualunque Applica. `NativeMutationComplete`
abilita le altre geometrie soltanto dopo validazione di conteggi nativi e
rappresentati, omissioni/errori, tipi e geometria finita leggibile, identità
univoche nello snapshot e proprietà esatta dei quattro bordi/quattro punti
per ogni immagine opaca incorporata. Le sottoparti condivise restano protette.

Le mappe usano indici locali alla singola risposta. L’anteprima esporta le
collezioni necessarie alla verifica, comprese quelle dei vincoli, senza
identità CAD selezionabili o proprietari persistenti provvisori. Lo stesso
controllo rigoroso viene applicato nel batch nativo e nel Core, quindi nella
sessione e nell’editor Parte/Lamiera. Il contratto non pubblica `insert_image`
fra le capacità e non abilita l’editing di immagini, linked o trasparenti.

La revisione negativa ha aggiunto rifiuti per dati che dichiarano
`supported=true` senza geometria, tipi sconosciuti, Partial non spiegato,
sottoparti malformate/duplicate/estranee, oltre a conteggi incoerenti,
omissioni, errori di lettura, frame mancanti e revisioni stale.
**1.150 Core e 1.615 bridge PASS**, zero SKIP riportati dalle suite; add-in
experimental compilato con zero warning/errori. Il runner xUnit bridge
segnala separatamente un ID duplicato preesistente nella teoria sulle revisioni.
Sorgenti e assembly conservati in `increment56/verified-r4/`.

La sonda `image56-structure-r2.json` passa entrambi i contesti attraverso
host HTTPS/MCP, add-in e Core effettivi. Esercita due immagini per sketch
(legacy e gestita), colori/frame correnti, proprietà dei bordi, selezione
della linea ordinaria e filtro dell’estremo condiviso, 19 rifiuti strutturali
per contesto con rollback, poi il percorso reale
`DesignSession.PreviewAsync → ConfirmRendered → ApplyAsync`.
Il piano richiede la cattura della mesh nativa; snapshot e preview restano
parziali ma strutturalmente completi. Prima della conferma Applica è vietato;
dopo Applica la revisione confermata coincide con quella riletta e le
immagini sono identiche salvo la revisione del documento.

`ConfirmRendered` è simulato nella sonda Core: **non prova rendering Unity
o Quest**. Il run mirato Unity `image-unity56-58-r5.xml` passa sei casi UI
56 e il caso lifecycle 58. La regressione generale
`image-unity56-58-full-r6.xml` passa **1.066/1.066 test Unity**; sorgenti e
assembly effettivi sono conservati in `increment56/unity-full-r6/`.

Il primo run nativo falliva nell’oracle della sonda: confrontava la revisione
visiva riletta con quella nulla restituita per contratto da
`CommitDesignAsync`. Il run successivo confronta identità e revisione CAD
confermate, mantenendo tutti i controlli sul risultato e sulle immagini.
Nessuna modifica di produzione per risolvere questo falso negativo.
I primi run Unity conservano i fallimenti dell’ordine di cleanup della
fixture UI e del messaggio di apertura sovrascritto dalla selezione; corretti
soltanto i test. Diagnostici/sorgenti/DLL restano separati e non sovrascritti.

L’add-in effettivo ha SHA256
`2EF5C9B35759F8F851C82E682B1FD5A42B8D7FC372DA3A94DBD63ABA05B4E68A`.
Registrazione temporanea verso una nuova directory, manifest originale
ripristinato e DLL installata invariata. PID dedicati 91496/73248 terminati;
il processo utente 91056 conserva la stessa ora d’avvio. Nessun documento
dell’utente letto o modificato; fixture e scratch della sonda ripuliti.

**NOT COVERED:** editor immagini/importazione completo, preview bitmap
integrale nel prodotto, linked/trasparenza/altri formati, storico OLE,
oracle desktop completo, nuova APK, installazione o runner Quest,
prestazioni del dispositivo e prove fisiche. Nessun gate complessivo chiuso.
