# Inventor XR SO — quattro esperienze nel visore

Data: 25 ottobre 2026, Europe/Rome.
Stato: **proposta di prodotto**, non implementata da questo documento.

Analisi dello stato attuale, attività dettagliate e sequenza di consegna:
[piano delle quattro esperienze](../plans/2026-10-25-quattro-esperienze-xr.md).

## Obiettivo

All'ingresso nel visore l'utente sceglie cosa vuole fare. La Home presenta
quattro sezioni stabili, con titolo e una breve descrizione:

| Sezione | Descrizione nella Home |
|---|---|
| Reality Capture | Scansiona un oggetto o un ambiente e porta il risultato in Inventor. |
| Disegno e modifica | Crea e modifica parti, lamiere e assiemi. |
| Presentazione | Esplora il modello, misura e prova i movimenti disponibili. |
| Verifica meccanica | Smonta virtualmente, esplodi e analizza l'assieme. |

La connessione al PC è uno stato comune, non una quinta attività. Un PC già
associato può riconnettersi in background, ma non deve aprire automaticamente
la progettazione. Senza pairing la Home resta accessibile; le attività che
richiedono Inventor propongono il collegamento quando serve. Reality Capture
conserva l'ingresso indipendente già presente.

## 1. Reality Capture

Percorso guidato: nuova scansione Oggetto/Area → delimitazione → acquisizione
→ revisione → riferimenti dimensionali → risultato → invio a Inventor.
Le scansioni salvate sono accessibili dalla stessa sezione.

La UI distingue acquisizione, trasferimento, elaborazione e risultato:
terminare una scansione non significa avere già un modello pronto.
Il risultato indica il proprio tipo (nuvola, mesh, primitive o geometria CAD)
e la qualità disponibile. Una mesh di riferimento non viene descritta come
parte parametrica modificabile. L'importazione in Inventor è esplicita;
«Continua in Disegno e modifica» appare dopo il completamento effettivo.
Implementazione e gate della pipeline restano quelli delle spec RC0–RC4 e
del [verbale Reality Capture](../../xr-reality-capture-verification.md).

## 2. Disegno e modifica

Raccoglie il lavoro CAD esistente: schizzo, feature, parametri, lamiera,
componenti e vincoli di assieme. Mantiene anteprima, Applica, annullamento,
revisione CAD. Il salvataggio resta esplicito: oggi il comando generale XR
rimanda al desktop; il percorso Salva riferimento di RC4 è separato. La nuova
UI deve rappresentare questa differenza finché il salvataggio generale non
viene implementato.

Dentro questa sezione il documento continua a scegliere il contesto M9:
Parte → Progettazione, Lamiera → Lamiera, Assieme → Assieme. Doppio Trigger,
breadcrumb e Torna mantengono il percorso assieme/sottoassieme/parte.
Misure e sezioni rimangono disponibili durante il lavoro dove già compatibili.

## 3. Presentazione

Tavolozza minima: Misura, Posiziona modello, Movimenti quando disponibili,
Ripristina vista, Home. Posizionare o ruotare l'intero modello agisce sulla
vista XR. Non modifica posizioni dei componenti nel documento.

Non espone modifica di feature, parametri, vincoli, salvataggio CAD o ingresso
in modifica tramite doppio Trigger. La selezione serve a indicare e misurare.
Anche voce, scorciatoie e callback asincroni rispettano questa limitazione;
nascondere i soli pulsanti non basta.

### Movimenti da implementare

Ogni movimento supportato mostra nome, slider, valore con unità, limiti e
Ripristina. I limiti provengono dai dati validati del meccanismo; un vincolo
da solo non implica che esista un movimento controllabile.

Prima versione: un movimento indipendente per volta. Occorre definire un
contratto per asse, componenti coinvolti, relazione con gli altri componenti,
intervallo e pose. Non basta spostare liberamente una mesh vincolata.
La riproduzione usa pose di presentazione senza scrivere parametri Inventor.
Meccanismi non supportati mostrano il motivo senza slider funzionanti fittizi.
L'animazione non viene presentata come simulazione dinamica o verifica delle
collisioni. Il ritorno alla posa iniziale è sempre disponibile.

## 4. Verifica meccanica

Strumenti: albero componenti, selezione, nascondi/isola, trasparenza, sezione,
misura, smontaggio virtuale, esploso, interferenze, distanza minima e stato
dei vincoli. Riusa le capacità M7 esistenti secondo i loro gate effettivi.

«Smonta» separa visivamente un componente selezionato e permette di
riposizionarlo nella scena XR; non cancella componenti o vincoli in Inventor.
«Esplodi» applica separazioni ripristinabili con slider 0–100%; può interessare
l'assieme o un sottoassieme. Smontaggio ed esploso sono nuove funzionalità
da implementare. Il ripristino ricompone le pose iniziali e azzera lo slider.

Interferenze e distanze CAD si riferiscono alla configurazione originale e
alla revisione analizzata. Con il modello esploso/smontato, la UI deve
proporre «Torna alla configurazione CAD e analizza» prima di eseguire o
focalizzare risultati CAD. Non mostra risultati originali come se fossero
calcolati sulle pose virtuali. Una revisione nuova invalida i risultati.

## Navigazione e isolamento

La sezione scelta e il tipo di documento sono due stati distinti.
Il router M9 può aggiornare documento e breadcrumb in tutte le sezioni CAD,
ma apre i workspace di authoring soltanto in Disegno e modifica. Un cambio
di documento dal desktop non cambia l'attività scelta dall'utente.

Ogni sezione mostra il proprio nome e un comando Home. Il cambio attività
preserva connessione e documento; chiude gli strumenti temporanei e ripristina
pose esplose, componenti smontati e movimenti dimostrativi. Un comando in corso
o una bozza richiede conclusione/annullamento; una scrittura CAD con esito
incerto mantiene la protezione di revisione esistente. Nessuna bozza viene
scartata e nessun documento viene salvato implicitamente dal cambio sezione.

Le trasformazioni di sola vista sono separate dalle pose CAD usate per
misurazioni e analisi. Il cambio revisione annulla le anteprime incompatibili.

## Sequenza di realizzazione proposta

1. Home con quattro ingressi e stato attività separato dal contesto documento;
   instradamento input/voce/catalogo per attività; migrazione degli strumenti
   esistenti e regressioni Reality Capture/M9.
2. Presentazione con misure e posizionamento; Verifica meccanica con strumenti
   M7 già presenti. Le nuove capacità non ancora disponibili sono indicate
   nella pagina della relativa sezione, senza comandi che fingono di operare.
3. Smontaggio ed esploso locali con gerarchie e ripristino; coerenza analisi.
4. Contratto dei movimenti, supporto dei primi meccanismi e slider validati.

Questa proposta cambia il livello superiore di navigazione M9; non sostituisce
la navigazione per contesto dentro la progettazione. Il piano collegato sopra
definisce file coinvolti, migrazione dei runner e gate numerati; prima di F5
e F7 occorrono le specifiche esecutive per esploso e contratto dei movimenti.

## Criteri di accettazione da riportare nel piano

- Dopo connessione/riconnessione compaiono le quattro attività; nessun ingresso
  automatico in modifica.
- Presentazione non invia scritture CAD da controller, voce o risposte tardive.
- Cambiare documento dal desktop mantiene l'attività selezionata.
- Capture funziona anche senza documento e conserva la gestione delle sessioni.
- Cambio attività durante bozza, acquisizione o revisione non perde lavoro.
- Smontaggio, esploso e movimenti si ripristinano senza modificare Inventor.
- Risultati CAD e pose virtuali non vengono confusi; revisione obsoleta segnalata.
- Test core/EditMode, runner Quest sintetico su fixture dedicata e prova fisica
  restano tre evidenze distinte; comfort e comprensibilità richiedono il visore.
