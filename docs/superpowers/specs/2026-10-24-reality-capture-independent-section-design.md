# Reality Capture — sezione indipendente (RC0)

Data: 24 ottobre 2026.

Riferimento funzionale: `Inventor XR SO/REALITY_CAPTURE_SPEC.md`.
Questa integrazione precisa la navigazione della specifica, senza dichiarare implementata la pipeline di acquisizione.

## Comportamento richiesto

Nella Home tecnica, insieme a **Entra · Realtà mista**, **Entra · Studio virtuale** e **Dimentica PC**, compare **Reality Capture**. Il pulsante è disponibile anche senza pairing, senza connessione e senza documento Inventor.

MR e VR restano modalità ambientali del workspace CAD. Reality Capture è una sezione applicativa autonoma, con UI propria e passthrough attivo. Non aggiunge una scheda alla tavolozza di progettazione.

Entrando nella sezione:

- chiudere gli strumenti CAD, bozze, maniglie, tastierini e contesto fantasma con i percorsi già usati dalla Home;
- nascondere modello, tavolozza, barra di conferma, HUD e legenda CAD;
- sospendere l'input semantico CAD, la selezione e la voce Inventor, inclusi i pannelli vocali che hanno una radice separata;
- conservare il puntatore UI per interagire con il pannello autonomo;
- mantenere l'eventuale collegamento al PC per il futuro trasporto dati, senza dipendere dal suo stato per la navigazione;
- impedire che eventi di documento, refresh del catalogo o richiesta di nuovo pairing riaprano l'interfaccia CAD.

**Torna alla Home** e il pulsante menu riportano alla schermata di ingresso. Da lì MR o VR riattivano la UI CAD e il contesto del documento corrente. La sezione non modifica o salva documenti Inventor.

## Perimetro RC0

RC0 introduce il contenitore e le pagine informative **Oggetti e aree** e **Riferimenti fisici**. L'interfaccia dichiara che acquisizione e ricostruzione sono in sviluppo. Non mostra copertura, precisione, mesh o risultati simulati e non registra immagini. L'accesso alla fotocamera per il pairing viene fermato entrando nella sezione.

Il punto di composizione resta `AppController`; `RealityCaptureWorkspace` possiede un pannello separato e non dipende da `InventorBackend` o da `ActionCatalog`. `UiShell.SetVisible` applica una soppressione persistente alle radici CAD anche quando cambiano i comandi o la barra di conferma.

Nessuna modifica manuale a `Main.unity`, nessun nuovo pacchetto, nessuna migrazione di dati o modifica al backend.

## Gate

| Gate | Criterio |
|---|---|
| RC0-01 | Ingresso dalla Home con e senza PC/documento |
| RC0-02 | Passthrough attivo, scena e tutti i pannelli CAD nascosti |
| RC0-03 | Input, selezione e voce CAD sospesi; puntatore UI disponibile |
| RC0-04 | Refresh del catalogo/documento/connessione non sovrappone il CAD |
| RC0-05 | Ritorno alla Home e successivo ingresso MR/VR, anche ripetuto |
| RC0-06 | Nessuna acquisizione o accuratezza dichiarata prima dell'implementazione |

I test EditMode verificano la composizione UI senza credenziali e senza rete. Il runner Quest riusa una fixture CAD dedicata per la regressione di ingresso/uscita e registra input sintetico. Ergonomia, passthrough reale, tracking e controller fisici restano una prova distinta.

## Sviluppo successivo

Il [piano di implementazione](../plans/2026-10-24-reality-capture.md) separa RC0 dal primo MVP realmente capace di acquisire e ricostruire. Gli stati `UNSCALED`, `METRIC`, `CALIBRATED`, `VALIDATED` mantengono le definizioni della specifica principale.
