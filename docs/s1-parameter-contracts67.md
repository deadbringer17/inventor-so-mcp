# S1 — Contratti dei parametri, incremento 67

14 ottobre 2026. **18 gate S1 OPEN**; comando Parametri completo ancora aperto.

Il mapper reale dei parametri conserva ora valori booleani e testuali con
`value_bool`/`value_text`. Mantiene `evaluated:null` precedente per questi
tipi, i metadati e tutti i campi/conversioni numerici esistenti. Valori
assenti, non supportati, non finiti o getter falliti rimangono indisponibili;
eccezioni e relativi contenuti non vengono serializzati.

Il Core offre builder limitati per i comandi già esistenti set/create/
rename/delete_parameter. Conserva le espressioni native senza valutarle,
verifica budget e Unicode e restituisce payload indipendenti. Documento,
revisione e flusso Anteprima → Applica rimangono responsabilità di DesignSession.
Non introduce una nuova sessione o un backend alternativo.

## Evidenze

- **1.190 Core PASS**, inclusi protocollo delle operazioni, espressioni
  opache anche non numeriche, budget, controlli Unicode e proprietà dei payload.
- **1.661 bridge PASS**; i test includono direttamente il mapper reale
  compilato senza dipendenza Inventor. Add-in experimental zero warning/errori.
- `text67-native-r2.json`: **Parte/Lamiera PASS**, quattro osservazioni
  dopo due cambi di sorgenti booleano/testo in ciascun contesto. Get e list
  HTTPS reali restituiscono valori tipizzati identici all'API Inventor,
  conservano evaluated:null e non cambiano revisione/Dirty durante la lettura.
  Testo esercitato include Unicode, virgolette e markup letterale.

La sonda crea/aggiorna direttamente i parametri nativi nella fixture
posseduta: non è una prova dei builder attraverso preview/commit, né di
consumatori dipendenti o UI. Wrapper e report usano namespace67; il report
si chiama text67 per riuso del contenitore di prova. Assembly e sorgenti
effettivi sono conservati in `increment67/inprocess-tested-r2/`.
La build della sonda conserva un warning NuGet preesistente; non equivale
a un warning dell'add-in. Primo warning nullable corretto prima del run.

Processo di prova terminato, manifest/DLL originali e processo utente
preservati. Nessuna APK o prova Quest. **NOT COVERED:** UI Parametri completa,
elenco associato alla revisione, consumer reali con due aggiornamenti,
preview/commit/rollback dei nuovi percorsi UI, rifiuto eliminazione con
dipendenti, tutte le unità/opzioni Inventor, oracle desktop e prove fisiche.
