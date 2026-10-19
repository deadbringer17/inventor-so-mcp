# S1 — fondazione produttiva Copia di cerchi e archi

Il percorso sperimentale non pubblicato transform_copy accetta ora anche
riferimenti tipizzati a cerchi e archi interi. L'intera selezione viene
validata prima del primo creator: documento/sketch/revisione, transazione
posseduta, punti nativi non condivisi, formato e grafo intrinseco.
Il percorso usa una curva alla volta in MoveSketchObjects con Copy=true;
il testo continua a usare AddFitted e non entra in quella API.

Il profilo iniziale conserva il formato default verificato92: layer e
override colore null, nessun attributo/blocco, kDefaultLineType,
kScreenSpace, peso0 e scala1. Concentric/centro e Coincident/estremi devono
collegare la curva ai suoi punti reali, mediante identità COM e chiavi;
relazioni esterne o eliminabili vengono rifiutate. Curve e punti derivati
da componenti di riferimento sono esclusi dal profilo iniziale.

Le copie devono avere chiavi e punti distinti dagli originali e dalle copie
precedenti, mantenere raggio, orientamento, locus e grafo mappato alle nuove
chiavi. Construction/HoleCenter e gli altri flag dei punti sono conservati.
Originali e stili vengono ricontrollati. Il controllo del contesto completo
si applica alle selezioni con cerchio/arco; le richieste di sole linee/testo
mantengono il percorso88. Contesti non catturabili, immagini e quote vengono
rifiutati prima della creazione. Le letture del grafo rispettano la deadline;
le spline non supportate vengono rifiutate senza enumerarne tutti i punti.

## Verifiche

- 96 test mirati delle policy PASS.
- 1.776 test bridge PASS, zero fallimenti e zero skip riportati; rimane il
 warning di discovery per un ID xUnit duplicato già presente.
- 44 controlli di sorgente/registrazione sperimentale PASS.
- Build add-in2027 ordinaria e sperimentale: zero errori/warning.
- Compatibilità net48 compilata: zero errori, un warning preesistente
 in SendCodeHandler.

Il candidato sperimentale congelato in increment93/producer-r1 ha SHA256
31680B0110529BBA8E20B5ECF9023E286D2E3385943509C459634C8C9FA7A8D8.
Non è installato. Le prove native92 verificano le primitive dell'API,
non questo nuovo handler. Il collaudo attraverso Core/HTTPS94 ha ottenuto
PASS client e oracle nativo nel run r2, con18workflow/28coppie/10rifiuti.
Verifica offline dei74checkpoint e otto alterazioni semantiche PASS.
Il run r1 fallito resta conservato e non viene considerato risolto.
[Rapporto94](s1-copy-core-curves94.md).

## Limiti

Nessuna nuova capability o azione UI viene pubblicata. Altri formati,
curve, vincoli, rimozione vincoli, testo parametrico/rich/ruotato/multilinea,
profili consumati, aggiornamenti e persistenza generali, desktop e Quest
restano obblighi della specifica completa. Tutti i18 gate complessivi S1
rimangono aperti.
