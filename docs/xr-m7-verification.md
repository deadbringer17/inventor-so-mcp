# Inventor XR SO — verifica M7 (Ispeziona, verifica ingegneristica)

Spec: [M7](superpowers/specs/2026-10-03-inventor-xr-so-m7-inspect-verifica-design.md).
Piano: [M7](superpowers/plans/2026-10-03-inventor-xr-so-m7-inspect-verifica.md).

## Stato dei gate

| Gate | Esito | Evidenza |
|---|---|---|
| M7-01 | APERTO | |
| M7-02 | APERTO | |
| M7-03 | APERTO | |
| M7-04 | APERTO | |
| M7-05 | APERTO | |
| M7-06 | APERTO | |
| M7-07 | APERTO | |
| M7-08 | APERTO (prova fisica) | |

## Sonde dal vivo (task 1)

Data: 3 ottobre 2026. Inventor 2027 (add-in `plugin-so27`), PC di sviluppo.
Fixture `XR_M7_Quest_Acceptance.iam` (4 cubi da 20 mm di `XR_M7_Quest_Acceptance_Cube.ipt`, in `%TEMP%`),
creata con `--prepare-quest m7`, chiusa senza salvare con `--restore-quest m7` (documento precedente riattivato).
Comandi: `dotnet run --project bridge/tests/QuestAcceptanceFixtures -- <argomenti>`. Le chiamate di interop
(`InferredTypeEnum`, `context.Name[i]`, `AnalyzeInterference(set1, set2)`) compilano così come scritte: nessun ricorso a `dynamic`.
Il vincolo `M7_Sick` e' risultato non sano al primo tentativo (offset 5 mm): il ripiego con offset 0 non e' servito.

### `--inspect-quest m7`

```json
{"milestone":"m7","document":"...\\XR_M7_Quest_Acceptance.iam","dirty":false,"occurrences":[{"name":"M7_A","x_mm":0.0,"y_mm":0.0,"z_mm":0.0,"grounded":true},{"name":"M7_B","x_mm":15.0,"y_mm":0.0,"z_mm":0.0,"grounded":true},{"name":"M7_C","x_mm":0.0,"y_mm":50.0,"z_mm":0.0,"grounded":true},{"name":"M7_D","x_mm":100.0,"y_mm":0.0,"z_mm":0.0,"grounded":false}],"volume_mm3":8000.0,"fixture_documents":2,"interference_bodies":1,"interference_volume_mm3":2000.0,"distance_a_c_mm":30.0,"constraints":[{"name":"M7_Sick","health":"kInconsistentHealth"}]}
```

Valori attesi confermati: interferenza 2000 mm³ (1 corpo), distanza `M7_A`–`M7_C` 30 mm, `M7_Sick` in `kInconsistentHealth`.

### `--probe-m7`

```json
{
  "interference_ms": 3,
  "interference": [
    { "a": "M7_B", "b": "M7_A", "volume_mm3": 2000.0, "min_mm": [15.0, 0.0, 0.0], "max_mm": [20.0, 20.0, 20.0] }
  ],
  "two_sets_count": 1,
  "distance_mm": 30.0,
  "distance_context": {
    "IntersectionFound": "False",
    "ClosestPointOne": [0.0, 20.0, 0.0],
    "ClosestPointTwo": [0.0, 50.0, 0.0],
    "ClosestEntityOne": "System.__ComObject",
    "ClosestEntityTwo": "System.__ComObject"
  },
  "constraints": [ { "name": "M7_Sick", "health": "kInconsistentHealth", "occurrence_one": "M7_B", "occurrence_two": "M7_C" } ]
}
```

(JSON compattato per leggibilita': i valori sono identici all'output.) Nota: la coppia dell'interferenza e' riportata
come `a = M7_B`, `b = M7_A` (ordine non garantito: il codice di produzione non deve dipendere dall'ordine).

### `--probe-active`

`NOT COVERED [M7-07] assieme reale non misurato: --probe-active richiede un assieme dell'utente, rinviato al collaudo (task 11)`

### Esito per rischio

- Durata: 3 ms su fixture, non misurata su assieme reale (4 occorrenze sulla fixture; assieme reale rinviato, vedi sopra).
- Box: leggibili sì (`InterferenceBody.RangeBox` restituisce min/max in cm, convertiti in mm: [15,0,0]–[20,20,20]).
- Punti distanza: chiavi `ClosestPointOne` / `ClosestPointTwo` (Point in cm), piu' `IntersectionFound`, `ClosestEntityOne`, `ClosestEntityTwo`. Coincidono con i default del codice.
- Occorrenze del vincolo: sì (`OccurrenceOne` = `M7_B`, `OccurrenceTwo` = `M7_C` leggibili su `M7_Sick`).
- Due insiemi: sì (`AnalyzeInterference(set1, set2)` funziona; `M7_B` contro gli altri: 1 corpo).
