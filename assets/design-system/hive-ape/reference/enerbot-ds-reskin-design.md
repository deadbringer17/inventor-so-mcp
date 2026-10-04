# Design Spec: EnerBot DS Reskin — Dashboard, HIVE, TwinCells

**Data:** 2026-06-26  
**Approccio:** A — Reskin progressivo, server-rendered, zero nuove dipendenze  
**Riferimento mockup:** `sitoweb/Riorganizzazione dashboard robot-handoff/riorganizzazione-dashboard-robot/project/Dashboard A.P.E.dc.html`

---

## Obiettivo

Portare la piattaforma Django APE a essere graficamente identica al mockup approvato, adottando il design system EnerBot già in uso sul sito pubblico. Tre pagine vengono ridisegnate: dashboard principale, sezione HIVE, sezione TwinCells.

---

## 1. CSS Foundation

**File coinvolti:** `static/`, `templates/base.html`

Il DS EnerBot è già presente in `static/enerbot-design-system-ffcaf531-f3c5-4ef1-b439-d62f1d1551ef/`. Si caricano due file nel `<head>` di `base.html`:

```
_ds/colors_and_type.css      → variabili CSS (--bg, --surface, --primary, --text, --muted, ecc.) + font Satoshi
_ds/ui_kits/website/styles.css → componenti base (bottoni, badge, card, eyebrow, ecc.)
```

Il font Satoshi è servito dal bundle locale (`fonts/Satoshi-Variable.ttf`), niente Google Fonts.

Si aggiunge un file `static/app-overrides.css` per le classi specifiche della piattaforma non coperte dal bundle DS (`.eb-status-card`, `.eb-spark`, `.eb-pin`, animazioni pulse/spin). `app.css` viene disattivato (non cancellato) dal `<link>` in `base.html`.

**Variabili colore stato** usate ovunque:
- Online/OK: `#2ee08e`
- Allerta/Stale: `#fdd11b`
- Offline: `#f0637c`

---

## 2. Header — `base.html`

**Struttura:** navbar sticky, `height: 72px`, `background: rgba(13,32,48,.82)`, `backdrop-filter: blur(18px)`, `border-bottom: 1px solid rgba(255,255,255,.06)`.

**Sinistra:** `Logo_mark.svg` (da `static/images/web/`) + testo "EnerBot" in bold + label "Operational workspace" in font-mono separata da divisore verticale.

**Centro:** nav link: Dashboard / HIVE / TwinCells / Sito pubblico. Link corrente con `aria-current="page"` e `color: #fff font-weight: 600`. Link inattivi `color: rgba(255,255,255,.7)`. Visibili solo se `user.is_authenticated` (eccetto Sito pubblico).

**Destra:** pill utente `(border-radius: 100px, background: rgba(255,255,255,.06))` con iniziale del customer, nome cliente, ruolo ("Operatore" per clienti normali, "Admin" per `is_enerbot_admin()`). Link Logout in font-mono.

**`<main>`:** rimosso padding precedente, `max-width: 1240px` centrato, `padding: 0 28px`.

---

## 3. Dashboard `/dashboard/`

**View:** `portal/views.py` — `DashboardView` (o funzione esistente) estesa con questi context data aggiuntivi.

### 3a. Dati aggiunti al context

| Chiave context | Tipo | Query |
|---|---|---|
| `hive_counts` | dict `{active, inactive, maintenance, total}` | aggregazione su `HiveInstallation.status` filtrata per customer |
| `hive_installations_json` | JSON string | lista installazioni con `{id, name, lat, lon, status, subdomain}` per la mappa Leaflet |
| `twincells_devices` | queryset | `TwinCellsDevice` con `prefetch_related('measurements')` — ultimi 10 punti per spark chart |
| `latest_measurements` | queryset | ultime 20 `TwinCellsMeasurement` cross-device per il customer, con `select_related('device__site')` |
| `overall_status` | string `'ok'|'warning'|'offline'` | calcolato: se tutti i device sono online → `ok`; almeno uno stale → `warning`; almeno uno offline → `offline` |

**Spark chart:** calcolato nel template Django. Per ogni device, i valori `soiling_index` degli ultimi 10 punti vengono normalizzati a una scala 0–26 per il viewBox `74×26`, generando la stringa `points` del `<polyline>` SVG inline.

### 3b. Layout template `templates/portal/dashboard.html`

**Page head:**
```
Eyebrow: "Area autenticata · {customer.name}"
H1: "Centro operativo A.P.E."
Filtro segmentato: [Con problemi] [Tutto ok]  ← JS puro, toggling classe CSS
Timestamp: "Ultimo aggiornamento" — now() formattato
```

**Status banner** (dark gradient card):
- Pin animato con colore `overall_status`
- Headline e sottotitolo dipendono da `overall_status`
- Chips: Operativi / In allerta / Offline / Dispositivi (contatori da `hive_counts` + device counts)
- CTA button → `/hive/`

**Mappa + Lista impianti** (griglia 2 colonne, `1.55fr 1fr`):
- **Mappa Leaflet** (CDN `unpkg.com/leaflet@1.9.4`) — pin colorati per stato HIVE, popup con nome e link "Apri HIVE". Dati da `hive_installations_json` in un `<script>` inline.
- **Lista impianti** scrollabile, ordinata con problemi in cima, bordo sinistro colorato per stato, click → `/hive/<pk>/`

**Device grid** (3 colonne):
- Card per ogni `TwinCellsDevice`: status pill, nome, sito, valore soiling dall'ultima misura, spark chart SVG inline, timestamp ultima ricezione
- Bordo top colorato per stato device

**HIVE tunnel + Telemetria** (griglia 2 colonne, `1fr 1.55fr`):
- **Card HIVE** (dark): mostra la prima installazione HIVE del customer; endpoint sottodominio; bottone "Apri HIVE" → `hive-installation-open`
- **Tabella telemetria**: colonne Dispositivo / Sito / Misura (soiling_index) / Soiling / Ricezione. Ultimi 20 record da `latest_measurements`

**Drawer:** slide-in panel per dettaglio sito HIVE (JS inline, `position: fixed`). Apre al click su riga lista impianti. Mostra: nome, stato, recovery/soiling, dispositivi del sito, CTA "Apri HIVE" + "Gestisci impianto".

**JS nel template** (tutto inline, no build):
- Leaflet init + pin colorati
- Filtro segmentato (toggle classe `.eb-hide` sulle righe)
- Drawer open/close

---

## 4. HIVE — `/hive/` (lista)

**Template:** `templates/hive/installation_list.html` — riscritto con DS EnerBot.

- Eyebrow `"01 — Installazioni HIVE"` + contatori status nella page head
- Lista card con: bordo sinistro colorato per status, pin animato, nome impianto in bold, location + sottodominio in font-mono, pill status, azioni "Apri HIVE" (primario) + "Dettaglio" (secondario)
- Empty state con stile DS

---

## 5. HIVE — `/hive/<pk>/` (dettaglio)

**Template:** `templates/hive/installation_detail.html` — riscritto.

- Header dark gradient (come drawer mockup): pin animato, nome installazione, location/customer
- Grid 2 colonne: metadati (nome, sito, coordinate, sottodominio, note) | card azione dark "Entrypoint HIVE" con bottone primario + link Google Maps se coordinate disponibili
- Breadcrumb `← Torna alla lista HIVE`

---

## 6. TwinCells — `/twincells/` (lista siti)

**Template:** `templates/twincells/site_list.html` — riscritto con DS EnerBot.

- Eyebrow `"01 — Siti TwinCells"` + contatori stato
- Card per ogni `TwinCellsSite`: bordo sinistro colorato per stato, pin animato, nome sito, location, contatore dispositivi, stato (online/stale/offline), timestamp ultimo contatto
- Click → `/twincells/<pk>/`

---

## 7. TwinCells — `/twincells/<pk>/` (dettaglio sito)

**Template:** da identificare/creare. View aggiornata per passare i device con ultimi 10 punti.

- Header dark gradient: stato sito, nome, location
- Grid dispositivi (3 colonne): stessa card della dashboard con soiling + spark chart
- Tabella misurazioni del sito (ultime 20 per questo sito)

---

## File toccati

| File | Azione |
|---|---|
| `static/app.css` | disattivato dal `<link>` (non cancellato) |
| `static/app-overrides.css` | nuovo — classi custom piattaforma |
| `templates/base.html` | riscritto header, aggiornati `<link>` CSS |
| `templates/portal/dashboard.html` | riscritto completo |
| `portal/views.py` | esteso con nuovi context data |
| `templates/hive/installation_list.html` | riscritto |
| `templates/hive/installation_detail.html` | riscritto |
| `templates/twincells/site_list.html` | riscritto (o creato se mancante) |
| `templates/twincells/site_detail.html` | riscritto (o creato se mancante) |

---

## Vincoli e note

- Nessuna dipendenza npm/build step. Tutto server-rendered, JS inline.
- Leaflet caricato da CDN (già nel mockup). Nessun'altra libreria JS aggiunta.
- Il drawer è JS puro (toggle `display`) — niente framework.
- `SECRET_KEY` e `DEBUG` rimangono invariati (fuori scope).
- Le URL esistenti non cambiano.
- Le view filtrano sempre per `customer` dell'utente loggato (invariato).
