# Inventor XR SO — piano per quattro esperienze

Data: 25 ottobre 2026. Stato: **F0–F4 implementate nel software sul branch `nuova_ux`; accettazione Quest/fisica aperta**.
Esiti e limiti: [verbale della prima consegna](../../xr-quattro-esperienze-verification.md).
Matrice F0: [baseline e migrazione](../../qx-f0-matrice.md).
Analisi statica del commit `0e81660c03a00916b2963f784e81e6b100a33fd2`;
nessuna build, suite, installazione o sessione Inventor eseguita per questo piano.
Requisiti: [design](../specs/2026-10-25-quattro-esperienze-xr-design.md).
Architettura: [contesto Unity](../../UnityProjectContext.md).

## 1. Stato reale e conseguenze

| Area | Evidenza nel checkout | Conseguenza per il piano |
|---|---|---|
| Home | AppController.RefreshHome offre MR, Studio VR e Capture; EnterSession abilita il CAD e sincronizza il router | Separare attività da ambiente e connessione, conservando la sessione |
| Progettazione | Design/Assembly/LamieraWorkspace, preview/Applica, cronologia, modifica feature, editor nativo schizzi e parametri | Riutilizzare i percorsi; non riscrivere gli editor |
| Contesto M9 | ContextWorkspaceSwitcher apre authoring per tipo documento, ContextTabs compone le schede | Il contesto deve diventare subordinato all'attività, senza perdere navigazione M9 |
| Ispezione | InspectWorkspace riunisce misure, sezione, browser, proprietà, visibilità, analisi M7 | Separare servizi riusabili e composizione per attività; evitare copie divergenti |
| Presentazione | Non esiste come attività autonoma; molte operazioni di sola vista esistono | Prima consegna soprattutto di composizione e isolamento |
| Smontaggio/esploso | Nessun percorso dedicato trovato nel codice XR ispezionato | Nuova gestione delle pose per occorrenza e per sottoassieme |
| Movimenti | Backend sperimentale sample_parameter_motion, 2–100 campioni con rebuild e controlli, transazione abortita | Riutilizzabile per studi discreti; non fornisce pose per animare né scopre automaticamente i movimenti |
| Capture | RC0–RC4 software: acquisizione, upload, ricostruzione sparsa, fitting, export, registrazione e reference part | Riorganizzare le attività esistenti e chiudere i gate; MVS denso ancora da sviluppare |
| Salva CAD generale | DocumentActions mostra Salva, ma AppController.SaveDocument emette «Salva dal desktop» | Correggere l'etichetta o integrare davvero il salvataggio; RC4 ha un flusso proprio separato |
| Input/voce | Catalogo comune, InputMap con soli stati di interazione; VoiceTarget sceglie il primo workspace attivo | Filtrare per attività anche input diretto, callback, superfici vocali e comandi dinamici |

Fonti principali: `Assets/XrSo/Xr/{AppController,ContextWorkspaceSwitcher,InspectActions,DocumentActions,DesignActions,DesignParameters}.cs`,
`Core/Ui/{ActionCatalog,ContextTabs,SketchCommandCatalog}.cs`,
`bridge/src/{server/Tools/PlanningTools.cs,shared/Handlers/Experimental/AssemblyHandlers.cs}`.

### Evidenze da conservare senza sovrastimarle

- [M7](../../xr-m7-verification.md): analisi su fixture Inventor e runner sintetico presenti;
  prova fisica e assieme reale/performance restano distinti.
- [M9](../../xr-m9-verification.md): leggere anche gli aggiornamenti annidati successivi,
  non soltanto l'esito PARTIAL iniziale. Restano limiti specifici e prove fisiche.
- [S1 incremento95](../../s1-pc-command-status95.md): fotografia del 23 ottobre,
  77 comandi censiti, 54 implementazioni PC parziali e 23 assenti in quella baseline;
  non è lo stato definitivo dopo gli incrementi successivi né parità completa.
  La migrazione non deve dichiarare disponibili comandi censiti o nascosti.
- [Reality Capture](../../xr-reality-capture-verification.md): RC4 ha evidenze software;
  COM nativo, Quest, overlay reale e gate fisici RC1–RC3 ancora aperti.
  In Inventor si creano punti, work geometry e parametri, non mesh nativa o B-Rep.

## 2. Struttura dell'esperienza

Home con quattro schede grandi, descrizione di una riga, stato PC e documento.
MR/Studio VR diventano preferenze della vista nelle attività CAD; Capture richiede
passthrough. Associazione e connessione vivono in una pagina comune richiamabile
anche senza documento. Nessun refresh di rete deve sostituire la pagina attività.

Dentro un'attività: nome sempre leggibile, documento/revisione disponibili,
Home stabile e Torna distinto. Torna chiude prima comando/pannello/sottolivello;
Home cambia attività tramite la procedura di uscita. Non usare «Esci» per
significare sia Home sia disconnessione.

La tavolozza mostra gruppi di attività, poi strumenti e infine opzioni del
comando corrente. Le liste lunghe mantengono paginazione e icone esistenti.
Ogni controllo indisponibile per documento, connessione o tier spiega il motivo;
i comandi non implementati non occupano la normale tavolozza operativa.

## 3. Attività dettagliate

Legenda: **R** = riuso con adattamento; **N** = nuovo; **C** = completamento o
collaudo separato. Nessuna sigla significa automaticamente accettato sul Quest.

### Reality Capture

| Gruppo | Attività previste | Stato/perimetro |
|---|---|---|
| Scansioni | Nuovo oggetto, nuova area, riapri sessione, stato locale/PC | R |
| Preparazione | Permesso camera, volume, unità/riferimenti, avvio esplicito | R + C dispositivo |
| Acquisizione | Avvia, pausa/riprendi, termina, frame accettati/scartati e motivi, recupero interruzioni | R + C camera/tracking reali |
| Elaborazione | Trasferisci/riprendi upload, avvia ricostruzione PC, stato/annulla, elenco revisioni | R; rendere leggibile il progresso reale |
| Revisione geometrica | Visualizza punti/mesh disponibili, seleziona regione, fitting punto/asse/piano/cerchio/cilindro | R; nessuna segmentazione automatica implicita |
| Dimensioni e qualità | Quote note, incertezza esterna, misure stimate, residui, controlli indipendenti, nuovo solve | R + C banco; distinguere RMS e accuratezza |
| Inventor | Allinea tre punti o piano/asse/origine, crea reference part, anteprima/Applica, salva esplicitamente | R + C COM; non promettere un solido automatico |
| Confronto | CAD ridotto e overlay 1:1 con riferimenti fisici nuovi | R + C fisico; esplicitare sessione/tracking |
| Consegna | Export GLB/PLY/OBJ con report; Continua in Disegno sul riferimento creato | Export R; passaggio attività N |
| Evoluzione separata | Ricostruzione densa e successiva ricostruzione di superfici/solidi parametrici | N; non blocca la nuova Home |

Una scansione non può perdersi per Home o disconnessione: uscita guidata con
pausa/stop e conservazione raw; un'origine tracking cambiata non consente
una ripresa fittizia. Upload/solve mantengono stati diversi dall'acquisizione.

### Disegno e modifica

| Gruppo | Attività previste | Stato/perimetro |
|---|---|---|
| Documento | Scegli documento aperto, percorso, parte/sottoassieme, Torna; gestione modifiche | R M9; inizialmente indicare Salva dal desktop |
| Schizzo | Nuovo su piano/faccia; apri esistente; crea geometria e seleziona entità/sottoparti | R editor semplice e nativo; disponibilità per comando S1 |
| Geometrie di schizzo | Linee, cerchi, archi, rettangoli, asole, ellissi, spline e varianti supportate | R/C; catalogo nativo non equivale a tutte le varianti pronte |
| Quote e vincoli | Quote numeriche/espressioni, vincoli compatibili, visualizzazione, modifica/rimozione supportate | R/C S1; autoquota e opzioni residue non implicite |
| Modifica schizzo | Selezione multipla, sposta/copia/ruota/offset/serie/proiezioni secondo disponibilità effettiva | R/C S1; importazioni/testo/immagini solo per profili esposti |
| Feature parte | Estrusione, foro, raccordo, smusso; selezione faccia→feature, parametri e maniglie | R; altre feature backend sono estensioni future XR |
| Parametri | Modifica valori/espressioni, crea/rinomina/gestisci parametri utente, dipendenze e limiti | R/C; mantenere il pannello nativo già presente |
| Lamiera | Regola/spessore, faccia, flangia, taglio, sviluppo; schizzo e parametri pertinenti | R/C M5; distinguere crea sviluppo da mostra/stacca vista |
| Assieme | Componenti, DOF, sposta/ruota CAD ammesso, riferimenti, vincoli, giunti, ingresso in parte | R/C M4; operazioni persistenti con preview/Applica |
| Conferma | Anteprima valida, Applica fisico, annulla, cronologia XR, recupero esito incerto | R invarianti da preservare |
| Supporto | Misura, sezione, adatta/scala, MR/VR e calibrazione postazione | R; senza duplicare il menu Verifica completo |

Gli strumenti avanzati rimangono nei gruppi specifici. Non aggiungere al progetto
di navigazione il completamento dell'intera parità Inventor: è un lavoro S1
separato. Ogni attività mostra ciò che è realmente supportato dalla build attiva.

### Presentazione

| Gruppo | Attività previste | Stato/perimetro |
|---|---|---|
| Modello | Seleziona documento già aperto; nome e componente indicato | R con accesso limitato; niente ingresso in authoring |
| Posizionamento | Muovi/ruota intero modello, scala 1:1/tavolo/stanza, adatta, ricentra | R di sola vista; dimensioni misurate sempre in mm CAD |
| Misura | Due punti, fissa/rimuovi quote, cancella misura | R; indicare ≈ per tessellazione, nessuna precisione nativa inventata |
| Movimenti | Scegli movimento predisposto, slider con unità/limiti, valore corrente, ripristino | N; una coordinata indipendente alla volta nella prima versione |
| Ambiente | MR/Studio VR, legenda, Home | R; opzioni secondarie fuori dalla tavolozza minima |
| Evoluzione | Riproduci/pausa, velocità e viste salvate | Opzionale successivo; non necessario al primo rilascio |

Presentazione non espone sezionamento tecnico, smontaggio, modifica vincoli,
parametri o salvataggio CAD. La selezione indica un pezzo e serve alla misura.
Doppio Trigger non attiva il documento della parte; voce «apri» non diventa
una scorciatoia alla progettazione. L'eventuale cambio documento è esplicito.
Offline mantiene il modello già caricato e gli strumenti locali, con dati
non aggiornati dichiarati; cache asset non significa catalogo offline di progetti.

### Verifica meccanica

| Gruppo | Attività previste | Stato/perimetro |
|---|---|---|
| Esplora | Albero, parti/sottoassiemi, selezione, proprietà e riferimenti identificabili | R; navigazione locale senza aprire la parte in modifica |
| Rendi visibile | Nascondi/mostra, isola, X-Ray, sezione, ripristina visibilità | R M2/M7; stato solo XR |
| Smonta | Prendi un componente o sottoassieme, separalo, ruotalo, parcheggialo, rimetti il singolo/tutti | N locale; selezione per occorrenza, non per definizione condivisa |
| Esplodi | Ambito assieme/sottoassieme, slider 0–100%, direzione proposta e correggibile, ricomponi | N; prima versione geometrica, non ordine di montaggio certificato |
| Misura | Quote locali; distanza minima CAD tra due componenti nella configurazione originale | R; separare distanza visiva smontata e distanza CAD |
| Analizza | Interferenze globali/solo selezione, salute vincoli/giunti, componenti liberi e avvisi BOM supportati | R M7; risultato legato a revisione e ambito |
| Risultati | Elenco, selezione coppia, evidenziazione/focus, dettagli, invalidazione se cambia revisione | R; differenziare nessun problema, analisi parziale ed errore |
| Studio movimento | Scegli parametro e intervallo, campioni, controlli, prima criticità e risultati discreti | R backend + N UI/contratto; dopo il movimento dimostrativo |
| Evoluzione | Note di verifica e report esportabile legato a configurazione/revisione | N opzionale successivo |

La prima versione non è FEM né simulazione di forze o contatti. Non dichiara
un percorso di smontaggio praticabile soltanto perché il pezzo si lascia
trascinare nel visore. Analisi native e focus dei risultati richiedono prima
il ritorno alla configurazione CAD originale.

## 4. Architettura proposta

Separare quattro variabili: `Activity` (Home/Capture/Design/Presentation/Verification),
`DocumentContext` (Parte/Lamiera/Assieme), `ConnectionState` e `EnvironmentMode`.
I nomi sono proposti, non classi già presenti.

1. **ActivityController nel Core**: transizioni, attività richiesta dopo pairing,
   blocchi, consenso allo scarto della bozza e token/generazione dell'attività.
   AppController compone i servizi, senza diventare un secondo router concorrente.
2. **Politica azioni unica**: elenco esplicito di capacità ammesse per attività,
   documento e stato. Applicarla a Find/TryInvoke, menu contestuali, schede
   dinamiche, dispatcher/input fisico, voce e al confine del backend client.
   La limitazione è del client XR: non cambiare globalmente la configurazione
   del server condiviso da altri client.
3. **Servizi di ispezione condivisi**: estrarre gradualmente misure, sezione,
   visibilità e analisi da InspectWorkspace; nuovi provider Presentazione e
   Verifica li compongono senza clonare l'intero workspace. In Disegno resta
   un gruppo compatto di misura/sezione.
4. **Pose XR indipendenti**: snapshot CAD immutabile + trasformazione dell'intero
   modello + offset per occorrenza di smontaggio/esploso oppure movimento.
   Stati mutuamente esclusivi per evitare somma di pose incoerenti. Collider,
   highlight, misure locali e selezione seguono la posa visualizzata.
5. **Gerarchie**: CadSceneView istanzia foglie; costruire la mappa sottoassieme→
   occorrenze discendenti dal scene graph. Le occorrenze ripetute condividono
   geometria, mai lo stato di smontaggio.
6. **Lifecycle**: cambio attività/documento/revisione, pausa e tracking perso
   invalidano operazioni pendenti pertinenti. Risposte tardive non riaprono
   authoring o pannelli. Non riutilizzare ShowHome come cleanup indiscriminato
   finché non è verificato il trattamento di ogni bozza/commit/acquisizione.

### Confine dei movimenti

`sample_parameter_motion` modifica temporaneamente il CAD e poi abortisce:
richiede permesso di scrittura, anche se non conserva il risultato. Non va
invocato di nascosto da uno slider di Presentazione in sola lettura.

Predisposizione del movimento in Disegno (o dal desktop): identifica parametro,
unità, intervallo dichiarato dall'autore e componenti coinvolti; campiona pose
risolte da Inventor e verifica il ripristino. Serve un nuovo contratto con pose
per occurrence ID, revisione, limiti, campioni mancanti e supporto dei giunti.
La prima versione ammette assiemi rigidi a geometria invariata con un driver;
deformazioni/adattività e mesh variabile sono rifiutate esplicitamente.

Presentazione riproduce soltanto dati predisposti compatibili con la revisione.
Non invia un'operazione CAD a ogni frame. Interpolazione tra pose non certifica
la soluzione del meccanismo: i profili che richiedono interpolazione non validata
vanno limitati ai campioni o dichiarati dimostrativi. «Intervallo campionato
senza errori» non significa assenza di collisioni tra campioni. Lo studio
discreto di Verifica mantiene questo limite anche quando mostra intervalli.

## 5. Piano di consegna

Prefissi dei gate QX locali a questo piano, senza rinumerare M1–M10, S1 o RC.
Dimensioni relative indicative: M = cambiamento circoscritto, L = più sistemi,
XL = nuovo contratto/backend e collaudo nativo. Non sono stime in giorni.

| Fase | Lavoro e file principali | Dipendenze / risultato | Taglia |
|---|---|---|---|
| F0 — baseline | Matrice azione→attività, disponibilità reale e passaggi input; test e runner esistenti, verbali | Prima delle modifiche: baseline verificata e casi da migrare; non chiudere vecchi gate | M |
| F1 — attività e transizioni | Nuovi Core/Experience/Activity*, AppController, ContextWorkspaceSwitcher, HomePanel, UiText | Home quattro ingressi, pairing indipendente, router M9 apre authoring solo in Disegno; gestione bozze | L |
| F2 — isolamento e migrazione | ActionCatalog, ContextTabs, InputMap/Dispatcher/XrInput, DocumentActions, WorkspaceVoiceTarget, provider workspace | Azioni per attività, Home distinto da disconnessione, Disegno preservato e Salva veritiero | L |
| F3 — Presentazione base | Nuovo PresentationWorkspace/provider; estrazione minima servizi Inspect; ViewActions e misure | Vista e misure senza comandi CAD di modifica; utilizzabile prima dei movimenti | M |
| F4 — Verifica base | Nuovo VerificationWorkspace/provider; InspectActions/Verify, ComponentVisibility, scene browser | Albero, sezione, visibilità e analisi M7 in area dedicata | L |
| F5 — smontaggio/esploso | Nuovo Core/Inspection/PoseState e modello esploso; Runtime/Scene/OccurrencePose*, CadSceneView/CadInstance | Per-occurrence, sottoassiemi, slider, reset e coerenza collider/misure; dipende F4 | L |
| F6 — Capture guidata | RealityCaptureWorkspace e partial Capture/*, UI di fase e passaggio a Disegno | Riuso RC1–RC4, gate COM/Quest/fisici; indipendente da F5/F7 dopo F2 | L |
| F7 — movimenti | Contratto nuovo/esteso bridge, PlanningTools/AssemblyHandlers/ToolContracts, Core/Backend/Motion*, UI preparazione e playback | Pose native campionate e rollback provato, slider solo su profili supportati; dipende F3/F4 | XL |
| F8 — accettazione complessiva | Runner per attività, fixture, script e nuovo verbale QX | Scenari completi, performance, APK ordinario e prova fisica distinta | L |

Ordine consigliato: F0→F1→F2→F3→F4 = prima consegna della suddivisione;
F5 = seconda consegna, verifica esplorativa completa; F6 = percorso Capture
reso coerente e collaudato; F7 = movimenti; F8 consolida tutte le consegne.
Il collaudo pertinente accompagna ogni fase, non viene rimandato tutto a F8.
MVS denso, parità S1 completa, report e simulazione continua sono backlog separati.

### Criteri per fase

| Gate | Criterio osservabile | Prova richiesta |
|---|---|---|
| QX-01 | Home conserva quattro attività con/senza PC e documento; MR/VR non cambia attività | Core/EditMode + runner |
| QX-02 | Cambio desktop/riconnessione aggiorna il documento ma non apre authoring fuori Disegno | Core + runner su fixture Inventor |
| QX-03 | Bozza/preview/commit incerto/upload/acquisizione escono secondo stato, senza perdita silenziosa | Test lifecycle + runner; tracking fisico distinto |
| QX-04 | Presentazione non esegue scritture da palette, ring, voce, doppio Trigger, input diretto o callback tardiva | Backend spy/FakeAddIn + runner con audit e revisione nativa |
| QX-05 | Schizzo semplice/nativo, parametri, lamiera, assieme e navigazione M9 mantengono i casi esistenti | Suite core/EditMode + runner migrati per sottocasi |
| QX-06 | Sezione, X-Ray, isola e misura funzionano nelle attività assegnate anche sui dati locali offline | EditMode + runner + leggibilità fisica |
| QX-07 | Analisi M7 mantiene documento/revisione/ambito, focus corretto e invalidazione stale | FakeAddIn + Inventor reale + runner |
| QX-08 | Smontare un'occorrenza non muove le altre copie; sottoassieme muove tutte e sole le sue foglie | Core/EditMode + fixture annidata sul Quest |
| QX-09 | Esploso a 0/100%, smontaggio e reset riproducibili, collider e misure coerenti; CAD invariato | EditMode + runner/revisione CAD + controller fisico |
| QX-10 | Analisi su pose virtuali richiede ritorno CAD; nessun risultato riferito alla configurazione sbagliata | Core + runner |
| QX-11 | Capture→reference part→Disegno mantiene provenienza, risultato corretto e applicazione/salvataggio espliciti | Gate RC correlati + COM nativo/save-reopen + runner |
| QX-12 | Movimento predisposto conserva pose/parametro/revisione nativi dopo preparazione o errore | Test contratto + sonda COM con rollback, timeout e document switch |
| QX-13 | Slider Presentazione usa solo pose compatibili, unità/limiti visibili, reset; rifiuta profili non supportati | Core/EditMode + runner; niente scritture durante playback |
| QX-14 | Studio discreto mostra campioni falliti e non promette sicurezza tra campioni | Fixture native con criticità nota + runner |
| QX-15 | Budget dispositivo rispettato per i modelli scelti e UI leggibile nelle quattro attività | Benchmark Quest con manifest, conteggi e profiler; soglie definite in F0 |
| QX-16 | Utente distingue attività, Home/Torna e spostamento vista/smontaggio/CAD senza assistenza continua | Prova fisica guidata e verbale; non sostituibile dal runner |

## 6. Verifica operativa e rischi di migrazione

F0 registra l'esito delle suite canoniche e le fixture disponibili. Comandi:

```powershell
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
dotnet test bridge/tests/Bimwright.Ipt.Tests
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\qx-editmode.xml`""
```

Backend completo quando cambia il bridge; core/EditMode per fasi client.
Unity batch richiede Editor chiuso secondo README. Non cambiare di nascosto
processi aperti per raccogliere una baseline. Aggiungere fixture dedicata QX
con copie della stessa parte, due sottoassiemi, componenti soppressi/liberi,
interferenza nota e meccanismi semplice rotativo/lineare. Nessun documento
dell'utente viene usato come fixture.

I runner vecchi entrano oggi attraverso sessione/contesto: migrarli dichiarando
esplicitamente Disegno, Verifica o Presentazione; evitare che i test aggirino
la nuova Home. Input sintetici devono attraversare gli stessi controlli fisici.
Conservare manifest/log/screenshot e ripristinare documento e APK ordinario.
Distinguere sempre test unitari, runner Quest sintetico e prova fisica.

Rischi prioritari: un provider condiviso può esporre scritture da schede nascoste;
un refresh può riaprire authoring; un reset scena può perdere gli offset virtuali;
un componente annidato può essere confuso con la definizione condivisa; una
misura sulla mesh esplosa può essere scambiata per distanza CAD; un abort del
campionamento può lasciare esito incerto. Ognuno ha un gate sopra.

## 7. Decisioni proposte per avviare lo sviluppo

- Ingresso sempre Home; connessione automatica in background se già associato.
- Un solo modello/documento operativo alla volta, coerente con la sessione attuale.
- Presentazione essenziale e senza sezioni tecniche; movimenti da profili predisposti.
- Smontaggio/esploso solo XR, ripristinati quando si cambia attività/documento.
- Navigazione locale dei sottoassiemi in Verifica; attivazione/modifica in Disegno.
- Nessun blocco della riorganizzazione in attesa di MVS denso o parità S1 completa.
- Salvataggio generale inizialmente etichettato «Salva dal desktop»; implementazione
  nativa esplicita come task separato, senza confonderlo con Salva riferimento RC4.

Queste scelte rendono F1–F4 realizzabili senza inventare nuovi comandi Inventor.
Le specifiche esecutive di F5 e F7 devono fissare algoritmo esploso, contratto
pose e supporto dei meccanismi prima di modificarne il backend.
