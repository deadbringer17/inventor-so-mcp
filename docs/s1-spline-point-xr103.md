# S1 — Modifica dei punti spline nell’editor XR, incremento 103

29 ottobre 2026. Collegamento del comando sperimentale `spline_point_update`
all’editor condiviso Parte/Lamiera. Riferimenti: spec e piano S1 del 6 ottobre,
[contratto nativo 98](s1-spline-point-edit98.md), [verifica PC 102](s1-spline-point-verification102.md).
Non completa S1-04/05/13/14/16 né la parità complessiva dello schizzo.

## Comportamento

In Schizzo → Modifica, **Modifica punto spline** richiede una spline intera
interpolata o CV e un punto nativo reale selezionati insieme. I filtri di
selezione e la scelta dei candidati sovrapposti restano quelli dell’editor.
L’ordine dei due pick è libero; il payload ordina sempre spline, poi punto.
Gli estremi Start/End della curva non sostituiscono il riferimento al punto.

Il tastierino parte dalle coordinate native del punto; si può inserire X/Y
oppure indicare la destinazione sul foglio. Confermare prepara soltanto la
bozza. Anteprima e Applica restano espliciti. Annullamento e nuova revisione
revocano i callback; dopo refresh occorre riselezionare entrambi i riferimenti
per riutilizzare la destinazione conservata.

Il Core controlla capacità, snapshot completa, tipo, sottoparte, selezionabilità,
documento, schizzo, revisione e coordinate numeriche finite. Congela la bozza e
ripete la validazione prima di confermarla. Punti strutturali di immagini/maniglie
non selezionabili restano esclusi. La snapshot attuale non espone i membri della
spline: il client **non attesta appartenenza da coordinate o indici**. Il plugin
la verifica nativamente in preview/commit e rifiuta atomicamente i punti estranei.
Nessuna modifica al contratto o al tier backend.

## Copertura

- Core: spline interpolata/CV, ordine dei pick e del wire, riferimenti scaduti o
  estranei, punti strutturali, capacità mancante, coordinate non valide,
  congelamento e riselezione su nuova revisione.
- EditMode Parte/Lamiera: selezione tramite filtri del prodotto, destinazione
  numerica e sul foglio, bozza senza commit implicito.
- Runner S1: otto creazioni aperte/chiuse Parte/Lamiera e otto modifiche del
  punto interno, tramite selezione/input del prodotto; Cancel, revoca callback,
  preview/rollback/revisione, renderer, Applica e prima snapshot con identità,
  metodo, chiusura e cardinalità conservati. Registra richiesta e posizione
  effettiva (residuo massimo ammesso 0,001 mm), senza dichiarare equivalenza
  a un oracle desktop. Le creazioni fit usano il metodo nativo 26369; gli altri
  metodi restano coperti solo dalla matrice PC separata.
- Il wrapper posseduto supporta `-Scope points`; la selezione mirata Editor usa
  `XR_SO_S1_SPLINE_POINTS_ONLY=1` ed esclude esplicitamente i casi storici.

Per ripetere il sottoinsieme, compilare l’add-in sperimentale in una nuova
cartella QA e `bridge/tests/S1QuestOwnedHost73`, poi eseguire il wrapper con
`-Scope points`, `-AddinDirectory` QA e `-OutputDirectory` nuovo. Il wrapper
conserva l’attestazione del target posseduto e verifica il cleanup separatamente.

## Esiti

Core **1.411**, backend **1.926**, Capture.Worker **16**, Unity EditMode
**1.207 PASS**: **4.560** test, zero falliti o saltati. La prima compilazione
Unity aveva rilevato un flag mancante nella fixture Lamiera dei nuovi test;
corretto il supporto di `NativeOverlap`, poi suite completa verde. Log iniziale
conservato. I warning preesistenti del bridge/host restano registrati nei log.

Meta XR Simulator 207.1 + Inventor 2027, `owned-simulator-r1`: **16 casi PASS,
1.644 asserzioni** (otto creazioni e otto modifiche punto). `success=true`,
verdetto **PARTIAL**; casi storici esplicitamente esclusi. Input sintetici,
nessuna esecuzione hardware. `readback-consistency.json` ricontrolla offline
le otto combinazioni univoche, identità, revisione, chiusura/metodo/cardinalità,
variazione degli stroke nativi e residui rispetto alla destinazione: massimo
**0,000000889 mm registrato** in questi otto casi. Non è un nuovo run CAD né un oracle
indipendente e non estende il risultato ad altre configurazioni.

`native-cleanup.json`: fixture ripristinate, PID QA **126692** assente, host
terminato con exit 0, credenziali private rimosse, manifest originale ripristinato
e DLL ordinaria immutata. Il processo preesistente **41096** è preservato e non
è stato usato dalle operazioni della fixture. Il `NOT COVERED` del runner per
il ripristino resta registrato, con l’esito separato dell’host.

Build `BuildQxApksBatch` e ispezione **PASS**: entrambi ARM64 IL2CPP,
16 nomi runner esatti nei metadati QA, zero nell’ordinario. Nessuna
esecuzione hardware. Dimensioni e SHA256:

- qa: **95.779.482 byte**. `9888ED37D90981D7E3056E19AF7F0AF1DE8076FBEEFC08D563CBA0A030DC22D3`.
- ordinary: **58.016.932 byte**. `3D9ECD1BE4CE2BF0B2AF9240A4B83ACB0FCB0BD80F5A120972A591C726C9D89E`.

Font Satoshi generati archiviati in `generated-fonts/`, poi ripristinati
selettivamente; modifiche utente preesistenti conservate. `git diff --check`
passa. `final-installation.json` conferma ancora manifest/DLL ordinari invariati,
PID QA assente e processo originale presente. Sorgenti e consegna nei manifest
locali `source-manifest.json` e `delivery.json`.

Evidenze nuove in
`artifacts/s1-verification/increment103/20261029-points-xr/`.

## Limiti

La matrice nativa 102 è evidenza separata. Questo incremento non prova modifica
maniglie, vincoli/dipendenze, persistenza/riapertura o tutte le opzioni spline.
Il simulatore usa input sintetico: prova fisica Quest, tracking, ergonomia e
prestazioni GPU restano aperti. Gli screenshot con posa sintetica non
certificano leggibilità o inquadratura del foglio nel visore.
