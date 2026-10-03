# Inventor XR SO — M7: Ispeziona, verifica ingegneristica degli assiemi

Data: 3 ottobre 2026. Stato: specifica di design approvata in sessione di
brainstorming. Implementazione e accettazione M7 non ancora eseguite.

Riferimenti: [specifica prodotto](../../../Inventor%20XR%20SO/inventor_meta_product.md)
§16 (Inspect Mode); [spec M6](2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md);
[verifica M6](../../xr-m6-verification.md); [test automatici sul Quest](../../xr-quest-acceptance.md).
Le prove M6 ancora aperte (M6-01, M6-06, M6-08…M6-10) restano gate M6 separati.

## Obiettivo e perimetro

Oggi Ispeziona serve a **leggere** il modello: misura punto-punto locale,
sezione, scala, gerarchia e proprietà (massa, materiale, DOF). M7 lo rende uno
strumento per **verificare** un assieme, cioè capire se funziona, senza
modificarlo.

M7 aggiunge quattro capacità, nell'ordine di consegna:

1. **Visibilità**: X-Ray (fantasma), isola, nascondi, ripristina. Solo client.
   Viene prima perché la lista dei risultati la usa per mettere a fuoco una
   coppia.
2. **Interferenze** calcolate da Inventor tra le occorrenze di primo livello.
3. **Distanza minima** calcolata da Inventor tra due occorrenze.
4. **Salute dell'assieme**: vincoli e giunti in errore, componenti non
   vincolati, DOF residui, e validazione della BOM.

Principi:

1. Ispeziona resta in sola lettura: nessuna capacità M7 cambia il documento o
   la sua revisione.
2. Un risultato è sempre legato alla revisione su cui è stato calcolato. Se la
   revisione cambia, il risultato si dichiara obsoleto e non si spaccia per
   attuale.
3. I calcoli di Inventor sono lenti e non interrompibili. La UI lo dice e non
   finge di annullarli.

Fuori da M7: modifiche all'assieme o correzione automatica delle interferenze,
interferenze tra componenti interni ai sottoassiemi (foglie), distanza tra
facce di occorrenze diverse, parti singole, lamiera e sviluppo piano,
confronto BOM (`inventor_compare_bom`), comandi vocali per le verifiche,
movimento della camera della finestra Inventor.

## Stato della base

Percorsi relativi a `Inventor XR SO/Assets/XrSo/` dove non indicato.

- `Xr/InspectWorkspace.cs` è il workspace predefinito. `Xr/InspectActions.cs`
  dichiara le azioni `inspect.*` nelle schede Misura, Sezione, Vista e Spazi.
- Backend del client: `InspectionBackend.cs` usa `inventor_inspect_xr`,
  `inventor_list_open_documents` e `inventor_activate_open_document_xr`.
- Selezione: in un contesto di assieme si selezionano occorrenze (id
  portabili), in un contesto di parte si selezionano facce.
- `Runtime/Scene/ComponentIsolation.cs` (Assieme) sposta il componente verso
  l'utente e mette il resto in fantasma: shader `XrSo/HighlightOverlay` su una
  copia della mesh, renderer originale spento, collider attivi. Si azzera
  sull'evento `CadSceneView.Rebuilt`.
- Non esiste un focus locale su una singola entità: i workspace hanno solo
  `FitView()`.
- `SessionController` emette `DocumentStateChanged` quando cambia `revision`
  o `visual_revision` del documento (eventi più polling ogni 5 s).

Bridge (`bridge/src/`):

- `inventor_check_interference` (stabile): occorrenze per **nome**, solo
  primo livello (un sottoassieme conta come un blocco unico). Restituisce
  coppie `{a, b, volume_mm3}` senza posizione dei corpi di interferenza e
  senza revisione.
- `inventor_measure_min_distance` (stabile): occorrenze per nome o riferimenti
  con nome (iMate, feature di lavoro, origine). Restituisce solo `distance_mm`,
  senza i punti più vicini.
- `inventor_get_assembly_health` (sperimentale): `occurrence_id` portabili,
  DOF, ancoraggio, vincoli e giunti in errore. I vincoli in errore hanno solo
  nome e stato di salute, senza le due occorrenze coinvolte.
- `inventor_validate_bom` (stabile): risultati con codici
  (`PART_NUMBER_MISSING`, `PART_NUMBER_DUPLICATE`, …).
- Tutti i tool usati dal Quest finiscono in `_xr` e ricevono `document_id`,
  `expected_revision` e id portabili. Rifiutano con `DOCUMENT_CHANGED` o
  `STALE_REVISION` (vedi `Handlers/Experimental/InspectXrHandlers.cs`). I tool
  generici sopra non seguono questo contratto, quindi il Quest non li usa
  direttamente.
- Ogni comando ha un limite di 30 s (`InventorAddInServerBase`,
  `timeout_ms` ≤ 30000). Allo scadere il client riceve `TIMEOUT`, ma il lavoro
  sul thread STA di Inventor continua fino alla fine.
- `inventor_focus_entity` muove la camera della finestra Inventor, non la
  scena sul Quest: M7 non lo usa.

## Architettura

Ogni capacità è un modulo in tre parti, testabili separatamente:

| Parte | Ruolo | Dove |
|---|---|---|
| Servizio | stato, chiamata al backend, parsing, legame con la revisione. Nessuna dipendenza da Unity. | core: `Packages/com.occhipinti.inventorxrso.core/Runtime/Verify/` |
| Vista | disegno in scena (fantasma, rosso, box, linea, etichetta) | `Runtime/Scene/` |
| Azioni | id `inspect.visibility.*` nella nuova scheda **Visibilità** e `inspect.verify.*` nella nuova scheda **Verifica** di `InspectActions` | `Xr/` |

La UI non chiama mai il backend direttamente: passa dal servizio.

### Tool bridge nuovi (tier sperimentale)

Tre tool XR, con handler in `shared/Handlers/Experimental/` sotto
`#if INVENTOR2027 && SO_EXPERIMENTAL`, derivati da `ExperimentalHandler`, con
contratto `XQ(...)` in `ToolContracts` e righe in `ExperimentalSourceTests`.
Tutti in sola lettura: nessuna transazione, nessun cambio di revisione.
Ognuno verifica `document_id` ed `expected_revision` come `inspect_xr` e
riusa la logica dell'handler stabile corrispondente, che resta invariato per
gli agenti testuali.

**`inventor_check_interference_xr`**
Input: `document_id`, `expected_revision`, `occurrence_ids` (opzionale).
Se assente, si analizzano tra loro tutte le occorrenze di primo livello non
soppresse. Se presente, si analizzano le occorrenze indicate contro tutte le
altre di primo livello non soppresse (secondo insieme di
`AnalyzeInterference`): così "solo selezione" funziona anche con un solo
componente selezionato.
Output: `revision`, `count`, `total_volume_mm3`, `pairs[]` con
`a_occurrence_id`, `b_occurrence_id`, `a_name`, `b_name`, `volume_mm3` e
`boxes[]` (un box min/max in mm, coordinate assieme, per ogni corpo di
interferenza della coppia, letto da `InterferenceBody.RangeBox`).
`elapsed_ms` è riportato per la calibrazione.

**`inventor_measure_min_distance_xr`**
Input: `document_id`, `expected_revision`, `a_occurrence_id`,
`b_occurrence_id`.
Output: `revision`, `distance_mm` e, se Inventor li fornisce, `point_a` e
`point_b` in mm (coordinate assieme). Se i punti non sono disponibili, il
campo `points_source` vale `"unavailable"`. La lettura dei punti tramite il
contesto di `MeasureTools.GetMinimumDistance` non è confermata sull'interop
installata: si usa late binding (`dynamic`) e un errore lascia i punti nulli
senza far fallire la misura.

**`inventor_assembly_health_xr`**
Input: `document_id`, `expected_revision`, `max_occurrences`.
Output: i campi di `get_assembly_health`, più `a_occurrence_id` e
`b_occurrence_id` per ogni vincolo o giunto in errore (dalle occorrenze del
vincolo, quando leggibili), e il risultato di `validate_bom` sotto `bom`.
Due letture in un'unica chiamata, sulla stessa revisione.

### Client: moduli

**`ComponentVisibility`** (`Runtime/Scene/`). Stato per occorrenza: Normale,
Fantasma o Nascosto.
- Fantasma riusa la tecnica di `ComponentIsolation`: copia della mesh con
  `HighlightOverlay`, collider attivi. La logica comune viene estratta in un
  helper condiviso, non duplicata.
- Nascosto spegne renderer **e** collider, così il raggio passa attraverso.
- Azioni: X-Ray della selezione, isola selezione (tutto il resto in
  fantasma), nascondi selezione, mostra tutto.
- Si azzera su `Rebuilt` e all'uscita da Ispeziona. Non si attiva in
  Assieme; `ComponentIsolation` non si attiva in Ispeziona.
- Funziona senza backend.

**`VerifyFinding`** (core). Una riga di risultato: severità (errore o
avviso), titolo breve, valore in mm o mm³ se presente, id delle occorrenze
coinvolte e box opzionali. Interferenze e salute producono righe di questo
tipo e le mostra un'unica **lista Risultati**.

Selezionando una riga:
1. le occorrenze coinvolte vengono evidenziate (`SelectionVisuals`);
2. il resto va in fantasma (`ComponentVisibility`);
3. la vista si centra sul box che le contiene (nuovo `FocusBounds` locale,
   che non muove Inventor).

Deselezionando, si torna allo stato di visibilità precedente.

**`InterferenceService`** (core) e **`InterferenceView`** (`Runtime/Scene/`).
Ambito: tutte le occorrenze di primo livello, oppure il componente
selezionato contro tutti gli altri. La vista colora di rosso le due occorrenze della coppia
selezionata e disegna i box di interferenza come contorni rossi, visibili
attraverso i corpi in fantasma. Zero interferenze è un risultato esplicito
("Nessuna interferenza su N occorrenze").

**`DistanceService`** (core) e **`DistanceView`** (`Runtime/Scene/`). Si
selezionano due occorrenze nel contesto di assieme. Il valore viene sempre da
Inventor. La linea:
- unisce `point_a` e `point_b` se Inventor li fornisce;
- altrimenti unisce i punti più vicini calcolati sulle mesh locali, ed è
  etichettata "linea indicativa".

La misura esistente nella scheda Misura si chiama "Punto-punto (locale)";
quella nuova "Distanza minima (Inventor)".

**`AssemblyHealthService`** (core). Riepilogo (sano o no; componenti non
vincolati; vincoli, giunti e righe BOM in errore) e righe `VerifyFinding`
per ogni problema.

### Schede Visibilità e Verifica

La palette accetta al massimo 8 azioni per scheda (`ActionCatalog.MaxPalette`),
quindi le azioni stanno in due schede nuove, dopo Vista:

- **Visibilità**: X-Ray, Isola, Nascondi, Mostra tutto.
- **Verifica**: Interferenze, Solo selezione (interruttore), Distanza minima,
  Salute, Risultati, Ignora risultato.

È attiva una sola verifica di Inventor alla volta. La lista Risultati è
condivisa e usa l'elenco a pagine già usato da Esplora e Documenti aperti.
Le azioni M7 non hanno comandi vocali (`voiceInvokes: false`).

Le sezioni che richiedono Inventor sono disabilitate quando:
- il backend è offline;
- il documento attivo non è un assieme;
- un'altra verifica è in corso.

Il motivo è sempre visibile.

## Flusso dati e stati

Ogni servizio con backend ha gli stati
`Idle → Running → Done | Failed`, più `Stale`.

- **Running**: indicatore di attesa con il tempo trascorso e il pulsante
  "Ignora risultato". Il pulsante scarta la risposta quando arriva; il calcolo
  in Inventor prosegue, e la UI lo scrive.
- **Done**: risultato più `revision` su cui è stato calcolato.
- **Stale**: su `DocumentStateChanged` con `revision` diversa da quella del
  risultato. La lista resta visibile ma grigia, con "Modello cambiato:
  rilancia". Si confronta `revision` e non `visual_revision`, perché anche un
  vincolo modificato senza effetti visivi invalida la salute.
- Una seconda richiesta mentre una è in corso viene rifiutata, non accodata.

## Errori

I codici del bridge diventano messaggi brevi nella HUD; il dettaglio va nel
log.
- `STALE_REVISION` / `DOCUMENT_CHANGED`: "Il modello è cambiato, rilancia"
  (nessun nuovo tentativo automatico).
- `TIMEOUT`: "Inventor sta ancora calcolando. Riprova tra poco o restringi
  alla selezione". Il client non può sapere quando Inventor finisce: per 30 s
  dopo un `TIMEOUT` rifiuta nuove verifiche con lo stesso messaggio. Dopo, una
  nuova richiesta parte e, se Inventor è ancora occupato, scade di nuovo.
- "Ignora risultato" vale allo stesso modo: la richiesta ignorata tiene
  occupato il client finché la sua risposta non arriva, e la risposta viene
  scartata.
- `WRONG_DOCUMENT_TYPE`: la sezione era già disabilitata; se capita
  comunque, "Serve un assieme".
- `EXPERIMENTAL_DISABLED`: "Verifiche non attive sul server".
- Altri errori: "Verifica non riuscita" più codice.

## Rischi da verificare prima del piano

1. **Durata di `AnalyzeInterference`.** Misurare il tempo sulla fixture e su
   almeno un assieme reale dell'utente (solo in lettura). Se un assieme
   tipico supera i 30 s, l'ambito predefinito diventa "solo selezione" e il
   piano lo registra.
2. **Punti della distanza minima.** Verificare dal vivo se l'interop
   restituisce i punti più vicini. L'esito decide solo l'etichetta della
   linea, non la consegna.
3. **Box di interferenza.** Verificare che `InterferenceBody.RangeBox` sia
   leggibile e in coordinate dell'assieme.
4. **Occorrenze dei vincoli in errore.** Verificare che `OccurrenceOne` e
   `OccurrenceTwo` siano leggibili su un vincolo in errore. Se non lo sono, la
   riga resta senza evidenziazione.

## Test e gate

Tre esiti sempre separati: test automatici senza hardware, runner sul Quest
con input sintetico, prova fisica.

**Test automatici**
- `bridge/tests/Bimwright.Ipt.Tests`: contratti e tier dei tre tool nuovi,
  sincronizzazione `ExperimentalSourceTests`, end-to-end con FakeAddIn
  (inoltro dei parametri, mappatura degli errori).
- `Tests~/XrSo.Core.Tests`: macchina a stati dei servizi, `Stale` su cambio
  di revisione, rifiuto della richiesta concorrente, "Ignora risultato",
  parsing delle risposte (punti assenti, box assenti), costruzione delle righe
  `VerifyFinding`.
- `Assets/XrSo/Tests/EditMode`: `ComponentVisibility` (stati, collider, reset
  su `Rebuilt`), azioni della scheda Verifica e loro abilitazione, selezione
  di una riga, `FocusBounds`, etichette delle due misure.

**Fixture Inventor dedicata** (temporanea, mai documenti dell'utente).
Assieme di primo livello con:
- due blocchi che si sovrappongono di un volume noto;
- due blocchi a distanza nota;
- un vincolo in errore costruito apposta;
- un componente non vincolato;
- una riga BOM senza numero di parte.

Il runner controlla questi valori noti, non la sola assenza di errori.
Tolleranze: volume di interferenza ±1% del valore costruito; distanza minima
±0,01 mm.

**Runner sul Quest** (`Xr/Acceptance/M7QuestAcceptance.cs`). Segue
`docs/xr-quest-acceptance.md`: gesti marcati **sintetici**, backend Inventor
reale dove il gate lo richiede, log `PASS [gate]` / `NOT COVERED [gate]`,
manifest, screenshot, ripristino dell'APK ordinario e del documento
precedente. Un timeout di avvio non è un fallimento del test.

| Gate | Criterio | Evidenza |
|---|---|---|
| M7-01 | X-Ray, isola, nascondi e mostra tutto funzionano senza backend; il raggio attraversa un componente nascosto e colpisce uno in fantasma | EditMode + runner |
| M7-02 | Sulla fixture trova esattamente la coppia attesa con volume entro tolleranza; rosso e box in scena; la riga mette a fuoco la coppia | runner + Inventor |
| M7-03 | Distanza minima uguale al valore noto entro tolleranza; linea tra i punti con etichetta corretta (Inventor o indicativa) | runner + Inventor |
| M7-04 | Salute: vincolo in errore, componente non vincolato e riga BOM senza numero rilevati; righe navigabili | runner + Inventor |
| M7-05 | Dopo una modifica del documento il risultato è `Stale`; "Ignora risultato" scarta la risposta; la seconda richiesta viene rifiutata | core + runner |
| M7-06 | Offline o su una parte: sezioni Inventor disabilitate con motivo; le due misure hanno etichette distinte | EditMode |
| M7-07 | Tempi di interferenze e salute misurati sulla fixture e su un assieme reale; comportamento corretto oltre i 30 s | Inventor reale |
| M7-08 | Leggibilità di rosso, fantasma, box, linea e lista da seduti con il visore; tracking reale dei controller | **prova fisica, resta aperta finché non eseguita** |

M7-08 non si chiude con test automatici o FakeAddIn. La richiesta fisica si
limita a ciò che il software non può osservare: leggibilità dei colori e
della lista, comfort nella lettura dei risultati.

I tre tool nuovi restano nel tier sperimentale finché M7-02, M7-03 e M7-04 non
passano dal vivo. La promozione segue la regola del tier sperimentale in
`bridge/CLAUDE.md`.

## Documenti

- Questa spec.
- Piano: `docs/superpowers/plans/2026-10-03-inventor-xr-so-m7-inspect-verifica.md`.
- Verbale: `docs/xr-m7-verification.md`.
- Aggiornamenti a fine milestone: `CLAUDE.md` (elenco milestone),
  `docs/DEVELOPMENT.md` (tool nuovi e stato del tier).
