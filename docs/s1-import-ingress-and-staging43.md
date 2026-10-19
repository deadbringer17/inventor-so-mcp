# S1 — Risorse ai due ingressi e staging sigillato

13 ottobre 2026, incremento 43, F03/S1-12/14. Perimetro integrale: 77 comandi,
18 gate aperti. È integrazione server verificata con FakeAddIn, senza nuova
capability di inserimento CAD.

`PlanningTools.PlanChange` e `ParameterTools.AtomicBatch` risolvono ora le
risorse di `edit_sketch/insert_image` tramite lo stesso resolver e l'identità
autenticata. Le operazioni sono prima convertite in JSON distaccato; owner,
path, URL e byte non sono argomenti accettati per l'immagine. Gli asset sono
deduplicati e acquisiti atomicamente. Missing, foreign, expired e deposito
assente producono lo stesso `ASSET_NOT_FOUND` prima del trasporto.

Il resolver controlla opzioni esplicite e metadata BMP Image senza dipendenze;
i metadata sono quelli del servizio inspector fidato. Non reinterpreta il
file BMP: l'inspector HTTP già valida il contenuto, mentre il deposito verifica
il suo hash. Record/hash acquisiti restano congelati anche se il catalogo viene
aggiornato. `Read` restituisce copie dei soli byte della lease.

Il batch diretto mantiene la lease fino al termine della chiamata. Il percorso
di pianificazione la mantiene durante preview e poi la trasferisce al piano,
che la conserva oltre la scadenza del catalogo. Commit, discard, scadenza del
piano ed errori/cancellazione la rilasciano. Solo il piano estratto da `Take`
può accedere internamente all'allegato tipizzato; Create/List e JSON non
espongono risorse o ownership. Il commit non risolve nuovamente il catalogo.

`SketchImportStagingStore` aggiunge un deposito API-agnostico in memoria:
ID opachi, binding owner/session, budget di byte/record riservati prima
dell'upload, TTL fisso, chunk sequenziali con offset esatto, seal solo a
dimensione completa e SHA256 corrispondente. Digest errato ritira il transfer.
Le lease accedono solo a trasferimenti sigillati e mantengono byte e budget
anche dopo discard/scadenza/disposal del deposito, fino all'ultimo rilascio.
Non crea file né riferimenti persistenti. Non è ancora registrato in un
handler o collegato al trasporto: nessun chunk è stato inviato a Inventor.

Verifica serializzata: **70 test mirati PASS**, poi **1.451 bridge PASS,
zero FAIL, un SKIP** su 1.452 casi, exit 0. Sono 50 casi nuovi: 26 resolver,
nove ingressi e 15 staging. Il test saltato riguarda l'avvio dell'host gestito,
già attivo; non è stato arrestato. Build add-in sperimentale 2027 e legacy
2024/.NET Framework PASS. Le DLL compilate non sono state installate.

I test degli ingressi usano BMP reali ispezionati e un FakeAddIn che accetta
esplicitamente la futura variante per verificare lifetime e cleanup: non
certificano inserimento, preview, geometria, OLE o rollback nativi. La variante
continua a essere rifiutata dall'add-in reale. `link=true` nel resolver è solo
acquisizione di byte, senza promessa di durata documentale.

Residui: adapter chunk e staging nel contesto add-in, binding al target
congelato, consumo da batch posseduto, Add sulla STA, journal OLE e soluzione
del fallimento linked condiviso dell'incremento 42, reader/rendering,
bozza Anteprima/Applica, prova Quest e fisica. Nessuna nuova APK o run nativa.
Core 1.000 e Unity 1.049 rimangono le verifiche dell'incremento 41,
non rieseguite qui. Evidenze congelate in `increment43-manifest.json`.
