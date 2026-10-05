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
