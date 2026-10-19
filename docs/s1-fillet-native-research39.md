# S1 — Raccordo 2D: ricerca nativa

12 ottobre 2026. Incremento 39, B04/S1-06. Il perimetro S1 resta completo,
77 comandi e 18 gate aperti. Questa ricerca API non espone una capability
prodotto e non certifica il comando desktop o lo Smusso.

L'interop installata 31.0.19200.0 espone `SketchArcs.AddByFillet` con due
SketchEntity, raggio e due Point2d obbligatori. Non espone switch taglio o
conservazione vincoli in quella firma. Nessun metodo Chamfer trovato nelle
collezioni/entità Sketch 2D esaminate; questa osservazione non prova che lo
smusso non sia implementabile con una strategia diversa.

La sonda riproducibile `arc-ui-fillet-research` esercita 27 casi per contesto:
linea-linea, linea-arco e arco-arco, quattro combinazioni indipendenti dei lati
con sorgenti libere oppure parametriche, più un caso per coppia con raggio
espresso come nome di parametro. Due aggiornamenti per caso: dimensione
sorgente 22/18 mm nei casi driving, parametro del raggio 4/2 mm negli altri.

Build della sonda: zero warning/errori, exit 0. Parte e Lamiera:
**54 osservazioni completate, 108 aggiornamenti, 54 tangenze iniziali verificate,
54 Abort esatti**, zero eccezioni/divergenze rispetto ai controlli dichiarati.
Tangenza verificata geometricamente sui tratti finiti, raggio iniziale 3 mm,
solver, identità, membri nativi Tangent/Radius e parametri inclusi nel readback.
Le chiavi delle 108 sorgenti sopravvivono, ma lo stato geometrico cambia;
il log conserva gli estremi prima/dopo. Preparazione/cleanup per contesto
separati e stato finale con zero documenti.

**Il nome del parametro passato come raggio non crea una dipendenza persistente.**
Nei sei casi con espressione nominata il parametro cambia realmente a 4 e
2 mm, ma il raccordo resta a 3 mm: zero aggiornamenti del raggio su dodici,
zero RadiusDim create. Non contare queste osservazioni completate come PASS
dell'associatività. La prossima sonda deve confrontare questo controllo con
una RadiusDim esplicita collegata al parametro, verificando solver e tangenza.

Restano da verificare oracolo desktop, opzioni di taglio/conservazione,
invalidi e geometrie degeneri, curve proiettate, profili consumati, dipendenze
downstream, rimappatura dei riferimenti, input XR e preview/commit sul Quest.
`NativeSketchHit` non conserva oggi il punto scelto sul piano: un contratto
prodotto deve congelare due pick_points_mm ordinati con i riferimenti versionati.
Lo Smusso richiede una ricerca propria; non è sostituito da una feature 3D.

Evidenze: `fillet39-part.json`, `fillet39-sheet.json`, report prepare/cleanup,
`fillet39-before.json`, `fillet39-after.json` e `native-build39.log` in
`artifacts/s1-verification`. Sorgente e DLL effettivamente testati conservati
in `increment39/native-tested`, separati dai successivi esperimenti.
