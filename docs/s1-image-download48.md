# S1 — Download dei payload immagine persistenti, incremento 48

13 ottobre 2026. S1-04/12, implementazione parziale; **18 gate aperti**.

La query interna sperimentale readonly `get_sketch_image_payload` legge il
payload originale gestito dal documento tramite `begin/chunk/discard`.
Ogni richiesta richiede documento attivo, revisione, schizzo, immagine,
owner e sessione. Il riferimento dell'immagine deve appartenere allo
schizzo e non essere linked. Il binding include tutti questi identificatori,
con sessione derivata mediante SHA256 di un array JSON.

Il deposito download è separato dall'upload: massimo 32 MiB per immagine,
64 MiB residenti e 32 trasferimenti, TTL fisso di due minuti con sweep lazy
alle operazioni successive. La disattivazione dell'add-in lo rilascia.
Blocchi limitati a 192 KiB; la risposta iniziale non contiene byte.
Begin legge e valida una volta il payload nativo; i blocchi successivi
copiano solo l'intervallo richiesto. Deadline, revisione e documento sono
ricontrollati. Gli errori iniziali rilasciano il trasferimento appena creato;
per un binding non più recuperabile il TTL rimane il fallback.

L'adattatore server `SketchImageDownload` conserva lo stesso target per
tutte le richieste, inclusa la pulizia con token indipendente di due secondi.
Verifica metadati e budget prima dell'allocazione, identità/offset/lunghezza
dei blocchi, dimensione base64 prima della decodifica, SHA256 totale e
layout BMP. Buffer parziali azzerati su errore; il risultato possiede il
buffer completo e lo azzera al Dispose. Nessun path o byte entra nei log.

Verifiche:

- **27 test dell'adattatore PASS**, pipe FakeAddIn: trasferimento oltre
  192 KiB, blocchi piccoli, BMP top-down, metadati/risposte/hash/layout errati,
  cancellazione, cambio target e fallimento della pulizia.
- **1.547 backend PASS, zero FAIL, un SKIP** per host già attivo.
- Build add-in experimental 2027 e sonda PASS, zero warning/errori;
  compatibilità net48 compilata con interop 2027 PASS.
- **32/32 casi nativi PASS**: 24/32 bit × top-down/bottom-up × quattro
  rotazioni, Parte e Lamiera. Originale eliminato e upload rilasciato
  prima del download; cinque blocchi per ciascun BMP di 78 byte, hash esatto,
  320 rifiuti, confronto esatto dello stato CAD incluse revisioni, Dirty,
  geometria e OLE. Blocchi massimi verificati nei test FakeAddIn, non con BMP
  grandi nella matrice nativa.
- Istanze dedicate terminate, documenti precedenti invariati, staging
  upload/download vuoto e file temporanei rimossi.

Lo snapshot diagnostico dopo ogni commit resta parziale per l'immagine.
Gli otto elementi del telaio sono già rappresentati; il vincolo nativo
`kTextBoxConstraintObject` viene letto con binding unavailable. Questa
osservazione non abilita rendering, selezione o modifica dell'immagine.

Il payload è il sorgente originale persistito dal nostro inserimento:
non certifica che una sostituzione o un flip dal desktop abbia conservato
la corrispondenza con i pixel visualizzati. Legacy/non gestite, linked,
orientamento UV, reader/texture XR, preview e download HTTP degli asset
restano da completare. Query e adattatore non sono ancora esposti da un
tool MCP di prodotto. Nessuna installazione DLL/APK o prova Quest;
Core/Unity non rieseguiti. Undo/OLE resta PARTIAL come nell'incremento 46.
