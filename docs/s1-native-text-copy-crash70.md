# S1 — Crash isolato nella copia TextBox, incremento 70

14 ottobre 2026. **18 gate S1 OPEN**; copia produttiva ancora bloccata.

Il worker70 riproduce il crash con un solo TextBox fitted `Copy70`,
origine (10,20) mm, nessun'altra curva/immagine/quota nella raccolta,
chiamando MoveSketchObjects con Copy=true, RemoveConstraints=false e
traslazione (12,-8) mm. È un processo Inventor dedicato nuovo, distinto
dal processo utente e verificato prima di ogni accesso ai documenti.

Il journal persistente registra:

- sequenza273: ingresso in `Sketch.MoveSketchObjects.TextBox.CopyTrue.RemoveFalse`;
- sequenza274: fallimento COM `0x800706BE`, senza ritorno riuscito dalla chiamata;
- guardie successive: `0x800706BA`, poiché il processo non è più raggiungibile.

**FAIL nativo**, exit1: processo dedicato14012 terminato, nessun conteggio
successivo disponibile e Abort impossibile. Non è un timeout, un successo
di copia o un rollback riuscito. Report e journal conservano la fase
primaria separata dalle guardie di cleanup, che cambiano l'ultima fase.
Il caso Lamiera non è stato ripetuto dopo aver isolato il crash in Parte.

La nuova sonda avrebbe verificato nuova identità, glifi nativi traslati,
originale e due modifiche successive soltanto se la copia fosse tornata
correttamente. Questi controlli **non sono stati eseguiti**. La build è
passata con zero warning/errori; non prova la funzione che poi ha fallito.

`increment70/r1-text-part/` conserva sorgenti e binari eseguiti, baseline,
journal e report. Manifest/DLL/interop installati e identità del processo
utente91056 invariati; nessun riferimento COM acquisito ai suoi documenti.
Nessuna distribuzione di DLL, APK, prova Quest o fisica. Il risultato
isola un caso sufficiente a terminare Inventor; non dimostra che sia
l'unica causa possibile della selezione mista storica.

**NOT COVERED:** rimedio produttivo, alternative native di clonazione,
Lamiera, copia mista/relazioni driving/immagini/altri tipi, preview/applica,
oracle desktop e dispositivo. La guardia generale non viene rimossa.
