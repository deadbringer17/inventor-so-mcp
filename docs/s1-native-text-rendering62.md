# S1 — Rendering dei glifi nativi, incremento 62

14 ottobre 2026. Implementazione sperimentale; **18 gate S1 OPEN**.

Il reader cattura i triangoli effettivi di TextBox.GetFacetsInfo nel processo
Inventor, convertendo cm in mm e indici nativi da 1 a 0. Il payload dichiara
`coordinate_space=model_mm`: vertici già trasformati e ruotati, come provato
in 61. Non applica SketchFrame o Rotation una seconda volta. Lo stesso
payload passa nella snapshot corrente e nell’anteprima senza identità CAD
provvisorie. Colore RGB letto dall’oggetto nativo.

Il contratto verifica campi, coordinate finite, indici, colore e budget:
8.192 vertici/32.768 triangoli per testo, 100.000 punti complessivi fra curve
e glifi e 200.000 triangoli di testo per snapshot. Geometria vuota esplicita
è ammessa; mesh malformati e riferimenti CAD aggiunti al payload sono rifiutati.
Gli array Core sono posseduti e immutabili. I vecchi snapshot senza mesh
mantengono il precedente percorso di annotazione, senza nuove capacità.

Unity disegna mesh native nella vista corrente e nell’anteprima, con shader
URP unlit, stereo, Cull Off e offset di profondità. Quando i mesh sono
presenti non sostituisce i glifi con TMP. Le selezioni possono evidenziarli;
non vengono creati collider o riferimenti di authoring dai triangoli.
Lo stesso snapshot riusa mesh e materiali; disattivazione/distruzione
rilasciano le risorse possedute.

## Evidenze

- **1.163 Core PASS**, inclusi parsing/immutabilità, coordinate modello,
  anteprima senza duplicazione delle annotazioni, dati malformati e budget.
- **1.631 bridge PASS**; add-in experimental compilato con zero warning/errori.
- **1.070 Unity PASS**: integrazione DesignGeometryView senza TMP sostitutivo,
  nessuna doppia trasformazione, riuso/lifecycle e shader realmente eseguito
  su GPU. La prova GPU verifica il colore del materiale; non è una prova
  stereo sul dispositivo o un confronto pixel dell’intera scena.
- `text62-native-r2.json`: Parte/Lamiera PASS sul percorso HTTP/Core/add-in
  effettivi, confrontando vertici modello e triangoli con l’API nativa dopo
  entrambi gli aggiornamenti del parametro. Le preview contengono i glifi.
  La conferma di rendering della sonda Core è sintetica e distinta dai test Unity.

Il primo test Unity usava uguaglianza float esatta fra due conversioni in
metri: corretto soltanto il test con tolleranza 0,00000001 m, mantenendo il
controllo della posizione assoluta. Sorgenti/DLL/log originali conservati.
La prima build della sonda aveva un nome locale duplicato; il wrapper r1
ha invocato il vecchio binario che ha rifiutato il nuovo argomento con exit64,
prima di creare Inventor. Manifest ripristinato; non è una prova CAD.
Build corretta e run r2 distinti, conservati con i binari effettivi.

Sorgenti: `increment62/verified-source/`. Assembly eseguiti:
`increment62/final-binaries/` e `increment62/inprocess-tested-r2/`.
Processo di prova terminato, installazione originale e processo utente
preservati. Nessuna nuova APK creata o installata.

**NOT COVERED:** testo su geometria, background del testo (rifiutato senza
fingere una resa equivalente), selezione completa tramite rettangolo ruotato,
editor/input XR e opzioni di testo completi, benchmark su modelli complessi,
APKs/Quest, stereo fisico, font/opzioni non esercitati e prove manuali.
`text_create/update/delete` restano non pubblicati nelle capacità XR.
