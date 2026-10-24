# S1 — Aggiornamenti e riapertura delle copie curve, incremento 95

23 ottobre 2026. Verifica PC in corso, nessun PASS lifecycle ancora attribuito.
Il candidato è il plugin 93 congelato, SHA256
`31680B0110529BBA8E20B5ECF9023E286D2E3385943509C459634C8C9FA7A8D8`.
Non contiene le successive modifiche spline 96.

La matrice conserva integralmente il [profilo 94](s1-copy-core-curves94.md):
18 workflow, 28 coppie, 10 rifiuti e 74 checkpoint. Aggiunge quattro modifiche
delle sorgenti, due salvataggi in copia con chiusura/riapertura, sei prime
letture Core via HTTPS e un protocollo di 14 azioni con ACK posseduti.

Le cinque sorgenti per contesto sono un cerchio e quattro archi (quadrante,
attraversamento angolare, maggiore, orario). La prima modifica trasla i centri
di (5,7) mm e porta il raggio del cerchio a 12 mm; la seconda porta i centri a
(-6,4) mm rispetto all'iniziale e il raggio a 6 mm. Gli archi devono conservare
raggio 9 mm, angoli e dominio. Tutte le 28 copie, i relativi punti/vincoli,
gli oggetti non selezionati e i parametri devono rimanere invariati.

La prima snapshot dopo ogni End/Open precede l'ispezione geometrica nativa.
Non si ritenta una lettura fallita e non si sostituisce il risultato osservato.
L'unica variazione di markup ammessa alla riapertura è il path locale del
campo parametro non copiato, con grammatica e identità esatte del profilo 89.
Le sole annotazioni sorgenti mobili devono seguire il punto individuato dai
veri membri ordinati nativi del vincolo e dal reader.

## Tentativi conservati

- r1, run `02dad5caadb84cfb9fcaae2963fc8025`, PID 64668: baseline completa,
  arresto alla prima snapshot Core dopo la modifica Parte. Centri e raggio
  corretti, estremi degli archi deformati: FAIL conservato, nessun PASS
  lifecycle. La fixture selezionava la sola curva. Il codice di prodotto
  `NativeSketchTransforms` include già anche i punti che la definiscono.
  Worker e cleanup normali, manifest/DLL ripristinati, assenza PID verificata.
- r2, run `af358696f8e34cd2bc55955fe74e2b78`, PID 99840: selezione corretta,
  due modifiche Parte e prima Core dopo riapertura passate. L'ispezione nativa
  si è fermata al confronto degli altri sketch: unico delta è il path locale
  del campo `L=10/S1QuestLength73` in `S1_TextRead`, aggiornato da SaveCopy.
  FAIL conservato, cleanup e assenza PID verificati. Il confronto puro dei
  raw005/006 passa con la sola ricollocazione attestata del campo; sei
  alterazioni semantiche sono rifiutate. 21 test della regola passati.
- r3, run `0495e916f5fd46c88e1b77492626e887`, PID 117200, terminato
  con attestazione congelata delle chiavi documento/sketch/
  text/parametro del campo10mm e confronto rigoroso della sola ricollocazione.
  Il normalizzatore13mm rimane invariato. La fixture usa
  la selezione corretta (curva più centro, start/end nativi
  dell'arco; curva più centro per il cerchio). Nessun punto cercato tramite
  coordinate, nessun Ground/MoveTo o riparazione dopo End. Build riuscita;
  il producer passa l'intera matrice: quattro aggiornamenti, due riaperture,
  sei prime letture Core. Cleanup normale e assenza PID verificata.
  Il verificatore offline non passa: manca la registrazione delle osservazioni
  degli anchor al secondo aggiornamento. La riassegnazione di un JArray già
  collegato al report ne provoca la clonazione, lasciando gli append successivi
  sull'array scollegato. Corretta la sola registrazione nella sonda; occorre
  ripetere l'intera matrice. Nessun PASS offline attribuito a r3. Inoltre
  corretto il verificatore: il DisplayName rimane quello originale dopo
  SaveCopy/Open e va confrontato con l'identità iniziale attestata, non con
  il nuovo basename su disco. Il path resta verificato separatamente.

Fonti, assembly e report di ciascun tentativo sono separati in
`artifacts/s1-verification/increment95/native-rN`. Non sono stati modificati
i criteri geometrici per accettare il fallimento r1.

Ridimensionamento/cambio estensione degli archi, altri profili Copia,
equivalenza desktop completa, UI/Quest e prova fisica restano aperti.
Questo lavoro non chiude nessuno dei 18 gate S1 complessivi.
