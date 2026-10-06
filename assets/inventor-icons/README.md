# M10 — risorse icone Autodesk Inventor

Le immagini provengono dall'installazione locale di Autodesk Inventor 2027,
non da un pack pubblico con licenza libera. Proprietà Autodesk; nessuna licenza
di redistribuzione verificata. Le licenze MIT/Apache del codice non si applicano
alle immagini. Il pack completo non viene aggiunto al repository.

Estrazione riproducibile dalla radice:

```powershell
./scripts/export-inventor-icons.ps1
# Oppure percorso esplicito su un altro PC:
./scripts/export-inventor-icons.ps1 -InventorBin 'C:\Program Files\Autodesk\Inventor 2027\Bin'
./scripts/prepare-m10-icons.ps1
```

Output locale ignorato da Git: `artifacts/m10-icons/pack/index.html`,
`pack/manifest.json` e `inventor-2027-icons.zip`. Il catalogo si apre anche
senza server, cerca per nome e filtra variante/dimensione. Originali ICO
conservati separatamente dai PNG; ogni immagine e DLL ha SHA-256 nel manifest.
L'estrattore non connette Inventor e non modifica documenti.

Inventario del 6 ottobre 2026: **8.386 PNG** (2.681 colore, 2.853 chiari,
2.852 scuri), originali conservati; due ICO vuoti esclusi e registrati nel
manifest, nessun errore di conversione. Alcune risorse .ico contengono BMP o
PNG: il formato viene riconosciuto dai byte, non dall'estensione. Il pilota
usa la variante **dark**, confrontata visivamente sul navy/teal M8.

`m10-catalog.json` è il mapping definitivo: **34 immagini, 46 azioni** nei sei
fornitori Progettazione, Lamiera, Assieme, Ispeziona, Documento e Vista.
`m10-pilot.json` conserva il pilota iniziale di 11 azioni come riferimento.
Solo quelle selezionate sono importate nel client Unity; conservare il manifest
di provenienza accanto ai PNG. Non cambiare automaticamente una corrispondenza
solo perché una risorsa ha un nome simile. I PNG 32×32 non sono vettoriali.

Il manifest e il NOTICE sono inclusi anche come risorse testuali offline
nell'APK. [Inventario](../../docs/xr-m10-action-inventory.md): ogni dichiarazione
statica indica icona o ragione del testo conservato. I nomi CAD nel comando
Interferenze/Distanza restano visibili; il simbolo appare solo senza il nome.

Verifica in lettura contro `ControlDefinitions` di Inventor 2027:
`AssemblyInsertConstraintCmd` è «Vincolo» (vincolo tra due componenti), non solo
un vincolo di inserimento. `SheetMetalUnfoldCmd` e `SheetMetalRefoldCmd` creano
lavorazioni: le loro icone non vengono usate per il cambio di vista XR.
Crea/mostra sviluppo condividono il simbolo Modello piatto; nascondi/piegato
rimangono testuali. Evidenza locale: `artifacts/m10-icons/control-definitions.json`.

[Spec M10](../../docs/superpowers/specs/2026-10-06-m10-icone-inventor-design.md),
[piano](../../docs/superpowers/plans/2026-10-06-m10-icone-inventor.md),
[esiti](../../docs/xr-m10-verification.md).
