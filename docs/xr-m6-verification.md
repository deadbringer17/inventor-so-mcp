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

## Fase 2 — Progettazione + Schizzo (30 settembre – 1 ottobre 2026)

Piano: [Fase 2](superpowers/plans/2026-09-30-inventor-xr-so-m6-fase2-progettazione-schizzo.md). Consegnato sul branch `feat/m6-fase2`:

- **Core**: `SketchSheetLayout` (posa del foglio schizzo orizzontale sul piano di lavoro, X a destra, Y lontano dall'utente); `NumericEntry` e `ActionCatalog` estesi (`Committed`/`CommitValue`, `FindTab`).
- **Viste Unity**: chip valore (`ChipView`), anello contestuale (`RingView`, max 6 azioni), foglio schizzo (`SketchSheetView`, «Vista modello» ↔ «Foglio» muovono solo la radice della scena), postazione (`Workbench`, transizioni di ~250 ms, Adatta, Ricentra).
- **`DesignWorkspace` migrato**: niente pannello fluttuante; è un `IActionProvider` con schede Schizzo · Vincoli · Feature · Opzioni feature · Parametri · Vista (+ Spazi) e id stabili `design.*`. Gli elenchi lunghi (piani, profili, parametri, vincoli) sono schede di scelta da massimo 8 voci, sfogliate con lo stick sinistro, senza «Precedenti/Successivi» e senza voci perse. Numeri via chip + tastierino in tavolozza; anello dopo la selezione di faccia piana (Crea schizzo, Estrusione, Foro) o bordo (Raccordo, Smusso). «Applica» solo dalla barra di conferma; la voce «Applica» non committa (M5-11).
- **Input**: il workspace legge i controller solo da `XrInput`. Trascinamento della maniglia con il solo Trigger tenuto; rilascio → anteprima; perdita di tracking → ultimo valore valido e impulso di errore; chiusura del workspace → cattura chiusa (M5-08). Trigger sinistro = precisione 10×; A = snap; X = indietro (tastierino, anello, elenco, ultimo passo di bozza); Y = Adatta / tenuto Ricentra; stick sinistro = zoom; aptica secondo i profili M6.
- **Runner M3**: invoca le azioni per id tramite il catalogo e digita i numeri sul tastierino; l'input è registrato come sintetico.

Commit: `4462ee8`, `8e27c46`, `1582db3`, `5d57518`.

| Prova | Esito |
|---|---|
| Core `XrSo.Core.Tests` | 439/439 PASS |
| Unity EditMode | 279/279 PASS (input sintetico `SyntheticInputSource` per trascinamento, tracking perso, precisione, snap, catena di X, zoom, aptica) |
| APK ordinario | build riuscita; sha256 `c61eaf71…2125` |
| Runner M1–M5 sul Quest 3 con Inventor 2027 (3 ottobre 2026, APK di collaudo `6d2152c0…61c0`) | PASS COMPLETE ×5, input sintetico: M1 `artifacts/m1-verification/quest-acceptance-run-20261003-134057.json`; M2 `…m2-verification/…-20261003-134133.json`; M3 `…m3-verification/…-20261003-134207.json` (Progettazione migrata: azioni per id, tastierino, barra di conferma); M4 `…m4-verification/…-20261003-134251.json`; M5 `…m5-verification/…-20261003-134321.json` |
| Foglio, chip, anello, barra di conferma e tavolozza visti sul Quest | NOT COVERED: serve la prova fisica da seduto (leggibilità, precisione della penna, aptica, trascinamento a Trigger e rilascio) |

I gate M6-02, M6-03, M6-04 e M6-05 hanno i sottocasi automatici coperti da test EditMode con input **sintetico**, ma restano **aperti**: mancano il runner sul Quest con Inventor reale e la prova fisica. Lamiera, Assieme e Ispeziona usano ancora il loro pannello (Fasi 3–4).

Note sul run del 3 ottobre:

- **Rete**: l'app aveva salvato l'IP `192.168.1.10`, ma il PC era a `192.168.1.227` («Cannot connect to destination host»). Host riavviato con `--pair-host 192.168.1.227` e pairing rifatto sul visore. Consigliata una riserva DHCP per il PC.
- **Inventor** si era chiuso durante il primo tentativo di preparare la fixture (`MK_E_UNAVAILABLE`); riavviato, le fixture sono passate.
- Ogni fixture è stata chiusa senza salvare e l'APK ordinario `01fdcf4c…30f6` è stato reinstallato dallo script.
- M6-09 resta **aperto**: serve anche la migrazione degli altri workspace (Fasi 3–4) e il runner M6. La prova fisica da seduto (M6-02…M6-05, M6-10) resta **aperta**.

## Fase 3 — Lamiera (3 ottobre 2026)

Piano: [Fase 3](superpowers/plans/2026-10-03-inventor-xr-so-m6-fase3-lamiera.md). Consegnato sul branch `feat/m6-fase3`:

- **`LamieraWorkspace` migrato**: niente pannello fluttuante; è un `IActionProvider` con schede Lamiera · Schizzo · Sviluppo · Vista (+ Spazi), id stabili `lamiera.*`, elenchi (regole, schizzi) come schede di scelta da massimo 8 voci. Altezza, angolo e spessore con chip + tastierino; Applica solo dalla barra di conferma; la voce «Applica» non committa (M5-11); dettagli di errore sull'HUD.
- **Input solo da `XrInput`**: flangia trascinata con il solo Trigger tenuto sulla maniglia; rilascio → anteprima; perdita di tracking → ultimo valore valido e impulso di errore; chiusura del workspace → cattura chiusa (M5-08); precisione 10×; stick destro ± passo e passo 10 / 1 / 0,1; X = indietro; Grip = solo vista; due mani = solo vista; zoom con lo stick sinistro; aptica.
- **Anello contestuale** solo su facce piane vere e su bordi.
- **Sviluppo piano** posato sul piano di lavoro davanti all'utente (adattato a 0,45 × 0,30 m); «Distacca» / «Riaggancia» spostano solo la mesh; azioni «Vista: piegato» / «Vista: sviluppo».
- **Correzioni emerse dal runner sul Quest**: confermare nel tastierino il valore già presente avviava nessuna anteprima (ora la richiede se manca); con la bozza modificata restava visibile il fantasma della vecchia anteprima (ora si nasconde: il fantasma blu è solo dello stato «Pronto»).

Commit: `e22a142`, `a3182c5` più la correzione dei runner e del workspace.

| Prova | Esito |
|---|---|
| Core `XrSo.Core.Tests` | 439/439 PASS |
| Unity EditMode | 325/325 PASS (gesti con `SyntheticInputSource`) |
| APK ordinario (sul Quest, hash verificato) | sha256 `584ad086…43e3` |
| APK di collaudo | sha256 `cfed3bbc…54cc` |
| Runner M1–M5 sul Quest 3 con Inventor 2027 | PASS COMPLETE ×5, input **sintetico**: M1 `artifacts/m1-verification/quest-acceptance-run-20261003-165142.json`; M2 `…m2-verification/…-20261003-165240.json`; M3 `…m3-verification/…-20261003-164559.json`; M4 `…m4-verification/…-20261003-164946.json`; M5 `…m5-verification/…-20261003-164330.json` |
| Gesti di Lamiera visti sul Quest, leggibilità, ergonomia | NOT COVERED: serve la prova fisica da seduto |

Note sull'esecuzione:

- **Runner M5 adeguato** al nuovo input: input sintetico attivo per tutta la durata del run (i controller reali, in mano o appoggiati, non devono muovere chip né armare campi: in un primo tentativo il rumore dello stick aveva portato l'angolo a 5°); passo sincrono del trascinamento, dell'afferra e delle due mani a ogni frame sintetico; rilascio prima di ogni nuova pressione (gli eventi sono a fronte); verifica dello sviluppo in coordinate mondo (sul piano di lavoro la mesh non vive più nel sistema del modello).
- **NOT COVERED nel log M5**: il Trigger sul bordo per deselezionarlo. Con la maniglia (raggio di cattura 45 mm) il bordo corto della fixture è coperto per intero: il rilevamento è dichiarato, non aggirato. La deselezione resta possibile con l'azione «Svuota bordi».
- **M4 falliva** («fixture has translation and rotation axes») anche con l'APK della Fase 2 che il mattino passava: la causa era l'**host HTTPS** rimasto agganciato al vecchio Inventor; riavviato l'host, M4 è passato. Non è una regressione.
- Il visore va indossato o tenuto sveglio: da fermo va in standby e il runner va in `TIMEOUT before_runner_start` (non un fallimento). Un `TIMEOUT runner` durante i gesti era la conseguenza di un'anteprima mai richiesta (valore uguale nel tastierino), poi corretta.
- Ogni fixture è stata chiusa senza salvare; l'APK ordinario finale è sul visore.

I gate M6-02, M6-03, M6-04 e M6-05 coprono ora Progettazione **e** Lamiera nei sottocasi automatici, ma restano **aperti**: mancano la prova fisica da seduto e le Fasi 4–5 (Assieme, Ispezione, runner M6). M6-09 e M6-10 restano aperti.

## Fase 4 — Assieme + Ispezione (3 ottobre 2026)

Piano: [Fase 4](superpowers/plans/2026-10-03-inventor-xr-so-m6-fase4-assieme-ispezione.md). Consegnato sul branch `feat/m6-fase4`:

- **`AssemblyWorkspace` migrato**: niente pannello; `IActionProvider` con schede Componenti · Vincoli · Vista (+ Spazi), id `assembly.*`, elenchi (componenti, riferimenti, vincoli, giunti, assi) come schede di scelta da massimo 8 voci. Distanza, angolo e gioco minimo con chip + tastierino; Applica solo dalla barra di conferma (la voce «Applica» non committa, M5-11). Spostamento componente: Trigger tenuto sulla maniglia/asse; rilascio → anteprima; perdita di tracking → bozza scartata e impulso di errore; chiusura → cattura chiusa (M5-08). Grip e due mani = solo vista.
- **Assieme sollevato** (`Workbench.ApplyAssembly`, transizione ~250 ms) e **isolamento** (`ComponentIsolation`): il componente avanza a metà strada, il resto al 20 %; solo visivo, non muove l'occorrenza in Inventor. Anello sul componente: Isola · Sposta · Vincola · Apri; da isolato «Apri in Progettazione» / «Apri in Lamiera»; «Rilascia» o X riporta tutto al suo posto.
- **`InspectWorkspace` migrato**: schede Misura · Sezione · Vista (+ Spazi), id `inspect.*`; menù polso, pannello, breadcrumb e scheda compatta rimossi (contesto e proprietà sull'HUD). Ispeziona è il workspace di default del catalogo: la tavolozza non è mai vuota. Misura, Sezione, Scala e Ambiente funzionano offline; Esplora, Proprietà e Documenti aperti no.
- **Tavolozza** riportata nella posizione della spec (tolto lo spostamento sotto il controller; test di guardia in `UiShellTests`).
- **Voce sul catalogo**: `WorkspaceVoiceTarget` risolve con `ActionCatalog.ResolveVoice`; ambiguità rifiutata, comando disabilitato senza mutazione, «Applica» vocale non committa.
- **Runner M1–M5** invocano le azioni per id (input registrato come sintetico).

| Prova | Esito |
|---|---|
| Core `XrSo.Core.Tests` | 443/443 PASS |
| Unity EditMode | 375/375 PASS (incluso il test del Grip nello stesso frame del Trigger) |
| Compilazione del codice dei runner (`XR_SO_ACCEPTANCE`) | PASS (controllo con `csc.rsp` temporaneo) |
| APK ordinario (sul Quest, hash verificato) / di collaudo | build riuscite; sha256 `e6d579aa…21b3` / `9b8b111e…b252` |
| Runner M1–M5 sul Quest 3 con Inventor 2027 (3 ottobre 2026), input **sintetico** | PASS COMPLETE ×5: M1 `artifacts/m1-verification/quest-acceptance-run-20261003-191506.json`; M2 `…m2-verification/…-20261003-191526.json`; M3 `…m3-verification/…-20261003-191048.json`; M4 `…m4-verification/…-20261003-191425.json`; M5 `…m5-verification/…-20261003-191349.json` |
| Assieme sollevato, isolamento, anello, tavolozza sul controller visti sul Quest | NOT COVERED: prova fisica da seduto |

Scostamenti dichiarati:

- «Apri in Lamiera» è abilitato per qualsiasi parte isolata (il contesto assieme non dice quali sono lamiera); Lamiera segnala se la parte non lo è.
- Ispeziona non mostra ancora l'anello contestuale: serve il punto colpito dal percorso di pick (`ControllerRay`, che legge ancora `OVRInput`; i workspace no).
- Rimossi da Ispeziona: «Aggiorna» (Proprietà ricarica ogni volta), schermata «Dettagli errore» (vanno sull'HUD), tinta Lamiera-primaria della vecchia scheda.

M6-01 e M6-06 hanno ora i sottocasi automatici (EditMode, input sintetico) ma restano **aperti**: mancano runner sul Quest e prova fisica. M6-08 ha il test sul catalogo, aperto per lo stesso motivo. M6-09 resta aperto (runner M1–M5 da rieseguire con la Fase 4 e runner M6 nella Fase 5); M6-10 resta aperto.

Note sull'esecuzione:

- **Bug trovato dal runner M4** (`…m4-verification/…-20261003-185955.json`, FAIL «Grip held with the Trigger is view only»): con Grip e Trigger premuti nello stesso frame, `XrInput` emetteva il Trigger prima del Grip e il workspace catturava la maniglia. Corretto: `PenGripHeld` è aggiornato prima degli eventi del Trigger e Assieme, Progettazione e Lamiera lo leggono (`GripDown`); aggiunto il test EditMode. Dopo la correzione APK ricompilati e M1–M5 rieseguiti: tutti PASS.
- **Run non valido**: durante la ripetizione Inventor si è chiuso da solo (`E_FAIL` in M5 `…m5-verification/…-20261003-191112.json`, fixture M4 non preparata). Non è un fallimento del software: Inventor riaperto, host HTTPS riavviato (`--target 2027`, `--pair-host 192.168.1.227`), M5 e M4 ripetuti con esito PASS.
- Primo tentativo M1 (`…-185318.json`): `TIMEOUT before_runner_start`, visore sveglio ma senza finestra in primo piano; non è un fallimento del test.
- Ogni fixture è stata chiusa senza salvare, il documento dell'utente riattivato e l'APK ordinario reinstallato con hash verificato.

## Fase 5 — Collaudo (3 ottobre 2026)

Consegnato (non ancora eseguito sul visore):

- **Runner M6** `Inventor XR SO/Assets/XrSo/Xr/Acceptance/M6QuestAcceptance.cs` (`XR_SO_ACCEPTANCE`, extra Android `xr_m6_acceptance`, fixture `XR_M6_Quest_Acceptance`). Gira sull'`AppController` reale: sostituisce la sorgente dell'`XrInput` dell'app con una `SyntheticInputSource` (ogni evento dei controller è un frame **sintetico**, scritto così nel log), invoca le azioni per id sull'`ActionCatalog` reale e controlla dopo ogni anteprima che documento e revisione di Inventor non siano cambiati.
- **Fixture M6** (`bridge/tests/QuestAcceptanceFixtures`, `--prepare-quest m6`): un assieme con due componenti, un blocco (sketch `Base_M6`) e una lamiera (sketch `Taglio_M6`), parti tenute aperte. Il runner passa da Ispeziona e Assieme sull'assieme, entra in Progettazione con «Apri in Progettazione» dal blocco isolato e in Lamiera con «Apri in Lamiera» dalla lamiera isolata, poi riattiva l'assieme.
- **Plumbing**: `UiHitOverride` su `DesignWorkspace` (come Assieme e Lamiera), `m6` in `scripts/run-quest-acceptance.ps1` (timeout predefinito 780 s per m6), `M6QuestAcceptance` nel test di contratto EditMode (`ReflectedMembersExist` e `ReflectedMembersListIsComplete`).

| Gate | Sottocasi che il runner esegue (input sintetico) | `NOT COVERED` dichiarato nel log |
|---|---|---|
| M6-01 | tavolozza figlia del controller sinistro e non della testa; schede e scheda Spazi su Ispeziona, Progettazione, Lamiera e Assieme; rotazione delle schede con lo stick sinistro; nessun pannello fluttuante; «Applica» solo sulla barra | leggibilità e posa sul controller reale |
| M6-02 | schizzo sul foglio orizzontale all'altezza del piano; linea con la punta penna e linea col raggio; «Vista modello» ↔ «Foglio» senza variare elementi e piano di schizzo; stesso punto fisico → stesse coordinate CAD | precisione reale della punta penna |
| M6-03 | chip e tastierino sulla tavolozza, X chiude il tastierino; stick ± passo con passo 1 → 10 → 0,1 e limiti; precisione col Trigger sinistro (drag della maniglia: +10 mm normale, +1 mm in precisione); dettatura nel campo armato (testo iniettato) | stick, tastierino e microfono reali |
| M6-04 | anello su componente, bordo e faccia piana (azioni previste), aperto da un raggio sintetico, chiuso da X e dal vuoto, azione dell'anello eseguita | anello di Ispeziona (non esiste ancora) |
| M6-05 | barra Empty, Draft, Previewing, Ready, Error (fillet 100 mm), Offline, Uncertain, Applied (un Apply reale di estrusione e il suo Undo XR); Applica abilitata solo in Ready; tabella pura `CommitBarState` con Stale | Stale dal vivo (coperto a livello backend da M3-Stale e M5-07) |
| M6-06 | Assieme sollevato (65 cm davanti, 15 cm sotto la testa); isolamento solo visivo (revisione e occorrenze di Inventor invariate); «Apri in Progettazione» e «Apri in Lamiera» dal componente isolato | percezione dell'isolamento |
| M6-07 | una mano e due mani solo vista; Adatta (Y breve); Ricentra (Y tenuto 1 s) | calibrazione dell'altezza del piano (nessun percorso nell'app chiama `Workbench.SetDeskHeight`); tracking reale |
| M6-08 | «applica» con anteprima valida mostra solo la conferma; etichetta disabilitata e frase ignota rifiutate senza mutazione; etichetta abilitata eseguita; ambiguità, disabilitato ed eseguito su un `ActionCatalog` controllato (stessa classe, provider sintetico) | pulsante B, microfono, audio |

| Prova | Esito |
|---|---|
| Core `XrSo.Core.Tests` | 443/443 PASS |
| Unity EditMode (batch, 3 ottobre 2026) | 377/377 PASS, incluso il contratto dei runner con `M6QuestAcceptance` (`ReflectedMembersExist`, `ReflectedMembersListIsComplete`, esclusione dall'APK ordinario) |
| Compilazione di runner e fixture | Runner M6 compilato con `XR_SO_ACCEPTANCE` (stesso `csproj` generato da Unity, build `dotnet`); fixture compilate (`dotnet build`); controllo statico di `ReflectedMembers` (tutti i nomi usati sono dichiarati e presenti nei sorgenti) |
| Runner M6 sul Quest 3 con Inventor 2027 | **NOT RUN**: nessun esito, nessun manifest. Non si deduce nulla da test automatici o dalla sola compilazione |
| Prova fisica da seduto (M6-10) | **NOT RUN** |

Stato dei gate: **M6-01…M6-08** hanno i sottocasi riproducibili scritti nel runner, ma restano aperti finché il runner non gira sul Quest e finché non c'è la prova fisica. **M6-09** resta **aperto**: servono il runner M6 eseguito (`PASS COMPLETE` sui sottocasi riproducibili) e i runner M1–M5 migrati rieseguiti con la Fase 4. **M6-10** resta **aperto**: serve una persona con i controller.

Prima del primo run: costruire l'APK di collaudo, `dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest m6`, avviare l'host HTTPS con `--target 2027` sull'IP corrente del PC, poi `pwsh scripts/run-quest-acceptance.ps1 -Milestone m6 -Apk artifacts/InventorXrSo-acceptance.apk -OrdinaryApk artifacts/InventorXrSo.apk`, `--inspect-quest m6` e `--restore-quest m6`. Dettagli e avvertenze (il runner fa un Apply reale e lo annulla; dopo un run interrotto riattivare l'assieme) in [xr-quest-acceptance.md](xr-quest-acceptance.md). Punti scritti senza Inventor e da confermare al primo run: nomi delle occorrenze (`Block`, `Sheet`), `Occurrences.Add` di una lamiera ancora aperta, esito del fillet da 100 mm come errore, mira dei raggi sintetici su bordo, faccia e componente (se non colpisce, il log riporta `NOT COVERED [M6-04]` o `[M6-07]` con il motivo).
