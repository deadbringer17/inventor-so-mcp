# Inventor XR SO — M4: Assembly

Data: 27 settembre 2026. Stato: proposta tecnica pronta per revisione;
implementazione M4 e accettazione non eseguite.

Riferimento: [specifica prodotto](../../../Inventor%20XR%20SO/inventor_meta_product.md),
§9, §28–34, §39, §48–49, §55 e §57. M3 costituisce la base del client.

## Obiettivo e perimetro

Dal Quest l'utente seleziona un componente, vede le libertà residue, propone un
movimento CAD o una relazione tra componenti, controlla il risultato calcolato
da Inventor e lo applica esplicitamente. Inventor rimane l'autorità geometrica.

M4 comprende Assembly, CAD Move, DOF, Constraints e Joints (§55). Browser,
breadcrumb, ispezione e navigazione dei sottoassiemi riusano M2. Grounding,
replace, pattern e representations, elencati nel quadro generale §28, non
costituiscono automaticamente nuovi strumenti di authoring di questa milestone.
Voce e lamiera appartengono a M5.

## Evidenza della base esistente

- `MoveComponentHandler` supporta traslazione/rotazione ma rifiuta componenti
  vincolati; non soddisfa ancora il movimento nelle libertà residue.
- `GetAssemblyHealthHandler` e `InspectXrHandler` espongono conteggi DOF senza
  direzioni: non bastano per disegnare assi e archi spaziali affidabili.
- `CreateConstraintHandler` e `CreateJointHandler` hanno transazioni proprie;
  non devono essere inseriti direttamente nella transazione atomic batch.
- `AtomicCadBatch` ammette la mesh di preview solo per parti;
  `InventorBatchBackend.CapturePreview` assume `PartDocument`.
- `DesignSession` offre invalidazione, preview visualizzata obbligatoria,
  gestione dell'esito incerto e cronologia XR riutilizzabili.
- `DesignPreviewView` rifiuta istanze assembly: occorre un percorso esplicito
  per la geometria di preview dell'assieme nel suo sistema di coordinate.

## Contratto di interazione

La modalità Assembly si apre dal polso. Mostra documento attivo, contesto,
componente selezionato e stato della connessione. Il Trigger seleziona; il solo
Grip sposta la rappresentazione XR, senza cambiare il CAD. Dopo aver scelto
CAD Move, Grip + Trigger sul componente selezionato o sul manipolatore attivo
avvia una bozza di movimento, mai un commit. L'asse scelto resta l'unico
evidenziato durante il gesto.

I valori numerici usano millimetri e gradi. Scala visuale e posa del modello
nella stanza non alterano i valori CAD. Al rilascio si richiede la preview;
Applica resta disabilitato finché il risultato corrente non è validato e
visualizzato. Annulla elimina la bozza senza cambiare Inventor.

Il Browser conserva la distinzione fra contesto di navigazione e documento
attivo. Entrare in un sottoassieme non autorizza implicitamente una scrittura
nel suo file di definizione. Per modificarne i figli, M4 propone **Attiva questo
assieme in Inventor**, usando il documento già aperto e il percorso di
attivazione M2. Dopo l'attivazione rilegge scena, riferimenti e DOF. Mostra che
si sta modificando la definizione condivisa, con effetti su tutte le istanze.
Non modifica figli annidati tramite proxy del documento radice e non apre file
automaticamente. Assiemi flessibili/adattivi, virtuali, soppressi o non risolti
richiedono un messaggio di non supporto quando manca una prova del contesto.

## CAD Move e DOF

Per ogni selezione il backend restituisce identità documento/revisione,
occurrence, grounded/suppressed, trasformazione, conteggi di traslazione e
rotazione, direzioni disponibili e centro di rotazione nel contesto assembly.
Un dato DOF indisponibile è sconosciuto, mai zero o sei per convenienza.

Il gizmo mostra solo libertà documentate dal solver: frecce per traslazioni e
archi per rotazioni, con etichetta `DOF: N`. Non si deducono assi dai soli
conteggi. Dati incompleti disabilitano i relativi manipolatori.

Movimento numerico e spaziale producono lo stesso comando, con traslazione,
asse/centro/angolo e revisione di riferimento. Nessun auto-snap assembly.
Grounded o suppressed impediscono il movimento. I vincoli non vengono rimossi,
disattivati o aggirati. Inventor risolve la posa proposta; M4 rifiuta una posa
non raggiungibile, mantenendo la bozza editabile. Non sostituisce silenziosamente
la richiesta con una posa diversa. Il confronto usa tolleranze dichiarate:
0,01 mm per la posizione e 0,01 gradi per l'orientamento. Queste sono soglie
di accettazione del comando, non affermazioni sull'accuratezza del tracking Quest.

## Constraints e Joints

Due ingressi conducono allo stesso stato: tipo → geometria A → geometria B,
oppure due geometrie già selezionate → tipi compatibili. Riferimenti appartenenti
alla stessa occurrence non formano una relazione fra componenti.

Matrice iniziale dei vincoli: Mate/Flush fra piani, Mate Axis fra superfici
assiali, Insert fra spigoli circolari, Angle fra riferimenti orientabili e
Tangent fra superfici compatibili. La compatibilità definitiva spetta a Inventor;
la UI non promette il successo sulla sola base del tipo geometrico.

Alla seconda entità valida si avvia la preview senza un ulteriore passaggio di
conferma della selezione. Offset, angolo, direzioni e opzioni restano modificabili;
ogni variazione invalida Applica. Si mostrano A/B, nomi, assi/piani/direzioni e
nome del vincolo. Gli spigoli richiedono selezione esplicita e highlight proprio.

Joint: Rigid, Rotational, Slider (`slide` sul protocollo), Cylindrical, Planar e
Ball quando supportati. Un suggerimento non applica né blocca la scelta.
Origini, orientamento e gap sono espliciti; per Rotational/Cylindrical il backend
esistente documenta l'uso di spigoli circolari, non della sola faccia cilindrica.
Per un'origine Planar selezionata su una faccia piana, il backend deriva un
punto stabile dal centro di un bordo circolare o dal punto medio di un bordo
lineare della stessa faccia. La faccia senza punto di bordo non è un'origine
valida per `CreateAssemblyJointDefinition` di Inventor 2027.

## Preview, validazione e concorrenza

La preview esegue la proposta in una transazione posseduta dal backend, risolve
e valida, cattura geometria e trasformazioni prima dell'abort e conferma il
ripristino della revisione. Il GLB temporaneo non contiene riferimenti CAD
selezionabili e non entra nella cache delle definizioni persistenti.

Originale traslucido e risultato solido devono comprendere tutti i componenti
spostati dal solver. È vietato ricostruire il risultato usando le pose precedenti
o recuperando la mesh dopo il rollback. Limiti geometrici superati impediscono
una preview applicabile; non si omettono componenti senza dichiararlo.

Validazioni minime: rebuild e salute di tutti i vincoli/joint attivi. M4 richiede
anche assenza di interferenza volumetrica alla posa finale, con contatto ammesso;
la clearance aggiuntiva è opzionale, non negativa e impostata in mm, default 0.
Il pannello espone la politica prima della preview. Errori o timeout di questi
controlli bloccano Applica; non sono convertiti in successo. Un assieme con
interferenze preesistenti può quindi essere rifiutato: il dettaglio identifica
la coppia coinvolta. Nessuna verifica del volume spazzato durante il movimento.

Il piano lega operazioni, controlli, documento, revisione, client e scadenza.
Cambio bozza/documento/revisione, disconnessione o perdita tracking durante il
gesto annullano la possibilità di applicare. Risposte tardive non ripristinano
un piano superato. Un commit di esito incerto non viene ritentato: serve lettura
autorevole e conferma dell'utente dopo aver controllato il CAD.

Undo/Redo mantiene le garanzie della cronologia XR M3 e rifiuta di annullare
modifiche desktop intermedie. Un errore mantiene valori editabili, ultimo ghost
distinguibile come non applicabile, messaggio breve e dettagli consultabili.

## Verifica e accettazione

1. Core: compatibilità geometrica, input finiti/unità, stato bozza, revisione,
   scadenza, risposte tardive, doppio Apply, perdita connessione e risultato incerto.
2. Backend: rollback completo, proprietà della transazione, identità client,
   assenza di riferimenti transitori, DOF incompleti, salute vincoli e joint.
3. Unity: selezione A/B, highlight, conversioni a scale diverse, assi/archi,
   separazione Grip e Grip + Trigger, arbitraggio UI/ray, cambio modalità e cleanup.
4. Inventor reale: assieme temporaneo, componente libero, grounded, vincolo che
   lascia una traslazione, joint rotazionale, movimento vietato, preview/cancel,
   commit e Undo/Redo; conservazione dei documenti dell'utente.
5. Quest: leggibilità e accuratezza del gizmo, input numerico e spaziale,
   selezione spigoli, workflow contestuale, tracking/rete persi, cambio documento,
   ritorno a Inspect/Design e verifica sul PC del risultato persistente.

La compilazione e i test FakeAddIn non costituiscono accettazione Inventor/Quest.
Ogni esito deve essere registrato con ambiente, comando, risultato e limiti.

## Architettura e contratto da implementare

I nomi nuovi di questa sezione sono proposti; non sono API già disponibili.
Il tier sperimentale resta necessario su server e add-in.

| Livello | Responsabilità M4 | Estensione prevista |
|---|---|---|
| Add-in | contesto, riferimenti proxy, libertà native, solver | query `get_assembly_context_xr` e operazioni batch assembly |
| Server MCP | capability, schema, autorizzazione, piano, asset | tool `inventor_get_assembly_context_xr`, catalogo e preview assembly |
| Core | DTO verificati, compatibilità, bozza e unità | `AssemblyContext`, `AssemblyOperations`, riuso del ciclo transazionale M3 |
| Unity runtime | ghost, riferimenti A/B, gizmo DOF | preview in coordinate assembly e visuali senza riferimenti transitori |
| Unity XR | controller, menu, selezioni, tastiera | `AssemblyWorkspace` collegato ad AppController e menu polso |

### Letture e identità

La query riceve `document_id`, `expected_revision` e un `occurrence_id` opzionale.
Senza occurrence elenca i figli diretti supportati; con occurrence restituisce
DOF e geometrie selezionabili del componente. La risposta include:

| Campo | Contratto |
|---|---|
| `document_id`, `revision`, `kind` | devono coincidere con il contesto richiesto; `kind = assembly` |
| `occurrence_id`, `definition_document_id` | distinguono istanza e definizione condivisa |
| `editable`, `unavailable_reason` | capacità reale per quel componente, con ragione traducibile |
| `grounded`, `suppressed`, `adaptive` | nessun default che abiliti la modifica in caso di dati mancanti |
| `matrix_rowmajor_cm` | trasformazione nel contesto assembly, convenzione esistente |
| `dof_translation`, `dof_rotation` | interi 0–3 oppure null; totale mostrato solo quando entrambi noti |
| `translation_axes`, `rotation_axes` | direzioni finite normalizzate, in coordinate CAD assembly |
| `rotation_center_mm` | centro finito esplicito; null disabilita il corrispondente gesto |
| `dof_complete` | true solo se conteggi e rappresentazione delle libertà sono coerenti |
| `references` | ID proxy stabili, occurrence proprietaria, tipo, nome e geometria visualizzabile |
| `truncated` | esplicito; le informazioni omesse non abilitano operazioni |

Limiti proposti: 2.000 occurrence nel contesto, 5.000 riferimenti per query e
500.000 triangoli nell'intera preview. L'elenco riferimenti può essere paginato
o filtrato, mantenendo la stessa revisione. Nessun risultato troncato può
essere descritto come insieme completo. Il backend controlla deadline durante
enumerazione, tessellazione e controlli fra coppie.

Le direzioni native vanno caratterizzate con prove Inventor prima di definirne
il parser definitivo: una rotazione libera a tre DOF o un meccanismo accoppiato
non va ridotto a un asse inventato. Il rilascio M4 richiede almeno componente
libero, slider e giunto rotazionale verificati; altre configurazioni devono
essere dichiarate non supportate quando non rappresentabili correttamente.

### Scritture nella transazione esistente

Il client usa `inventor_plan_change` e `inventor_commit_plan`, senza una seconda
API di commit. Nuove operazioni sperimentali del catalogo:

| Operazione proposta | Argomenti principali |
|---|---|
| `assembly_move` | `occurrence_id`, `translation_mm[3]`, eventuali `rotation_axis[3]`, `rotation_center_mm[3]`, `rotation_degrees` |
| `assembly_constraint` | `type`, `entity_a_id`, `entity_b_id`, `offset_mm` oppure `angle_degrees`, opzioni coerenti con il tipo |
| `assembly_joint` | `joint_type`, `origin_a_id`, `origin_b_id`, `gap_mm`, `flip_origin`, `flip_alignment` |

Ogni bozza UI M4 rappresenta un solo comando. Gli handler batch eseguono solo
la mutazione all'interno della transazione atomic posseduta; non avviano,
concludono o annullano una transazione propria. Si estraggono primitive native
riutilizzabili dagli handler esistenti, preservando le loro API pubbliche e i
controlli per i chiamanti precedenti. Non si introducono flag che permettano
di saltare autorizzazioni o controlli fuori da atomic batch.

I controlli `rebuild`, `constraint_health`, `interference` e l'eventuale
`min_clearance:Nmm` fanno parte del piano consumato dal commit. `feature_health`
è specifico delle parti: il client M3 lo richiede oggi e deve essere adattato
esplicitamente al tipo documento, senza indebolire i controlli di Design.

La preview assembly restituisce un GLB in metri con geometria già trasformata
nel sistema del documento attivo, oppure istanze con trasformazioni catturate
nella stessa transazione. La scelta implementativa proposta è geometria
consolidata per riusare il reader attuale; il budget riguarda l'intero assieme,
non ciascuna definizione. Normali, orientamento delle facce, parti ripetute e
sottoassiemi devono essere verificati. Nessun ID temporaneo in asset o selezione.

### Stato e ciclo di vita

| Evento | Risultato richiesto |
|---|---|
| ingresso Assembly | legge contesto coerente con la scena; strumenti disabilitati durante la lettura |
| inizio gesto intenzionale | `CAD MOVE ARMED`; cattura posa iniziale e revisione |
| trascinamento | aggiorna bozza locale; nessuna scrittura CAD o chiamata per frame |
| rilascio valido / seconda entità | una richiesta preview; nuova generazione annulla il valore delle risposte precedenti |
| preview riuscita e GLB mostrato | abilita Applica per quel piano soltanto |
| rendering fallito | Applica disabilitato, errore recuperabile |
| Apply | un tentativo, UI bloccata fino all'esito, niente retry automatico |
| successo | ricarica scena, contesto e DOF; elimina ghost e riferimenti superati |
| errore validazione | conserva parametri; ghost precedente etichettato come non aggiornato |
| tracking perso nel gesto | termina il gesto senza preview automatica, richiede nuova azione intenzionale |
| rete persa / revisione cambiata | invalida piano e riferimenti; vista e navigazione locale restano disponibili |
| uscita dalla modalità | scarta bozza non applicata, libera visuali; non cancella il controllo di esito incerto |

Design e Assembly condividono l'arbitraggio di mutazione: un commit in corso o
incerto impedisce di aggirare il blocco cambiando modalità. Eventi, richieste,
mesh/materiali temporanei e handler input vengono sganciati o distrutti alla
chiusura/disabilitazione secondo il ciclo di vita esistente.

## Gate di consegna

| ID | Caso | Evidenza richiesta |
|---|---|---|
| A01 | Grip semplice | posa CAD e revisione invariati dopo manipolazione XR |
| A02 | libero: traslazione e rotazione | stessa posa finale via numeri e controller, a scala 1:1 e ridotta |
| A03 | grounded / DOF sconosciuto | gesto non avviabile, motivo visibile, nessuna transazione |
| A04 | slider / rotational | solo libertà residue, vincoli intatti, solver e gizmo concordanti |
| A05 | movimento vietato | rollback e bozza conservata; nessun Apply possibile |
| A06 | Mate, Flush, Mate Axis, Insert, Angle, Tangent | almeno un caso nativo positivo e uno incompatibile per tipo |
| A07 | workflow contestuale | A/B selezionate prima del comando, lista compatibile, niente proximity auto-snap |
| A08 | sei tipi joint | caso positivo per ogni tipo dichiarato disponibile; fallback esplicito per gli altri |
| A09 | preview/cancel | documento ripristinato, nessun riferimento transitorio, assieme completo nel ghost |
| A10 | Apply / Undo / Redo | risultato persistente e cronologia XR; edit desktop intermedio rifiutato |
| A11 | rete, revisione, scadenza | piano inutilizzabile; risposta tardiva non riabilita Applica |
| A12 | esito commit incerto | nessun doppio commit, blocco mantenuto anche passando a Design |
| A13 | sottoassieme ripetuto | navigazione distinta da attivazione, contesto e impatto sulla definizione visibili |
| A14 | UI, tracking, errori | nessun click-through/gesto involontario, errore leggibile, cleanup dopo riaperture |
| A15 | regressione M1–M3 | pairing, Inspect, Design, selezione facce e cronologia continuano a funzionare |

Una voce non provata resta aperta. Se un caso obbligatorio A01–A15 non passa,
la milestone non è accettata; un pulsante disabilitato non sostituisce una
funzione richiesta, salvo i limiti di supporto esplicitamente descritti sopra.

## Risposte alla regola §57

| Domanda | Risposta M4 |
|---|---|
| entità CAD | occurrence diretta, geometrie proxy A/B e relazione assembly |
| contesto attivo | documento assembly esplicito; attivazione separata per figli di sottoassiemi |
| modalità | Assembly, esclusiva rispetto a Design per input e mutazioni |
| controller | Trigger seleziona; Grip + Trigger arma il manipolatore |
| grab semplice | sola posa della rappresentazione XR |
| intenzionalità | gesto armato o input numerico, poi Applica esplicito dopo preview |
| precisione | mm/gradi indipendenti da scala stanza; solver autorevole |
| voce | nomi comando/parametri stabili per M5, nessun riconoscimento vocale M4 |
| preview | risultato nativo catturato prima dell'abort, originale traslucido |
| validator | rebuild, salute vincoli/joint, interferenza finale, clearance opzionale |
| errori | bozza conservata, messaggio breve, dettagli e riferimenti coinvolti |
| stale | piano/riferimenti invalidati; nuova lettura e preview obbligatorie |
| disconnessione | vista locale, niente Apply; esito incerto protetto senza retry |
| familiarità Inventor | nomi e geometrie dei vincoli/joint espliciti, nessun auto-snap |
