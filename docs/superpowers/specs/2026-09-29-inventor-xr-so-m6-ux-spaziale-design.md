# Inventor XR SO — M6: UX spaziale per la progettazione da seduto

Data: 29 settembre 2026. Stato: specifica di design approvata in sessione di
brainstorming; implementazione e accettazione M6 non ancora eseguite.

Riferimenti: [specifica prodotto](../../../Inventor%20XR%20SO/inventor_meta_product.md)
§52–58; [spec M5](2026-09-28-inventor-xr-so-m5-design.md);
[verifica M5](../../xr-m5-verification.md); [test automatici sul Quest](../../xr-quest-acceptance.md).
M6 non aggiunge capacità CAD: cambia come l'utente raggiunge quelle di M1–M5.
Le prove fisiche M5 ancora aperte (M5-12) restano gate M5 separati.

## Obiettivo e perimetro

Oggi ogni workspace costruisce da solo un pannello uGUI di circa 820×1100 mm
che fluttua accanto alla testa, organizzato come un wizard a pagine con sei
pulsanti di testo per pagina e paragrafi che spiegano i gesti. Si usa solo il
controller destro; il sinistro non ha ruolo. Il risultato copre il modello,
richiede 3–5 click per comando e non si capisce senza leggere.

M6 sostituisce questo modello con un'interfaccia spaziale pensata per
l'utente **seduto davanti al modello**, che usa soprattutto **Schizzo e
Lamiera** e, in Assieme, vuole l'assieme in primo piano. Input: **solo
controller** (Quest 3 Touch Plus).

Principi:

1. **Destra = penna, sinistra = tavolozza e vista.**
2. Il modello non è mai coperto da un pannello.
3. Un comando frequente costa 1–2 azioni: selezione e anello contestuale,
   oppure tavolozza.
4. Nessun gesto viene spiegato da paragrafi: si capisce da forma, colore,
   evidenziazione e aptica.
5. Si modifica il CAD in un solo posto: la barra di conferma.

Non rientrano in M6: hand tracking, Meta Interaction SDK, set di icone
grafiche (le chiavi icona esistono nel catalogo ma M6 mostra etichette),
nuovi comandi CAD, modifiche al backend `bridge/`, temi chiaro/scuro,
personalizzazione della tavolozza da parte dell'utente.

## Stato della base

- `Runtime/Ui/UiFactory.cs`: uGUI costruito in codice, font `LegacyRuntime.ttf`,
  canvas world-space con 1 unità = 1 mm. Nessun prefab.
- `Xr/DesignWorkspace.cs` (837 righe), `Xr/LamieraWorkspace.cs` (1043),
  `Xr/AssemblyWorkspace.cs` (499), `Xr/InspectWorkspace.cs` (595): ognuno
  mescola logica di dominio, costruzione del pannello (`Render()`, `Page(...)`)
  e letture dirette di `OVRInput`.
- `InspectWorkspace` ha un menù polso (`_wrist`) da cui partono Progettazione
  e gli altri workspace; il runner M3 lo usa.
- La voce (`WorkspaceVoiceTarget`, `VoiceCommandRouter`) risolve i comandi
  scandendo le etichette dei `Button` visibili.
- Pacchetti: `com.meta.xr.sdk.core` 207, `com.unity.ugui` 2.6 (TextMeshPro
  incluso). Nessun Interaction SDK.

Approccio scelto (tra guscio comune incrementale, Meta Interaction SDK e
riscrittura completa): **guscio UI comune e migrazione un workspace alla
volta**, senza nuove dipendenze. Il Meta Interaction SDK è basato su prefab,
mentre il progetto costruisce tutto in codice con scena generata; avrebbe
richiesto di riscrivere tutto l'input insieme e di riagganciare i runner M1–M5.

## Disposizione spaziale

Tutte le pose derivano da un **frame postazione** calcolato alla prima
attivazione dalla posa della testa (posizione e yaw, senza pitch/roll). Il
frame non segue la testa; «Ricentra» lo ricalcola. Calibrazione opzionale:
toccando la scrivania reale con la punta del controller destro e confermando,
l'altezza del piano di lavoro si allinea a quel punto.

```
            HUD stato (striscia sottile, ~15° sopra l'orizzonte, segue lenta)

        ASSIEME: modello sollevato (~65 cm davanti, box ~80 cm)
                      [componente isolato → avanza verso l'utente]

   tavolozza    ─────────── PIANO DI LAVORO (altezza avambracci) ───────────
   (controller  │  parte / lamiera: modello posato, box ~40 cm              │
    sinistro)   │  schizzo: foglio orizzontale ~45×30 cm, penna destra      │
                └──────────── [ Anteprima · Applica · Annulla ] ────────────┘
                                   barra di conferma (bordo vicino)
```

| Elemento | Posa e dimensione |
|---|---|
| Piano di lavoro | centro a 40 cm davanti al frame, altezza avambracci (default: testa − 45 cm, o calibrata) |
| Parte / Lamiera | modello posato sul piano, scalato per stare in un cubo di 40 cm |
| Foglio schizzo | piano dello schizzo ribaltato orizzontale sul piano di lavoro, ~45×30 cm, scala adattata all'estensione dello schizzo |
| Assieme | centro a 65 cm davanti, testa − 15 cm, box di 80 cm |
| Componente isolato | a metà strada tra assieme e utente; resto dell'assieme al 20% di opacità |
| Barra di conferma | centrata sul bordo vicino del piano di lavoro, inclinata verso l'utente |
| HUD | striscia ~15° sopra l'orizzonte, lazy follow (segue lo yaw solo oltre ±25°) |
| Tavolozza | sulla faccia del controller sinistro, ~12×9 cm, rivolta verso la testa |
| Chip valore | accanto alla maniglia/quota, billboard verso la testa |
| Anello contestuale | attorno al punto selezionato, massimo 6 azioni, raggio ~6 cm |

Transizioni tra modalità (tavolo, foglio, assieme sollevato, isolamento):
interpolazione di posa e scala di ~250 ms; nessun salto istantaneo.

Leggibilità: tutto il testo è TextMeshPro SDF. Altezza minima delle
maiuscole: 14 mm sul piano di lavoro e sulla barra, 8 mm sulla tavolozza
(~30 cm dagli occhi). Bersagli puntabili di almeno 25 mm sul piano, 15 mm
sulla tavolozza.

### Schizzo: tavolo da disegno

Entrando in uno schizzo, il piano dello schizzo si posa orizzontale sul piano
di lavoro come un foglio, con l'asse X dello schizzo verso destra e Y lontano
dall'utente; il modello ruota di conseguenza. Si disegna con la **punta del
controller destro come una penna**: il punto viene proiettato sul foglio
quando la punta è entro 2 cm dal piano; oltre, vale il raggio. Il thumbstick
sinistro fa zoom e scorrimento del foglio.

Il pulsante «Vista modello» (tavolozza, scheda Schizzo) riporta il modello
nell'orientazione 3D con lo schizzo nel suo contesto; un secondo tocco
(«Foglio») torna al tavolo da disegno. Le coordinate CAD dello schizzo non
cambiano: cambia solo la trasformazione di visualizzazione.

### Assieme: primo piano e isolamento

In Assieme il modello si solleva nella posa «assieme» (tabella sopra).
Selezionando un componente, l'anello contestuale offre «Isola»: il componente
avanza verso l'utente e il resto diventa semitrasparente. Da isolato sono
disponibili «Apri in Progettazione» e, per parti lamiera, «Apri in Lamiera»,
che attivano il documento del componente con i percorsi già esistenti.
«Rilascia» (o X) riporta il componente al suo posto. L'isolamento è solo
visivo: non modifica la posa dell'occorrenza in Inventor.

## Mappatura dei controller

**Controller destro (penna)**

| Input | Azione |
|---|---|
| Trigger | seleziona; disegna un punto nello schizzo; preme i pulsanti. Tenuto su una maniglia la trascina: modifica solo la bozza, mai il CAD |
| Grip | afferra e sposta/ruota il modello con una mano (solo vista) |
| Thumbstick ←/→ | puntando una maniglia o un chip: ± un passo |
| Thumbstick ↑/↓ | cambia passo: 10 / 1 / 0,1 mm (gradi per gli angoli), mostrato sul chip |
| A | snap sì/no |
| B | push-to-talk (invariato da M5) |

**Controller sinistro (tavolozza)**

| Input | Azione |
|---|---|
| Tavolozza | sempre visibile sulla faccia del controller; si sceglie con la penna |
| Thumbstick ←/→ | scheda precedente/successiva |
| Thumbstick ↑/↓ | zoom del foglio schizzo o del modello |
| Trigger tenuto | modalità precisione: i trascinamenti della penna sono 10 volte più lenti |
| X | Annulla/Indietro: chiude anello o tastierino, scarta l'ultimo passo di bozza |
| Y | Adatta vista; tenuto 1 s: Ricentra la postazione |
| Grip sinistro + grip destro | manipolazione a due mani: ruota, scala, sposta (solo vista) |

**Modifica rispetto a M5.** Il trascinamento della maniglia (flangia, quota,
estrusione) passa da Grip + Trigger al solo Trigger tenuto sulla maniglia.
Resta sicuro perché il trascinamento cambia solo la bozza; nel CAD entra solo
con «Applica» sulla barra di conferma. Il rilascio del Trigger, la perdita di
tracking e il cambio di workspace chiudono la cattura (contratto M5-08,
da riverificare con la nuova mappatura).

**Aptica.** Tick breve a ogni scatto di snap o passo; impulso breve quando il
raggio entra su un bersaglio interattivo; doppio impulso su «Applica»
riuscito; impulso lungo su errore o rifiuto.

## Tavolozza, tastierino, chip e anello

**Tavolozza.** Mostra le schede del workspace attivo più una scheda fissa
**«Spazi»** (Ispeziona, Progettazione, Lamiera, Assieme, Connessione), che
sostituisce il menù polso attuale. Schede per workspace:

| Workspace | Schede |
|---|---|
| Progettazione | Schizzo · Feature · Parametri · Vista · Spazi |
| Lamiera | Lamiera · Schizzo · Sviluppo · Vista · Spazi |
| Assieme | Componenti · Vincoli · Vista · Spazi |
| Ispeziona | Misura · Sezione · Vista · Spazi |

Il contenuto esatto di ogni scheda è l'insieme di azioni già offerte oggi dal
workspace, redistribuito; M6 non aggiunge né toglie comandi CAD. Una scheda
mostra al massimo 8 pulsanti in griglia 2×4; se servono di più la scheda si
divide, senza paginazione «Precedenti/Successivi».

**Tastierino.** Toccando un chip valore, la tavolozza passa in modalità
tastierino: cifre, virgola, segno, cancella, «OK», «Annulla». Il chip si
aggiorna mentre si digita; «OK» aggiorna la bozza, X o «Annulla» ripristina
il valore precedente. La voce (dettatura M5) scrive nello stesso campo.

**Chip valore.** Mostra valore, unità e passo corrente; bordo blu se modificato
e non ancora in anteprima. Uno solo è «armato» alla volta (quello che riceve
tastierino, thumbstick e dettatura).

**Anello contestuale.** Compare dopo una selezione con le azioni pertinenti:

| Selezione | Azioni (esempi, secondo il workspace) |
|---|---|
| Faccia piana | Schizzo · Estrudi · Foro · Misura |
| Bordo | Raccordo · Smusso · Flangia · Misura |
| Componente | Isola · Sposta · Vincola · Apri |
| Nulla | l'anello non compare |

Si chiude con X, selezionando il vuoto o scegliendo un'azione.

## Barra di conferma e stati

Unica per tutti i workspace, stessa posa. Stati derivati dalla sessione del
workspace:

| Stato | Aspetto | Azioni ammesse |
|---|---|---|
| Vuoto | nascosta o compatta | — |
| Bozza | neutra | Anteprima · Annulla |
| Anteprima in corso | blu, indicatore di attività | — |
| Pronto | verde tenue, anteprima fantasma blu sul modello | Applica · Annulla |
| Stale | ambra | Aggiorna documento |
| Esito incerto | rosso | Ho controllato il CAD |
| Errore / rifiuto | rosso, messaggio breve | Annulla · (Riprova se sensato) |
| Offline | grigio | — |
| Applicato | verde, ~1,5 s poi Vuoto | — |

Le regole di abilitazione sono quelle di oggi (`RefreshRequired`,
`CommitOutcomeUnknown`, `_pendingMutations`, online/offline, revisione):
M6 le mostra in modo uniforme, non le cambia. Le azioni della tavolozza che
mutano il CAD si disabilitano con lo stesso criterio e mostrano il motivo al
passaggio del raggio. Il dettaglio di un errore va sull'HUD. La voce
«Applica» continua a non committare (M5-11).

## Architettura

### Core senza Unity

Nuova cartella `Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/`,
coperta da `Tests~/XrSo.Core.Tests`.

| Unità | Responsabilità |
|---|---|
| `XrAction` | id stabile (es. `design.extrude`), etichetta IT, chiave icona, scheda, `Enabled` + motivo di disabilitazione, sinonimi vocali, tipo (Comando / Interruttore / Numerico), `Invoke` |
| `IActionProvider` | implementato da ogni workspace: `Tabs`, `Palette(tab)`, `ContextActions(selezione)`, `CommitBar` |
| `ActionCatalog` | aggrega le azioni del workspace attivo più la scheda Spazi; rifiuta etichette/sinonimi ambigui tra azioni abilitate; risolve id e frasi vocali |
| `CommitBarState` | macchina a stati della barra (tabella sopra) con transizioni esplicite |
| `NumericEntry` | valore, unità, passo, buffer del tastierino, limiti min/max, conferma/annullamento; la voce usa `ItalianNumberParser` |
| `WorkbenchLayout` | dato il frame postazione e i bounding box, calcola pose e scale di piano, foglio schizzo, assieme e isolamento |

### Unity

| Unità | Percorso | Responsabilità |
|---|---|---|
| `XrInput` | `Assets/XrSo/Xr/Input/` | unico lettore di `OVRInput`; pubblica eventi semantici: PenPress/Hold/Release, Grab, StepDelta, StepSize, TabDelta, Zoom, Precision, Back, Fit, Recenter, TwoHand. Accetta eventi sintetici marcati come tali |
| `UiShell` | `Assets/XrSo/Runtime/Ui/Shell/` | costruisce e aggiorna dal catalogo tavolozza/tastierino, chip, anello, barra di conferma, HUD |
| `Workbench` | `Assets/XrSo/Xr/` | applica le pose di `WorkbenchLayout` con le transizioni animate; gestisce Adatta, Ricentra, calibrazione |
| `SketchSheetView` | `Assets/XrSo/Runtime/Scene/` | trasformazione foglio ↔ vista modello; proiezione della punta penna sul foglio |
| `ComponentIsolation` | `Assets/XrSo/Runtime/Scene/` | avanzamento del componente e trasparenza del resto |
| `UiFactory` | esistente | passa a TextMeshPro; stili per stato |
| `Haptics` | `Assets/XrSo/Xr/Input/` | profili aptici nominati (tick, hover, successo, errore) |

I workspace mantengono la logica di dominio (sessioni, bozze, chiamate al
backend, cronologia XR). Perdono `Render()`/`Page()`, i testi di istruzione e
le letture di `OVRInput`; diventano `IActionProvider` e consumano gli eventi
di `XrInput`. `HomePanel` mantiene il flusso di pairing e connessione, passa a
TextMeshPro e viene raggiunto dalla scheda Spazi → Connessione.

### Flusso

```
OVRInput → XrInput ─┬─ colpisce UI      → UiShell → XrAction.Invoke → Workspace → sessione → backend
                    └─ colpisce modello → Workspace (selezione, maniglie, disegno)
Workspace (stato cambiato) → ActionCatalog / CommitBarState → UiShell aggiorna
Voce (B) → ActionCatalog.Resolve → stessa XrAction.Invoke
Runner Quest → eventi sintetici XrInput + XrAction per id
```

La voce smette di scandire i `Button` visibili: `WorkspaceVoiceTarget`
interroga `ActionCatalog`, che usa etichette e sinonimi dichiarati. Le regole
M5 restano: vocabolario finito, ambiguità rifiutata, comando disabilitato
senza mutazione.

## Errori e casi limite

- **Tracking perso durante un trascinamento**: cattura chiusa, bozza
  all'ultimo valore valido, impulso di errore.
- **Controller sinistro spento o non tracciato**: la tavolozza appare
  ancorata al bordo sinistro del piano di lavoro finché il controller non
  torna; nessuna funzione persa.
- **Cambio documento, revisione o connessione**: come in M5, invalida
  selezione, bozza, anteprima, chip armato e anello; il foglio schizzo torna
  alla vista modello se lo schizzo non esiste più.
- **Schizzo più grande del foglio**: scala adattata; zoom col thumbstick
  sinistro; le quote restano in mm reali sul chip.
- **Modello molto piccolo o molto grande**: la scala di adattamento ha limiti
  (min 1:1000, max 10:1); oltre, «Adatta» mostra la scala sull'HUD.
- **Richiesta in corso**: nuovi comandi CAD disabilitati; l'annullamento di una
  bozza resta ammesso.
- **Ambiguità vocale**: rifiutata; l'HUD mostra le azioni in conflitto.

## Test

- **Core** (`XrSo.Core.Tests`): `ActionCatalog` (ambiguità, sinonimi,
  disabilitate, scheda Spazi), `CommitBarState` (tutte le transizioni),
  `NumericEntry` (buffer, virgola, segno, limiti, annulla), `WorkbenchLayout`
  (pose e scale, limiti, isolamento).
- **Unity EditMode**: `UiShell` costruita dal catalogo; eventi sintetici di
  `XrInput` → azioni; transizioni di `Workbench`; foglio ↔ vista modello
  senza cambiare le coordinate CAD.
- **Runner Quest**: i runner M1–M5 migrano da «premi il pulsante con
  l'etichetta X» a «invoca l'azione con id X» più eventi sintetici di
  `XrInput`, marcati **sintetici** nel log. Nuovo runner M6 per i sottocasi
  riproducibili di M6-01…M6-09, su fixture dedicata.
- **Prova fisica** da seduto: leggibilità, comfort, precisione della penna sul
  foglio, aptica, tavolozza sul controller reale. Resta **aperta** finché non
  viene eseguita.

## Fasi di consegna

Ogni fase lascia l'app funzionante e i runner esistenti verdi.

1. **Fondamenta**: core (`XrAction`, `ActionCatalog`, `CommitBarState`,
   `NumericEntry`, `WorkbenchLayout`), `XrInput`, `Haptics`, `UiShell` con
   tavolozza, barra di conferma e HUD; `UiFactory` in TextMeshPro. I
   workspace non ancora migrati continuano a funzionare col pannello attuale.
2. **Progettazione + Schizzo**: migrazione di `DesignWorkspace`; foglio sul
   tavolo con «Vista modello»; chip e tastierino; anello contestuale.
3. **Lamiera**: migrazione di `LamieraWorkspace`; flangia con trascinamento a
   Trigger; sviluppo piano posato sul piano di lavoro.
4. **Assieme + Ispezione**: assieme sollevato, isolamento, migrazione degli
   ultimi due workspace; scheda Spazi sostituisce il menù polso; voce sul
   catalogo.
5. **Collaudo**: runner M1–M6 e prova fisica.

## Gate di consegna M6

| ID | Criterio |
|---|---|
| M6-01 | Tavolozza sul controller sinistro in tutti i workspace; nessun pannello fluttuante accanto alla testa; scheda Spazi sostituisce il menù polso. |
| M6-02 | Schizzo sul foglio orizzontale; disegno con la punta penna e col raggio; «Vista modello» ↔ «Foglio» senza variare le coordinate CAD. |
| M6-03 | Chip valore e tastierino in tavolozza; thumbstick ± passo con passo 10/1/0,1; modalità precisione; dettatura nel campo armato. |
| M6-04 | Anello contestuale per faccia, bordo e componente con le azioni pertinenti; chiusura con X o selezione del vuoto. |
| M6-05 | Barra di conferma unica con tutti gli stati della tabella; Applica solo da barra; stale/incerto/offline non abilitano Applica. |
| M6-06 | Assieme sollevato; isolamento del componente solo visivo; apertura in Progettazione/Lamiera dal componente isolato. |
| M6-07 | Manipolazione a una e due mani solo vista; Adatta; Ricentra; calibrazione dell'altezza del piano. |
| M6-08 | Voce risolta su `ActionCatalog`; ambiguità e comandi disabilitati rifiutati senza mutazione; `Applica` vocale non committa. |
| M6-09 | Runner M1–M5 migrati `PASS COMPLETE` su Quest con Inventor reale; runner M6 `PASS COMPLETE` sui sottocasi riproducibili. |
| M6-10 | Prova fisica da seduto con controller: leggibilità, comfort su una sessione di 30 minuti, precisione della penna, aptica, trascinamento a Trigger e rilascio (M5-08). |

Un gate fisico non eseguito resta aperto; i risultati vanno in
`docs/xr-m6-verification.md`.

## Risposte alla regola §57

1. **Entità CAD**: le stesse di M1–M5; M6 cambia solo accesso e presentazione.
2. **Contesto Inventor**: invariato; il catalogo riflette documento, revisione
   e connessione tramite l'abilitazione delle azioni.
3. **Workspace**: tutti e quattro, migrati in fasi.
4. **Interazione controller**: penna destra, tavolozza sinistra (tabelle sopra).
5. **Grab semplice**: solo vista (una o due mani).
6. **Intenzionalità della modifica CAD**: solo «Applica» sulla barra di
   conferma, dopo un'anteprima valida.
7. **Input numerico**: chip + tastierino in tavolozza, thumbstick a passi,
   dettatura.
8. **Voce**: push-to-talk B, risolta su `ActionCatalog`.
9. **Preview**: fantasma blu sul modello, stato «Pronto» sulla barra.
10. **Validator**: quelli esistenti di M3–M5, invariati.
11. **Errori**: barra di conferma (breve) + HUD (dettaglio) + aptica.
12. **Revisione stale**: barra ambra, solo «Aggiorna documento».
13. **Connessione caduta**: barra grigia Offline, azioni CAD disabilitate.
14. **Riconoscibilità per l'utente Inventor**: etichette e comandi con i nomi
    italiani di Inventor; anello contestuale analogo alla mini-toolbar.
