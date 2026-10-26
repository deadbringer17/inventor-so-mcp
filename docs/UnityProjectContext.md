# Contesto Unity — Inventor XR SO

Aggiornamento operativo del 25 ottobre 2026 sul branch `nuova_ux`: F0–F4 sono
implementati ([esiti](xr-quattro-esperienze-verification.md)). Home mantiene
connessione e documento e offre Capture, Disegno, Presentazione e Verifica;
l'authoring segue il contesto documento soltanto in Disegno. È stato aggiunto
OpenXR 1.18.0 per Standalone e il percorso Editor
`Tools~/Invoke-Simulator.ps1`, con Meta XR Simulator 207.1 e HTTPS FakeAddIn.
Android mantiene Oculus. [Verbale del simulatore](xr-meta-simulator-verification.md).
`Tools~/Invoke-SimulatorLive.ps1` estende la prova al CAD nativo: prepara,
ispeziona e ripristina la fixture del runner richiesto (M7 nello smoke); interferenza, distanza, salute e BOM
sono eseguite tramite il workspace Verifica e l'host HTTPS di produzione.
`Tools~/Invoke-SimulatorSuite.ps1` esegue in sequenza i runner migrati M1–M10/QX;
S1 ha una corsa separata e un bootstrap dedicato. Il
[verbale esteso](xr-simulator-full-collaudo.md) distingue esiti e copertura;
la [procedura in CLAUDE.md](../CLAUDE.md#collaudo-autonomo-senza-visore--meta-xr-simulator)
descrive riproduzione, errori di avvio e ripristino dell'add-in temporaneo.
L'analisi statica sottostante descrive il punto di partenza precedente ai lavori.

<!-- unity-onboarding:generated:start -->

Analisi statica: 25 ottobre 2026. Commit: `0e81660c03a00916b2963f784e81e6b100a33fd2`.
Root: `Inventor XR SO/`. Nessun asset o setting modificato, nessun Editor avviato.

## Ambiente confermato

- Unity 6000.6.3f1, revisione 45d8eee7de74; URP 17.6.0, asset pipeline assegnato.
- Quest 3, Android ARM64/IL2CPP; Meta XR Core e MRUK 207.0.0 dalla registry Meta,
  Oculus XR 4.5.5, XR Management 4.7.0, uGUI/TMP, Test Framework 1.8.0.
- Input configurato Both; Input System 1.20.0 transitivo nel lock. L'app usa una
  propria astrazione controller `XrInput`/`InputDispatcher`/`InputMap`, sopra Meta.
- HTTPS/MCP verso il PC, certificato pinnato e pairing persistente; non è multiplayer.
- Core embedded senza Unity; nessun provider Unity MCP rilevato nel manifest/lock
  o tra gli strumenti disponibili. Editor, console e profiler non ispezionati.

Fonti: `ProjectSettings/{ProjectVersion.txt,GraphicsSettings.asset,ProjectSettings.asset}`,
`Packages/{manifest.json,packages-lock.json}`, `README.md`, `Xr/Input/XrInput.cs`.

## Confini e struttura

| Assembly / percorso | Ruolo e dipendenze confermate |
|---|---|
| InventorXrSo.Core / Packages/com.occhipinti.inventorxrso.core/Runtime | Logica C# senza engine: backend, sessione, navigazione, input, cataloghi, capture; Newtonsoft.Json |
| InventorXrSo.Unity / Assets/XrSo/Runtime | Trasporto Unity, mesh, UI e strumenti di scena; Core, UI, TMP |
| InventorXrSo.Xr / Assets/XrSo/Xr | Composizione AppController, workspace, controller, voce, capture; Core, Unity, Oculus, MRUK |
| InventorXrSo.Editor / Assets/XrSo/Editor/Setup | Setup, scena e build; solo Editor, dipende dagli assembly runtime e XR/URP |
| InventorXrSo.Bootstrap / Assets/XrSo/Editor/Bootstrap | Bootstrap Editor separato, senza riferimenti espliciti |
| InventorXrSo.Tests.EditMode / Assets/XrSo/Tests/EditMode | NUnit Editor; Core, Unity, Xr, Editor e test runner |

Shader e geometria sono in Runtime; vault Android e ZXing in Assets/Plugins.
`Tests~/` contiene test .NET Core e host HTTPS FakeAddIn. Scene di produzione:
solo `Assets/XrSo/Scenes/Main.unity`, abilitata nei Build Settings e generata
dagli script Editor; non modificarla manualmente.

## Architettura e avvio

Confermato da `AppController.cs`, `ContextWorkspaceSwitcher.cs`, `ActionCatalog.cs`:
`Start` costruisce la UI, carica il pairing, mostra Home e avvia la connessione.
Home offre MR/Studio VR e Reality Capture. L'ingresso nella sessione CAD apre il
workspace in base al documento attivo: Parte, Lamiera o Assieme. Ispeziona è un
provider condiviso. Reality Capture ha un lifecycle indipendente e sospende il CAD.
`AppController` è il punto di composizione di MonoBehaviour ed eventi; il Core
separa macchine a stati e servizi testabili. Catalogo azioni comune a tavolozza,
anello e voce, ma non tutti i percorsi fisici sono necessariamente filtrati
dal solo catalogo. La nuova suddivisione deve verificare anche quelli.

`CadSceneView` condivide mesh per asset e istanzia le parti per occorrenza.
L'albero semantico non corrisponde necessariamente a una gerarchia Transform:
smontaggio di sottoassiemi richiede selezione esplicita delle foglie discendenti.
`ComponentVisibility` modifica soltanto renderer/collider locali, non Inventor.

Convenzioni osservate: namespace InventorXrSo.*, campi serializzati privati,
campi interni `_camelCase`, classi partial per feature, Task/CancellationToken,
callback su thread Unity, DTO senza COM; documenti prodotto in italiano.

## Verifica e vincoli

Supporto confermato: test .NET Core/FakeAddIn, NUnit EditMode, runner in-app
`XR_SO_ACCEPTANCE`, fixture native Inventor e wrapper Quest con ripristino.
Non rilevata una suite PlayMode dedicata nella struttura ispezionata.
Comandi canonici in `Inventor XR SO/README.md` e `CLAUDE.md`.
Le suite non sono state rieseguite per questa analisi; gli esiti storici sono
nei verbali, distinti da runner sintetici e prove fisiche. RC1–RC4 e S1 hanno
capacità software e gate ancora aperti: un catalogo non equivale a disponibilità.
Il backend usa mm nelle API e cm all'interno di Inventor; GLB in metri.
Conservare guardie su revisione, anteprima, commit incerto e rollback.

## Fonti funzionali aggiuntive e incertezze

Letti anche: `DesignActions.cs`, `DesignParameters.cs`, `DesignNativeSketch.cs`,
`AssemblyActions.cs`, `InspectActions.cs`, `DocumentActions.cs`, `Voice/WorkspaceVoiceTarget.cs`,
`Runtime/Scene/{CadInstance,AssemblyManipulation,ComponentVisibility}.cs`,
`Core/Ui/{ContextTabs,SketchCommandCatalog}.cs`, i verbali M4/M5/M7/M9/RC,
l'audit S1 incremento95 e `bridge/CLAUDE.md`.
Nessuna verifica di binari installati, stato del Quest, qualità delle scansioni,
ergonomia o prestazioni. Le dipendenze risultano dalle configurazioni del repo,
non da una nuova importazione Unity. Pianificazione dettagliata:
[quattro esperienze](superpowers/plans/2026-10-25-quattro-esperienze-xr.md).

<!-- unity-onboarding:generated:end -->
