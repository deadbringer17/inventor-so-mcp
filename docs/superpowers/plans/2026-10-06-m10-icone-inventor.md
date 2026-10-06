# M10 icone Inventor — piano di sviluppo

Data: 6 ottobre 2026, Europe/Rome. Stato: **software e runner consegnati; prova fisica confermata dall'utente; budget GPU aperto**.
Spec obbligatoria: [M10](../specs/2026-10-06-m10-icone-inventor-design.md).
Esiti: [verbale](../../xr-m10-verification.md).

## T1 — Pack locale e mapping (M10-01/02)

- [x] Creare `scripts/export-inventor-icons.ps1`: lettura delle tre DLL .NET,
  ICO originali, PNG, SHA-256, catalogo HTML e ZIP; errori di conversione espliciti.
- [x] Eseguire sulla versione installata e controllare immagini reali.
- [x] Registrare provenienza/limiti in `assets/inventor-icons/README.md`.
- [x] Definire mapping pilota in `assets/inventor-icons/m10-pilot.json` e
  preparare solo le icone selezionate sotto `Assets/XrSo/Ui/Resources/InventorIcons`.

## T2 — Pulsante icona e tooltip (M10-03/04/08)

- [x] Resolver Sprite condiviso con caching, fallback su chiave assente.
- [x] Estendere `UiFactory` con un pulsante `XrAction` e hint su puntamento.
- [x] Integrare `PaletteView` e `RingView`, mantenendo callback e dimensioni.
- [x] Import deterministico Single Sprite, alpha, sRGB, senza compressione lossy.
- [x] Test EditMode: fallback, hover/uscita/disable/rebuild, disabled senza
  invocazione, icona non raycastabile, toggle, risorse condivise e hit area.

## T3 — Pilota Progettazione (M10-05)

- [x] Assegnare `icon:` esplicito alle 11 azioni della spec, senza rinominare id
  o label. Valori, picker e barra di conferma conservano il testo.
- [x] Verificare import/compilazione e core/EditMode con i percorsi esistenti.
- [x] Salvare gallery della UI reale, distinta da screenshot Quest.
- [x] Annotare esiti effettivi e problemi prima dell'estensione.

## T4 — Estensione per contesto (M10-06)

- [x] Inventariare ogni azione statica nei tre workspace rimanenti e Documento/Vista:
  [125 dichiarazioni](../../xr-m10-action-inventory.md), inclusi i campi di modifica feature.
- [x] Verificare corrispondenze semantiche e variante; controllo nativo in lettura
  delle associazioni ambigue Vincola/Modello piatto/Spiega/Ripiega.
- [x] Migrare in gruppi: Lamiera → Assieme → Ispeziona → Documento/Vista.
- [x] Mantenere testo per valori/stati e icone assenti; regressioni per gruppo:
  34 icone/46 azioni, test contro i sei provider reali.

## T5 — Runner, build e collaudo (M10-07/08/09)

- [x] Aggiungere runner `M10QuestAcceptance`, registry/dispatch e wrapper M10.
- [x] Coprire su Quest con input sintetico: tooltip/fallback/toggle/disabled,
  schede/tastierino/anello, voce, preview/applica e invarianti M9 pertinenti.
- [x] Usare fixture dedicata; conservare manifest/log/screenshot e ripristini.
- [x] Profilare risorse dopo 100 rebuild e baseline equivalente; costruire APK.
- [ ] Completare budget GPU: il plugin XR non restituisce campioni nel run
  riuscito del 6 ottobre; CPU/GC e profilo A/B/A conservati nel verbale.
- [x] Prova fisica MR/Studio VR: conferma qualitativa dell'utente il 6 ottobre
  («va bene tutto»), registrata nel verbale; riconoscimento comandi, leggibilità
  tooltip, nitidezza, focus e assenza di occlusione del modello.
- [x] Aggiornare verbale, README, CLAUDE e stato sviluppo; nessun PASS dedotto.

Comandi dalla radice:

```powershell
./scripts/export-inventor-icons.ps1
./scripts/prepare-m10-icons.ps1
python scripts/inventory-m10-actions.py
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-m10-editmode.xml`"" -Log "$env:TEMP\xrso-m10-editmode.log"
```

Prima del batch verificare che questo progetto non sia aperto in Editor.
Modifiche locali di performance/settings già presenti nel checkout sono esterne
a M10 e vanno preservate. Non installare APK o cambiare il documento dell'utente
durante l'estrazione del pack e le verifiche locali della prima consegna.
