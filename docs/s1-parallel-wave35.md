# S1 — Vincoli nel prodotto, risorse HTTP e ricerca topologica

11 ottobre 2026. Incremento D04-B2/B3, F03-C2 e ricerca C02 del
[piano parallelo](superpowers/plans/2026-10-11-s1-pacchetti-paralleli.md).
Il perimetro resta 77 comandi, Parte/Lamiera e contesti Scheletro/3D.
Tutti i 18 gate complessivi rimangono aperti.

## D04 — Layer e selezione nel prodotto

Il renderer condiviso usa ora `NativeSketchConstraintLayer`: rettangoli
planari con footprint fisico misurato, pooling per identità, aggiornamento
di layout e linee di richiamo al cambio scala/trasformazione. Il layer ha
budget massimo di 512 etichette, oltre a una vista nascosta per misurazione.
La selezione usa il rettangolo effettivamente visibile e riferimenti della
stessa istanza di snapshot; revisioni precedenti e viste nascoste non
producono hit. In caso di sovrapposizione il percorso UI propone candidati.

Parte e Lamiera instradano la pressione della penna verso le etichette;
UI, prompt di coordinate, selezione a finestra, filtri geometrici e sottoparti
mantengono la precedenza. Il filtro include il tipo vincolo. Mostra/Nascondi
conserva il vincolo selezionato; cambi filtro o revisione revocano candidati.
L'evidenziazione delle geometrie associate usa riferimenti validati dal
layout, senza inventare associazioni quando il reader non le fornisce.

Passano 24 nuovi casi EditMode: 11 layer, 10 input nei due workspace e
tre renderer. Coprono pooling, scala, budget, leader, cleanup, UI, filtri,
prompt/finestra e refresh della revisione prima della preview di eliminazione.
Il primo run ha 1.006 PASS/7 FAIL su 1.013: mancava l'aggancio all'input
sintetico nei nuovi test. Un GUID di 33 caratteri faceva inoltre ignorare
gli 11 test layer. Dopo la correzione del GUID è emerso un nome Assert non
supportato dalla versione NUnit del progetto, corretto in `IsNotEmpty`.
Diagnostici conservati; run finale `unity-constraint35-r2.xml`: **1.024 PASS**,
zero falliti/saltati, processo terminato con codice 0.

Restano filtri UI per tipo nativo, diagnostica visibile delle omissioni,
occlusioni, confronto con simboli desktop, runner Quest e leggibilità fisica.
Le etichette testuali non certificano ancora equivalenza dei glyph Inventor.

## F03 — Trasporto autenticato delle risorse

Il server HTTP registra le rotte `/imports/resources` solo con policy
esplicita e inspector fornito dall'host. Upload binario, elenco paginato,
descrittore e contenuto usano autenticazione e rate limit comuni. Il
proprietario deriva dall'identità autenticata, mai da query o session header.
Dimensione dichiarata e corpo chunked rispettano i limiti; il deposito
riceve solo contenuto completo e metadati ispezionati. Copie separate e
controlli impediscono a un inspector di modificare i byte registrati.
La configurazione rifiuta formati HTTP non ammessi dalla policy del deposito.

La risposta dichiara deposito in memoria e `cad_import_available:false`.
Dipendenze esterne non risolte sono rifiutate esplicitamente. I 17 nuovi
test esercitano host loopback reale con inspector finto, bearer/owner,
limiti, cancellazione, hash, scadenza, capacità e policy assente/incompatibile.
Suite `bridge-import35.trx`: **1.285 PASS**, zero falliti/saltati, codice 0.
Non è una prova di parsing dei formati reali o importazione CAD. Restano
inspector effettivi, selettore Quest, bundle, staging, preview/commit e
conservazione persistente delle risorse collegate ai documenti.

## C02 — Ricostruzione Add/Delete su Inventor reale

La sonda `arc-ui-topology-reconstruct` esegue 30 osservazioni per contesto:
12 linee, 16 archi e due conversioni cerchio/arco. Fixture unica temporanea
non salvata; ogni caso usa una transazione poi Abort e verifica il ripristino
esatto di geometria, chiavi, conteggi e stato solver della baseline vuota.

Parte e Lamiera producono lo stesso esito: **12 controlli di primitive
completati e 18 divergenze per contesto**, con 30 rollback esatti ciascuno.
Le linee riusano gli estremi, anche con vicini connessi, ma i vincoli H/V
della sorgente non vengono trasferiti. Gli archi conservano le chiavi degli
estremi esterni; i centri dei due pezzi sono invece nuovi e distinti già
prima della cancellazione della sorgente. Anche cerchio/arco cambia centro.
Il solo conteggio di vincoli concentrici non dimostra il legame associativo:
serve leggere e verificare le entità coinvolte e la propagazione delle modifiche.

Le ampiezze native sono positive; la sonda verifica l'ampiezza assoluta e
separatamente estremi e punto medio del tratto richiesto, anche per input
negativi. Il PASS esterno indica raccolta conclusa con ripristino, non 30
primitive riuscite. Queste sono prove API Add/Delete: nessun confronto
desktop Taglia/Dividi, comando batch di prodotto o PASS Quest.
Cleanup dedicato Parte/Lamiera completato; `topology35-final-state.json`
conferma zero documenti aperti. Nessuna distribuzione DLL.

## Evidenze e prossimi passi

Core resta ai **941 PASS** dell'incremento 34, senza nuove modifiche Core.
Sorgenti e assembly testati/eseguiti sono congelati in `increment35/` prima
della build APK; risultati e diagnostici sono distinti. Nessuna installazione
Quest o commit Git. APK cumulativa `InventorXrSo-s1-constraint35.apk`
compilata con successo (marker `Build Finished, Result: Success.`, codice 0):
53.941.290 byte, SHA-256
`FDB51061B71E9C6950085EF2EDD8218A24FA2C301987A4B29E77D7A938CAC957`.
Non installata. File, log e hash sono inclusi nel manifest dell'incremento.

Prossimo lavoro C02: grafo esplicito delle relazioni native, strategia per
centri e dipendenze, replay verificato e lineage dopo mutazione, poi oracle
desktop prima dell'aggancio ai comandi. D04 e F03 proseguono sui residui sopra;
nessun modulo parziale chiude da solo un gate S1.
