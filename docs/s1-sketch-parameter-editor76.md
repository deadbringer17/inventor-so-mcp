# S1 — Parametri dentro lo schizzo76

Il pannello Parametri è ora condiviso dall'editor nativo usato da Parte e
Lamiera. Legge il contesto corrente, mostra tutti i parametri con la
paginazione esistente e conserva valori numerici, Boolean e Text distinti.
Consente di preparare espressione, creazione, rinomina ed eliminazione tramite
i builder67 e i comandi batch esistenti. Le unità iniziali sono richieste
nella creazione; le unità di un parametro esistente sono mostrate in sola
lettura. Non è stato inventato un setter Units.

Le operazioni entrano nella stessa NativeSketchSession di geometria,
Anteprima e Applica. La digitazione non produce preview o commit; Apply
richiede il piano realmente renderizzato dal workspace. Un checkpoint
conserva documento e revisione della bozza parametrica: dopo un cambiamento
esterno occorrono Annulla, nuova lettura e nuova preparazione. La conferma
generica della geometria non può ritargettare silenziosamente un nome.
Letture, selezioni e callback vengono revocate su cambio documento/revisione,
offline, refresh, chiusura o workspace non presentato.

Le prime modifiche sono state preparate in copie sotto
`artifacts/s1-verification/increment76/draft` mentre Unity costruiva la QA.
Sei test Core isolati su queste copie passano; il relativo harness emette
CS0028 perché include le classi Core nell'assembly di test. È un warning del
harness separato, non un risultato di build del prodotto.

Dopo la chiusura di Unity, root ha riletto e applicato dieci file. La suite
reale passa con **1.253 Core** e **1.119 Unity EditMode**, senza fallimenti o
test saltati. I binari realmente testati e le sorgenti prima/dopo sono
archiviati. I nuovi test UI non eseguono Preview o Commit nativi: verificano
valori tipizzati, input esplicito, vocabolario delle bozze e revoca dei
callback. I test Core usano un backend Fake e verificano separatamente il
gate di rendering e la transizione alla revisione successiva.

Questa è evidenza software. Non chiude il workflow nativo del pannello dentro
lo schizzo, la prova Quest in entrambi i contesti, le proprietà parametriche
restanti, il confronto desktop o la prova fisica. Tutti i 18 gate complessivi
rimangono aperti. La QA78 include il pannello, ma il runner74 esercita ancora
i parametri fuori dall'editor in Progettazione; non attribuirgli copertura
dei nuovi percorsi76.
