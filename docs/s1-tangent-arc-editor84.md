# S1 — Bozza e riferimenti Arco tangente84

15 ottobre 2026. Regole Core integrate; UI solo in bozza separata, non
abilitata nel prodotto. Tutti i 18 gate S1 restano aperti.

La sessione nativa richiede per `arc_tangent` un riferimento Start/End
attuale della curva sorgente. La validazione verifica i sei campi tipizzati,
documento/revisione/sketch, tipo effettivo e sottoparte disponibile nello
snapshot completo. Il contratto wire contiene un solo endpoint annidato
`points_mm:[[x,y]]`, con coordinate planari finite.

La destinazione opzionale è un riferimento nativo distinto: le coordinate
derivano dal suo punto nello snapshot, senza usare l'approssimazione del
puntatore. Una bozza con riferimenti scaduti non può essere confermata.
La riconferma esplicita richiede nuove selezioni e ricongela il punto della
destinazione agganciata; una destinazione libera conserva le coordinate
inserite. Calcolo di tangente, raggio e solver restano nel backend nativo83.

La suite Core aggiornata supera **1.275 test**, zero fallimenti e zero skip,
inclusi22 nuovi casi. Il primo tentativo mirato conserva un fallimento del
test stesso: costruiva un CadPoint non finito prima dell'asserzione, mentre
il costruttore già rifiuta quel valore. La correzione verifica l'ingresso
wire non finito all'interno dell'asserzione e conserva i rifiuti per Z non
planare e lunghezza nulla. Il test sul JSON serializzato verifica
indipendentemente la forma esatta dell'array1×2; non basta un roundtrip
fra builder e validatore.

Evidenze: `artifacts/s1-verification/increment84/core-targeted-r1` (FAIL
conservato), `core-full-r2` (PASS) e `integrated-core-r1` (sorgenti/hash).
Questa prova software non certifica UI, Quest o parità desktop.

Il partial UI e i sette casi EditMode proposti restano sotto
`increment84/draft`, separati dai sorgenti reali. L'integrazione del comando
attende le prove native sulle famiglie aggiuntive e sui target agganciati.
Nessuna APK è stata compilata o installata per84.
