# S1 B02 — Scelta del metodo della spline interpolante

11 ottobre 2026. L'editor condiviso Parte/Lamiera offre durante la raccolta
punti tre scelte esplicite: **Norme**, **Energia minima**, **AutoCAD**.
I valori Inventor installati sono rispettivamente 26369, 26370 e 26371.
«Norme» conserva l'etichetta osservata nel menu del desktop italiano; i
campioni e le letture native del precedente incremento minimo/default
identificano i tre metodi. Nessuna preferenza globale Inventor viene cambiata.

All'avvio il metodo selezionato viene dalla preferenza nativa dello snapshot.
La scelta non cancella i punti e non crea una bozza o una preview implicita.
Durante X/Y numerico i pulsanti del metodo sono disabilitati. Concludi spline
congela punti e metodo nelle opzioni della bozza; refresh e riconferma non
adottano una preferenza cambiata successivamente. Annulla revoca l'input;
un nuovo comando riparte dalla preferenza corrente. Le spline CV non offrono
queste opzioni e non inviano `fit_method`.

## Verifica

La sonda riproducibile aggiunge lo stage `arc-ui-spline-methods`, sulla
fixture dedicata preparata con `arc-ui-prepare` oppure
`arc-ui-prepare-sheet`, seguita da `arc-ui-cleanup`.
`spline-method33-part-batch.json` e `spline-method33-sheet-batch.json`
verificano **36 casi**, 18 per contesto: tre metodi, 2/3/5 punti e due
rotazioni. Il metodo effettivo viene letto dall'oggetto nativo e confrontato
con quello richiesto. Punti risolti, snapshot di geometrie/vincoli e
anteprima/commit vengono confrontati esattamente con l'API diretta;
preview e oracle annullati preservano snapshot/revisione. È confronto
API/batch, non una nuova prova del comando desktop o una prova Quest.

`spline-method33-final-state.json` conferma zero documenti aperti e nessuna
fixture rimasta. Il deposito degli assembly eseguiti è
`artifacts/s1-verification/spline-method33-clearance/`.

I nuovi test Unity esercitano i tre default, scelta/punti/input numerico,
rimozione punto, congelamento dopo cambio revisione/preferenza, annullamento,
CV e callback stale in entrambi i workspace. Nel primo run tre casi Lamiera
si fermavano correttamente perché la fixture non caricava la scena della
nuova revisione prima del refresh: preparazione corretta, diagnostico
`unity-spline-method33.xml` conservato. Il run finale
`unity-spline-method33-final.xml` passa **987 test, zero fallimenti/skipped**,
con processo terminato a codice zero: 16 nuovi casi Unity. I quattro assembly
verificati sono stati archiviati prima della build APK.

APK ordinaria cumulativa compilata con esito Success e processo a codice
zero, conservata come `InventorXrSo-s1-spline-method.apk`: **53.897.214 byte**,
SHA-256 `EDD3C74A0727BB1405B04DC4B6CE335A19718652F3B79546B633113332F969B8`.
Non installata sul Quest e senza runner S1. Il manifest
`spline-method33-manifest.json` identifica sorgenti, assembly eseguiti,
rapporti, diagnostico e APK.

Chiusura spline, editing di punti/maniglie/tensione/metodo delle spline
esistenti, caso misto CV/poligono, renderer/runner e prova fisica rimangono
necessari. Nessun gate S1 complessivo viene chiuso da questo incremento.
