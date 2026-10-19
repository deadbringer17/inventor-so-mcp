# S1 — Workflow nativi dei parametri, incremento 69

14 ottobre 2026. **18 gate S1 OPEN**; UI68 e Quest sono evidenze separate.

I builder Core67 sono esercitati attraverso il backend HTTP effettivo e
DesignSession: per ciascuna fixture Parte/Lamiera, due set di espressione
del parametro sorgente (10→20→35 mm), creazione di un parametro inutilizzato,
rinomina e cancellazione. **Dieci preview e dieci commit confermati**.
Ogni preview ripristina esattamente stato catturato e revisione prima
della conferma sintetica di rendering e dell'Apply esplicito.

Il parametro sorgente guida una linea con quota driving e un TextBox con
campo parametrico nativo. Due aggiornamenti per contesto verificano
lunghezza della linea, valore/espressione della quota, testo risolto,
identità persistenti, snapshot HTTP con glifi e DesignContext associato
alla revisione. Gli array dei glifi correnti sono congelati prima di creare
un oracle nativo indipendente: l'oracle non può riparare uno snapshot
obsoleto senza far fallire la prova. Quattro confronti dei glifi PASS e
quattro Abort esatti dell'oracle.

**Otto rifiuti atomici** mantengono contenuto e revisione: cancellazione
del parametro con dipendenti, cancellazione del parametro modello della
quota, revisione obsoleta ed espressione non valida. Rinomina preserva
l'identità del parametro inutilizzato; la cancellazione rimuove solo quello.
Le verifiche numeriche dei consumatori usano tolleranza 0,00001 mm; il
confronto dello stato dopo preview/refusal/oracle Abort è esatto.

`text69-native.json`: Parte/Lamiera PASS al primo run. Sorgenti, wrapper e
assembly effettivamente eseguiti in `increment69/inprocess-tested-r1/`;
sorgenti principali archiviati anche in `verified-source/`. L'add-in è lo
stesso binario67 già compilato/verificato; questo incremento aggiunge la
sonda, non una nuova funzione nativa. Build della sonda senza errori,
con il solo warning NuGet preesistente. Processo dedicato terminato e
manifest/DLL originali/processo utente preservati.

**NOT COVERED:** input UI68 attraverso il dispositivo, mutazioni booleani/
testuali tramite questi workflow, rinomina di una sorgente con dipendenti,
tutti i tipi/unità/opzioni, comando Parametri dentro l'editor schizzo,
oracle desktop, performance e prove fisiche. Nessuna nuova APK creata o
installata; la conferma render della sonda Core è sintetica.
