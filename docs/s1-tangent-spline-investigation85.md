# S1 — Conservazione della spline nell'Arco tangente85

15 ottobre 2026. Indagine nativa aperta; nessuna parità o nuova azione XR
dichiarata completa. Tutti i 18 gate S1 restano aperti.

## Evidenza negativa

Il supplemento83 prepara spline interpolanti non lineari con quattro fit
point e metodo26369, `kSmoothSplineFit` (centripeto). Questo metodo è
distinto dai26370/26371, come indica la
[documentazione Autodesk](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SplineFitMethodEnum.htm).

Il primo caso Start/lato negativo supera preview/rollback e commit, ma
fallisce la conservazione della sorgente. L'ispezione indipendente r3
mostra una definizione iniziale di ordine4, quattro poli e otto nodi;
la definizione finale ha ordine5, cinque poli e dieci nodi. I257 campioni
mantengono il numero, con distanza massima1,0444804700247066mm. Gli estremi
rimangono uguali: non bastano a provare la conservazione della curva.

Il vincolo nativo viene creato con endpoint realmente condiviso e proximity
esplicita. [AddTangent](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/GeometricConstraints_AddTangent.htm)
documenta la prossimità per scegliere fra due punti condivisi, ma non
garantisce che una sorgente sottovincolata rimanga geometricamente immutata.
La parità con il comando desktop deve ancora essere provata.

## Ipotesi provate e tentativi conservati

- r3, DLL `producer-r1`: otto casi ellittici e due rifiuti riusciti,
  poi FAIL della spline con modifica reale, acquisita nell'oracle nativo.
- r4, DLL `producer-order-r1`: invertire EntityOne/EntityTwo di AddTangent
  conserva la stessa deformazione. Ipotesi non risolutiva, cleanup riuscito.
- r5, DLL `producer-handle-r1`: attivazione della maniglia al punto sorgente
  prima di AddTangent; la preview viene rifiutata e non viene eseguito Apply
  della spline. Non è una prova riuscita di conservazione della forma.
- r6, stesso DLL di r5: client con osservatore degli errori strutturati,
  per conservare codice e dettagli consentiti prima che DesignSession
  trasformi l'errore in stato UI. Nessuna asserzione viene rimossa.
- r7, DLL `producer-handle-r2`: errore strutturato confermato
  `ROLLED_BACK`, passo0, `step_code=API_ERROR`,
  `native_stage=add_tangent`, `native_hresult=-2147467259` (E_FAIL).
  GetHandleStatus/SetHandleStatus sono stati superati: è AddTangent a
  rifiutare l'associazione dopo l'attivazione della maniglia. Nessun Apply
  della spline. Cleanup, ispezione nativa e ripristino riusciti.

Il candidato maniglia usa
[GetHandleStatus/SetHandleStatus](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchSpline_SetHandleStatus.htm)
con il vero SketchPoint della spline. L'API
[GetHandle](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchSpline_GetHandle.htm)
può restituire Nothing quando la maniglia è disabilitata; una maniglia
espone tangente e peso nativi. Questa informazione giustifica l'indagine,
senza dimostrare che l'attivazione sia la soluzione.

La modifica resta sperimentale e l'azione Quest indisponibile. Il DLL
installato ordinario viene preservato; ogni candidato è caricato soltanto
da un nuovo processo dedicato. Un FAIL della preview o della sorgente
non viene trasformato in PASS da una rilettura o da una riparazione.

La suite bridge aggiornata con l'errore strutturato supera1.679 test,
con uno skip preesistente; build add-in sperimentale e client diagnostico
senza errori o warning. Questo non risolve il FAIL nativo. Il passo
successivo è isolare in transazioni native possedute i comportamenti di
spline, maniglia e vincolo, con definizione prima/dopo e Abort esatto,
prima di scegliere e integrare una strategia correttiva.

## Osservazioni native e percorso successivo, 16 ottobre

`increment85/native-r1` completa20 osservazioni isolate: Parte/Lamiera,
Start/End, cinque strategie. In tutte20 la transazione Abort ripristina
esattamente lo stato nativo acquisito prima, comprese definizioni spline,
punti, maniglie, identità, Dirty e DatabaseRevisionId. Non sono20 PASS di
prodotto.

La tangenza diretta sulla spline modifica la forma; abilitare una maniglia
e vincolare ancora la spline intera produce E_FAIL. Vincolare la **maniglia
nativa** conserva invece definizione e campioni in quattro osservazioni,
con sorgente sottovincolata e Solve/Update riusciti. Il fissaggio temporaneo
conserva la forma ma lascia la sorgente sovravincolata: strategia scartata.
Nel percorso integrato r8 il fissaggio viene anche rifiutato dall'API.

Il tentativo integrato r9 usa maniglia+arco. Si ferma al primo snapshot
post-Apply della spline, dopo otto casi ellittici; rimane FAILED. L'ispezione
indipendente conferma che maniglia e suoi due estremi distinti sono nella
raccolta SketchEntities. Il reader li classificava però come linea e non
ne rappresentava l'ownership. Worker terminato normalmente, manifest
ripristinato e DLL ordinario invariato.

L'incremento87 introduce tipo `spline_handle`, ownership tramite slot locali
allo snapshot e protezione della maniglia/dei suoi estremi. Il FitPoint della
spline è un'identità distinta e resta selezionabile. Il vincolo deve
continuare a riportare i membri reali maniglia+arco; la spline proprietaria
è un legame separato, mai una sostituzione del membro nativo.
La prova integrata di questo contratto è ancora in corso. Quest rimandato
su richiesta dell'utente; tutti18 gate complessivi restano aperti.
