# S1 — Ricerca testo nativo, incremento 59

14 ottobre 2026. Comandi 27/28; **77 comandi nel perimetro e 18 gate OPEN**.
Questa prima versione prepara la sonda; nessuna prova COM 59 eseguita ancora.

Ownership esclusivo: nuovo progetto `bridge/tests/S1TextProbe/`, questo
documento e nuovi artefatti 59. Nessun reader, sessione, UI, dispatch,
registrar o catalogo di produzione modificato; nessun deployment di DLL o
manifest. Il progetto non referenzia l'add-in: usa l'interfaccia COM
installata, versione `31.0.19200.0`, e la policy interop condivisa.

## Superficie confermata e fonti

La lettura offline `artifacts/s1-verification/text59-interop-r1.json`
conserva versione/SHA dell'interop, membri di Sketch/PlanarSketch/TextBoxes,
TextBox/TextStyle e la scansione dei metodi Text/Path nelle interfacce
Sketch/Text. Le firme installate sono:

```text
TextBoxes.AddFitted(Point2d, string, object) -> TextBox
TextBoxes.AddByRectangle(Point2d, Point2d, string, object) -> TextBox
TextStyle.Copy(string) -> Style
TextBox.GetReferenceKey(ref byte[], int)
TextBox.GetFacetsInfo(out int, out int, out double[], out int[])
```

`TextBox` espone contenuto formattato, stile, origine, dimensioni, rotazione,
allineamento, fattore di larghezza e boundary. Sono proprietà del testo
ordinario. Le due modalità di creazione e lo stile opzionale sono documentati
nelle pagine Autodesk 2027 [AddFitted](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/TextBoxes_AddFitted.htm),
[AddByRectangle](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/TextBoxes_AddByRectangle.htm)
e [TextBox](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/TextBox.htm).

La scansione non trova un tipo GeometryText/TextOnPath o una creazione
testo-su-percorso nelle interfacce esaminate. Non è una prova universale di
impossibilità. La [procedura desktop Autodesk 2026](https://help.autodesk.com/cloudhelp/2026/ENU/Inventor-Help/files/GUID-1DD1246B-57D5-4E0A-B4F9-DD63FAD54A32.htm)
descrive un comando distinto che prende linee, archi o cerchi. Il corrispondente
URL 2027 non era accessibile nella ricerca. Quella procedura non conferma un
metodo API e non è una baseline verificata del desktop 2027 installato.
**Testo su percorso resta NOT COVERED**: la sonda non dispone glifi per
imitare visivamente il comando e non esegue ControlDefinition/dialoghi PC.

La documentazione delle creazioni ammette markup; non è stata trovata nella
ricerca una specifica ufficiale completa dei tag Parameter/Property per
schizzi di parte 2027. I quattro casi di associazione sono quindi **esperimenti
espliciti sul contenuto formattato**, non un contratto di prodotto validato
in anticipo. Rifiuti COM, markup accettato senza propagazione e associazioni
realmente aggiornate sono esiti separati. Nessuna API ipotetica viene invocata.

## Sonda preparata

Otto casi per Parte e Lamiera, con una fixture nuova per caso:

1. Testo fitted letterale, identità e modifica contenuto.
2. Rettangolo multilinea e dimensioni native.
3. Escape, override formato, allineamento, origine, rotazione 37° e larghezza.
4. Copia locale dello stile nella fixture non salvata; originale invariato.
5. Tag parametro locale, due aggiornamenti sorgente 22/7 mm.
6. Tag parametro con ComponentIdentifier vuoto, stessi aggiornamenti.
7. Campo iProperty personalizzato, Alpha→Beta→Gamma.
8. Parametro esportato come iProperty, due aggiornamenti 22/7 mm.

Snapshot prima/dopo Abort: document identity/revisione/Dirty, testo, parametri,
proprietà personalizzate, stili e conteggi di entità/vincoli/immagini/OLE.
Le faccette native vengono conservate mediante conteggi e SHA degli array;
un getter non disponibile è registrato come NOT COVERED, mai come geometria
verificata. Readback della proprietà Rotation non basta a provare che i
glifi ruotino correttamente: manca un oracle geometrico/desktop indipendente.

Due casi separati SaveCopy/riapertura confrontano contenuto, stile, geometria
e proprietà di due TextBox, preservando esattamente il documento originale
non salvato. Identità della copia registrata separatamente; nessuna ipotesi
che la namespace di riferimento sia identica. File generati conservati
nella directory univoca del rapporto; nessun salvataggio dei template.

Il guard verifica PID distinto da tutti quelli preesistenti, vieta
esplicitamente 91056 e richiede collezione documenti inizialmente vuota prima
di qualunque mutazione. Le successive guardie ammettono solo documenti
creati dalla sonda nel medesimo PID/data di avvio. Nessuna lettura dei
documenti del processo utente; nessun accesso ROT; nessun kill. Chiusura
senza salvataggio delle fixture e Quit esclusivamente dell'istanza posseduta;
uscita del PID e conservazione dei processi preesistenti vengono misurate.

## Build e identità preparate

Build r3: zero errori/warning, exit 0,
`artifacts/s1-verification/text59-build-r3.log`. Sorgenti e tutti i file
dell'output reale copiati prima di qualunque run in
`artifacts/s1-verification/increment59/research-r2/{sources,bin}`;
SHA/size in `research-r2-files.json`. La sola modalità `interop` è stata
eseguita dal binario conservato: non crea un'istanza Inventor.

Il run nativo richiede review del coordinatore e slot COM seriale libero.
Il completamento di una raccolta non chiude parità testo, testo-su-percorso,
default desktop, rendering XR, Quest, prova fisica o alcun gate S1.

## Esiti effettivi del 14 ottobre

La matrice estesa `text59-native-r3.json`, eseguita dai sorgenti/DLL conservati
in `increment59/research-r4/`, comprende **22 casi: 16 PASS e sei FAIL**,
con **22 Abort esatti** su Parte/Lamiera. Il processo dedicato 86380 è
terminato e il processo utente 91056 conserva la stessa ora d’avvio.

| Caso per contesto | Esito |
|---|---|
| Testo fitted, rettangolare/multilinea, modifica letterale | PASS |
| Quattro modalità di collegamento Parameter/Property, due aggiornamenti ciascuna | PASS: otto casi e 16 aggiornamenti complessivi |
| Creazione con stile locale copiato, per nome e per oggetto | PASS: quattro casi complessivi, stile originale preservato |
| Rotazione di 37° assegnata a TextBox.Rotation | FAIL: ArgumentException/E_INVALIDARG |
| Assegnazione dello stile copiato a un TextBox esistente | FAIL: COM/E_FAIL; la creazione con lo stesso stile è un caso distinto |
| Matrice quarti di giro/glifi | FAIL prima della matrice, durante GetFacetsInfo sul campione; nessuna prova delle quattro rotazioni |

`GetFacetsInfo` riesce su altri campioni e restituisce vertici/indici reali:
il fallimento non autorizza a dichiarare tutta l’API indisponibile. Rimane
da isolare il caso specifico e verificare orientamento/topologia dei glifi.
La [documentazione 2027](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/TextBox_GetFacetsInfo.htm)
definisce coordinate XYZ e indici a partire da 1; non dimostra che ogni font
e contenuto siano già collaudati. La
[creazione con stile](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/TextBoxes_AddFitted.htm)
accetta nome o oggetto; la
[proprietà Style](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/TextBox_Style.htm)
è documentata read/write, ma i due rifiuti reali restano registrati.

La persistenza estesa aggiunge un terzo testo collegato a parametro. I primi
confronti fallivano soltanto sul `ComponentIdentifier`: Inventor sostituisce
il nome del documento non salvato con il percorso della copia salvata.
Il run separato **`text59-persistence-r4.json` passa due SaveCopy/riaperture**,
con quattro aggiornamenti reali del parametro nella copia e originale
esattamente preservato. Prima di normalizzare quel solo attributo, la sonda
verifica che identifichi il percorso esatto della copia esclusivamente
posseduta e che Name/Resolved siano corretti; tutte le altre proprietà e i
facets restano nel confronto. Non elimina genericamente campi divergenti.
Sorgenti e DLL eseguiti in `increment59/research-r5/`.

Le copie native generate sono conservate come evidenza, non sono template
né documenti dell’utente. Nessuna installazione o manifest modificato dalle
sonde 59. Il primo snapshot `research-r1` è stato usato solo in modalità
interop; i run COM usano i successivi snapshot con errori sanitizzati.

**NOT COVERED:** prodotto/API batch/UI del comando completo, testo su
geometria, modifica dello stile, orientamento/glifi completi, tutte le
opzioni/default desktop, storico, Quest e prove fisiche. La fondazione
successiva 60 è distinta da questa ricerca e deve superare i propri test.
