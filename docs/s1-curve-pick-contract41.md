# S1 — Punti di selezione ordinati per le soluzioni 2D

12 ottobre 2026, incremento 41, fondamento B04/A03, gate S1-04/06/14.
Il perimetro resta 77 comandi; nessuno dei 18 gate è chiuso.

`NativeSketchHit.PickPointMm` conserva il puntamento originale nelle coordinate
planari dello schizzo, in mm, senza sostituirlo con un estremo o con il punto
più vicino sulla curva. I hit di finestra non hanno un punto individuale.
Il Core congela due selezioni ordinate linea/arco in `references` e
`pick_points_mm`, clonando le opzioni. Rifiuta curve duplicate, sottoparti,
punti mancanti, schizzo parziale, entità di un'altra istanza dello snapshot
anche con chiavi/revisione uguali, e puntamento non planare.

L'editor condiviso Parte/Lamiera conserva gli hit diretti insieme alla
selezione. Toggle e sostituzione eliminano i punti rimossi; refresh, cambio
contesto e finestra invalidano la soluzione. `SelectedPickPoints` espone
l'ordine e l'assenza di un punto, mentre `FreezeSelectedCurveOptions`
richiede selezioni correnti. Nessun punto è ricostruito dall'ID dell'entità.
La riconferma generica dei soli riferimenti rifiuta una bozza con
`pick_points_mm`: per una futura operazione occorre rigenerare la coppia
da due hit freschi e passare le opzioni a `SetOperation`.

Verifiche: **1.000 Core PASS e 1.049 Unity EditMode PASS**, entrambi processi
con exit 0, nessun fallimento o skip. Dieci nuovi casi Core e sei Unity:
ordine, millimetri e immutabilità, anchor/puntamento, stale e sostituzione
snapshot, selezione finestra, duplicati/tipi, invalidi, toggle e refresh
in entrambi i workspace, assenza di preview/commit implicito. Un primo test
Core aveva un atteso JSON integer invece di double: diagnostico conservato,
correzione solo dell'atteso numerico, senza variazioni di tolleranza.

Questo incremento prepara il contratto di input: non implementa il comando
Raccordo o Smusso, non abilita capability e non prova lato/soluzione native.
Proiezione del puntamento sulla curva quando necessaria, default/opzioni
desktop, gestione delle quote sorgenti rilevata dall'incremento 40,
preview/commit prodotto, Quest e prova fisica restano da completare.
Nessuna nuova APK prodotta/installata e nessuna nuova suite bridge eseguita.

Evidenze in `artifacts/s1-verification`: `core-pick41-final.trx/.log`,
`unity-pick41.xml/.log`, diagnostici Core precedenti, snapshot sorgenti e
assembly in `increment41`, manifest con hash verificati. L'Inventor aperto
dall'utente non è stato modificato durante queste suite software.
