# Inventor XR SO — Milestone 1: design

**Data:** 2026-09-26
**Stato:** approvato in brainstorming, in attesa di review scritta
**Spec prodotto di riferimento:** [`Inventor XR SO/inventor_meta_product.md`](../../../Inventor%20XR%20SO/inventor_meta_product.md) (v0.1)
**Scope:** solo Milestone 1 (§52 della spec prodotto) — primo loop reale `QUEST ↔ MCP ↔ INVENTOR`.

---

## 1. Obiettivo e Definition of Done

Build Quest 3 che:

1. si associa al PC (QR con pinning, fallback codice);
2. si connette all'host `Inventor.So.Mcp.Http` e legge `inventor_get_capabilities`;
3. mostra nella Home PC, versioni backend/Inventor, documento attivo e tipo;
4. carica scene graph + GLB del documento attivo e lo rende a scala 1:1;
5. seleziona una occurrence (assembly) o una faccia (part) col ray;
6. risolve il face id corretto localmente;
7. evidenzia la selezione sul Quest **e** in Inventor;
8. segue automaticamente il cambio di documento attivo fatto in Inventor;
9. su perdita rete resta visibile, read-only, e si riconnette da solo.

Fuori da M1: menu polso, Browser, breadcrumb, misure, section, scale modes diverse da 1:1, voce, qualunque scrittura CAD.

## 2. Decisioni prese

| Tema | Decisione |
|---|---|
| Stack | Unity 6 (installato: 6000.6.3f1) + Meta XR Core/Interaction SDK, glTFast, build APK Android/IL2CPP |
| Pairing/trasporto | HTTPS con certificato self-signed pinnato; QR `{host, port, token monouso, sha256 cert}`; fallback codice 6 cifre + IP |
| Repo | monorepo: progetto Unity in `Inventor XR SO/` dentro `inventor-so-mcp`; `.gitignore` Unity; Git LFS per binari |
| Client MCP | client JSON-RPC/SSE minimo scritto a mano su `UnityWebRequest` + Newtonsoft.Json (niente SDK `ModelContextProtocol`: rischio IL2CPP/stripping, pinning su Android) |

Prerequisito ambiente: modulo **Android Build Support** (OpenJDK, SDK, NDK) da aggiungere all'editor 6000.6.3f1 via Unity Hub.

## 3. Architettura

### 3.1 Backend (`bridge/`) — modifiche M1

Solo pairing. Tutto il resto usa tool XR già esistenti e live-verificati (tier sperimentale, che deve essere abilitato sull'add-in):
`inventor_get_capabilities`, `inventor_get_scene_graph`, `inventor_get_display_mesh`, `inventor_get_visual_revision`, `inventor_pick_entity`, `inventor_highlight_entity`, `/assets/{id}`, risorsa `inventor://events`.

**`PairingService`** nell'host HTTP:

- Comando `Inventor.So.Mcp.Http --pair <nome>`: genera token monouso (TTL 2 min, un solo uso), stampa in console codice a 6 cifre e scrive un QR (PNG + pagina locale) con payload JSON `{"v":1,"host","port","ott","cert_sha256"}`.
- Se non è configurato un PFX, genera al primo avvio un certificato self-signed persistente per la macchina; `cert_sha256` è la sua impronta.
- `POST /pair` (non autenticato, rate-limited): `{ott | code, device_name}` → `{client_name, token}`. Aggiunge `name:token` al registry token esistente. Token monouso consumato anche in caso di errore successivo.
- Errori: `PAIRING_EXPIRED`, `PAIRING_USED`, `PAIRING_INVALID`.
- Audit della coppia creata sotto il nome client.

### 3.2 Client Unity (`Inventor XR SO/`)

Assembly definition separati. I primi quattro non dipendono dal Meta SDK e sono testabili in EditMode senza visore.

| Modulo | Responsabilità | Dipende da |
|---|---|---|
| `XrSo.Net` | JSON-RPC su Streamable HTTP, stream SSE, `CertificateHandler` con pinning sha256, bearer, backoff | Newtonsoft.Json |
| `XrSo.Backend` | interfaccia tipizzata `IInventorBackend` (`GetCapabilities`, `GetSceneGraph`, `GetVisualRevision`, `DownloadAsset`, `PickEntity`, `Highlight`, `SubscribeEvents`) + DTO | Net |
| `XrSo.Session` | stato sessione: server, documento attivo, revision, visual_revision, online/offline, read-only | Backend |
| `XrSo.Scene` | scene graph → gerarchia GameObject, GLB via glTFast, instancing per definizione, cache asset su disco per `asset_id`, mappa `(primitive, triangolo) → face_id` da `extras.faces` | Backend, glTFast |
| `XrSo.Selection` | ray pick → occurrence/face, highlight locale (overlay sul range di indici), chiamata highlight a Inventor | Scene, Backend |
| `XrSo.Pairing` | scan QR (Passthrough Camera API, permesso camera), inserimento manuale, storage credenziali in Android Keystore | Net |
| `XrSo.App` | bootstrap, rig Meta XR, Home, scelta MR/Studio VR, UI minima | tutti |

Credenziali salvate: `{host, port, cert_sha256, client_name, token}`. Mai credenziali dell'add-in, mai accesso a COM/pipe/file CAD (spec §4.2).

## 4. Flussi

### 4.1 Pairing

```text
PC:   Inventor.So.Mcp.Http --pair quest3  → QR + codice
Quest: Home → "Associa PC" → scan QR (o IP + codice)
      → TLS: accettato solo se sha256(cert) == cert_sha256
      → POST /pair → {client_name, token}
      → salva nel Keystore
```

Fingerprint diverso → rifiuto senza retry automatico (possibile MITM), messaggio esplicito. Col fallback manuale (niente QR) il fingerprint viene mostrato su Quest e sul PC per conferma visiva prima di salvare (trust on first use confermato).

### 4.2 Sessione

```text
connect (TLS pinnato + bearer) → initialize
→ inventor_get_capabilities   (tool XR presenti? add_in_experimental_enabled?)
→ Home: PC, versione backend, versione Inventor, doc attivo, tipo, stato MCP
→ "Entra" → Mixed Reality | Studio VR
→ inventor_get_scene_graph(include_meshes=true)
→ per ogni definizione: asset in cache? altrimenti GET /assets/{mesh_asset_id}
→ istanzia 1 mesh per definizione, N GameObject con matrix_gltf
→ modello 1:1 davanti all'utente
→ subscribe inventor://events
```

M1: MR = passthrough base; Studio VR = sfondo neutro. Nessun MRUK in M1.

La cache asset non va mai invalidata: l'id è l'hash del contenuto.

### 4.3 Selezione e highlight

```text
ray + trigger su triangolo t dell'istanza di occurrence O
→ face_id = mappa locale (primitive, t)           [nessun round-trip]
→ assembly: inventor_pick_entity(O, face_id) → id proxy ent_
  part:     face_id è già l'id portabile
→ highlight locale
→ inventor_highlight_entity(ids, mode=highlight)
```

Default di selezione (spec §14): assembly attivo → occurrence; part attiva → faccia. Selezione vuota → `inventor_highlight_entity(mode=clear)`.

### 4.4 Eventi e refresh

| Evento | Azione |
|---|---|
| cambio documento attivo | carica la nuova scena (spec §5.4) |
| `visual_revision` cambiata | rifetch scene graph; scarica solo i `mesh_asset_id` nuovi |
| solo `revision` cambiata | aggiorna token, nessun reload |

## 5. Errori e connessione

| Situazione | Comportamento |
|---|---|
| rete persa / SSE chiuso | modello resta visibile, badge "Offline" discreto, sessione read-only, riconnessione con backoff 1→2→4…30 s |
| riconnessione | ricontrolla capabilities, document id, revision, visual_revision; ricarica solo ciò che è cambiato |
| tier sperimentale spento | Home spiega come abilitarlo; ingresso in sessione bloccato |
| `MESH_TOO_LARGE` o >200 definizioni | carica ciò che riesce, avviso con numero di componenti omessi |
| face id stale (`pick_entity` rifiuta) | deseleziona, messaggio breve, nessun guess |
| 401 | torna alla Home con "Ri-associa PC" |
| fingerprint cert cambiato | blocco connessione, richiede nuovo pairing |

## 6. Rischio tecnico prioritario

glTFast deve preservare ordine degli indici e primitive così che i range di `extras.faces` restino validi. **Primo task del piano:** spike su GLB fixture prodotti da `GlbBuilder`. Se l'ordine non è garantito, `XrSo.Scene` legge accessor ed `extras` direttamente dal chunk JSON/BIN del GLB e costruisce la mappa da lì.

## 7. Test

- **Backend (`Bimwright.Ipt.Tests`):** `PairingService` — TTL, monouso, codice errato, concorrenza su stesso token, rate limit, token aggiunto al registry.
- **EditMode Unity (PC, senza visore):** `XrSo.Net` contro host HTTP reale + `FakeAddIn` in processo separato; pairing end-to-end con cert self-signed e pinning (successo e fingerprint errato); parsing `extras.faces`; mapping triangolo→faccia su fixture `GlbBuilder`; macchina a stati riconnessione con clock finto; refresh incrementale.
- **Manuale, Quest 3 + Inventor 2027:** checklist DoD della sezione 1, più cambio documento da Inventor e stacco Wi-Fi.

## Appendice A — Review della spec prodotto v0.1

Punti di forza: principi chiari (model-first, preview obbligatoria, Grip+Trigger intenzionale, tre canali di precisione), milestone ben tagliate, regola di sviluppo §57.

### A.1 Gap backend (prerequisiti di milestone successive)

| # | Spec | Gap | Serve in |
|---|---|---|---|
| G1 | §25 ghost preview | la preview backend è transazione + abort e non restituisce la mesh del risultato; serve tessellazione dentro la transazione prima dell'abort | M3 |
| G2 | §24, §36 drag live | ogni preview è un round-trip verso Inventor (da 100 ms a secondi); durante il drag serve preview locale approssimata, preview Inventor su rilascio/debounce | M3 |
| G3 | §5.2 pairing QR | solo token statici | **M1 (coperto qui)** |
| G4 | §29, §34 DOF gizmo | il backend dà solo conteggi `dof_translation/rotation`, non assi/direzioni | M4 |
| G5 | §39 undo/redo | nessun tool; l'undo di Inventor è per documento e può annullare modifiche fatte dal desktop: serve una policy | M3 |
| G6 | §38 voce italiano | nessuna scelta STT (Meta Voice SDK/Wit.ai cloud, Whisper sul PC, on-device); impatta privacy e architettura | M5 |

### A.2 Ambiguità da risolvere nella spec prodotto

1. §17 misure: la mesh ha tolleranza 0.1 mm; distinguere misura rapida su mesh e misura esatta B-rep (`inventor_measure_min_distance`).
2. §18 section: esplicitare che è clipping visuale locale.
3. §20–22 sketch: rubber-band e snap vanno calcolati localmente; servono dati geometria sketch dal backend (handler sperimentali non verificati live).
4. §14 "doppio pinch" in un MVP controller-first: definire l'equivalente controller.
5. §7 Fit to room: in MR richiede scene model (MRUK), in VR i confini guardian.
6. Limiti backend (200 definizioni, 500k triangoli per mesh): la spec deve dire cosa succede sugli assiemi grandi.
7. Rete: HTTP in chiaro solo su loopback; su LAN serve HTTPS (deciso qui).
8. Concorrenza desktop/Quest: la stale revision è gestita, ma la UX di "documento cambiato durante un comando" non è descritta.
9. Tutti i tool XR sono tier sperimentale, spento di default: va dichiarato come prerequisito d'installazione.
