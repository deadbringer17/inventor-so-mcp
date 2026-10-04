# M8 grapics — stato e verifica

Data: 4 ottobre 2026, Europe/Rome.
Spec: [adattamento HIVE](superpowers/specs/2026-10-04-m8-grapics-design.md).
Piano: [M8 grapics](superpowers/plans/2026-10-04-m8-grapics.md).

**Stato:** implementazione software presente sul branch `codex/m8-grapics`;
collaudo dispositivo, performance e prova fisica ancora aperti.

## Evidenza della preparazione

Repository HIVE identificato su GitHub: `deadbringer17/APE_Hive`, commit
`436a52a1450be2f3330c055670515df20696f031`. Esaminati fondazioni e fogli caricati,
template HIVE, manifest, kit e spec di reskin. Scaricati **10 file**, incluso
Satoshi variabile, in `assets/design-system/hive-ape/`. Verificato ogni file
contro il Git blob SHA fornito dall'API; SHA-256 e dimensioni nel
[manifest](../assets/design-system/hive-ape/provenance.json).

Letti `CLAUDE.md`, `bridge/CLAUDE.md`, spec M6 e codice reale dei componenti
grafici e della finestra pairing. Rilevate differenze rispetto ai riferimenti
storici: Satoshi in produzione, Home ancora legacy `Text`, anello a 7 mm,
assenza di un set di icone CAD esportato nel bundle GitHub.

La preparazione è stata seguita dall'implementazione: token semantici e focus,
sprite 9-slice, Satoshi statico ufficiale e fallback CAD locale, migrazione
Home/voce/quote a TMP, tema Windows e runner M8 su fixture M6. La scena Main
non è stata modificata a mano. Preview/chip modificato blu e interferenze rosse
restano separati dagli stati della UI. Anello/barra sono stati ampliati per
conservare cap height e label intere; l'ingombro fisico resta da verificare.

## Test automatici locali — 4 ottobre 2026

| Suite | Esito | Evidenza locale |
|---|---|---|
| Core XR .NET / FakeAddIn | PASS 467/467 | `artifacts/m8-verification/m8-core.trx` |
| Backend / FakeAddIn | PASS 985/985 | `artifacts/m8-verification/m8-backend.trx` |
| Pairing Windows su STA, dipendenze finte | PASS 9/9 | `artifacts/m8-verification/m8-pairing.trx` |
| Unity EditMode | PASS 416/416 | `artifacts/m8-verification/editmode.xml` |

Gallery dei componenti Unity reali, con soli dati fixture, in
`artifacts/m8-verification/{baseline,current}/`: Home, palette, keypad, anello,
chip e otto stati barra. Le immagini non sono screenshot del visore né una
misura di leggibilità fisica. Le capture Windows includono un QR generato dalla
fixture e layout scalati sinteticamente a 100/150/200%; **non** sono cambi di
DPI del monitor. Acquisiti font statici senza conversioni, con FFL e hash in
`assets/design-system/hive-ape/font-provenance.json`; la licenza è inclusa nelle
risorse referenziate del tema Unity e nel packaging Windows.
Fallback per nomi CAD greci/cirillici da Liberation già incluso in TMP,
con OFL e provenienza locale separata; CJK non incluso. Verificate label
operative senza glyph mancanti e tastiera Home dentro il canvas; il pannello
si estende verticalmente se le istruzioni richiedono spazio. Sprite importato
in modalità Single, con bordi 9-slice effettivi; «Progettazione» nell'anello
resta su una riga a 14 mm. Distribuzione Windows Release riuscita, FFL presente
in `artifacts/m8-verification/windows-release/licenses/`.

La modifica preesistente di `LiberationSans SDF - Fallback.asset` è stata
confrontata byte per byte con la copia salvata prima dei test: preservata.

Runner `M8QuestAcceptance` registrato, escluso dall'APK ordinario: eredita
controlli nativi e input sintetici M6 sui quattro workspace; aggiunge font,
contrasto, fit e 100 rebuild. Gli id M6 e i relativi `NOT COVERED` restano nel
log. Il wrapper `scripts/run-m8-acceptance.ps1` ripristina documento e APK nel
`finally`. **Runner eseguito il 4 ottobre 2026 (sul Quest, input SINTETICO,
fixture M6 dedicata): `PASS COMPLETE`**, manifest
`artifacts/m8-verification/device/quest-acceptance-run-20261004-185056.json`.
PASS: M8-01/02, M8-03/07-rebuild (cap, fit, 100 rebuild senza crescita di
materiali/texture), M8-04/05-regression (regressione nativa M6). NOT COVERED:
M8-05-stale-native, M8-06 (DPI reali/QR), M8-07-performance (GC/frame p95/GPU),
M8-08 (prova fisica). APK ordinario ripristinato (sha256 verificato), fixture
chiusa senza salvare.
Tre problemi trovati e risolti per arrivare al PASS: (1) rate limit dell'host
(240/min) durante il passo Lamiera: host avviato con `--http-rate-limit 3000`;
(2) `AssemblyWorkspace.SceneCurrent` confrontava la revisione intera, per cui un
bump non visivo dopo Apply+Undo lasciava `Idle` falso e "Componenti"
disabilitata: ora confronta `DocumentId` e `VisualRevision` (test EditMode
aggiunto, 417/417); (3) `run-quest-acceptance.ps1` terminava il run in anticipo
perché `PASS COMPLETE` compariva nel testo di una riga NOT COVERED: ora cerca
`] PASS COMPLETE;`. Le attese del runner hanno ora un timeout di 60 s con
chiamante nel messaggio. Il Quest ordinario va installato dal nuovo
`InventorXrSo-m8-ordinary.apk` (sha256 6eeea401…).

## Gate

| Gate | Stato | Evidenza / lavoro rimanente |
|---|---|---|
| M8-01 | PASS locale | snapshot/hash, font statici ufficiali, FFL e risorse locali; distribuzione build da verificare |
| M8-02 | PASS locale | tema, contrasto essenziale ≥4,5:1, focus ≥3:1, sRGB/Linear; MR fisico aperto |
| M8-03 | PASS locale | TMP, glyph CAD/italiani, cap misurata e fit; lettura sul Quest aperta |
| M8-04 | PARZIALE | reskin implementato, input/EditMode passato; runner Quest da eseguire |
| M8-05 | PARZIALE | stati/disabled/voce coperti da regressioni locali; invarianti native da rieseguire |
| M8-06 | PARZIALE | tema Windows, 9 test e QR fixture; DPI reali/scansione fisica aperti |
| M8-07 | PARZIALE | regressioni locali passate; build in corso, runner e profilo Quest aperti |
| M8-08 | APERTO — prova fisica | sessione da seduto, lettura e controller reali |

Registrare separatamente test unitari/FakeAddIn, runner Quest con input
**sintetico**, prova fisica. Per ogni run conservare manifest, log e screenshot
in `artifacts/m8-verification/`; dichiarare `NOT COVERED` per ciascun sottocaso
non esercitato. M8 non cambia gli esiti dei collaudi M6/M7 o pairing precedenti.
