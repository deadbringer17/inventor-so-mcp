# S1 — Schizzo XR: piano per colmare il gap con Inventor 2027

Data: 6 ottobre 2026. Stato: **implementazione in corso, parziale**.
Identificativo dell'implementazione: **S1**.
Spec obbligatoria: [design e gate S1](../specs/2026-10-06-schizzo-parita-inventor-2027-design.md).
Baseline: [77 comandi censiti](../../xr-sketch-2027-gap-inventory.md).

Le fasi sono sequenziali per dipendenze. Ogni fase consegna software integrato e propri test; la parità completa è dichiarabile solo alla fine, dopo collaudo. Questo piano non costituisce un'implementazione già eseguita.

## F0 — Congelare la baseline e provare le API

- [ ] Registrare commit/diff del checkout, build installata Inventor, APK, configurazione host e capacità esposte.
- [ ] Ripetere il censimento sulla fixture di parte in modifica schizzo, parte fuori schizzo e lamiera. Enumerare menu overflow, pannelli nascosti e opzioni dei dialoghi, comprese quelle di Formato/Parametri.
- [ ] Aggiungere a ciascun comando dell'inventario contesto, ordine delle selezioni, tipi, default/opzioni, esito e collegamento alla fixture oracle.
- [ ] Creare fixture native dedicate per archi/spline, testo, curve vincolate, proiezioni, serie, importazioni e formato; conservare lo stato del documento precedente.
- [ ] Sonde sul 2027 per Taglia/Estendi/Dividi, serie native modificabili, levigato, testo e importazione; verificare firme interop e rollback prima di scegliere la strategia.
- [ ] Benchmark di baseline UI/CAD; fissare tolleranze, limiti entità/istanze/file, timeout e budget CPU/GPU.

File: inventario e verbale nuovo `docs/xr-sketch-2027-verification.md`; sonde sotto `bridge/tests/` e wrapper `scripts/` secondo le convenzioni esistenti. Nuove capacità nel tier experimental.

Uscita: S1-01 riconciliato per il perimetro 2D, matrice opzioni e decisione documentata sulle API. Il solo censimento read-only già salvato è il punto di partenza.

## F1 — Tavolozza ampliata e gruppi

Avanzamento 11 ottobre: collegato il fallback condiviso per tracking sinistro
perso al bordo del banco (frame e altezza calibrata), con ritorno alla posa
della mano e conservazione di input/gruppi. Prima di Ricentra il frame viene
congelato, senza seguire la testa; il fissaggio esplicito prevale sul fallback.
La perdita di uno dei controller annulla il click UI pendente senza Click/Drop
e chiude la cattura CAD prima del rilascio. Il Trigger tenuto al recupero non
avvia un altro gesto; serve un rilascio intenzionale. Il polling semantico
avviene dopo il raycast UI per evitare propagazione al foglio. Il ritorno alla
mano è differito fino al rilascio se è in corso un click/gesto. Verificati
780 test Unity EditMode, inclusi quattro angoli sopra il banco nei tre formati;
APK ordinario compilato, conservato come `InventorXrSo-s1-palette-tracking.apk`,
non installato. Manifest `palette-tracking31-manifest.json` con sorgenti e
assembly eseguiti, test, log e hash APK. Runner Quest, prestazioni ed
ergonomia restano aperti. Nessun gate complessivo S1 viene chiuso.

- [x] `PaletteLayout` con dimensioni/profili/capacità; default 320 × 220, 4 × 4. Layout a dimensioni min/max senza ridurre testo e hit area.
- [x] Integrare `PaletteView` e `UiShell`: gruppi cliccabili, breadcrumb, varianti, regolazione posa/dimensione e ancoraggio mano/banco. Integrazione software verificata in EditMode; runner e prova fisica nelle attività successive.
- [x] Separare capacità della tavolozza, capacità dei picker e limite dell'anello in `ActionCatalog`; aggiornare i provider che dividono liste con `MaxPalette`.
- [ ] `ContextTabs`: navigazione Schizzo coerente in parte/lamiera e mantenimento M9 per Assieme/Vista/Documento.
- [ ] Conservare keypad, focus, tooltip M10, disabled reason e mappa controller; prevenire click simultanei UI/foglio.
- [ ] Test EditMode geometrici su layout/hit target, picker lunghi, Back e cambio contesto; runner sintetico UI sulla fixture dedicata.

Uscita: S1-02/03 e regressioni S1-15; geometria della UI verificata automaticamente, ergonomia S1-17 ancora aperta. I comandi futuri hanno stato esplicito indisponibile, non callback vuote.

## F2 — Editor nativo condiviso, selezione e sessione

Avanzamento 11 ottobre: selezione a finestra locale condivisa Parte/Lamiera,
con Trigger tenuto e applicazione della selezione al rilascio. Verso destra
contiene, verso sinistra attraversa; sostituzione/aggiunta/inversione, filtri
tipo e sottoparte, limite atomico di 2.000 riferimenti. Cerchi analitici e
segmenti nativi per le altre curve. Il contorno non ricostruisce le geometrie
durante il trascinamento; annullamento su X, tracking, offline, cambio revisione,
chiusura, manipolazione a due mani o rilascio sulla UI. Nessun commit implicito.
La regione segue gli assi dello schizzo; oracle desktop delle curve ai bordi,
prospettiva, quote/vincoli/testo/immagini e collaudo Quest restano aperti.
Verificati 741 test core e 797 Unity EditMode, inclusi input sintetici nei
due workspace, annullamenti e colori del renderer. Il primo controllo dei
colori richiedeva erroneamente uguaglianza esatta dopo il passaggio al
renderer; verifica finale con tolleranza colore `1e-5`, senza modificare
le tolleranze CAD. Il manifest `window-selection31-manifest.json` conserva
i risultati finali e il diagnostico precedente.
Nessuna attività complessiva F2 o gate S1 è chiuso.

- [ ] DTO versionati per entità/sottoparti/quote/vincoli, snapshot completo e selezione multipla.
- [ ] Riferimenti opachi nel bridge e id temporanei per la bozza; mapping dopo commit/split/delete, invalidazione dopo cambio revisione.
- [ ] Editor schizzo esistente senza ricreare entità o perdere feature dipendenti.
- [ ] Sessione persistente, transazione per comando, storico XR, Concludi/Annulla e compatibilità con Anteprima/Applica.
- [ ] Editor condiviso da Design/Lamiera; stato parziale esplicito per dati troncati/non supportati.
- [ ] Test di identità, preview/rollback, conflitti desktop, stale, esito incerto, tracking e cap 32 operazioni.

File: core `Backend/`, `Ui/`, `Selection/`; Design/Lamiera e renderer Unity; contratti/snapshot e handler bridge. Leggere spec/piani M6 e M9 prima di cambiare input o lifecycle.

Uscita: S1-04/13/14 su schizzo base e fixture native; nessun risultato FakeAddIn usato per certificare Inventor.

## F3 — Crea e varianti

Avanzamento 11 ottobre: Linea punto medio collegata all'API nativa 2027
`AddByMidEndPoints`, con coordinate o riferimenti selezionati per il centro,
l'estremo o entrambi. UI condivisa Parte/Lamiera, campi numerici soltanto per
i punti mancanti, vincolo Punto medio nativo e riutilizzo dell'estremo.
Ruoli conservati nella bozza e dopo riselezione; nessun commit implicito.
Passano 746 test core, 1.247 bridge e 807 Unity. La sonda dedicata passa
24 casi con tre famiglie di punti sorgente e quattro modalità su Parte/Lamiera,
48 aggiornamenti sorgente, confronto API, rollback e guardie. Ripristino
confermato anche da lettura indipendente. Manifest `midpoint31-manifest.json`.
Linea continua/tangente, altre varianti, oracle desktop e Quest restano aperti;
nessuna attività complessiva F3 o gate S1 è chiuso.

Inserimento punti, 11 ottobre: i punti X/Y delle 19 creazioni si raccolgono
anche tramite Trigger sul foglio nativo, con tastierino alternativo e cursore
condiviso Parte/Lamiera. Linea punto medio raccoglie solo i punti non già
referenziati. Un click sostituisce la coppia X/Y corrente. Il cerchio centro/raggio
accetta un secondo punto sulla circonferenza, con raggio numerico alternativo.
Quattro nuovi casi Parte/Lamiera verificano anche rifiuti e callback chiusi:
855 PASS Unity (`unity-circle-pen-final.xml`); manifest `circle-pen31-manifest.json`.
L'ellisse raccoglie centro, primo semiasse e punto sulla curva tramite penna;
il secondo semiasse segue la formula verificata nel desktop, non la sola
distanza dal primo asse. Conservato l'ordine dei semiassi, anche secondo > primo:
rimossa la guardia non equivalente del bridge dopo prova API/desktop.
867 test Unity, 1.247 bridge; 18 casi API contro preview/commit Parte/Lamiera,
3 campioni desktop con screenshot e readback (`ellipse-pen31-manifest.json`).
Inferenze automatiche, anteprima dinamica, editing e collaudo Quest aperti.
Arco dal centro: centro/punto iniziale/estremo tramite penna, raggio iniziale
conservato e ampiezza firmata accumulata dal movimento anche oltre 180°.
Guida locale persistente, distinta dall'Anteprima CAD; alternativa numerica
e input misto, sospensione sopra UI/al centro/perdita tracking e rifiuti
di arco nullo/giro completo. Passano 758 test Core e 887 Unity; quattro
campioni desktop Parte/Lamiera e sedici casi API contro batch verificano
±90°/±270°, inizio 0°/170°, geometrie/vincoli e rollback esatto.
Fixture chiuse e zero documenti confermati da lettura indipendente;
manifest `arc-pen31-manifest.json`, APK compilato senza installazione.
Giri completi, inferenze, editing, runner Quest e prova fisica ancora aperti;
nessun gate complessivo S1 chiuso.
Asole lineari: terzo punto per la larghezza in Punti d'interasse, Punti
estremi e Punto centrale; tastierino alternativo. Calcolo dal segmento dei
centri (riflesso per Punto centrale) oppure dalla distanza perpendicolare
per Punti estremi, secondo tre campioni del desktop installato.
Ventiquattro confronti API/batch Parte/Lamiera passati, inclusi geometria,
vincoli, rollback esatto e commit; 772 test Core e 911 test Unity passati.
Sorgenti, assembly, risultati e APK compilato senza installazione sono
identificati da `slot-pen31-manifest.json` e dal verbale aggiornato.
Inferenze, anteprima dinamica, asole ad arco, editing e Quest restano aperti.
Poligono: limite S1 portato da 64 a 120 lati dopo creazione desktop di 120
lati e rifiuto di 121 senza variazioni. Ventiquattro confronti API/batch
Parte/Lamiera (3/65/120 lati, inscritto/circoscritto, due orientamenti) e
otto rifiuti passati. Corretto lo snapshot dei vincoli `PatternConstraint`
senza definizione rettangolare/circolare esposta: associazioni native lette
dalle entità, definizione esplicitamente indisponibile, nessuna serie inventata.
923 test Unity e 1.247 backend passati; Core invariato rispetto ai 772 test
dell'incremento asole. Editing, inferenze e prova Quest restano aperti.
Sedici regressioni native delle serie passate; zero documenti/fixture finali.
`polygon-12031-manifest.json` identifica codice, assembly ed evidenze; APK
ordinario compilato e conservato senza installazione.
Asole ad arco: raccolta con penna di ampiezza e larghezza per centro/inizio,
e del quarto punto per la variante a tre punti, con tastierino alternativo.
Due campioni desktop confermano inizio/fine/passaggio e distanza dal tratto
finito. Corretto il riordino API StartPoint/MidPoint/EndPoint: il controllo
geometrico indipendente rilevava 315° invece dei 90° richiesti.
Sessantaquattro confronti finali API/batch Parte/Lamiera passati, inclusi
arco di costruzione, vincoli, snapshot completi, rollback esatto e commit.
787 test Core, 1.247 bridge e 951 Unity passati; fixture chiuse e zero
documenti finali confermati. Inferenze, editing, aggiornamenti sorgente,
runner Quest e prova fisica restano aperti; nessuna attività complessiva
F3 o gate S1 è chiuso.
Arco a tre punti: corretto anche qui il mapping inizio/fine/passaggio verso
StartPoint/MidPoint/EndPoint dopo verifica desktop; ruoli indicati nella UI,
semicerchio locale predefinito conservato, penna con rifiuto dei punti degeneri.
Trentadue casi API/batch Parte/Lamiera passati per ±45°/±90°/±180°/±270°,
con centro/raggio/ampiezza/estremi indipendenti, vincoli, snapshot completi,
rollback esatto e commit. 955 test Unity e 1.247 bridge passati; Core invariato
rispetto ai 787 precedenti. Fixture chiuse e zero documenti confermati.
Inferenze, editing, sorgenti e Quest restano aperti; nessun gate S1 chiuso.
Nel primo incremento dei punti, raggi, angoli, larghezze e lati restavano
numerici; le aggiunte successive sono descritte sopra. Quel primo incremento non dichiarava
completi i gesti geometrici delle rispettive famiglie. Nessuna preview o
mutazione implicita. Tracking perso conserva i punti già accettati e richiede
rilascio prima di un'altra pressione; X/revisione revocano l'input pendente.
Verificati 851 test Unity EditMode con input sintetico e tavolozza collegata;
manifest `native-point-input31-manifest.json`. La prima prova con tavolozza
rilevava due sequenze di test incomplete sul poligono: la scelta esplicita
inscritto/circoscritto precede correttamente l'acquisizione dei punti.
Runner Quest, oracle dell'input e prova fisica restano aperti.

- [ ] Collegare arco/punto disponibili nel batch e completare linea continua/punto medio, cerchio tangente, archi e rettangoli in tutte le varianti.
- [ ] Completare ellisse, spline interpolante/CV, poligoni e cinque asole; aggiungere editing nativo e renderer.
- [ ] Raccordo/Smusso 2D con id distinti da `design.fillet`/`design.chamfer`.
- [ ] Testo/testo su geometria, tastiera/espressioni XR e curva da equazione.
- [ ] Pack icone tramite pipeline M10: mapping variante, provenienza e fallback verificati.
- [ ] Confrontare con oracle desktop geometria, vincoli e aggiornamento successivo; runner per ciascuna variante.

Uscita: S1-05/06 per tutte le varianti nel perimetro 2D. Spline visiva tessellata deve mantenere la spline nativa.

Avanzamento 11 ottobre, spline: raccolta continua 3–64 punti nell'editor
Parte/Lamiera, conclusione esplicita, rimozione ultimo punto, penna/X/Y,
annullamento e invalidazione revisione. Passano 967 test Unity e 1.247 bridge;
Core invariato (787 precedenti), APK compilata non installata. La sonda
nativa passa otto casi interpolanti su Parte (3/4/5/64 punti, due rotazioni),
poi rileva `E_UNEXPECTED` nel getter Constraints delle geometrie CV e uno
snapshot parziale. Accesso dinamico/tipizzato non risolve; tentativi rimossi.
Matrice CV/Lamiera, oracle desktop, minimo nativo, opzioni/editing e Quest
restano aperti. Diagnostici e cleanup confermato nel manifest
`spline-points31-manifest.json`; nessun gate S1 chiuso.

Aggiornamento successivo, spline: indice inverso dei vincoli letto solo se
lo schizzo contiene PatternConstraint; evita il getter COM sulle CV quando
non necessario. Passano 32 casi spline Parte/Lamiera (3/4/5/64 punti e due
rotazioni, fit/CV), 24 regressioni poligono e otto rifiuti; bridge 1.247 PASS.
La prova mista spline/poligono passa quattro fit e fallisce al primo CV,
ancora con snapshot parziale: caso misto non risolto. Cleanup confermato,
manifest `spline-constraints31-manifest.json`, nessuna installazione Quest.
Client invariato (967 Unity precedenti); opzioni/editing/oracle desktop e
tutti i gate restano aperti.

Diagnostica successiva del caso misto CV: 16 osservazioni native con/senza
poligono, in/fuori modifica e fissaggio di spline/punto/lato. Il getter
Constraints fallisce anche quando i vincoli globali includono i fissaggi;
fissare la spline crea tre GroundConstraint reali. Ingresso in modifica e
filtri basati solo su OwnedBy non risolvono. Transazioni annullate e zero
documenti confermati. Manifest `spline-access31-manifest.json`; è ricerca
API, non PASS del caso misto né nuova capacità del prodotto.

Snapshot parziali: conserva geometrie valide e separa l'indice dei vincoli
indisponibile; nessun sottoinsieme di associazioni dichiarato completo.
NativePartial nell'anteprima blocca Applica anche dopo rendering, con
rifiuto dei flag nativi assenti. Passano 793 Core, 967 Unity e 1.247 bridge.
Sedici casi nativi Parte/Lamiera: otto fit con preview/commit completi e
otto CV parziali con rifiuto anteprima/rollback esatto, senza commit CV.
Il backend rifiutava già le anteprime parziali. Cleanup confermato e manifest
`spline-partial31-manifest.json`; editing del caso misto e tutti i gate
restano aperti. Nessuna installazione Quest o distribuzione DLL.

## F4 — Quote, vincoli e Formato

Incremento successivo minimo/default spline: quattro campioni desktop
Parte/Lamiera confermano conclusione con due punti per fit e CV. Il client
condiviso e il bridge accettano 2–64 punti; la preferenza nativa viene letta
nello snapshot e congelata nella bozza, senza variazione fra Anteprima e
Applica. Assenza della preferenza disabilita fit, senza disabilitare CV.
Passano 804 Core, 971 Unity e 1.247 bridge; 40 casi finali nativi su due
contesti, cinque conteggi e due orientamenti, più 18 rifiuti con rollback
esatto. Prima sottoscrizione eventi Lamiera fallita prima dei casi, retry
riuscito; diagnostico conservato. Zero documenti finali confermati.
Manifest `spline-minimum31-manifest.json`; opzioni/chiusura/editing, caso
misto CV/poligono e Quest restano aperti. Nessun gate complessivo chiuso.

Ricerca opzioni spline, 11 ottobre: 48 creazioni API Parte/Lamiera con
2/3/5 punti, aperte/chiuse; tutti e tre i metodi interpolanti installati e
chiusura CV tramite ripetizione del primo punto. Creazione, lettura dopo
solver e Abort riusciti; chiusura aggiunge un punto coincidente con il primo.
Manifest `spline-options31-manifest.json`, fixture chiuse e zero documenti
confermati. Non prova il minimo o le opzioni del comando desktop; il minimo
XR era ancora tre punti durante quella ricerca; il successivo incremento minimo/default sopra lo porta a due. Nessun cambiamento del prodotto o nuovo APK in quella sonda: oracle
desktop, geometria completa, editing, dipendenze e Quest ancora da eseguire.

Avanzamento 7 ottobre: dodici vincoli espliciti collegati all'editor condiviso e
alla barra Anteprima/Applica. Sonda nativa su cardinalità, fusione dei punti,
G2 e rollback; opzioni XR per assi delle ellissi e punto vicino. Restano aperti
oracle completo, inferenza, simboli/rimozione, quote e runner Quest: nessuna
attività complessiva F4 o relativo gate è ancora chiusa.

Avanzamento 8 ottobre: tredici varianti della Quota generica nel menu condiviso,
con posizione testo, valori mm/gradi e modalità driving/driven conservati
nella bozza. Sonda nativa: 78 casi su Parte/Lamiera, incluse espressioni e nomi
dei parametri nel bridge, anteprima annullata e unità verificate. Restano
inserimento espressioni/nomi XR, quotatura automatica,
dipendenze, oracle delle opzioni e runner Quest; la checklist F4 resta aperta.
Modifica ed eliminazione quote esistenti sono ora collegate allo stesso editor,
con selezione dall'elenco, valore mm/gradi, posizione e modalità, evidenziazione
e riselezione esplicita dopo refresh. Ventisei scenari nativi verificano cinque
mutazioni per ogni famiglia su Parte/Lamiera, identità conservata, rollback e
rifiuto di riferimenti errati/contraffatti. Posizione della lunghezza arco
confrontata con l'API nativa, che la normalizza; oracle desktop ancora aperto.

Avanzamento 10 ottobre: tastiera XR a pagine per espressioni e nomi, con
cursore e annullamento, condivisa da Parte/Lamiera. Prepara nuove quote
tramite espressione e modifica quelle esistenti senza inviare anteprime o
commit impliciti; cambio revisione revoca l'input pendente. Verificati 723 test
core e 726 Unity EditMode, inclusi Annulla e passaggio fra tastiere senza
riattivare campi cancellati. Dopo due avvii COM falliti prima dei casi CAD,
la sonda delle quote passa ora 78 creazioni e 26 cicli su parte/lamiera,
con 130 mutazioni e 52 aggiornamenti reali dei parametri sorgente. Il cleanup
scarta i parametri temporanei chiudendo senza salvare le fixture dedicate;
il tentativo di cancellarli singolarmente dopo la quota veniva rifiutato
dall'API. Ripristino confermato e manifest `dimensions-cleanup31-manifest.json`.
Dettatura testuale, gestione completa dei parametri, oracle desktop e Quest
restano aperti; nessuna attività complessiva F4 è chiusa.

Rimozione vincoli: elenco nativo Parte/Lamiera, scelta per identità opaca,
bozza `constraint_delete`, anteprima e commit espliciti. Il bridge controlla
tipo, revisione e proprietario reale e rifiuta i vincoli non eliminabili;
refresh richiede riselezione. Core e Unity coprono il percorso con backend
fittizio; la sonda nativa ora comprende cancellazione, rollback esatto e
guardie per ogni vincolo aggiunto eliminabile. Esecuzione CAD, simboli e
associazione visiva alle geometrie non ancora verificati/completati.
Il collaudo mirato Fissaggio/Levigato passa quattro scenari su parte/lamiera
con eliminazione e confronto API diretto. Eliminare G2 può modificare le
spline anche nell'oracle nativo: la sonda confronta il risultato esatto con
l'API, senza presumere forma invariata. Matrice completa ancora in corso;
nessun gate F4 chiuso.
Il run completo successivo passa tutti i 30 scenari e le 28 eliminazioni,
con associazioni, guardie, confronto nativo di anteprima/commit e rollback
esatto. Filtro nullo e ripristino confermato; evidenze nel manifest
`constraints-complete31-manifest.json`. Simboli, tutte le opzioni/inferenza,
oracle desktop e Quest restano aperti.

Associazioni e annotazioni: il reader legge le entità coinvolte nei dodici
tipi di vincolo e collega estremi/centri mediante chiavi native. Snapshot
con stato esplicito dei collegamenti, validazione client e budget; scelta del
vincolo evidenzia gli oggetti associati. Mostra/nascondi delle annotazioni è
stato di vista e mantiene visibile il vincolo selezionato. Preview con
annotazioni senza riferimenti selezionabili. Verificati 726 test core e 732
Unity; le associazioni sono ora verificate nei 30 scenari nativi completi
del 10 ottobre. Sono annotazioni XR, non ancora i simboli nativi completi;
oracle, filtri e collaudi CAD/Quest restano aperti.

- [ ] Quota generica completa e automatica; espressioni/unità, driving/driven, posizione testo, modifica/eliminazione mirata.
- [ ] Dodici vincoli con cardinalità/compatibilità native; opzioni/inferenza, mostra/nascondi e rimozione mirata.
- [ ] Costruzione, Linea d'asse, Punto centrale, Quota non parametrica e proprietà di formato native.
- [ ] Parametri con espressioni/dipendenze; diagnostica e gradi di libertà dal solver.
- [ ] Verificare ogni tipo anche su geometrie importate/esistenti, conflitti e annullamento.

Uscita: S1-10/11 e componente Formato/Parametri di S1-12; i vincoli automatici non contano come azioni esplicite mancanti.

## F5 — Modifica topologica, Serie e Proietta

Avanzamento parziale del 10 ottobre: Sposta, Copia e Ruota sono collegati
alle API native `MoveSketchObjects`/`RotateSketchObjects`, nel tier
sperimentale. Parte e lamiera condividono selezioni intere, tastierino,
opzioni esplicite di copia/rimozione consentita dei vincoli e bozza con
Anteprima/Applica. Dopo refresh occorre riselezionare; i parametri sono
conservati senza esecuzione automatica. La sonda prepara 24 casi
su geometrie libere/vincolate, confronto API diretto e rollback. Due casi
Sposta su parte passano; durante l'oracle diretto di copia Inventor si è
chiuso autonomamente (confermato dall'utente). Il cleanup di quel run non è
verificabile. Copia e Ruota con copia sono temporaneamente rifiutate dal
backend prima della chiamata nativa e non dichiarate nelle capacità; il
client rifiuta anche bozze di rotazione con copia conservate prima del cambio.
Il successivo run conferma il rifiuto, ma rileva una differenza nella
rotazione dei glifi tramite `GetFacetsInfo`: anche Ruota con testo selezionato
è temporaneamente rifiutato, prima di modificare geometrie. L'override
`TextBox.Rotation` e il workaround Autodesk avanti/indietro non risolvono
la fixture. Collaudo, rotazione testo e isolamento della copia restano aperti.
La matrice successiva con guardie passa 16 esecuzioni native su parte/lamiera,
otto scenari copia rifiutati e otto controlli di rifiuto della rotazione testo;
confronta anche geometria anteprima, commit, identità e rollback esatto.
`transforms-native-guarded-complete31.json` conferma il ripristino precedente.
Le immagini nel client
restano parziali. Scala, restante F5 e tutti i gate rimangono aperti.

Allunga ora prepara una mutazione nativa sui punti/sottoparti selezionati
e sulle curve intere, con vettore in mm e `RemoveConstraints` esplicito.
Il backend deduplica i punti condivisi e le selezioni sovrapposte; UI/Core
conservano due estremi distinti della stessa curva, opzioni e ordine dopo
riselezione. Verificati 738 test core e 752 Unity; la sonda prepara 64 casi
con oracle nativo, quote/fissaggio, geometria, identità e rollback esatto.
Il run `stretch-native-live31.json` passa tutti i 64 scenari (16 fixture,
due opzioni vincoli, parte/lamiera), con ripristino precedente confermato.
La documentazione pubblica `PlanarSketch` non espone metodi dedicati Scala
o Allunga: per quest'ultimo la primitiva è `MoveSketchObjects` sui punti
nativi; Scala richiede ancora un algoritmo e opzioni equivalenti verificati.
La finestra/crossing locale è collegata all'editor condiviso; equivalenza
desktop dei bordi, opzioni complete e Quest restano aperti.

Offset per distanza e per punto sono ora collegati alle API native, con
selezioni intere ordinate, inclusione automatica della catena da una sola
curva base, direzione e creazione dei vincoli opzionali. Il default API
conservato crea i vincoli e non include automaticamente la catena.
Anteprima/Applica e riselezione dopo refresh sono condivisi fra parte e
lamiera. La sonda compilata prepara 80 casi per linee, cerchi, spline e
catene manuali/automatiche, entrambi i lati, confronto API e due aggiornamenti
sorgente. La matrice nativa passa tutti gli 80 scenari su parte/lamiera:
72 creazioni con due cambi sorgente reali e otto rifiuti equivalenti all'API
diretta, con rollback esatto e cleanup. La conservazione dei punti spline
usa tolleranza `1e-5 mm`; ID, flag e confronto dell'oracle restano esatti.
Manifest `offsets-live31-manifest.json`; modifica dedicata della
definizione offset e collaudo Quest restano da completare.

Serie rettangolare/circolare: aggiunti gli handler nativi di creazione,
aggiornamento della definizione ed eliminazione, snapshot della definizione
nel vincolo `PatternConstraint` e UI condivisa con Anteprima/Applica.
Sono presenti conteggi/distanze/angoli numerici, una/due direzioni, opzioni
associativa/fitted/direzione/simmetria; il backend accetta anche espressioni
native e soppressioni. L'editing XR espone per ora i parametri numerici.
La sonda compilata prepara 48 scenari su parte/lamiera, confronto API,
soppressione, due aggiornamenti sorgente/definizione e cancellazione.
L'interop installata in `Extensions 2027` era versione 29: build SO e sonda
ora risolvono la `Bin` 2027 dal registro e richiedono versione 31.
Il nuovo avvio COM autorizzato è fallito prima delle fixture; l'istanza poi
aperta normalmente dall'utente permette le prove. Tutti i 48 casi su
parte/lamiera passano, con aggiornamento tramite
parametri nativi, quota radiale sorgente, rigenerazione, rollback e cleanup.
La divergenza della creazione associativa simmetrica era nel confronto:
allineati i default Y dell'oracle per serie a una direzione.
Parità completa e collaudo Quest restano aperti; opzioni residue,
blocchi/boundary e revisione completa della definizione rimangono aperti.

Espressioni serie: tastiera XR condivisa per creazione e modifica di
conteggi, distanze X/Y e angoli. Conserva le espressioni native e la bozza
senza anteprime implicite, annulla l'input alla variazione di revisione e
richiede riselezione esplicita dopo refresh. Corretto il riferimento usato
dalla conferma di riselezione di modifica/eliminazione della serie: ora
punta al vincolo nativo selezionato. Passano 759 test Unity, inclusi parte
e lamiera. La sonda aggiunge una matrice separata con parametri sorgente,
due aggiornamenti reali, espressioni errate e rollback: il primo tentativo
diverge solo di circa 1e-13 mm nel testo quota; dopo aver allineato l'ordine
dei solve dell'oracle, 16 casi circolari passano con confronto esatto e
ripristino. Il run completo `patterns-native-expressions-complete31.json`
passa tutti i 48 scenari (24 associativi, 24 statici), con 96 cicli di
aggiornamento sorgente/definizione e ripristino confermato. Il manifest
`pattern-expressions31-manifest.json` conserva sorgenti, assembly eseguiti,
diagnostici, 759 test Unity e APK ordinaria aggiornata. Editor soppressioni,
opzioni residue, oracle desktop e Quest restano aperti; nessun gate chiuso.

Opzioni X/Y e soppressioni: verso/simmetria Y separati da X/asse nella UI,
attivi solo per due direzioni. Lista di indici nativi in creazione/modifica,
annullamento, invalidazione e riselezione; riattivazione esplicita di tutti
gli elementi. Il backend aggiorna `SketchPatternElement.Suppressed`, senza
sostituire la definizione; i parametri numerici/espressioni conservano il
percorso nativo precedente. Passano 739 test core, 1.247 bridge e 773 Unity.
La prima sonda delle opzioni diverge di circa 1e-14 mm nel testo quota:
allineato l'ordine dei solve sorgente/definizione anche per input numerici,
senza cambiare le tolleranze. La sonda di soppressione multipla passa 16
scenari rettangolari a due direzioni su parte/lamiera, statici/associativi,
fitted/non-fitted, con 24 modifiche e ripristino confermato. Una matrice
separata passa tutti i 48 scenari verso/simmetria delle tre famiglie su
parte/lamiera, con 144 modifiche delle soppressioni, 96 cicli sorgente e
48 rifiuti, anche dopo aver modificato un parametro. Associatività e fitted
sono ai default in questo run: il prodotto cartesiano completo delle altre
opzioni resta aperto. Confronti nativi esatti e ripristino confermato;
una lettura indipendente post-run conferma documento attivo vuoto come
prima e zero documenti/fixture aperti. Il controllo di cleanup della sonda
ora verifica esplicitamente anche il caso di documento precedente nullo.
Manifest `pattern-options31-manifest.json`, assembly e sorgenti eseguiti
conservati; APK ordinaria aggiornata compilata, non installata sul Quest.
Modifica completa delle altre opzioni, selezione visiva delle istanze,
ruoli, boundary, blocchi, oracle desktop e Quest rimangono aperti.

Fattibilità Scala, 10 ottobre: sonda dedicata `scale-api`, senza nuova
capacità di produzione. Centoquarantaquattro osservazioni su parte/lamiera
confrontano `MoveTo` e `MoveSketchObjects` sui punti, risoluzione differita
sì/no e fattori 0,5/2. Ottanta corrispondono alla scala uniforme su linee
libere/connesse, cerchi, ellissi e spline CV. Archi, spline interpolanti e
linee fissate/quotate non corrispondono; su otto linee fissate la geometria
risultante non è leggibile. Tutte le prove sono annullate. Stato precedente
e assenza di fixture confermati anche da lettura indipendente; manifest
`scale-api31-manifest.json`. È ricerca sulle primitive API, non collaudo del
comando Scala. La strategia completa e S1-09 restano aperti.

- [ ] Implementare Sposta/Copia/Ruota/Scala/Allunga con opzioni native sui vincoli.
- [ ] Taglia/Estendi/Dividi: strategia verificata in F0, intersezioni, entità risultanti e dipendenze; nessun alias verso semplice movimento di punto.
- [ ] Offset parametrico, Specchio e serie rettangolare/circolare di schizzo con modifica della definizione e dell'originale.
- [ ] Proiezioni associative, spigoli di taglio, lamiera e DWG; aggiornamento dopo cambi sorgente.
- [ ] Operazioni aggregate entro una transazione: serie/importazioni non diventano decine di commit per eludere il cap.
- [ ] Oracle di topologia/relazioni e secondo aggiornamento; runner sul Quest per le selezioni e opzioni.

Uscita: S1-07/08/09, con limitazioni residue registrate per singolo caso. Nessuna dichiarazione di parità se l'effetto è solo visivamente simile.

## F6 — Inserisci e contesti avanzati

- [ ] Selettore asset, trasferimento/scope host, immagini, punti e ACAD con opzioni native, preview e cleanup.
- [ ] Scheletro: Crea blocco/parte/componenti e gestione dei documenti risultanti con relativa preview/lifecycle.
- [ ] Editor Schizzo 3D, proiezione verso 3D e curva ponte nei contesti appropriati; baseline di gesti e opzioni separata.
- [ ] Definire esplicitamente eventuali contesti assieme/IDW ulteriori se richiesti; non estrapolare la baseline `Part`.
- [ ] Runner sulle fixture dedicate, verifica dipendenze/risorse e ripristino.

Uscita: S1-12 completo e S1-18. Il perimetro 2D può essere consegnato come tranche, etichettato chiaramente; «tutte le funzioni della ribbon censita» richiede anche questa estensione oppure una riduzione esplicita concordata.

## F7 — Integrazione, Quest e prova fisica

- [ ] Copertura dell'inventario: per ogni comando/variante/opzione link a implementazione, unit/FakeAddIn, sonda Inventor, runner Quest e prova fisica dove necessaria.
- [ ] Build add-in experimental/stabile secondo il tier; host HTTP, Unity e APK QA/ordinario.
- [ ] Runner deterministico in-app usando gli stessi percorsi input del prodotto; log marcati SYNTHETIC e confronto al backend reale.
- [ ] Fixture dedicata per esecuzione, verifica revisione prima/dopo preview/commit, errori/rollback e ripristino del documento precedente/APK ordinario.
- [ ] Performance A/B equivalente, 100 cambi gruppi/varianti, elenchi lunghi, scene di sketch complesse; CPU/frame/GC/GPU senza dedurre campioni mancanti.
- [ ] Solo dopo i test automatici, prova fisica MR/Studio VR per dimensione/posa/lettura/occlusione, selezioni e gesti non osservabili dal software.
- [ ] Pubblicare verbale con PASS e NOT COVERED per sottocaso; aggiornare prodotto/README/stato senza estendere i PASS di M3/M10 a queste nuove funzioni.

Uscita: S1-15/16/17 e tutti i gate delle fasi precedenti, con evidenza effettiva. Nessun gate ancora eseguito in questa consegna di pianificazione.

## Comandi di validazione previsti

Dalla radice, dopo le modifiche pertinenti:

```powershell
dotnet test bridge/tests/Bimwright.Ipt.Tests
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
dotnet build bridge/src/plugin-so27 -c Debug
dotnet build bridge/src/plugin-so27 -c Debug -p:SoExperimental=true
dotnet build bridge/src/server-http -c Debug
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode"
```

Aggiungere sonda e wrapper Quest specifici dopo averli implementati; nomi definitivi nel verbale. Non installare DLL mentre Inventor le mantiene caricate. La presente consegna verifica la documentazione e l'inventario in lettura: non esegue build/test del prodotto perché il prodotto non è stato modificato.

## Priorità e stima

Prima tranche: F0 → F1, con risultato visibile della tavolozza ampliata. Fondamento funzionale: F2. Parità 2D: F3 → F4 → F5 → importazioni F6. Copertura contestuale completa: restante F6. Collaudo finale F7.

Non assegnare una data di completamento prima delle sonde F0: modifiche topologiche, serie associative e lifecycle multi-documento sono i punti di incertezza maggiori. Ogni fase può essere suddivisa in task con file, criteri S1 e comando di verifica; nessuna implementazione parallela sugli stessi file.
