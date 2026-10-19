# S1 — Copia di cerchi e archi attraverso Core e Inventor

Il collaudo94 usa il candidato sperimentale93 congelato, una nuova istanza
Inventor posseduta e HTTPS con certificato fissato. La fixture attesta con
chiavi native linea, testo letterale, campo parametro non copiabile,
cerchio e quattro archi: quarto di giro, attraversamento dell'origine
angolare, arco maggiore e costruzione in senso orario. Parte e Lamiera
hanno documenti distinti; nessuna selezione viene ricavata per vicinanza.

La matrice prevede18 workflow e28 coppie: oggetti singoli e selezioni miste.
Ogni workflow passa per anteprima, Annulla, nuova anteprima, conferma PC
sintetica e Applica. Dopo LastCommit il client esegue GetDocumentState e
una sola GetNativeSketch; salva la prima risposta prima degli assert,
senza letture native diagnostiche o retry intermedi.

Dieci rifiuti coprono revisione scaduta, riferimento estraneo, campo
parametro non supportato, selezione mista non copiabile e fallimento al
secondo passo dopo copie valide. Il riferimento estraneo mantiene identità
e documento reali, ma usa esplicitamente la revisione del documento attivo
per esercitare il controllo di ownership senza arrestarsi al controllo di
revisione. Questa alterazione è esclusivamente un caso negativo registrato.

I74 checkpoint raw verificano annullamenti, rollback e conservazione degli
oggetti precedenti a ogni commit. L'oracle nativo indipendente controlla
chiavi e punti distinti, traslazione, raggio, angoli, evaluator, flag,
grafo intrinseco mappato, glifi, stili e parametri. I confronti dello stato
immutato usano serializzazione esatta; soltanto source→copy usa tolleranze
esplicite: coordinate1e-5mm, raggio1e-8mm, angoli/domain1e-10.

## Stato

Host94 e client94 compilati senza errori. L'host mantiene il warning
NU1903 già presente nella dipendenza SQLite; il client ha zero warning.
Il run nativo r1 è FAILED: dieci workflow completati, cinque rifiuti di
Parte verificati e41 checkpoint conservati. L'anteprima dell'arco quarter
in Lamiera ha riportato lo stato Error; il report non aveva conservato
l'eccezione consumata da DesignSession. Il motivo del fallimento resta
non determinato. Nessun PASS complessivo del collaudo94 è dichiarato.

Cleanup r1 normale: documenti posseduti chiusi, Quit, PID992 assente al
successivo controllo OS, manifest ripristinato e DLL ordinaria invariata.
Il19 ottobre è avviato r2 sullo stesso candidato93. Il solo client di
prova registra ora l'errore strutturato al confine di DesignSession, senza
retry, letture CAD aggiuntive o variazione dei criteri. Build client r2:
zero errori e warning. R2 ha completato18workflow,28coppie e10rifiuti,
con74checkpoint: PASS_PC_CURVE_COPY_SUBSET e PASS_NATIVE_CURVE_COPY_SUBSET.
Il wrapper ha terminato con codice0; documenti chiusi, Quit, PID78440
assente nel controllo OS successivo, manifest/DLL ordinari ripristinati.
Il motivo dell'arresto r1 non è stato riprodotto né risolto e resta aperto.

La verifica offline sul corpus completo ha evidenziato due problemi del
verificatore: l'accesso a una proprietà di array PowerShell vuoto produceva
un elemento nullo nei casi di solo testo; il self-test dell'ordine dei
checkpoint veniva respinto correttamente, ma con un messaggio che univa
ordine/binding/SHA. Le correzioni enumerano soltanto coppie reali e
separano le diagnosi. Nessun artefatto nativo o criterio è stato modificato;
le versioni precedenti e i fallimenti sono conservati.

Verifica offline conclusa con codice0:
`native-r2/verification-copy94-r1.json`, stato
VERIFIED_PC_CORE_NATIVE_COPY94_SUBSET. Ricalcolati18workflow,28coppie,
10rifiuti e74checkpoint; zero proprietà opzionali UNKNOWN nella fixture.
Otto alterazioni rifiutate: raggio copia, riuso punto originale, parametro
estraneo, stato dirty dopo Abort, prima snapshot, ordine checkpoint,
identità del riferimento estraneo e membro di vincolo collegato all'originale.
Per le alterazioni raw sono ricalcolati SHA e trace: il rifiuto è semantico,
non dovuto soltanto all'integrità del file. Questo certifica la coerenza del
corpus salvato; la prova nativa è quella separata dell'host94.

Gli artefatti sono in artifacts/s1-verification/increment94; il wrapper
archivia sorgenti e assembly effettivi, catalogo, report e stati raw,
ripristina il manifest e verifica che la DLL ordinaria sia invariata.

## Limiti

Aggiornamenti delle sorgenti e riaperture di cerchi/archi restano da
verificare. Gli altri profili Copia, testo parametrico/rich, vincoli,
opzioni complete, UI, Quest e prove fisiche non sono coperti. Il candidato
non è pubblicato come capability e tutti i18 gate complessivi restano aperti.
