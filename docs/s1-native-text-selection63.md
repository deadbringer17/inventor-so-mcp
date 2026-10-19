# S1 — Selezione del testo nativo, incremento 63

14 ottobre 2026. Implementazione sperimentale; **18 gate S1 OPEN**.

Il reader aggiunge `text_bounds_mm`: quattro angoli in ordine ciclico, in
millimetri locali dello schizzo. Ruota RangeBox attorno a TextBox.Origin
con la rotazione nativa; non usa i vertici modello dei glifi come punti
locali. Il contratto ammette solo rettangoli finiti, non degeneri, con
limite di coordinate, ortogonalità e chiusura verificati. Payload opzionale
per compatibilità con i precedenti snapshot.

Il Core possiede gli angoli immutabili. Pick puntuale e finestra usano
l'intero rettangolo, compresi gli spazi fra lettere. Una finestra crossing
interamente interna seleziona il testo; contained richiede tutti gli angoli.
La selezione conserva l'identità nativa corrente; non inventa endpoint o
centri vincolabili. Il frame inclinato viene applicato dal percorso di input
e visualizzazione già esistente, non una seconda volta durante il pick.

## Evidenze

- **1.177 Core PASS**, incluse rotazioni 0/37/90/180/270°, distanza da
  interno/bordi/angoli, immutabilità, finestre e dati malformati.
- **1.643 bridge PASS**; add-in experimental zero warning/errori.
- **1.070 Unity PASS** (regressione completa PC).
- `text63-native-r3.json`: Parte/Lamiera PASS nel percorso HTTP/Core/add-in;
  otto verifiche di limiti e pick a 0/90/180/270° contro RangeBox/Origin e
  rotazione nativi. Revisione/Dirty invariati durante ogni lettura/pick.
  Ripetute anche otto preview/commit, quattro aggiornamenti di parametro
  con confronto dei glifi e quattordici rifiuti con rollback.

La sonda riusa la modalità `--text62`, estesa con osservazioni dei limiti;
wrapper, risultati e copie di sorgenti/binari hanno tutti namespace 63.
Il run r2 precede l'aggiunta della selezione a finestra al Core; r3 usa il
Core finale. I primi test Core avevano una fixture senza sketch_name e
poi costruivano un CadPoint NaN, che il costruttore rifiuta correttamente.
Corretti solo i test; log, sorgenti e DLL originali conservati.

Sorgenti finali: `increment63/verified-source/`; binari effettivi nativi
in `increment63/inprocess-tested-r3/`, regressioni in `final-binaries/`.
Processo dedicato terminato e installazione originale/processo utente
preservati. Nessuna APK nuova creata o installata.

**NOT COVERED:** interazione fisica Quest, runner Quest per questi pick,
oracle UI desktop/default/opzioni, matrice di font/giustificazione/background,
rettangoli degeneri o vuoti, testo su geometria, editor XR completo e
parità di tutte le trasformazioni. Le capacità testo restano non pubblicate.
