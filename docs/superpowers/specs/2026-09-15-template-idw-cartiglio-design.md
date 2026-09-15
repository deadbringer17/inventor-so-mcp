# Template IDW aziendali e campi del cartiglio — design

Data: 2026-09-15
Stato: implementato (verifica live su Inventor 2027 ancora aperta)

## Problema

`create_drawing_safe` produceva sempre un disegno sul template Inventor di serie: foglio nudo,
cartiglio standard, nessun bordo aziendale. Per una messa in tavola che esca dallo studio serve il
cartiglio dell'azienda, con i suoi campi compilati. Due cose mancavano:

1. partire da un template `.idw`/`.dwg` aziendale invece che da quello di serie;
2. riempire i campi del cartiglio.

## Decisioni

### 1. I template stanno in una libreria governata, indirizzata per nome

Env `INVENTOR_SO_TEMPLATES`, default `%LOCALAPPDATA%\InventorSO\templates`. Il chiamante passa un
**nome file**, mai un percorso.

Il resto della superficie safe non accetta percorsi arbitrari, e il motivo vale qui più che altrove:
questo argomento arriva da un modello a cui è stato detto "usa il cartiglio aziendale". Un parametro
path lo renderebbe una primitiva di lettura file arbitraria. `DrawingTemplatePolicy` rifiuta
separatori, qualificatori di unità, `..` e nomi device riservati, e ricontrolla il percorso risolto
contro la radice. La libreria è in sola lettura: Inventor copia il template in un documento nuovo in
memoria, niente lì viene mai scritto.

Scartato: percorso assoluto libero (rompe la postura del bridge); cartella template di Inventor
(spesso sotto Program Files, non controllata dall'utente).

### 2. Con un template comanda il template

`sheet_size` e `orientation` insieme a `template` sono rifiutati con `INVALID_ARGUMENT`.

Non è purismo: bordo e cartiglio aziendali sono disegnati per **un** formato e non si riscalano
quando cambia `Sheet.Size`. Onorare l'override produrrebbe un disegno con la cornice fuori posto
riportando successo — peggio che rifiutare.

### 3. L'area utile è dichiarata in un manifest, non misurata

Sidecar con lo stesso stem del template (`Company_A3.idw` → `Company_A3.json`):

```json
{ "sheet_size": "A3", "orientation": "landscape",
  "usable_area_mm": { "x_min": 20, "y_min": 15, "x_max": 340, "y_max": 282 } }
```

Dichiarata perché il range box del cartiglio dice dove stanno i suoi grafismi, non quanto foglio
riserva la norma: distinte, tabelle di revisione e colonne note vengono piazzate dopo, in spazio che
nessun range box vede.

Il manifest è **dato**: numeri e due enumerazioni, nessun percorso, nessun testo libero che arrivi a
Inventor. Un manifest che dichiara un foglio diverso da quello realmente prodotto dal template
fallisce `TEMPLATE_SHEET_MISMATCH` — è esattamente il caso (manifest copiato sul template sbagliato)
che l'area dichiarata esiste per prevenire.

Senza template resta il calcolo precedente da `TitleBlock.RangeBox`.

### 4. Il planner ragiona su un rettangolo, non su una fascia

`SheetPlanner` e `DrawingLayout` lavorano su `UsableArea` (rettangolo + foglio). La vecchia riserva
in basso descrive solo un cartiglio a tutta larghezza in fondo; un cartiglio a colonna destra no.

Le firme vecchie restano come overload costruiti da `UsableArea.FromReservedBottom`, e un test di
regressione verifica che il percorso a rettangolo riproduca il layout a riserva-bassa scala per scala
e posizione per posizione. In più: una vista deve comunque stare `MinMarginCm` da ogni bordo del
foglio, così un manifest che dichiara tutto il foglio non può spingere una vista contro la carta. Il
suggerimento di formato riaggiunge lo spazio riservato, quindi nomina un foglio che contiene le viste
**e** il cartiglio.

### 5. I campi si indirizzano per nome di iProperty

`title_block` è un oggetto nome→valore. Un cartiglio Inventor mostra iProperties: i suoi campi di
testo sono legati a nomi di proprietà, non scritti nel foglio. Un vocabolario fisso
(title/designer/revision) riempirebbe il cartiglio di serie e lascerebbe vuoto quello aziendale, che
quasi sempre legge proprietà custom ("Commessa", "Disegnato Da"). Un nome che il documento non ha
viene creato come proprietà user-defined.

Limiti: 30 campi, nome 1–80, valore ≤255, niente caratteri di controllo, niente duplicati
case-insensitive, numeri formattati invarianti (mai `2,5`).

## Codici di errore

`TEMPLATE_NOT_FOUND`, `TEMPLATE_MANIFEST_MISSING`, `TEMPLATE_MANIFEST_INVALID`,
`TEMPLATE_SHEET_MISMATCH`, `TEMPLATE_UNUSABLE`, `TITLE_BLOCK_FIELD_REJECTED`. Ognuno è una cosa
diversa da sistemare per il chiamante — installare il file, scrivere il sidecar, correggerlo,
accoppiarlo al template giusto — quindi nessuno viaggia come prosa dentro un codice generico.

## Superficie

- `inventor_create_drawing_safe` + `template`, `title_block_json`
- `inventor_list_drawing_templates` (read-only): elenca la libreria con foglio e area dichiarati; un
  template con manifest rotto è elencato `usable: false` con la ragione, così il problema si vede in
  fase di scoperta e non di disegno.

## Rollback

Il preview continua a chiudere la bozza. Le scritture di iProperty in Inventor **non** stanno nella
transazione del documento — come la modifica di stile già nota in `ApplyProjection` — ma spariscono
col documento scartato, quindi `preview=true` resta senza residui. Il file template non viene mai
toccato.

## Test

676 unit/protocol verdi (erano 598): policy template (traversal, estensione, radice, nomi device),
manifest (parsing, area, verifica contro il foglio reale, tolleranza, portrait), campi cartiglio
(tipi, limiti, duplicati, controlli), `UsableArea` + planner (equivalenza con la riserva bassa,
colonna destra, area ridotta, suggerimento formato, margine carta). Le verifiche live sono i punti
10–17 di `bridge/docs/testing/manual-smoke.md`.

## Aperto

Verifica live con un template aziendale reale; un cartiglio che lega i campi attraverso un property
set custom; riscrittura dei campi su un disegno già creato (sarebbe un
`inventor_apply_title_block` separato).
