# M5 — Decisioni tecniche da chiudere

Data: 28 settembre 2026. Riferimento: [specifica M5](superpowers/specs/2026-09-28-inventor-xr-so-m5-design.md),
sezione *Decisioni tecniche da chiudere prima dell'implementazione completa*.

Nessuna delle quattro decisioni è chiusa. Ognuna richiede una misura sulla
postazione reale (Inventor 2027, PC di lavoro, Quest 3 con controller). Questo
documento fissa, per ciascuna, lo strumento di misura, i criteri di scelta e
dove registrare l'esito. Un punto resta **aperto** finché l'esito non è
scritto qui con la data e il riferimento al report.

| # | Decisione | Strumento | Stato |
|---|---|---|---|
| D1 | GLB del flat pattern senza Flat Pattern Edit, identità della cache | `get_flat_pattern_mesh` (sperimentale) + `bridge/tests/M5LiveProbe` | aperta: codice pronto, non eseguito su Inventor |
| D2 | Motore STT locale per l'italiano | `Inventor XR SO/Tools~/stt-bench/` | aperta: harness pronto, nessuna registrazione |
| D3 | Pulsante fisico push-to-talk | analisi mappatura attuale (sotto) + prova sul visore | aperta: proposta **B**, da confermare sul Quest |
| D4 | Tempi e dimensioni preview lamiera e sviluppo | `bridge/tests/M5LiveProbe` (sezione misure) | aperta: codice pronto, non eseguito |

## D1 — Geometria del flat pattern come asset separato

**Domanda.** L'API di Inventor 2027 permette di leggere la geometria dello
sviluppo senza entrare in Flat Pattern Edit e senza alterare il modello piegato?
Con quale chiave si mette in cache l'asset?

**Strumento.** Handler sperimentale `get_flat_pattern_mesh` (tool MCP
`inventor_get_flat_pattern_mesh`): legge `FlatPattern.SurfaceBodies`, con
ripiego su `FlatPattern.Body`, tramite late binding. Non crea mai lo sviluppo:
se manca, rifiuta. Registra lo stato di edit prima e dopo la lettura e, se la
lettura ha forzato l'edit, esce e lo segnala. La mesh non porta ID di facce o
spigoli.

Il probe `M5LiveProbe` crea un proprio fixture lamiera temporaneo (faccia base
più flangia) e verifica:
- rifiuto senza flat pattern, e nessun pattern creato;
- mesh presente, documento non in edit, modello piegato invariato (body,
  volume, revisione);
- ingombro della mesh contro lunghezza/larghezza riportate da Inventor, piano
  dello sviluppo, lato dello spessore, unità;
- identità e hash della mesh stabili tra due letture;
- identità diversa dopo una modifica della flangia;
- rifiuto su parte multi-body.

Comandi e significato di ogni controllo: [`bridge/tests/M5LiveProbe/README.md`](../bridge/tests/M5LiveProbe/README.md).
L'identità proposta per la cache è `flat_pattern_identity`: documento,
revisione, lunghezza/larghezza/pieghe/allineamento Inventor, tolleranza e un
hash dei vertici su griglia di 1 µm (`content_hash` esclude documento e
revisione). Il probe deve confermare che resta stabile tra due letture e cambia
dopo la modifica.

Stato del codice: compilazione del server e test lato server (FakeAddIn)
verdi. Handler e probe **non** sono mai stati compilati contro l'interop reale
né eseguiti su Inventor. Il README del probe elenca le ipotesi API da
confermare.

**Criterio di chiusura.** Tutti i controlli D1 `PASS` sul fixture reale.
Se l'estrazione senza edit non è possibile, si ferma solo il percorso
`Detach` (piano M5, passo 2): nessuna mesh appiattita nel client.

**Esito.** _da compilare dopo l'esecuzione (data, versione Inventor, report)._

## D2 — Motore STT locale

**Domanda.** Quale motore locale sul PC riconosce il vocabolario M5 e la
dettatura numerica italiana con accuratezza, latenza e memoria accettabili?

**Strumento.** `Inventor XR SO/Tools~/stt-bench/`: corpus del vocabolario M5,
registratore guidato, confronto tra almeno faster-whisper e Vosk (anche con
grammatica vincolata al vocabolario), router e parser numerico di riferimento.
Istruzioni e criteri nel README dell'harness. Le registrazioni restano sul PC e
non entrano nel repository.

**Criterio di chiusura.** Misure in almeno due condizioni acustiche (silenzio e
rumore dell'ambiente d'uso), con lo stesso microfono o uno a distanza
equivalente a quello del Quest. Soglie proposte, da confermare prima della
misura:
- zero comandi eseguiti da frasi fuori vocabolario;
- accuratezza d'intenzione ≥ 95 % in silenzio;
- latenza p95 ≤ 800 ms dalla fine del parlato.

Se nessun motore rispetta i gate, la voce M5 resta aperta e i comandi manuali
restano l'unico canale (spec M5).

**Esito.** _da compilare: motore, modello, runtime (CPU/GPU), budget memoria, report._

## D3 — Pulsante push-to-talk

**Vincoli.** La spec di prodotto (§8.2) mette il push-to-talk sulla mano
dominante. La spec M5 chiede che non collida con Grip, Trigger, menu polso e
scorciatoie di sistema.

**Mappatura attuale del client** (controller destro, unico ray oggi supportato):

| Ingresso | Uso | Dove |
|---|---|---|
| Trigger | selezione CAD, click UI | `ControllerRay`, `ControllerUiInputModule` |
| Grip | muove la vista | workspace Inspect/Design/Assembly |
| Grip + Trigger | manipola la bozza armata | `DesignWorkspace`, `AssemblyWorkspace` |
| A (`Button.One`) | blocca/sblocca lo snap nello schizzo | `DesignWorkspace` |
| B (`Button.Two`) | libero | — |
| Thumbstick (asse e click) | libero | — |
| Meta | riservato al sistema | — |
| Menu sinistro (`Button.Start`) | esce dalla sessione | `AppController` |

Il menu polso è un pannello sulla mano sinistra, azionato dal ray destro: non
occupa pulsanti.

**Proposta: B tenuto premuto sulla mano dominante.**
- È libero in tutte le modalità e non interferisce con Grip e Trigger: il
  pollice tiene B mentre l'indice resta sul Trigger e il ray continua a puntare.
- Il thumbstick è scartato: premerlo a lungo lo fa deviare, ed è l'unico asse
  libero che resta per usi futuri (scorrimento liste, regolazione fine).
- Il menu sinistro è scartato: è sulla mano non dominante, ed è già "esci dalla
  sessione". Un rilascio mancato chiuderebbe la sessione.
- Rischio: B è accanto ad A (blocco snap). Mitigazione: la cattura parte dopo
  una pressione continua di circa 150 ms. Su A il feedback già visibile dello
  snap segnala l'errore opposto.
- Per il mancino (non ancora supportato dal client) il mirror è Y sul
  controller sinistro (`Button.Two` su `LTouch`).

**Criterio di chiusura.** Prova sul Quest 3 con l'utente: durante una sessione
di schizzo e una di flangia, tenere premuto B mentre si punta e si usa il
Trigger. Nessuna attivazione accidentale di A o del sistema, e cattura chiusa al
rilascio. Va provata anche la soglia di pressione.

**Esito.** _da compilare dopo la prova fisica._

## D4 — Tempi e dimensioni della preview lamiera

**Domanda.** Quanto costano preview della flangia, mesh del piegato, creazione
dello sviluppo e mesh dello sviluppo sul fixture reale? Servono a fissare limiti
UI e timeout prima di promettere altri handler lamiera.

**Strumento.** Sezione misure di `M5LiveProbe`: più ripetizioni per operazione
(min/mediana/max), numero di triangoli e dimensione di payload/GLB a tolleranza
0,1 e 0,5 mm.

**Criterio di chiusura.** Misure registrate qui. Da queste si ricavano:
- timeout della preview;
- tolleranza di tessellazione di default per lo sviluppo;
- elenco degli handler lamiera che possono diventare pulsanti XR.

**Esito.** _da compilare dopo l'esecuzione._
