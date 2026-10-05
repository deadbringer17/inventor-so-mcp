# M9 navigazione per contesto — stato e verifica

Data: 5 ottobre 2026, Europe/Rome.
Spec: [M9 navigazione per contesto](superpowers/specs/2026-10-04-m9-navigazione-contesto-design.md).
Piano: [M9](superpowers/plans/2026-10-04-m9-navigazione-contesto.md).

**Stato:** implementazione software presente sul branch
`feat/m9-navigazione-contesto`. **Nessuna prova sul Quest, nessuna prova con
Inventor reale, nessuna prova fisica è stata eseguita.** I gate che le
richiedono restano aperti.

## Test automatici locali — 5 ottobre 2026

| Suite | Esito |
|---|---|
| Core XR .NET / FakeAddIn (`XrSo.Core.Tests`) | PASS 584/584 |
| Backend `Bimwright.Ipt.Tests` (con `INVENTOR_SO_EXPERIMENTAL` non impostata) | PASS 1041, 1 skipped |
| Unity EditMode | PASS 559/559 |
| Compilazione di tutti i runner con `XR_SO_ACCEPTANCE` | PASS (nessun CS) |

Con `INVENTOR_SO_EXPERIMENTAL=1` nella shell `TierPolicyTests.ExperimentalFlagLoadsFromCliAndEnvironment`
fallisce per l'ambiente, non per M9.

## Esito per gate

Tre esiti separati: **U** = test unitario/FakeAddIn/EditMode, **R** = runner
Quest con input sintetico, **F** = prova fisica/Inventor reale.

| ID | U | R | F | Note |
|---|---|---|---|---|
| M9-01 contesto dal documento, scheda Documento | PASS | non eseguito | — | Il «Salva» del visore non salva: HUD «Salva dal desktop» (nessun tool di salvataggio lato client). |
| M9-02 doppio Trigger su componente / sottoassieme / guardie | PASS | non eseguito | — | Sottoassieme a due livelli: la fixture M6 non ne ha uno, NOT COVERED nel runner. |
| M9-03 assieme fantasma | PASS | non eseguito | aperto | Screenshot assenti. Il fantasma non si aggiorna mentre si modifica la parte. |
| M9-04 Torna (X tenuto / scheda), «●» | PASS | non eseguito | — | «●» non si azzera da solo: nessun salvataggio dal visore; il codice di cattura errore «parent chiuso» dipende da `McpToolException`, non verificato con l'errore reale. |
| M9-05 schede per contesto, gruppo Ispeziona, Vista unica, Ispeziona trasversale | PASS | non eseguito | — | Scostamenti dalla spec nel verbale del task 10 (scheda «opzioni» Assieme, scheda «esplora» senza contesto). |
| M9-06 InputMap unica fonte | PASS (dispatcher) | non eseguito | — | Trigger, Grip, Zoom, schede e due mani non sono ancora filtrati dall'InputMap (zoom e schede agiscono anche con tastierino aperto). |
| M9-07 legenda 3D | PASS (logica) | non eseguito | **aperto** | Posizioni delle etichette approssimate: serve verifica sul Quest e screenshot. |
| M9-08 `face_feature` | PASS (modello, FakeAddIn, contratto) | — | **aperto** | Handler add-in non compilato né eseguito (richiede Inventor 2027 e build sperimentale). Sonda `bridge/tests/M3LiveProbe --face-feature` scritta, non eseguita. Tier sperimentale mantenuto. Proprietà `dynamic` per ogni tipo da confermare dal vivo. |
| M9-09 modifica feature da faccia | PASS (chip, batch atomico, espressioni, soppressa, STALE) | non eseguito | **aperto** | Maniglia (distanza/flangia) **non collegata**; evidenziata solo la faccia scelta; «Feature precedente» mostra solo il nome. |
| M9-10 voce per contesto | PASS | non eseguito | — | `OpenByName` asincrono senza test EditMode. Comandi di spazio rifiutati con spiegazione; niente Applica a voce. |
| M9-11 regressioni M1–M8 | PASS (suite) | **non eseguito** | — | Runner M1–M7 migrati ma mai rieseguiti sul Quest. |
| M9-12 prova fisica da seduto | — | — | **aperto** | Scoperta del doppio Trigger e di Torna, leggibilità della legenda, nessun Torna involontario in 30 minuti. |

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

1. Sonda live: `bridge/tests/M3LiveProbe` con `--face-feature` contro Inventor
   2027 (build sperimentale) → M9-08.
2. Runner Quest: `scripts/run-m9-acceptance.ps1` con fixture dedicata
   (vedi [accettazione Quest](xr-quest-acceptance.md)); registrare log,
   manifest, screenshot e ripristino APK/documento → M9-01…07, 09, 10, 11.
3. Prova fisica da seduto → M9-12 e leggibilità M9-07.
