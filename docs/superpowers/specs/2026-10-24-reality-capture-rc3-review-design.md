# Reality Capture RC3 — revisione indipendente

Data: 24 ottobre 2026. Attuazione della fase RC3 autorizzata dall’utente,
con sviluppo software prima dei test sul Quest. Il rinvio dell’integrazione
client indicato nel verbale RC2 viene superato da questa richiesta; i gate
fisici restano aperti.

## Contratto e limiti

La revisione apre esclusivamente sessioni finalizzate appartenenti al bearer
originale. Il client conserva uno snapshot del PC associato, usa HTTPS con
pinning e URL costruiti localmente, verifica dimensione e SHA-256 di ogni
artefatto. Gli otto artefatti RC2 restano compatibili: non serve migrare le
ricostruzioni precedenti.

Il viewer contiene mesh e punti propri, senza `CadBody`, collider o catalogo
CAD. GLB: metri, glTF destro; punti: mm, Unity sinistro. La riduzione della
vista riguarda esclusivamente il rendering; selezione e misure usano gli ID
e le coordinate originali. Il Trigger rispetta il raycaster UI e la guardia
di tracking; il picking CAD rimane sospeso. Rientro alla Home e chiusura
annullano le richieste UI, invalidano le risposte tardive e rimuovono la geometria.
La sospensione dell’app riporta alla Home della sezione. Il job sul PC può
proseguire dopo la chiusura della vista; `Annulla solve` è un’azione esplicita.

Le primitive sono fitting **manuali su una regione selezionata**, non
riconoscimento automatico dell’oggetto. Punto, asse PCA, piano PCA, cerchio
su piano e cilindro con ricerca dell’asse/proiezione circolare condividono
lo stesso sorgente matematico tra host e core XR. Il cilindro richiede almeno
12 punti spaziali e un’estensione assiale sufficiente; cerchi e cilindri
rifiutano copertura angolare inferiore a 90°, degenerazioni e residui radiali
eccessivi. I fitting ai minimi quadrati non costituiscono segmentazione robusta
di regioni miste: l’operatore deve isolare il supporto e controllare l’RMS.
Nessuna parte non osservata viene ricostruita o classificata come foro.

Parametri geometrici di riferimento: [modelli PCL](https://pointclouds.org/documentation/group__sample__consensus.html).
PCL non viene aggiunta come dipendenza; l’implementazione è gestita e limitata
a 2000 punti, con un solo fitting host alla volta e senza trattenere il lock
dei job durante il calcolo. L’RMS descrive la dispersione del supporto, **non** l’incertezza
metrologica, che viene riportata come `null` / «non stimata».

Le annotazioni risiedono in `solves/rXXXXXX/review/vXXXXXX.json`: CAS della
versione, massimo 32 primitive, 64 versioni, 1 MiB per snapshot, 8 MiB per
revisione. Conservano supporti e ID dei vincoli del solve congelato. Non
modificano raw o gli otto artefatti immutabili. Tre nuovi tool sperimentali:
`get_review`, `fit_primitive`, `remove_primitive`. Nessun comando Inventor.

La UI distingue misure ricostruite, quote note e residui del solve caricato.
I vincoli del solver restano distanze tra due punti e diametri definiti da tre
punti sul bordo, come in RC2; la separazione tra piani è una misura di revisione.
I vincoli correnti vengono modificati con CAS; un nuovo solve produce una
nuova revisione, senza aggiornare implicitamente quella visualizzata. Le
quote di controllo create nella UI non sono dichiarate indipendenti senza
evidenza esterna. Punti a due viste = debole supporto osservativo, non una
stima di copertura geometrica. Nessuna percentuale di copertura viene inventata.

Export GLB/PLY/OBJ sul visore in `RealityCapture/exports`, con unità, qualità
e annotazioni della revisione; nessun percorso arbitrario del PC è accettato.

## Gate

| Gate | Criterio software | Evidenza fisica separata |
|---|---|---|
| RC3-01 | Viewer separato, cambio unità/assi e scala di sola visualizzazione | Leggibilità e resa MR |
| RC3-02 | Picking ID, regione in mm, supporto riconoscibile, nessun input CAD | Controller e tracking reali |
| RC3-03 | Cinque primitive, degenerazioni rifiutate, RMS distinto da incertezza | Superfici opache/lucide e geometrie reali |
| RC3-04 | Distanza punti, punto-piano, piani paralleli, angolo acuto, raggio/diametro/estensione | Errori assoluti/relativi e ripetibilità indipendenti |
| RC3-05 | CAS quote/annotazioni, riferimenti congelati, nuova revisione senza mutare raw | Correttezza delle quote di controllo sul banco |
| RC3-06 | Export verificato e unità esplicite | Riutilizzo dei dati di un oggetto fisico |
| RC3-07 | Hash, limiti, token, cancellazione UI e cleanup | Prestazioni e interruzioni del dispositivo |
| RC3-08 | Runner sintetico preparato e compilato, distinto dal collaudo reale | Run Quest e prova con persona |

RC4 (riferimento Inventor, allineamento e overlay) resta una fase successiva.
Anche RC2 mantiene aperti MVS denso, prior calibrati e dataset fisico. Un
risultato software sintetico non attribuisce `VALIDATED` a una misura reale.
