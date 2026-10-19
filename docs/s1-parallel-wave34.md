# S1 — Seconda ondata parallela: intervalli, etichette, risorse

11 ottobre 2026. Incremento dei pacchetti C02, D04 e F03 del
[piano parallelo](superpowers/plans/2026-10-11-s1-pacchetti-paralleli.md).
Il perimetro resta quello della specifica S1: 77 comandi, Parte/Lamiera e
estensioni contestuali. Nessuno dei 18 gate complessivi viene chiuso.

## C02-B — Partizione dei tratti

`NativeSketchCurveIntervals` usa le intersezioni C02-A per dividere segmenti,
archi firmati e cerchi in intervalli parametrici. Taglia individua il tratto
contenente il punto e distingue tratti rimossi/conservati; Dividi partiziona
una curva aperta. Input in mm, validazione sulla curva finita, tagli ordinati
e deduplicati, intervalli circolari che attraversano la seam.

La revisione ha corretto una fusione eccessiva vicino alla seam: due tagli
ai lati di zero vengono uniti soltanto se la distanza ciclica è entro la
tolleranza. La regressione conserva due tagli distanti 1,5 volte la tolleranza.
I 35 nuovi casi Core coprono anche estremi, sovrapposizioni, coincidenze,
tangenze, trasformazioni, scala e punti fuori supporto finito.

`ReadyGeometry` certifica soltanto la partizione matematica. Tangenze,
Taglia con meno di due tagli interni e divisione singola del cerchio hanno
stato esplicito `RequiresOracle`; un hit sul confine è ambiguo. Non sono
ancora comandi CAD: ricostruzione, vincoli, dipendenze e riferimenti dopo
mutazione richiedono strategia nativa e confronto desktop. Ellissi/spline
ed Estendi restano nel pacchetto C02, senza riduzione del requisito.

## D04-B1 — Etichetta planare misurabile

`NativeSketchConstraintLabelView` espone un rettangolo sul piano dello
schizzo, con dimensioni fisiche compensate rispetto alla scala del modello,
footprint effettivo in coordinate CAD e hit-test esplicito del Ray. Testo
completo separato dalla rappresentazione con ellissi, padding e maschera;
nessun collider o target UI intercetta l'input del foglio. Il rettangolo
predefinito è 48 × 12 mm fisici, padding 2 mm e maiuscole 7 mm: è una scelta
di vista XR, non una misura dei simboli desktop.

I casi EditMode dedicati coprono scala uniforme/non uniforme,
rotazioni, parent distinto, piano indipendente dalla testa, hit/miss/raggi
dietro o paralleli, aggiornamento senza ricreazione, testo lungo, input
invalidi, cleanup e deformazione del parent rifiutata con recupero.
Il primo run (994 PASS, cinque FAIL su 999) ha rilevato che normalizzare
direttamente il prodotto vettoriale degli assi già convertiti in metri
restituiva un vettore nullo sotto la soglia interna di Unity. Il calcolo
normalizza ora gli assi tramite divisione esplicita prima del prodotto;
la regressione a scala ridotta esercita questo caso senza cambiare le
tolleranze. Il diagnostico iniziale viene conservato separatamente.

Il renderer centrale e l'input Parte/Lamiera non usano ancora questo modulo.
Il layout Core D04-A deve ricevere il footprint misurato invece del suo
default 8 × 4 mm. Restano pooling, selezione tramite Ray del prodotto,
filtri, diagnostica delle omissioni, occlusioni, oracle dei simboli e Quest.

## F03-C1 — Deposito importazioni in memoria

`ImportResourceStore` separa i byte deduplicati SHA-256 dai record specifici
del proprietario autenticato, passato dal chiamante. Policy esplicita per
byte/record/metadati/formati/TTL; scadenza per proprietario, LRU e acquisizione
atomica di più risorse. La lease congela i metadati e protegge i byte dalla
rimozione fino al rilascio, anche dopo la scadenza del catalogo. Una risorsa
scaduta non è acquisibile da una nuova lease. Copie difensive e controlli
d'integrità impediscono di esporre buffer interni.

La revisione ha rilevato che `JObject.DeepClone` conserva il riferimento CLR
di un `JValue(byte[])`. Ora il deposito serializza/parsa il JSON e applica
il budget UTF-8 alla rappresentazione conservata; byte array nei metadati
diventano stringhe base64. Due regressioni verificano isolamento e budget.
I 21 nuovi casi bridge comprendono anche owner, scadenza, eviction atomica,
lease simultanee, corruzione intenzionale e concorrenza.

Il deposito non autentica da solo, non è registrato in un endpoint e non
esegue importazioni CAD. Non è persistente e non garantisce la durata di
file collegati a un documento salvato. Upload/selettore Quest, validazione
effettiva dei formati, staging, percorsi fidati, preview/commit e conservazione
documentale delle dipendenze restano necessari per F03.

## Verifiche ed evidenze

Core: **941 PASS** (`core-parallel34.trx`), 35 nuovi casi rispetto a 906.
Bridge: **1.268 PASS** (`bridge-parallel34-final.trx`), 21 nuovi rispetto a
1.247. Il primo run bridge, prima della correzione dei metadati, passa 1.266;
il suo log rimane come evidenza distinta. Unity: **1.000 PASS**, zero falliti
o saltati (`unity-parallel34-final.xml`, processo terminato con codice 0),
13 nuovi casi rispetto a 987. I 69 nuovi casi delle tre suite sono prove
unitarie/FakeAddIn/EditMode; non certificano il comportamento CAD o Quest.
Il manifest `artifacts/s1-verification/parallel34-manifest.json` conserva
sorgenti, assembly testati, risultati finali e diagnostici iniziali. Gli
archivi parallel32, spline-minimum31 e spline-method33 sono stati nuovamente
verificati, senza variazioni di dimensione o hash.

La fixture temporanea `S1_ArcInput_Oracle.ipt`, rimasta dalla preparazione
del confronto spline, è stata chiusa senza salvare con la guardia dedicata;
`parallel34-fixture-cleanup.json` riporta zero documenti. Questo cleanup non
costituisce una nuova prova desktop di chiusura spline o degli altri comandi.
Nessun APK nuovo, installazione Quest, distribuzione DLL o commit Git.

## Prossimi agganci di prodotto

1. D04-B2/B3: layer di etichette riutilizzate per identità, layout alimentato
   dal footprint reale, selezione con Ray e associazioni validate; integrazione
   nel renderer e nel percorso condiviso Parte/Lamiera. Dopo, filtri e stato
   di omissione esplicito, evitando di perdere la selezione nel cambio vista.
2. C02: oracle desktop dei casi temporaneamente `RequiresOracle` e verifica
   della strategia di mutazione nativa con trasferimento delle relazioni.
   La preview deve evidenziare proprio il tratto pianificato e restituire
   riferimenti invalidati/nuovi, prima di collegare Taglia/Dividi al client.
3. F03: trasporto autenticato e selettore risorse, con lease del bundle
   congelato dal piano e staging fidato. I file collegati richiedono una
   gestione persistente distinta dal deposito volatile appena verificato.

Il coordinatore mantiene l'esclusiva sui file centrali e sulle prove CAD,
Unity e Quest; i moduli e i test dedicati possono continuare in parallelo
quando non è attiva una build Unity sul progetto condiviso.
