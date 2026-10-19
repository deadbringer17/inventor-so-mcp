# S1 — Vita delle risorse e ripristino OLE

12 ottobre 2026, incremento 42, F03/S1-12/14. Perimetro integrale:
77 comandi e 18 gate aperti. Nessuna capability di inserimento CAD abilitata.

Il deposito piani possiede un allegato `IDisposable` opzionale. Lo rilascia
su discard, sweep di scadenza, eviction, errori di creazione/validazione e
disposal del deposito. `Take` trasferisce ownership al piano di commit;
il consumer dispone in `finally`, anche su errore/cancellazione. Create/List
non possiedono l'allegato; List è distaccata; nessuna risorsa entra nel JSON.
Cleanup tenta tutte le risorse fuori dal lock senza mascherare l'errore
originario con errori Dispose. Se impatto o serializzazione falliscono dopo
Create, PlanChange elimina il piano non consegnato e rilancia l'errore.

Il client controlla i byte UTF-8 dell'intero envelope prima di aprire il
trasporto. Il limite condiviso è 1 MiB escluso LF; include autenticazione,
escape e caratteri multibyte. Un percorso interno per futuri trasferimenti
congela target, credenziali ed endpoint, rifiutando switch/re-pin o riuso
dell'ID con credenziali/pipe diverse. Non trasferisce ancora immagini in
chunk e non pubblica nuovi tool.

La ricerca `arc-ui-bmp-lifecycle` crea una baseline immagine/OLE in una
transaction esterna posseduta e prova Add in transaction annidate:
embedded/linked, file distinti/riuso baseline, uno/tre Add ed errori subito
dopo il primo/secondo Add. Confronta immagini, geometria e identità/file OLE.

**Matrice incompleta e fallita:** 58 casi avviati dei 64 previsti,
56 osservazioni completate con ripristino esatto della baseline preesistente,
due rifiuti linked + stessa sorgente linked. Sei casi successivi non eseguiti.
Non è equivalenza desktop o prova del percorso prodotto.

Il primo diagnostico rifiutava il filename vuoto già dopo Add embedded.
La proprietà è realmente vuota: il journal attribuisce l'identità dal delta
dell'Add isolato e conserva separatamente il file sorgente richiesto,
senza inventare un filename nativo.

Il secondo diagnostico mostra che eliminare il nuovo descrittore linked,
con chiave distinta ma stesso file del baseline, rende il precedente
`SketchImage.ReferencedFileDescriptor` illeggibile (`E_FAIL`).
**Chiavi OLE distinte non provano indipendenza del collegamento.**
La versione finale precontrolla tutto l'insieme e rifiuta la cancellazione
se un descrittore condivide il file linked protetto. Non esegue mutazioni
del repair interno prima del rifiuto, che resta FAIL. Dopo Abort esterno,
senza immagini, il journal della fixture ripristina la baseline vuota:
quattro cicli esterni esatti, file temporanei della versione finale rimossi.

Il documento Quest precedente, pulito prima della chiusura, è stato riaperto
attivo con la stessa revisione dopo ogni run. Zero documenti di prova prima
della riapertura. Nessuna transaction desktop terminata/abortita: l'idle
`Unidentified transaction`, priva di documento, è stata solo osservata.
Gli stati transaction sono documentati da
[Autodesk](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/TransactionStateEnum.htm).

Residui: risoluzione owner/asset ai due ingressi, lease del piano immagine,
chunk sigillati, journal add-in integrato con revisione/rollback,
reader/rendering CAD, bozza Quest e preview/commit. I file linked richiedono
deposito duraturo e preservazione dei riferimenti preesistenti. Undo/Redo,
OLE di altre applicazioni e documenti salvati non coperti. Punti/curve,
ACAD e altri formati restano nel perimetro.

Nessuna nuova APK, installazione add-in/host o prova Quest. Core 1.000 e
Unity 1.049 sono le verifiche precedenti dell'incremento 41. Sorgenti, DLL
delle sonde, diagnostici e manifest in `artifacts/s1-verification/increment42`
e nei file `image42-*`; la suite bridge finale è registrata nel manifest.

Suite bridge finale: **1.401 PASS, zero FAIL, un SKIP**, 1.402 casi, exit 0.
Il test di avvio host è saltato perché il processo gestito è già attivo;
non è stato arrestato per il test. Build add-in sperimentale e sonda native
PASS; DLL compilate conservate, senza installazione. La matrice OLE nativa
resta FAIL con il rifiuto descritto sopra; tutti i 18 gate S1 restano aperti.
