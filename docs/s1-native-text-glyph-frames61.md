# S1 — Coordinate dei glifi nativi, incremento 61

14 ottobre 2026. Ricerca API; **18 gate S1 OPEN**. Nessuna capacità XR
abilitata da questa sonda.

`text61-glyphframes-r1.json` conserva 36 catture reali: Parte/Lamiera,
piano XY e piano inclinato di 45° con origine modello (20,30,40) mm,
rotazioni 0/90/180/270°, scale orizzontali 1/1,25 e il campione Rotate59.
Tutte le catture conservano DatabaseRevisionId, Dirty e chiave del testo;
quattro Abort ripristinano lo stato nativo esatto. PID dedicato 99468
terminato, processo preesistente 91056 preservato, manifest/DLL invariati.

Il verificatore indipendente `verify-text-glyph61.ps1` conferma:

- tutti i 36 mesh giacciono sul piano dello schizzo nelle coordinate del
  **modello**, anche sul piano traslato/inclinato;
- 32 rotazioni corrispondono alla trasformazione teorica attorno all’origine
  del testo, proiettando sulla base locale dello schizzo e tornando al modello;
- i vertici includono già rotazione e scala: il renderer non deve applicarle
  nuovamente, né applicare una seconda trasformazione SketchFrame;
- RangeBox resta il rettangolo **non ruotato**. I glifi rientrano in quel
  rettangolo dopo la sola rotazione inversa nel piano, entro 0,00001 mm;
- BoundaryGeometry è vuoto per tutti questi campioni.

Le prime classificazioni booleane della sonda confrontavano i glifi ruotati
con RangeBox non ruotato e tentavano una rotazione XY globale sul piano
inclinato: i loro false non sono fallimenti dei glifi. Il verificatore usa
le coordinate grezze conservate e la trasformazione indipendente corretta.

Rotate59 funziona con array di output inizializzati e rigenerazione completa
prima della cattura. Questo non distingue quale delle due differenze
risolva il precedente fallimento 59: nessuna causa singola viene dichiarata.

Sorgenti e DLL effettivi: `increment61/native-tested-r1/`. È ricerca nativa,
non prova del renderer Unity, di shader stereo, della selezione XR, delle
altre rotazioni/opzioni/font, del testo su geometria o del Quest.
