# S1 — Ricerca RotateSketchObjects e testo, incremento 64

14 ottobre 2026. **18 gate S1 OPEN**. Ricerca nativa separata dal prodotto.

Dodici casi su Parte/Lamiera confrontano i glifi effettivi prima/dopo
`PlanarSketch.RotateSketchObjects` a 37°, 90° e 180°, attorno a (5,7) mm.
La raccolta contiene solo TextBox oppure TextBox e OriginSketchPoint.
L'oracle ruota indipendentemente ciascun vertice nativo nello spazio locale
dello schizzo e verifica la corrispondenza dei vertici dopo il rebuild.

**0/12 rotazioni equivalenti, 12/12 Abort esatti.** Senza OriginSketchPoint
la chiamata lascia invariati posizione e TextBox.Rotation. Includendo il
punto, l'origine ruota una sola volta attorno al centro richiesto, ma
TextBox.Rotation rimane zero e i glifi traslano senza cambiare orientamento.
Gli errori massimi dell'oracle sono compresi fra circa 11,16 e 36,59 mm.
La chiamata API non genera un'eccezione: è la verifica geometrica a fallire.

`text64-texttransforms64-r1.json` registra **PARTIAL / 12 FAIL**; il processo
sonda ritorna exit1. Questi sono fallimenti della funzione ricercata, non
prove passate. Il wrapper conserva quell'exit nel report di installazione.
Build della sonda: zero warning/errori. Sorgenti e assembly effettivi in
`increment64/native-tested-r1/`; non è stata distribuita una nuova DLL.

Il processo dedicato 91940 è terminato. Manifest/DLL installati e processo
utente 91056 sono preservati. Nessun documento utente è stato aperto dalla
sonda; nessuna APK/Quest/prova fisica. Il blocco produttivo della rotazione
del testo resta attivo. La proprietà TextBox.Rotation ai quarti di giro,
verificata separatamente in 61, non dimostra una rotazione arbitraria o la
correttezza del comando generale.

**NOT COVERED:** soluzione produttiva di rotazione, opzioni desktop e
parità del dialogo, angoli arbitrari tramite altri percorsi API, copia,
testo su geometria, ergonomia e dispositivo. L'oracle qui usa XY e testo
fitted; altri piani e formati restano da verificare per una soluzione.
