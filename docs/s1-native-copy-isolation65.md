# S1 — Isolamento della copia nativa, incremento 65

14 ottobre 2026. **18 gate S1 OPEN**; copia produttiva ancora disabilitata.

La prima prova storica di copia della selezione mista terminava Inventor;
l'errore osservato durante Abort non identificava la chiamata che aveva
causato la perdita del processo. La nuova sonda esegue un solo caso in un
processo Inventor nuovo, con journal JSONL persistito prima/dopo API di
copia, enumerazione del risultato, Solve, Update e Abort.

**Parte e Lamiera PASS**, ciascuna in un processo distinto: una linea libera
(0,0)→(20,10) mm copiata di (12,-8) mm. La copia ha identità e punti nativi
distinti. Due traslazioni successive della copia (+3,+4), (-2,+5) mm
conservano la sua identità e lasciano invariati originale e relazioni
native originali. L'oracle confronta gli endpoint con valori analitici.
Questi aggiornamenti provano editabilità della copia, non associatività
parametrica o propagazione da una sorgente.

Entrambi gli Abort ripristinano esattamente il baseline catturato di
identità, geometria, parametri, conteggi e relazioni; questa cattura non
certifica l'uguaglianza DatabaseRevisionId/Dirty. Processi dedicati usciti,
manifest/DLL/interop installati e identità dei processi precedenti invariati.
La sonda non acquisisce un riferimento COM ai documenti del processo utente,
quindi non dichiara un readback dei loro contenuti.

Evidenze: `increment65/r1-line-part/` e `r2-line-sheet/`, con sorgenti,
assembly realmente eseguiti, journal, report, baseline e hash del wrapper.
La prima build falliva per FileOptions ambiguo fra Inventor e System.IO;
corretto qualificando System.IO. Sorgenti/log iniziali conservati. Build
finale zero warning/errori. Nessuna distribuzione di DLL o APK.

**NOT COVERED:** selezione mista originale, copia di cerchi/archi/ellissi/
spline/testo/immagini, varianti vincolate o RemoveConstraints, rotazione
con copia, relazioni driving e dipendenze, preview/applica nel prodotto,
oracle desktop, Quest e prove fisiche. I due casi non autorizzano ancora
la rimozione del blocco generale della copia.
