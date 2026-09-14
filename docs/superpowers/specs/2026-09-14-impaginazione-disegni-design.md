# D1 — Motore di impaginazione disegni

Data: 2026-09-14
Stato: approvato, pronto per il piano di implementazione
Ambito: `bridge/` — Inventor SO MCP

## Contesto

L'obiettivo generale è esportare disegni chiari per la produzione. L'analisi dello stato
attuale ha individuato quattro sotto-progetti indipendenti:

| # | Sotto-progetto | Contenuto | Dipende da |
|---|---|---|---|
| **D1** | Motore di impaginazione | formato foglio, orientamento, diedro, set di viste, auto-scala | — |
| D2 | Leggibilità produzione | cartiglio da iProperties, centerline, quote recuperate, draft standard | D1 |
| D3 | Viste derivate | sezione, dettaglio, interruzione, crop | D1 |
| D4 | Assiemi | parts list e ballooning | D1, D2 |

Questa spec copre **solo D1**. Ogni altro sotto-progetto avrà la sua spec e il suo piano.

### Stato attuale del codice

`inventor_create_drawing_safe` → `bridge/src/shared/Handlers/Core/CreateDrawingHandler.cs`:

- foglio A3 landscape fisso, template di default dell'host
- quattro viste fisse (fronte, due proiettate, iso top-right) a coordinate hardcoded
  (27 %/73 % × 36 %/74 % del foglio)
- stile unico `kHiddenLineRemovedDrawingViewStyle`, `ShowLabel = false`
- scala obbligatoria a carico del chiamante, nessun adattamento
- `bridge/src/shared/Infrastructure/DrawingLayout.cs` valida **a posteriori**: fuori margine o
  sovrapposizione producono `VIEW_OUTSIDE_LAYOUT` / `VIEW_OVERLAP`, con il suggerimento di
  riprovare a scala minore
- `manufacturing_ready = false`, `dimensions_added = 0`

### Il difetto di correttezza che D1 risolve

`AddProjectedView` piazza una vista sopra il fronte e una a destra
(`CreateDrawingHandler.cs:55-56`). Il significato di quelle viste non dipende dalla posizione ma
dallo standard di disegno del template host:

- terzo diedro (ANSI): sopra = pianta, destra = vista da destra
- primo diedro (ISO/UNI): sopra = vista da sotto, destra = vista da sinistra

Stesse coordinate, disegno opposto. Il tool oggi non legge né imposta lo standard, quindi
l'output è corretto o ribaltato a seconda della macchina, in silenzio. Per una tavola che va in
officina è un difetto di correttezza.

## Decisioni

1. **Diedro forzato.** Il tool imposta esplicitamente il primo diedro (ISO/UNI) sul disegno
   creato; parametro `projection` per l'override. Output deterministico su qualsiasi macchina.
   Se il template non consente la modifica: errore esplicito, nessun ripiego silenzioso.
2. **Auto-scala con corridoio quote.** La modalità automatica percorre la scala normalizzata
   ISO 5455 dal gradino più grande al più piccolo e prende il primo che fa entrare le viste
   lasciando un corridoio libero attorno a ciascuna. Senza quel corridoio la tavola non è
   quotabile in D2.
3. **Set di viste esplicito, con default.** `views` è una lista comma-separated; omessa, vale
   `front,top,right,iso`, cioè il comportamento odierno. Nessun vocabolario di preset da
   mantenere, ma un modello debole può ignorare il parametro e ottenere una tavola sensata.
4. **Un foglio solo.** Il multi-foglio serve davvero con sezioni (D3) e distinte (D4):
   anticiparlo è complessità senza consumatore.
5. **Nessuna rottura di firma.** `scale` diventa opzionale nullable; omessa significa auto. I
   chiamanti esistenti continuano a funzionare invariati.
6. **Nessuna voce in `CadBatchCommandCatalog`.** D1 resta un comando singolo in una sola
   transazione. La voce si aggiunge quando le operazioni disegno diventano più d'una.

## Architettura

### Misurare prima di piazzare

Il corridoio quote impone di conoscere l'ingombro di ogni vista prima di scegliere scala e
posizioni. Ma `DrawingView.Width` / `Height` esistono solo dopo che la vista è stata creata e
aggiornata. Approcci valutati:

- **Stima analitica dal `RangeBox` del modello.** Nessuna vista creata, veloce. Ma il bounding
  box assiale non è la sagoma proiettata, l'isometrica non si deriva in modo semplice e la stima
  sbaglia in eccesso proprio sui pezzi dove la scala è critica. Scartato.
- **Crea, misura, riposiziona, ripeti per ogni scala candidata.** Esatto ma lento e fragile:
  un `Update2()` per gradino su un documento COM.
- **Misura una volta a scala di riferimento, poi risolvi in forma chiusa.** Scelto.

L'approccio scelto sfrutta il fatto che **l'ingombro di una vista scala linearmente con la sua
scala**:

1. crea ogni vista richiesta una sola volta alla scala di riferimento, il gradino più basso
   della scala normalizzata, che entra per costruzione

La scala normalizzata usata è, dal più grande al più piccolo:

```
10:1  5:1  2:1  1:1  1:2  1:5  1:10  1:20  1:50  1:100  1:200  1:500
```

Il gradino di riferimento è quindi 1:500. Il limite superiore resta quello già imposto da
`DrawingLayout.ValidateScale` (scala finita, positiva, al più 100), che continua a valere anche
per il valore esplicito.

2. un `Update2()`, poi leggi `Width` / `Height` di ciascuna e normalizza a scala unitaria
   (`w₁ = w_ref / s_ref`)
3. il planner risolve in aritmetica pura, fuori da Inventor: per ogni gradino dal più grande al
   più piccolo prova il layout con ingombri `w₁·s` più corridoio, e prende il primo che entra
4. applica scala e posizioni alle viste già create, secondo `Update2()`
5. rivalida con `DrawingLayout.Validate` sulle misure reali

Due `Update2()` in totale, indipendenti dal numero di gradini provati. Il passo 5 è ridondante
per costruzione ed è voluto: se il planner sbaglia, la creazione fallisce invece di consegnare
una tavola sbagliata.

### Separazione dei componenti

La logica che vale è aritmetica, non COM, e va dove i test la compilano senza Inventor — come
già fa `DrawingLayoutTests`.

```
bridge/src/shared/Infrastructure/DrawingLayout.cs   # invariato nel ruolo: asserzione finale
bridge/src/shared/Infrastructure/SheetPlanner.cs    # NUOVO — puro, nessun using Inventor
    Plan(sheetW, sheetH, reservedFooter, reservedBorder,
         viewExtentsAtUnitScale[], slots[], gutterCm, ladder[])
      -> PlanResult { scale, positions[] } | NoFittingScale { suggestedSheet }
```

`CreateDrawingHandler` resta sottile: parsing argomenti, standard di proiezione, creazione
viste, misura, chiamata a `SheetPlanner`, applicazione, validazione, commit o abort.

Contratto di `SheetPlanner`: funzione pura, nessuno stato, nessuna dipendenza da Inventor.
Ingressi in centimetri di foglio (unità interna dell'API Inventor, coerente con il resto del
codice); il parametro utente `gutter_mm` è convertito al confine dell'handler secondo la
convenzione già stabilita in `shared/Handlers/UnitConvert.cs`.

### Proiezione e assegnazione degli slot

Griglia 3×3 ancorata al fronte. Il significato degli slot è invertito fra i due diedri:

| vista | primo diedro (default) | terzo diedro |
|---|---|---|
| `top` (pianta) | sotto il fronte | sopra |
| `bottom` | sopra | sotto |
| `left` | a destra | a sinistra |
| `right` | a sinistra | a destra |
| `back` | colonna esterna | colonna esterna |
| `iso` | angolo libero, nessun vincolo di proiezione | idem |

Larghezza di colonna e altezza di riga pari al massimo ingombro della cella più il corridoio.
Con il default `front,top,right,iso` la tavola prodotta su un template ANSI sarà **diversa da
quella di oggi**: è precisamente il difetto corretto.

La zona riservata in basso continua a derivare dal cartiglio reale
(`sheet.TitleBlock.RangeBox`), con il minimo di 40 mm già previsto; si aggiunge la riserva del
bordo foglio quando presente.

### Superficie del tool

`inventor_create_drawing_safe`, tutti i parametri nuovi opzionali:

| parametro | default | note |
|---|---|---|
| `scale` | `null` → auto | era obbligatorio; il valore esplicito è ancora accettato e validato come prima |
| `sheet_size` | `"A3"` | A4…A0 |
| `orientation` | `"landscape"` | `landscape` \| `portrait` |
| `projection` | `"first"` | `first` \| `third` |
| `views` | `"front,top,right,iso"` | comma-separated |
| `gutter_mm` | `15` | corridoio quote |
| `preview` | `true` | invariato |

La risposta aggiunge `projection`, le `views` effettive, `gutter_mm` e la `scale` **risolta**: il
chiamante deve sapere cosa è uscito. `manufacturing_ready` resta `false` — le quote sono D2.

Il parametro `views` segue lo stile dei parametri primitivi già in uso nei tool
(cfr. `dxf_version` in `SafeArtifactTools`), non JSON: è più facile da produrre per un modello
debole. Validazione: nomi ammessi `front`, `back`, `top`, `bottom`, `left`, `right`, `iso`;
confronto senza distinzione di maiuscole, spazi attorno alle virgole ignorati. Lista vuota,
nome sconosciuto, duplicato, o assenza di `front` quando è richiesta almeno una vista proiettata
(`top`, `bottom`, `left`, `right`, `back`) producono `INVALID_ARGUMENT`: le viste proiettate
esistono solo come figlie di una vista base.

### Errori

Nuovi, nei `details` machine-readable già previsti dal contratto:

- `NO_FITTING_SCALE` — nessun gradino entra nemmeno al minimo. Il payload indica quale formato
  foglio basterebbe, così il chiamante riprova con `sheet_size` maggiore invece di indovinare.
- `PROJECTION_UNAVAILABLE` — lo standard del disegno non è modificabile.

`VIEW_OUTSIDE_LAYOUT` e `VIEW_OVERLAP` restano ma diventano asserzioni interne: con il planner
non devono più raggiungere l'utente.

Invariati tutti i guard esistenti dell'handler: `READ_ONLY`, `WRONG_DOCUMENT_TYPE`,
`DOCUMENT_CHANGED`, `STALE_REVISION`, `TRANSACTION_BUSY`, `SOURCE_CHANGED`, scadenza deadline,
chiusura della sola bozza di proprietà del tool, ripristino di
`UserInterfaceManager.UserInteractionDisabled`.

## Test

**xUnit senza Inventor** — il grosso della copertura, su `SheetPlanner`:

- scelta del gradino corretto sulla scala ISO, in riduzione e in ingrandimento
- corridoio rispettato fra viste adiacenti e verso i margini
- inversione degli slot fra primo e terzo diedro
- `NO_FITTING_SCALE` con formato foglio suggerito
- vista singola, due viste, sei viste
- ingombri degeneri e valori non finiti
- tutti i formati da A4 ad A0, landscape e portrait
- riserva cartiglio e bordo
- parsing di `views`: nomi validi, maiuscole, spazi, lista vuota, duplicati, nome sconosciuto,
  viste proiettate senza `front`

**Smoke-test su Inventor 2027 live** — richiesto da `bridge/CLAUDE.md` per ogni handler che
tocca l'API. Tre cose da verificare lì, non deducibili a tavolino:

1. che una vista creata alla scala di riferimento minima si misuri in modo affidabile;
2. che `Width` / `Height` scalino davvero linearmente con `DrawingView.Scale`;
3. che la proprietà di proiezione sia scrivibile su un template di default. **Il nome è ora
   verificato**: `DrawingStylesManager.ActiveStandardStyle` è un `DrawingStandardStyle` che espone
   `FirstAngleProjection` di tipo `bool`; non esiste alcun `ProjectionType`, e `ProjectionTypeEnum`
   riguarda ortografica/prospettica, non il diedro. Resta da verificare a runtime solo se lo stile
   attivo accetta la scrittura o va reso locale al documento prima.

Se la linearità non regge, si ripiega sull'approccio crea-misura-ripeti: `SheetPlanner` resta
identico, cambia solo chi lo alimenta. Questo è il motivo per cui il planner è isolato.

## Fuori ambito

Quote, cartiglio compilato, centerline e center mark, sezioni e dettagli, parts list e
ballooning, multi-foglio, opzioni di export PDF. Sono D2, D3 e D4.
