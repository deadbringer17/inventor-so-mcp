# Inventor XR SO — M9: navigazione per contesto

Data: 4 ottobre 2026, Europe/Rome. Stato: **design approvato, da pianificare**.
Collaudo Quest e prova fisica aperti.

## Obiettivo e perimetro

L'uso reale di M6–M8 ha mostrato tre problemi di usabilità:

1. **Modello "a spazi".** L'utente sceglie Ispeziona, Progettazione, Lamiera
   o Assieme dalla scheda «Spazi». Inventor fa il contrario: è il tipo di
   documento attivo a decidere gli strumenti. In un assieme compaiono così
   Schizzo e Lamiera, che lì non hanno senso e vengono solo bloccati da
   `CanEnter`.
2. **Entrare in una parte costa quattro passi**: Trigger → anello → «Isola» →
   «Apri in Progettazione/Lamiera». Il «doppio pinch» della spec prodotto §14
   non è mai stato tradotto per i controller (M1, domanda aperta 4).
3. **Tasti senza significato visibile.** A, stick destro e Trigger sinistro
   fanno qualcosa solo in certi stati; Y ha due funzioni nascoste (premuto e
   tenuto). Nessuna legenda dice cosa fa un tasto adesso.

M9 porta il client a un modello guidato dal documento:

- il contesto (Assieme, Parte, Lamiera) segue il documento attivo;
- si entra con un doppio Trigger, si risale con «Torna»;
- Misura, Sezione e Verifica sono strumenti trasversali, non uno spazio;
- i tasti seguono una tabella unica stato → azione, mostrata da una legenda
  sul controller;
- il doppio Trigger su una faccia apre la modifica della feature che l'ha
  generata.

Fuori perimetro:

- la modifica in-place vera dentro l'assieme (`Occurrence.Edit`, proiezione
  di geometria da altri componenti);
- nuovi comandi CAD diversi dalla lettura faccia → feature;
- riprogettazione grafica (resta M8);
- disposizione spaziale, barra di conferma e regole di Applica, che restano
  quelle di M6.

Riferimenti:

- [M6](2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md)
- [M7](2026-10-03-inventor-xr-so-m7-inspect-verifica-design.md)
- [M8](2026-10-04-m8-grapics-design.md)
- [Spec prodotto](../../../Inventor%20XR%20SO/inventor_meta_product.md)
- [Accettazione Quest](../../xr-quest-acceptance.md)

## Approccio

Si aggiungono un router di contesto e una pila di navigazione **sopra** i
workspace esistenti (`AssemblyWorkspace`, `DesignWorkspace`,
`LamieraWorkspace`, `InspectWorkspace`). Non si riscrive un workspace unico:
così si riusano il guscio M6 e i percorsi già collaudati, senza riaprire i
gate M4–M7.

Consegna in quattro fasi. Le fasi 1–3 toccano solo il client; la fase 4 tocca
anche il bridge.

| Fase | Contenuto |
|---|---|
| 1 | Navigazione: router, pila, doppio Trigger su componente e sottoassieme, assieme fantasma, Torna, scheda Documento |
| 2 | Strumenti per contesto: schede filtrate, gruppo Ispeziona, Vista unica, anello per tipo di documento |
| 3 | Tasti contestuali: `InputMap` e legenda 3D |
| 4 | Doppio Trigger su faccia → modifica della feature (backend sperimentale) |

## §1 Navigazione (fase 1)

### Pila di navigazione

`NavigationStack` (core, senza Unity) contiene un livello per ogni documento
in cui si è entrati:

`{documentId, tipo (assembly | part | sheetmetal), nome, occorrenza di provenienza, posa dell'occorrenza nell'assieme padre}`

La radice è il documento attivo all'ingresso in sessione. La pila esiste solo
nel client: Inventor conosce soltanto il documento attivo.

### ContextRouter

`ContextRouter` (core) osserva il documento attivo, sia che cambi dal Quest
sia dal desktop, e decide il contesto:

| Documento attivo | Contesto | Workspace |
|---|---|---|
| `.iam` | Assieme | `AssemblyWorkspace` |
| `.ipt` | Parte | `DesignWorkspace` |
| `.ipt` lamiera | Lamiera | `LamieraWorkspace` |

Il sottotipo lamiera si legge da document info. Se il documento cambia dal
desktop e non corrisponde alla cima della pila, la pila si azzera su quel
documento e l'HUD lo segnala («Documento cambiato dal PC: Staffa.ipt»).

La scelta manuale del workspace sparisce. `InspectWorkspace` smette di essere
un workspace esclusivo (vedi §2).

### Doppio Trigger su componente

Il doppio Trigger è riconosciuto da `DoubleTriggerDetector` (core) se:

- le due pressioni cadono entro **350 ms**;
- il raggio colpisce lo **stesso** bersaglio a entrambe le pressioni;
- la punta del raggio si sposta meno di **2°** di angolo.

La prima pressione seleziona come oggi. La seconda chiama il percorso
esistente `ActivateDefinitionAsync` (`AssemblyWorkspace`) e, se riesce,
aggiunge un livello alla pila:

- parte → contesto Parte o Lamiera;
- sottoassieme → nuovo livello Assieme. L'HUD ricorda che le modifiche alla
  definizione riguardano tutte le istanze, come già fa oggi.

Il doppio Trigger non ha effetto se c'è un comando in corso, un'anteprima, una
cattura di maniglia o una revisione CAD da fare (stessa guardia `Editable` di
oggi): l'HUD dice perché. Nell'anello del componente restano «Isola» e «Apri»
come via alternativa.

### Assieme fantasma

Entrando in una parte (o in un sottoassieme), la scena del livello padre non
viene scaricata:

- resta visibile con materiale semitrasparente, non selezionabile e senza
  ombre;
- la parte si posa al suo posto nell'assieme, nella posa sollevata (assieme)
  di `Workbench`, con la posa dell'occorrenza salvata nella pila;
- «Adatta» (Y) inquadra la parte, e il fantasma si muove insieme a lei
  rimanendo coerente;
- il fantasma mostra la revisione dell'assieme al momento dell'ingresso e
  porta l'etichetta «contesto: prima delle modifiche». Non si aggiorna mentre
  si modifica la parte.

Viene mostrato un solo livello di fantasma (il padre diretto), per contenere
memoria e draw call. Se il padre supera i limiti di mesh già previsti
(200 definizioni, `MESH_TOO_LARGE`), il fantasma mostra solo le definizioni
caricate e l'HUD lo segnala.

### Torna

Si risale di un livello in due modi:

- tenendo premuto **X per 1 s a riposo**, cioè senza anello, tastierino,
  gruppo di schede, bozza o isolamento aperti. Un tocco breve a riposo mostra
  solo «Tieni X per tornare a <nome padre>»;
- con il pulsante «Torna» della scheda Documento.

Torna riattiva il documento sotto nella pila e ricarica la sua scena.
**Non salva**. Se il documento che si lascia ha modifiche non salvate, il
breadcrumb lo indica con «●» e la scheda Documento offre «Salva».

### Scheda Documento

Prende il posto della scheda Spazi. Contiene:

- il breadcrumb, per esempio `Telaio.iam › Staffa.ipt ●`, con un pulsante
  per ogni livello che risale fino a quel punto;
- Torna, Salva;
- l'elenco dei documenti aperti in Inventor, per saltare a uno di essi (la
  pila si azzera su quello);
- Ricentra postazione e calibrazione del piano, spostate qui da Y tenuto;
- Connessione / Esci.

### Errori

| Caso | Comportamento |
|---|---|
| Definizione non aperta in Inventor | messaggio esistente; la pila non cambia |
| Attivazione fallita o annullata | la pila non cambia; resta il contesto attuale |
| Connessione persa durante l'ingresso | la pila resta al livello precedente; alla riconnessione decide il router |
| Documento padre chiuso dal desktop durante Torna | la pila si azzera sul documento attivo; HUD |
| Revisione CAD in sospeso (`RequiresCadReview`) | ingresso e Torna bloccati con il motivo |

## §2 Strumenti per contesto (fase 2)

### Regola

Una scheda compare solo se nel contesto attuale può fare qualcosa. Nessuna
scheda resta visibile ma bloccata. Dentro una scheda, un'azione che adesso non
si può fare si mostra disabilitata con il motivo («seleziona una faccia»), ma
solo se appartiene a quel contesto.

### Schede

| Contesto | Schede principali |
|---|---|
| Assieme | Componenti · Vincoli · **Ispeziona ▸** · Vista · Documento |
| Parte | Schizzo · Feature · Parametri · **Ispeziona ▸** · Vista · Documento |
| Lamiera | Lamiera · Schizzo · Sviluppo · **Ispeziona ▸** · Vista · Documento |

Schede che compaiono da sole:

- «Vincoli» (dello schizzo) appare in Parte e Lamiera solo con uno schizzo
  aperto;
- «Opzioni feature» appare solo con una feature in costruzione e diventa la
  scheda attiva;
- «Feature: <nome>» appare in fase 4 dopo il doppio Trigger su una faccia.

### Gruppo Ispeziona

«Ispeziona ▸» apre un gruppo di schede tramite `PaletteView.ShowTabGroup`,
che esiste già:

| Contesto | Schede del gruppo |
|---|---|
| Assieme | Misura · Sezione · Visibilità · Verifica |
| Parte, Lamiera | Misura · Sezione |

Visibilità e Verifica (interferenze, distanza minima, salute, BOM) hanno senso
solo in Assieme. Dentro il gruppo, lo stick sinistro ←/→ scorre le sue
schede; X o la scheda «◂» torna alle schede principali.

### Vista unica

Le schede Vista oggi duplicate in ogni provider diventano una sola scheda
comune: Adatta, scala, ambiente MR/VR, legenda tasti sì/no.

### Ispeziona trasversale

Misura e Sezione sono solo visive e non scrivono nel CAD, quindi restano
attive durante il lavoro in Parte e Lamiera. Si sospendono soltanto durante
la cattura di una maniglia o una revisione CAD. `InspectWorkspace` diventa un
fornitore di strumenti incluso da ogni contesto. La guardia
`OtherWorkspaceActive` sparisce; resta la regola che una misura o una sezione
non bloccano la barra di conferma.

### Anello contestuale

| Contesto | Selezione | Azioni |
|---|---|---|
| Assieme | componente | Isola · Sposta · Vincola · Apri |
| Parte | faccia piana | Schizzo · Estrudi · Foro · Misura |
| Parte | bordo | Raccordo · Smusso · Misura |
| Lamiera | faccia | Schizzo · Taglio · Misura |
| Lamiera | bordo | Flangia · Misura |
| qualsiasi | nulla | l'anello non compare |

Il doppio Trigger resta la scorciatoia di «Apri» e, in fase 4, della modifica
della feature.

### Voce

`ActionCatalog` si costruisce dal contesto, quindi la voce risolve solo le
azioni presenti. I comandi che cambiavano spazio («vai in progettazione»)
diventano «apri <componente>», «torna» e «salva»; un comando di spazio
pronunciato riceve una risposta che spiega il nuovo modo di procedere, senza
eseguire nulla. Restano le regole M5/M6: niente Applica a voce.

## §3 Tasti contestuali e legenda (fase 3)

### InputMap

`InputMap` (core) è l'unica tabella **stato → tasto → (azione, etichetta)**.
Il dispatcher la usa per instradare gli eventi di `XrInput`, la legenda la usa
per disegnare le etichette. Un tasto senza voce nello stato corrente non fa
niente e non ha etichetta. Lo stato si calcola con questa priorità:
tastierino > cattura/chip armato > schizzo > componente selezionato >
riposo.

### Mappatura

— = inattivo, senza etichetta.

| Tasto | A riposo | Componente selezionato (Assieme) | Schizzo aperto | Chip o maniglia armati | Tastierino |
|---|---|---|---|---|---|
| Trigger dx | seleziona; doppio = apri / modifica feature | idem | punto | trascina (tenuto) | preme i tasti |
| Grip dx | sposta vista | idem | idem | idem | — |
| Stick dx ←/→ | ruota vista ±15° | idem | idem | ± passo | — |
| Stick dx ↑/↓ | — | — | — | cambia passo 10 / 1 / 0,1 | — |
| A | — | Isola / Rilascia | Snap sì/no | apre il tastierino | OK |
| B | parla (sempre, M5) | idem | idem | idem | idem |
| Trigger sx | — | — | precisione | precisione (in trascinamento) | — |
| Stick sx ←/→ | schede | schede | schede | schede | — |
| Stick sx ↑/↓ | zoom | zoom | zoom del foglio | zoom | — |
| X | tocco = suggerimento; tenuto 1 s = Torna | Indietro | Indietro | Indietro | Annulla |
| Y | Adatta | Adatta | Adatta | Adatta | — |
| Grip sx + Grip dx | due mani (sempre, M6) | idem | idem | idem | — |

Cambiamenti rispetto a M6:

- **A** da «snap» fisso diventa l'azione rapida del contesto.
- **Stick dx ←/→ a riposo** ruota la vista di 15° attorno all'asse verticale
  del modello (solo vista, aptica a ogni scatto).
- **Y tenuto** non ricentra più: Ricentra è nella scheda Documento. Y fa solo
  Adatta.
- **X a riposo** ha la doppia soglia tocco/tenuto descritta al §1.
- La catena Indietro di X resta: tastierino → anello → gruppo schede → passo
  di bozza → isolamento.

### Legenda 3D

- Etichette di 12 caratteri al massimo, ancorate alla posizione di ogni tasto
  sul modello del controller, solo per i tasti attivi nello stato corrente.
- Opacità bassa di base, piena quando il controller entra nel cono di
  sguardo (≤ 25° dall'asse della testa).
- Si aggiornano al cambio di stato senza animazioni lunghe (≤ 150 ms).
- Le azioni a soglia temporale (doppio Trigger, X tenuto) mostrano un
  anellino di avanzamento sull'etichetta.
- Si spengono dalla scheda Vista; la preferenza resta salvata sul visore.
- Stile: token M8.

## §4 Doppio Trigger su faccia → modifica feature (fase 4)

### Backend (tier sperimentale)

Nuovo comando in sola lettura, `face_feature` (handler in
`bridge/src/shared/Handlers/Experimental/`, tool MCP
`inventor_face_feature`, tier sperimentale secondo `bridge/CLAUDE.md`).

Input: id della faccia, nello stesso formato della selezione XR attuale.

Output:

```json
{
  "feature": { "name": "Estrusione1", "type": "extrude", "suppressed": false, "healthy": true },
  "parameters": [
    { "name": "d3", "role": "distance", "value": 20.0, "unit": "mm",
      "expression": "20 mm", "editable": true }
  ],
  "previous_feature": "Schizzo1"
}
```

- Si basa su `Face.CreatedByFeature` e sulla definizione della feature.
- `editable` è `false` se l'espressione non è un valore semplice (dipende da
  altri parametri) o se la feature è soppressa o in errore.
- Errori: `NO_OWNING_FEATURE` (corpo base, derivato, importato),
  `UNSUPPORTED_FEATURE` (tipo fuori tabella: restituisce comunque nome e
  tipo), `STALE_REVISION`.
- Unità in mm e gradi (`UnitConvert`). Nessuna transazione: il comando non
  scrive.

Tipi supportati:

| Feature | Ruoli |
|---|---|
| Estrusione | distance |
| Rivoluzione | angle |
| Raccordo | radius |
| Smusso | distance |
| Foro | diameter, depth |
| Serie rettangolare / circolare | count, spacing (o angle) |
| Flangia lamiera | distance, angle |

### Client

Doppio Trigger su una faccia in Parte o Lamiera:

1. chiama `face_feature`;
2. evidenzia tutte le facce della feature;
3. apre la scheda «Feature: <nome>» con un chip per parametro;
4. dove la geometria lo permette aggiunge la maniglia esistente (distanza di
   estrusione lungo la normale, flangia).

Chip e maniglia modificano solo la bozza. Anteprima e Applica mandano uno o
più `set_parameter` in un unico `inventor_atomic_batch`, sulla stessa barra di
conferma usata oggi dalla scheda Parametri.

Per i tipi non supportati la scheda mostra nome e tipo, «Modifica dal
desktop» e un collegamento alla scheda Parametri filtrata sulla feature.

### Casi limite

| Caso | Comportamento |
|---|---|
| Parametro guidato da un'espressione | chip in sola lettura con l'espressione; «Modifica <sorgente>» se la sorgente è un parametro; non si scrive mai un numero sopra un'espressione |
| Feature soppressa o in errore | lettura consentita, modifica bloccata con il motivo |
| Revisione cambiata dal desktop tra lettura e Applica | `STALE_REVISION`; bozza invalidata come oggi |
| Faccia toccata da più feature | vale `CreatedByFeature` (l'ultima); «Feature precedente» risale alla feature indicata in `previous_feature` |
| Doppio Trigger su faccia in Assieme | apre il componente (§1), non modifica feature |

## Architettura

### Core (`Packages/com.occhipinti.inventorxrso.core/Runtime/`)

| Unità | Responsabilità | Dipende da |
|---|---|---|
| `Navigation/NavigationStack` | livelli, push/pop, azzeramento, stato «●» | — |
| `Navigation/ContextRouter` | documento attivo → contesto; riconciliazione con la pila | `NavigationStack`, info documento |
| `Input/DoubleTriggerDetector` | tempo, bersaglio, soglia angolare | — |
| `Input/InputMap` | tabella stato → tasto → azione/etichetta | — |
| `Ui/ContextTabs` | schede principali e gruppo Ispeziona per contesto e stato | `ActionCatalog` |
| `Backend/FaceFeature` | modello e parsing della risposta `face_feature` | backend MCP |

### Unity (`Assets/XrSo/`)

| Unità | Responsabilità |
|---|---|
| `Xr/AppController` | collega router → workspace; rimuove la scelta manuale |
| `Xr/DocumentActions` | sostituisce `SpacesActions` con la scheda Documento |
| `Runtime/Scene/GhostContext` | scena padre semitrasparente, non selezionabile |
| `Xr/Input/InputDispatcher` | instrada gli eventi di `XrInput` secondo `InputMap` |
| `Runtime/Ui/Shell/ControllerLegend` | etichette 3D sui tasti |
| `Xr/DesignWorkspace`, `Xr/LamieraWorkspace` | scheda «Feature: <nome>» e bozza su `set_parameter` |

`Scenes/Main.unity` è generata: le nuove componenti si aggiungono dal
generatore in `Assets/XrSo/Editor/`.

### Flusso: doppio Trigger su componente

1. `DoubleTriggerDetector` riconosce il gesto sul componente.
2. `AssemblyWorkspace` controlla `Editable` e chiama `ActivateDefinitionAsync`.
3. Inventor attiva la definizione; arriva il cambio di documento.
4. `ContextRouter` aggiunge il livello alla pila e attiva il workspace giusto.
5. `GhostContext` conserva la scena padre; la parte si posa all'occorrenza.
6. `ContextTabs` e `InputMap` aggiornano tavolozza e legenda.

## Test

Tre esiti separati, come da regole trasversali: test unitari/FakeAddIn, runner
sul Quest con input **sintetico**, prova fisica.

- **Core (`XrSo.Core.Tests`)**: pila e router (incluso il cambio dal
  desktop); `DoubleTriggerDetector` con tempi al limite, bersaglio diverso e
  soglia; `InputMap` senza buchi né doppioni per ogni stato; `ContextTabs`
  con la tabella attesa per ogni contesto e stato (nessuna scheda inerte).
- **Bridge (`Bimwright.Ipt.Tests`)**: contratto `face_feature`, errori,
  espressioni non modificabili, unità.
- **EditMode Unity**: fantasma non selezionabile, scheda Documento e
  breadcrumb, gruppo Ispeziona con stick e X, legenda per ogni stato.
- **Sonda live (`tests/*LiveProbe/`)**: `face_feature` su ogni tipo
  supportato della fixture, contro Inventor 2027.
- **Runner Quest M9** sulla fixture dedicata, con log M9:
  doppio Trigger → parte → Torna; sottoassieme a due livelli; schede per
  contesto; ogni riga di `InputMap`; doppio Trigger su faccia → chip →
  anteprima → Applica → parametro verificato in Inventor → cleanup e
  ripristino del documento precedente.

## Gate di consegna M9

| ID | Criterio | Evidenza |
|---|---|---|
| M9-01 | Il contesto segue il documento attivo (Quest e desktop); nessuna scelta manuale di workspace; scheda Documento con breadcrumb, Torna, Salva, documenti aperti, Ricentra | core + EditMode + runner |
| M9-02 | Doppio Trigger su componente apre parte o lamiera nel contesto giusto; su sottoassieme scende di livello; bloccato con motivo durante comando/anteprima/revisione | core + runner |
| M9-03 | Assieme fantasma semitrasparente e non selezionabile; parte posata all'occorrenza; Adatta coerente; etichetta di revisione | EditMode + runner + screenshot |
| M9-04 | Torna con X tenuto e da scheda; tocco breve solo suggerimento; nessun salvataggio implicito; «●» sulle modifiche non salvate | core + runner |
| M9-05 | Schede per contesto senza schede inerti; gruppo Ispeziona; Vista unica; Misura e Sezione attive durante la modifica | core + EditMode + runner |
| M9-06 | `InputMap` unica fonte per dispatcher e legenda; ogni riga della tabella esercitata | core + runner |
| M9-07 | Legenda 3D: solo tasti attivi, aggiornamento al cambio stato, avanzamento per doppio Trigger e X tenuto, disattivabile | EditMode + screenshot |
| M9-08 | `face_feature` corretto su ogni tipo supportato contro Inventor reale; errori e espressioni come da tabella | xUnit + sonda live |
| M9-09 | Modifica feature da doppio Trigger: chip, maniglia, anteprima e Applica in batch atomico; nessuna scrittura su espressioni | runner con Inventor reale |
| M9-10 | Voce coerente con il contesto; comandi di spazio rifiutati con spiegazione; niente Applica a voce | core + runner |
| M9-11 | Regressioni M1–M8 verdi; runner M6/M7 migrati alla nuova navigazione `PASS COMPLETE` | suite + runner |
| M9-12 | Prova fisica da seduto: scoperta del doppio Trigger e di Torna, leggibilità della legenda, nessun Torna involontario in 30 minuti | prova fisica Quest |

Un gate fisico non eseguito resta aperto. Gli esiti vanno in
`docs/xr-m9-verification.md`. `face_feature` resta nel tier sperimentale
finché M9-08 non passa dal vivo.
