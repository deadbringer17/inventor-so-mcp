# M9 navigazione per contesto — piano di sviluppo

Data: 4 ottobre 2026, Europe/Rome. Stato: **piano pronto, implementazione da avviare**.

> **Per gli agenti:** eseguire con `superpowers:subagent-driven-development`.
> Per `CLAUDE.md` del repository, in Claude Code il codice lo scrivono
> subagent con `model: "sonnet"`; l'agente principale rilegge ogni diff e
> riesegue i test prima di committare. I passi usano `- [ ]`.

**Obiettivo:** portare il client Quest a un modello guidato dal documento
attivo (contesto Assieme/Parte/Lamiera, pila di navigazione, doppio Trigger,
Torna, schede per contesto, tasti contestuali con legenda, modifica feature da
faccia).

**Architettura:** router di contesto e pila di navigazione **sopra** i
workspace esistenti (`AssemblyWorkspace`, `DesignWorkspace`, `LamieraWorkspace`,
`InspectWorkspace`), che non vengono riscritti. Logica pura nel core
(`Navigation/`, `Input/`, `Ui/ContextTabs`, `Backend/FaceFeature`); Unity e
bridge solo per ciò che richiede l'engine o Inventor.

**Stack:** C# / .NET 8 (core + xUnit), Unity 6000.6.3f1 (URP, Meta XR, TMP),
bridge C# (.NET 8 server, add-in SO 2027 con `INVENTOR2027 && SO_EXPERIMENTAL`).

**Spec (leggere prima di ogni task):**
[`2026-10-04-m9-navigazione-contesto-design.md`](../specs/2026-10-04-m9-navigazione-contesto-design.md).
Leggere anche `CLAUDE.md`, `bridge/CLAUDE.md` e `Inventor XR SO/README.md`.

## Vincoli globali

- Documentazione di prodotto e di milestone in italiano; test e identificatori
  seguono la lingua del file toccato.
- Unità: ogni input/output dei tool in **mm** e gradi (`UnitConvert`).
- `Scenes/Main.unity` è **generata** dal generatore in `Assets/XrSo/Editor/`:
  non modificarla a mano.
- `face_feature` è tier sperimentale (`bridge/CLAUDE.md`, *Experimental tier*)
  finché M9-08 non passa dal vivo.
- Doppio Trigger: due pressioni entro **350 ms**, stesso bersaglio, punta del
  raggio spostata **< 2°**. X tenuto per Torna: **1 s**. Etichette legenda:
  **≤ 12 caratteri**. Rotazione vista: **15°** per scatto.
- Un gate fisico o su Quest non eseguito resta **aperto**; non segnarlo passato
  da test unitari/FakeAddIn. Il runner dichiara «sintetico» l'input simulato.
- Nessun Applica a voce (M5-11); nessun salvataggio implicito su Torna.
- Namespace core: `InventorXrSo.Core.<Area>`; test in
  `Inventor XR SO/Tests~/XrSo.Core.Tests/<Area>/`.
- Comandi test: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"` e
  `dotnet test bridge/tests/Bimwright.Ipt.Tests`. Unity (EditMode, runner):
  da **PowerShell** con `Inventor XR SO/Tools~/Invoke-Unity.ps1`, mai da Bash.
- Branch: `feat/m9-navigazione-contesto`. Commit piccoli, uno per task.

## Sequenza e parallelismo

```
T1 NavigationStack ─┐
T2 ContextRouter ───┤→ T5 AppController → T6 Doppio Trigger → T7 Fantasma → T8 Torna
T3 DoubleTrigger ───┤
T4 DocumentActions ─┘
T9 ContextTabs → T10 Ispeziona/Vista → T11 Anello
T12 InputMap → T13 Dispatcher → T14 Legenda
T15 face_feature bridge → T16 FaceFeature core → T17 Feature UI
T18 Voce → T19 Runner M9 + migrazione M6/M7 → T20 Verbale
```

T1–T3, T9, T12 e T15 sono indipendenti e parallelizzabili. I task Unity
(T4–T8, T10, T11, T13, T14, T17) toccano `AppController`/workspace: in sequenza.

---

## FASE 1 — Navigazione

### Task 1: NavigationStack (core)

**Gate:** M9-01, M9-04.

**Files:**
- Create: `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Navigation/NavigationStack.cs`
- Test: `Inventor XR SO/Tests~/XrSo.Core.Tests/Navigation/NavigationStackTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace InventorXrSo.Core.Navigation
  {
      public enum DocContext { Assembly, Part, SheetMetal }
      public sealed class NavLevel
      {
          public NavLevel(string documentId, DocContext context, string name,
              string fromOccurrenceId = null, float[] occurrencePose = null);
          public string DocumentId { get; }
          public DocContext Context { get; }
          public string Name { get; }
          public string FromOccurrenceId { get; }   // null alla radice
          public float[] OccurrencePose { get; }    // 16 float, posa nell'assieme padre; null alla radice
          public bool Dirty { get; set; }           // «●»
      }
      public sealed class NavigationStack
      {
          public IReadOnlyList<NavLevel> Levels { get; }
          public NavLevel Top { get; }               // null se vuota
          public NavLevel Parent { get; }            // livello sotto la cima, null se Count<2
          public bool CanPop { get; }                // Levels.Count > 1
          public void Reset(NavLevel root);
          public void Push(NavLevel level);
          public NavLevel Pop();                     // InvalidOperationException se !CanPop
          public void PopTo(int index);              // tiene Levels[0..index]
          public string Breadcrumb { get; }          // "Telaio.iam › Staffa.ipt ●"
          public event Action Changed;
      }
  }
  ```

- [ ] **Step 1: test che falliscono**

```csharp
using InventorXrSo.Core.Navigation;

namespace XrSo.Core.Tests.Navigation
{
    public class NavigationStackTests
    {
        private static NavLevel Asm(string id = "a", string name = "Telaio.iam") => new NavLevel(id, DocContext.Assembly, name);
        private static NavLevel Part(string id = "p", string name = "Staffa.ipt") => new NavLevel(id, DocContext.Part, name, "occ1", new float[16]);

        [Fact]
        public void Reset_sets_single_root_and_cannot_pop()
        {
            var s = new NavigationStack();
            s.Reset(Asm());
            Assert.Single(s.Levels);
            Assert.False(s.CanPop);
            Assert.Null(s.Parent);
            Assert.Throws<InvalidOperationException>(() => s.Pop());
        }

        [Fact]
        public void Push_and_pop_track_top_and_parent()
        {
            var s = new NavigationStack(); s.Reset(Asm());
            s.Push(Part());
            Assert.Equal("p", s.Top.DocumentId);
            Assert.Equal("a", s.Parent.DocumentId);
            Assert.Equal("p", s.Pop().DocumentId);
            Assert.Equal("a", s.Top.DocumentId);
        }

        [Fact]
        public void Breadcrumb_marks_dirty_levels()
        {
            var s = new NavigationStack(); s.Reset(Asm()); s.Push(Part());
            Assert.Equal("Telaio.iam › Staffa.ipt", s.Breadcrumb);
            s.Top.Dirty = true;
            Assert.Equal("Telaio.iam › Staffa.ipt ●", s.Breadcrumb);
        }

        [Fact]
        public void PopTo_keeps_prefix_and_Reset_clears_everything()
        {
            var s = new NavigationStack(); s.Reset(Asm());
            s.Push(new NavLevel("s", DocContext.Assembly, "Sub.iam", "o", new float[16]));
            s.Push(Part());
            s.PopTo(0);
            Assert.Single(s.Levels);
            s.Push(Part()); s.Reset(Asm("z", "Altro.iam"));
            Assert.Single(s.Levels); Assert.Equal("z", s.Top.DocumentId);
        }

        [Fact]
        public void Changed_fires_on_every_mutation()
        {
            var s = new NavigationStack(); int n = 0; s.Changed += () => n++;
            s.Reset(Asm()); s.Push(Part()); s.Pop();
            Assert.Equal(3, n);
        }
    }
}
```

- [ ] **Step 2:** `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter NavigationStackTests` → FAIL (tipi mancanti).
- [ ] **Step 3: implementare** `NavigationStack.cs` come da interfaccia (lista interna, `Breadcrumb` = nomi uniti da `" › "`, `" ●"` dopo i livelli `Dirty`, `Changed` su Reset/Push/Pop/PopTo e quando cambia `Dirty` solo se il livello è nella pila — basta notificare nei metodi di mutazione).
- [ ] **Step 4:** stesso comando → PASS.
- [ ] **Step 5:** `git add` dei due file; `git commit -m "feat(xr): M9 NavigationStack"`.

### Task 2: ContextRouter (core)

**Gate:** M9-01.

**Files:**
- Create: `.../Runtime/Navigation/ContextRouter.cs`
- Test: `.../Tests~/XrSo.Core.Tests/Navigation/ContextRouterTests.cs`

**Interfaces:**
- Consumes: `NavigationStack`, `NavLevel`, `DocContext` (T1).
- Produces:
  ```csharp
  public sealed class DocInfo   // documento attivo osservato
  {
      public DocInfo(string documentId, string name, string kind /* "assembly"|"part"|"drawing"|... */, bool isSheetMetal);
      public string DocumentId, Name, Kind; public bool IsSheetMetal;
  }
  public enum RouteChange { None, Reset, Pushed, Popped }
  public sealed class RouteResult { public RouteChange Change; public DocContext? Context; public string Message; }
  public sealed class ContextRouter
  {
      public ContextRouter(NavigationStack stack);
      public static DocContext? ContextOf(DocInfo doc);                 // null se tipo non supportato (disegno ecc.)
      /// Chiamare quando il documento attivo è noto dopo un ingresso richiesto dal client.
      public void ExpectEntry(string documentId, string fromOccurrenceId, float[] pose);
      /// Chiamare a ogni cambio di documento attivo (dal Quest o dal PC).
      public RouteResult OnActiveDocument(DocInfo doc);
  }
  ```
  Regole di `OnActiveDocument`:
  1. pila vuota → `Reset` con radice, `Change=Reset`, senza messaggio;
  2. `doc.DocumentId == Top.DocumentId` → `None` (aggiorna solo `Context` se è cambiato il sottotipo lamiera);
  3. c'è un `ExpectEntry` pendente con lo stesso id → `Push` con occorrenza e posa dichiarate, `Pushed`, e il pendente si consuma;
  4. `doc.DocumentId == Parent.DocumentId` → `Pop`, `Popped`;
  5. altrimenti → `Reset` sul documento, `Message = "Documento cambiato dal PC: <nome>"`.
  Tipo non supportato → `Change=None`, `Context=null`, `Message="Documento non supportato: <nome>"`, pila invariata.

- [ ] **Step 1: test** (uno per regola): radice; stesso id → None; `ExpectEntry` + `OnActiveDocument` → Pushed con `FromOccurrenceId`/posa; id del padre → Popped; id sconosciuto → Reset con messaggio «Documento cambiato dal PC: Staffa.ipt»; `.ipt` con `isSheetMetal` → `DocContext.SheetMetal`; `drawing` → contesto nullo, pila invariata; `ExpectEntry` non consumato se arriva un altro id (poi regola 5 lo scarta).
- [ ] **Step 2:** `--filter ContextRouterTests` → FAIL.
- [ ] **Step 3:** implementare.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(xr): M9 ContextRouter`.

### Task 3: DoubleTriggerDetector (core)

**Gate:** M9-02.

**Files:**
- Create: `.../Runtime/Input/DoubleTriggerDetector.cs`
- Test: `.../Tests~/XrSo.Core.Tests/Input/DoubleTriggerDetectorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace InventorXrSo.Core.Input
  {
      public sealed class DoubleTriggerDetector
      {
          public const double WindowSeconds = 0.350, MaxAngleDegrees = 2.0;
          /// target = id stabile del bersaglio colpito (null se nulla); direction = direzione del raggio (x,y,z).
          /// Restituisce true alla SECONDA pressione valida; poi si riarma (la terza è una prima).
          public bool Press(double time, string target, double dx, double dy, double dz);
          public double PendingProgress(double now);   // 0..1 per l'anellino della legenda, 0 se nessuna prima pressione
          public void Reset();
      }
  }
  ```
  Una pressione con `target == null` azzera e non conta come prima.

- [ ] **Step 1: test** — doppio valido a 0,30 s; a 0,351 s no (la seconda diventa prima); stesso tempo ma bersaglio diverso no; angolo 1,9° sì, 2,1° no (direzioni costruite con `Math.Cos/Sin`); target nullo azzera; dopo un doppio la terza pressione immediata non fa doppio; `PendingProgress` ≈ 0,5 a 175 ms e 0 dopo la finestra.
- [ ] **Step 2:** FAIL. **Step 3:** implementare (angolo = `acos` del prodotto scalare normalizzato, clamp ±1). **Step 4:** PASS. **Step 5:** commit `feat(xr): M9 DoubleTriggerDetector`.

### Task 4: DocumentActions — scheda Documento (Unity, sostituisce SpacesActions)

**Gate:** M9-01.

**Files:**
- Create: `Inventor XR SO/Assets/XrSo/Xr/DocumentActions.cs`
- Modify: `Assets/XrSo/Xr/SpacesActions.cs` (tolto), `Packages/.../Runtime/Ui/ActionCatalog.cs` (`SpacesTab` → `DocumentTab = "documento"`; il parametro `_spaces` diventa `_document`), test `Assets/XrSo/Tests/EditMode/SpacesActionsTests.cs` → `DocumentActionsTests.cs`, `Tests~/XrSo.Core.Tests/Ui/ActionCatalogTests.cs` (adeguare i riferimenti).
- Test: `Assets/XrSo/Tests/EditMode/DocumentActionsTests.cs`

**Interfaces:**
- Consumes: `NavigationStack` (T1), `IInspectionBackend.ListOpenAsync/ActivateOpenAsync` (esistente).
- Produces:
  ```csharp
  public sealed class DocumentActions : IActionProvider
  {
      public DocumentActions(NavigationStack stack, Action goBack, Action save, Action recenter,
          Action calibrateDesk, Action openConnection, Action leaveSession,
          Func<bool> inSession, Func<bool> canNavigate, Func<IReadOnlyList<OpenDocument>> openDocs,
          Action<string> jumpTo);
      // Tab "documento": breadcrumb (un'azione per livello: "doc.crumb.<i>", salta a quel livello),
      // "doc.back", "doc.save", "doc.recenter", "doc.calibrate", "doc.connection", "doc.exit",
      // più "doc.open.<n>" per i documenti aperti (max 6 per pagina).
  }
  ```
  Regola `MaxPalette = 8` rispettata: breadcrumb e documenti aperti usano **tab dinamiche** (`_doc_crumbs`, `_doc_open_<page>`) come già fanno i picker di `AssemblyActions`.

- [ ] **Step 1: test EditMode** — «Torna» disabilitato se `!stack.CanPop` con motivo «Sei già al livello più alto»; «Salva» abilitato solo se `inSession`; ogni azione ha id unico; il numero di azioni per scheda ≤ 8.
- [ ] **Step 2:** eseguire gli EditMode (`Invoke-Unity.ps1`, vedi README) → FAIL.
- [ ] **Step 3:** implementare; `SpacesActions` e il suo test spariscono; `AppController` costruisce `DocumentActions` al posto di `SpacesActions` (collegamento completo in T5).
- [ ] **Step 4:** EditMode + `dotnet test` core → PASS.
- [ ] **Step 5:** commit `feat(xr): M9 scheda Documento al posto di Spazi`.

### Task 5: AppController guidato dal router

**Gate:** M9-01, M9-02.

**Files:**
- Modify: `Assets/XrSo/Xr/AppController.cs`, `Assets/XrSo/Xr/AssemblyWorkspace.cs` (esporre `ActivateDefinitionAsync` come evento `EntryRequested(string documentId, string occurrenceId, float[] pose, bool sheetMetal)` invece di `DesignRequested/LamieraRequested`), `DesignWorkspace.cs`, `LamieraWorkspace.cs` (`Open/Close` invariati).
- Test: `Assets/XrSo/Tests/EditMode/ContextNavigationTests.cs`

**Interfaces:**
- Consumes: `ContextRouter`, `NavigationStack`, `DocInfo` (T1–T2), `DoubleTriggerDetector` (T3), `DocumentActions` (T4).
- Produces in `AppController`: `public NavigationStack Navigation`, `public DocContext? Context`, `public void EnterOccurrence(...)`, `public void GoBack()` (usato da T7 e dal runner).

- [ ] **Step 1: test EditMode** — con workspace finti: documento `.iam` attivo → solo `AssemblyWorkspace.Active`; passaggio a `.ipt` → `DesignWorkspace.Active` e le altre chiuse; `.ipt` lamiera → `LamieraWorkspace`; cambio dal PC non coerente con la pila → pila azzerata e HUD «Documento cambiato dal PC: …».
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** in `OnSceneLoaded` costruire `DocInfo` da `scene.Graph` (`Kind`, `Root.Name`, sottotipo lamiera da `SheetMetalContext`/`LamieraWorkspace.IsPrimary`) e chiamare `router.OnActiveDocument`; il risultato sceglie il workspace con `OpenDesign/OpenAssembly/OpenLamiera` esistenti (che restano privati). Rimuovere `OpenInspection`, `OnLamieraPrimaryChanged` come scelta manuale e i collegamenti `DesignRequested/LamieraRequested` → `EntryRequested` → `router.ExpectEntry` + attivazione. «Isola/Apri» dell'anello usano lo stesso percorso. `CanEnter` condiviso resta.
- [ ] **Step 4:** EditMode e regressioni `AssemblyWorkspaceTests/DesignWorkspaceTests/LamieraWorkspaceTests` → PASS (adeguare solo i riferimenti rinominati).
- [ ] **Step 5:** commit `feat(xr): M9 contesto guidato dal documento attivo`.

### Task 6: Doppio Trigger su componente + guardie

**Gate:** M9-02.

**Files:**
- Modify: `AppController.cs`, `AssemblyWorkspace.cs` (`TryEnterSelected()`), `Xr/ControllerRay.cs` (id del bersaglio e direzione per il detector)
- Test: `Assets/XrSo/Tests/EditMode/DoubleTriggerEntryTests.cs`

**Interfaces:**
- Consumes: `DoubleTriggerDetector.Press`, `AssemblyWorkspace.Editable`, `EntryRequested`.
- Produces: `AssemblyWorkspace.TryEnterSelected(out string blockedReason)`.

- [ ] **Step 1: test** — doppio su componente con workspace `Editable` → `EntryRequested` una volta; durante comando in corso / anteprima / cattura maniglia / `RequiresCadReview` → nessun evento e `HudMessage` con il motivo; su sottoassieme l'HUD ricorda «le modifiche alla definizione riguardano tutte le istanze».
- [ ] **Step 2:** FAIL. **Step 3:** la prima pressione seleziona come oggi; la seconda (detector) chiama `TryEnterSelected`. **Step 4:** PASS. **Step 5:** commit `feat(xr): M9 doppio Trigger su componente`.

### Task 7: Assieme fantasma

**Gate:** M9-03.

**Files:**
- Create: `Assets/XrSo/Runtime/Scene/GhostContext.cs`
- Modify: `AppController.cs` (crea il fantasma all'ingresso, lo distrugge a Torna/Reset), `Runtime/Scene/CadSceneView.cs` (clonare i `CadBody` del livello padre senza collider), `Xr/Workbench.cs` (la posa sollevata considera la posa dell'occorrenza)
- Test: `Assets/XrSo/Tests/EditMode/GhostContextTests.cs`

**Interfaces:**
- Consumes: `NavLevel.OccurrencePose`, `LoadedScene` del padre.
- Produces: `GhostContext.Show(LoadedScene parent, float[] occurrencePose, string revisionLabel)`, `Clear()`, `bool HasSelectableColliders` (deve essere `false`), `string Label` = `"contesto: prima delle modifiche"`.

- [ ] **Step 1: test EditMode** — dopo `Show` nessun `Collider` abilitato sotto il fantasma; materiale con alfa < 1 e ombre disattivate; `Label` esatta; `Clear` lo svuota; con padre oltre 200 definizioni mostra solo le caricate e segnala `Truncated=true`.
- [ ] **Step 2:** FAIL. **Step 3:** implementare con materiale URP trasparente dai token M8 (`UiTheme`), un solo livello (padre diretto). **Step 4:** PASS. **Step 5:** commit `feat(xr): M9 assieme fantasma`.

### Task 8: Torna (X tenuto) e «●»

**Gate:** M9-04.

**Files:**
- Modify: `Xr/Input/XrInput.cs` (evento `BackHeld` a 1 s a riposo, `BackTapped` altrimenti; la catena Indietro invariata se non a riposo), `AppController.GoBack()`, `DocumentActions`, `DesignWorkspace`/`LamieraWorkspace`/`AssemblyWorkspace` (esporre `AtRest` = nessun anello, tastierino, gruppo schede, bozza, isolamento)
- Test: `Assets/XrSo/Tests/EditMode/BackNavigationTests.cs`, aggiornare `XrInputTests.cs`

**Interfaces:** `XrInput.RestingProbe` (`Func<bool>`), eventi `BackHeld`, `BackTapped`; `XrInput.BackHoldSeconds = 1f`.

- [ ] **Step 1: test** — X tenuto 1 s a riposo → `BackHeld` e `GoBack`; tocco breve a riposo → `BackTapped` con HUD «Tieni X per tornare a <padre>»; X non a riposo → catena Indietro (tastierino → anello → gruppo schede → bozza → isolamento) e mai Torna; `GoBack` non salva; livello con modifiche non salvate → `Dirty=true` e breadcrumb con «●»; `GoBack` bloccato se `RequiresCadReview` con motivo.
- [ ] **Step 2:** FAIL. **Step 3:** implementare (attivazione del documento padre via `ActivateOpenAsync`; documento padre chiuso dal PC → pila azzerata sul documento attivo + HUD). **Step 4:** PASS. **Step 5:** commit `feat(xr): M9 Torna con X tenuto`.

---

## FASE 2 — Strumenti per contesto

### Task 9: ContextTabs (core)

**Gate:** M9-05.

**Files:**
- Create: `.../Runtime/Ui/ContextTabs.cs`
- Test: `.../Tests~/XrSo.Core.Tests/Ui/ContextTabsTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class TabState   // cosa è vero adesso
  {
      public bool SketchOpen, FeatureInProgress, FeatureEditOpen;  // schede che compaiono da sole
      public string FeatureName;
  }
  public static class ContextTabs
  {
      public static IReadOnlyList<string> Main(DocContext c, TabState s);       // id schede principali, in ordine
      public static IReadOnlyList<string> InspectGroup(DocContext c);           // gruppo Ispeziona
      public const string Inspect = "ispeziona"; // scheda ▸ che apre il gruppo
  }
  ```
  Tabella attesa (id): Assembly = `componenti, vincoli, ispeziona, vista, documento`;
  Part = `schizzo, feature, parametri, ispeziona, vista, documento`;
  SheetMetal = `lamiera, schizzo, sviluppo, ispeziona, vista, documento`.
  Da sole: `vincoli_schizzo` solo in Part/SheetMetal con `SketchOpen`; `opzioni_feature` con `FeatureInProgress` e in testa; `feature_<nome>` con `FeatureEditOpen` (fase 4). Gruppo: Assembly = `misura, sezione, visibilita, verifica`; Part/SheetMetal = `misura, sezione`.

- [ ] **Step 1: test** — le tre tabelle esatte; nessuna scheda duplicata; `vincoli_schizzo` assente senza schizzo; `opzioni_feature` prima delle altre; `visibilita`/`verifica` assenti in Part/SheetMetal.
- [ ] **Step 2:** FAIL. **Step 3:** implementare. **Step 4:** PASS. **Step 5:** commit `feat(xr): M9 ContextTabs`.

### Task 10: Gruppo Ispeziona, Vista unica, Ispeziona trasversale

**Gate:** M9-05.

**Files:**
- Modify: `Runtime/Ui/Shell/PaletteView.cs` (riuso `ShowTabGroup`; X o «◂» torna alle principali), `Xr/InspectWorkspace.cs` + `InspectActions.cs` (fornitore incluso da ogni contesto; elimina `OtherWorkspaceActive`/`OthersChanged`), `Xr/AssemblyActions.cs`/`DesignActions.cs`/`LamieraActions.cs` (rimuovono le schede Vista duplicate), nuovo `Xr/ViewActions.cs` (Adatta, scala, MR/VR, legenda sì/no con preferenza salvata sul visore via `PlayerPrefs`), `Core/Ui/ActionCatalog.cs` (compone le schede dal contesto con `ContextTabs`)
- Test: `Assets/XrSo/Tests/EditMode/InspectTransversalTests.cs`, `ViewActionsTests.cs`, aggiornare `InspectWorkspaceTests.cs`

- [ ] **Step 1: test** — Misura e Sezione abilitate in Parte e Lamiera durante la modifica; sospese solo con maniglia catturata o revisione CAD; misura/sezione non bloccano la barra di conferma; una sola scheda `vista` per contesto; lo stick sinistro ←/→ scorre le schede del gruppo; X esce dal gruppo.
- [ ] **Step 2:** FAIL. **Step 3:** implementare. **Step 4:** EditMode + regressioni `InspectVerifyTests`, `VerifyOverlayTests` → PASS. **Step 5:** commit `feat(xr): M9 schede per contesto e Ispeziona trasversale`.

### Task 11: Anello per tipo di documento

**Gate:** M9-05.

**Files:**
- Modify: `AssemblyActions.cs`, `DesignActions.cs`, `LamieraActions.cs` (`ContextActions` come da tabella spec §2: Assieme componente → Isola·Sposta·Vincola·Apri; Parte faccia piana → Schizzo·Estrudi·Foro·Misura; Parte bordo → Raccordo·Smusso·Misura; Lamiera faccia → Schizzo·Taglio·Misura; Lamiera bordo → Flangia·Misura; nessuna selezione → vuoto)
- Test: `Tests~/XrSo.Core.Tests/Ui/ActionCatalogTests.cs`, `Assets/XrSo/Tests/EditMode/RingViewTests.cs`

- [ ] **Step 1: test** — per ogni (contesto, `SelectionKind`) l'elenco di id azione dell'anello è esattamente quello della spec, ≤ `MaxContext`.
- [ ] **Step 2–5:** FAIL → implementare → PASS → commit `feat(xr): M9 anello contestuale per documento`.

---

## FASE 3 — Tasti contestuali e legenda

### Task 12: InputMap (core)

**Gate:** M9-06.

**Files:**
- Create: `.../Runtime/Input/InputMap.cs`
- Test: `.../Tests~/XrSo.Core.Tests/Input/InputMapTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum InputState { Rest, ComponentSelected, SketchOpen, ArmedOrHandle, Keypad }   // priorità: Keypad > ArmedOrHandle > SketchOpen > ComponentSelected > Rest
  public enum Key { Trigger, Grip, StickRightH, StickRightV, A, B, TriggerLeft, StickLeftH, StickLeftV, X, Y, TwoGrips }
  public enum InputAction { None, Select, DoubleSelect, Point, Drag, PressKeys, MoveView, RotateView, StepChange, StepSize,
      Isolate, SnapToggle, OpenKeypad, KeypadOk, Speak, Precision, Tabs, Zoom, Suggest, Back, BackHold, KeypadCancel, Fit, TwoHands }
  public sealed class InputBinding { public InputAction Action; public string Label; /* ≤12 car., "" se senza etichetta */ }
  public static class InputMap
  {
      public static InputState Resolve(bool keypad, bool armedOrHandle, bool sketch, bool componentSelected);
      public static InputBinding Lookup(InputState state, Key key);   // null se inattivo
      public static IEnumerable<(Key key, InputBinding binding)> Active(InputState state);
  }
  ```
  Contenuto = tabella spec §3 (colonne `A riposo`, `Componente selezionato`, `Schizzo aperto`, `Chip o maniglia armati`, `Tastierino`), riga per riga; «Y tenuto» **non** esiste (Y = `Fit`).

- [ ] **Step 1: test** — priorità di `Resolve` per tutte le 16 combinazioni; ogni etichetta ≤ 12 caratteri; nessuna `(state,key)` duplicata; `B` = `Speak` in **tutti** gli stati e `TwoGrips`/`Y` presenti in tutti tranne `Keypad`; `X` = `Suggest`/`BackHold` solo a `Rest`, `Back` altrove, `KeypadCancel` in `Keypad`; `A` = `Isolate` in `ComponentSelected`, `SnapToggle` in `SketchOpen`, `OpenKeypad` in `ArmedOrHandle`, `KeypadOk` in `Keypad`, `null` a `Rest`.
- [ ] **Step 2–5:** FAIL → implementare con tabella dichiarativa unica → PASS → commit `feat(xr): M9 InputMap`.

### Task 13: InputDispatcher

**Gate:** M9-06.

**Files:**
- Create: `Assets/XrSo/Xr/Input/InputDispatcher.cs`
- Modify: `Xr/Input/XrInput.cs` (non invoca più azioni dirette: pubblica `KeyPressed(Key)`, `KeyReleased(Key)`, `AxisFlick(Key,int)`; `Recenter` da Y tenuto rimosso), workspace (si iscrivono al dispatcher invece che a `XrInput`)
- Test: `Assets/XrSo/Tests/EditMode/InputDispatcherTests.cs`, aggiornare `XrInputTests.cs`

**Interfaces:**
- Consumes: `InputMap`, `DoubleTriggerDetector`, `XrInput`.
- Produces: `InputDispatcher.StateProbe` (`Func<InputState>`), evento `Action<InputAction, int> Invoked`.

- [ ] **Step 1: test** — con `SyntheticInputSource` ogni riga della tabella produce l'azione attesa nello stato atteso; tasto senza voce non produce nulla; stick destro ←/→ a riposo ruota la vista di ±15° con aptica; `Y` = solo Adatta; doppio Trigger a riposo → `DoubleSelect`.
- [ ] **Step 2–5:** FAIL → implementare → PASS (incluse regressioni M5/M6 `VoiceInputTests`, `WorkspaceVoiceTargetTests`) → commit `feat(xr): M9 InputDispatcher su InputMap`.

### Task 14: ControllerLegend (legenda 3D)

**Gate:** M9-07.

**Files:**
- Create: `Assets/XrSo/Runtime/Ui/Shell/ControllerLegend.cs`
- Modify: `Xr/AppController.cs`, `Editor/` (generatore scena: aggiunge il componente, non toccare `Main.unity` a mano)
- Test: `Assets/XrSo/Tests/EditMode/ControllerLegendTests.cs`

**Interfaces:**
- Consumes: `InputMap.Active(state)`, `DoubleTriggerDetector.PendingProgress`, `XrInput.BackHoldProgress`.
- Produces: `ControllerLegend.SetState(InputState)`, `Enabled` (dalla scheda Vista), `IReadOnlyList<LegendLabel> Labels` (testo, tasto, opacità, progresso).

- [ ] **Step 1: test** — per ogni `InputState` le etichette mostrate = `InputMap.Active` e nient'altro; testo ≤ 12 caratteri; opacità bassa di base e piena con controller entro 25° dall'asse della testa; cambio stato applicato in ≤ 150 ms (nessuna animazione più lunga); anellino di avanzamento su doppio Trigger e X tenuto; `Enabled=false` nasconde tutto e la preferenza resta in `PlayerPrefs`.
- [ ] **Step 2–5:** FAIL → implementare con `UiTheme` M8 → PASS → commit `feat(xr): M9 legenda 3D dei controller`.

---

## FASE 4 — Modifica feature da faccia

### Task 15: `face_feature` nel bridge (sperimentale)

**Gate:** M9-08.

**Files:**
- Create: `bridge/src/shared/Handlers/Experimental/FaceFeatureHandler.cs`
- Modify: `bridge/src/shared/Contracts/CadBatchCommandCatalog.cs` (solo se serve per il wire), `bridge/src/server/Tools/XrTools.cs` (tool `inventor_face_feature`), `bridge/src/server/ToolContracts.cs` (riga con `Tier = Experimental`, `Access = "query"`, `RequiresRevision = true`, `Verification = Pending`, come `inventor_inspect_xr` a riga ~128)
- Test: `bridge/tests/Bimwright.Ipt.Tests/FaceFeatureTests.cs` (FakeAddIn), sonda `bridge/tests/*LiveProbe/` (nuovo caso)

**Interfaces — contratto JSON** (spec §4):
input `{document_id, expected_revision, face_id}`; output
`{feature:{name,type,suppressed,healthy}, parameters:[{name,role,value,unit,expression,editable}], previous_feature}`.
Tipi/ruoli: extrude→distance; revolve→angle; fillet→radius; chamfer→distance; hole→diameter,depth; rectangular/circular pattern→count,spacing(angle); sheet-metal flange→distance,angle. `editable=false` se espressione non semplice, feature soppressa o in errore. Errori `NO_OWNING_FEATURE`, `UNSUPPORTED_FEATURE` (restituisce comunque nome e tipo), `STALE_REVISION`. Sola lettura: nessuna transazione. Unità mm/gradi via `UnitConvert`.

- [ ] **Step 1: test xUnit** — contratto con FakeAddIn: estrusione `d3 = 20 mm` → `value 20`, `editable true`; espressione `d1*2` → `editable false` e `expression` valorizzata; feature soppressa → `editable false`; corpo base → `NO_OWNING_FEATURE`; tipo fuori tabella → `UNSUPPORTED_FEATURE` con nome e tipo; revisione vecchia → `STALE_REVISION`; `ToolContracts` dichiara il tool come Experimental (test di coerenza già esistente deve passare).
- [ ] **Step 2:** `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter FaceFeature` → FAIL.
- [ ] **Step 3:** implementare con `Face.CreatedByFeature`; handler `#if INVENTOR2027 && SO_EXPERIMENTAL` come `InspectXrHandler`; registrare handler e tool.
- [ ] **Step 4:** test bridge → PASS; i test Windows-path noti falliscono solo su Linux (`bridge/CLAUDE.md`).
- [ ] **Step 5:** aggiungere il caso alla sonda live (una chiamata per tipo supportato della fixture) e **non** eseguirla qui: richiede Inventor 2027 → resta un gate aperto M9-08 nel verbale.
- [ ] **Step 6:** commit `feat(bridge): M9 face_feature sperimentale`.

### Task 16: FaceFeature (core)

**Gate:** M9-08, M9-09.

**Files:**
- Create: `.../Runtime/Backend/FaceFeature.cs`, estendere `IInspectionBackend`/`InventorBackend` con `Task<FaceFeatureInfo> GetFaceFeatureAsync(DocumentState state, string faceId, CancellationToken ct)` (tool `inventor_face_feature`)
- Test: `Tests~/XrSo.Core.Tests/Backend/FaceFeatureTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class FeatureParameter { public string Name, Role, Unit, Expression; public double Value; public bool Editable; }
  public sealed class FaceFeatureInfo
  {
      public string FeatureName, FeatureType, PreviousFeature;
      public bool Suppressed, Healthy;
      public IReadOnlyList<FeatureParameter> Parameters;
      public bool CanEdit => !Suppressed && Healthy && Parameters.Any(p => p.Editable);
      public static FaceFeatureInfo FromJson(JObject json);
      // error "UNSUPPORTED_FEATURE" → Parameters vuoto, CanEdit=false, Supported=false
      public bool Supported { get; }
  }
  ```

- [ ] **Step 1: test** — parsing dell'esempio JSON della spec; `editable=false` per espressione; soppressa → `CanEdit=false`; `UNSUPPORTED_FEATURE` → nome/tipo conservati; `NO_OWNING_FEATURE` → eccezione tipizzata già usata dal client per gli errori MCP.
- [ ] **Step 2–5:** FAIL → implementare → PASS → commit `feat(xr): M9 FaceFeature core`.

### Task 17: Scheda «Feature: <nome>» e bozza su `set_parameter`

**Gate:** M9-09.

**Files:**
- Modify: `Xr/DesignWorkspace.cs`, `Xr/LamieraWorkspace.cs`, `Xr/DesignActions.cs`, `Xr/LamieraActions.cs` (scheda `feature_<nome>`, chip per parametro, «Modifica dal desktop» e collegamento alla scheda Parametri per i tipi non supportati, «Feature precedente»), `Runtime/Scene/DesignGeometryView.cs` (evidenzia tutte le facce della feature), manipolatore di distanza lungo la normale e flangia esistenti riusati; `Xr/AssemblyWorkspace.cs` (in Assieme il doppio Trigger su faccia apre il componente: nessuna chiamata `face_feature`)
- Test: `Assets/XrSo/Tests/EditMode/FeatureEditTests.cs`

**Interfaces:**
- Consumes: `FaceFeatureInfo` (T16), la barra di conferma e il percorso `inventor_atomic_batch` + `set_parameter` della scheda Parametri esistente.
- Produces: `DesignWorkspace.OpenFeatureForFace(string faceId)`, `LamieraWorkspace.OpenFeatureForFace(...)`.

- [ ] **Step 1: test (backend finto)** — doppio Trigger su faccia in Parte: chiama `face_feature`, evidenzia le facce, apre la scheda con un chip per parametro; parametro guidato da espressione → chip in sola lettura che mostra l'espressione, mai `set_parameter` su un numero sopra un'espressione; feature soppressa → lettura sì, modifica bloccata con motivo; Anteprima+Applica emettono **un** `inventor_atomic_batch` con N `set_parameter`; `STALE_REVISION` → bozza invalidata come oggi; «Feature precedente» riapre con `previous_feature`; Assieme → nessuna chiamata `face_feature`.
- [ ] **Step 2–5:** FAIL → implementare → PASS (regressioni Design/Lamiera) → commit `feat(xr): M9 modifica feature da faccia`.

---

## Voce, runner, verbale

### Task 18: Voce coerente con il contesto

**Gate:** M9-10.

**Files:**
- Modify: `Packages/.../Runtime/Voice/VoiceCommandBridge.cs`, `VoiceCommandRouter.cs`, `CommandIds.cs`, `Xr/Voice/WorkspaceVoiceTarget.cs`
- Test: `Tests~/XrSo.Core.Tests/Voice/VoiceCommandBridgeTests.cs`, `Assets/XrSo/Tests/EditMode/WorkspaceVoiceTargetTests.cs`

- [ ] **Step 1: test** — i comandi di spazio («vai in progettazione», «lamiera», «assieme», «ispeziona») non eseguono nulla e rispondono con una frase che spiega il nuovo modo di procedere (apri un componente con doppio Trigger o «apri <componente>»); «torna» e «salva» risolvono `doc.back`/`doc.save`; «apri <componente>» risolve la sola azione presente nel contesto; «applica» continua a non eseguire (M5-11); `ActionCatalog` per contesto non risolve azioni di altri contesti.
- [ ] **Step 2–5:** FAIL → implementare → PASS (incluso `VoiceCorpusTests`) → commit `feat(xr): M9 voce per contesto`.

### Task 19: Runner Quest M9 e migrazione M6/M7

**Gate:** M9-01…M9-11.

**Files:**
- Create: `Assets/XrSo/Xr/Acceptance/M9QuestAcceptance.cs`
- Modify: `Xr/Acceptance/QuestAcceptanceRunner.cs` (registra M9, guardia fixture), `M6QuestAcceptance.cs`, `M7QuestAcceptance.cs` (sostituiscono `OpenDesign/OpenAssembly/OpenLamiera` con il doppio Trigger **sintetico**/`EnterOccurrence` e `GoBack`), `Assets/XrSo/Tests/EditMode/QuestAcceptanceContractTests.cs`, `docs/xr-quest-acceptance.md`
- Reference: il pattern dei runner M6/M7/M8 esistenti (`PASS [gate]` / `NOT COVERED [gate]` nel log, manifest, screenshot, cleanup e ripristino documento).

Sottocasi del runner (fixture Inventor **dedicata**, input sintetico dichiarato):
1. doppio Trigger → parte → Torna; 2. sottoassieme a due livelli; 3. schede per contesto (nessuna inerte); 4. ogni riga di `InputMap`; 5. fantasma + etichetta + screenshot; 6. legenda per stato + screenshot; 7. doppio Trigger su faccia → chip → anteprima → Applica → parametro verificato in Inventor → cleanup.

- [ ] **Step 1:** estendere `QuestAcceptanceContractTests` con il contratto M9 (id gate presenti nel log, guardia fixture, nessun `PASS COMPLETE` senza tutti i sottocasi) → FAIL.
- [ ] **Step 2:** implementare `M9QuestAcceptance` e migrare M6/M7 → contratto PASS in EditMode.
- [ ] **Step 3:** **eseguire** i test completi: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`, `dotnet test bridge/tests/Bimwright.Ipt.Tests`, EditMode Unity (PowerShell). Registrare i risultati reali.
- [ ] **Step 4:** il run sul Quest richiede headset, Inventor 2027 e fixture: seguire `docs/xr-quest-acceptance.md` e la memoria «Quest workstation setup». Se l'hardware non è disponibile, **non** dichiararlo passato.
- [ ] **Step 5:** commit `test(xr): M9 runner Quest e migrazione M6/M7`.

### Task 20: Verbale e aggiornamento documenti

**Files:**
- Create: `docs/xr-m9-verification.md` (un esito per M9-01…M9-12: `PASS` con evidenza, `NOT COVERED` con motivo, oppure *aperto*; tre esiti separati unitario / runner sintetico / prova fisica)
- Modify: `CLAUDE.md` (riga M9 nella sezione milestone e nella tabella), `docs/DEVELOPMENT.md` (gate aperti), `Inventor XR SO/README.md` (navigazione e tasti), spec M9 (stato: «implementato, collaudo Quest e prova fisica aperti»), questo piano (spuntare i passi eseguiti)

- [ ] **Step 1:** compilare il verbale con i risultati reali dei task precedenti. M9-08 (sonda live), M9-09 (runner con Inventor reale), M9-12 (prova fisica da seduto) restano **aperti** se non eseguiti.
- [ ] **Step 2:** aggiornare i documenti elencati.
- [ ] **Step 3:** commit `docs(xr): M9 verbale e stato`.

---

## Copertura della spec (auto-revisione)

| Spec | Task |
|---|---|
| §1 pila, router, errori | T1, T2, T5 |
| §1 doppio Trigger, guardie | T3, T6 |
| §1 fantasma | T7 |
| §1 Torna, «●» | T8 |
| §1 scheda Documento | T4 |
| §2 schede, gruppo Ispeziona, Vista unica, trasversale | T9, T10 |
| §2 anello | T11 |
| §2 voce | T18 |
| §3 InputMap, dispatcher, legenda | T12, T13, T14 |
| §4 backend `face_feature` | T15, T16 |
| §4 client | T17 |
| Test e runner | tutti i task + T19 |
| Gate M9-01…M9-12 | T19–T20 (M9-12 solo prova fisica) |

Rischio principale: `AssemblyWorkspace`/`DesignWorkspace`/`LamieraWorkspace` sono
file da 1.100–1.500 righe; T5 e T10 vanno fatti in sequenza e con le suite
M4–M7 verdi a ogni commit. Stima: 10–15 giornate di sviluppo e verifica, più
disponibilità per collaudo fisico (non un impegno di calendario).
