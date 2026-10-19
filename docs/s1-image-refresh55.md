# S1 — Aggiornamenti del layer immagini, incremento 55

14 ottobre 2026. Ottimizzazione locale per S1-15; **18 gate aperti**.

Il layer riconosce il riuso della stessa istanza immutabile di snapshot
dopo le guardie disposed/null/disabled. Evita filtri, copie della metadata,
costruzione delle chiavi e aggiornamenti delle mesh. Uno snapshot nuovo
con la medesima revisione continua ad aggiornare il piano CAD.

La prova EditMode crea 32 quad e una texture condivisa, usando il client
Core e PNG realmente decodificati. Dopo dieci aggiornamenti di riscaldamento
misura 1.000 chiamate sullo stesso snapshot, verificando identità di mesh,
materiali, texture, task e numero di download. Verifica anche nuovo piano,
disabilitazione, null e dispose.

Il contatore Mono GetAllocatedBytesForCurrentThread restituisceva zero
anche per un'allocazione esplicita di 4.096 byte: non viene usato come prova.
Il recorder nativo GC.Alloc osserva un evento per il controllo positivo.
Il valore del campione è una misura temporale, non byte; il test confronta
quindi il numero di eventi e rifiuta la saturazione del recorder.

| Misura sul PC | Prima | Dopo |
|---|---:|---:|
| Eventi GC.Alloc in 1.000 chiamate | 1.025.994 | 0 |
| Tempo totale del ciclo | 233,2508 ms | 0,0172 ms |
| Quad / texture | 32 / 1 | 32 / 1 |

Il baseline corretto conserva il FAIL atteso sul budget zero. Due
diagnostiche precedenti documentano gli osservatori non validi; non sono
prove di assenza di allocazioni. La verifica finale supera **8/8 test**,
inclusi i sette test funzionali immagini. Sorgenti e assembly prima/dopo
sono conservati separatamente in `increment55`.

Questi numeri descrivono soltanto il ciclo misurato in Editor sul PC,
senza rendering di un frame completo. Non certificano p95 CPU, GC globale,
GPU, batteria o prestazioni del Quest. Nessuna nuova APK o installazione;
S1-15 e tutti i gate complessivi rimangono aperti.
