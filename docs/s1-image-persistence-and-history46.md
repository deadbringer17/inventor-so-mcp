# S1 — Persistenza dei byte immagine e cronologia nativa

13 ottobre 2026, incremento 46, F03/S1-12/14. Perimetro integrale invariato:
77 comandi e 18 gate aperti. Variante immagine ancora non dichiarata nelle
capability XR; nessuna installazione add-in/host o nuova APK.

L'inserimento embedded conserva ora i BMP originali in un AttributeSet
binario persistente della nuova SketchImage, dentro la stessa transazione.
Schema 1: byte, SHA256, asset ID derivato dall'hash, formato e dimensioni
pixel. Nessun percorso, credenziale o owner viene scritto nel documento.
La scrittura verifica immediatamente la lettura; hash, layout BMP, tipi e
campi devono coincidere. Le copie dei byte sono separate dal valore nativo.

Prima di Add si verificano tutte le immagini gestite del documento e i
budget: 32 MiB per sorgente, 64 MiB totali, massimo 32 immagini gestite.
I metadata vengono controllati prima di leggere i valori binari; l'API
espone la lunghezza effettiva dell'array solo dopo Value, che viene confrontata
col dato dichiarato. I byte non sono ancora esposti in DTO o trasporto di
lettura. Le immagini legacy prive del nostro AttributeSet non acquisiscono
automaticamente una sorgente XR: reader e rendering rimangono da integrare.

La prima prova Undo è **FAIL conservato**: l'immagine viene rimossa ma il
descrittore OLE del commit resta. Nessuna cancellazione o riparazione viene
eseguita nella cronologia. Il controllo di baseline OLE esatta resta fallito.

La seconda prova distingue i risultati: **32 casi** (16 Parte, 16 Lamiera),
BMP 24/32, top/bottom e quattro orientamenti. Tre cicli Undo/Redo per caso:
96 Undo ripristinano esattamente il modello, 96 Redo ripristinano immagini,
geometria, identità OLE e payload. Durante le ripetizioni non cresce il numero
di OLE: Undo conserva esattamente le identità del commit. I 64 rifiuti per
owner o revisione errati non mutano il documento. Sorgente originale rimossa
prima del commit, sorgente privata già rimossa e staging azzerato prima
della cronologia. Nessun byte/file esterno serve al Redo.

**Esito complessivo cronologia: PARTIAL**, non PASS completo. Baseline OLE
esatta dopo Undo: 0/32. Restano da studiare la proprietà dei descrittori nella
cronologia e il comportamento quando la catena Redo viene scartata; non è
stato provato che conservarli o cancellarli sia la strategia prodotto finale.

Persistenza su file: **due casi PASS**, Parte e Lamiera, BMP 32 bit top-down
con un quarto di giro. SaveCopyAs, riapertura invisibile, lettura nativa del
payload e confronto della geometria: hash/byte esatti e geometria entro
0,00001 mm. In queste due run anche i valori geometrici prima/dopo sono
identici. Identità, percorso vuoto, Dirty e DatabaseRevisionId dell'originale
invariati; copie chiuse e file temporanei rimossi. Non è una prova di tutti
i formati o di apertura su un altro PC/versione Inventor.

La suite backend passa **1.506 test**, zero fallimenti e uno skip per host
già attivo. Add-in e sonda compilati senza warning. Core e Unity non
rieseguiti: le verifiche precedenti sono 1.000 e 1.049 PASS. Native test solo
in istanze create e possedute dalla sonda, senza documenti utente modificati;
le istanze vengono terminate e non restano sorgenti private delle run.

Regressione dopo la persistenza binaria: **96/96 casi nativi PASS**, 48 su
Parte e 48 su Lamiera, ripetendo preview, commit ed errore dopo Add con il
nuovo payload. Ripristino immagini/OLE/revisione esatto, staging rilasciato,
fixture chiuse e sorgenti private rimosse. Questa regressione è distinta
dalla verifica PARTIAL della cronologia descritta sopra.

Il manifest 46 conserva l'eseguibile e la libreria effettivamente usati per
salvataggio/riapertura e regressione. Per la run history r2 sono conservati
sorgente, build log e report; l'eseguibile specifico non era stato copiato
prima della compilazione della sonda SaveCopyAs. La libreria nativa del
prodotto è invariata fra queste run. Il diagnostico iniziale history r1
conserva anche le sue DLL originali.

Residui: lettura/trasferimento dei byte dal documento, rendering/UV e bozza
XR, preview visiva, immagini native non gestite, linked, cronologia OLE,
Quest e prova fisica. La persistenza rende possibile proseguire oltre la
scadenza del catalogo HTTP; non abilita da sola l'immagine sul visore.
