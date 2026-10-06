# Inventor XR SO — verifica M7 (Ispeziona, verifica ingegneristica)

Spec: [M7](superpowers/specs/2026-10-03-inventor-xr-so-m7-inspect-verifica-design.md).
Piano: [M7](superpowers/plans/2026-10-03-inventor-xr-so-m7-inspect-verifica.md).

## Stato dei gate

| Gate | Esito | Evidenza |
|---|---|---|
| M7-01 | PASS EditMode + runner Quest sintetico | Manifest `quest-acceptance-run-20261004-145650.json`: nascondi, X-Ray, isola, mostra tutto e raycast |
| M7-02 | PASS runner Quest + Inventor reale | Stesso manifest: coppia M7_A/M7_B, 2000 mm³, 1 box, focus riga e ritorno; 101 ms |
| M7-03 | PASS runner Quest + Inventor reale | Stesso manifest: 30 mm, linea dai punti forniti da Inventor |
| M7-04 | PASS runner Quest + Inventor reale | Stesso manifest: `M7_Sick` e focus della riga, solo M7_D libero, `DESCRIPTION_MISSING`, 3 righe |
| M7-05 | PASS core/FakeAddIn + runner Quest sintetico | Stesso manifest: revisione sintetica → Stale, rifiuto concorrente, risposta ignorata scartata; cambio CAD reale non esercitato sul visore |
| M7-06 | PASS EditMode | `InspectVerifyTests` 17/17, incluse abilitazione offline/su parte ed etichette delle misure |
| M7-07 | PASS fixture; APERTO assieme reale e limite 30 s dal vivo | Manifest: interferenza 101 ms; durata salute e assieme reale non misurate dal runner, timeout coperto dai test core |
| M7-08 | APERTO (prova fisica) | |

## Runner Quest — 4 ottobre 2026, 14:57 Europe/Rome

**PASS COMPLETE**, input sintetico tramite il catalogo reale dell'app, backend
Inventor reale su HTTPS pinnato. Manifest e log in `artifacts/m7-verification/`:
`quest-acceptance-run-20261004-145650.json` e
`quest-acceptance-20261004-145650-m7-acceptance.txt`; tre screenshot (`interference`,
`distance`, `health-focus`) con lo stesso prefisso. Il visore era `Awake` e il
runner è partito regolarmente, senza timeout.

Tutti i sottocasi M7-01…M7-05 previsti dal runner sono passati. M7-05 usa una
revisione sintetica sul client: non è una prova di modifica desktop reale.
Il runner dichiara `NOT COVERED` M7-06 (offline/parte, verificati in EditMode),
M7-07 su assieme reale e M7-08 fisico. Nessun PASS del runner certifica
leggibilità, ergonomia o tracking fisico.

Ispezione nativa finale: fixture pulita (`dirty=false`), quattro occorrenze e
valori attesi invariati. Fixture chiusa senza salvare; documento precedente
assente. APK ordinario reinstallato e hash verificato dallo script. Nessuna
promozione dei tool dal tier sperimentale effettuata in questa esecuzione.

## Finalizzazione Windows — 4 ottobre 2026

Il branch `feat/m7-inspect-verifica` è già integrato in `main` (merge `5b849b0`).
Suite backend: **984 PASS, 1 SKIP, 0 FAIL** (`m7-backend.trx`). Il test di processo
pairing è saltato perché l'host gestito dell'utente è già in esecuzione. Il primo
tentativo aveva un fallimento di configurazione: `INVENTOR_SO_EXPERIMENTAL=1`
ereditata dalla shell contraddice il test del default disabilitato. Riesecuzione
con la variabile rimossa solo dal processo dei test: nessun fallimento.
Core XR: **467/467** (`m7-core.trx`). Evidenza EditMode disponibile della stessa
giornata: **407/407** (`editmode.xml`, 12:31 ora locale), inclusi i contratti dei
runner 13/13. Nessuna modifica al codice dopo questa esecuzione.

Fixture rigenerata con `bom_finding = DESCRIPTION_MISSING`. La sonda MCP su PC
(`live-smoke.py`, `live-smoke.json`) usa il server stdio e l'add-in Inventor reale:
tutti e tre i tool risultano esposti e lo sperimentale è abilitato nell'add-in.
Confermati interferenza 2000 mm³ (45 ms), distanza 30 mm con punti Inventor,
vincolo non sano, componente libero e avviso BOM. Identità e revisione prima e
dopo le letture sono identiche. Questa evidenza non certifica il percorso HTTPS
né il runner Quest. I tool restano sperimentali.

APK QA e ordinario compilati con successo dalla stessa revisione. File:
`artifacts/InventorXrSo-m7-acceptance.apk` e `artifacts/InventorXrSo-m7.apk`;
hash, risultati e stato raccolti in `artifacts/m7-verification/readiness.json`,
log in `acceptance-build.log` e `ordinary-build.log`. L'APK ordinario è stato
installato sul Quest `2G0YC1ZFB407P1` e il suo SHA-256 verificato sul dispositivo.
La fixture PC è stata chiusa senza salvare; non c'erano documenti precedenti
da riattivare. Runner Quest **NOT RUN**: visore in standby (`Asleep`).

Avvio preparato (PowerShell 7 disponibile nel runtime Codex; ADB configurato
dal wrapper), dalla radice del repository con il Quest indossato e sveglio:

```powershell
& "$env:USERPROFILE/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe" -NoProfile -File artifacts/m7-verification/run-m7.ps1
```

Il wrapper prepara una fixture nuova, esegue il runner e ispeziona il risultato;
nel `finally` ripristina fixture e APK ordinario. Manifest, log e screenshot del
runner vanno in `artifacts/m7-verification/`. Servono host HTTPS sperimentale
già avviato e Quest associato; nessun pairing nuovo è stato effettuato qui.

Prova fisica ridotta M7-08: con il visore addosso, da seduto, verificare la
leggibilità del rosso delle interferenze, dei fantasmi, dei box, della linea
della distanza e della lista Risultati; verificare la selezione dei componenti
con il controller e il comfort di lettura.

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

### Revisione finale: verifica BOM (DESCRIPTION_MISSING)

Il gate M7-04 non cerca piu' `PART_NUMBER_MISSING`. `get_assembly_bom` (stabile, non modificato da M7) sostituisce un numero di
parte vuoto con il nome del file: su file reali `PART_NUMBER_MISSING` non puo' comparire. La fixture azzera quindi la
`Description` del cubo (manifest `bom_finding = DESCRIPTION_MISSING`), FakeAddIn rispecchia l'handler reale (riga `Bolt` con
descrizione vuota) e il runner controlla `DESCRIPTION_MISSING` (avviso: `BomValid` resta vero). La fixture va rigenerata
(`prepare m7`) prima del prossimo run sul Quest: i documenti preparati prima di questa modifica hanno ancora la Description.
Il runner apre inoltre la riga `M7_Sick` di Risultati e controlla visibilita' (M7_B e M7_C normali, gli altri due fantasma).

### Vista di revisione interferenze: X-Ray globale e pulsazione (2026-10-06)

Al termine di **Interferenze** (con almeno un risultato) il visore applica da solo la vista di revisione: tutto l'assieme in
X-Ray, le parti in interferenza rosse e i box rossi, con pulsazione per 5 s (1,2 Hz, profondita' 0,6), poi rosso fisso.
Salute non la applica; la riga di Risultati restringe il focus come prima e riavvia la pulsazione. Indietro / Mostra tutto
ripristinano la vista (stesso snapshot di `ClearFocus`). La fase e' calcolata in C# (`_PulseWave`) cosi' tinta e box restano
sincroni.

- `PASS` test EditMode (636/636 su Unity 6000.6.3f1): vista globale applicata e ripristinata, 0 interferenze senza vista, Salute senza vista, pulsazione spenta dopo 5 s, materiale ghost invariato.
- `PASS [M7-xray-pulse]` nel runner Quest (2026-10-06, `quest-acceptance-run-20261006-191023.json`, input sintetico): vista globale con X-Ray, tinta rossa, 1 box e pulsazione avviata dopo il risultato; `PASS [M7-01]` e `PASS [M7-02]` (riga di Risultati e Indietro) nello stesso run.
- Difetto trovato dal runner e corretto: nell'Assieme (M9) `InspectWorkspace.Back()` non ripristinava la vista di verifica (la ramificazione `ViewOwnedElsewhere` saltava `ClearFocus`). Test EditMode dedicato.
- Causa del `FAIL` in M7-03 e fix: il backend (`AssemblyContextXrHandler`) con `occurrence_id` restituisce solo quell'occorrenza e `AssemblyWorkspace.LoadAsync` sostituiva l'intero contesto, quindi dopo una selezione l'elenco Componenti (e la ricerca vocale per nome) vedeva un solo componente. Ora l'elenco completo viene conservato (`2cbd690`, 3 test EditMode, 655/655 verdi). Difetto reale di M9, non solo del runner.
- **Run M7 `PASS COMPLETE`** (2026-10-06 19:50 Europe/Rome, `quest-acceptance-run-20261006-195026.json`, input sintetico): M7-01, M7-xray-pulse, M7-02, M7-03 (30 mm, punti Inventor), M7-04, M7-05, M7-07 (solo fixture). Restano `NOT COVERED`: M7-xray-pulse-visual, M7-06 (EditMode), M7-07 su assieme reale (`--probe-active`), M7-08 (prova fisica da seduto).