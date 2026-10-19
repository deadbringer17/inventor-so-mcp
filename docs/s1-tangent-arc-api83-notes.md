# S1 — Fondamento API per Arco tangente83

15 ottobre2026. Ricerca per una capacità ancora mancante; nessun nuovo
handler, azione XR o gate viene dichiarato implementato da questo documento.
Arco tangente resta parte della consegna completa S1, insieme al passaggio
continuo linea/arco e alle inferenze desktop.

L'API2027 non elenca un costruttore `AddByTangent` nella collezione
[SketchArcs](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchArcs.htm).
Il percorso da verificare è una creazione nativa con endpoint condiviso,
seguita da un vincolo nativo di tangenza nella stessa transazione posseduta.

- [AddByCenterStartEndPoint](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchArcs_AddByCenterStartEndPoint.htm)
  accetta SketchPoint o Point2d per centro/start/end. Uno SketchPoint in
  ingresso collega l'arco a quel punto; il flag CounterClockwise sceglie
  il verso. Non sostituire un endpoint selezionato con la sua sola coordinata.
- [Curve2dEvaluator.GetTangent](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/Curve2dEvaluator_GetTangent.htm)
  restituisce direzioni tangenti ai parametri indicati. Parametri e tangenti
  usano array inizializzati; la firma ref/out va confermata nell'interop31
  installato prima del codice. Vanno anche verificati gli estremi del
  dominio con il punto nativo selezionato, senza presumere il verso API.
- [GeometricConstraints.AddTangent](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/GeometricConstraints_AddTangent.htm)
  crea il vincolo e rifiuta una condizione sovravincolata. Il parametro
  facoltativo di prossimità distingue casi con due punti condivisi; serve
  un test nativo prima di usarlo per varianti ambigue.

## Geometria iniziale e associazione

Dato il punto iniziale P, la tangente uscente unitaria t e il punto finale Q,
si pone n=(-t.y,t.x), d=Q-P e k=(d·d)/(2d·n). Il centro iniziale è P+k n
e il raggio è |k|. Il verso deve concordare con t al punto P. Questa
costruzione determina una configurazione iniziale tangente, ma da sola non
prova l'associazione parametrica: servono endpoint condiviso e vincolo nativo.

Rifiutare prima della creazione coordinate non finite, tangente nulla,
endpoint coincidenti e d·n prossimo a zero. Per curve chiuse, endpoint
coincidenti o parametri ambigui non scegliere un ramo arbitrario.

I riferimenti devono usare documento/revisione/sketch/entità/subpart attuali,
con owner e tipo nativo verificati. Il percorso esistente
`NativeSketchConstraints.ResolveSelections` già risolve endpoint e protegge
la geometria strutturale delle immagini; conserva anche la curva sorgente
per il vincolo, senza perdere l'identità dopo la risoluzione del punto.

## Prova necessaria prima di esporre il comando

Confrontare prima il gesto desktop e i default con fixture proprie: endpoint
Start/End, entrambi i lati e versi, sorgenti linea/arco e ulteriori curve
ammesse dal desktop. Per Parte e Lamiera verificare preview/Abort esatto,
commit, punto condiviso reale, vincolo tangente con binding completo,
identità e geometria sorgente conservate, due aggiornamenti delle dipendenze
e SaveCopy/reopen. Aggiungere rifiuti stale/owner/tipo, degenerazioni,
sovravincolo ed errore successivo alla creazione.

La UI deve usare riferimenti selezionati, preview renderizzata e input
numerico equivalenti. Il runner Quest deve usare quelle azioni reali;
una costruzione numericamente tangente o un marker PASS di una sonda non
chiude da sola il comando, le inferenze o i gate S1.
