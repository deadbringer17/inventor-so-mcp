# Piano — runner di accettazione Quest per M1, M2, M3 e M5

Data: 29 settembre 2026. Obiettivo: estendere a M1, M2, M3 e M5 lo standard di
test automatico sul Quest 3 introdotto con M4
(`Inventor XR SO/Assets/XrSo/Xr/M4QuestAcceptance.cs`, fixture
`bridge/tests/M4LiveProbe/QuestFixture.cs`, evidenze in `docs/xr-m4-collaudo.md`).

## Lo standard (derivato da M4)

1. **Runner in-process opt-in.** Un `MonoBehaviour` per milestone, compilato solo
   con il define `XR_SO_ACCEPTANCE` (`XrSoBuild.BuildAcceptanceApkBatch`),
   avviato solo dall'extra Android booleano `xr_mN_acceptance=true`. L'APK
   ordinario (`BuildApkBatch`) non lo contiene.
2. **Fixture dedicata.** Il runner lavora solo se il documento attivo inizia con
   `XR_MN_Quest_Acceptance`; lo verifica prima di ogni mutazione. La fixture è
   creata in Inventor da un comando `--prepare-quest` e chiusa senza salvare da
   `--restore-quest`, che riattiva il documento precedente. Manifest in
   `artifacts/mN-verification/quest-fixture.json` con percorsi, documento
   precedente e valori attesi.
3. **Percorso reale.** Il runner usa i workspace, la `DesignSession` e il backend
   dell'app così come li ha costruiti `AppController` (campi privati letti per
   riflessione), quindi attraversa TLS pinnato, MCP, add-in e Inventor veri.
4. **Evidenza.** Log `mN-acceptance.txt` e screenshot `mN-acceptance-*.png` in
   `Application.persistentDataPath`. Righe
   `<UTC ISO> [MNQuest] PASS [<gate>] <messaggio>`; ultima riga
   `PASS COMPLETE; ...` oppure `FAIL; <eccezione>`. Timeout globale.
5. **Onestà.** Il runner non esercita i controller fisici, la leggibilità, il
   tracking, il microfono né l'audio: l'ultima riga lo dichiara e i gate fisici
   restano aperti nei documenti di verifica.

## Componenti

| # | Componente | File |
|---|---|---|
| A | Base comune `QuestAcceptanceRunner` (intent, log, check, attese, riflessione, screenshot) e M4 riportato sulla base senza cambiarne i passi | `Assets/XrSo/Xr/Acceptance/QuestAcceptanceRunner.cs`, `Assets/XrSo/Xr/M4QuestAcceptance.cs` |
| A | Orchestrazione ADB: install, verifica hash, avvio con extra, attesa esito, pull di log/screenshot, manifest, reinstallazione facoltativa dell'APK ordinario | `scripts/run-quest-acceptance.ps1` |
| B | Runner M1 e M2 | `Assets/XrSo/Xr/Acceptance/M1QuestAcceptance.cs`, `M2QuestAcceptance.cs` |
| C | Runner M3 | `Assets/XrSo/Xr/Acceptance/M3QuestAcceptance.cs` |
| D | Runner M5 | `Assets/XrSo/Xr/Acceptance/M5QuestAcceptance.cs` |
| E | Fixture Inventor M1, M2, M3, M5 | `bridge/tests/QuestAcceptanceFixtures/` |
| F | Test EditMode di contratto: ogni campo privato letto dai runner esiste con il tipo atteso | `Assets/XrSo/Tests/EditMode/QuestAcceptanceContractTests.cs` |

Ogni nuovo file o cartella sotto `Assets/` ha il proprio `.meta` con GUID nuovo.

## Fixture

| Milestone | Documento attivo | Contenuto | Valori attesi |
|---|---|---|---|
| M1 | `XR_M1_Quest_Acceptance.iam` | due occurrence di `XR_M1_Quest_Block.ipt` (blocco 100 × 60 × 20 mm), la seconda traslata di 150 mm in X, prima grounded | 2 occurrence, luce 50 mm, lato lungo 100 mm |
| M2 | `XR_M2_Quest_Acceptance.iam` | come M1; anche la parte `XR_M2_Quest_Acceptance_Block.ipt` resta aperta | volume 120000 mm³, distanza tra centri 150 mm, 2 documenti fixture aperti |
| M3 | `XR_M3_Quest_Acceptance.ipt` | blocco 40 × 30 × 10 mm (schizzo `Blocco` su XY), schizzo non consumato `Base_M3` sulla faccia superiore con cerchio R 5 mm al centro | volume iniziale 12000 mm³; dopo estrusione join 20 mm di Base_M3: 12000 + π·25·20 mm³ |
| M5 | `XR_M5_Quest_Acceptance.ipt` | parte lamiera: Face 100 × 60 mm con lo spessore della regola, nessuno sviluppo esistente, schizzo non consumato `Taglio_M5` sulla faccia con un rettangolo 20 × 10 mm | parte `is_sheet_metal=true`, uno spessore, zero pieghe |

## Passi dei runner (gate coperti)

Solo passi programmabili; ogni passo scrive una riga `PASS [...]`.

**M1** (spec M1 §1, DoD 1–9): sessione Online con credenziali salvate (DoD 1–2);
Home con PC, versioni, documento e tipo (3); scene graph con 2 occurrence e mesh
caricate, estensione del blocco = 0,100 m ± 1 mm a scala 1:1 (4); pick di una
occurrence e poi di una faccia tramite `CadBody`/triangolo reale come fa
`AppController.OnPicked` (5–6); highlight locale presente e richiesta
`highlight` accettata dal backend (7); cambio MR ↔ Studio VR; screenshot.
Non coperti: DoD 8 (cambio documento fatto sul desktop), 9 (rete), pairing QR.

**M2** (§53): apertura Inspect; Browser con i due documenti fixture; selezione
occurrence → proprietà con volume 120000 mm³ ± 1; misura punto-punto tra i
centri delle due occurrence = 150 mm ± 0,5; pin misura; sezione con offset
numerico e piano visibile; Table scale ≤ 60 cm e ritorno 1:1; cambio
MR/Studio; attivazione della parte fixture dal Browser, scena seguita, poi
riattivazione dell'assieme; cambio revisione invalida misure/selezione.
Screenshot della sezione.

**M3** (§54, casi 1, 2, 9, 10, 11, 12, 15 del collaudo M3): Design sulla parte;
estrusione di `Base_M3` 20 mm join → preview visibile, revisione invariata,
screenshot; Annulla comando; nuova preview → Applica → revisione nuova;
Undo/Redo XR; preview stale rifiutata dopo Undo; foro cieco su faccia →
preview → Annulla; raccordo 1 mm valido, poi 100 mm → validazione fallita,
Applica disabilitato, poi 1 mm riabilita → Annulla; ritorno a Inspect senza
ghost né manipolatore; riapertura Design.

**M5** (M5-01, 02, 03 senza gesto, 04, 05, 06, 07 parziale, 09, 10, 11):
Lamiera primaria sulla fixture; contesto con regola/spessore e revisione;
flangia su uno spigolo reale con altezza numerica → preview → Annulla →
preview → Applica → Undo/Redo; Cut da `Taglio_M5` → preview → Annulla;
sviluppo mostrato, Detach senza chiamate backend, nessun secondo documento;
comandi vocali iniettati come testo nel router (nessun microfono):
`Applica` apre solo la conferma, comando disabilitato non muta la revisione,
dettatura aggiorna solo il campo armato. Screenshot di preview flangia e
sviluppo. Non coperti: M5-03 gesto, M5-08 push-to-talk fisico, M5-12.

## Criteri di accettazione del lavoro

- I runner compilano solo con `XR_SO_ACCEPTANCE`; nessuna modifica al
  comportamento dell'APK ordinario.
- Nessun runner muta un documento il cui nome non inizia con il prefisso.
- Il test di contratto F fallisce se un campo usato per riflessione cambia nome
  o tipo.
- I runner, lo script e le fixture sono scritti in ambiente Linux senza Unity,
  Inventor né Quest: finché non vengono compilati ed eseguiti sulla postazione,
  **nessun gate è segnato come passato** e i file di verifica lo dicono.

## Esecuzione sulla postazione

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode" -Log "$env:TEMP\xrso-editmode.log"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoBuild.BuildAcceptanceApkBatch" -Log "$env:TEMP\xrso-acceptance-build.log"
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest m3
pwsh scripts/run-quest-acceptance.ps1 -Milestone m3 -Apk "Inventor XR SO/Builds/InventorXrSo.apk" -OrdinaryApk artifacts/InventorXrSo.apk
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --restore-quest m3
```
