# S1 — Arco tangente sperimentale83

15 ottobre 2026. Backend sperimentale implementato; matrice iniziale
integrata PC superata, collaudo completo in corso.
Il comando XR rimane indisponibile. I 77 comandi e tutti i 18 gate complessivi
S1 restano nel perimetro e aperti.

`edit_sketch/arc_tangent` conserva un riferimento nativo Start/End della curva
sorgente e, facoltativamente, un secondo punto nativo di destinazione.
`points_mm` contiene un solo endpoint finale. L'evaluator nativo determina
la tangente uscente: il confronto con i due estremi del dominio evita di
presumere l'orientamento della curva. La costruzione geometrica iniziale
aggiunge un arco con SketchPoint condiviso e un TangentSketchConstraint,
con prossimità esplicita all'estremo selezionato. Il solver e la transazione
rimangono nel percorso atomico esistente. Coordinate non finite, tangente
nulla, corde degeneri e curve con estremi ambigui vengono rifiutati.

Il calcolo puro usa normalizzazione scalata per evitare overflow evitabili.
I 19 test matematici passano; la suite bridge aggiornata passa con 1.679 test
e uno skip preesistente. La build sperimentale passa senza errori o warning.
I sorgenti e il DLL candidato sono conservati in
`artifacts/s1-verification/increment83/producer-r1`; SHA256 del DLL:
`AAE71E29B0131BE3D8D91CD6C124FBA50D9C1B2F724EAB4BF10FE8BA9F796797`.

## Prove native e diagnosi conservata

La sonda usa Core reale, HTTPS con certificato fissato, bootstrap privato e
un nuovo processo Inventor posseduto con sole fixture dedicate Parte/Lamiera.
La conferma di rendering PC è sintetica. La prima richiesta dopo Applica
legge lo snapshot, prima di qualsiasi oracle COM. Ogni preview verifica il
ripristino dello stato rappresentato e della revisione evento.

`native-r1` e `native-r2` conservano un caso nativo riuscito e sei rifiuti;
entrambi si fermano sul test del secondo passo. La diagnosi strutturata r2
mostra `ROLLED_BACK`, `step_code=STALE_REVISION`, `step_index=1`: il primo
arco valido ha già avanzato l'epoch e il secondo passo riutilizzava il
riferimento iniziale. Non è un PASS del test di argomento invalido.
Entrambi i processi posseduti terminano normalmente, ripristinando manifest
e DLL installato e preservando i processi preesistenti.

Nel tentativo r3 il secondo passo è un `arc_center` con raggio zero, senza
riferimenti. Questo esercita il rollback dell'arco tangente già creato senza
aggirare il controllo di revisione. Restano obbligatori `INVALID_ARGUMENT`
e indice1; le asserzioni non accettano genericamente qualunque errore API.

La matrice prevista contiene 16 casi: Parte/Lamiera × linea/arco sorgente ×
Start/End × due lati. Controlla identità e geometria sorgente conservate,
centro/raggio/locus analitici indipendenti e binding completo del vincolo.
Il verificatore `artifacts/s1-verification/verify-tangent83.ps1` confronta
anche le chiavi native finali, il punto realmente condiviso, i vincoli e
la geometria letta indipendentemente dal nuovo host83. Non interpreta un
timeout o un report assente come prova riuscita.

`native-r3` termina con **16 casi e 14 rifiuti PASS**. Il verificatore
`increment83/verification-r1.json` conferma tutti i16 casi contro le chiavi
native case-sensitive (Base64), geometria finale, endpoint realmente
condiviso e vincoli TangentSketchConstraint. Il processo posseduto108020
termina normalmente; processo preesistente91056 con identità di avvio
invariata, manifest e DLL ordinario preservati. Le due fixture salvate
restano identiche alla baseline su disco; non è una prova di persistenza
delle modifiche, che sono intenzionalmente scartate durante il cleanup.

## Residui

La matrice iniziale non certifica tutte le famiglie accettate dal resolver:
arco ellittico, spline interpolante e spline CV richiedono casi aggiuntivi.
Restano da provare target nativo opzionale, due aggiornamenti delle
dipendenze, SaveCopy/reopen, rollback completo nativo per operazione,
inferenze/default desktop, UI Quest, input fisico, prestazioni e 3D.
L'ispezione finale read-only di DatabaseRevisionId/Dirty non sostituisce
il controllo completo del rollback di ciascuna operazione.

## Supplemento nativo in corso

Il nuovo host `S1TangentSupplementOwnedHost83` conserva `S1_Create` vuoto
e prepara sorgenti distinte nello schizzo `S1_TangentSources`. Il client
separato prevede52 casi Parte/Lamiera: arco ellittico, spline interpolante
e spline CV, entrambi gli estremi/lati, target libero o punto nativo;
inoltre archi complementari270° con entrambi gli estremi condivisi.
I quattro rifiuti previsti riguardano coordinate e tipo del target nativo.

`supplement-native-r1` supera otto casi ellittici e due rifiuti, poi fallisce
leggendo i pesi spline: l'API restituisce un array nullo per curve non
razionali e `JArray(null)` lo aveva rappresentato come `[null]`. Il nuovo
host serializza questa assenza come array vuoto, senza inventare pesi.
`supplement-native-r2` supera nuovamente gli otto casi ellittici e due
rifiuti, poi fallisce al confronto del locus sorgente della prima spline.
Non è un PASS delle spline o dell'intera matrice. Entrambi i cleanup e
i ripristini dell'installazione terminano normalmente.

Il tentativo r3 conserva, prima dell'asserzione, sorgente prima/dopo,
conteggi e distanza fra campioni. Il wrapper acquisisce l'ispezione nativa
anche dopo un fallimento del client, senza cambiarne l'esito. Questo
permette di confrontare poli/nodi/pesi contro la definizione nativa della
fixture e distinguere mutazione reale e discretizzazione. Il verificatore
`verify-tangent83-supplement.ps1` richiede tutti52 casi e4 rifiuti, chiavi
case-sensitive degli estremi e identità dei target condivisi; non può
approvare i tentativi incompleti r1/r2.

`supplement-native-r3` conferma una modifica reale della prima spline
interpolante:257 campioni prima e dopo, distanza massima1,04448mm. La
definizione indipendente passa da quattro a cinque poli e cambia nodi/ordine,
pur conservando gli estremi. Il risultato resta FAIL; non è una differenza
di sola tessellazione. Ispezione nativa diagnostica e cleanup sono conservati.

Il candidato `producer-order-r1`, provato in r4, inverte EntityOne/EntityTwo
nel solo AddTangent, mantenendo endpoint condivisi e proximity esplicita.
La deformazione rimane identica: l'ipotesi è confutata. Le prove16/14
precedenti si riferiscono al DLL `producer-r1`. Nessuna UI viene esposta
mentre si indaga la conservazione della sorgente.

L'[indagine85](s1-tangent-spline-investigation85.md) conserva i tentativi
successivi con maniglia nativa esplicita: r5 rifiuta la preview e r6 aggiunge
diagnostica strutturata. Questi candidati non sono una correzione verificata.
