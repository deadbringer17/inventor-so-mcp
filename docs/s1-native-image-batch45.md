# S1 — Immagine nativa e journal OLE del batch

13 ottobre 2026, incremento 45, F03/S1-12/14. Perimetro invariato: 77 comandi,
18 gate aperti. Implementazione sperimentale in corso, non consegna completa.

`insert_image` dispone ora di un percorso interno nativo da byte sigillati:
il backend crea il journal prima della transazione, il comando verifica
ownership della transaction e del documento/sketch, hash e layout BMP contro
i metadata congelati, poi scrive una sorgente privata GUID e inserisce
l'immagine embedded. Non accetta percorsi pubblici. `link=true` è rifiutato
prima del file/Add: deposito documentale e caso linked condiviso non risolti.

Il journal cattura tutte le immagini e OLE preesistenti e il delta immediato
di ogni Add, anche su eccezione. Dopo Abort precontrolla tutte le cancellazioni,
protegge identità/file baseline, elimina soltanto descrittori catturati e
verifica il ripristino esatto prima di consentire il ripristino della revisione.
Un fallimento resta persistente anche se il runner chiede Rollback una seconda
volta. Dopo End il cleanup non può trasformare un commit riuscito in errore.
I file diagnostici restano conservati se la proprietà del ripristino è incerta.

La variante non viene pubblicata nelle capacità snapshot: reader, rendering
e bozza XR non sono ancora integrati. L'endpoint Risorse resta con
`cad_import_available=false`. Nessuna DLL è stata installata nel processo utente.

Prima del collaudo risultavano 30 documenti dell'utente aperti: la guardia
impediva di chiuderli. È stata preparata una sonda che accetta solo un nuovo
processo distinto e vuoto, identificato tramite HWND/PID; non chiude né
modifica l'istanza preesistente. L'utente ha poi comunicato di aver chiuso
Inventor. Le run hanno creato e chiuso soltanto la loro istanza dedicata.

Diagnostici nativi conservati:

- r1: primo caso preview rifiutato per una proprietà geometrica; il batch
  restituisce ROLLED_BACK e la fixture/processo posseduti vengono chiusi.
- r2: larghezza letta 29,999997183680893 mm a fronte di 30 mm richiesti.
  La tolleranza locale iniziale 0,000001 mm era più stretta della quantizzazione
  osservata. Usata la soglia già presente nel piano S1 di 0,00001 mm per
  proprietà numeriche; identità, OLE e confronto del ripristino restano esatti.
- r3: quattro casi completati, quinto fallito dopo commit con un quarto di
  giro. Width=20 mm, Height=13,333334585 mm, bordo circa 30×20 mm: il test
  ipotizzava erroneamente proprietà invariate e ingombro scambiato. Il caso
  resta FAIL. Nessuna sorgente
  privata residua, fixture e processo chiusi; nessun documento utente presente.

La ricerca diretta successiva conserva 32 osservazioni di ordine dei setter
e RotateLeft e poi una matrice ampliata di **48 osservazioni**, tutte con Abort
e ripristino esatto della baseline vuota. `RotateLeft` non ruota il bordo
dello schizzo e, sui quarti dispari, i setter successivi non producono il
ridimensionamento atteso. `PlanarSketch.RotateSketchObjects`, con l'immagine
nell'ObjectCollection e senza copia/rimozione vincoli, conserva invece Width
e Height locali e ruota realmente il bordo. L'implementazione usa ora questo
metodo attorno al punto d'inserimento e ricontrolla le proprietà dopo Update.
Il confronto resta API/API; non certifica il comando desktop o i pixel.

La matrice prodotto prevista comprende 48 casi per Parte e 48 per Lamiera:
BMP 24/32, top/bottom, quattro orientamenti, preview/commit/errore dopo Add.
Le run r1–r3 incomplete non certificano la matrice. La run corretta r4
completa **96/96 casi**, 48 per contesto: 32 preview, 32 commit e 32 errori
dopo Add con ripristino esatto, zero fallimenti. I commit mantengono immagini
embedded e OLE preesistenti delle righe precedenti, che diventano baseline
progressive. Width/Height locali, posizione e ingombro del bordo rispettano
le verifiche dei quattro orientamenti entro 0,00001 mm; non è un confronto
dei pixel. Le due fixture vengono chiuse senza salvare, il processo posseduto
terminato e nessuna sorgente privata residua è rilevata. Exit 0.

HTTP, installazione reale, preesistenti linked nel percorso prodotto,
preview mesh, Undo/Redo, linked, desktop equivalence, Quest e prova fisica
restano aperti. La suite FakeAddIn/backend finale passa **1.506 casi**, zero
fallimenti e uno skip per host già attivo; build add-in e sonda PASS senza
warning. Build legacy PASS con il warning nullable preesistente. Nessuna
nuova APK o prova Quest; Core/Unity non rieseguiti in questo incremento.
Sorgenti, DLL realmente eseguite, diagnostici e risultati sono congelati nel
manifest `artifacts/s1-verification/increment45-manifest.json`.
