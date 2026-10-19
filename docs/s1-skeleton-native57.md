# S1 — Blocchi di schizzo: ricerca API nativa, incremento 57

14 ottobre 2026. **18 casi PASS su Parte e Lamiera**, con 14 preview annullate
e ripristinate esattamente, 4 commit e 2 SaveCopy/riaperture. È ricerca API
per la voce `PartCreateSketchBlockCmd` e il gate S1-18: nessuna nuova capacità
di prodotto, parità desktop o chiusura dei 18 gate S1.

La sonda standalone `bridge/tests/S1SkeletonProbe` usa direttamente l'API
Inventor 2027 installata, senza riferimento all'add-in di produzione.
Interop 31.x e firme sono conservati in `skeleton57-api-evidence.json`.
La build effettivamente eseguita passa con zero warning/errori; sorgenti,
DLL e dipendenze sono conservati in `increment57/native-tested-r1/`.

L'API documenta tre primitive distinte:

- [SketchBlockDefinitions.Add](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchBlockDefinitions_Add.htm)
  crea una definizione vuota.
- [SketchBlocks.Add](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchBlocks_Add.htm)
  crea definizione e istanza da oggetti di schizzo selezionati.
- [SketchBlocks.AddByDefinition](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchBlocks_AddByDefinition.htm)
  inserisce un'istanza facendo coincidere il punto d'inserimento della
  definizione con la posizione richiesta nello schizzo ospite.

Per ciascun contesto la sonda crea una definizione con una linea orizzontale,
origine fissata e quota driving collegata esplicitamente al parametro utente
`S1_BlockLength57`. Due istanze rigide condividono la definizione, nelle
posizioni dichiarate di 100/200 mm e 350/−50 mm. La definizione risulta
completamente vincolata e aggiornata; le istanze restano sottovincolate
nello schizzo ospite. Questo non costituisce un default della UI desktop:
le opzioni e le posizioni della ricerca sono esplicite.

La creazione passa sia in preview/Abort sia in commit. Modificando il parametro
da 20 a 35 mm si aggiornano linea sorgente e entrambe le istanze native.
[SketchBlock.GetObject](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchBlock_GetObject.htm)
restituisce gli oggetti corrispondenti nelle istanze: le coordinate osservate
sono nel piano dello schizzo ospite e coincidono con la traslazione attesa.
La verifica separata ricalcola le coordinate attese dai valori dichiarati.

Altri esiti reali, identici in Parte e Lamiera:

| Caso | Esito osservato |
|---|---|
| Rinomina definizione/istanza e riposizionamento | Identità della definizione conservata; Abort esatto |
| Eliminazione di una istanza | Rimangono una istanza e la definizione referenziata; Abort esatto |
| Explode seguito da modifica parametro 35→40 mm | La linea esplosa rimane a 35 mm; l'istanza ancora collegata arriva a 40 mm |
| Conversione di una linea quotata con SketchBlocks.Add | Quota trasferita nella definizione; due istanze aggiornano entrambe a 40 mm tramite il parametro originale |
| Eliminazione di definizione ancora referenziata | Inventor rifiuta con E_FAIL; nessuna rimozione, Abort esatto |
| SaveCopy e riapertura | Definizione, due istanze, geometria a 35 mm e chiavi native conservate; originale identico |

L'osservazione su Explode è coerente con la distinzione fra geometria
conservata e dipendenza parametrica: non va presentata come conservazione del
collegamento. La [documentazione Explode](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchBlock_Explode.htm)
descrive la rimozione dell'istanza e il mantenimento dei contenuti; il
comportamento del collegamento qui riportato deriva dalla sonda reale.

Ogni preview confronta prima/dopo le chiavi native, la geometria, le quote e
le espressioni, i parametri modello/utente, le definizioni, le istanze con
matrici e posizioni, DatabaseRevisionId e Dirty. Il confronto è esatto;
le verifiche numeriche delle lunghezze usano 0,00001 mm. Le copie vengono
aperte nel processo dedicato, chiuse senza salvare, e il documento originale
viene riattivato e ricontrollato.

Il PID dedicato 99556 è terminato; il controllo esterno osserva soltanto
il processo preesistente 91056 con la stessa ora d'avvio. La sonda verifica
un PID distinto prima di leggere Documents o modificare Visible. Non legge
documenti dell'utente. Nessun manifest è stato cambiato e nessuna DLL
installata è stata sovrascritta; gli hash della baseline installata sono
invariati. Directory e documenti temporanei sono stati eliminati.

Evidenze principali:

- `artifacts/s1-verification/skeleton57-native.json`
- `artifacts/s1-verification/skeleton57-installation-r1.json`
- `artifacts/s1-verification/skeleton57-process-exit-r1.json`
- `artifacts/s1-verification/skeleton57-api-evidence.json`
- `artifacts/s1-verification/increment57/native-tested-r1/`
- `artifacts/s1-verification/verify-skeleton57.ps1`

**NOT COVERED:** `PartMakePartCmd`, `PartMakeComponentsCmd`, Schizzo 3D,
curva ponte, opzioni/default del comando desktop, blocchi annidati/flessibili,
rotazioni d'istanza, altre geometrie, immagini/testo nei blocchi, collegamenti
fra documenti, Undo/Redo, rifiuti di riferimenti stale o estranei, API/DTO/reader
di prodotto, piano/commit HTTP, UI, runner Quest e prova fisica. La tolleranza
di ricerca e i risultati sulle due fixture non autorizzano generalizzazioni
agli altri contesti o alle opzioni non esercitate.
