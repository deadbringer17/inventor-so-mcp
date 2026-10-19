# S1 — fondazione sperimentale Copia, incremento88

Il comando39 resta incompleto e non pubblicato nel catalogo dei variant.
`transform_copy` viene gestito dal solo percorso sperimentale `edit_sketch`.
Sono implementate la traslazione di linee libere e la copia di testo fitted,
letterale, su una riga e senza rotazione, in Parte e Lamiera. La selezione
intera viene controllata prima del primo creator; tutte le modifiche usano
la transazione atomica del batch chiamante.

Per le linee si usa `MoveSketchObjects` con una sola linea per chiamata.
Per i testi si usa `AddFitted`, conservando stile locale, proprietà e colore.
Il percorso nativo Copy=true sui TextBox, già responsabile del crash70,
non viene invocato. Le copie devono avere chiavi e punti distinti; gli
originali e i loro stili vengono confrontati prima e dopo la creazione.

## Verifiche PC

- Policy pura iniziale: **71 test mirati PASS**, poi75 con guardie finite.
- Suite bridge aggiornata: **1756 PASS**, nessun test saltato; include anche
  il rifiuto dei testi con sfondo, prima del primo creator.
  Il runner conserva l'avviso preesistente di ID duplicato in
  `CadEventJournalRevisionTests`; il contatore TRX riporta0 skipped.
- Build add-in sperimentale e ordinario: **0 errori, 0 warning**.
- DLL sperimentale congelato in `increment88/producer-r1`:
  `72C4A8F1EEC5357CE97C67FBF8753E3DCAD500AC5F1BA2E76644669CBA41C156`.
- DLL successivo p2, guardie finite:
  `C431D31E877B61897AD900CBEA1B9DED542F4BD31A74F616A100F9A8EC9BE448`.
- DLL p3, sfondo rifiutato e proprietà confrontata:
  `29BC225DDCB0609071B8C9A28D6D33EF66A79BEB95D39C1B7DFECE1875BC279A`.
  Ogni candidato ha un proprio snapshot dei sorgenti e manifest di evidenza.

Le sonde dedicate usano il Core reale tramite HTTPS con pinning, preview,
conferma sintetica PC, Apply e prima lettura immediata dello schizzo.
Checkpoint COM indipendenti confrontano chiavi, geometria, glifi, stili,
parametri, Dirty e DatabaseRevisionId prima e dopo ogni rifiuto.

`native-r1` e `native-r2` falliscono nella preparazione della fixture, prima
di esercitare il comando. Sono conservati: sono state corrette nella sola
sonda la firma dello stile e l'inizializzazione degli array GetFacetsInfo.

`native-r3` resta **FAILED**. Esegue tre workflow Parte (linea, testo e
selezione mista), con originali invariati e glifi traslati nella prima
lettura, e cinque rifiuti con checkpoint nativo esatto. Il quinto rifiuto
ottiene ROLLED_BACK / INVALID_ARGUMENT allo step1 dopo una copia valida.
L'inizializzazione successiva della Lamiera fallisce con STALE_REVISION,
mentre il log conservava ancora il nome dell'ultimo rifiuto Parte. Nessun
risultato Lamiera o matrice completa viene dedotto da questo tentativo. Worker e
Inventor dedicati terminano normalmente; manifest originale ripristinato
e DLL installato invariato. `native-r4` ripete la prova con diagnostica
più precisa dei token, senza cambiare il DLL candidato: conferma cinque
rifiuti Parte esatti e il cambio di revisione alla prima lettura Lamiera.
Anche questo tentativo rimane FAILED, con cleanup normale e installazione
ripristinata. `native-r5` corregge il nome della fase e consente fino a tre
rinnovi del token esclusivamente durante l'inizializzazione del contesto;
nessun retry è ammesso nella prima lettura dopo Apply o dopo un rifiuto.
R5 conferma che lo STALE precede lo snapshot, nel GetDesignContext Lamiera;
resta FAILED. La gestione limitata dell'inizializzazione viene quindi
applicata anche a GetDesignContext, come nella sonda83. R6 verifica il DLL
p2: **PASS del sottoinsieme PC** con sei workflow Parte/Lamiera (linea,
testo, misto), otto coppie originali/copiate verificate via COM, dieci
rifiuti e venti checkpoint nativi esatti. Il GetDesignContext iniziale
Lamiera richiede un solo rinnovo di revisione, nessun retry degli snapshot
dopo Apply o rollback. Worker e Inventor terminano normalmente e il manifest
e il DLL installati sono ripristinati. Il verificatore degli artefatti
`verify-copy88-evidence.ps1` conferma matrice, binding, chiavi e attestazioni
di cleanup in `native-r6/verification-r1.json`.

`native-r7` passa separatamente la stessa matrice completa sul DLL p3, con
il controllo indipendente di finitezza anche nell'oracle COM. Verificatore
degli artefatti PASS, worker/Inventor terminati normalmente e installazione
ripristinata. Questo è il candidato corrente verificato per il sottoinsieme.

## Lavoro obbligatorio aperto

La matrice88 verifica il rollback e il confronto COM nel sottoinsieme
dichiarato. Restano inoltre testi
con campi parametro, rich text, rotazioni e più righe, altri tipi di
geometria, profili vincolati, opzioni di rimozione vincoli, due aggiornamenti
delle sorgenti, persistenza/riapertura, confronto desktop, interfaccia e
Quest. Le capacità escluse sono rifiutate esplicitamente, non simulate.

La conferma PC è sintetica; non prova rendering o input Unity, Quest o
controller fisici. **Tutti i18 gate generali S1 restano aperti.**
