# S1 — Editor Parametri in Progettazione, incremento 68

14 ottobre 2026. **18 gate S1 OPEN**; comando74 complessivo ancora incompleto.

La scheda Parametri di Progettazione offre espressioni native, creazione
di parametri utente, rinomina ed eliminazione. Riusa il DesignContext
associato alla revisione, i builder67 e la DesignSession già esistente;
i nuovi flussi preparano una singola bozza e richiedono Anteprima → Applica.
Non valuta espressioni nel client né esegue una preview implicitamente.

L'elenco conserva valori booleani, testuali, numerici e indisponibili,
senza trasformarli in zero. L'input numerico precedente continua a essere
disponibile solo per valori numerici supportati; conserva il suo flusso
preesistente. Nomi, espressioni e unità rimangono stringhe native con
budget/Unicode controllati. Un'espressione multilinea non viene appiattita
silenziosamente: il campo a riga singola richiede una sostituzione esplicita.
Callback e selezioni sono revocati con refresh, cambio revisione/documento,
offline, Back, chiusura e distruzione. Il percorso voce numerico non cattura
il campo testuale aperto.

## Evidenze

**1.104/1.104 Unity EditMode PASS**, inclusi venti nuovi casi su tipo dei
valori, paging, espressioni, CRUD, conferme, cancellazione e invalidazione.
Backend fittizio: queste prove non certificano l'esecuzione nativa o Quest.
I workflow nativi dei builder e dei consumatori sono separati in 69.

La prima regressione aveva 1.103 PASS/1 FAIL: azioni di gestione facevano
riferimento a una scheda non dichiarata prima della selezione. Corretto
il catalogo dichiarando sempre la scheda nascosta; il test strutturale
originale rimane invariato. Sorgenti, DLL e log r1 conservati; sorgenti e
assembly del full r2 archiviati separatamente in `increment68/`.

**NOT COVERED:** Parametri dentro l'editor schizzo, UI equivalente Lamiera,
selettore di unità/funzioni/espressioni completo, tutte le opzioni/default
desktop, conservazione/edit multilinea, oracle desktop, performance,
runner Quest e prove fisiche. I vincoli EditableNow restano attivi durante
lo schizzo. Nessuna APK nuova creata o installata.
