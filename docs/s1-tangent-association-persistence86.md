# S1 — Dipendenze e riapertura Arco tangente86

15 ottobre 2026. Sonda integrata e collaudo in corso; nessun nuovo gate
complessivo chiuso. Il Quest è rimandato su richiesta dell'utente.

Il campione Parte/Lamiera usa una linea orizzontale con origine fissata,
quota guidata da `S1TangentLength86=20 mm` e un punto nativo fissato a(50,10).
L'arco tangente parte dall'End della linea e termina sul punto nativo.
Per L=20/25/30mm il raggio analitico è `((50-L)^2+100)/20` e il centro
è(L,R). Due modifiche del solo parametro devono aggiornare l'arco senza
ricrearlo, conservando chiavi dei punti, quota, vincoli e geometrie originali.

La sonda Core/HTTPS usa preview, rollback rappresentato esatto e conferma
di rendering sintetica PC. La PRIMA richiesta dopo ogni Apply legge lo
snapshot con LastCommit; nessuna lettura di stato, oracle COM o riparazione
la precede. Poi un marker ordinato abilita una lettura nativa indipendente
nel worker posseduto. Sono richiesti sei checkpoint20/25/30 in Parte/Lamiera.

Solo dopo client riuscito e sei checkpoint il wrapper invia `persist`:
HTTP viene fermato, si salvano copie, si chiudono solo gli originali owned
senza salvarli e si adottano immediatamente le copie riaperte. La prima
lettura semantica verifica geometria, vincoli e chiavi senza Rebuild/Update
espliciti. Il wrapper conserva l'ordine marker→comando e archivia le quattro
IPT con hash, oltre a log, JSON pubblici e sorgenti/binari eseguiti.

## Correzioni alla bozza prima dell'esecuzione

La bozza86 rimane conservata separatamente. Il root ha corretto riferimenti
dei project nei percorsi reali, cast espliciti COM e una stringa regex C#.
È stata corretta anche un'assunzione sulla document identity: il composer
reale usa `doc_+InternalName` quando il nome è unico tra i documenti aperti.
Non aggiunge un hash del percorso in quel caso. La copia riaperta può quindi
avere lo stesso wire ID; nessuna vecchia sessione HTTP o reference Core viene
riutilizzata dopo la riapertura. La prova non inventa un nuovo ID per far
passare l'asserzione. Un futuro snapshot HTTP della copia richiede comunque
un nuovo handshake ed è fuori da questo campione nativo.

Il vecchio processo utente77764 risulta assente prima del run: il controllo
mantiene il divieto di acquisire i PID storici e rifiuta un eventuale riuso
dell'identità protetta. L'inventario dinamico protegge tutti gli Inventor
preesistenti. Non è richiesta la presenza di un'app personale per creare
una nuova istanza dedicata già autorizzata.

## Tentativi e limiti

Host/client compilano. Il tentativo `increment86/native-r1` fallisce dopo
il primo snapshot sulla quantità di vincoli nuovi, prima del primo
checkpoint: zero workflow completi certificati. Il cleanup e il ripristino
di manifest e DLL sono riusciti. Il tentativo r2 conserva l'inventario dei
vincoli e acquisisce un'ispezione nativa anche dopo errore del client per
diagnosticare la divergenza, senza trasformare il fallimento in PASS.

L'ispezione r2 conferma cinque vincoli originali conservati e quattro nuovi:
due Coincident degli estremi, un Concentric del centro nativo e la tangenza.
Il test iniziale presumeva erroneamente un solo nuovo vincolo. La correzione
non accetta quantità arbitrarie: verifica tutti quattro i binding, inclusi
linea.End→arco.Start, target.Point→arco.End e punto reale→arco.Center.
Congela poi l'intero insieme di nove identità per i due aggiornamenti.

`increment86/native-r3` termina con sei workflow/checkpoint riusciti:
Parte e Lamiera20→25→30mm, quattro aggiornamenti parametrici complessivi,
due copie salvate e riaperte, quattro IPT archiviate con hash. Raggi nativi
50/36,25/25mm, centri e settore CCW coincidono con il calcolo indipendente;
punti sorgente/target condivisi, tutte nove le chiavi dei vincoli e chiavi
di geometrie/quota/parametro rimangono stabili. Il verificatore indipendente
`verify-tangent86-association.ps1` conferma Core→chiavi native, sei marker
ordinati prima dell'oracle, stato persistito e file archiviati:
`increment86/verification-r1.json`, **PASS_SCOPED_EVIDENCE**.

Il processo dedicato33100 termina normalmente; manifest e DLL installati
sono ripristinati. Il DLL provato è83 `producer-r1`, SHA256
`AAE71E29B0131BE3D8D91CD6C124FBA50D9C1B2F724EAB4BF10FE8BA9F796797`.
Questa evidenza riguarda la sorgente linea del campione; non risolve né
certifica il candidato sperimentale con maniglia spline85.

Fuori dal campione: solver spline85, altre sorgenti, desktop completo,
rollback nativo Dirty/DatabaseRevisionId per ogni preview, storico Undo/Redo,
primo snapshot HTTP della copia, UI/Quest/fisico, prestazioni e3D. I18 gate
S1 rimangono aperti.
