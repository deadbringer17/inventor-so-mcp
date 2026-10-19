# S1 — Schizzo XR: parità con Inventor 2027 e tavolozza sinistra ampliata

Data: 6 ottobre 2026, Europe/Rome. Stato: **specifica proposta, implementazione e collaudo da eseguire**.

Identificativo dell'implementazione: **S1**. Criteri di accettazione: **S1-01…S1-18**.

Richiesta: ampliare la tavolozza nella mano sinistra e rendere disponibili le funzioni della scheda Schizzo di Inventor 2027, con comportamento equivalente. Questa consegna contiene analisi, requisiti e piano; non modifica il client, il bridge o l'APK.

Piano: [implementazione](../plans/2026-10-06-schizzo-parita-inventor-2027.md).
Baseline completa: [inventario dei 77 comandi](../../xr-sketch-2027-gap-inventory.md), con [lettura nativa JSON](../../xr-sketch-2027-ribbon-baseline.json).

## 1. Evidenza e perimetro

Sono state confrontate quattro fonti:

1. Immagine dell'utente: Schizzo, Crea, Serie, Modifica, Vincola, Inserisci, Formato e Fine; i menu a discesa non sono aperti.
2. Ribbon dell'Inventor **2027 installato**, letta tramite COM il 6 ottobre alle 21:16 Europe/Rome: ribbon `Part`, tab `id_TabSketch` e `id_TabSketch_Exit`, controlli e figli. Target confrontato con MCP: processo 97300. Nessun comando eseguito, nessuno schizzo aperto. `InternalName`, `DatabaseRevisionId` e `Dirty` del documento attivo identici prima/dopo. La lettura non prova il funzionamento dei comandi.
3. Codice corrente di XR e bridge, compresi i file con modifiche locali già presenti. La baseline descrive il checkout corrente; il piano dovrà fissare commit e diff prima dell'implementazione.
4. Documentazione Autodesk 2027 per geometrie, spline e vincoli, citata nei paragrafi pertinenti.

La lettura ha trovato **77 comandi terminali**: Schizzo 2, Crea 32, Serie 3, Modifica 9, Vincola 16, Scheletro 3, Inserisci 3, Formato 5, Parametri 1, Esci 3. Il conteggio comprende comandi contestuali e Schizzo 3D: non significa che tutti siano disponibili nello schizzo 2D corrente. La visibilità attuale, eventuali pannelli nascosti, personalizzazioni, menu overflow e opzioni di dialogo devono essere verificati nella fixture di F0.

Il requisito principale è la parità **dello schizzo 2D di parte e lamiera**, comprendendo i menu a discesa. La parità del sottoinsieme visibile nello screenshot è un traguardo intermedio, non la consegna finale. I comandi contestuali censiti devono avere una destinazione esplicita: Schizzo 3D, curva ponte, proiezione verso 3D e Scheletro hanno una fase di estensione propria. Disegni IDW e schizzi di assieme richiedono una baseline separata; questo inventario riguarda `Part`.

## 2. Diagnosi del gap

| Area | Evidenza nel codice corrente | Differenza da colmare |
|---|---|---|
| Tavolozza | `PaletteView.Create`: 160 × 110 mm, griglia 2 × 4, celle 75,5 × 20 mm | Poco spazio per gruppi, varianti, opzioni e selezioni multiple |
| Catalogo | `ActionCatalog.MaxPalette = 8`; eccezione oltre il limite | Aumentare il limite senza gestire layout, picker e navigazione provoca regressioni |
| Geometria XR | `SketchShape`: Line, Rectangle, Circle | Archi, punti, ellissi, spline, poligoni, asole e testo non sono strumenti XR |
| Schizzi letti dal CAD | `SketchSnapshot.Parse`: linee e cerchi; punti ignorati, altre curve contate come unsupported | Non è possibile selezionare e modificare fedelmente uno schizzo completo già esistente |
| Schizzo esistente | Bozza `SketchDraft` crea un nuovo schizzo; picker di schizzo esistente usato per estrusione | Serve un editor di entità native, non ricreare lo schizzo a ogni modifica |
| Quote | Lunghezza linea, larghezza/altezza rettangolo, raggio cerchio nella bozza | Mancano quote tra entità, angoli, diametri, driven, espressioni, modifica mirata e automatica |
| Vincoli | Sei scelte esplicite: tangente, uguale, simmetria, parallelo, perpendicolare, concentrico | Coincidente/H/V automatici non sostituiscono i corrispondenti comandi espliciti; mancano collineare, fissaggio, levigato e opzioni |
| Modifica/serie | Assenti nel provider Schizzo XR | Sposta, Copia, Ruota, Taglia, Estendi, Dividi, Scala, Allunga, Offset e serie devono agire su curve 2D |
| Lamiera | Picker di schizzi CAD esistenti, non editor completo condiviso | Integrare lo stesso editor per parte/lamiera evitando due implementazioni divergenti |
| Capacità backend | Alcuni comandi di base nel batch stabile; geometrie/quote avanzate nel tier sperimentale | Handler presente non significa funzione XR presente o equivalenza verificata |
| Capacità/sessione | Max 32 operazioni per transazione; bozza max 30 geometrie + vincoli | Uno schizzo persistente deve supportare più comandi successivi, senza spezzare una singola operazione atomica |

Differenze semantiche decisive:

- `design.fillet` e `design.chamfer` operano sugli spigoli 3D. **Raccordo e Smusso della scheda Schizzo sono operazioni 2D distinte** e richiedono azioni, selezione e handler propri.
- `rectangular_pattern` e `circular_pattern` del bridge ripetono feature; non realizzano una serie parametrica di geometrie nello schizzo.
- `move_sketch_point` modifica un punto. La descrizione corrente lo presenta come sostituzione di trim/extend: non soddisfa Taglia/Estendi/Dividi, che dipendono da intersezioni e topologia.
- Uno snap visivo non è un vincolo persistente. L'evidenza del vincolo è il risultato del solver Inventor.
- La quota XR sul cerchio è oggi un raggio: non equivale a offrire tutte le modalità della Quota generica.
- `SketchDraft` usa riferimenti posizionali e quattro slot per elemento. Questo schema non basta per curve generiche né per entità native che vengono divise, eliminate o riordinate.

## 3. Significato verificabile di «funziona nello stesso modo»

Per ogni comando/variante l'inventario deve registrare: nome italiano, `InternalName` Inventor, contesto, prerequisiti, tipi e ordine delle selezioni, opzioni e default, passi di input, risultato nativo, vincoli/quote creati, dipendenze, errori, annullamento, modifica successiva e livello di evidenza.

La parità richiede:

- stessi input CAD, stesse unità, stessi default applicabili e stessa geometria entro la tolleranza;
- stesso tipo di oggetto nativo, topologia e comportamento parametrico dopo una modifica;
- stessi vincoli, associatività, opzioni di mantenimento/rimozione dei vincoli e stato del solver;
- stesso significato di chiusura del comando e dello schizzo, con annullamento coerente;
- controlli XR accessibili senza ricorrere al mouse/monitor PC per concludere l'operazione.

Il dispositivo cambia il gesto: click diventa Trigger sulla penna destra, puntamento diventa penna/raggio sul foglio, trascinamento diventa Trigger tenuto. Il risultato CAD deve restare equivalente.

**Decisione di prodotto:** conservare il flusso XR Anteprima → Applica della specifica M6. I click desktop possono modificare immediatamente lo schizzo; in XR raccolgono una bozza e Applica effettua la transazione. Questa differenza temporale è deliberata e deve essere visibile. Non si dichiara identità del workflow desktop finché questo requisito rimane. Nessun pulsante può mostrare successo dopo aver soltanto eseguito una simulazione locale.

## 4. Tavolozza sinistra: dimensioni e organizzazione

### 4.1 Dimensione proposta

Modalità predefinita **320 × 220 mm**: raddoppio di larghezza/altezza, area quadrupla. Sempre ancorata al controller sinistro. Dimensione regolabile fra 280 × 190 e 360 × 250 mm, proporzioni coerenti; il ridimensionamento interessa solo la UI, non il modello o le coordinate CAD.

Layout di riferimento a 320 × 220 mm:

- margini laterali 6 mm, verticali 4 mm;
- intestazione 16 mm con «Schizzo», nome CAD e breadcrumb della famiglia/variante;
- selettore gruppi 32 mm, due righe, con etichette complete;
- corpo 144 mm: massimo 16 azioni in griglia **4 × 4**, celle 74 × 33 mm, spazi 4 mm;
- indicatore inferiore 8 mm per stato/selezione; spazi fra le sezioni 4 mm.

Testo almeno 7 mm, icone base 16 mm, label leggibili e tooltip M10. A dimensioni inferiori ridurre il numero di celle visibili e usare sottogruppi; non comprimere testo/hit area per far entrare tutti i pulsanti. Il layout effettivo va validato per tutte le dimensioni offerte.

La posa attuale inclinata di 45° è una baseline da verificare. Ampliare la superficie richiede spostare il centro verso l'esterno della mano, evitare l'intersezione con controller/avambraccio e consentire regolazione della posa. Solo la prova fisica può approvare offset e inclinazione definitivi. Durante la selezione non cambiare orientamento automaticamente. Alla perdita di tracking applicare il fallback della shell, annullare la cattura e preservare la bozza.

Modalità facoltativa «Fissa al banco»: congela la posa per lavorare a lungo, con «Riaggancia alla mano» esplicito. La modalità predefinita resta in mano sinistra.

### 4.2 Gruppi corrispondenti a Inventor

Dentro **Schizzo**: Schizzo · Crea · Serie · Modifica · Vincola · Inserisci · Formato · Parametri · Fine. Scheletro e strumenti contestuali sono raggiungibili nel gruppo avanzato quando il contesto li ammette. Il controller sinistro cambia gruppo con stick ←/→; ↑/↓ conserva lo zoom della mappatura M9. I gruppi sono anche cliccabili con la penna.

La pagina Crea espone famiglie: Linea, Cerchio, Arco, Rettangolo, Asola, Spline, Ellisse, Poligono, Raccordo, Smusso, Testo, Punto, Proietta. La freccia di famiglia apre le varianti, non avvia una variante arbitraria. Memorizzare l'ultima variante scelta mostrando nome e icona corretti. Spline avanzata/curva da equazione/curva ponte sono nel relativo sottogruppo.

Vincola espone direttamente i 12 vincoli geometrici della baseline, Quota, Quota automatica, Mostra vincoli e Opzioni vincolo (16 azioni); modifica/eliminazione di quote e vincoli nel pannello della selezione. Il punto medio resta selezionabile come relazione nel sottogruppo appropriato anche se non è un pulsante terminale di questa ribbon.

Nessuna funzione deve sparire per overflow. Sottogruppi con breadcrumb e Torna; per elenchi lunghi usare sezioni raggiungibili come oggi, con stato preservato. Il numero di comandi totali non deve coincidere col numero di pulsanti simultanei. Il gruppo Fine contiene Concludi schizzo e Annulla schizzo; la conferma di una mutazione resta sulla barra Applica.

### 4.3 Cambi strutturali

Separare il limite di layout dal catalogo semantico: capacità di pagina calcolata da `PaletteLayout`, con profilo 16 celle per Schizzo ampliato e profili compatibili per altri contesti. Picker basati sulla capacità effettiva, senza usare un nuovo `MaxPalette` globale come divisore indiscriminato. `MaxContext = 6` dell'anello resta indipendente.

Aggiornare `ContextTabs` e `ActionCatalog`: i sottogruppi Schizzo devono essere raggiungibili in parte/lamiera, senza confondere i vincoli di assieme con quelli dello schizzo. Compatibilità degli id esistenti tramite alias quando necessario. Un'unica azione invocabile da tavolozza, anello e voce; motivi di disabilitazione coerenti. Applica vocale continua a chiedere conferma sul controllo dedicato.

## 5. Interazioni e comportamento per famiglia

### 5.1 Selezione ed editor

Selezionare punti, estremi, centri, curve, lati di rettangolo, testo, immagini, quote e vincoli nativi. Preselezione e selezione multiple, filtri di tipo, scelta fra entità sovrapposte e finestre di selezione con comportamento verificato contro Inventor. Distinguere «tutta la curva» da «estremo» e da «segmento fra intersezioni».

Sessione: documento → schizzo nuovo/esistente → comando → selezioni/opzioni → bozza → anteprima nativa → Applica → schizzo ancora aperto. Concludi schizzo termina la sessione, non applica di nascosto una bozza. Con bozza pendente presenta Applica oppure Scarta bozza; dopo la scelta termina lo schizzo. Annulla comando/X torna al passo precedente; Annulla schizzo riproduce l'esito desktop verificato sulla fixture, senza cancellare modifiche desktop estranee.

Adattare il gesto desktop alle istruzioni per controller e mostrarne la legenda. Non sovraccaricare doppio Trigger, Back, precisione, zoom e PTT M9. La UI intercetta gli input: un click sul pulsante non deve disegnare anche sul foglio.

### 5.2 Creazione

Linea singola e continua, punto medio, passaggio linea/arco tangente; cerchio centro/raggio e tangente; archi tre punti, centro e tangente; rettangoli due/tre punti e centrati; cinque varianti di asola censite; ellisse, poligono inscritto/circoscritto; punto normale/centro foro. I click seguono gli stessi punti caratteristici, con input numerico equivalente.

Spline interpolante e a vertici di controllo devono produrre oggetti nativi con editing dei punti/maniglie, chiusura e opzioni presenti nel 2027 installato. Curva da equazione: editor di espressioni e parametri compatibile con Inventor; non eseguire codice arbitrario. Le varianti di rettangolo ed ellisse sono documentate in [Geometrie 2D Autodesk 2027](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-Help/files/GUID-D489CE6D-7299-4211-A43A-F3580A4BA357.htm); le due famiglie di spline e i loro controlli in [Spline Autodesk 2027](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-Help/files/GUID-DB15C479-66F2-456A-9E54-40A60F83DC92.htm).

Raccordo/Smusso 2D: scelta delle curve, lato/soluzione, raggio o distanze, opzioni di taglio e conservazione vincoli. Testo ordinario e testo sulla geometria: contenuto, stile, orientamento, allineamento, collegamento ai parametri e modifica successiva come nel comando nativo. Serve input testuale XR, non solo tastierino numerico.

### 5.3 Proiezione

Proietta geometria: spigoli/vertici/riferimenti ammessi; legame associativo e possibilità di interromperlo dove consentito. Spigoli di taglio: intersezione col piano; modello piatto: contesto lamiera; geometria DWG: riferimenti CAD importati. Proiezione verso 3D: fase contestuale, con editor di destinazione. Non sostituire la proiezione associativa con una copia di coordinate.

### 5.4 Serie

Serie rettangolare: entità, direzioni, conteggi, passo/estensione e opzioni native. Serie circolare: entità, centro/asse, numero, angolo e distribuzione. Specchio: entità, linea asse e relazioni native. Modificare l'originale e la definizione deve aggiornare i risultati come in Inventor, comprese esclusioni delle istanze quando presenti. Le copie scollegate e le serie di feature non soddisfano questo requisito.

### 5.5 Modifica

| Comando | Input ed esito richiesto |
|---|---|
| Sposta | Entità, punto base/destinazione o delta; mantenimento/rimozione vincoli secondo le opzioni native |
| Copia | Selezione e riferimento; copie e relazioni secondo i default/opzioni del desktop |
| Ruota | Selezione, centro, angolo firmato; stessa gestione dei vincoli |
| Taglia | Puntamento del tratto fra intersezioni; evidenziare il tratto rimosso prima della conferma |
| Estendi | Estremo/lato e intersezione limite; stessa curva e vincoli risultanti |
| Dividi | Curva e punto di divisione; nuove entità/continuità/riferimenti coerenti |
| Scala | Entità, punto base e fattore; trasformazione CAD distinta dallo zoom del foglio |
| Allunga | Regione/estremi e spostamento; distinguere estremi inclusi e geometrie interamente selezionate |
| Offset | Curve/catena, lato, distanza, chiusura e relazione parametrica nativa |

Per Taglia/Estendi/Dividi una sonda API deve verificare gli strumenti effettivamente esposti dall'interop 2027. Se serve ricostruire la curva, trasferire correttamente le relazioni e dipendenze oppure dichiarare il caso non supportato. Invocare una `ControlDefinition` per lasciare un dialogo sul PC non costituisce implementazione XR. Nessuna chiamata API ipotizzata è considerata disponibile senza sonda.

### 5.6 Quote e vincoli

Quota generica: riconoscimento da selezione e posizione del testo, con scelta esplicita della variante; lunghezze, distanze H/V/allineate, angoli, raggi/diametri e ulteriori casi presenti nel dialogo 2027. Quote driving/driven, espressioni con unità, riferimenti a parametri, nome e posizione del testo modificabili. Quota automatica deve leggere i gradi di libertà e produrre le stesse categorie di quote/vincoli, con preview delle aggiunte.

Vincoli espliciti: coincidente, parallelo, tangente, collineare, perpendicolare, levigato, concentrico, orizzontale, simmetrico, fissaggio, verticale, uguale. Levigato richiede la continuità ammessa dal comando nativo, non una semplice tangente. Controllare compatibilità dei tipi, cardinalità e significato delle selezioni, mostrare/filtrare i simboli e consentire eliminazione mirata.

Coincidenza punto-curva, H/V e fissaggio hanno cardinalità diverse: abbandonare il wizard che richiede sempre due geometrie. [Vincoli geometrici Autodesk 2027](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-Help/files/GUID-A85A7A30-7D81-4C75-8769-CAD034EEA930.htm) documenta questi flussi distinti. Mostrare sottovincolato/completamente vincolato/conflitto e diagnostica dal solver. Non dedurre lo stato solo dal numero di vincoli.

### 5.7 Inserisci, Formato, Parametri

Inserisci immagine, punti e ACAD devono usare un selettore di risorse sul Quest e produrre gli oggetti nativi previsti, con unità, posizionamento, scala, orientamento e opzioni di collegamento/importazione effettivamente disponibili. Il comando punti include le opzioni di connessione/curve offerte dall'installazione. Il server risolve asset mediante id in un'area di importazione: formato, dimensione e dipendenze vengono validati prima della preview; nessun path del Quest passa per un path Windows.

Formato: Costruzione, Linea d'asse, Mostra formato, Quota non parametrica, Punto centrale, proprietà colore/tipo/spessore nelle opzioni native. Separare la funzione CAD dallo stile XR che evidenzia selezioni o errori. Parametri: leggere e modificare espressioni e unità, aggiungere parametri dove ammesso, preservando dipendenze e aggiornamento; il solo valore numerico attuale non realizza l'intero comando Parametri.

## 6. Architettura proposta

### 6.1 Core XR

Introdurre un editor condiviso, utilizzato da Design e Lamiera:

- `SketchCommandCatalog`: corrispondenza Inventor ↔ XR, famiglia/variante, selezioni, opzioni, capability e stato;
- `SketchEditSession`: schizzo nuovo/esistente, snapshot versionato, comandi di bozza, preview, commit e storico;
- `SketchEntityRef`: documento, schizzo, revisione, id opaco, sottoparte (estremo/centro/curva), tipo;
- DTO di entità native per arco, ellisse/arco ellittico, spline, punti, testo, immagini, costruzione, quote, vincoli, proiezioni, serie e blocchi;
- `SketchCommandState` per i passi di input; `PaletteLayout` per dimensioni/capacità.

Nomi proposti, da fissare nel piano esecutivo. Gli id posizionali attuali restano solo per compatibilità delle API esistenti; l'editor nuovo non li conserva fra revisioni. Le nuove entità della bozza usano id temporanei risolti dal backend nel batch. Una divisione restituisce esplicitamente riferimenti vecchi invalidati e nuovi id.

Il rendering usa curve native/tessellazione dedicata e hit test per tipo. Una spline può essere tessellata a schermo ma resta una spline nel CAD. Uno schizzo con entità non supportate deve essere dichiarato parziale: non nascondere silenziosamente entità e poi salvare una ricostruzione incompleta.

### 6.2 Bridge e contratti

Estendere contratti API-agnostici, handler STA, registrazione, `ToolContracts`, `CadBatchCommandCatalog` e snapshot. Riusare i comandi esistenti solo quando semantica e opzioni corrispondono. I nomi di nuove operazioni (ad esempio `trim_sketch_entities`, `pattern_sketch_entities`, `edit_sketch`) sono **proposte**, non tool già disponibili.

Ogni mutazione passa per piano/anteprima/transazione: `document_id`, revisione, schizzo e selezioni obbligatori; preview eseguita e abortita, risultato nativo catturato prima del rollback; Applica vincolato al piano effettivamente visualizzato. Tutte le lunghezze mm e angoli gradi al confine, cm/radianti solo nel bridge Inventor.

Capability/versioni pubblicate per variante, contesto, tier e opzioni. Un comando assente/sperimentale disabilitato non diventa un pulsante attivo che fallisce al click. Il tier dichiarato nel contratto e l'evidenza live vanno registrati separatamente: alcune capacità XR collaudate sono ancora marcate experimental nel codice. Nuove capacità restano experimental fino alle prove richieste dal repository.

**Limite batch:** mantenere 32 operazioni; abbassare una serie o un'importazione a una singola operazione semantica atomica con budget interni dichiarati. Il nuovo editor applica una transazione per comando concluso e mantiene aperto lo schizzo, così non ricrea tutte le entità precedenti a ogni click. Non frammentare un comando in commit parziali per superare il limite. Conteggi massimi entità/istanze/file e timeout devono essere fissati e pubblicati dopo il benchmark F0.

Undo/Redo resta associato alle transazioni XR di proprietà della sessione: interruzione da desktop invalida la catena e richiede refresh. Errori strutturati nei codici/details; timeout o esito incerto non autorizzano replay automatico. Visibilità dei simboli, highlight e camera sono stato di vista; costruzione, quote driven, importazioni e proprietà native sono stato documentale e richiedono il relativo commit.

### 6.3 Unity e regressioni

File attuali coinvolti: `PaletteView`, `UiShell`, `UiFactory`, `DesignWorkspace`, `DesignActions`, provider Lamiera, `DesignGeometryView`, `SketchSheetView`, `SketchDimensionLabel`, `ActionCatalog`, `ContextTabs`, `SketchDraft`, `DesignGeometry`, `DesignOperations`, `DesignBackend`, catalogo e handler del bridge.

UI generata dal catalogo, nessuna modifica manuale a `Scenes/Main.unity`. Riutilizzare tema M8 e pipeline icone M10; mapping per variante e distinzione 2D/3D esplicita. Riusare lo stesso input/eventi semantici del runner. Ottimizzare rebuild tramite pooling/diff dopo aver misurato: evitare di ricostruire 16 bottoni ogni frame.

## 7. Verifica di equivalenza e gate

Per ciascuna riga dell'inventario e ciascuna variante/opzione:

1. Generare due fixture equivalenti: comando nativo desktop e comando XR.
2. Confrontare tipi/geometrie/topologia/quote/vincoli/dipendenze e salute.
3. Modificare un parametro o la geometria sorgente e confrontare l'aggiornamento.
4. Verificare preview senza revisione finale, Applica, Undo/Redo e annullamento.
5. Verificare invalidi, conflitti, stale, offline, cambio documento, tracking e timeout.

Tolleranze iniziali proposte: 0,001 mm per punti/lunghezze e 0,001° per angoli; la tassellazione a schermo non è l'oracolo CAD. Confermare tali soglie sulle fixture native prima di congelarle. Per testo/immagini/formato verificare anche dati nativi, riferimenti e proprietà. Per curve/proiezioni/serie verificare associatività con una modifica successiva.

| Gate | Criterio di accettazione |
|---|---|
| S1-01 | Baseline dei 77 comandi riconciliata con contesti attivi, menu/opzioni/default e personalizzazioni; nessuna riga senza destinazione |
| S1-02 | Tavolozza 320 × 220 mm predefinita; tutte le dimensioni offerte senza clipping, overlap, perdita di comandi o hit target illeggibili |
| S1-03 | Gruppi e varianti raggiungibili; breadcrumb/Torna/picker e voce coerenti, nessun input UI propagato al foglio |
| S1-04 | Editor di schizzo nuovo/esistente condiviso parte/lamiera; riferimenti opachi versionati, geometrie non supportate dichiarate |
| S1-05 | Tutte le geometrie/varianti Crea nel perimetro 2D con oggetti nativi, editing e vincoli equivalenti |
| S1-06 | Raccordo/Smusso 2D, testo e testo su geometria equivalenti; nessuna confusione con feature 3D |
| S1-07 | Proiezioni e dipendenze equivalenti, anche dopo modifica della sorgente |
| S1-08 | Serie rettangolare/circolare e specchio modificabili e associativi secondo l'oracolo desktop |
| S1-09 | Nove comandi Modifica con topologia/vincoli corretti; Taglia/Estendi/Dividi verificati per intersezioni e casi limite |
| S1-10 | Quota generica/automatica, driven/espressioni e modifica/eliminazione quote equivalenti |
| S1-11 | Dodici vincoli, opzioni e visualizzazione; cardinalità corretta, salute e diagnostica native |
| S1-12 | Importazioni, Formato e Parametri coperti con tutte le opzioni nel perimetro; accessibili dal Quest |
| S1-13 | Concludi/Annulla/Torna alla lavorazione, sessione e storico rispettano esito CAD e differenza Anteprima/Applica dichiarata |
| S1-14 | Offline/stale/tracking/timeout/cambio documento e conflitti non producono commit impliciti, duplicazioni o perdita di bozza |
| S1-15 | Regressioni M6/M8/M9/M10 e contesti non-Schizzo; nessun aumento non spiegato di allocazioni/rebuild o perdita di fluidità |
| S1-16 | Runner sul Quest con fixture Inventor dedicata per ogni sottocaso deterministico, ripristino APK/documento e manifest/log/screenshot |
| S1-17 | Prova fisica MR e Studio VR: leggibilità, ergonomia mano sinistra, occlusioni, tracking e gesti reali accettati dall'utente |
| S1-18 | Estensioni contestuali censite (Scheletro/3D/curva ponte) implementate e collaudate oppure perimetro finale ridotto esplicitamente dall'utente |

Tre esiti separati per ogni gate: **unitario/FakeAddIn**, **Quest sintetico + Inventor reale**, **prova fisica**. Nella presente consegna sono tutti da eseguire; la lettura della ribbon chiude soltanto il censimento iniziale, non S1-01 completo.

Obiettivo prestazioni iniziale: nessun peggioramento oltre il 10% del p95 CPU rispetto a baseline equivalente a 8 azioni; misurare anche frame time complessivo, GC e GPU a frequenza dichiarata. A 72 Hz il budget frame è 13,89 ms. Un contatore GPU assente resta NOT COVERED, come il gate aperto M10. Soglie e fixture vanno confermate in F0, non certificate su PC.

## 8. Consegna e decisioni

Consegna finale: tavolozza ampliata, catalogo riconciliato, editor e bridge, APK ordinario e QA, sonda nativa riproducibile, runner Quest, verbale con evidenze separate, documentazione di prodotto aggiornata. I comandi incompleti restano visibili con motivo di indisponibilità durante lo sviluppo e non contano come parità consegnata.

Decisioni proposte già concrete: 320 × 220 mm, 16 azioni per pagina, famiglie con varianti, parte/lamiera condivise, risultato nativo equivalente, Anteprima/Applica conservato, 32 operazioni batch conservate. Da determinare con prove tecniche: API 2027 per modifiche topologiche/serie/testo, opzioni non visibili nello screenshot, limiti di scala e dati. Da determinare con prova fisica: posa e limite ergonomico della tavolozza. Questi punti non impediscono di iniziare F0 e F1.
