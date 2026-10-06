# M10 icone Inventor — stato e verifica

Data: 6 ottobre 2026, Europe/Rome.
Spec: [M10](superpowers/specs/2026-10-06-m10-icone-inventor-design.md).
Piano: [M10](superpowers/plans/2026-10-06-m10-icone-inventor.md).

**Migrazione software completata; runner Quest sintetico PASS COMPLETE.**
34 immagini native coprono 46 azioni nei sei provider: Progettazione, Lamiera,
Assieme, Ispeziona, Documento e Vista. Prova fisica MR/Studio VR confermata
dall'utente; rimane aperto il budget GPU.

| Gate | Locale | Quest sintetico | Fisico / limite |
|---|---|---|---|
| M10-01 pack/provenienza | PASS | notice/manifest offline PASS | — |
| M10-02 sprite/mapping | PASS, 34 immagini/46 id | PASS | — |
| M10-03 icona/tooltip/hit area | PASS EditMode | PASS, sei provider reali | PASS riferito dall'utente |
| M10-04 fallback/disabled/toggle | PASS | PASS, hover senza invocazione | — |
| M10-05 Progettazione/voce | PASS | regressione nativa e voce iniettata PASS | microfono escluso |
| M10-06 altri contesti | PASS | presentazione dei sei provider PASS | — |
| M10-07 regressioni/build | core/EditMode/APK PASS | regressione M6 nativa PASS | non riesegue tutti i runner M1–M9 |
| M10-08 risorse/performance | cache PASS | 100 rebuild PASS, profilo raccolto | budget completo aperto: GPU non disponibile |
| M10-09 MR/VR fisico | — | NOT COVERED | PASS riferito dall'utente, 6 ottobre 2026 |

## Pack e mapping definitivo

Estratti **8.386 PNG**: 2.681 color, 2.853 light, 2.852 dark. Manifest con
SHA-256 delle tre DLL, originali e PNG. ICO decodificati secondo la firma
BMP/PNG/ICO; due ICO dark vuoti esclusi e registrati in `skipped`, conservando
originale/hash. **Zero errori di conversione**. Risorse Autodesk, senza licenza
di redistribuzione verificata; il pack completo resta un artefatto locale.

Artefatti: `artifacts/m10-icons/pack/index.html`, `pack/manifest.json`,
`artifacts/m10-icons/inventor-2027-icons.zip`. ZIP: CRC verificato su
**16.777 voci**, SHA-256
`d042ce1bcac04e78f93062761923aa01be9d64ec3c34ce83524baf0ced1fef6b`.

Catalogo definitivo: `assets/inventor-icons/m10-catalog.json`; pilota 11 azioni
conservato come storico. Importati solo 34 PNG dark 32×32 con hash verificati,
alpha/sRGB, Single Sprite e senza compressione. Provenienza e notice offline
anche nell'APK. Fondo navy per mantenere contrasto sul toggle giallo.

[Inventario](xr-m10-action-inventory.md): **125 dichiarazioni fisse** esaminate,
tutti i 46 id mappati trovati. Valori, nomi CAD, commit, picker e comandi XR
senza equivalente verificato mantengono testo. I componenti scelti per distanza/
interferenze mantengono il nome. Vincola e Modello piatto verificati leggendo
le ControlDefinitions native: le feature Spiega/Ripiega non rappresentano il
cambio di vista XR. Evidenza: `artifacts/m10-icons/control-definitions.json`.

## Test e build

- Core XR/FakeAddIn: **590 PASS, zero failed**.
- Unity 6000.6.3f1 in copia isolata: **646 PASS, 6 ignored, zero failed**,
  `artifacts/m10-verification/final-editmode.xml` e `.log`.
- Dopo le asserzioni sui nomi CAD selezionati: test M10 mirati **11 PASS**,
  `scoped-names-tests.xml` e `.log` nella stessa cartella.
- Verificati cache, risorse, fallback/callback, tooltip a 7/14 mm e layout,
  non-raycast, lifecycle, disabled, toggle, hit area, sei provider e wrapper.
- Gallery rigenerata e ispezionata: `artifacts/m10-verification/gallery/`,
  palette pilota, tooltip, anello e cinque pagine del catalogo completo.
  Componenti Unity reali con azioni fixture; distinta dalla prova nel visore.
- Build QA Development con `XR_SO_ACCEPTANCE` e ordinaria riuscite:
  `build-m10-final.log`, `build-manifest.json`. CRC ZIP APK verificati,
  libreria ARM64 presente; classi M10 acceptance/profilo presenti solo nella QA.

| APK in `artifacts/m10-verification/` | SHA-256 |
|---|---|
| `InventorXrSo-m10-acceptance.apk` | `fa5a3c670711d25c518e3a5d3497f6b8cecea9536e2a0f8002d9436534064bc6` |
| `InventorXrSo-m10-ordinary.apk` | `4185f969d5d19a6795caf183443370996605e91314af92b13f86345880807b4b` |

## Quest e Inventor reale

Quest 3 `2G0YC1ZFB407P1`, 6 ottobre **18:42:04–18:42:57 Europe/Rome**.
Wrapper `scripts/run-m10-acceptance.ps1`, fixture dedicata M6, exit **0**.
Manifest: `artifacts/m10-verification/device/quest-acceptance-run-20261006-184204.json`.
Esito `PASS`, `runnerStarted=true`, visore `Awake`, hash QA remoto verificato.

34 sprite/cache, notice/manifest offline, fallback numerico, toggle, disabled
senza callback, tooltip e hit area della tavolozza/anello verificati nei sei
provider reali prima e dopo la regressione. Hover e input **sintetici**.

Regressione nativa M6: contesti/schede, isolamento solo visivo, doppio Trigger,
schizzo, tastierino, anello su componente/bordo/faccia, manipolazione solo vista,
preview/guardie commit, errore CAD, estrusione Apply e Undo, Lamiera con preview
nativa, voce/testo iniettato. Non certifica microfono, tracking, comfort,
calibrazione fisica, Stale live o tutti i runner M1–M9: le righe `NOT COVERED`
ereditate mantengono il loro ambito.

Log e screenshot in `device/`, prefisso `quest-acceptance-20261006-184204-`.
Screenshot ispezionato: tavolozza Assieme con simboli e label preservate per
azioni senza mapping; non dimostra leggibilità o comfort fisici.

Ispezione finale fixture: `dirty=false`, volume **12000 mm³**, centri **60 mm**,
occorrenze nella posizione iniziale. Tre documenti fixture chiusi senza salvare;
stato precedente ripristinato (**nessun documento**). Evidenze:
`native-fixture-inspection.log`, `fixture-restoration.log`. APK ordinario
reinstallato con hash remoto verificato, senza runner. Modifiche concorrenti e
scena generata preservate.

## Risorse e performance

100 rebuild runtime: **261 materiali, 278 texture, 37 sprite** prima/dopo,
nessuna crescita; Sprite condivisi. Memoria totale allocata 128.317.321 →
128.329.871 byte: osservazione, non budget per frame.

A/B/A nello stesso APK/fixture/posa/tema: otto label con geometria M8, stesse
azioni con icone M10, ritorno al testo. 90 frame warm-up + 240 campioni per fase,
senza hover/rebuild. JSON originale:
`device/quest-acceptance-20261006-184204-m10-acceptance-performance.json`.

| Presentazione | CPU main-thread frame p95 ms | GC p95 byte/frame | Intervallo frame p95 ms | GPU |
|---|---:|---:|---:|---|
| Testo A | 14,330 | 10235 | 14,525 | non disponibile |
| Icone B | 14,407 | 10235 | 14,582 | non disponibile |
| Testo A finale | 14,351 | 10235 | 14,555 | non disponibile |

CPU/GC: 240 campioni per fase; GPU: **0**. CPU +0,056 ms rispetto al maggiore
controllo, GC invariato: entro le soglie parziali della spec. **M10-08-budget
rimane NOT COVERED**, perché il plugin XR non fornisce i tempi GPU. Intervalli
frame includono pacing XR e non sostituiscono GPU/CPU. Questo confronto
controllato non equivale a due versioni complete del prodotto.

## Prova fisica e gate aperto

**M10-08:** acquisire tempi GPU validi con profiling Quest e completare il
budget della spec. Nessun PASS dedotto da CPU o screenshot.

**M10-09 — PASS riferito dall'utente, 6 ottobre 2026.** Alla richiesta di
verificare nell'app MR e Studio VR riconoscibilità delle icone, leggibilità
dei tooltip puntati con il controller, sfocatura, fastidio e copertura del
modello, l'utente ha risposto: «va bene tutto». Esito qualitativo della persona
nel visore, distinto dai test automatici; non estende la copertura a microfono,
tracking o comfort prolungato delle milestone precedenti. Log e manifest del
runner mantengono il loro originale `NOT COVERED` fisico; questa conferma è
successiva. Software, runner e prova fisica M10 consegnati; la chiusura completa
della milestone resta subordinata al budget GPU.
