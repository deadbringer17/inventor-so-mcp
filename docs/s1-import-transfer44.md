# S1 — Trasferimento immagini e binding al batch

13 ottobre 2026, incremento 44, F03/S1-12/14. Perimetro integrale invariato:
77 comandi, 18 gate aperti. Inserimento CAD ancora disabilitato.

Il server trasferisce ora le risorse acquisite dai due ingressi al deposito
add-in: Begin, chunk di massimo 192 KiB, Seal con SHA256 e dimensione esatta.
Le risposte sono validate, i byte sorgenti temporanei azzerati e il batch
contiene solo descrittori, senza path o sorgente base64 inline. Il limite
generale dell'envelope NDJSON continua a includere autenticazione e UTF-8.

La lease del piano conserva il target della prima preview: endpoint,
credenziali e istanza devono coincidere per ogni chunk e per il successivo
commit. Uno switch o re-pin rifiuta il trasferimento; non segue il nuovo
target. Il commit riparte dai byte congelati, senza consultare il catalogo.
Ogni tentativo usa una sessione opaca distinta e nuovi transfer ID.

Il comando interno `stage_sketch_import` è registrato solo nel build
sperimentale e richiede experimental attivo e scrittura consentita. Non è
un tool MCP o un comando ammesso come step CAD. Opera solo in memoria, con
binding owner/session, tipi/campi rigorosi, limiti prima della decodifica,
deadline e azzeramento del buffer chunk. Non legge documenti Inventor.

Il deposito vive per l'istanza add-in e viene disposto alla disattivazione.
Budget espliciti: 32 MiB per transfer, 64 MiB totali, 32 transfer, chunk
192 KiB, TTL fisso dieci minuti, identità massimo 256 caratteri. Le risorse
scadute sono eliminate allo sweep lazy del deposito; la TTL è il fallback
quando il server non riesce a inviare Discard.

Il loader del batch verifica la corrispondenza esatta fra asset richiesti
e descrittori: nessun extra, duplicato, foreign, scaduto o non sigillato;
hash, dimensione, formato BMP e metadata Image senza dipendenze devono
corrispondere. Acquisisce tutte le lease prima della transazione CAD,
le rende disponibili solo nel contesto del batch e le rilascia anche su
errore parziale o deadline. I numeri JSON usano la cultura wire anche
su desktop italiano. L'handler immagine futuro potrà leggere solo queste
risorse, senza risolvere percorsi provenienti dagli argomenti pubblici.

Il cleanup server prova Discard in ordine inverso con token indipendente
da quello cancellato e budget complessivo di due secondi. Non sostituisce
l'errore o il risultato principale e non ritenta il batch CAD incerto.
Un transfer creato senza risposta osservabile resta soggetto alla TTL.

Verifica serializzata: **153 test mirati PASS**, poi suite completa finale
**1.506 PASS, zero FAIL, un SKIP**, 1.507 casi, exit 0. Sono 55 nuovi casi:
18 protocollo/dispatcher, 26 binding, 11 sender. Gli ingressi esistenti sono
stati ampliati per attraversare pipe reale, handler staging, loader e
cleanup. La prima suite completa era già PASS; è stata ripetuta dopo
l'ampliamento dell'asserzione del percorso integrato, conservando entrambe.
Il test host è saltato perché il processo gestito è già attivo.

Build add-in sperimentale 2027 PASS senza warning; build legacy 2024/.NET
Framework PASS con il warning nullable preesistente in SendCodeHandler.
Nessuna DLL installata, run nativa, nuova APK o prova Quest. Core 1.000 e
Unity 1.049 rimangono verifiche precedenti, non rieseguite qui. Il FakeAddIn
accetta esplicitamente la variante futura solo per provare il trasferimento:
non dimostra Add, topologia, OLE, preview o rollback Inventor.

Residui: inserimento sulla STA, journal degli effetti OLE e soluzione del
caso linked condiviso fallito nell'incremento 42, deposito documentale dei
linked, reader/rendering, bozza e preview CAD, Quest e prova fisica. La
variante reale `insert_image` non è ancora ammessa dall'EditSketchHandler.
Evidenze e DLL compilate congelate in `increment44-manifest.json`.
