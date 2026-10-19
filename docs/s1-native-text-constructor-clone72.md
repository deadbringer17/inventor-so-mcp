# S1 — Clonazione con costruttore TextBox nativo, incremento 72

15 ottobre 2026. **18 gate S1 OPEN**; alternativa ricercata, non ancora
integrata nel comando Copia del prodotto.

Dopo il crash70, la sonda crea una nuova TextBox fitted con AddFitted,
usando FormattedText letto dall'originale e lo stile locale proprietario.
Trasferisce proprietà e colore nativi; non ricostruisce glifi e non chiama
MoveSketchObjects con Copy=true. Gli array restituiti da GetFacetsInfo
devono essere la traslazione modello dell'originale, con triangoli nativi
equivalenti e winding preservato.

**Parte e Lamiera PASS**, ciascuna in un processo dedicato: quattro
sottocasi complessivi (letterale e campo parametrico per contesto), tutti
con Abort esatto di geometria, identità, stili e parametri catturati.
La nuova TextBox e il suo punto d'origine hanno identità distinte.
Quattro modifiche dell'origine della copia preservano l'originale.
Quattro aggiornamenti del parametro (10→22→7 mm per contesto) aggiornano
testo risolto e glifi sia dell'originale sia della copia mantenendo identità,
pose e proprietà; quattro oracle costruiti solo dopo gli assert confermano
i glifi senza riparare gli oggetti esistenti.

Profilo esercitato: testo fitted a riga singola, rotazione zero, font ereditato
dallo stile locale, altezza4 mm, grassetto, WidthScale1,25, centratura
orizzontale e verticale, interlinea multipla1,2, colore testo RGB80/60/140
opaco e stile RGB32/48/64. Non è una matrice completa dei formati.

La prima build falliva per SaveOptions ambiguo; qualificato System.Xml.Linq.
Prima del run è stato evitato l'assegnamento di Style a una TextBox già
creata, che falliva nelle prove59: lo stile locale viene passato al
costruttore nativo, eliminando il solo seed posseduto. Sorgenti iniziali
e log conservati. Build finale zero warning/errori.

Evidenze e assembly eseguiti: `increment72/r1-clone-part/` e
`r2-clone-sheet/`. Processi dedicati106824/85044 usciti, manifest/DLL/interop
originali e identità del processo utente91056 preservati. Nessun riferimento
COM acquisito ai suoi documenti. Nessuna nuova DLL distribuita o APK.

**NOT COVERED:** rettangolare/multilinea, rotazione non zero, tutte le
combinazioni di font/allineamento/interlinea/trasparenza, campi esterni,
vincoli o feature dipendenti, copia mista e rotazione con copia, percorso
produttivo preview/applica, oracle desktop, Quest e prove fisiche. La guardia
generale della copia rimane attiva finché l'integrazione è verificata.
