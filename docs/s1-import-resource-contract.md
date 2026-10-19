# S1 F03-A — Contratto locale delle risorse importabili

L'incremento introduce descrittori immutabili e validazione locale dei metadata
per Inserisci immagine, punti e ACAD (S1-12, specifica §5.7 e piano F6).
Non trasferisce file, non risolve asset sul server e non importa oggetti CAD.
Nessun gate S1 viene chiuso da questo contratto.

## Contratto e policy

`SketchImportResourceBundle` contiene un asset radice e tutti i descrittori delle
sue dipendenze. Gli ID sono token opachi ASCII (lettere, cifre, `_`, `-`), con
lunghezza massima dichiarata dalla policy: nessun filename, URL o path del
Quest/Windows. La sintassi non dimostra esistenza, autorizzazione o scope: il
server dovrà verificarli contro il proprio registro di asset.

Ogni risorsa dichiara tipo (Image, Points, Acad), formato mediante token esatto,
byte dichiarati e dipendenze. La policy del chiamante fornisce esplicitamente
formati ammessi per tipo, unità e fattori in mm, limiti di risorse/dipendenze,
lunghezza dei token, byte singoli/totali, pixel, punti e coordinate. Nessun
default rappresenta un limite del desktop. Un formato ammesso dalla policy
non diventa automaticamente una capacità nativa verificata.

Image richiede dimensioni in pixel positive e non accetta metadata geometrici.
Questo schema descrive immagini raster decodificabili; eventuali oggetti OLE
Office nativi richiedono un contratto distinto o una revisione esplicita.
Points richiede unità, fattore di conversione e numero positivo di punti;
Acad richiede unità/fattore, senza fingere di essere un elenco di punti.
Le unità assenti o non risolte non diventano implicitamente mm: l'host dovrà
risolverle prima di produrre il descrittore. Eventuali bounds 2D sono nelle
unità sorgente; il validatore controlla ordine, finitezza e coordinate in mm.
Non sostituiscono la verifica di ogni coordinata nel contenuto decodificato.

La validazione rifiuta ID duplicati, dipendenze duplicate nello stesso oggetto,
risorse mancanti, cicli e risorse estranee alla radice. Una dipendenza condivisa
fra più nodi è ammessa e pesa una volta nel budget totale. L'attraversamento è
iterativo; il controllo dei byte evita overflow. Le collezioni sono copiate
alla costruzione, così il chiamante non può alterare i metadata dopo il controllo.

## Fonti e lavoro ancora richiesto

Le fonti Autodesk 2027 consultate distinguono:

- [Importazione punti](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-Help/files/GUID-D4A4288F-32F1-4E23-A382-930EBA152E87.htm):
  Excel, creazione di punti/linee/spline; dati importati non associativi alla
  sorgente e gestione separata delle coordinate Z in 2D.
- [Importazione dati AutoCAD nello schizzo](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-Help/files/GUID-541DB455-8FC7-4CEC-BC4C-E270864217E4.htm):
  DWG/DXF e scelta di layer/oggetti/opzioni; il contratto non implementa queste scelte.
- [Esempio API di decalcomania](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/DecalFeatures_Add_Sample.htm):
  aggiunta di una SketchImage da BMP; il percorso filesystem rimane un dettaglio
  del futuro adattatore host, mai un ID nel contratto condiviso.

Le opzioni native, i formati realmente accettati nell'installazione e le unità
vanno verificati con oracle desktop e sonde prima di offrire le capacità XR.
Posizionamento, scala, orientamento, collegamento/importazione e connessione
dei punti appartengono al futuro comando di importazione, non a questi metadata.
Restano da realizzare selettore Quest, trasferimento e registro host, sniffing e
decodifica dei byte, verifica della lunghezza reale, dipendenze esterne,
autorizzazione, oggetti nativi, preview/commit/rollback e cleanup.

Sono aggiunti test del validatore, non eseguiti dall'agente delegato: la verifica
Core e la revisione appartengono al coordinatore. Non sono state avviate build,
Unity, Inventor o installazioni Quest.
