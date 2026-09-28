# Inventor XR SO — M5: Lamiera, sviluppo piano e voce

Data: 28 settembre 2026. Stato: specifica tecnica iniziale; implementazione e
accettazione M5 non ancora eseguite.

Riferimenti: [specifica prodotto](../../../Inventor%20XR%20SO/inventor_meta_product.md)
§35–38, §43, §50–51, §56–57; [collaudo M4](../../xr-m4-collaudo.md).
M5 riusa il ciclo di bozza, preview, validazione, Applica e cronologia XR di M3/M4.
Le prove fisiche M4 ancora aperte restano gate M4 separati.

## Obiettivo e perimetro

Su una parte Inventor di tipo Sheet Metal, il Quest apre Lamiera come modalità
primaria. L'utente vede regola e spessore, crea una flangia selezionando un bordo,
regola altezza e angolo con manipolatore o numeri, controlla la preview calcolata
da Inventor e applica. Può creare lo sviluppo piano, mostrarlo accanto al
modello piegato e muoverne la sola rappresentazione XR. Push-to-talk in italiano
richiama i comandi disponibili o inserisce un numero nel campo attivo.

I comandi lamiera M5 da esporre inizialmente nel client sono regola/spessore,
faccia base da schizzo chiuso, flangia, taglio da schizzo e sviluppo piano. La
prima user journey obbligatoria è bordo → flangia → preview → Applica → sviluppo
→ detach. Gli altri handler lamiera già presenti nel backend non diventano
automaticamente pulsanti XR; richiedono selezione, parametri e prove native
specifiche prima di essere annunciati nella UI.

Non rientrano in M5: conversione implicita di una parte ordinaria in lamiera,
nesting, CAM, certificazione di fabbricabilità, un secondo documento per lo
sviluppo staccato, agente CAD a linguaggio libero, ascolto permanente e hand
tracking. L'esportazione DXF già presente nel backend è opzionale nella UI M5:
non è richiesta per completare il confronto visivo piegato/sviluppato.

## Stato della base e lavoro necessario

Il backend espone `inventor_get_sheet_metal_info`, `set_sheet_metal_rule`,
`sheet_metal_face`, `sheet_metal_flange`, `sheet_metal_cut` e
`create_flat_pattern`; quest'ultimo segnala un pattern esistente senza
ricrearlo. I comandi sono nel catalogo atomic e l'export DXF richiede un
pattern già presente. Questo è codice disponibile, non evidenza di un workflow
M5 completo sul Quest. Il client XR non ha ancora contesto lamiera, mesh dello
sviluppo, detachment o acquisizione voce. La preview Design attuale mostra la
parte risultante e valida `rebuild`/`feature_health`; M5 deve verificare
esplicitamente regola, pieghe e pattern senza cambiare il comportamento delle
parti ordinarie.

## Contesto Inventor e modalità

All'attivazione di un documento, il client interroga stato e revisione e poi
`inventor_get_sheet_metal_info`. Solo `is_sheet_metal=true` rende Lamiera la
modalità primaria. Modello 3D, Schizzo e Ispeziona restano disponibili. Se la
lettura è assente, incompleta o stale, Lamiera mostra un motivo e non arma una
scrittura. Aprire una parte ordinaria non attiva Lamiera né tenta una
conversione. Cambio documento, revisione o stato di connessione invalida
selezioni, bozza, preview e asset dello sviluppo; il client rilegge il contesto.

Il contesto M5 comprende `document_id`, revisione, regola attiva, spessore in
mm, regole disponibili, stato del flat pattern, numero di body e pieghe e
riferimenti di bordo/faccia del modello piegato. I riferimenti CAD selezionabili
restano quelli persistenti del documento, mai gli ID della mesh GLB. Le quote
di sviluppo mostrate all'utente sono valori riportati da Inventor, non stime
calcolate dalla triangolazione.

## Authoring della lamiera

La flangia parte da uno o più bordi reali selezionati e evidenziati. Il pannello
mostra altezza in mm, angolo in gradi e datum esterno/interno/tangente. Il
manipolatore spaziale modifica la stessa bozza del campo numerico, con scala
visuale indipendente dalle dimensioni CAD. Grip semplice muove la vista; il
gesto di modifica richiede comando Flangia armato e Grip+Trigger sul
manipolatore. Il rilascio chiede una preview, non un commit. Ogni variazione
di bordo, numero, datum o regola invalida Applica e genera una nuova preview.

Faccia base e taglio richiedono uno schizzo valido nel documento attivo; il
client usa lo stesso contesto Schizzo di Design e indica quale profilo verrà
usato. Lo spessore deriva dalla regola attiva; una modifica esplicita della
regola/spessore è una bozza separata. Non si deduce un valore di piega dal drag.
Un bordo che non appartiene al modello attivo, un profilo aperto, più body per
lo sviluppo o un parametro non applicabile causano errore leggibile e lasciano
la bozza editabile senza scrittura persistente.

Ogni authoring passa dal piano atomic revision-bound. I controlli minimi sono
`rebuild` e `feature_health`; la prova nativa M5 deve aggiungere controlli
espliciti su stato di pieghe, spessore e generazione dello sviluppo dove
richiesti, senza dichiararli passati per il solo esito del rebuild. Applica è
abilitato solo dopo mesh di preview renderizzata e validator confermati.
Annulla scarta la bozza. Undo/Redo usa la cronologia XR del documento. Un
commit incerto conserva il blocco condiviso con Design e Assembly.

## Flat Pattern XR

`Crea sviluppo` è una mutazione CAD esplicita: usa `create_flat_pattern` in
preview/commit, segnala se lo sviluppo esiste già e non lo ricostruisce
silenziosamente. Se disponibile, il backend deve produrre un asset GLB separato
del flat pattern reale, con coordinate, unità e revisione dichiarate. Occorre
prima una prova nativa che stabilisca come leggere la geometria del pattern
senza lasciare Inventor in modalità Flat Pattern Edit né alterare il modello
piegato. L'API documenta `FlatPattern.SurfaceBodies` come collezione dei body
risultanti ([Autodesk](https://help.autodesk.com/cloudhelp/2022/ENU/Inventor-API/files/FlatPattern_SurfaceBodies.htm));
il probe deve verificarne uso e coordinate su Inventor 2027. Non è sufficiente
appiattire graficamente la mesh piegata nel client.

`Detach Flat Pattern` duplica solo la visualizzazione nel Quest: il pezzo
piegato rimane nella posizione corrente e lo sviluppo appare accanto, con
etichetta `Sviluppo — sola vista` e misure native. Grip riposiziona questa
vista locale; non scrive pose CAD, non modifica l'orientamento del pattern in
Inventor e non crea un file. La scelta di un bordo per allineare lo sviluppo è
invece una modifica CAD esplicita, con preview e Applica. Il pattern staccato
non è selezionabile come geometria CAD del pezzo piegato. Se il documento cambia,
la vista staccata viene nascosta finché il nuovo asset è verificato; a rete
persa resta visibile con indicazione di revisione e sola ispezione.

Il confronto piegato/sviluppato mostra spessore, lunghezza, larghezza e numero
di pieghe forniti da Inventor. Se il pattern manca o non è esportabile nel
budget geometrico, la UI spiega il limite e non mostra uno sviluppo stimato.
La mesh separata non contiene token di facce/spigoli transitori. La sua cache è
legata a documento, revisione e identità del flat pattern, separata dalla
cache della parte piegata e dagli asset di preview temporanei.

## Voce: comando specchio e dettatura numerica

Push-to-talk usa un pulsante fisico del controller mentre è tenuto premuto;
rilascio, perdita tracking, cambio modalità o disconnessione chiudono la
cattura. La UI mostra ascolto, elaborazione, trascrizione e comando proposto.
Il microfono non rimane acceso in background. Il permesso audio è richiesto al
primo uso e un rifiuto lascia intatti i comandi manuali. Audio e testo non
entrano nei log ordinari; diagnostica opt-in e cancellazione dei buffer a fine
richiesta. Trasmissione, se necessaria, usa solo il canale associato e TLS
pinning già impiegato per il Quest. Nessun servizio cloud viene attivato
implicitamente.

Il riconoscimento è separato dal router dei comandi. Il primo target è un
motore locale sul PC, dietro un'interfaccia sostituibile; una prova iniziale
deve misurare italiano, rumore ambiente, latenza e risorse prima di sceglierne
il runtime. Se non raggiunge i gate, la voce M5 resta aperta e i comandi manuali
continuano a funzionare. Il router accetta solo un vocabolario finito di
intenzioni e alias italiani associati agli stessi command ID della UI. Non
interpreta testo libero come operazioni backend e non aggira modalità,
selezione, validazione o abilitazione del pulsante corrispondente.

`Flangia`, `Crea sviluppo`, `Smusso`, `Raccordo`, `Misura`, `Isola`, `Annulla`
e `Crea schizzo` sono esempi del mirror. `Annulla` agisce sulla bozza attiva;
senza bozza, il contesto esplicita se significa Undo prima di procedere.
`Applica` pronunciato mostra la conferma del piano corrente, ma richiede la
pressione fisica di Applica: una trascrizione errata non può committare CAD.
Comandi indisponibili sono riconosciuti ma spiegati, senza chiamata mutante.

La dettatura numerica opera soltanto con un campo armato. Il parser italiano
accetta numeri decimali (`dodici virgola cinque`), segno e unità dichiarate
`millimetri`/`gradi`; normalizza a mm/gradi e mostra testo e valore prima di
confermare il campo. Non converte senza avviso unità ambigue, non accetta
valori non finiti/fuori range e non cambia un campo diverso da quello attivo.
L'inserimento aggiorna la bozza e invalida Applica come la tastiera; il flusso
richiede ancora preview e commit esplicito.

## Ciclo di vita, errori e accessibilità

Preview tardive, trascrizioni tardive e mesh di sviluppo tardive sono scartate
se non coincidono documento, revisione, modalità e generazione della richiesta.
Le cancellazioni staccano microfono, callback, indicatori e mesh temporanee.
La UI conserva i nomi Inventor dei comandi e rende sempre visibili selezione,
unità, regola, stato preview e possibilità di Applica. I pannelli non
intercettano il ray di selezione CAD quando sono chiusi; i bersagli dei
manipolatori restano distinguibili a scala 1:1 e ridotta. Testo ed errori
devono essere leggibili nel visore, inclusi permesso audio negato, voce non
compresa, pattern non disponibile e regola incompatibile.

## Gate di consegna M5

| ID | Prova richiesta |
|---|---|
| M5-01 | Parte lamiera apre Lamiera primaria; parte ordinaria no; Schizzo/Ispeziona restano accessibili. |
| M5-02 | Regola, spessore, pieghe e stato pattern coincidono con Inventor e seguono documento/revisione. |
| M5-03 | Flangia: bordo reale, manipolatore e numero producono parametri equivalenti a scala 1:1 e ridotta; Grip semplice non scrive CAD. |
| M5-04 | Face/Cut da schizzo e regola/spessore passano preview, validator, Applica, Cancel e Undo/Redo senza residui. |
| M5-05 | Flat pattern nativo positivo; pattern esistente, multi-body e impossibilità di unfold hanno esiti distinti. |
| M5-06 | Sviluppo staccato affiancato al piegato: misura e orientamento verificati, Grip solo vista, nessun secondo documento. |
| M5-07 | Perdita rete, revisione stale, preview tardiva e commit incerto non abilitano Applica né riutilizzano asset vecchi. |
| M5-08 | Push-to-talk fisico: microfono solo durante pressione; rifiuto permesso e interruzioni chiudono cattura senza bloccare la UI. |
| M5-09 | Vocabolario italiano finito richiama gli stessi comandi manuali; comando disabilitato e trascrizione ambigua non mutano il CAD. |
| M5-10 | Dettatura di mm/gradi, decimali e segni aggiorna solo il campo armato; valore confermato richiede preview. |
| M5-11 | Voce `Applica` non committa; conferma fisica e piano valido producono un solo commit. |
| M5-12 | Quest + Inventor reale: journey flangia→sviluppo→detach e voce in ambiente d'uso; regressioni M1–M4. |

Le prove .NET/Unity e il runner Quest possono automatizzare i contratti; i gate
che riguardano ergonomia, audio reale e tracking richiedono il visore e
controller fisici. Un caso obbligatorio non provato resta aperto.

## Decisioni tecniche da chiudere prima dell'implementazione completa

1. Provare nell'API Inventor 2027 l'estrazione GLB del flat pattern senza
   entrare in edit e definire la sua identità di cache.
2. Confrontare almeno due motori STT locali sul PC su frasi italiane del
   vocabolario M5; scegliere runtime e budget solo dopo misure di accuratezza,
   latenza e memoria sulla postazione reale.
3. Stabilire il pulsante fisico push-to-talk che non collide con Grip,
   Trigger, menu polso e scorciatoie di sistema sul Quest 3.
4. Misurare tempi e dimensioni della preview lamiera e dello sviluppo sul
   fixture reale prima di fissare limiti UI o promettere tutti gli handler
   lamiera presenti nel catalogo.

## Risposte alla regola §57

| Domanda | Risposta M5 |
|---|---|
| entità CAD | parte Sheet Metal attiva, bordi/facce persistenti, schizzi e flat pattern Inventor |
| contesto | documento e revisione espliciti; nessuna conversione implicita |
| modalità | Lamiera primaria per parte lamiera, Design/Schizzo/Inspect accessibili |
| controller | Trigger seleziona; Grip muove vista; Grip+Trigger manipola bozza armata; pulsante dedicato per push-to-talk |
| intenzionalità | comando armato, preview validata e Applica fisico |
| precisione | mm/gradi numerici o dettati nel campo attivo, indipendenti da scala XR |
| voce | alias italiani → command ID disponibili; mai tool backend da testo libero |
| preview | modello calcolato da Inventor; asset flat pattern nativo separato |
| validator | rebuild, feature health e verifiche lamiera/flat pattern provate nativamente |
| errori | bozza conservata, motivo leggibile, trascrizione/valore visibili |
| stale/disconnessione | invalida piano e asset; vista locale in sola ispezione |
| familiarità Inventor | regola, flangia, spessore, pieghe e sviluppo con significato Inventor |
