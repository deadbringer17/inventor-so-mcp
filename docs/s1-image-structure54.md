# S1 — Geometria strutturale delle immagini, incremento 54

14 ottobre 2026. F03; **18 gate aperti**.

`image_current` descrive il frame UV corrente e il recupero differito del
bitmap, separatamente dal sorgente originale gestito. Il reader identifica
per identità COM esatta i bordi e gli angoli delle immagini, conservando
geometria e associazioni dei vincoli. La selezionabilità è esplicita: questi
oggetti non vengono trattati come normali linee o punti modificabili.
Anche gli angoli condivisi con curve ordinarie sono protetti. Le guardie
backend verificano i riferimenti costruiti manualmente; una classificazione
incompleta viene rifiutata. Le immagini restano unsupported e lo snapshot
parziale: questo incremento non abilita ancora l'editor completo immagini.

Verifiche automatiche: **1.119 Core PASS**, inclusi 39 nuovi casi;
**1.058 Unity PASS**, compresa selezione strutturale e lettura dei vincoli;
**1.615 bridge PASS**, zero saltati. Add-in sperimentale compilato con zero
errori e warning. Primo compilato diagnostico con due cast interop mancanti,
corretto e conservato separatamente.

La prova HTTP con add-in reale passa su Parte e Lamiera. Ogni contesto
contiene un'immagine legacy e una creata dal batch di produzione con risorsa
registrata nella fixture server: non si certifica un nuovo upload endpoint.
Verificati frame correnti, sei pixel PNG, metadata originali separate,
quattro bordi e quattro punti per immagine, proprietari esatti e assenza
dai pick. Una linea ordinaria resta selezionabile; il suo angolo condiviso
con l'immagine viene escluso.

**38/38 rifiuti PASS**: 16 riferimenti ai bordi/angoli più tre operazioni
sull'angolo condiviso per ciascun contesto. Tutti i riferimenti si legano
nativamente e raggiungono la guardia strutturale esplicita; esito
ROLLED_BACK/INVALID_ARGUMENT, primo step, modello e revisioni invariati.
La costruzione di una linea con angolo immagine condiviso è riuscita sulla
API installata. Anteprima della formattazione su una linea ordinaria
annullata esattamente, poi commit riuscito con immagini preservate.

Tre tentativi diagnostici restano FAIL: primo NO_TARGET iniziale; secondo
e terzo assumono erroneamente che i dettagli del tool siano al livello
esterno dell'eccezione Core. La versione finale usa il contratto corretto
Details[details]. Il retry iniziale è limitato alla lettura del medesimo
target esplicito prima del baseline; nessuna mutazione viene ripetuta.
Non sono state indebolite le guardie per far passare la sonda.

Run finale `image54-structure-r4.json`; sorgenti e DLL effettive in
`increment54/inprocess-tested-r4`. Manifest e DLL installati ripristinati;
processo dedicato terminato e processo Inventor preesistente preservato.
Nessuna nuova APK per questi cambiamenti, né prova Quest o fisica.
Restano editing/importazione XR completi, cronologia OLE, linked,
trasparenza e tutti i gate complessivi S1.
