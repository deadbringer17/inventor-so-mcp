# Inventor XR SO
## Product & UX Specification — MVP

**Versione:** 0.1  
**Stato:** Product direction cristallizzata  
**Target hardware MVP:** Meta Quest 3 + Meta Quest Touch Plus  
**Backend:** Inventor SO MCP su PC Windows con Autodesk Inventor 2027  
**Lingua MVP:** Italiano

---

# 1. Product Vision

**Inventor XR SO** è un ambiente CAD immersivo per Meta Quest 3 che utilizza Autodesk Inventor come motore parametrico e `inventor-so-mcp` come backend semantico, transazionale e sicuro.

L'obiettivo non è replicare l'interfaccia desktop di Inventor dentro un visore.

L'obiettivo è preservare:
- terminologia;
- modello mentale;
- gerarchie;
- logica parametrica;
- feature;
- workflow CAD;

trasformandoli in una esperienza spaziale nativa XR.

```text
Autodesk Inventor
      ↓
Inventor SO Add-in
      ↓
Inventor SO MCP
      ↓
HTTPS / MCP / Assets
      ↓
Inventor XR SO
      ↓
Meta Quest 3
```

Il modello CAD deve essere l'interfaccia primaria. Menu, pannelli, Browser e ribbon servono a supportare l'azione sul modello e non devono diventare il centro dell'esperienza.

---

# 2. Principi non negoziabili

## 2.1 Model-first
Il modello è sempre il protagonista della scena. La UI appare quando serve, in forma contestuale e con il minimo ingombro possibile.

## 2.2 Familiarità Inventor
La tassonomia deve rimanere riconoscibile per un utente Inventor. Usare nomi e concetti come:
- Schizzo
- Modello 3D
- Lamiera
- Assembla
- Ispeziona
- Estrusione
- Smusso
- Raccordo
- Quota
- Vincolo
- Flangia
- Piega
- Sviluppo

Non creare una nuova terminologia XR quando esiste già un equivalente Inventor consolidato.

## 2.3 Nessuna modifica accidentale
Interagire normalmente con il modello non deve modificare il CAD. La navigazione e la manipolazione visuale devono essere separate dalle operazioni CAD persistenti.

Per spostare realmente un componente in modalità Assembly è richiesta una intenzionalità fisica esplicita:

```text
Grip + Trigger
```

sulla stessa mano.

Una presa normale modifica solo la visualizzazione o la manipolazione locale della scena.

## 2.4 Preview obbligatoria nell'MVP

Ogni modifica CAD dell'MVP segue:

```text
ACTION
  ↓
PREVIEW
  ↓
VALIDATION
  ↓
APPLY / CANCEL
  ↓
COMMIT
```

Il modello originale resta disponibile come ghost durante la preview.

## 2.5 Tre canali di precisione

Quando possibile, una operazione dimensionale deve essere controllabile attraverso tre canali equivalenti:
1. manipolazione fisica;
2. input numerico;
3. input vocale.

Esempio:

```text
Estrusione
   ↓
drag della freccia
oppure
50 mm da keypad
oppure
"cinquanta millimetri"
```

## 2.6 Inventor resta il CAD master
Inventor sul PC è la sorgente autorevole dello stato CAD. Se l'utente cambia documento attivo direttamente da Inventor, Inventor XR SO segue automaticamente il nuovo documento.

---

# 3. Utente target

Utente principale:
- progettista meccanico;
- ingegnere;
- tecnico R&D;
- progettista di assiemi;
- utente già familiare con Inventor.

Inventor XR SO non deve richiedere di imparare una nuova disciplina CAD. Deve invece rendere spaziali operazioni già note.

---

# 4. Architettura

## 4.1 PC

```text
Autodesk Inventor 2027
        │
        ▼
Inventor SO Add-in
        │
    Named Pipe
        ▼
Inventor SO MCP
        │
        ├── Streamable HTTP MCP
        ├── /assets
        ├── authentication
        ├── events
        ├── revision
        ├── scene graph
        ├── GLB
        ├── plan / preview / commit
        └── validation
```

## 4.2 Quest

```text
Inventor XR SO
├── Network / MCP client
├── Scene graph
├── GLB loader
├── CAD selection layer
├── Interaction system
├── UI spatial system
├── Preview renderer
├── Voice input
└── Session state
```

Quest non accede mai a COM, Named Pipe, file CAD locali o credenziali interne dell'add-in.

---

# 5. Session lifecycle

## 5.1 Home
L'app apre una **Home tecnica**.

Deve mostrare:
- PC/server disponibili;
- stato connessione;
- versione backend;
- versione Inventor;
- documento Inventor attivo;
- tipo documento;
- stato MCP;
- eventuali problemi di connessione.

La Home deve rimanere leggibile e non sembrare una console diagnostica.

## 5.2 Pairing
Prima associazione tramite QR code generato dal PC.

```text
PC mostra QR
    ↓
Quest scansiona
    ↓
PC associato
```

## 5.3 Documento iniziale
All'ingresso nella sessione:
- usare il documento Inventor attivo;
- permettere dal Browser di passare ad altri documenti aperti.

## 5.4 Cambio documento da Inventor

```text
Inventor cambia ActiveDocument
        ↓
evento backend
        ↓
Quest segue automaticamente
        ↓
carica nuova scena
```

## 5.5 Perdita connessione
In caso di perdita connessione:
- il modello resta visibile;
- la sessione diventa read-only;
- indicatore offline visibile ma non invasivo;
- tentativo automatico di riconnessione;
- al ritorno verificare capabilities, document identity, revision, visual revision e asset.

---

# 6. Spatial environment

All'avvio l'utente sceglie:

```text
Mixed Reality
Studio VR
```

Le due modalità devono usare la stessa UI e gli stessi workflow CAD.

## 6.1 Mixed Reality
Passthrough attivo. Il modello viene collocato nello spazio reale.

Uso ideale:
- validazione 1:1;
- ingombri;
- manutenzione;
- assembly review;
- confronto fisico;
- progettazione su tavolo reale.

## 6.2 Studio VR
Ambiente neutro orientato alla progettazione.

Uso ideale:
- sessioni lunghe;
- concentrazione;
- grandi modelli;
- lavoro senza vincoli dello spazio reale.

---

# 7. Scala e posizionamento

## 7.1 Default
Il modello entra in scena a **scala 1:1**. Appare automaticamente davanti all'utente e può essere riposizionato e ruotato globalmente.

## 7.2 Alternative rapide

```text
1:1
Fit to room
Table scale
```

## 7.3 Modello più grande della stanza
Non ridurre automaticamente il CAD senza consenso.

Mostrare:

```text
[ Mantieni 1:1 ]
[ Fit to room ]
[ Table scale ]
```

---

# 8. Input model MVP

## 8.1 Controller-first
L'MVP viene sviluppato e validato per **Meta Quest Touch Plus**. Hand tracking non è requisito dell'MVP.

## 8.2 Mano dominante
Configurabile al primo avvio:

```text
Destrimano
Mancino
```

Esempio destrimano:

```text
MANO SINISTRA
- menu polso
- Browser
- tab
- modalità
- undo/redo

MANO DESTRA
- ray
- selezione
- tool corrente
- Grip + Trigger CAD move
- push-to-talk
```

---

# 9. Interazione primaria

## 9.1 Selezione ibrida

```text
vicino → direct interaction
lontano → ray
```

## 9.2 Manipolazione visuale
Grab normale:
- ruota;
- sposta;
- esplora;
- manipola la rappresentazione locale;

ma non modifica persistentemente il CAD.

## 9.3 CAD Move

```text
Grip + Trigger
        ↓
CAD MOVE ARMED
        ↓
feedback visivo
        ↓
spostamento preview
        ↓
rilascio
        ↓
validation
        ↓
Apply / Cancel
```

Quando CAD Move è armato:
- evidenziare chiaramente il componente;
- mostrare gizmo DOF;
- distinguere lo stato dal grab visuale.

---

# 10. Modalità operative

Tre modalità principali:

```text
INSPECT
DESIGN
ASSEMBLY
```

Ogni modalità ha un feedback visivo discreto nell'HUD/UI.

---

# 11. UI architecture

## 11.1 Paradigma
UI ibrida:

```text
Spatial interaction
+
pannelli contestuali
```

## 11.2 Menu polso
Il menu principale vive sul polso non dominante.

Sul polso compaiono le principali **tab riconoscibili di Inventor** in base al contesto.

Esempi:

```text
Modello 3D
Schizzo
Lamiera
Assembla
Ispeziona
```

## 11.3 Ribbon / tool panel
Selezionando una tab sul polso:
- non riempire il polso di comandi;
- aprire un pannello/ribbon flottante;
- il pannello può essere riposizionato;
- resta nella posizione scelta per la durata della sessione.

Esempio:

```text
SCHIZZO

Linea
Cerchio
Arco
Rettangolo
Quota
Vincolo
Proietta geometria
...
```

---

# 12. Browser / Model Tree

Il Browser è:
- nascosto di default;
- richiamabile dal polso;
- pinnabile nello spazio.

Una volta pinnato rimane nella posizione scelta durante la sessione.

Deve riflettere la struttura Inventor.

---

# 13. Active context e breadcrumb

Il sistema segue la gerarchia Inventor.

Esempio:

```text
APE_Main.iam
  > Drive_Module.iam
      > Wheel_Support.ipt
```

Il breadcrumb deve:
- mostrare il contesto attivo;
- permettere di tornare ai livelli superiori;
- rimanere minimale;
- essere accompagnato da un comando Back rapido.

---

# 14. Selection hierarchy

La selezione dipende dal documento attivo e dal contesto.

## Assembly attivo
Default:
- occurrence;
- subassembly.

## Subassembly aperto
Default:
- occurrence contenute.

## Part attiva
Default:
- facce;
- bordi;
- feature;
- geometria di sketch quando applicabile.

Non mostrare ogni volta un menu `Face | Body | Part | Assembly`.

Doppio pinch / doppia azione equivalente = `Apri / Modifica`, mantenendo anche i comandi espliciti nel menu contestuale.

---

# 15. Pannello contestuale

Quando si seleziona un'entità, mostrare un piccolo badge vicino all'oggetto.

Esempio:

```text
┌───────────────┐
│ Bracket.ipt   │
│       >       │
└───────────────┘
```

Aprendo il badge compare un pannello più completo davanti all'utente.

---

# 16. Inspect Mode

Inspect è la modalità sicura di lettura.

Azioni principali:
- selezione;
- misure;
- materiale;
- massa;
- proprietà;
- vincoli;
- DOF;
- BOM;
- parametri;
- interference check;
- section;
- X-Ray;
- isolate;
- hide/show visuale;
- flat-pattern inspection.

## 16.1 Scheda compatta
Alla selezione mostrare inizialmente pochi dati essenziali.

Esempio:

```text
Bracket.ipt
Steel
1.28 kg
3 constraints

[ Dettagli ]
```

---

# 17. Misure

Le misure sono temporanee di default.

```text
|<------ 234.5 mm ------>|
```

L'utente può usare `Pin` per mantenerle nello spazio.

Supporto progressivo:
- distanza;
- angolo;
- raggio;
- diametro;
- coordinate;
- minimum distance;
- area;
- volume.

---

# 18. Section

La sezione deve essere fisica.

```text
Section
   ↓
compare piano
   ↓
grab piano
   ↓
traslazione / rotazione
   ↓
sezione aggiornata
```

In parallelo il pannello permette precisione:

```text
Offset: 125 mm
Angle: 30°
```

---

# 19. Design Mode

Design abilita:
- Schizzo;
- Modello 3D;
- feature;
- parametri;
- modifica geometrica.

Regola:

```text
Direct manipulation
        +
exact parameter
        +
preview
```

---

# 20. Sketch Workflow

## 20.1 Crea schizzo
Se esiste una faccia/piano valido già selezionato:

```text
Crea schizzo
     ↓
entra direttamente nello sketch
```

Se non esiste selezione, proporre XY, XZ, YZ, work plane e facce valide.

## 20.2 Interazione sketch
Ibrida:

```text
ray → precisione
touch/pinch vicino → interazione diretta
```

Il ray resta il riferimento principale per operazioni CAD precise.

## 20.3 Linea

```text
Linea
  ↓
punto 1
  ↓
punto 2
```

Durante il movimento mostrare snap, inferenze, coordinate e relazione geometrica prevista.

---

# 21. Sketch snap

Gli snap sono automatici ma leggibili.

Supporto progressivo:
- endpoint;
- midpoint;
- centre;
- intersection;
- horizontal;
- vertical;
- tangent;
- concentric.

Quando uno snap è candidato:
- mostrare un indicatore spaziale chiaro;
- permettere di bloccarlo intenzionalmente.

---

# 22. Vincoli geometrici Sketch

Politica mista.

Automatici:
- Coincidente;
- Orizzontale;
- Verticale.

Da confermare:
- Tangente;
- Uguale;
- Simmetria;
- altri vincoli più invasivi.

## 22.1 Visualizzazione vincoli
Default nascosti.

Selezionando una geometria, mostrare i vincoli interessati.

Comando `Mostra tutti i vincoli` per visualizzare l'intero sketch.

---

# 23. Quote

Il workflow Inventor deve rimanere disponibile:

```text
Quota
  ↓
selezione geometria
  ↓
posizionamento
  ↓
valore
```

In aggiunta, la selezione contestuale può mostrare subito:
- lunghezza;
- raggio;
- diametro;
- angolo.

Input:
- keypad numerico;
- voice;
- manipolatore quando applicabile.

---

# 24. Feature dimensionali

Principio generale:

```text
Feature
  ↓
manipolatore spaziale
+
pannello parametrico
```

Esempio Estrusione:

```text
             ↑
             │
          42.5 mm
             │
          profilo

[ Operazione: Join ▼ ]
[ Direzione: Positive ▼ ]
[ 42.5 mm ]
```

L'utente può trascinare, parlare o digitare. I tre sistemi modificano lo stesso parametro.

---

# 25. Preview

Preview MVP: **Ghost semplice**.

```text
Originale → semi-trasparente
Preview   → solida
```

---

# 26. Validation

Prima del commit:

```text
Preview
  ↓
Validation
```

Possibili verifiche:
- rebuild;
- feature health;
- sketch health;
- constraints;
- references;
- interference;
- clearance;
- drawing validation;
- sheet metal checks.

## 26.1 Validation failure
Se fallisce:
- non chiudere il comando;
- mantenere il ghost;
- evidenziare la geometria problematica;
- mostrare messaggio sintetico;
- offrire `Dettagli`;
- consentire di modificare parametri e riprovare.

---

# 27. Error UX

Gli errori non devono essere solo popup testuali.

Formato:

```text
evidenziazione geometria
+
messaggio breve
+
dettagli tecnici opzionali
```

---

# 28. Assembly Mode

Assembly segue concetti Inventor ma con interazione spaziale.

Funzioni:
- selezione occurrence;
- apertura subassembly;
- CAD Move;
- vincoli;
- joint;
- DOF;
- grounding;
- replace;
- pattern;
- representations;
- inspection.

---

# 29. Assembly movement

Per spostare persistentemente un componente:

```text
Grip + Trigger
```

Se il componente è vincolato, muoverlo solo nei gradi di libertà effettivamente consentiti.

Mostrare:
- assi disponibili;
- rotazioni disponibili;
- DOF residui.

---

# 30. Assembly constraints

Supportare due workflow.

## Workflow Inventor

```text
Vincolo
  ↓
tipo
  ↓
geometria A
  ↓
geometria B
```

## Workflow contestuale
Se l'utente ha già selezionato due geometrie compatibili, mostrare i vincoli applicabili.

**Nessun auto-snap assembly nell'MVP.**

---

# 31. Constraint representation

Mostrare:
- riferimenti geometrici;
- assi;
- piani;
- direzioni;
- nome vincolo.

Esempio:

```text
Mate
Insert
Angle
Tangent
```

---

# 32. Constraint commit

Quando viene selezionata la seconda entità valida:
- calcolare subito il risultato;
- mostrare la preview;
- evitare passaggi di menu inutili;
- mantenere Preview → Validation → Apply/Cancel.

---

# 33. Joint

Il sistema può suggerire il tipo di Joint compatibile.

Esempio:

```text
Suggested: Rotational
```

L'utente può sempre sostituirlo con:
- Rigid;
- Rotational;
- Slider;
- Cylindrical;
- Planar;
- Ball;

quando supportato.

---

# 34. DOF

Quando un componente è selezionato, mostrare `DOF: N` nel pannello e un gizmo spaziale delle libertà residue.

---

# 35. Lamiera

Quando il documento è Sheet Metal, `Lamiera` diventa la tab primaria.

Devono comunque restare disponibili quando appropriato:
- Modello 3D;
- Schizzo;
- Ispeziona.

---

# 36. Feature Lamiera

Seguono lo stesso paradigma delle feature solide.

Esempio Flangia:

```text
seleziona bordo
     ↓
Flangia
     ↓
manipolatore spaziale
+
pannello
```

Drag e valore numerico restano sincronizzati.

---

# 37. Flat Pattern

Il comportamento standard resta simile a Inventor.

In più l'utente può usare:

```text
Detach Flat Pattern
```

e posizionare lo sviluppo accanto al pezzo piegato.

Lo sviluppo staccato è inizialmente una rappresentazione visuale, non un secondo documento indipendente.

---

# 38. Voice system

## 38.1 MVP
Voce attivata tramite pulsante fisico del controller.

**Push-to-talk.**

Il microfono non rimane permanentemente in ascolto.

## 38.2 Lingua
Italiano.

## 38.3 Funzioni

### Input numerico

```text
"centoventi millimetri"
"dodici virgola cinque"
"quarantacinque gradi"
```

### Voice Command Mirror
Ogni comando manuale importante deve poter essere richiamato anche a voce.

Esempi:

```text
"Crea schizzo"
"Schizzo"
"Linea"
"Crea linea"
"Smusso"
"Raccordo"
"Quota"
"Misura"
"Isola"
"Annulla"
```

La voce nell'MVP non deve essere un agente AI completo: deve mappare voce → comando disponibile.

---

# 39. Undo / Redo

Disponibili direttamente dal menu polso.

MVP:

```text
Undo
Redo
```

---

# 40. UI Look & Feel

Stile:

**XR moderno + familiarità Inventor**

Caratteristiche:
- pannelli puliti;
- profondità leggibile;
- trasparenza controllata;
- poche decorazioni;
- icone tecniche;
- nomenclatura Inventor;
- gerarchia chiara;
- colori di stato coerenti.

Non copiare pixel-per-pixel Inventor desktop.

---

# 41. Modalità visuali

Le modalità devono avere una differenziazione discreta:

```text
INSPECT
DESIGN
ASSEMBLY
```

Usare soprattutto:
- bordo HUD;
- indicatore modalità;
- accent UI;
- stato tool.

---

# 42. Stato dei pannelli

Per l'MVP i pannelli mantengono la posizione durante la sessione.

Non è richiesta persistenza tra riavvii.

---

# 43. MVP — Scope funzionale

## Connessione
- [ ] Home tecnica
- [ ] pairing QR
- [ ] autenticazione
- [ ] capabilities
- [ ] connessione al server
- [ ] riconnessione automatica

## Documento
- [ ] seguire ActiveDocument Inventor
- [ ] Browser documenti
- [ ] scene graph
- [ ] visual revision
- [ ] incremental asset refresh

## Visualizzazione
- [ ] GLB
- [ ] assembly instancing
- [ ] 1:1
- [ ] Fit to room
- [ ] Table scale
- [ ] MR
- [ ] Studio VR

## Input
- [ ] Touch Plus
- [ ] destrimano/mancino
- [ ] ray
- [ ] near interaction
- [ ] grab visuale
- [ ] Grip+Trigger CAD Move

## UI
- [ ] menu polso
- [ ] tab Inventor
- [ ] tool panel
- [ ] Browser pinnabile
- [ ] breadcrumb
- [ ] pannello contestuale
- [ ] mode indicator

## Inspect
- [ ] selection
- [ ] properties
- [ ] measure
- [ ] pin measure
- [ ] section
- [ ] mass/material/basic properties
- [ ] DOF display

## Design
- [ ] create sketch
- [ ] line
- [ ] circle
- [ ] arc
- [ ] rectangle
- [ ] dimensions
- [ ] basic constraints
- [ ] extrude
- [ ] hole
- [ ] fillet
- [ ] chamfer
- [ ] parametric input

## Assembly
- [ ] occurrence selection
- [ ] subassembly context
- [ ] component move preview
- [ ] constraint workflow
- [ ] joint workflow
- [ ] DOF gizmo
- [ ] grounding where supported

## Sheet Metal
- [ ] Lamiera tab
- [ ] key sheet-metal commands già esposti dal backend
- [ ] spatial parameter manipulation
- [ ] flat pattern
- [ ] detachable visual flat pattern

## Voice
- [ ] push-to-talk
- [ ] numeric dictation
- [ ] Voice Command Mirror
- [ ] vocabolario italiano

## Safety
- [ ] Preview
- [ ] Validation
- [ ] Apply / Cancel
- [ ] stale revision handling
- [ ] connection loss read-only
- [ ] visible errors
- [ ] retry validation

---

# 44. Post-MVP

Non bloccare l'MVP per queste capability.

## Input
- hand tracking;
- gesture CAD;
- eye tracking su hardware futuro;
- multimodal controller + hands.

## AI
- natural-language CAD agent;
- multi-step engineering commands;
- design intent reasoning;
- automatic change plan generation;
- conversational inspection.

## Rendering
- advanced LOD;
- geometry streaming;
- advanced X-Ray;
- richer CAD diff;
- exploded animation;
- large-assembly optimization.

## UI
- persistent spatial layout;
- project workspaces;
- advanced timeline;
- command favorites;
- customizable wrist menu.

## CAD
- full drawing authoring;
- GD&T;
- Content Center;
- fastener intelligence;
- advanced motion;
- Apprentice indexing;
- semantic search.

---

# 45. Non-obiettivi MVP

Inventor XR SO MVP non deve diventare:
- un clone completo di Inventor desktop;
- un file manager CAD;
- un replacement totale del PC;
- un AI agent autonomo completo;
- un sistema hand-tracking-first;
- un motore CAD indipendente;
- un viewer passivo senza capacità di modifica.

---

# 46. User Journey 1 — Open & Inspect

```text
Avvia Inventor XR SO
        ↓
Home
        ↓
connessione PC
        ↓
ActiveDocument
        ↓
modello 1:1
        ↓
Inspect
        ↓
ray su componente
        ↓
scheda compatta
        ↓
Dettagli
        ↓
Measure
        ↓
Pin misura
```

---

# 47. User Journey 2 — Sketch & Extrude

```text
Design
  ↓
seleziona faccia
  ↓
Crea schizzo
  ↓
Linea
  ↓
snap
  ↓
Quota
  ↓
"cinquanta millimetri"
  ↓
chiudi sketch
  ↓
Estrusione
  ↓
drag freccia
  ↓
50 mm
  ↓
ghost preview
  ↓
validation
  ↓
Apply
```

---

# 48. User Journey 3 — Component Move

```text
Assembly
   ↓
seleziona occurrence
   ↓
DOF gizmo
   ↓
Grip + Trigger
   ↓
CAD MOVE ARMED
   ↓
sposta nei DOF disponibili
   ↓
rilascia
   ↓
ghost preview
   ↓
validation
   ↓
Apply
```

---

# 49. User Journey 4 — Constraint

```text
Assembly
   ↓
Vincolo
   ↓
tipo
   ↓
entità A
   ↓
entità B
   ↓
preview immediata
   ↓
validation
   ↓
Apply
```

La semplice vicinanza dei componenti non genera suggerimenti automatici.

---

# 50. User Journey 5 — Sheet Metal

```text
Part lamiera
   ↓
Lamiera tab primaria
   ↓
seleziona bordo
   ↓
Flangia
   ↓
drag manipolatore
   ↓
"novanta gradi"
   ↓
preview
   ↓
validation
   ↓
Apply
   ↓
Flat Pattern
   ↓
Detach
   ↓
confronto piegato / sviluppo
```

---

# 51. User Journey 6 — Voice

```text
premi Push-to-Talk
        ↓
"Smusso"
        ↓
tool Smusso
```

oppure:

```text
premi Push-to-Talk
        ↓
"dodici virgola cinque millimetri"
        ↓
campo corrente = 12.5 mm
```

---

# 52. Prima milestone Unity

La prima build Quest non deve tentare di implementare tutto il CAD.

Definition of Done:

```text
Quest 3
  ↓
pairing / connessione
  ↓
get_capabilities
  ↓
ActiveDocument
  ↓
get_scene_graph
  ↓
download GLB
  ↓
render 1:1
  ↓
seleziona occurrence
  ↓
seleziona face
  ↓
Face ID corretto
  ↓
highlight sul Quest
  ↓
highlight su Inventor
```

Questa milestone chiude il primo loop reale:

```text
QUEST ↔ MCP ↔ INVENTOR
```

---

# 53. Seconda milestone Unity

```text
Inspect
+
Browser
+
breadcrumb
+
Measure
+
Section
+
scale modes
+
MR / Studio
```

---

# 54. Terza milestone Unity

```text
Design
+
Sketch basic
+
parameter input
+
Extrude
+
Hole
+
Fillet / Chamfer
+
Preview
+
Validation
+
Commit
```

---

# 55. Quarta milestone Unity

```text
Assembly
+
CAD Move
+
DOF
+
Constraints
+
Joints
```

---

# 56. Quinta milestone Unity

```text
Sheet Metal
+
Voice Command Mirror
+
Flat Pattern XR
+
UX polishing
```

---

# 57. Regola di sviluppo

Ogni nuova funzione Unity deve rispondere a queste domande:

1. Quale entità CAD manipola?
2. Qual è il contesto Inventor attivo?
3. È Inspect, Design o Assembly?
4. Qual è l'interazione controller?
5. Cosa succede con un semplice grab?
6. Cosa rende intenzionale una modifica CAD?
7. Esiste input preciso numerico?
8. Esiste mapping vocale quando opportuno?
9. Come viene mostrata la preview?
10. Quali validator vengono eseguiti?
11. Come vengono mostrati gli errori?
12. Cosa accade se la revision diventa stale?
13. Cosa accade se la connessione cade?
14. L'utente Inventor riconosce il comando e il suo significato?

Se queste domande non hanno risposta, la feature non è pronta per entrare nell'MVP.

---

# 58. Sintesi prodotto

**Inventor XR SO** deve essere percepito come:

> Inventor che esce dallo schermo e diventa uno spazio di lavoro tridimensionale.

Non come:

> Inventor desktop proiettato su finestre VR.

Il paradigma finale è:

```text
MODELLO
  ↓
SELEZIONE
  ↓
CONTESTO
  ↓
AZIONE FISICA / NUMERICA / VOCALE
  ↓
PREVIEW
  ↓
VALIDATION
  ↓
COMMIT
```

Questa sequenza costituisce il nucleo dell'esperienza Inventor XR SO.
