# Inventor XR SO — M6: verifica

Spec: [M6 UX spaziale](superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md).
Piano Fase 1: [fondamenta](superpowers/plans/2026-09-29-inventor-xr-so-m6-fase1-fondamenta.md).

## Fase 1 — Fondamenta (29–30 settembre 2026)

Consegnato sul branch `feat/m6-fase1`:

- **Core** (`InventorXrSo.Core.Ui`): catalogo delle azioni, stato della barra di conferma, inserimento numerico e layout della postazione.
- **TextMeshPro** in `UiFactory`, con i colori di stato in `UiStyle`.
- **Input**: `XrInput` e aptica, con un impulso per mano; un impulso interrotto spegne il suo controller.
- **Guscio UI** (`UiShell`):
  - tavolozza sul controller sinistro, per ora con la sola scheda «Spazi»;
  - barra di conferma, nascosta perché nessun workspace è ancora migrato;
  - HUD al posto di `StatusBadge`, su due righe; i messaggi lunghi vanno a capo senza troncamenti.

Workspace e menù polso restano invariati. Nella Fase 1 la tavolozza sta **sotto** il controller sinistro, perché il menù polso di Ispeziona occupa lo stesso ancoraggio e serve al runner M3. La Fase 4 la riporta nella posizione della spec.

La geometria della tavolozza è stata cambiata con una decisione dell'utente (spec aggiornata):

- tavolozza 160×110 mm, testo di 7 mm, celle di 75,5×20 mm;
- etichette su al massimo due righe, mai troncate;
- tastierino 3×5 con celle di 49×15 mm.

Un test EditMode verifica che nessuna etichetta venga troncata.

| Prova | Esito |
|---|---|
| Core `XrSo.Core.Tests` | 428/428 PASS |
| Unity EditMode (test host compilato) | 209/209 PASS, 0 saltati |
| APK di accettazione / ordinario | build riuscite; sha256 `1ed09c5f…1290` / `4aacd424…c8eb` |
| Runner M1 sul Quest 3 con Inventor 2027 | PASS COMPLETE — `artifacts/m1-verification/quest-acceptance-run-20260930-084131.json` |
| Runner M2 | PASS COMPLETE — `artifacts/m2-verification/quest-acceptance-run-20260930-084515.json` |
| Runner M3 | PASS COMPLETE — `artifacts/m3-verification/quest-acceptance-run-20260930-084553.json` |
| Runner M4 | PASS COMPLETE — `artifacts/m4-verification/quest-acceptance-run-20260930-085021.json` |
| Runner M5 | PASS COMPLETE — `artifacts/m5-verification/quest-acceptance-run-20260930-085100.json` |
| Tavolozza e HUD visti sul Quest | NOT COVERED: da osservare con la persona (leggibilità a 7 mm, posa sotto il controller, HUD su due righe) |

Note sull'esecuzione:

- **Primo tentativo M1** (29 settembre, 23:01): TIMEOUT `before_runner_start` con visore `Asleep`. È un timeout prima dell'avvio, non un fallimento del test; ripetuto il 30 settembre con il visore sveglio.
- **PowerShell 7 assente**: `scripts/run-quest-acceptance.ps1` richiede PowerShell 7, che non è installato sulla postazione. I run hanno usato una copia temporanea per Windows PowerShell 5.1, senza `#requires` e con stderr di adb non terminante. Lo script del repository è invariato.
- **Fixture M4**: `bridge/tests/M4LiveProbe` non compila in un worktree senza l'add-in compilato. La fixture M4 è stata preparata e ripristinata dal checkout principale, dove `bridge/` è identico a quello del branch.
- **Ripristino**: ogni fixture è stata chiusa senza salvare e il documento precedente dell'utente è stato riattivato. Alla fine di ogni run è stato reinstallato l'APK ordinario della Fase 1, con hash verificato.

I runner M1–M5 esercitano input sintetico: le righe `NOT COVERED` dei loro log restano aperte come prima della Fase 1. I gate M6-01…M6-10 sono tutti **aperti**: la Fase 1 non ne chiude nessuno.
