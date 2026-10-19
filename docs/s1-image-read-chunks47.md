# S1 — Lettura bounded dei payload immagine, incremento 47

13 ottobre 2026. Proseguimento di S1-12; tutti i 18 gate restano aperti.

`SketchImportStagingStore.Lease.ReadChunk(offset, count)` restituisce una
copia indipendente del solo intervallo richiesto. Il limite configurato
`MaxChunkBytes` viene applicato prima dell'allocazione; conteggi nulli,
negativi o eccessivi e intervalli oltre la lunghezza sono rifiutati.
Il controllo usa sottrazione per evitare overflow di `offset + count`.
Lettura e rilascio della lease condividono il lock del deposito.

Una lease già acquisita conserva i dati dopo scadenza, discard o Dispose
del deposito, mantenendo addebitata la memoria fino al proprio Dispose.
La copia restituita resta indipendente dall'azzeramento finale del buffer.
Dopo Dispose della lease ogni lettura è rifiutata.

Verifiche: **29 test mirati PASS**, di cui 14 nuovi; suite backend completa
**1.520 PASS, zero FAIL, un SKIP** per host già attivo. Build add-in SO 2027
experimental PASS. Controllo di compilazione net48 PASS usando l'interop
2027 installata, con il warning nullable preesistente in SendCodeHandler.
Il primo tentativo legacy senza override è fallito per interop 2024 assente:
log diagnostico conservato, nessuna installazione 2024 o prova live dedotta.

È una primitiva per il futuro trasferimento inverso delle immagini dal CAD:
non introduce ancora un comando di download, un reader di immagini nello
snapshot o il rendering XR. Non rieseguiti test nativi, Core, Unity o Quest;
nessuna DLL distribuita e nessuna nuova APK. Le prove native dell'incremento
46 restano separate e congelate nel relativo manifest.
