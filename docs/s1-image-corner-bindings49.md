# S1 — Riferimenti degli angoli immagine, incremento 49

13 ottobre 2026. Implementazione parziale S1-04/12; **18 gate aperti**.

I nuovi inserimenti gestiti conservano un secondo AttributeSet persistente,
`InventorSO_SketchImageFrame`, con schema 1 e quattro chiavi native: top_left,
top_right, bottom_right, bottom_left. CopyWithOwner è abilitato. La cattura
avviene dopo il dimensionamento iniziale e prima delle rotazioni, verificando
topologia del rettangolo, posizione, larghezza e altezza native.

La lettura associa le chiavi ai punti correnti del bordo di quella precisa
immagine. Non ordina punti per coordinate, non ricostruisce l'orientamento
dal bounding box e non usa gli indici del bordo dopo la cattura. Verifica
identità distinte, appartenenza, chiusura affine, ortogonalità e dimensioni,
con tolleranza 0,00001 mm. Chiavi mancanti/errate o geometria divergente
restano indisponibili; nessuna migrazione automatica delle immagini precedenti.

Il resolver globale `CanBindKeyToObject` rifiuta con E_INVALIDARG le chiavi
esposte dai punti del bordo. Le prime due run native falliscono e sono
conservate con assembly e sorgenti. Spostare la cattura dopo il dimensionamento
non risolve da solo il rifiuto: la causa accertata è la chiamata al resolver.
Il confronto esatto con le chiavi correnti del bordo risolve il caso verificato.

Il reader restituisce `image_frame` e `image_source` senza leggere il buffer
binario completo. I metadati del sorgente sono dichiarazioni di header;
il download verifica separatamente byte, hash e BMP. I due oggetti JSON sono
pubblicati insieme soltanto dopo la loro lettura riuscita, così un errore
non lascia una coppia incompleta che invaliderebbe il parser dell'intero
schizzo. L'immagine resta **supported=false**, snapshot **partial=true**,
con `representation_state=geometry_and_original_source`.

Core introduce `NativeSketchImage` immutabile con quattro angoli e metadati
validati. Mantiene compatibilità con immagini unsupported senza metadati;
non inventa stroke, hit target o texture. Il parser verifica anche ordine
esplicito dei vertici, budget e layout minimo dichiarato del sorgente.

Verifiche:

- **32/32 casi nativi PASS**, Parte/Lamiera × BMP 24/32 × top-down/bottom-up
  × quattro rotazioni. Angoli confrontati con trasformazione teorica;
  spostamento e rotazione ulteriore di 37° verificati in 32 transazioni poi
  abortite, con ripristino esatto del frame.
- **Due SaveCopyAs/riaperture PASS**, sull'ultimo caso di ciascun contesto:
  riferimenti persistenti e angoli conservati, originali invariati anche
  per DatabaseRevisionId/Dirty, copie chiuse e file temporanei rimossi.
- Download e 320 rifiuti della matrice 48 riesercitati con il nuovo frame;
  stato CAD invariato e memoria rilasciata. Istanze dedicate terminate.
- **65 test Core mirati PASS**, **1.046 Core PASS** complessivi, 46 nuovi;
  **1.547 bridge PASS**, zero FAIL e uno SKIP per host già attivo.
- I **32 snapshot nativi effettivi** sono stati analizzati anche dal parser
  Core compilato: metadati coerenti e stato partial conservato.
- **1.049 Unity EditMode PASS**, zero FAIL; build experimental e sonda PASS.
  Due errori iniziali di compilazione della sonda, poi corretti, sono nei log.

Regressione nativa: **96/96 casi PASS**, 48 Parte e 48 Lamiera, con 32 preview,
32 commit e 32 errori dopo Add. Rollback immagini/OLE/revisione esatto;
nessuna risorsa staging o sorgente privata residua e istanze dedicate terminate.

Il frame è una base per le UV del sorgente originale, non prova dei pixel
effettivamente visualizzati: sostituzioni, flip e RotateLeft dal desktop
richiedono ancora verifica. Texture/preview XR, selezione dedicata immagini,
immagini legacy/linked, gestione dei figli del bordo nei filtri di selezione,
endpoint di prodotto e Quest restano aperti. Undo/OLE resta PARTIAL.
Nessuna DLL installata, nuova APK o prova fisica.
