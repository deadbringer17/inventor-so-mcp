# S1 — Riferimenti API ancora da colmare, ricerca 71

14 ottobre 2026. Ricerca documentale; nessuna nuova funzione o gate chiuso.

La [documentazione API Autodesk 2027 di RotateSketchObjects](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/PlanarSketch_RotateSketchObjects.htm)
ammette TextBox nella raccolta di input e specifica un angolo in radianti.
Le dodici osservazioni native64 sul testo fitted XY non hanno invece
prodotto la rotazione dei glifi desiderata. Questa evidenza non dimostra
che tutte le varianti API o il comando desktop abbiano lo stesso limite:
prima di cambiare la guardia servono una soluzione nativa e il confronto
con il comportamento desktop effettivo, inclusi vincoli e orientamento.

La [guida Autodesk 2026 al testo allineato alla geometria](https://help.autodesk.com/cloudhelp/2026/ENU/Inventor-Help/files/GUID-1DD1246B-57D5-4E0A-B4F9-DD63FAD54A32.htm)
descrive un comando distinto su linee, archi e cerchi e opzioni di formato
specifiche. Il percorso del dialogo permette anche parametri/iProperties;
Update e Cancel hanno un comportamento distinto da una singola conferma
finale. Sono dettagli da includere nell'oracle desktop e nelle prove di
rollback, non una dimostrazione di un'API pubblica di creazione.

La scansione dell'interop installato59 non ha trovato la creazione di
GeometryText negli oggetti Sketch/Text esaminati. Le pagine sopra non
forniscono tale API. **Resta aperta** la ricerca di un percorso nativo
equivalente; distribuire glifi manualmente lungo una curva non soddisfa
il requisito di oggetti e dipendenze native S1. Nessuna nuova prova
Inventor, Unity, Quest o fisica in questo documento.
