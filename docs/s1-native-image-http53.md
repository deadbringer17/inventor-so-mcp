# S1 — Immagini native attraverso HTTP e Core, incremento 53

14 ottobre 2026. **2/2 casi PASS**, Parte e Lamiera, nel percorso completo
`InventorBackend → MCP HTTPS → SketchImageAssets → pipe dell'add-in reale → PNG HTTPS → NativeSketchImageClient`.
Questa evidenza riguarda il backend e il client Core. I 18 gate S1 restano aperti.

La nuova sonda `bridge/tests/S1ImageHttpProbe` crea un processo Inventor
dedicato e due documenti temporanei. Ogni documento contiene un'immagine
embedded legacy, priva degli AttributeSet gestiti, ricavata da un BMP
32 bit top-down di sei pixel distinti. La sonda applica una rotazione
geometrica di 37° e MirrorHorizontal; elimina il sorgente prima della query.

Il server è il vero `HttpHost`, con porta loopback casuale, certificato
autofirmato e target fissato all'identificatore esatto del nuovo processo.
L'autenticazione usa un proprietario dedicato; un secondo proprietario riceve
404 tentando di leggere lo stesso asset. Il trasporto della sonda è un
adattatore .NET dedicato, con pin del certificato, rifiuto dei redirect e
lettura limitata al numero di byte richiesto. Il codice Core usato è quello
di produzione; questo adattatore non è `UnityHttpTransport`.

In entrambi i contesti la run finale r5 verifica:

- Chiamata MCP e download effettivo del PNG attraverso HTTPS autenticato.
- Identità di documento, revisione, schizzo e immagine, formato e dimensioni.
- Decodifica PNG indipendente con sei valori RGBA esatti, alpha 255.
- Quattro angoli confrontati con la teoria ricavata da Position/Width/Height
  prima delle trasformazioni, tolleranza 0,00001 mm.
- Stato nativo identico prima/dopo: DatabaseRevisionId, Dirty, percorso,
  geometria e dimensioni, flag, numero di entità/immagini/attributi e chiavi
  native di linee, punti e descrittore OLE. Anche due letture consecutive
  prima della query devono essere identiche.
- Revisione e revisione visuale del backend invariate.
- Documento chiuso senza salvataggio, processo dedicato terminato e directory
  temporanea eliminata.

Le run r1–r4 sono conservate come **FAIL diagnostici**, senza rinominarne
l'esito. Il confronto iniziale usava indirizzi IUnknown dei proxy COM
esterni. La diagnostica r4 mostra che, in Lamiera, tali indirizzi cambiano
durante il lavoro asincrono mentre geometria, stato del documento e revisioni
restano identici. La sonda finale confronta invece le chiavi native
`GetReferenceKey(context0)` di tutte le stesse entità: il confronto esatto
passa in Parte e Lamiera. Non è stato modificato codice di produzione per
risolvere questa falsa segnalazione. La diagnostica r3 aveva inoltre omesso
il secondo confronto dal report per una riassegnazione di JArray con parent;
r4 corregge soltanto la raccolta dell'evidenza.

Il processo Inventor preesistente 91056 è rimasto aperto: la sonda non legge
i suoi documenti. Prima di qualsiasi accesso a Documents o modifica verifica
che COM abbia restituito un PID distinto, inizialmente vuoto. Il wrapper usa
una nuova directory per la DLL e cambia temporaneamente soltanto il manifest;
non sovrascrive la DLL installata. Manifest originale e DLL installata sono
verificati tramite SHA-256 come ripristinati/invariati in tutte le cinque
run. PID e ora d'avvio del processo preesistente restano identici. La verifica
esterna finale osserva il solo PID 91056 e l'assenza del PID dedicato 102224.
Il conteggio immediato del wrapper può ancora includere il processo durante
la chiusura; non sostituisce la verifica separata del PID.

La build finale della sonda passa con zero errori; rimane il warning NU1903
della dipendenza SQLite transitiva già presente. Tutte le run conservano
sorgenti e intero output binario effettivamente eseguito. La run finale usa
l'add-in con SHA-256 `3D919DFDDA5D8EFE3E0B6581DDCEFF18BB53A99C5747A90A51D6B0134623155C`.

Evidenze principali:

- `artifacts/s1-verification/image53-http-core-r5.json`
- `artifacts/s1-verification/inprocess-install53-r5.json`
- `artifacts/s1-verification/image53-process-exit-r5.json`
- `artifacts/s1-verification/increment53/inprocess-tested-r5/`
- `artifacts/s1-verification/verify-image-http53.ps1`

La verifica dedicata controlla solo queste prove native HTTP/Core e le
diagnostiche r1–r4; non costituisce il manifest complessivo dell'incremento.
Non copre rendering Unity, shader nel visore, Quest, prova fisica,
trasparenza, immagini linked, altri formati/dimensioni o Undo/OLE.
