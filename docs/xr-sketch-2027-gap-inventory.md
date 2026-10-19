# S1 — Inventor 2027 ↔ Schizzo XR: inventario e gap

Data: 6 ottobre 2026. Stato: **baseline congelata del checkout di quella data**.

La matrice seguente conserva i gap iniziali e i 77 identificativi nativi.
L'implementazione successiva e le evidenze per comando sono nel
[verbale S1 aggiornato](xr-sketch-2027-verification.md): gli stati N/X/P qui
riportati non descrivono il checkout corrente e non chiudono alcun gate.

Identificativo dell'implementazione: **S1**.

Spec: [requisiti e gate](superpowers/specs/2026-10-06-schizzo-parita-inventor-2027-design.md). Piano: [fasi F0–F7](superpowers/plans/2026-10-06-schizzo-parita-inventor-2027.md).

Fonte nativa: [JSON ribbon Part / Schizzo / Esci](xr-sketch-2027-ribbon-baseline.json), letto il 6 ottobre 2026 alle 21:16 Europe/Rome dall’Inventor 2027 installato, processo 97300 verificato contro il target MCP. **77 InternalName distinti**, comprese varianti nei menu e comandi contestuali. Nessuna esecuzione di comandi; identità, revisione database e stato Dirty invariati prima/dopo.

Questo inventario censisce controlli, non tutte le opzioni dei dialoghi. I controlli senza ControlDefinition sono contenitori: il lettore visita i figli fino a profondità 5. Le proprietà ChildControls non disponibili sui controlli terminali sono annotate nel JSON; il pannello ShowPanels senza comandi leggibili resta da verificare in F0. I nomi italiani normalizzano gli a-capo; due label vuote sono riportate con identificazione esplicita.

La copertura si riferisce al provider Schizzo XR e ai comandi catalogati nel bridge, non alla generica esistenza di un comando omonimo altrove. B e X descrivono il catalogo batch, non dichiarano equivalenza o nuovo collaudo.

| Stato | Significato |
|---|---|
| P | Percorso XR presente ma parità completa non dimostrata |
| B | Backend di base nel batch stabile; percorso XR mancante o incompleto |
| X | Backend pertinente nel tier sperimentale; integrazione/equivalenza da completare |
| N | Nessuna azione Schizzo XR corrispondente rilevata; mapping/implementazione da sviluppare |
| C | Comando contestuale censito, da includere nell’estensione dedicata |

## Matrice dei comandi

| # | Gruppo | Comando Inventor | InternalName nativo | Stato e differenza | Fase |
|---|---|---|---|---|---|
| 1 | Schizzo | Nuovo schizzo | `AppSketch2DWrapperCmd` | **P** — Crea schizzo XR su piano/faccia; manca sessione completa nuovo/esistente. | F2 |
| 2 | Schizzo | Nuovo schizzo | `AppSketch3DWrapperCmd` | **C** — Nuovo schizzo 3D: editor e baseline contestuale da sviluppare. | F6 |
| 3 | Crea | Linea | `SketchLineCmd` | **P** — Linea XR a due punti; inferenza coincidenza/H/V; continuità e opzioni da completare. | F3 |
| 4 | Crea | Linea punto medio | `SketchMidpointLineWrapperCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 5 | Crea | Spline vertice di controllo | `SketchCPSplineCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 6 | Crea | Spline | `SketchSplineCmd` | **X** — draw_spline sperimentale interpolante; nessuno strumento/maniglie XR. | F3 |
| 7 | Crea | Curva da equazione | `EquationCurveCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 8 | Crea | Curva ponte | `MoldBridgeCurve` | **C** — Curva ponte: prerequisiti/contesto nativo da verificare, editor dedicato. | F6 |
| 9 | Crea | Cerchio mediante centro e punto | `SketchCenterPointCircleCmd` | **P** — Cerchio XR centro/raggio; altre opzioni e editing nativo da completare. | F3 |
| 10 | Crea | Cerchio tangente | `SketchTangentCircleCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 11 | Crea | Ellisse | `SketchEllipseCmd` | **X** — draw_ellipse sperimentale; nessuno strumento XR dedicato. | F3 |
| 12 | Crea | Arco a tre punti | `SketchThreePointArcCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 13 | Crea | Arco tangente | `SketchTangentArcCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 14 | Crea | Arco mediante centro e punto | `SketchCenterPointArcCmd` | **B** — draw_arc stabile (centro/raggio/angoli); assente dal toolset XR, gesto diverso da completare. | F3 |
| 15 | Crea | Rettangolo mediante due punti | `SketchTwoPointRectangleCmd` | **P** — Rettangolo XR due punti; editor nativo e opzioni da completare. | F3 |
| 16 | Crea | Rettangolo mediante tre punti | `SketchThreePointRectangleCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 17 | Crea | Rettangolo centro a due punti | `SketchTwoPointCenRectangleCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 18 | Crea | Rettangolo centro a tre punti | `SketchThreePointCenRectangleCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 19 | Crea | Asola da centro a centro | `SketchCentertoCenterSlotCmd` | **X** — draw_slot sperimentale rettilinea centro-centro; non prova equivalenza delle varianti. | F3 |
| 20 | Crea | Asola sull'intera lunghezza | `SketchOverallLengthSlotCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 21 | Crea | Asola tramite punto centrale | `SketchCenterPointSlotCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 22 | Crea | Asola tramite arco mediante tre punti | `SketchThreePointArcSlotCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 23 | Crea | Asola tramite arco mediante centro e punto | `SketchCenterPointArcSlotCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 24 | Crea | Poligono | `SketchPolygonCmd` | **X** — draw_polygon sperimentale; nessuno strumento XR dedicato. | F3 |
| 25 | Crea | Raccordo | `SketchFilletCmd` | **N** — Raccordo 2D assente; design.fillet raccorda spigoli 3D. | F3 |
| 26 | Crea | Smusso | `SketchChamferCmd` | **N** — Smusso 2D assente; design.chamfer smussa spigoli 3D. | F3 |
| 27 | Crea | Crea testo | `SketchCreateTextCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 28 | Crea | Crea testo allineato alla geometria | `SketchCreateGeometryTextCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 29 | Crea | Punto, Punto centrale | `SketchHoleCenterPointCmd` | **B** — draw_point stabile nel batch; snapshot XR ignora i punti, nessuno strumento penna dedicato. | F3 |
| 30 | Crea | Proietta geometria | `SketchProjectGeometryCmd` | **B** — project_geometry stabile per spigoli modello; nessuna azione Schizzo XR, varianti/vertici da verificare. | F5 |
| 31 | Crea | Proietta spigoli di taglio | `SketchProjectCutEdgesCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 32 | Crea | Proietta modello piatto | `SketchProjectFlatPatternCmd` | **N** — Proiezione modello piatto da implementare nel contesto lamiera. | F5 |
| 33 | Crea | Proietta verso schizzo 3D (label COM vuota; da verificare in contesto) | `SketchProjectTo3dSketchCmd` | **C** — Proiezione verso schizzo 3D: richiede contesto/editor dedicato. | F6 |
| 34 | Crea | Proietta geometria DWG | `SketchProjectDwgGeomCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F3 |
| 35 | Serie | Serie rettangolare | `SketchRectangularPatternCmd` | **N** — Serie di sketch assente; rectangular_pattern del bridge è una serie di feature. | F5 |
| 36 | Serie | Serie circolare | `SketchCircularPatternCmd` | **N** — Serie di sketch assente; circular_pattern del bridge è una serie di feature. | F5 |
| 37 | Serie | Specchio | `SketchMirrorCmd` | **X** — mirror_sketch_entities sperimentale limitato ad alcuni tipi; parità e modifica da verificare. | F5 |
| 38 | Modifica | Sposta | `SketchMoveCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F5 |
| 39 | Modifica | Copia | `SketchCopyCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F5 |
| 40 | Modifica | Ruota | `SketchRotateCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F5 |
| 41 | Modifica | Taglia | `SketchTrimCmd` | **N** — Taglio per intersezioni assente; move_sketch_point non è equivalente. | F5 |
| 42 | Modifica | Estendi | `SketchExtendCmd` | **N** — Estensione per intersezioni assente; move_sketch_point non è equivalente. | F5 |
| 43 | Modifica | Dividi (label COM vuota; nome da screenshot) | `SketchSplitCmd` | **N** — Divisione curva assente; split del corpo solido non è equivalente. | F5 |
| 44 | Modifica | Scala | `SketchScaleCmd` | **N** — Scala CAD di geometria assente; zoom/manipolazione vista non sono equivalenti. | F5 |
| 45 | Modifica | Allunga | `SketchStretchCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F5 |
| 46 | Modifica | Offset | `SketchOffsetCmd` | **X** — offset_sketch_entities sperimentale; opzioni/associatività e percorso XR da completare. | F5 |
| 47 | Vincola | Quota generica | `SketchGeneralDimensionCmd` | **P** — XR quota linea/rettangolo/raggio; add_dimension advanced experimental, mancano modalità generiche. | F4 |
| 48 | Vincola | Quota automatica | `SketchAutoDimensionCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F4 |
| 49 | Vincola | Mostra vincoli | `SketchShowAllConstraintsCmd` | **P** — Mostra selezionati/tutti da snapshot preview; visibilità/identità native da completare. | F4 |
| 50 | Vincola | Opzioni vincolo | `SketchConstraintSettingsCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F4 |
| 51 | Vincola | Coincidente | `SketchCoincidentConstraintCmd` | **B** — add_sketch_constraint stabile; XR inferisce alcune coincidenze ma manca azione esplicita. | F4 |
| 52 | Vincola | Parallelo | `SketchParallelConstraintCmd` | **P** — Wizard XR presente con tipi/cardinalità limitati nella bozza; completare selezione nativa e opzioni. | F4 |
| 53 | Vincola | Tangente | `SketchTangentConstraintCmd` | **P** — Wizard XR presente con tipi/cardinalità limitati nella bozza; completare selezione nativa e opzioni. | F4 |
| 54 | Vincola | Collineare | `SketchColinearConstraintCmd` | **B** — add_sketch_constraint collinear stabile; non offerto nel wizard XR. | F4 |
| 55 | Vincola | Perpendicolare | `SketchPerpendicularConstraintCmd` | **P** — Wizard XR presente con tipi/cardinalità limitati nella bozza; completare selezione nativa e opzioni. | F4 |
| 56 | Vincola | Levigato | `SketchCurvatureConstraintCmd` | **N** — Levigato/continuità: assente; non sostituire con Tangente. | F4 |
| 57 | Vincola | Concentrico | `SketchConcentricConstraintCmd` | **P** — Wizard XR presente con tipi/cardinalità limitati nella bozza; completare selezione nativa e opzioni. | F4 |
| 58 | Vincola | Orizzontale | `SketchHorizontalConstraintCmd` | **B** — add_sketch_constraint stabile; automatico su linee, manca azione XR esplicita. | F4 |
| 59 | Vincola | Simmetrico | `SketchSymmetricConstraintCmd` | **P** — Wizard XR presente con tipi/cardinalità limitati nella bozza; completare selezione nativa e opzioni. | F4 |
| 60 | Vincola | Fissaggio | `SketchFixConstraintCmd` | **X** — fix solo tramite estensione experimental di add_sketch_constraint; azione XR assente. | F4 |
| 61 | Vincola | Verticale | `SketchVerticalConstraintCmd` | **B** — add_sketch_constraint stabile; automatico su linee, manca azione XR esplicita. | F4 |
| 62 | Vincola | Uguale | `SketchEqualConstraintCmd` | **P** — XR uguale linee/cerchi; equal linee stabile, equal_radius cerchi experimental. | F4 |
| 63 | Scheletro | Crea parte | `PartMakePartCmd` | **C** — Scheletro: azione XR assente; lifecycle/contesto da sviluppare. | F6 |
| 64 | Scheletro | Crea componenti | `PartMakeComponentsCmd` | **C** — Scheletro: azione XR assente; lifecycle/contesto da sviluppare. | F6 |
| 65 | Scheletro | Crea blocco | `PartCreateSketchBlockCmd` | **C** — Scheletro: azione XR assente; lifecycle/contesto da sviluppare. | F6 |
| 66 | Inserisci | Inserisci immagine | `AppInsertImageWrapperCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F6 |
| 67 | Inserisci | Importa punti... | `SCxImportPointsCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F6 |
| 68 | Inserisci | Inserisci file di AutoCAD | `SketchInsertAutoCADFileCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F6 |
| 69 | Formato | Costruzione | `SketchConstructionCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F4 |
| 70 | Formato | Linea d'asse | `SketchCenterlineCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F4 |
| 71 | Formato | Commutazione formattazione | `AppLinePropertiesToggleCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F4 |
| 72 | Formato | Quota non parametrica | `SketchDimensionToggleCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F4 |
| 73 | Formato | Punto centrale | `SketchHoleCenterCmd` | **N** — Nessuna azione Schizzo XR corrispondente; implementare varianti/opzioni native. | F4 |
| 74 | Parametri | Parametri | `AppParametersCmd` | **P** — XR modifica valore numerico di parametri esistenti; dialogo/espressioni completi mancanti. | F4 |
| 75 | Esci | Torna alla lavorazione | `ReturnToFeatureCmd` | **N** — Torna alla lavorazione dal contesto di modifica sketch da implementare. | F2 |
| 76 | Esci | Concludi schizzo | `Finish2dSketchCmd` | **B** — close_sketch stabile; manca azione XR dedicata per sessione di sketch persistente. | F2 |
| 77 | Esci | Annulla schizzo | `Cancel2dSketchCmd` | **P** — Annulla bozza XR presente; equivalenza con Annulla schizzo nativo da verificare. | F2 |

## Evidenza nel repository

- `Inventor XR SO/Assets/XrSo/Runtime/Ui/Shell/PaletteView.cs`: tavolozza 160 × 110 mm, griglia 2 × 4.
- `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/ActionCatalog.cs`: massimo 8 azioni, anello massimo 6.
- `Inventor XR SO/Assets/XrSo/Xr/DesignActions.cs`: tre forme, quota geometria, sei vincoli espliciti e azioni feature 3D.
- `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Backend/SketchDraft.cs`: tre tipi, massimo 30 geometrie + vincoli, nuova creazione e riferimenti posizionali.
- `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Backend/DesignGeometry.cs`: snapshot linee/cerchi; punti ignorati e curve ulteriori unsupported.
- `bridge/src/shared/Contracts/CadBatchCommandCatalog.cs`: operazioni stabili/sperimentali e limite 32 per batch.
- `bridge/src/shared/Handlers/Experimental/SketchHandlers.cs`: ellisse/spline/asola/poligono, offset/specchio, movimento punto, eliminazione entità e quote avanzate.
- `bridge/src/shared/Handlers/Sketch/`: creazione/chiusura, arco/punto/proiezione, dimensioni e vincoli di base.
- `docs/xr-m3-acceptance.md`: evidenza storica delle capacità base; non certifica le nuove varianti qui richieste.

## Conclusione operativa

La parità richiede sia più spazio UI sia un editor persistente di entità native con selezione/quote/vincoli/associatività. Il primo incremento consegnabile è la tavolozza ampliata; tutti i comandi rimangono tracciati fino all’equivalenza verificata. La percentuale di parità non è calcolata: P include differenze e il denominatore definitivo dipende dai contesti/opzioni da congelare in F0.
