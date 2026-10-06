# M9 navigazione per contesto — stato e verifica

Data: 5 ottobre 2026, Europe/Rome.
Spec: [M9 navigazione per contesto](superpowers/specs/2026-10-04-m9-navigazione-contesto-design.md).
Piano: [M9](superpowers/plans/2026-10-04-m9-navigazione-contesto.md).

**Stato:** implementazione software presente sul branch
`feat/m9-navigazione-contesto`. Eseguiti il **runner Quest con input sintetico**
(5 ottobre 2026, esito `PARTIAL`) e la **sonda live `face_feature`** su 4 tipi
di feature. **Nessuna prova fisica** da seduto. Gli altri gate restano aperti.

## Test automatici locali — 5 ottobre 2026

| Suite | Esito |
|---|---|
| Core XR .NET / FakeAddIn (`XrSo.Core.Tests`) | PASS 584/584 |
| Backend `Bimwright.Ipt.Tests` (con `INVENTOR_SO_EXPERIMENTAL` non impostata) | PASS 1041, 1 skipped |
| Unity EditMode | PASS 559/559 |
| Compilazione di tutti i runner con `XR_SO_ACCEPTANCE` | PASS (nessun CS) |

Con `INVENTOR_SO_EXPERIMENTAL=1` nella shell `TierPolicyTests.ExperimentalFlagLoadsFromCliAndEnvironment`
fallisce per l'ambiente, non per M9.

## Runner Quest M9 — 5 ottobre 2026 (input sintetico, fixture M6, Inventor 2027 reale)

Esito: **`PARTIAL; NOT COVERED: M9-09-handle, M9-09-highlight, M9-02-subassembly`**
(uscita 4). Manifest e log: `artifacts/m9-verification/device/quest-acceptance-run-20261005-124428.json`,
`...-m9-acceptance.txt`, screenshot `ghost-context`, `legend-rest/armed/component`.
Ripristino eseguito: fixture chiusa senza salvare, APK ordinario reinstallato.
I giri precedenti sono falliti per cause mie e del prodotto (vedi sotto); il
giro che conta è l'ultimo, con APK costruito dopo la correzione di Lamiera.

Tre esiti separati: **U** = unitario/FakeAddIn/EditMode, **R** = runner Quest
sintetico, **F** = prova fisica.

| ID | U | R | F | Note |
|---|---|---|---|---|
| M9-01 contesto dal documento, scheda Documento | PASS | **PASS** (anche cambio documento dal PC e da «Documenti aperti») | — | «Salva» del visore non salva: HUD «Salva dal desktop». |
| M9-02 doppio Trigger / guardie | PASS | **PASS** su componente (parte) e guardie (revisione, Sposta); sottoassieme **NOT COVERED** (la fixture non lo ha) | — | |
| M9-03 assieme fantasma | PASS | **PASS** (non selezionabile, posa, etichetta, coerente dopo Adatta) | aperto | Screenshot `ghost-context` salvato; aspetto da valutare a occhio. |
| M9-04 Torna, «●» | PASS | **PASS** (X breve = suggerimento, X tenuto 1 s, scheda Documento, nessun salvataggio) | — | «●» non si azzera mai da solo. |
| M9-05 schede per contesto | PASS | **PASS** (Assieme, Parte, Lamiera) | — | |
| M9-06 InputMap | PASS | **PASS** (46 righe attive, 14 mute; stati Rest, SketchOpen, ArmedOrHandle, Keypad) | — | Trigger/Grip/Zoom/schede/due mani non ancora filtrati dall'InputMap; B non passa dal dispatcher. |
| M9-07 legenda 3D | PASS (logica) | **PASS** (etichette = `InputMap.Active`, ≤150 ms, anellini, interruttore Vista) | **aperto** | Gli screenshot non inquadrano le etichette sui controller: posizione e leggibilità non verificate. |
| M9-08 `face_feature` | PASS | — | **parziale** | Sonda live PASS su estrusione, raccordo, smusso, foro + caso con espressione, revisione invariata (vedi sotto). Rivoluzione, serie, flangia non provate; comando resta sperimentale. |
| M9-09 modifica feature da faccia | PASS | **PASS** (chip 10→12 mm, un solo `set_parameter`, Applica, riletto da Inventor = 12 mm, Undo XR); lamiera: tipo fuori tabella con «Modifica dal desktop» | — | **NOT COVERED**: maniglia sulla geometria, evidenziazione di tutte le facce, caso con espressione/soppressa (solo unitario). «Feature precedente» mostra solo il nome. |
| M9-10 voce per contesto | PASS | **PASS** (comandi di spazio rifiutati con spiegazione, «apri <componente>», «torna», Applica non esegue; testo iniettato) | aperto (microfono) | |
| M9-11 regressioni M1–M8 | PASS (suite) | **non eseguito** | — | Runner M1–M7 migrati ma mai rieseguiti sul Quest. |
| M9-12 prova fisica da seduto | — | — | **aperto** | |

## Runner annidato M9N — 5 ottobre 2026 (input sintetico, fixture `m9n`, Inventor 2027 reale)

Scenario: Assieme3 contiene Assieme1 e Assieme2. Esito: **`PASS COMPLETE`**
(`artifacts/m9n-verification/device/quest-acceptance-run-20261005-142207.json`).
Verificato: selezione di un corpo di Assieme1 → occorrenza diretta del sottoassieme
(nessun errore); doppio Trigger → Assieme3 › Assieme1; doppio Trigger sulla parte →
Assieme3 › Assieme1 › PartA in Progettazione; modifica 10→12 mm riletta da Inventor e
annullata; Torna (X tenuto) e Torna (scheda Documento) fino ad Assieme3, che elenca di
nuovo i due sottoassiemi; Assieme3 › Assieme2 e ritorno con revisione visuale invariata;
fantasma sempre e solo del padre diretto. Chiude il vecchio NOT COVERED
`M9-02-subassembly`. Resta aperto: sottoassieme la cui finestra di definizione non è
aperta in Inventor (`Document.Activate()` non verificato) e la prova fisica.

Difetti trovati con questo percorso e corretti: (1) selezione/doppio Trigger su un
sottoassieme: il raggio dava l'id della parte foglia annidata invece dell'occorrenza
diretta (backend: «Select a direct occurrence…»), ora risolto con
`SceneGraph.TopLevelOccurrenceId`; il fantasma non ridisegna più le parti del livello
appena entrato. (2) Il marcatore «●» usava `Revision`, che sale anche all'attivazione
di un documento: navigare lo accendeva senza modifiche. Ora usa `VisualRevision`
(geometria/struttura) e si spegne con XR Undo.

## Runner flessibile M9F — 5 ottobre 2026 (input sintetico, fixture `m9f`, Inventor 2027 reale)

Struttura ricostruita da quella dell'assieme reale dell'utente (APE-A-0001: sottoassieme
fissato + sottoassieme libero **flessibile** che ne contiene un altro flessibile, scena
estesa ~5 m). Esito: **`PASS COMPLETE`**
(`artifacts/m9f-verification/device/quest-acceptance-run-20261005-150941.json`).
Verificato: testo di selezione del flessibile in italiano («Sottoassieme flessibile: Sposta e
Vincola non sono disponibili. Doppio Trigger (o Apri) per entrare…», nessun «flexible»
inglese), Sposta/Vincola/Giunto/Isola disabilitati con motivo e Apri abilitato; raggio su un
corpo a 5 m selezionato; ingresso Robot › AsmFlex › AsmFlexInner › PartX con fantasma del
solo padre diretto; modifica e Torna ×3 fino a Robot con revisione visuale invariata.
Causa del messaggio visto dall'utente: nessun blocco dell'ingresso (il codice non legge mai
la flessibilità dell'occorrenza); ingannava il testo grezzo. Corretto anche «Apri» dall'anello,
che attivava il documento senza aggiungere un livello alla pila.
Aperto: vincolo su sottoassieme flessibile (la fixture non ne ha), sottoassieme con finestra
di definizione non aperta, prova fisica sul robot reale.

## Definizioni senza finestra — difetto trovato dal vivo (5 ottobre 2026)

Nell'assieme reale dell'utente solo l'assieme di livello superiore ha una finestra; le altre ~100 definizioni
sono **caricate ma senza finestra** (`Documents.VisibleDocuments` = 1). `Document.Activate()` su un documento senza
finestra lancia `COMException 0x80004005` (E_FAIL): l'audit mostrava `inventor_activate_open_document_xr` ->
`API_ERROR` in 3-5 ms per APE-A-0112. Le fixture tenevano ogni definizione aperta *con* finestra, per questo
nessuna corsa lo aveva visto. Correzione (software, **non ancora provata dal vivo**): il gestore, dopo le guardie,
prova `Activate()`; su E_FAIL (o se il documento attivo non e quello richiesto) mostra la finestra del documento
gia caricato con `Documents.Open(FullFileName, true)` (nessun caricamento da disco, nessun salvataggio) e riattiva;
senza nome di file mantiene l'errore con messaggio chiaro; conferma finale `ACTIVATE_NOT_CONFIRMED` e guardie di
transazione invariate. Logica di decisione pura in `DocumentActivationPolicy` (test unitari). Lato XR un `API_ERROR`
dell'attivazione mostra «Inventor non riesce ad attivare il documento. Aprilo in una finestra in Inventor e riprova.»
con codice e testo originale nel dettaglio (`ActivationErrors`). Fixture `m9h` e runner `M9HiddenQuestAcceptance`
(`scripts/run-m9-hidden-acceptance.ps1`) riproducono la situazione: **non ancora eseguiti**. Aperto: corsa del
runner sul Quest, ricarica dell'add-in e prova sull'assieme reale (che il `Documents.Open(.., true)` su un documento
gia caricato restituisca lo stesso documento senza ricaricarlo e senza sporcarlo non e verificato senza Inventor).

## Runner definizioni senza finestra M9H — 6 ottobre 2026 (input sintetico, fixture `m9h`, Inventor 2027 reale)

Riproduce il caso dell'assieme reale dell'utente: solo l'assieme principale ha una finestra,
tutte le definizioni sono caricate **senza finestra** (`Documents.VisibleDocuments` = 1; ispezione
nativa prima del run: `has_window=false` per AsmFixed e le altre). Prima della correzione
`inventor_activate_open_document_xr` falliva con `API_ERROR` (E_FAIL 0x80004005) subito
(3–5 ms) perché `Document.Activate()` non attiva un documento senza finestra. Dopo la
correzione (`Documents.Open(FullFileName, visible)` come ripiego) esito **`PASS COMPLETE`**
(`artifacts/m9h-verification/device/quest-acceptance-run-20261006-105640.json`): ingresso in
AsmFlex, AsmFlexInner, PartX, AsmFixed e AsmInner e ritorno con Torna ×3.
Resta non provato: sull'assieme reale (APE-A-0001, 103 documenti) e il vincolo su un
sottoassieme flessibile; l'effetto «mostra la finestra» sui documenti entrati è voluto.

## Sonda live `face_feature` — 5 ottobre 2026

`dotnet run --project bridge/tests/M3LiveProbe -- --face-feature` contro Inventor
2027 (add-in costruito con `SoExperimental=true`): **PASS** per estrusione,
raccordo, smusso, foro e caso con espressione (non modificabile, espressione
restituita); revisione invariata. La prima esecuzione ha trovato **due difetti
del gestore**, corretti: raccordo e smusso leggevano API inesistenti
(`FilletFeature.FilletDefinition.EdgeSetItem`, `ChamferFeature.Definition.Distance*`)
e non restituivano raggio/distanza; angolo della flangia ora da `FlangeAngle`
(non provato). Aggiunto un ripiego per le facce senza `CreatedByFeature`
(pareti del foro).

## Difetti di prodotto trovati dal runner (corretti)

- **Rilevamento lamiera**: entrando in una parte, il documento cambia prima
  della scena (con `_kind` ancora quello del documento precedente), la
  lettura «è lamiera?» veniva saltata e Lamiera restava con la lettura della
  parte precedente: il contesto restava «Parte». Corretto con
  `LamieraWorkspace.EnsureSheetContext`.
- Il runner ora attende che l'assieme sia libero e ripete la prima pressione
  sintetica; attende anche la lista dei componenti dopo un refresh.

## Sottoassiemi: difetti trovati leggendo il codice (5 ottobre 2026) — corretti, da verificare sul Quest

Segnalazione dell'utente: il doppio Trigger su un sottoassieme di un assieme complesso non lo apre (un errore alla selezione).

- **Causa 1 (selezione e ingresso)**: la scena disegna solo le parti foglia (`SceneGraph.PlacedParts`), quindi `CadBody.Instance.OccurrenceId`
  e l'id della foglia annidata (proxy `occurrence_proxy`), non quello dell'occorrenza diretta del sottoassieme. `AssemblyWorkspace.PickBodyAsync`
  lo passava a `get_assembly_context_xr`, che rifiuta ogni occorrenza non diretta («Select a direct occurrence or activate its subassembly
  first», o `REFERENCE_TYPE_MISMATCH` per l'id proxy): messaggio d'errore sul HUD e nessuna selezione. In `OnPenPressed` il bersaglio del
  rilevatore (foglia) non coincideva mai con `_occurrence.Id`, quindi il doppio Trigger rispondeva «Seleziona prima un componente» /
  «Selezione in corso». Correzione: `SceneGraph.TopLevelOccurrenceId` mappa la foglia sull'occorrenza diretta; usata in `PickBodyAsync`,
  `OnPenPressed` (bersaglio del doppio Trigger) e `HitMoveTarget`. Una parte diretta resta invariata.
- **Causa 2 (fantasma)**: `GhostContext.Show` saltava l'occorrenza appena aperta confrontando l'id della foglia; per un sottoassieme non
  coincideva mai e il fantasma ridisegnava le sue parti sopra quelle reali. Correzione: `SceneGraph.OccurrenceAndDescendantIds`.
- **Non corretto, da sapere**: «Isola» su un sottoassieme (ora disabilitato con motivo, vedi sotto) diceva «Il componente non è nella scena» (`ComponentIsolation` lavora su un'istanza
  foglia); `Sposta` su un sottoassieme non ha istanza da evidenziare. `activate_open_document_xr` richiede che la definizione del
  sottoassieme sia nell'elenco dei documenti aperti: se Inventor la tiene solo come riferimento invisibile, `Document.Activate()` potrebbe non
  portarla in primo piano: **non verificato**, serve Inventor (la fixture `m9n` tiene le definizioni visibili come la M6).
- Test: `SceneGraph` (core, `DtoTests`), `AssemblyWorkspaceTests` (doppio Trigger e selezione su un corpo di sottoassieme con un backend che
  rifiuta gli id annidati come il bridge), `GhostContextTests`. Il runner annidato (`M9NestedQuestAcceptance`) riproduce lo scenario e fallisce se la
  selezione non risolve all'occorrenza diretta. Esito sul Quest: **non eseguito**.

## Sottoassiemi flessibili (5 ottobre 2026) — corretto il testo, ingresso invariato, da verificare sul Quest

Segnalazione dell'utente: sul suo assieme reale (APE-A-0001, sottoassieme APE-A-0112 flessibile, non a terra) il doppio Trigger mostra un testo sui
sottoassiemi FLESSIBILI e «attivare i figli», e non sembra entrare come in un sottoassieme normale.

Lettura del codice (nessun Inventor): **nessun blocco dell'ingresso dovuto alla flessibilita**. `EntryBlockedReason` e `ActivateDefinitionAsync` usano `Editable`
del *workspace* (lettura del contesto, online, scena aggiornata), mai `AssemblyOccurrence.Editable` dell'occorrenza; `activate_open_document_xr` attiva il
documento di definizione e non guarda `Flexible`; il ring «Apri» e `CanOpenDefinition` non dipendono dall'occorrenza. Il bridge marca `editable=false`
e `unavailable_reason=flexible` solo per Sposta/Vincola (`AssemblyBatchHandler` li rifiuta). Quello che l'utente vedeva era il testo: «Componente non
modificabile: flexible» (inglese grezzo) seguito da «Attivare la definizione per modificarne i figli», che suggeriva un blocco. Inoltre il ring «Apri»
su un sottoassieme chiamava `ActivateSubassemblyCore(false)` (attiva il documento ma **non** annuncia l'ingresso al router, quindi nessun nuovo livello
nella pila), a differenza del doppio Trigger.

Correzioni: testi italiani espliciti (`OccurrenceSummary`: «Sottoassieme flessibile: Sposta e Vincola non sono disponibili.» e «Doppio Trigger (o Apri) per
entrare e modificarne i componenti; le modifiche riguardano tutte le istanze.»; motivi tradotti flessibile/adattivo/soppresso/virtuale); «Apri» su un
sottoassieme ora entra come il doppio Trigger (nuovo livello); Sposta, Vincola e Giunto sono disabilitati con motivo quando la selezione non e modificabile come
unita (cambiando selezione tornano attivi); «Isola» su un sottoassieme e disabilitato con motivo («Isola vale per un singolo pezzo: per un sottoassieme usa
Apri»), invece del vecchio «Il componente non e nella scena». `AssemblyOccurrence.Flexible` esposto dal core. Test: `AssemblyTests` (core),
`DoubleTriggerEntryTests` (backend finto con occorrenza flessibile: editable=false, flexible=true, reason flexible), contratto `QuestAcceptanceContractTests`.

Runner `M9FlexQuestAcceptance` (fixture `m9f`, `scripts/run-m9-flex-acceptance.ps1`): vedi `docs/xr-quest-acceptance.md`. Esito sul Quest: **non eseguito**.
Resta aperto: un vincolo di assieme su un sottoassieme flessibile, una definizione non aperta in Inventor e la prova fisica.

## Limiti noti da chiudere

- Il runner M9 su fixture M6 terminerà `PARTIAL` per costruzione (nessun
  sottoassieme, nessuna maniglia, nessun elenco facce): serve una fixture con
  assieme annidato.
- `NavLevel.Context` resta `Part` quando il rilevamento lamiera sposta il
  workspace su Lamiera; il runner controlla `App.Context`.
- Su una parte lamiera non c'è più un modo di iniziare uno schizzo da visore
  (la vecchia azione Modello 3D/Schizzo è stata rimossa con la scelta degli
  spazi): da prevedere in una fase successiva.
- `face_feature`: `previous_feature` è la feature precedente nell'elenco della
  parte (non lo schizzo del profilo, diversamente dall'esempio della spec);
  `UNSUPPORTED_FEATURE` è restituito come errore con nome/tipo in
  `error.details`.

## Come chiudere i gate aperti

1. Fixture con **assieme annidato** e geometria con espressione/feature
   soppressa; collegare la maniglia della modifica feature e l'elenco facce
   in `face_feature` → chiude i NOT COVERED di M9-02/M9-09.
2. Sonda live: aggiungere rivoluzione, serie e flangia → chiude M9-08.
3. Rieseguire i runner M1–M7 migrati sul Quest → M9-11.
4. Prova fisica da seduto → M9-12 e posizione/leggibilità della legenda (M9-07).

Nota operativa: sulla postazione manca PowerShell 7; per eseguire gli script
`run-*-acceptance.ps1` servono copie senza `#requires` e con
`$ErrorActionPreference='Continue'` (adb scrive su stderr). Il Quest va tenuto
sveglio e l'app in primo piano; PC e visore devono stare sulla stessa rete
(cavo scollegato = IP diverso = pairing non funzionante).
