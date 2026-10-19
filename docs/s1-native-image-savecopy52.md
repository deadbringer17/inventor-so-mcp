# S1 — Bitmap e frame nativi dopo SaveCopy, incremento 52

14 ottobre 2026. Evidenza nativa aggiuntiva per F03/S1-04/12; **18 gate aperti**.

**6/6 casi PASS** nell'add-in effettivamente caricato in Inventor 2027:
Parte e Lamiera, ciascuna con RotateLeft, MirrorHorizontal e rotazione
geometrica di 37° seguita da MirrorHorizontal. Le immagini sono embedded
legacy, prive dei due AttributeSet gestiti. Il BMP generato contiene sei
pixel distinti, 32 bit top-down; il file sorgente viene eliminato prima
della lettura. Non vengono usati cache o payload persistenti gestiti.

Ogni inserimento e trasformazione termina la propria transazione prima del
salvataggio. Il test legge bitmap e frame tramite il pipe autenticato
dell'add-in, esegue SaveCopyAs, apre e attiva la copia nel processo dedicato,
poi ripete la stessa lettura. I quattro angoli sono confrontati con una
teoria indipendente ricavata da Position/Width/Height prima delle operazioni,
con tolleranza 0,00001 mm. I colori restano quelli del sorgente: le
trasformazioni sono rappresentate dal frame, non da pixel trasformati.

Risultati verificati in tutti i sei casi:

- Bitmap, hash, frame, dimensioni semantiche e flag conservati nella copia;
  manifest prima/dopo identici, escluso l'identificatore del trasferimento.
- Teoria degli angoli e colori esatti verificati prima e dopo la riapertura;
  revisione del backend invariata dalle letture.
- Documento originale preservato esattamente per identità, percorso,
  DatabaseRevisionId, Dirty, geometria delle immagini e chiavi OLE.
- Copie chiuse, sorgenti e copie temporanee rimosse in entrambi i contesti.

Il primo tentativo rifiuta Activate sul documento aperto nascosto con
Open(false), con E_FAIL. La sonda usa quindi Open(true), perché la query
richiede il documento attivo. Il secondo tentativo usa erroneamente
doc_ + InternalName: SaveCopy conserva InternalName, e due documenti aperti
richiedono il suffisso del percorso previsto da DocumentIdComposer. È
corretto soltanto il calcolo dell'identificatore nella sonda. Entrambi i
fallimenti sono conservati con report, sorgenti e DLL effettive; nessuna
guardia del prodotto è stata indebolita.

La build finale della sonda passa con zero errori e warning. La run finale
r3 termina con exit code 0; il PID dedicato 76596 risulta terminato dopo
Quit, con zero processi Inventor al controllo successivo. Manifest originale
e DLL installata sono verificati tramite hash come ripristinati/invariati.
Il conteggio immediato dello script d'installazione può osservare il processo
durante l'uscita: la verifica separata del PID documenta lo stato terminale.

Evidenze: `artifacts/s1-verification/image52-live-savecopy-r3.json`,
`inprocess-install52-r3.json`, `image52-exit-verification-r3.json`;
sorgenti e assembly in `increment52/compiled-r3`, diagnostiche r1/r2 e
baseline nativa 51 conservate separatamente. Il manifest 52 verifica anche
gli hash dei manifest precedenti 38–51.

Questo incremento aggiunge una sonda e il relativo harness, senza modifiche
al prodotto, nuove build di Unity, APK o prove Quest. Non copre trasparenza,
immagini linked, altri formati o dimensioni, sostituzioni del bitmap,
salvataggi di documenti dell'utente, riavvio dell'intero processo o rendering
fisico. I gate S1 e l'esito parziale Undo/OLE restano aperti.
