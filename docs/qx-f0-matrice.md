# QX F0 — matrice della migrazione

25 ottobre 2026. Baseline: `0e81660c03a00916b2963f784e81e6b100a33fd2`, branch di partenza `codex/reality-capture`. Le modifiche preesistenti a CLAUDE.md, .claude e ai documenti di progetto sono conservate.

## Baseline verificata

- Core: **1.337/1.337 PASS**, eseguiti prima delle modifiche runtime.
- Unity EditMode: **1.146/1.146 PASS**, log `artifacts/qx-f0-f4/baseline-unity.log`, risultati `baseline-editmode.xml`. Il processo era già stato avviato prima delle modifiche; il risultato riporta la suite precedente all’integrazione.
- Nessuna modifica bridge: suite backend completa non necessaria per questa tranche client; i test Core includono integrazioni HTTPS/FakeAddIn.
- Nessun Unity MCP configurato: verifiche tramite Editor batch installato 6000.6.3f1.
- Quest: `adb devices -l` senza dispositivi il 25 ottobre; nessuna installazione, fixture Inventor o prova fisica effettuata. M7 conserva la fixture dedicata esistente, senza usare documenti utente.

## Assegnazione delle capacità

| Famiglia | Disegno | Presentazione | Verifica | Percorsi da verificare |
|---|---|---|---|---|
| Schizzo semplice/nativo, feature, parametri, lamiera, vincoli e giunti CAD, cronologia/commit | sì, con disponibilità esistente | esclusi | esclusi | palette, anello, voce, input diretto, callback, confine MCP |
| Misura locale, scala, MR/VR, adatta, ricentra, sposta/ruota intero modello | sì | sì | sì | azioni condivise, grip, doppio grip, Y, stick |
| Sezione | sì, gruppo compatto | esclusa | sì | tastierino, grip piano, reset, uscita attività |
| Albero e proprietà | contesto M9 esistente | sola indicazione pezzo | navigazione locale; proprietà CAD online | browser offline, selezione, doppio Trigger locale |
| X-Ray/nascondi/isola, interferenze, clearance, salute/BOM M7 | area Verifica dedicata | esclusi | sì, secondo documento/revisione/stato | picker risultati, focus, stale, backend reale separato |
| Attivazione documento aperto | sì | scelta esplicita da elenco | scelta esplicita da elenco | nessun ingresso in authoring fuori Disegno |
| Salvataggio generale | etichetta Salva dal desktop | escluso | escluso | non equivale a salvataggio nativo |
| Capture | attività indipendente | — | — | raw, upload annullabile, acquisizione e commit incerto protetti |

## Inventario baseline per provider

ID letti dal sorgente al commit baseline. Un ID censito non certifica disponibilità, tier o parità delle opzioni. Per schizzo nativo valgono i cataloghi e i gate S1, conservati senza promozioni. Prefissi dinamici vengono filtrati anche al momento dell’invocazione.

### DesignActions.cs

`design.chamfer`, `design.constraint.add`, `design.constraint.all`, `design.constraint.removelast`, `design.constraint.show`, `design.diameter`, `design.dimension`, `design.direction`, `design.extrude`, `design.fillet`, `design.history.redo`, `design.history.undo`, `design.hole`, `design.operation`, `design.parameters`, `design.pick.`, `design.position`, `design.refresh`, `design.sketch.create`, `design.sketch.dimension`, `design.sketch.numeric`, `design.sketch.removelast`, `design.through`, `design.view.model`, `design.view.sheet`, `sketch.editor.open`

### AssemblyActions.cs

`assembly.activate`, `assembly.clearance`, `assembly.components`, `assembly.constrain`, `assembly.constraint.`, `assembly.history.redo`, `assembly.history.undo`, `assembly.isolate`, `assembly.joint`, `assembly.joint.`, `assembly.move`, `assembly.move.axis`, `assembly.move.axis.`, `assembly.move.mode`, `assembly.move.value`, `assembly.open`, `assembly.open.design`, `assembly.open.lamiera`, `assembly.pick.`, `assembly.references`, `assembly.references.mode`, `assembly.refresh`, `assembly.relation.align`, `assembly.relation.flip`, `assembly.relation.value`, `assembly.release`

### LamieraActions.cs

`lamiera.cut`, `lamiera.cut.acrossbends`, `lamiera.cut.direction`, `lamiera.cut.extent`, `lamiera.face`, `lamiera.flange`, `lamiera.flange.angle`, `lamiera.flange.clear`, `lamiera.flange.datum`, `lamiera.flange.height`, `lamiera.flat.attach`, `lamiera.flat.create`, `lamiera.flat.detach`, `lamiera.flat.hide`, `lamiera.flat.show`, `lamiera.history.redo`, `lamiera.history.undo`, `lamiera.pick.`, `lamiera.refresh`, `lamiera.rule`, `lamiera.sketch.change`, `lamiera.view.flat`, `lamiera.view.folded`, `sketch.editor.open`

### InspectActions.cs

`inspect.browse`, `inspect.context.back`, `inspect.context.enter`, `inspect.documents`, `inspect.environment`, `inspect.group`, `inspect.group.exit`, `inspect.measure`, `inspect.measure.cancel`, `inspect.measure.clear`, `inspect.measure.pin`, `inspect.pick.`, `inspect.properties`, `inspect.scale`, `inspect.scale.fit`, `inspect.scale.one`, `inspect.scale.recenter`, `inspect.scale.room`, `inspect.scale.table`, `inspect.section`, `inspect.section.angle`, `inspect.section.offset`, `inspect.section.reset`, `inspect.verify.distance`, `inspect.verify.health`, `inspect.verify.ignore`, `inspect.verify.interference`, `inspect.verify.results`, `inspect.verify.scope`, `inspect.visibility.hide`, `inspect.visibility.isolate`, `inspect.visibility.showall`, `inspect.visibility.xray`

### DocumentActions.cs

`doc.back`, `doc.calibrate`, `doc.connection`, `doc.crumb.`, `doc.documents`, `doc.exit`, `doc.list.close.`, `doc.open.`, `doc.path`, `doc.recenter`, `doc.save`

### ViewActions.cs

`view.fit`, `view.legend`

## Casi da migrare e gate

- M1/M2: ingresso esplicito in Verifica per i percorsi Ispeziona, senza chiudere manualmente l’authoring dopo un cambio documento.
- M3/M5/M6/M9/S1 e varianti: ingresso esplicito in Disegno, conservando gli assert e i limiti dei gate storici.
- M7: Verifica e selezione dal browser condiviso; stessi controlli su interferenza, distanza, salute, ambito, focus e invalidazione.
- Presentazione: misure e trasformazioni locali offline, nessun editor CAD o analisi tecnica; richiami catturati prima di cambiare attività rifiutati.
- QX-15: obiettivo iniziale 72 Hz (13,89 ms/frame), regressione p95 CPU non oltre 10% rispetto alla fixture equivalente; misurare GC e GPU quando disponibile. Soglie proposte, **non certificate** senza benchmark Quest.
- QX-16: ergonomia, comprensione e leggibilità richiedono persona e controller; gate aperto.

I gate M1–M10, S1 e RC non vengono chiusi o rinumerati da questa tranche. Esiti finali in [verbale QX](xr-quattro-esperienze-verification.md).
