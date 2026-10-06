# M10 — inventario dei comandi

Generato con `python scripts/inventory-m10-actions.py`. Mapping esplicito:
`assets/inventor-icons/m10-catalog.json`: **34 risorse, 46 azioni**.
Le righe testuali sono decisioni di copertura, non pulsanti senza asset.
Picker, parametri e campi di modifica feature generati a runtime conservano
sempre nomi, numeri e unità; tastierino e barra di conferma restano testuali.
`m10-pilot.json` conserva la selezione iniziale di 11 azioni come riferimento.

| Sorgente | Azione | Etichetta / espressione | Presentazione |
|---|---|---|---|
| DesignActions | `design.sketch.create` | `"Crea schizzo"` | icona sketch |
| DesignActions | `design.sketch.numeric` | `"Coordinate numeriche"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions | `design.sketch.dimension` | `"Quota geometria"` | icona dimension |
| DesignActions | `design.sketch.removelast` | `"Rimuovi ultimo"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions | `design.constraint.add` | `"Aggiungi vincolo"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions | `design.constraint.show` | `"Vincoli della geometria"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions | `design.constraint.all` | `"Mostra tutti i vincoli"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions | `design.constraint.removelast` | `"Rimuovi ultimo vincolo"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions | `design.extrude` | `"Estrusione"` | icona extrude |
| DesignActions | `design.hole` | `"Foro"` | icona hole |
| DesignActions | `design.fillet` | `"Raccordo"` | icona fillet |
| DesignActions | `design.chamfer` | `"Smusso"` | icona chamfer |
| DesignActions | `design.history.undo` | `"Annulla modifica XR"` | icona undo |
| DesignActions | `design.history.redo` | `"Ripeti modifica XR"` | icona redo |
| DesignActions | `design.dimension` | `"Dimensione numerica"` | testo: valore/stato corrente o nome CAD |
| DesignActions | `design.diameter` | `"Diametro"` | testo: valore/stato corrente o nome CAD |
| DesignActions | `design.through` | `"Foro passante"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions | `design.position` | `"Posizione esatta XYZ"` | testo: valore/stato corrente o nome CAD |
| DesignActions | `design.operation` | `"Operazione: " + OperationLabel(_operation)` | testo: valore/stato corrente o nome CAD |
| DesignActions | `design.direction` | `"Direzione: " + (_symmetric ? "Simmetrica" : _negative ? "Negativa" : "Positiva")` | testo: valore/stato corrente o nome CAD |
| DesignActions | `design.parameters` | `"Parametri"` | icona parameters |
| DesignActions | `design.refresh` | `"Aggiorna riferimenti"` | icona refresh |
| DesignActions | `design.view.model` | `"Vista modello"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions | `design.view.sheet` | `"Foglio"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions | `CommitIds.Preview` | `"Anteprima"` | testo: conferma CAD esplicita |
| DesignActions | `CommitIds.Apply` | `"Applica"` | testo: conferma CAD esplicita |
| DesignActions | `CommitIds.Cancel` | `"Annulla comando"` | testo: conferma CAD esplicita |
| DesignActions | `CommitIds.Recover` | `_session?.CommitOutcomeUnknown == true ? "Ho controllato il CAD" : "Aggiorna documento"` | testo: conferma CAD esplicita |
| LamieraActions | `lamiera.rule` | `"Regola / Spessore"` | icona sheetmetal-rule |
| LamieraActions | `lamiera.flange` | `"Flangia"` | icona flange |
| LamieraActions | `lamiera.flange.height` | `"Altezza: " + Fmt(_flange.HeightMm) + " mm"` | testo: valore/stato corrente o nome CAD |
| LamieraActions | `lamiera.flange.angle` | `"Angolo: " + Fmt(_flange.AngleDegrees) + " °"` | testo: valore/stato corrente o nome CAD |
| LamieraActions | `lamiera.flange.datum` | `"Riferimento: " + DatumLabel(_flange.Datum)` | testo: valore/stato corrente o nome CAD |
| LamieraActions | `lamiera.flange.clear` | `"Svuota bordi"` | testo: comando XR o corrispondenza nativa non verificata |
| LamieraActions | `lamiera.history.undo` | `"Annulla modifica XR"` | icona undo |
| LamieraActions | `lamiera.history.redo` | `"Ripeti modifica XR"` | icona redo |
| LamieraActions | `lamiera.face` | `"Faccia da schizzo"` | icona sheetmetal-face |
| LamieraActions | `lamiera.cut` | `"Taglio da schizzo"` | icona sheetmetal-cut |
| LamieraActions | `lamiera.sketch.change` | `"Cambia schizzo"` | icona select-sketch |
| LamieraActions | `lamiera.cut.extent` | `"Estensione: " + (_extent == "thickness" ? "spessore" : "passante")` | testo: valore/stato corrente o nome CAD |
| LamieraActions | `lamiera.cut.direction` | `"Direzione: " + DirectionLabel(_direction)` | testo: valore/stato corrente o nome CAD |
| LamieraActions | `lamiera.cut.acrossbends` | `"Attraverso pieghe: " + (_acrossBends ? "sì" : "no")` | testo: valore/stato corrente o nome CAD |
| LamieraActions | `lamiera.flat.create` | `"Crea sviluppo"` | icona flatpattern |
| LamieraActions | `lamiera.flat.show` | `"Mostra sviluppo"` | icona flatpattern |
| LamieraActions | `lamiera.flat.hide` | `"Nascondi sviluppo"` | testo: comando XR o corrispondenza nativa non verificata |
| LamieraActions | `lamiera.flat.detach` | `"Stacca sviluppo"` | testo: comando XR o corrispondenza nativa non verificata |
| LamieraActions | `lamiera.flat.attach` | `"Riaggancia sviluppo"` | testo: comando XR o corrispondenza nativa non verificata |
| LamieraActions | `lamiera.view.folded` | `"Vista: piegato"` | testo: comando XR o corrispondenza nativa non verificata |
| LamieraActions | `lamiera.view.flat` | `"Vista: sviluppo"` | icona flatpattern |
| LamieraActions | `lamiera.refresh` | `"Aggiorna"` | icona refresh |
| LamieraActions | `CommitIds.Preview` | `"Anteprima"` | testo: conferma CAD esplicita |
| LamieraActions | `CommitIds.Apply` | `"Applica"` | testo: conferma CAD esplicita |
| LamieraActions | `CommitIds.Cancel` | `"Annulla comando"` | testo: conferma CAD esplicita |
| LamieraActions | `CommitIds.Recover` | `_session?.CommitOutcomeUnknown == true ? "Ho controllato il CAD" : "Aggiorna documento"` | testo: conferma CAD esplicita |
| AssemblyActions | `assembly.components` | `"Componenti"` | testo: comando XR o corrispondenza nativa non verificata |
| AssemblyActions | `assembly.isolate` | `"Isola"` | icona isolate |
| AssemblyActions | `assembly.release` | `"Rilascia"` | icona release-isolation |
| AssemblyActions | `assembly.move` | `"Sposta"` | icona move |
| AssemblyActions | `assembly.open` | `"Apri"` | icona open-component |
| AssemblyActions | `assembly.open.design` | `"Apri in Progettazione"` | testo: comando XR o corrispondenza nativa non verificata |
| AssemblyActions | `assembly.open.lamiera` | `"Apri in Lamiera"` | testo: comando XR o corrispondenza nativa non verificata |
| AssemblyActions | `assembly.activate` | `"Attiva questo assieme"` | testo: comando XR o corrispondenza nativa non verificata |
| AssemblyActions | `assembly.constrain` | `"Vincola"` | icona constrain |
| AssemblyActions | `assembly.joint` | `"Giunto"` | icona joint |
| AssemblyActions | `assembly.references` | `"Facce / spigoli"` | testo: comando XR o corrispondenza nativa non verificata |
| AssemblyActions | `assembly.references.mode` | `"Raggio su spigoli"` | testo: comando XR o corrispondenza nativa non verificata |
| AssemblyActions | `assembly.relation.value` | `field` | testo: valore/stato corrente o nome CAD |
| AssemblyActions | `assembly.relation.flip` | `"Inverti direzione"` | testo: comando XR o corrispondenza nativa non verificata |
| AssemblyActions | `assembly.relation.align` | `_command == "assembly_joint" ? "Inverti allineamento" : "Tangente interna/esterna"` | testo: valore/stato corrente o nome CAD |
| AssemblyActions | `assembly.clearance` | `"Gioco minimo: " + Fmt(_clearance) + " mm"` | testo: valore/stato corrente o nome CAD |
| AssemblyActions | `assembly.refresh` | `"Aggiorna"` | icona refresh |
| AssemblyActions | `assembly.history.undo` | `"Annulla modifica XR"` | icona undo |
| AssemblyActions | `assembly.history.redo` | `"Ripeti modifica XR"` | icona redo |
| AssemblyActions | `assembly.move.mode` | `_rotating ? "Rotazione → Traslazione" : "Traslazione → Rotazione"` | testo: valore/stato corrente o nome CAD |
| AssemblyActions | `assembly.move.axis` | `"Asse"` | testo: comando XR o corrispondenza nativa non verificata |
| AssemblyActions | `assembly.move.value` | `_rotating ? "Angolo preciso" : "Spostamento preciso"` | testo: valore/stato corrente o nome CAD |
| AssemblyActions | `CommitIds.Preview` | `"Anteprima"` | testo: conferma CAD esplicita |
| AssemblyActions | `CommitIds.Apply` | `"Applica"` | testo: conferma CAD esplicita |
| AssemblyActions | `CommitIds.Cancel` | `"Annulla comando"` | testo: conferma CAD esplicita |
| AssemblyActions | `CommitIds.Recover` | `_session?.CommitOutcomeUnknown == true ? "Ho controllato il CAD" : "Aggiorna documento"` | testo: conferma CAD esplicita |
| InspectActions | `inspect.measure` | `"Punto-punto (locale)"` | icona measure |
| InspectActions | `inspect.measure.pin` | `"Fissa misura"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.measure.cancel` | `"Annulla misura"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.measure.clear` | `"Rimuovi tutte"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.section` | `"Sezione"` | icona section |
| InspectActions | `inspect.section.offset` | `"Scostamento: " + Format(_section?.OffsetMm, "mm")` | testo: valore/stato corrente o nome CAD |
| InspectActions | `inspect.section.angle` | `"Angolo Y: " + Format(_section?.AngleDegrees, "°")` | testo: valore/stato corrente o nome CAD |
| InspectActions | `inspect.section.reset` | `"Ripristina piano"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.group` | `"Apri strumenti"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.group.exit` | `"Schede principali"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.browse` | `"Esplora"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.properties` | `"Proprietà"` | icona properties |
| InspectActions | `inspect.documents` | `"Elenco documenti"` | icona documents |
| InspectActions | `inspect.scale` | `"Scala"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.environment` | `env` | testo: valore/stato corrente o nome CAD |
| InspectActions | `inspect.context.enter` | `"Apri contesto"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.context.back` | `"Livello superiore"` | icona back |
| InspectActions | `inspect.visibility.xray` | `"X-Ray"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.visibility.isolate` | `"Isola"` | icona isolate |
| InspectActions | `inspect.visibility.hide` | `"Nascondi"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.visibility.showall` | `"Mostra tutto"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.verify.interference` | `_scopeSelection && _selected != null ? "Interferenze di " + _selected.Name : "Interferenze"` | icona interference; testo quando contiene un nome CAD |
| InspectActions | `inspect.verify.scope` | `"Solo selezione"` | testo: comando XR o corrispondenza nativa non verificata |
| InspectActions | `inspect.verify.distance` | `_distanceA == null ? "Distanza minima" : "Distanza minima da " + _distanceA.Name` | icona measure; testo quando contiene un nome CAD |
| InspectActions | `inspect.verify.health` | `"Salute assieme"` | icona assembly-health |
| InspectActions | `inspect.verify.results` | `"Risultati (" + _findings.Count + ")"` | testo: valore/stato corrente o nome CAD |
| InspectActions | `inspect.verify.ignore` | `"Ignora risultato"` | testo: comando XR o corrispondenza nativa non verificata |
| DocumentActions | `doc.path` | `"Percorso"` | testo: comando XR o corrispondenza nativa non verificata |
| DocumentActions | `doc.back` | `"Torna"` | icona back |
| DocumentActions | `doc.save` | `"Salva"` | icona save |
| DocumentActions | `doc.documents` | `"Documenti aperti"` | icona documents |
| DocumentActions | `doc.recenter` | `"Ricentra postazione"` | testo: comando XR o corrispondenza nativa non verificata |
| DocumentActions | `doc.calibrate` | `"Calibra piano"` | testo: comando XR o corrispondenza nativa non verificata |
| DocumentActions | `doc.connection` | `"Connessione"` | testo: comando XR o corrispondenza nativa non verificata |
| DocumentActions | `doc.exit` | `"Esci"` | testo: comando XR o corrispondenza nativa non verificata |
| ViewActions | `view.fit` | `"Adatta"` | icona fit |
| ViewActions | `view.legend` | `"Legenda tasti: " + (LegendOn ? "sì" : "no")` | testo: valore/stato corrente o nome CAD |
| DesignFeatureEdit | `design.feature.prev` | `"Feature precedente: " + model.Info.PreviousFeature` | testo: valore/stato corrente o nome CAD |
| DesignFeatureEdit | `design.feature.desktop` | `"Modifica dal desktop"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignFeatureEdit | `design.feature.parameters` | `"Parametri"` | testo: comando XR o corrispondenza nativa non verificata |
| LamieraFeatureEdit | `lamiera.feature.prev` | `"Feature precedente: " + model.Info.PreviousFeature` | testo: valore/stato corrente o nome CAD |
| LamieraFeatureEdit | `lamiera.feature.desktop` | `"Modifica dal desktop"` | testo: comando XR o corrispondenza nativa non verificata |
| DesignActions.Shape | `design.shape.line` | Linea | icona line |
| DesignActions.Shape | `design.shape.rectangle` | Rettangolo | icona rectangle |
| DesignActions.Shape | `design.shape.circle` | Cerchio | icona circle |
