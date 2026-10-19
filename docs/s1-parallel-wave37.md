# S1 — Preview PNG delle risorse e replay dei vincoli

12 ottobre 2026. Incremento 37, successivo al
[rapporto 36](s1-parallel-wave36.md). Perimetro invariato: 77 comandi,
18 gate complessivi aperti. Nessuna installazione Quest o distribuzione DLL.

## F03 — Preview reale dei BMP caricati

Il servizio HTTP delle risorse può ora offrire una preview PNG autenticata
tramite `GET /imports/resources/{assetId}/preview`. L'host deve configurare
esplicitamente `BmpImportPreviewRenderer` nella policy, con la stessa istanza
di `BmpImportContentInspector` usata per ispezionare i BMP. Nessun formato o
budget viene attivato automaticamente dalla CLI.

Il renderer rivalida i byte, converte righe top-down/bottom-up e padding,
produce PNG RGBA8 con alpha 255 e ignora il byte X dei BMP32 BI_RGB. Codifica
zlib in chunk IDAT consecutivi con CRC, senza allocare un'immagine RGBA
completa o un buffer compresso intermedio. Limiti espliciti per output e
array posseduti dal renderer; quest'ultimo include capacità di encoding,
massima copia finale, riga e header. Non è un cap della memoria del processo:
input del chiamante, overhead CLR e workspace ZLib sono esclusi e dichiarati.
Il cap output è applicato prima della scrittura dei chunk, incluso overhead.

Catalogo, upload e descrittore pubblicano `preview_available`. La route usa
una lease del proprietario autenticato e rispetta scadenza/scope; risorse
estranee, assenti e scadute hanno lo stesso 404. Renderer assente produce
415, contenuto/budget non renderizzabile 422. Cache-Control no-store, hash
del PNG e hash del BMP originale distinti. Il contenuto CAD originale non
viene modificato. `cad_import_available` rimane false: questa è preview
della risorsa caricata, non Anteprima di un'operazione CAD.

Suite bridge `bridge-preview37.trx`: **1.372 PASS**, zero falliti/saltati,
codice 0, **35 nuovi casi**. Reader PNG indipendente nei test verifica CRC,
framing, decompressione, filtri e pixel per nove layout; inoltre equivalenza
BGR/BGRX, orientamento, limiti esatti e meno uno, invalidi, cancellazione,
concorrenza/immutabilità. Test HTTP reali su loopback verificano auth,
proprietario, TTL, catalogo, hash, originale invariato, configurazione e
fallimento del budget senza risposta PNG parziale. Warning preesistenti e
ID duplicato del test di revisione restano nel log, senza nuova copertura.

Selettore/quad Unity, piano CAD, staging, OLE con descrittori preesistenti,
file persistenti linked, history e altri formati restano da integrare.
Le decisioni e i punti di aggancio sono nel
[documento d'integrazione immagini](s1-image-import-integration.md).

## F03 — Getter dei pixel nativi, esito distinto

La sonda esterna confronta `SketchImage.Image` tipizzato e dispatch, più
`ImageAlpha`, dopo i normali controlli di immagine. Nei 16 casi Parte/Lamiera
BMP24/32, top/bottom e linked/embedded, entrambi i getter Image restituiscono
**E_UNEXPECTED**; ImageAlpha restituisce **null** (assenza alpha), senza
inventare un'immagine trasparente o un successo di estrazione.

Nessun pixel nativo è stato acquisito. L'estrattore GDI predisposto richiede
il PID proprietario della finestra Inventor uguale al processo corrente
prima di leggere handle; conversione GDI e cattura in-process non sono state
eseguite e non sono certificate. Occorre un esperimento nell'add-in STA.
La texture dal file congelato risolve le risorse gestite, ma non dimostra
lettura delle immagini incorporate preesistenti di cui mancano i byte.

Report finali `bmp37-part-final.json`/`bmp37-sheet-final.json`: tutti i
16 controlli API di inserimento/posa/dimensioni/rotazione inversa/Delete
completati; tutti i ripristini esatti richiedono ancora pulizia OLE esplicita.
Report iniziale `bmp37-part-pixels.json` conservato: l'errore pixel fermava
il caso prima dei successivi controlli, dunque zero casi completi, otto
divergenze e otto ripristini. Versione finale separa gli esiti pixel dai
controlli geometrici. Compilazioni fallite iniziali (riferimento stdole e
accessor Units) e relativi log sono diagnostici, non PASS.

## C02 — Quote e vincoli dopo ricostruzione delle linee

Nuova sonda `arc-ui-topology-constraint-replay`: sei casi per contesto,
linea H/V e tre politiche dichiarate: osservazione senza replay, quota
totale fra endpoint esterni, quote dei pezzi nel rapporto 40/60. Quest'ultimo
è un input di ricerca, non la semantica attribuita al comando desktop.

La fixture ha una linea quotata tramite parametro utente e vicini collegati
agli endpoint nativi. La sonda registra geometria, chiavi/lineage, H/V/Ground,
membri reali delle relazioni, quote/espressioni e tutti i parametri prima e
dopo Delete/replay. Due aggiornamenti a 24/16 mm verificano identità condivise,
vicini, quote, allineamento e salute solver; Abort confronta anche i parametri.

`replay37-part.json` e `replay37-sheet.json`: **12 osservazioni completate,
24 aggiornamenti e 12 rollback esatti**. Di queste quattro sono controlli
senza replay: la quota totale segue la sorgente, ma i due pezzi non hanno
entrambi vincoli H/V espliciti e il rapporto non è preservato. Le otto
politiche con replay soddisfano i propri controlli; nella politica totale
il punto intermedio non ha una legge proporzionale imposta.

Non prova Split desktop, altri vincoli/curve, profili consumati o dipendenze
di feature; questi requisiti rimangono necessari prima dei comandi prodotto.
La guardia di nesting usa HasParentTransaction del proprio transaction:
CurrentTransaction può essere non nullo anche quando Inventor è idle.

## Runner e consegna

Il lavoro parallelo H01 ha prodotto la
[matrice operativa del runner Quest](s1-quest-runner-integration.md): dodici
percorsi Parte/Lamiera, agganci reali, readback, lifecycle e cleanup. È un
blueprint, non un runner implementato o eseguito. F03 e H01 hanno lavorato
su file separati; tutte le prove Inventor sono state gestite dal coordinatore.

`native37-final-state.json` conferma zero documenti aperti dopo cleanup.
Core e Unity non modificati/rieseguiti: restano rispettivamente 941 e 1.030
PASS precedenti. Nessuna nuova APK: resta quella cumulativa 36, archiviata
sul PC e non installata. Sorgenti/assembly/report/log sono congelati nel
manifest 37, con verifica di integrità dei manifest precedenti. Goal S1 attivo.
