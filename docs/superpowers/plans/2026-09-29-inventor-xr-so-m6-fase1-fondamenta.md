# M6 Fase 1 — Fondamenta UX spaziale: piano di implementazione

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Creare lo strato comune della UI spaziale M6: catalogo azioni, barra di conferma, inserimento numerico, layout della postazione (core senza Unity), input semantico, aptica, TextMeshPro e `UiShell` con tavolozza sul controller sinistro, barra di conferma e HUD, agganciati all'app con la sola scheda «Spazi».

**Architecture:** La logica sta nel core `InventorXrSo.Core.Ui` (netstandard2.1, testata con xUnit). Unity aggiunge `XrInput` (unico lettore di `OVRInput`, con sorgente sintetica per i test), `Haptics` e `UiShell` (uGUI + TextMeshPro costruiti in codice, come `UiFactory`). I workspace esistenti **non vengono toccati** in questa fase: continuano col loro pannello; la migrazione è nelle fasi 2–4 (piani separati).

**Tech Stack:** C# 9 / netstandard2.1 (core), xUnit (`Tests~/XrSo.Core.Tests`), Unity 6000.6.3f1, uGUI 2.6 con TextMeshPro, Meta XR Core SDK 207 (`OVRInput`), NUnit EditMode.

**Spec:** `docs/superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md`

## Global Constraints

- Core: `noEngineReferences: true`, `LangVersion 9.0`, `TreatWarningsAsErrors`, nessun `record` (netstandard2.1 non ha `IsExternalInit`).
- Nessuna nuova dipendenza di pacchetto (niente Meta Interaction SDK); TextMeshPro è già dentro `com.unity.ugui` 2.6.
- UI costruita in codice, nessun prefab; `Scenes/Main.unity` è generata: non modificarla a mano.
- Testi utente in italiano; commenti del codice come nei file vicini.
- Unità: canvas world-space 1 unità = 1 mm (`UiFactory.WorldCanvas`); layout postazione in metri, Y verso l'alto.
- Testo TextMeshPro: altezza minima 14 mm su piano/barra, 8 mm sulla tavolozza. Bersagli ≥ 25 mm sul piano, ≥ 15 mm sulla tavolozza.
- Tavolozza: massimo 8 azioni per scheda (griglia 2×4), niente «Precedenti/Successivi». Anello: massimo 6 azioni.
- Passi numerici: 10 / 1 / 0,1 (mm o gradi).
- Distanze: piano 0,40 m, box parte 0,40 m, assieme 0,65 m davanti e 0,15 m sotto la testa, box assieme 0,80 m, foglio 0,45×0,30 m, piano di default testa − 0,45 m, scala in [0,001; 10].
- B resta push-to-talk (M5). La voce «Applica» non committa (M5-11).
- Regola `CLAUDE.md`: il codice lo scrivono subagent Sonnet; l'agente principale rilegge il diff e riesegue i test prima di ogni commit.
- **Prerequisito:** il working tree deve essere pulito. Il lavoro M5 non committato (voce, runner) va committato dall'utente prima del Task 1.

## File

| File | Stato | Responsabilità |
|---|---|---|
| `Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/XrAction.cs` | nuovo | `XrActionKind`, `XrTab`, `SelectionKind`, `XrAction` |
| `Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/IActionProvider.cs` | nuovo | contratto dei workspace |
| `Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/ActionCatalog.cs` | nuovo | aggregazione, limiti, ricerca, voce |
| `Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/CommitBarState.cs` | nuovo | stati della barra di conferma |
| `Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/NumericEntry.cs` | nuovo | chip/tastierino/passi |
| `Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/WorkbenchLayout.cs` | nuovo | frame postazione, pose, scale |
| `Tests~/XrSo.Core.Tests/Ui/*.cs` | nuovi | test xUnit dei quattro moduli |
| `Assets/XrSo/Runtime/InventorXrSo.Unity.asmdef` | modifica | riferimento `Unity.TextMeshPro` |
| `Assets/XrSo/Runtime/Ui/UiFactory.cs` | modifica | `Text(...)` TextMeshPro, `TextButton(...)` |
| `Assets/XrSo/Runtime/Ui/UiStyle.cs` | nuovo | colori per stato della barra |
| `Assets/XrSo/Xr/Input/XrInputFrame.cs` | nuovo | snapshot dei controller + sorgenti |
| `Assets/XrSo/Xr/Input/XrInput.cs` | nuovo | eventi semantici |
| `Assets/XrSo/Xr/Input/Haptics.cs` | nuovo | profili aptici |
| `Assets/XrSo/Runtime/Ui/Shell/PaletteView.cs` | nuovo | schede + griglia 2×4 + tastierino |
| `Assets/XrSo/Runtime/Ui/Shell/CommitBarView.cs` | nuovo | barra di conferma |
| `Assets/XrSo/Runtime/Ui/Shell/HudView.cs` | nuovo | HUD (sostituisce `StatusBadge`) |
| `Assets/XrSo/Runtime/Ui/Shell/UiShell.cs` | nuovo | possiede e aggiorna le tre viste |
| `Assets/XrSo/Xr/SpacesActions.cs` | nuovo | scheda «Spazi» |
| `Assets/XrSo/Xr/AppController.cs` | modifica | crea shell, input, HUD, Spazi |
| `Assets/XrSo/Tests/EditMode/*` | nuovi | test EditMode |
| `docs/xr-m6-verification.md` | nuovo | esiti Fase 1 |

Comandi di test usati in tutto il piano (dalla radice del repository):

```bash
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~XrSo.Core.Tests.Ui"
```

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"
```

---

### Task 1: Azioni e catalogo (core)

**Files:**
- Create: `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/XrAction.cs`
- Create: `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/IActionProvider.cs`
- Create: `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/ActionCatalog.cs`
- Test: `Inventor XR SO/Tests~/XrSo.Core.Tests/Ui/ActionCatalogTests.cs`

**Interfaces:**
- Consumes: `InventorXrSo.Core.Voice.ItalianTextNormalizer.Normalize(string)`.
- Produces: `XrAction`, `XrTab`, `XrActionKind`, `SelectionKind`, `IActionProvider`, `ActionCatalog` (con `SpacesTab`, `CommitTab`, `Tabs`, `Palette`, `Context`, `Find`, `TryInvoke`, `ResolveVoice`, `Changed`), `VoiceMatch`, `VoiceMatchKind`, id riservati `CommitIds.Preview/Apply/Cancel/Recover`. `CommitBarState` è definito nel Task 2: in questo task `IActionProvider.CommitBar` usa quel tipo, quindi il Task 1 crea anche uno stub vuoto `public sealed class CommitBarState { }` in `CommitBarState.cs`, che il Task 2 sostituisce.

- [ ] **Step 1: Scrivi i test che falliscono**

`Tests~/XrSo.Core.Tests/Ui/ActionCatalogTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class ActionCatalogTests
    {
        private sealed class Provider : IActionProvider
        {
            public List<XrTab> TabList = new List<XrTab>();
            public List<XrAction> All = new List<XrAction>();
            public Dictionary<SelectionKind, List<XrAction>> Ctx = new Dictionary<SelectionKind, List<XrAction>>();
            public IReadOnlyList<XrTab> Tabs => TabList;
            public IEnumerable<XrAction> Actions => All;
            public IEnumerable<XrAction> ContextActions(SelectionKind s) => Ctx.TryGetValue(s, out var l) ? l : Enumerable.Empty<XrAction>();
            public CommitBarState CommitBar => null;
        }

        private static XrAction A(string id, string label, string tab, bool enabled = true, Action run = null, string[] syn = null, bool voice = true)
            => new XrAction(id, label, tab, () => enabled, run ?? (() => { }), () => "spento", syn, voiceInvokes: voice);

        private static Provider Spaces()
        {
            var p = new Provider();
            p.TabList.Add(new XrTab(ActionCatalog.SpacesTab, "Spazi"));
            p.All.Add(A("spaces.inspect", "Ispeziona", ActionCatalog.SpacesTab));
            return p;
        }

        [Fact]
        public void Tabs_are_active_tabs_then_spaces_and_hide_internal_tabs()
        {
            var design = new Provider();
            design.TabList.Add(new XrTab("sketch", "Schizzo"));
            design.TabList.Add(new XrTab(ActionCatalog.CommitTab, "commit"));
            var c = new ActionCatalog(Spaces());
            c.SetActive(design);
            Assert.Equal(new[] { "sketch", ActionCatalog.SpacesTab }, c.Tabs.Select(t => t.Id));
        }

        [Fact]
        public void Without_active_provider_only_spaces_is_shown()
        {
            var c = new ActionCatalog(Spaces());
            Assert.Equal(new[] { ActionCatalog.SpacesTab }, c.Tabs.Select(t => t.Id));
            Assert.Single(c.Palette(ActionCatalog.SpacesTab));
        }

        [Fact]
        public void Palette_rejects_more_than_eight_actions()
        {
            var p = new Provider();
            p.TabList.Add(new XrTab("t", "T"));
            for (int i = 0; i < 9; i++) p.All.Add(A("a" + i, "Azione " + i, "t"));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            Assert.Throws<InvalidOperationException>(() => c.Palette("t"));
        }

        [Fact]
        public void Context_rejects_more_than_six_actions()
        {
            var p = new Provider();
            p.Ctx[SelectionKind.Edge] = Enumerable.Range(0, 7).Select(i => A("e" + i, "E" + i, "t")).ToList();
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            Assert.Throws<InvalidOperationException>(() => c.Context(SelectionKind.Edge));
        }

        [Fact]
        public void Duplicate_ids_between_provider_and_spaces_are_rejected()
        {
            var p = new Provider();
            p.All.Add(A("spaces.inspect", "Altro", "t"));
            var c = new ActionCatalog(Spaces());
            Assert.Throws<InvalidOperationException>(() => c.SetActive(p));
        }

        [Fact]
        public void TryInvoke_runs_enabled_actions_only()
        {
            int runs = 0;
            var p = new Provider();
            p.All.Add(A("on", "Acceso", "t", true, () => runs++));
            p.All.Add(A("off", "Spento", "t", false, () => runs++));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            Assert.True(c.TryInvoke("on"));
            Assert.False(c.TryInvoke("off"));
            Assert.False(c.TryInvoke("missing"));
            Assert.Equal(1, runs);
        }

        [Fact]
        public void Disabled_action_reports_its_reason()
        {
            var off = A("off", "Spento", "t", false);
            Assert.False(off.Enabled);
            Assert.Equal("spento", off.DisabledReason);
            Assert.Equal("", A("on", "Acceso", "t").DisabledReason);
        }

        [Theory]
        [InlineData("estrusione", VoiceMatchKind.Ok, "design.extrude")]
        [InlineData("Estrudi", VoiceMatchKind.Ok, "design.extrude")]
        [InlineData("raccordo", VoiceMatchKind.Disabled, "design.fillet")]
        [InlineData("flangia", VoiceMatchKind.NotFound, null)]
        public void ResolveVoice_matches_labels_and_synonyms(string phrase, VoiceMatchKind kind, string id)
        {
            var p = new Provider();
            p.All.Add(A("design.extrude", "Estrusione", "t", syn: new[] { "estrudi" }));
            p.All.Add(A("design.fillet", "Raccordo", "t", enabled: false));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            var m = c.ResolveVoice(phrase);
            Assert.Equal(kind, m.Kind);
            Assert.Equal(id, m.Action?.Id);
            if (kind == VoiceMatchKind.Disabled) Assert.Equal("spento", m.Reason);
        }

        [Fact]
        public void ResolveVoice_rejects_two_enabled_actions_with_the_same_name()
        {
            var p = new Provider();
            p.All.Add(A("a", "Estrusione", "t"));
            p.All.Add(A("b", "Estrudi schizzo", "t", syn: new[] { "estrusione" }));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            var m = c.ResolveVoice("estrusione");
            Assert.Equal(VoiceMatchKind.Ambiguous, m.Kind);
            Assert.Equal(new[] { "a", "b" }, m.Conflicts.Select(x => x.Id).OrderBy(x => x));
        }

        [Fact]
        public void Ambiguity_ignores_disabled_duplicates()
        {
            var p = new Provider();
            p.All.Add(A("a", "Estrusione", "t"));
            p.All.Add(A("b", "Estrusione", "u", enabled: false));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            Assert.Equal("a", c.ResolveVoice("estrusione").Action.Id);
        }

        [Fact]
        public void Apply_is_found_by_voice_but_marked_as_not_invokable()
        {
            var p = new Provider();
            p.All.Add(A(CommitIds.Apply, "Applica", ActionCatalog.CommitTab, voice: false));
            var c = new ActionCatalog(Spaces());
            c.SetActive(p);
            var m = c.ResolveVoice("applica");
            Assert.Equal(VoiceMatchKind.Ok, m.Kind);
            Assert.False(m.Action.VoiceInvokes);
        }

        [Fact]
        public void SetActive_and_NotifyChanged_raise_Changed()
        {
            int changes = 0;
            var c = new ActionCatalog(Spaces());
            c.Changed += () => changes++;
            c.SetActive(new Provider());
            c.NotifyChanged();
            c.SetActive(null);
            Assert.Equal(3, changes);
        }

        [Fact]
        public void Constructor_validates_required_fields()
        {
            Assert.Throws<ArgumentException>(() => new XrAction("", "L", "t", () => true, () => { }));
            Assert.Throws<ArgumentException>(() => new XrAction("id", " ", "t", () => true, () => { }));
            Assert.Throws<ArgumentException>(() => new XrAction("id", "L", "", () => true, () => { }));
            Assert.Throws<ArgumentNullException>(() => new XrAction("id", "L", "t", null, () => { }));
        }
    }
}
```

- [ ] **Step 2: Esegui i test e verifica che falliscano**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~XrSo.Core.Tests.Ui"`
Expected: errore di compilazione, `InventorXrSo.Core.Ui` non esiste.

- [ ] **Step 3: Implementa**

`Runtime/Ui/XrAction.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Ui
{
    public enum XrActionKind { Command, Toggle, Numeric }

    /// <summary>Cosa ha selezionato l'utente: decide le azioni dell'anello contestuale.</summary>
    public enum SelectionKind { None, PlanarFace, Face, Edge, Component }

    /// <summary>Scheda della tavolozza. Gli id che iniziano con "_" sono interni e non si mostrano.</summary>
    public sealed class XrTab
    {
        public XrTab(string id, string label)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id scheda vuoto");
            Id = id; Label = label ?? id;
        }
        public string Id { get; }
        public string Label { get; }
        public bool Hidden => Id.StartsWith("_", StringComparison.Ordinal);
    }

    /// <summary>
    /// Un comando dichiarato da un workspace. Tavolozza, anello, barra di conferma, voce e runner Quest
    /// passano tutti da qui: stessa abilitazione, stesso Invoke.
    /// </summary>
    public sealed class XrAction
    {
        private readonly Func<bool> _enabled;
        private readonly Action _invoke;
        private readonly Func<string> _reason;
        private readonly Func<bool> _isOn;

        public XrAction(string id, string label, string tab, Func<bool> enabled, Action invoke,
            Func<string> disabledReason = null, IEnumerable<string> synonyms = null,
            XrActionKind kind = XrActionKind.Command, string icon = null, Func<bool> isOn = null, bool voiceInvokes = true)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id azione vuoto");
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("etichetta vuota: " + id);
            if (string.IsNullOrWhiteSpace(tab)) throw new ArgumentException("scheda vuota: " + id);
            _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            _reason = disabledReason;
            _isOn = isOn;
            Id = id; Label = label; Tab = tab; Kind = kind; Icon = icon ?? "";
            Synonyms = (synonyms ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            VoiceInvokes = voiceInvokes;
        }

        public string Id { get; }
        public string Label { get; }
        public string Tab { get; }
        public string Icon { get; }
        public XrActionKind Kind { get; }
        public IReadOnlyList<string> Synonyms { get; }
        /// <summary>False per "Applica": la voce la trova ma non la esegue (M5-11).</summary>
        public bool VoiceInvokes { get; }
        public bool Enabled => _enabled();
        public bool IsOn => _isOn != null && _isOn();
        public string DisabledReason => Enabled ? "" : (_reason?.Invoke() ?? "");

        public bool TryInvoke()
        {
            if (!Enabled) return false;
            _invoke();
            return true;
        }
    }

    /// <summary>Id riservati della barra di conferma, dichiarati dal workspace nella scheda CommitTab.</summary>
    public static class CommitIds
    {
        public const string Preview = "commit.preview";
        public const string Apply = "commit.apply";
        public const string Cancel = "commit.cancel";
        public const string Recover = "commit.recover";
    }
}
```

`Runtime/Ui/IActionProvider.cs`:

```csharp
using System.Collections.Generic;

namespace InventorXrSo.Core.Ui
{
    /// <summary>Implementato da ogni workspace: dichiara schede, azioni, anello e barra di conferma.</summary>
    public interface IActionProvider
    {
        IReadOnlyList<XrTab> Tabs { get; }
        /// <summary>Tutte le azioni del workspace; ognuna dichiara la sua scheda.</summary>
        IEnumerable<XrAction> Actions { get; }
        IEnumerable<XrAction> ContextActions(SelectionKind selection);
        /// <summary>Null se il workspace non ha una barra di conferma.</summary>
        CommitBarState CommitBar { get; }
    }
}
```

`Runtime/Ui/ActionCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Voice;

namespace InventorXrSo.Core.Ui
{
    public enum VoiceMatchKind { NotFound, Ok, Ambiguous, Disabled }

    public sealed class VoiceMatch
    {
        internal VoiceMatch(VoiceMatchKind kind, XrAction action, IReadOnlyList<XrAction> conflicts, string reason)
        { Kind = kind; Action = action; Conflicts = conflicts; Reason = reason ?? ""; }
        public VoiceMatchKind Kind { get; }
        public XrAction Action { get; }
        public IReadOnlyList<XrAction> Conflicts { get; }
        public string Reason { get; }
    }

    /// <summary>Azioni del workspace attivo piu la scheda fissa "Spazi".</summary>
    public sealed class ActionCatalog
    {
        public const string SpacesTab = "spazi";
        public const string CommitTab = "_commit";
        public const int MaxPalette = 8;
        public const int MaxContext = 6;

        private readonly IActionProvider _spaces;
        private IActionProvider _active;

        public ActionCatalog(IActionProvider spaces) { _spaces = spaces ?? throw new ArgumentNullException(nameof(spaces)); }

        public event Action Changed;
        public IActionProvider Active => _active;

        public void SetActive(IActionProvider provider)
        {
            if (provider != null)
            {
                var ids = provider.Actions.Concat(_spaces.Actions).Select(a => a.Id).ToList();
                var dup = ids.GroupBy(i => i).FirstOrDefault(g => g.Count() > 1);
                if (dup != null) throw new InvalidOperationException("Id azione duplicato: " + dup.Key);
            }
            _active = provider;
            Changed?.Invoke();
        }

        /// <summary>Da chiamare quando il workspace cambia abilitazioni o contenuto.</summary>
        public void NotifyChanged() => Changed?.Invoke();

        public IReadOnlyList<XrTab> Tabs =>
            (_active?.Tabs ?? Array.Empty<XrTab>()).Concat(_spaces.Tabs).Where(t => !t.Hidden).ToArray();

        public IReadOnlyList<XrAction> Palette(string tabId)
        {
            var list = All().Where(a => a.Tab == tabId).ToArray();
            if (list.Length > MaxPalette) throw new InvalidOperationException($"Scheda {tabId}: {list.Length} azioni, massimo {MaxPalette}.");
            return list;
        }

        public IReadOnlyList<XrAction> Context(SelectionKind selection)
        {
            if (_active == null || selection == SelectionKind.None) return Array.Empty<XrAction>();
            var list = _active.ContextActions(selection).ToArray();
            if (list.Length > MaxContext) throw new InvalidOperationException($"Anello {selection}: {list.Length} azioni, massimo {MaxContext}.");
            return list;
        }

        public XrAction Find(string id) => All().FirstOrDefault(a => a.Id == id);

        public bool TryInvoke(string id) => Find(id)?.TryInvoke() == true;

        public VoiceMatch ResolveVoice(string transcript)
        {
            string said = ItalianTextNormalizer.Normalize(transcript);
            if (said.Length == 0) return new VoiceMatch(VoiceMatchKind.NotFound, null, Array.Empty<XrAction>(), "");
            var hits = All().Where(a => Names(a).Contains(said)).ToArray();
            var enabled = hits.Where(a => a.Enabled).ToArray();
            if (enabled.Length == 1) return new VoiceMatch(VoiceMatchKind.Ok, enabled[0], Array.Empty<XrAction>(), "");
            if (enabled.Length > 1) return new VoiceMatch(VoiceMatchKind.Ambiguous, null, enabled, "Comando ambiguo.");
            if (hits.Length > 0) return new VoiceMatch(VoiceMatchKind.Disabled, hits[0], Array.Empty<XrAction>(), hits[0].DisabledReason);
            return new VoiceMatch(VoiceMatchKind.NotFound, null, Array.Empty<XrAction>(), "");
        }

        private IEnumerable<XrAction> All() => (_active?.Actions ?? Enumerable.Empty<XrAction>()).Concat(_spaces.Actions);

        private static HashSet<string> Names(XrAction a) =>
            new HashSet<string>(new[] { a.Label }.Concat(a.Synonyms).Select(ItalianTextNormalizer.Normalize));
    }
}
```

`Runtime/Ui/CommitBarState.cs` (stub, sostituito nel Task 2):

```csharp
namespace InventorXrSo.Core.Ui
{
    public sealed class CommitBarState { }
}
```

Unity richiede un file `.meta` per ogni nuovo file e cartella sotto `Packages/`: Unity li genera all'apertura del progetto; committali insieme ai `.cs` quando esistono (dopo il primo avvio di Unity nel Task 5).

- [ ] **Step 4: Esegui i test e verifica che passino**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~XrSo.Core.Tests.Ui"`
Expected: PASS, 15 test.

- [ ] **Step 5: Commit**

```bash
git add "Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui" "Inventor XR SO/Tests~/XrSo.Core.Tests/Ui"
git commit -m "feat(xr-core): action catalog for the M6 spatial UI"
```

---

### Task 2: Stato della barra di conferma (core)

**Files:**
- Modify: `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/CommitBarState.cs` (sostituisce lo stub)
- Test: `Inventor XR SO/Tests~/XrSo.Core.Tests/Ui/CommitBarStateTests.cs`

**Interfaces:**
- Produces: `CommitBarPhase`, `CommitBarState` con `Phase`, `Message`, `Changed`, `Update(CommitBarInputs, double nowSeconds)`, `MarkApplied(double nowSeconds)`, `Tick(double nowSeconds)`, `CanPreview`, `CanApply`, `CanCancel`, `RecoveryLabel`, `static Derive(CommitBarInputs)`, `AppliedSeconds = 1.5`; `CommitBarInputs` (struct con `Online`, `OutcomeUnknown`, `RefreshRequired`, `Busy`, `HasPreview`, `HasDraft`, `Error`).

- [ ] **Step 1: Scrivi i test che falliscono**

```csharp
using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class CommitBarStateTests
    {
        private static CommitBarInputs In(bool online = true, bool unknown = false, bool refresh = false, bool busy = false,
            bool preview = false, bool draft = false, string error = null)
            => new CommitBarInputs(online, unknown, refresh, busy, preview, draft, error);

        [Theory]
        [InlineData(false, true, true, true, true, true, "x", CommitBarPhase.Offline)]
        [InlineData(true, true, true, true, true, true, "x", CommitBarPhase.Uncertain)]
        [InlineData(true, false, true, true, true, true, "x", CommitBarPhase.Stale)]
        [InlineData(true, false, false, true, true, true, "x", CommitBarPhase.Previewing)]
        [InlineData(true, false, false, false, true, true, "x", CommitBarPhase.Error)]
        [InlineData(true, false, false, false, true, true, null, CommitBarPhase.Ready)]
        [InlineData(true, false, false, false, false, true, null, CommitBarPhase.Draft)]
        [InlineData(true, false, false, false, false, false, null, CommitBarPhase.Empty)]
        public void Derive_follows_the_priority_order(bool online, bool unknown, bool refresh, bool busy, bool preview, bool draft, string error, CommitBarPhase expected)
            => Assert.Equal(expected, CommitBarState.Derive(In(online, unknown, refresh, busy, preview, draft, error)));

        [Fact]
        public void Only_ready_allows_apply_and_only_draft_allows_preview()
        {
            var s = new CommitBarState();
            s.Update(In(draft: true), 0);
            Assert.True(s.CanPreview); Assert.False(s.CanApply); Assert.True(s.CanCancel);
            s.Update(In(preview: true), 0);
            Assert.False(s.CanPreview); Assert.True(s.CanApply); Assert.True(s.CanCancel);
            foreach (var bad in new[] { In(online: false, preview: true), In(unknown: true, preview: true), In(refresh: true, preview: true), In(busy: true, preview: true) })
            {
                s.Update(bad, 0);
                Assert.False(s.CanApply);
            }
        }

        [Fact]
        public void Recovery_label_matches_the_blocking_state()
        {
            var s = new CommitBarState();
            s.Update(In(refresh: true), 0);
            Assert.Equal("Aggiorna documento", s.RecoveryLabel);
            s.Update(In(unknown: true), 0);
            Assert.Equal("Ho controllato il CAD", s.RecoveryLabel);
            s.Update(In(draft: true), 0);
            Assert.Null(s.RecoveryLabel);
        }

        [Fact]
        public void Error_message_is_kept()
        {
            var s = new CommitBarState();
            s.Update(In(error: "Validazione fallita"), 0);
            Assert.Equal(CommitBarPhase.Error, s.Phase);
            Assert.Equal("Validazione fallita", s.Message);
        }

        [Fact]
        public void Applied_lasts_one_and_a_half_seconds_then_empties()
        {
            var s = new CommitBarState();
            s.MarkApplied(10);
            s.Update(In(), 10.5);
            Assert.Equal(CommitBarPhase.Applied, s.Phase);
            s.Tick(11.6);
            Assert.Equal(CommitBarPhase.Empty, s.Phase);
        }

        [Fact]
        public void Blocking_states_override_applied_immediately()
        {
            var s = new CommitBarState();
            s.MarkApplied(10);
            s.Update(In(online: false), 10.1);
            Assert.Equal(CommitBarPhase.Offline, s.Phase);
        }

        [Fact]
        public void Changed_fires_only_on_real_changes()
        {
            int n = 0;
            var s = new CommitBarState();
            s.Changed += () => n++;
            s.Update(In(draft: true), 0);
            s.Update(In(draft: true), 0);
            s.Update(In(preview: true), 0);
            Assert.Equal(2, n);
        }
    }
}
```

- [ ] **Step 2: Esegui e verifica il fallimento**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~XrSo.Core.Tests.Ui.CommitBarStateTests"`
Expected: errore di compilazione (`CommitBarInputs`, `CommitBarPhase` mancanti).

- [ ] **Step 3: Implementa**

```csharp
using System;

namespace InventorXrSo.Core.Ui
{
    public enum CommitBarPhase { Empty, Draft, Previewing, Ready, Stale, Uncertain, Error, Offline, Applied }

    /// <summary>Fatti del workspace da cui si ricava lo stato della barra; le regole restano quelle di M3–M5.</summary>
    public readonly struct CommitBarInputs
    {
        public CommitBarInputs(bool online, bool outcomeUnknown, bool refreshRequired, bool busy, bool hasPreview, bool hasDraft, string error)
        {
            Online = online; OutcomeUnknown = outcomeUnknown; RefreshRequired = refreshRequired; Busy = busy;
            HasPreview = hasPreview; HasDraft = hasDraft; Error = error ?? "";
        }
        public bool Online { get; }
        public bool OutcomeUnknown { get; }
        public bool RefreshRequired { get; }
        public bool Busy { get; }
        public bool HasPreview { get; }
        public bool HasDraft { get; }
        public string Error { get; }
    }

    /// <summary>Barra di conferma unica: l'unico posto da cui una modifica entra nel CAD.</summary>
    public sealed class CommitBarState
    {
        public const double AppliedSeconds = 1.5;
        private double _appliedAt = double.NegativeInfinity;

        public CommitBarPhase Phase { get; private set; } = CommitBarPhase.Empty;
        public string Message { get; private set; } = "";
        public event Action Changed;

        public bool CanPreview => Phase == CommitBarPhase.Draft;
        public bool CanApply => Phase == CommitBarPhase.Ready;
        public bool CanCancel => Phase == CommitBarPhase.Draft || Phase == CommitBarPhase.Ready || Phase == CommitBarPhase.Error;
        public string RecoveryLabel => Phase == CommitBarPhase.Stale ? "Aggiorna documento"
            : Phase == CommitBarPhase.Uncertain ? "Ho controllato il CAD" : null;

        public static CommitBarPhase Derive(CommitBarInputs i)
        {
            if (!i.Online) return CommitBarPhase.Offline;
            if (i.OutcomeUnknown) return CommitBarPhase.Uncertain;
            if (i.RefreshRequired) return CommitBarPhase.Stale;
            if (i.Busy) return CommitBarPhase.Previewing;
            if (i.Error.Length > 0) return CommitBarPhase.Error;
            if (i.HasPreview) return CommitBarPhase.Ready;
            if (i.HasDraft) return CommitBarPhase.Draft;
            return CommitBarPhase.Empty;
        }

        public void Update(CommitBarInputs inputs, double nowSeconds)
        {
            var phase = Derive(inputs);
            if (phase == CommitBarPhase.Empty && nowSeconds - _appliedAt < AppliedSeconds) phase = CommitBarPhase.Applied;
            Set(phase, phase == CommitBarPhase.Error ? inputs.Error : "");
        }

        public void MarkApplied(double nowSeconds)
        {
            _appliedAt = nowSeconds;
            Set(CommitBarPhase.Applied, "");
        }

        public void Tick(double nowSeconds)
        {
            if (Phase == CommitBarPhase.Applied && nowSeconds - _appliedAt >= AppliedSeconds) Set(CommitBarPhase.Empty, "");
        }

        private void Set(CommitBarPhase phase, string message)
        {
            if (phase == Phase && message == Message) return;
            Phase = phase; Message = message;
            Changed?.Invoke();
        }
    }
}
```

- [ ] **Step 4: Esegui e verifica che passi**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~XrSo.Core.Tests.Ui"`
Expected: PASS (Task 1 + Task 2).

- [ ] **Step 5: Commit**

```bash
git add "Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/CommitBarState.cs" "Inventor XR SO/Tests~/XrSo.Core.Tests/Ui/CommitBarStateTests.cs"
git commit -m "feat(xr-core): single commit bar state"
```

---

### Task 3: Inserimento numerico (core)

**Files:**
- Create: `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/NumericEntry.cs`
- Test: `Inventor XR SO/Tests~/XrSo.Core.Tests/Ui/NumericEntryTests.cs`

**Interfaces:**
- Consumes: `InventorXrSo.Core.Voice.ItalianNumberParser.Parse(string text, double min, double max)`, `QuantityUnit`.
- Produces: `NumericEntry(string id, QuantityUnit unit, double value, double min, double max)` con `Id`, `Unit`, `Value`, `Min`, `Max`, `Step`, `Steps` (`{0.1, 1, 10}`), `Editing`, `Buffer`, `Display`, `Changed`, `CycleStep(int)`, `Nudge(int)`, `BeginEdit()`, `Type(char)`, `Backspace()`, `Commit(out string reason)`, `CancelEdit()`, `SetValue(double, out string reason)`, `static Format(double)`.

- [ ] **Step 1: Scrivi i test che falliscono**

```csharp
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;

namespace XrSo.Core.Tests.Ui
{
    public class NumericEntryTests
    {
        private static NumericEntry Mm(double v = 20) => new NumericEntry("flange.height", QuantityUnit.Millimeters, v, 0.01, 1000);

        [Fact]
        public void Default_step_is_one_and_cycles_between_tenth_and_ten()
        {
            var e = Mm();
            Assert.Equal(1, e.Step);
            e.CycleStep(+1); Assert.Equal(10, e.Step);
            e.CycleStep(+1); Assert.Equal(10, e.Step);
            e.CycleStep(-1); e.CycleStep(-1); Assert.Equal(0.1, e.Step);
            e.CycleStep(-1); Assert.Equal(0.1, e.Step);
        }

        [Fact]
        public void Nudge_moves_by_step_and_clamps()
        {
            var e = Mm(999.5);
            Assert.True(e.Nudge(+1));
            Assert.Equal(1000, e.Value);
            Assert.False(e.Nudge(+1));
            e.CycleStep(-1);
            var small = Mm(0.05);
            small.CycleStep(-1);
            Assert.True(small.Nudge(-1));
            Assert.Equal(0.01, small.Value, 9);
        }

        [Fact]
        public void Nudge_avoids_floating_point_drift()
        {
            var e = Mm(0);
            e.CycleStep(-1);
            var start = new NumericEntry("x", QuantityUnit.Millimeters, 0.1, 0, 10);
            start.CycleStep(-1);
            start.Nudge(+1); start.Nudge(+1);
            Assert.Equal(0.3, start.Value, 12);
        }

        [Fact]
        public void Keypad_builds_a_comma_decimal_and_commits()
        {
            var e = Mm();
            e.BeginEdit();
            foreach (var c in "12.35") e.Type(c);
            Assert.Equal("12,35", e.Buffer);
            Assert.True(e.Commit(out var reason), reason);
            Assert.Equal(12.35, e.Value, 9);
            Assert.False(e.Editing);
        }

        [Fact]
        public void Keypad_ignores_a_second_separator_and_toggles_sign()
        {
            var e = new NumericEntry("a", QuantityUnit.Degrees, 0, -180, 180);
            e.BeginEdit();
            foreach (var c in "4,5,6") e.Type(c);
            Assert.Equal("4,56", e.Buffer);
            e.Type('-');
            Assert.Equal("-4,56", e.Buffer);
            e.Type('-');
            Assert.Equal("4,56", e.Buffer);
            e.Backspace();
            Assert.Equal("4,5", e.Buffer);
        }

        [Fact]
        public void Out_of_range_commit_is_refused_and_keeps_editing()
        {
            var e = Mm();
            e.BeginEdit();
            foreach (var c in "5000") e.Type(c);
            Assert.False(e.Commit(out var reason));
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.True(e.Editing);
            Assert.Equal(20, e.Value);
        }

        [Fact]
        public void Empty_commit_is_refused()
        {
            var e = Mm();
            e.BeginEdit();
            Assert.False(e.Commit(out _));
        }

        [Fact]
        public void Cancel_restores_the_previous_display()
        {
            var e = Mm();
            e.BeginEdit();
            e.Type('7');
            e.CancelEdit();
            Assert.False(e.Editing);
            Assert.Equal("20 mm", e.Display);
        }

        [Fact]
        public void Display_uses_comma_and_unit()
        {
            Assert.Equal("12,5 mm", Mm(12.5).Display);
            Assert.Equal("90°", new NumericEntry("a", QuantityUnit.Degrees, 90, 0, 180).Display);
            var e = Mm(); e.BeginEdit(); e.Type('3');
            Assert.Equal("3", e.Display);
        }

        [Fact]
        public void SetValue_checks_the_range()
        {
            var e = Mm();
            Assert.True(e.SetValue(33.3, out _));
            Assert.Equal(33.3, e.Value);
            Assert.False(e.SetValue(-1, out var reason));
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Fact]
        public void Changed_fires_on_every_visible_change()
        {
            int n = 0;
            var e = Mm();
            e.Changed += () => n++;
            e.Nudge(+1); e.CycleStep(+1); e.BeginEdit(); e.Type('1'); e.Backspace(); e.CancelEdit();
            Assert.Equal(6, n);
        }
    }
}
```

- [ ] **Step 2: Esegui e verifica il fallimento**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~XrSo.Core.Tests.Ui.NumericEntryTests"`
Expected: errore di compilazione (`NumericEntry` mancante).

- [ ] **Step 3: Implementa**

```csharp
using System;
using System.Globalization;
using InventorXrSo.Core.Voice;

namespace InventorXrSo.Core.Ui
{
    /// <summary>
    /// Il valore di un chip: passi del thumbstick, buffer del tastierino in tavolozza e dettatura.
    /// Cambia solo la bozza del workspace; nel CAD entra con Anteprima e Applica.
    /// </summary>
    public sealed class NumericEntry
    {
        public static readonly double[] Steps = { 0.1, 1, 10 };
        private int _step = 1;

        public NumericEntry(string id, QuantityUnit unit, double value, double min, double max)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id campo vuoto");
            if (double.IsNaN(min) || double.IsNaN(max) || min > max) throw new ArgumentException("intervallo non valido");
            Id = id; Unit = unit; Min = min; Max = max;
            Value = Math.Max(min, Math.Min(max, value));
        }

        public string Id { get; }
        public QuantityUnit Unit { get; }
        public double Min { get; }
        public double Max { get; }
        public double Value { get; private set; }
        public double Step => Steps[_step];
        public bool Editing => Buffer != null;
        public string Buffer { get; private set; }
        public event Action Changed;

        public string Display => Editing ? Buffer : Format(Value) + (Unit == QuantityUnit.Degrees ? "°" : Unit == QuantityUnit.Millimeters ? " mm" : "");

        public static string Format(double v) => v.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',');

        public void CycleStep(int direction)
        {
            int next = Math.Max(0, Math.Min(Steps.Length - 1, _step + Math.Sign(direction)));
            if (next == _step) return;
            _step = next;
            Changed?.Invoke();
        }

        /// <summary>Un passo nella direzione data; false se il valore era gia al limite.</summary>
        public bool Nudge(int direction)
        {
            double next = Math.Round(Value + Math.Sign(direction) * Step, 6);
            next = Math.Max(Min, Math.Min(Max, next));
            if (next == Value) return false;
            Value = next;
            Changed?.Invoke();
            return true;
        }

        public void BeginEdit() { Buffer = ""; Changed?.Invoke(); }

        public void Type(char c)
        {
            if (!Editing) return;
            if (c == '-') Buffer = Buffer.StartsWith("-", StringComparison.Ordinal) ? Buffer.Substring(1) : "-" + Buffer;
            else if (c == ',' || c == '.') { if (Buffer.IndexOf(',') >= 0) return; Buffer += ","; }
            else if (c >= '0' && c <= '9') Buffer += c;
            else return;
            Changed?.Invoke();
        }

        public void Backspace()
        {
            if (!Editing || Buffer.Length == 0) return;
            Buffer = Buffer.Substring(0, Buffer.Length - 1);
            Changed?.Invoke();
        }

        public bool Commit(out string reason)
        {
            reason = "";
            if (!Editing) return false;
            var parsed = ItalianNumberParser.Parse(Buffer, Min, Max);
            if (!parsed.Ok) { reason = parsed.Reason; return false; }
            Value = parsed.Value;
            Buffer = null;
            Changed?.Invoke();
            return true;
        }

        public void CancelEdit()
        {
            if (!Editing) return;
            Buffer = null;
            Changed?.Invoke();
        }

        /// <summary>Stesso controllo del tastierino, per dettatura e trascinamento.</summary>
        public bool SetValue(double value, out string reason)
        {
            reason = "";
            if (double.IsNaN(value) || double.IsInfinity(value) || value < Min || value > Max)
            {
                reason = $"Fuori intervallo {Format(Min)}–{Format(Max)}.";
                return false;
            }
            if (value == Value) return true;
            Value = value;
            Changed?.Invoke();
            return true;
        }
    }
}
```

Nota: `ItalianNumberParser.Parse("12,35", ...)` accetta la virgola decimale (vedi `NumberParserTests`). Se un test del tastierino fallisce su un caso del parser, non modificare il parser: riporta il caso all'agente principale.

- [ ] **Step 4: Esegui e verifica che passi**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~XrSo.Core.Tests.Ui"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/NumericEntry.cs" "Inventor XR SO/Tests~/XrSo.Core.Tests/Ui/NumericEntryTests.cs"
git commit -m "feat(xr-core): numeric entry for value chips and palette keypad"
```

---

### Task 4: Layout della postazione (core)

**Files:**
- Create: `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/WorkbenchLayout.cs`
- Test: `Inventor XR SO/Tests~/XrSo.Core.Tests/Ui/WorkbenchLayoutTests.cs`

**Interfaces:**
- Consumes: `InventorXrSo.Core.Backend.CadPoint` (usato come vettore in metri; X destra, Y su, Z avanti come in Unity).
- Produces: `WorkbenchFrame.FromHead(CadPoint headPosition, double headYawDegrees, double? calibratedDeskY)` con `Origin`, `YawDegrees`, `DeskY`, `Forward`, `Right`; `LayoutPose` (`Position`, `YawDegrees`, `Scale`, `Clamped`); `WorkbenchLayout.Part(frame, extentM)`, `Assembly(frame, extentM)`, `Isolated(frame, componentPosition, componentScale)`, `SheetScale(widthM, depthM)`, `CommitBarPosition(frame)`, `FitScale(extentM, boxM, out bool clamped)` e le costanti.

- [ ] **Step 1: Scrivi i test che falliscono**

```csharp
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class WorkbenchLayoutTests
    {
        private static readonly WorkbenchFrame Front = WorkbenchFrame.FromHead(new CadPoint(0, 1.2, 0), 0, null);

        private static void Near(CadPoint expected, CadPoint actual)
        {
            Assert.Equal(expected.X, actual.X, 6);
            Assert.Equal(expected.Y, actual.Y, 6);
            Assert.Equal(expected.Z, actual.Z, 6);
        }

        [Fact]
        public void Default_desk_is_45_cm_below_the_head_and_calibration_wins()
        {
            Assert.Equal(0.75, Front.DeskY, 6);
            Assert.Equal(0.72, WorkbenchFrame.FromHead(new CadPoint(0, 1.2, 0), 0, 0.72).DeskY, 6);
        }

        [Fact]
        public void Yaw_rotates_forward_and_right_around_the_vertical()
        {
            var f = WorkbenchFrame.FromHead(new CadPoint(0, 1.2, 0), 90, null);
            Near(new CadPoint(1, 0, 0), f.Forward);
            Near(new CadPoint(0, 0, -1), f.Right);
        }

        [Fact]
        public void Part_sits_on_the_desk_40_cm_ahead_scaled_into_a_40_cm_box()
        {
            var p = WorkbenchLayout.Part(Front, 0.2);
            Near(new CadPoint(0, 0.75, 0.40), p.Position);
            Assert.Equal(2.0, p.Scale, 6);
            Assert.False(p.Clamped);
        }

        [Fact]
        public void Assembly_is_raised_65_cm_ahead_and_fits_80_cm()
        {
            var p = WorkbenchLayout.Assembly(Front, 1.6);
            Near(new CadPoint(0, 1.05, 0.65), p.Position);
            Assert.Equal(0.5, p.Scale, 6);
        }

        [Fact]
        public void Scale_is_clamped_and_reported()
        {
            var tiny = WorkbenchLayout.Part(Front, 0.00001);
            Assert.Equal(WorkbenchLayout.MaxScale, tiny.Scale);
            Assert.True(tiny.Clamped);
            var huge = WorkbenchLayout.Part(Front, 1000);
            Assert.Equal(WorkbenchLayout.MinScale, huge.Scale);
            Assert.True(huge.Clamped);
            Assert.Equal(1, WorkbenchLayout.Part(Front, 0).Scale);
        }

        [Fact]
        public void Isolated_component_moves_halfway_to_the_user()
        {
            var p = WorkbenchLayout.Isolated(Front, new CadPoint(0.2, 1.0, 0.8), 0.5);
            Near(new CadPoint(0.1, 1.1, 0.4), p.Position);
            Assert.Equal(0.5, p.Scale);
        }

        [Fact]
        public void Sheet_scale_fits_the_sketch_into_45_by_30_cm()
        {
            Assert.Equal(1.5, WorkbenchLayout.SheetScale(0.3, 0.1), 6);
            Assert.Equal(1.0, WorkbenchLayout.SheetScale(0.45, 0.3), 6);
            Assert.Equal(0.5, WorkbenchLayout.SheetScale(0.2, 0.6), 6);
        }

        [Fact]
        public void Commit_bar_is_on_the_near_edge_of_the_desk()
            => Near(new CadPoint(0, 0.75, 0.20), WorkbenchLayout.CommitBarPosition(Front));
    }
}
```

- [ ] **Step 2: Esegui e verifica il fallimento**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~XrSo.Core.Tests.Ui.WorkbenchLayoutTests"`
Expected: errore di compilazione.

- [ ] **Step 3: Implementa**

```csharp
using System;
using InventorXrSo.Core.Backend;

namespace InventorXrSo.Core.Ui
{
    /// <summary>Frame della postazione seduta: posizione e yaw della testa al Ricentra, altezza del piano.</summary>
    public sealed class WorkbenchFrame
    {
        public const double DefaultDeskDrop = 0.45;

        private WorkbenchFrame(CadPoint origin, double yaw, double deskY) { Origin = origin; YawDegrees = yaw; DeskY = deskY; }

        public CadPoint Origin { get; }
        public double YawDegrees { get; }
        public double DeskY { get; }
        public CadPoint Forward { get { double r = YawDegrees * Math.PI / 180; return new CadPoint(Math.Sin(r), 0, Math.Cos(r)); } }
        public CadPoint Right { get { double r = YawDegrees * Math.PI / 180; return new CadPoint(Math.Cos(r), 0, -Math.Sin(r)); } }

        /// <summary>Metri, Y verso l'alto. Pitch e roll della testa sono ignorati di proposito.</summary>
        public static WorkbenchFrame FromHead(CadPoint headPosition, double headYawDegrees, double? calibratedDeskY)
            => new WorkbenchFrame(headPosition, headYawDegrees, calibratedDeskY ?? headPosition.Y - DefaultDeskDrop);
    }

    public readonly struct LayoutPose
    {
        public LayoutPose(CadPoint position, double yawDegrees, double scale, bool clamped)
        { Position = position; YawDegrees = yawDegrees; Scale = scale; Clamped = clamped; }
        public CadPoint Position { get; }
        public double YawDegrees { get; }
        public double Scale { get; }
        /// <summary>True se la scala ideale era fuori da [MinScale, MaxScale]: l'HUD mostra la scala.</summary>
        public bool Clamped { get; }
    }

    /// <summary>Pose di piano, foglio, assieme e isolamento (spec M6, disposizione spaziale).</summary>
    public static class WorkbenchLayout
    {
        public const double PartDistance = 0.40, PartBox = 0.40;
        public const double AssemblyDistance = 0.65, AssemblyDrop = 0.15, AssemblyBox = 0.80;
        public const double SheetWidth = 0.45, SheetDepth = 0.30;
        public const double MinScale = 0.001, MaxScale = 10;

        public static double FitScale(double extentM, double boxM, out bool clamped)
        {
            clamped = false;
            if (!(extentM > 0)) return 1;
            double raw = boxM / extentM;
            double s = Math.Max(MinScale, Math.Min(MaxScale, raw));
            clamped = s != raw;
            return s;
        }

        public static LayoutPose Part(WorkbenchFrame f, double extentM)
        {
            double s = FitScale(extentM, PartBox, out bool c);
            return new LayoutPose(OnFloor(f, PartDistance, f.DeskY), f.YawDegrees, s, c);
        }

        public static LayoutPose Assembly(WorkbenchFrame f, double extentM)
        {
            double s = FitScale(extentM, AssemblyBox, out bool c);
            return new LayoutPose(OnFloor(f, AssemblyDistance, f.Origin.Y - AssemblyDrop), f.YawDegrees, s, c);
        }

        /// <summary>Solo visivo: il componente avanza a meta strada verso la testa, stessa scala dell'assieme.</summary>
        public static LayoutPose Isolated(WorkbenchFrame f, CadPoint componentPosition, double componentScale)
            => new LayoutPose((componentPosition + f.Origin) * 0.5, f.YawDegrees, componentScale, false);

        public static double SheetScale(double widthM, double depthM)
        {
            if (!(widthM > 0) || !(depthM > 0)) return 1;
            return Math.Max(MinScale, Math.Min(MaxScale, Math.Min(SheetWidth / widthM, SheetDepth / depthM)));
        }

        public static CadPoint CommitBarPosition(WorkbenchFrame f) => OnFloor(f, PartDistance - SheetDepth / 2 - 0.05, f.DeskY);

        private static CadPoint OnFloor(WorkbenchFrame f, double distance, double y)
        {
            var p = f.Origin + f.Forward * distance;
            return new CadPoint(p.X, y, p.Z);
        }
    }
}
```

Il bordo vicino del piano è a 0,40 − 0,15 − 0,05 = 0,20 m dalla testa: il foglio ha profondità 0,30 m e la barra resta 5 cm oltre il suo bordo.

- [ ] **Step 4: Esegui e verifica che passi**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~XrSo.Core.Tests.Ui"`
Expected: PASS. Poi l'intera suite core, senza regressioni:
Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`
Expected: tutti PASS (375 esistenti + nuovi).

- [ ] **Step 5: Commit**

```bash
git add "Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui/WorkbenchLayout.cs" "Inventor XR SO/Tests~/XrSo.Core.Tests/Ui/WorkbenchLayoutTests.cs"
git commit -m "feat(xr-core): seated workbench layout"
```

---

### Task 5: TextMeshPro in `UiFactory` e stili di stato (Unity)

**Files:**
- Modify: `Inventor XR SO/Assets/XrSo/Runtime/InventorXrSo.Unity.asmdef`
- Modify: `Inventor XR SO/Assets/XrSo/Runtime/Ui/UiFactory.cs`
- Create: `Inventor XR SO/Assets/XrSo/Runtime/Ui/UiStyle.cs`
- Create (se mancano): `Inventor XR SO/Assets/TextMesh Pro/` (TMP Essential Resources)
- Test: `Inventor XR SO/Assets/XrSo/Tests/EditMode/UiFactoryTmpTests.cs`
- Modify: `Inventor XR SO/Assets/XrSo/Tests/EditMode/InventorXrSo.Tests.EditMode.asmdef` (riferimento `Unity.TextMeshPro`)

**Interfaces:**
- Consumes: `CommitBarPhase` (Task 2).
- Produces: `UiFactory.Text(Transform parent, string text, float capHeightMm, FontStyles style = FontStyles.Normal) : TextMeshProUGUI`; `UiFactory.TextButton(Transform parent, string text, Color color, float capHeightMm, Action onClick) : Button`; `UiFactory.ClearChildren(Transform parent)` (distrugge i figli in ordine inverso, `DestroyImmediate` in EditMode, `SetActive(false)` + `Destroy` in Play); `UiStyle.For(CommitBarPhase) : Color`; `UiStyle.Ghost`, `UiStyle.Hover`, `UiStyle.Disabled`. `UiFactory.Label` e `UiFactory.Button` legacy **restano** per i workspace non ancora migrati.

- [ ] **Step 1: Importa le risorse TextMeshPro essenziali**

Verifica se esiste `Inventor XR SO/Assets/TextMesh Pro/Resources/TMP Settings.asset`. Se manca, crea `Assets/XrSo/Editor/TmpResources.cs`:

```csharp
using UnityEditor;

namespace InventorXrSo.Editor
{
    public static class TmpResources
    {
        /// <summary>Batch: importa TMP Essential Resources (font SDF di default e impostazioni).</summary>
        public static void ImportEssentials()
        {
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
        }
    }
}
```

ed eseguilo:

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.TmpResources.ImportEssentials","-quit" -Log "$env:TEMP\xrso-tmp.log"
```

Se `TMP_PackageResourceImporter.ImportResources` non esiste o non è pubblico in uGUI 2.6, **fermati**: chiedi all'utente di eseguire una volta in Unity *Window → TextMeshPro → Import TMP Essential Resources*, poi riprendi. Verifica: `Assets/TextMesh Pro/Resources/TMP Settings.asset` esiste. Se `TmpResources.cs` non serve più, cancellalo; se l'hai usato, tienilo.

- [ ] **Step 2: Scrivi il test che fallisce**

`Assets/XrSo/Tests/EditMode/UiFactoryTmpTests.cs`:

```csharp
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class UiFactoryTmpTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        [Test]
        public void TextUsesTheDefaultSdfFontAndTheRequestedCapHeight()
        {
            var canvas = UiFactory.WorldCanvas(null, "T", new Vector2(100, 100));
            _root = canvas.gameObject;
            var text = UiFactory.Text(canvas.transform, "Estrusione", 14);
            Assert.IsNotNull(text.font, "TMP Essential Resources non importate");
            Assert.AreEqual("Estrusione", text.text);
            // Cap height ~0,7 della dimensione del font: 14 mm di maiuscole = 20 unita canvas (mm).
            Assert.AreEqual(20f, text.fontSize, 0.01f);
        }

        [Test]
        public void TextButtonInvokesItsAction()
        {
            var canvas = UiFactory.WorldCanvas(null, "T", new Vector2(100, 100));
            _root = canvas.gameObject;
            int clicks = 0;
            var button = UiFactory.TextButton(canvas.transform, "Applica", UiFactory.Accent, 14, () => clicks++);
            button.onClick.Invoke();
            Assert.AreEqual(1, clicks);
            Assert.AreEqual("Applica", button.GetComponentInChildren<TextMeshProUGUI>().text);
        }

        [Test]
        public void EveryCommitPhaseHasADistinctColourExceptEmpty()
        {
            var seen = new System.Collections.Generic.HashSet<Color>();
            foreach (CommitBarPhase p in System.Enum.GetValues(typeof(CommitBarPhase)))
                if (p != CommitBarPhase.Empty) Assert.IsTrue(seen.Add(UiStyle.For(p)), p.ToString());
        }
    }
}
```

Aggiungi `"Unity.TextMeshPro"` a `references` in `InventorXrSo.Tests.EditMode.asmdef` e in `Assets/XrSo/Runtime/InventorXrSo.Unity.asmdef`. Se Unity segnala che l'assembly non esiste con quel nome, cerca il nome reale con `Grep` sui file `.asmdef` in `Library/PackageCache/com.unity.ugui*` e usa quello.

- [ ] **Step 3: Esegui e verifica il fallimento**

Run (PowerShell): `& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testFilter","InventorXrSo.Tests.UiFactoryTmpTests","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"`
Expected: errore di compilazione (`UiFactory.Text` mancante), visibile nel log.

- [ ] **Step 4: Implementa**

In `UiFactory.cs` aggiungi `using TMPro;` e:

```csharp
        /// <summary>Rapporto tra altezza delle maiuscole e fontSize per il font SDF di default.</summary>
        public const float CapHeightRatio = 0.7f;

        /// <summary>Testo TextMeshPro; capHeightMm e l'altezza delle maiuscole (canvas: 1 unita = 1 mm).</summary>
        public static TextMeshProUGUI Text(Transform parent, string text, float capHeightMm, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = capHeightMm / CapHeightRatio;
            label.fontStyle = style;
            label.color = Color.white;
            label.text = text;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        public static Button TextButton(Transform parent, string text, Color color, float capHeightMm, Action onClick)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = Color.white;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = UiStyle.Hover;
            colors.pressedColor = new Color(0.12f, 0.35f, 0.65f);
            colors.selectedColor = color;
            colors.disabledColor = UiStyle.Disabled;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClick());
            var label = Text(go.transform, text, capHeightMm, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            return button;
        }

        /// <summary>Svuota un contenitore: all'indietro, perche distruggere mentre si itera salta figli.</summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }
```

Se `textWrappingMode` non esiste nella versione di TextMeshPro installata, usa `enableWordWrapping = true` (in uGUI 2.x è solo deprecato).

`Runtime/Ui/UiStyle.cs`:

```csharp
using InventorXrSo.Core.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Colori di stato M6: anteprima blu, stale ambra, errore rosso, applicato verde.</summary>
    public static class UiStyle
    {
        public static readonly Color Hover = new Color(0.35f, 0.65f, 0.95f);
        public static readonly Color Disabled = new Color(0.22f, 0.24f, 0.28f, 0.45f);
        public static readonly Color Ghost = new Color(0.25f, 0.55f, 1f, 0.35f);

        public static Color For(CommitBarPhase phase)
        {
            switch (phase)
            {
                case CommitBarPhase.Draft: return new Color(0.22f, 0.24f, 0.28f, 0.95f);
                case CommitBarPhase.Previewing: return new Color(0.16f, 0.36f, 0.72f, 0.95f);
                case CommitBarPhase.Ready: return new Color(0.18f, 0.52f, 0.32f, 0.95f);
                case CommitBarPhase.Stale: return new Color(0.85f, 0.58f, 0.10f, 0.95f);
                case CommitBarPhase.Uncertain: return new Color(0.78f, 0.18f, 0.18f, 0.95f);
                case CommitBarPhase.Error: return new Color(0.62f, 0.14f, 0.20f, 0.95f);
                case CommitBarPhase.Offline: return new Color(0.40f, 0.40f, 0.42f, 0.95f);
                case CommitBarPhase.Applied: return new Color(0.20f, 0.70f, 0.35f, 0.95f);
                default: return UiFactory.Background;
            }
        }
    }
}
```

- [ ] **Step 5: Esegui e verifica che passi**

Run lo stesso comando dello Step 3.
Expected: 3/3 PASS nel file XML dei risultati.

- [ ] **Step 6: Commit**

```bash
git add "Inventor XR SO/Assets/TextMesh Pro" "Inventor XR SO/Assets/XrSo/Runtime" "Inventor XR SO/Assets/XrSo/Tests/EditMode" "Inventor XR SO/Assets/XrSo/Editor" "Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Ui"
git commit -m "feat(xr): TextMeshPro text and commit-state styles in UiFactory"
```

(Include i `.meta` generati da Unity per i file core dei Task 1–4.)

---

### Task 6: Input semantico e aptica (Unity)

**Files:**
- Create: `Inventor XR SO/Assets/XrSo/Xr/Input/XrInputFrame.cs`
- Create: `Inventor XR SO/Assets/XrSo/Xr/Input/XrInput.cs`
- Create: `Inventor XR SO/Assets/XrSo/Xr/Input/Haptics.cs`
- Test: `Inventor XR SO/Assets/XrSo/Tests/EditMode/XrInputTests.cs`

**Interfaces:**
- Produces:
  - `struct XrInputFrame` con campi pubblici `bool PenTrigger, PenGrip, A, B, PaletteTrigger, PaletteGrip, X, Y; Vector2 PenStick, PaletteStick; bool PenTracked, PaletteTracked`.
  - `interface IXrInputSource { XrInputFrame Read(); bool Synthetic { get; } }`, `OvrInputSource`, `SyntheticInputSource` (con `XrInputFrame Next` impostabile).
  - `XrInput : MonoBehaviour` con `Source`, `Poll(XrInputFrame frame, float timeSeconds)`, eventi `PenPressed`, `PenReleased`, `PenGrabStarted`, `PenGrabEnded`, `Back`, `Fit`, `Recenter`, `SnapToggled`, `StepDelta(int)`, `StepSizeDelta(int)`, `TabDelta(int)`, `Zoom(float)`, `TwoHandChanged(bool)`, proprietà `PenHeld`, `Precision`, `TwoHand`, `Synthetic`. B **non** è gestito qui (resta a `PushToTalkInput`).
  - `enum HapticPulse { Tick, Hover, Success, Error }`, `static class Haptics { static Action<HapticPulse, bool> Sink; static void Play(HapticPulse pulse, bool pen = true); }`.

- [ ] **Step 1: Scrivi i test che falliscono**

```csharp
using System.Collections.Generic;
using InventorXrSo.Xr.Input;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class XrInputTests
    {
        private GameObject _go;
        private XrInput _input;
        private readonly List<string> _log = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("input");
            _input = _go.AddComponent<XrInput>();
            _input.PenPressed += () => _log.Add("press");
            _input.PenReleased += () => _log.Add("release");
            _input.Back += () => _log.Add("back");
            _input.Fit += () => _log.Add("fit");
            _input.Recenter += () => _log.Add("recenter");
            _input.SnapToggled += () => _log.Add("snap");
            _input.StepDelta += d => _log.Add("step" + d);
            _input.StepSizeDelta += d => _log.Add("size" + d);
            _input.TabDelta += d => _log.Add("tab" + d);
            _input.TwoHandChanged += on => _log.Add("two" + on);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_go); _log.Clear(); }

        private static XrInputFrame F() => new XrInputFrame { PenTracked = true, PaletteTracked = true };

        [Test]
        public void TriggerPressAndReleaseAreEdges()
        {
            var down = F(); down.PenTrigger = true;
            _input.Poll(down, 0); _input.Poll(down, 0.1f); _input.Poll(F(), 0.2f);
            CollectionAssert.AreEqual(new[] { "press", "release" }, _log);
        }

        [Test]
        public void LosingTrackingReleasesTheHeldPen()
        {
            var down = F(); down.PenTrigger = true;
            _input.Poll(down, 0);
            var lost = down; lost.PenTracked = false;
            _input.Poll(lost, 0.1f);
            CollectionAssert.AreEqual(new[] { "press", "release" }, _log);
            Assert.IsFalse(_input.PenHeld);
        }

        [Test]
        public void StickFlicksFireOncePerFlickWithHysteresis()
        {
            var right = F(); right.PenStick = new Vector2(0.9f, 0);
            var half = F(); half.PenStick = new Vector2(0.5f, 0);
            _input.Poll(right, 0); _input.Poll(half, 0.1f); _input.Poll(right, 0.2f);
            _input.Poll(F(), 0.3f); _input.Poll(right, 0.4f);
            CollectionAssert.AreEqual(new[] { "step1", "step1" }, _log);
        }

        [Test]
        public void PenStickVerticalChangesStepSizeAndPaletteStickChangesTab()
        {
            var up = F(); up.PenStick = new Vector2(0, 0.9f);
            var left = F(); left.PaletteStick = new Vector2(-0.9f, 0);
            _input.Poll(up, 0); _input.Poll(F(), 0.1f); _input.Poll(left, 0.2f);
            CollectionAssert.AreEqual(new[] { "size1", "tab-1" }, _log);
        }

        [Test]
        public void YTapFitsAndYHoldRecentersOnce()
        {
            var y = F(); y.Y = true;
            _input.Poll(y, 0); _input.Poll(F(), 0.3f);
            _input.Poll(y, 1); _input.Poll(y, 1.5f); _input.Poll(y, 2.1f); _input.Poll(y, 2.5f); _input.Poll(F(), 2.6f);
            CollectionAssert.AreEqual(new[] { "fit", "recenter" }, _log);
        }

        [Test]
        public void XIsBackAndAIsSnap()
        {
            var x = F(); x.X = true;
            var a = F(); a.A = true;
            _input.Poll(x, 0); _input.Poll(F(), 0.1f); _input.Poll(a, 0.2f);
            CollectionAssert.AreEqual(new[] { "back", "snap" }, _log);
        }

        [Test]
        public void BothGripsMakeTwoHandAndPaletteTriggerIsPrecision()
        {
            var both = F(); both.PenGrip = true; both.PaletteGrip = true; both.PaletteTrigger = true;
            _input.Poll(both, 0);
            Assert.IsTrue(_input.TwoHand);
            Assert.IsTrue(_input.Precision);
            _input.Poll(F(), 0.1f);
            CollectionAssert.AreEqual(new[] { "twoTrue", "twoFalse" }, _log);
        }

        [Test]
        public void ZoomReportsPaletteStickVerticalBeyondTheDeadZone()
        {
            float zoom = 0; _input.Zoom += z => zoom = z;
            var f = F(); f.PaletteStick = new Vector2(0, 0.1f);
            _input.Poll(f, 0);
            Assert.AreEqual(0, zoom);
            f.PaletteStick = new Vector2(0, -0.6f);
            _input.Poll(f, 0.1f);
            Assert.AreEqual(-0.6f, zoom, 1e-5);
        }

        [Test]
        public void SyntheticSourceIsReported()
        {
            _input.Source = new SyntheticInputSource();
            Assert.IsTrue(_input.Synthetic);
        }

        [Test]
        public void HapticsGoThroughTheReplaceableSink()
        {
            var played = new List<string>();
            var old = Haptics.Sink;
            Haptics.Sink = (p, pen) => played.Add(p + ":" + pen);
            try { Haptics.Play(HapticPulse.Success); Haptics.Play(HapticPulse.Tick, false); }
            finally { Haptics.Sink = old; }
            CollectionAssert.AreEqual(new[] { "Success:True", "Tick:False" }, played);
        }
    }
}
```

- [ ] **Step 2: Esegui e verifica il fallimento**

Run: `& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testFilter","InventorXrSo.Tests.XrInputTests","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"`
Expected: errore di compilazione (`InventorXrSo.Xr.Input` mancante).

- [ ] **Step 3: Implementa**

`Xr/Input/XrInputFrame.cs`:

```csharp
using UnityEngine;

namespace InventorXrSo.Xr.Input
{
    /// <summary>Stato di entrambi i controller in un frame. Pen = destro, Palette = sinistro.</summary>
    public struct XrInputFrame
    {
        public bool PenTrigger, PenGrip, A, B, PaletteTrigger, PaletteGrip, X, Y;
        public Vector2 PenStick, PaletteStick;
        public bool PenTracked, PaletteTracked;
    }

    public interface IXrInputSource
    {
        XrInputFrame Read();
        /// <summary>True per le sorgenti dei runner: i log devono chiamare "sintetico" l'input.</summary>
        bool Synthetic { get; }
    }

    public sealed class OvrInputSource : IXrInputSource
    {
        public bool Synthetic => false;

        public XrInputFrame Read()
        {
            const OVRInput.Controller r = OVRInput.Controller.RTouch, l = OVRInput.Controller.LTouch;
            return new XrInputFrame
            {
                PenTrigger = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, r),
                PenGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, r),
                A = OVRInput.Get(OVRInput.Button.One, r),
                B = OVRInput.Get(OVRInput.Button.Two, r),
                PaletteTrigger = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, l),
                PaletteGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, l),
                X = OVRInput.Get(OVRInput.Button.One, l),
                Y = OVRInput.Get(OVRInput.Button.Two, l),
                PenStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, r),
                PaletteStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, l),
                PenTracked = Tracked(r),
                PaletteTracked = Tracked(l),
            };
        }

        private static bool Tracked(OVRInput.Controller c) => OVRInput.IsControllerConnected(c)
            && OVRInput.GetControllerPositionTracked(c) && OVRInput.GetControllerOrientationTracked(c);
    }

    public sealed class SyntheticInputSource : IXrInputSource
    {
        public XrInputFrame Next;
        public bool Synthetic => true;
        public XrInputFrame Read() => Next;
    }
}
```

`Xr/Input/XrInput.cs`:

```csharp
using System;
using UnityEngine;

namespace InventorXrSo.Xr.Input
{
    /// <summary>
    /// Unico lettore dei controller per la UI M6: trasforma lo stato grezzo in eventi semantici
    /// (spec M6, mappatura dei controller). B resta a PushToTalkInput.
    /// </summary>
    public sealed class XrInput : MonoBehaviour
    {
        public const float FlickOn = 0.6f, FlickOff = 0.3f, ZoomDeadZone = 0.2f, RecenterHoldSeconds = 1f;

        private XrInputFrame _last;
        private bool _penStickX, _penStickY, _paletteStickX;
        private float _yDownAt = -1;
        private bool _recentered;

        public IXrInputSource Source { get; set; } = new OvrInputSource();
        public bool Synthetic => Source != null && Source.Synthetic;
        public bool PenHeld { get; private set; }
        public bool Precision { get; private set; }
        public bool TwoHand { get; private set; }

        public event Action PenPressed, PenReleased, PenGrabStarted, PenGrabEnded, Back, Fit, Recenter, SnapToggled;
        public event Action<int> StepDelta, StepSizeDelta, TabDelta;
        public event Action<float> Zoom;
        public event Action<bool> TwoHandChanged;

        private void Update() { if (Source != null) Poll(Source.Read(), Time.unscaledTime); }

        public void Poll(XrInputFrame f, float time)
        {
            bool trigger = f.PenTrigger && f.PenTracked;
            if (trigger && !PenHeld) { PenHeld = true; PenPressed?.Invoke(); }
            else if (!trigger && PenHeld) { PenHeld = false; PenReleased?.Invoke(); }

            bool grip = f.PenGrip && f.PenTracked;
            bool lastGrip = _last.PenGrip && _last.PenTracked;
            if (grip && !lastGrip) PenGrabStarted?.Invoke();
            else if (!grip && lastGrip) PenGrabEnded?.Invoke();

            bool two = grip && f.PaletteGrip && f.PaletteTracked;
            if (two != TwoHand) { TwoHand = two; TwoHandChanged?.Invoke(two); }
            Precision = f.PaletteTrigger && f.PaletteTracked;

            if (f.X && !_last.X) Back?.Invoke();
            if (f.A && !_last.A) SnapToggled?.Invoke();

            if (f.Y && !_last.Y) { _yDownAt = time; _recentered = false; }
            if (f.Y && !_recentered && _yDownAt >= 0 && time - _yDownAt >= RecenterHoldSeconds) { _recentered = true; Recenter?.Invoke(); }
            if (!f.Y && _last.Y) { if (!_recentered) Fit?.Invoke(); _yDownAt = -1; }

            Flick(f.PenStick.x, ref _penStickX, StepDelta);
            Flick(f.PenStick.y, ref _penStickY, StepSizeDelta);
            Flick(f.PaletteStick.x, ref _paletteStickX, TabDelta);
            if (Mathf.Abs(f.PaletteStick.y) > ZoomDeadZone) Zoom?.Invoke(f.PaletteStick.y);

            _last = f;
        }

        private static void Flick(float axis, ref bool armed, Action<int> raise)
        {
            if (!armed && Mathf.Abs(axis) >= FlickOn) { armed = true; raise?.Invoke(axis > 0 ? 1 : -1); }
            else if (armed && Mathf.Abs(axis) <= FlickOff) armed = false;
        }
    }
}
```

`Xr/Input/Haptics.cs`:

```csharp
using System;
using System.Collections;
using UnityEngine;

namespace InventorXrSo.Xr.Input
{
    public enum HapticPulse { Tick, Hover, Success, Error }

    /// <summary>Profili aptici M6. Sink e sostituibile nei test; di default vibra il controller.</summary>
    public static class Haptics
    {
        public static Action<HapticPulse, bool> Sink = PlayOnController;
        private static HapticsRunner _runner;

        public static void Play(HapticPulse pulse, bool pen = true) => Sink?.Invoke(pulse, pen);

        private static void PlayOnController(HapticPulse pulse, bool pen)
        {
            if (!Application.isPlaying) return;
            if (_runner == null) _runner = new GameObject("Haptics").AddComponent<HapticsRunner>();
            _runner.Play(pulse, pen ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch);
        }

        private sealed class HapticsRunner : MonoBehaviour
        {
            public void Play(HapticPulse pulse, OVRInput.Controller c) { StopAllCoroutines(); StartCoroutine(Run(pulse, c)); }

            private static IEnumerator Run(HapticPulse pulse, OVRInput.Controller c)
            {
                switch (pulse)
                {
                    case HapticPulse.Tick: yield return Buzz(c, 0.8f, 0.25f, 0.015f); break;
                    case HapticPulse.Hover: yield return Buzz(c, 0.5f, 0.15f, 0.02f); break;
                    case HapticPulse.Success:
                        yield return Buzz(c, 0.6f, 0.5f, 0.05f);
                        yield return new WaitForSecondsRealtime(0.06f);
                        yield return Buzz(c, 0.6f, 0.5f, 0.05f);
                        break;
                    case HapticPulse.Error: yield return Buzz(c, 0.3f, 0.8f, 0.3f); break;
                }
            }

            private static IEnumerator Buzz(OVRInput.Controller c, float frequency, float amplitude, float seconds)
            {
                OVRInput.SetControllerVibration(frequency, amplitude, c);
                yield return new WaitForSecondsRealtime(seconds);
                OVRInput.SetControllerVibration(0, 0, c);
            }
        }
    }
}
```

- [ ] **Step 4: Esegui e verifica che passi**

Run il comando dello Step 2.
Expected: 9/9 PASS.

- [ ] **Step 5: Commit**

```bash
git add "Inventor XR SO/Assets/XrSo/Xr/Input" "Inventor XR SO/Assets/XrSo/Tests/EditMode/XrInputTests.cs"*
git commit -m "feat(xr): semantic controller input and haptic profiles"
```

---

### Task 7: `UiShell`: tavolozza, tastierino, barra di conferma, HUD (Unity)

**Files:**
- Create: `Inventor XR SO/Assets/XrSo/Runtime/Ui/Shell/PaletteView.cs`
- Create: `Inventor XR SO/Assets/XrSo/Runtime/Ui/Shell/CommitBarView.cs`
- Create: `Inventor XR SO/Assets/XrSo/Runtime/Ui/Shell/HudView.cs`
- Create: `Inventor XR SO/Assets/XrSo/Runtime/Ui/Shell/UiShell.cs`
- Test: `Inventor XR SO/Assets/XrSo/Tests/EditMode/UiShellTests.cs`

**Interfaces:**
- Consumes: `ActionCatalog`, `XrAction`, `XrTab`, `CommitIds`, `CommitBarState`, `CommitBarPhase`, `NumericEntry` (Task 1–3); `UiFactory.Text`, `UiFactory.TextButton`, `UiFactory.ClearChildren`, `UiStyle.For` (Task 5).
- Produces:
  - `PaletteView : MonoBehaviour` — `static Create(Transform parent)`, `Render(ActionCatalog catalog)`, `SelectTab(int delta)`, `CurrentTab : string`, `ShowKeypad(NumericEntry entry)`, `HideKeypad()`, `KeypadVisible : bool`, `Canvas : Canvas`. Dimensione 120×90 mm; testo pulsanti 8 mm; griglia 2×4 di pulsanti ≥ 15 mm.
  - `CommitBarView : MonoBehaviour` — `static Create(Transform parent)`, `Render(CommitBarState state, ActionCatalog catalog)`, `Canvas`. Dimensione 360×60 mm, testo 14 mm. Pulsanti: Anteprima (`CommitIds.Preview`) se `CanPreview`, Applica (`CommitIds.Apply`) se `CanApply`, Annulla (`CommitIds.Cancel`) se `CanCancel`, `RecoveryLabel` → `CommitIds.Recover`. Nascosta se `Phase == Empty` o lo stato è null.
  - `HudView : MonoBehaviour` — `static Create(Transform head)`, `SetStatus(string)`, `Flash(string)`, `Canvas`. Stessa API di `StatusBadge`; striscia 300×40 mm; testo 14 mm; posa ~15° sopra l'orizzonte a 1,2 m; segue lo yaw della testa solo oltre ±25°.
  - `UiShell : MonoBehaviour` — `static Create(Transform paletteAnchor, Transform head, ActionCatalog catalog)`, `Palette`, `CommitBar`, `Hud`, `Catalog`, `Refresh()`, `PlaceCommitBar(Vector3 position, Quaternion rotation)`. Si ridisegna a ogni `catalog.Changed` e a ogni `CommitBarState.Changed` del provider attivo.

- [ ] **Step 1: Scrivi i test che falliscono**

```csharp
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Tests
{
    public class UiShellTests
    {
        private sealed class Provider : IActionProvider
        {
            public List<XrTab> TabList = new List<XrTab>();
            public List<XrAction> All = new List<XrAction>();
            public CommitBarState Bar = new CommitBarState();
            public IReadOnlyList<XrTab> Tabs => TabList;
            public IEnumerable<XrAction> Actions => All;
            public IEnumerable<XrAction> ContextActions(SelectionKind s) => Enumerable.Empty<XrAction>();
            public CommitBarState CommitBar => Bar;
        }

        private readonly List<GameObject> _roots = new List<GameObject>();
        private GameObject _anchor, _head;

        [SetUp]
        public void SetUp()
        {
            _anchor = new GameObject("left"); _head = new GameObject("head");
            _roots.Add(_anchor); _roots.Add(_head);
        }

        [TearDown]
        public void TearDown() { foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r); _roots.Clear(); }

        private static XrAction A(string id, string label, string tab, System.Action run = null, bool enabled = true)
            => new XrAction(id, label, tab, () => enabled, run ?? (() => { }), () => "spento");

        private UiShell Shell(ActionCatalog catalog)
        {
            var shell = UiShell.Create(_anchor.transform, _head.transform, catalog);
            _roots.Add(shell.gameObject); _roots.Add(shell.CommitBar.Canvas.gameObject); _roots.Add(shell.Hud.Canvas.gameObject);
            return shell;
        }

        private static ActionCatalog Catalog(out Provider spaces)
        {
            spaces = new Provider();
            spaces.TabList.Add(new XrTab(ActionCatalog.SpacesTab, "Spazi"));
            spaces.All.Add(A("spaces.inspect", "Ispeziona", ActionCatalog.SpacesTab));
            return new ActionCatalog(spaces);
        }

        private static string[] Labels(Component root) =>
            root.GetComponentsInChildren<Button>(false).Select(b => b.GetComponentInChildren<TextMeshProUGUI>().text).ToArray();

        [Test]
        public void PaletteFollowsTheLeftControllerAndShowsSpacesByDefault()
        {
            var shell = Shell(Catalog(out _));
            Assert.AreSame(_anchor.transform, shell.Palette.Canvas.transform.parent);
            Assert.AreEqual(ActionCatalog.SpacesTab, shell.Palette.CurrentTab);
            CollectionAssert.Contains(Labels(shell.Palette), "Ispeziona");
        }

        [Test]
        public void TabsCycleAndButtonsInvokeActions()
        {
            var catalog = Catalog(out _);
            int runs = 0;
            var design = new Provider();
            design.TabList.Add(new XrTab("sketch", "Schizzo"));
            design.All.Add(A("design.sketch", "Crea schizzo", "sketch", () => runs++));
            var shell = Shell(catalog);
            catalog.SetActive(design);
            Assert.AreEqual("sketch", shell.Palette.CurrentTab);
            shell.Palette.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Crea schizzo").onClick.Invoke();
            Assert.AreEqual(1, runs);
            shell.Palette.SelectTab(+1);
            Assert.AreEqual(ActionCatalog.SpacesTab, shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1);
            Assert.AreEqual("sketch", shell.Palette.CurrentTab);
        }

        [Test]
        public void DisabledActionsAreNotInteractable()
        {
            var catalog = Catalog(out _);
            var p = new Provider();
            p.TabList.Add(new XrTab("t", "T"));
            p.All.Add(A("off", "Spento", "t", enabled: false));
            var shell = Shell(catalog);
            catalog.SetActive(p);
            Assert.IsFalse(shell.Palette.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Spento").interactable);
        }

        [Test]
        public void KeypadReplacesTheTabAndCommitsIntoTheEntry()
        {
            var shell = Shell(Catalog(out _));
            var entry = new NumericEntry("h", QuantityUnit.Millimeters, 20, 0.01, 1000);
            shell.Palette.ShowKeypad(entry);
            Assert.IsTrue(shell.Palette.KeypadVisible);
            Button Key(string t) => shell.Palette.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == t);
            Key("1").onClick.Invoke(); Key("2").onClick.Invoke(); Key(",").onClick.Invoke(); Key("5").onClick.Invoke();
            Key("OK").onClick.Invoke();
            Assert.AreEqual(12.5, entry.Value, 1e-9);
            Assert.IsFalse(shell.Palette.KeypadVisible);
        }

        [Test]
        public void CommitBarShowsOnlyTheAllowedButtonsAndTheRecovery()
        {
            var catalog = Catalog(out _);
            var p = new Provider();
            int applied = 0;
            p.All.Add(A(CommitIds.Preview, "Anteprima", ActionCatalog.CommitTab));
            p.All.Add(A(CommitIds.Apply, "Applica", ActionCatalog.CommitTab, () => applied++));
            p.All.Add(A(CommitIds.Cancel, "Annulla", ActionCatalog.CommitTab));
            p.All.Add(A(CommitIds.Recover, "Recupera", ActionCatalog.CommitTab));
            var shell = Shell(catalog);
            catalog.SetActive(p);

            Assert.IsFalse(shell.CommitBar.Canvas.gameObject.activeSelf, "Empty hides the bar");
            p.Bar.Update(new CommitBarInputs(true, false, false, false, false, true, null), 0);
            CollectionAssert.AreEquivalent(new[] { "Anteprima", "Annulla" }, Labels(shell.CommitBar));
            p.Bar.Update(new CommitBarInputs(true, false, false, false, true, false, null), 0);
            CollectionAssert.AreEquivalent(new[] { "Applica", "Annulla" }, Labels(shell.CommitBar));
            shell.CommitBar.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Applica").onClick.Invoke();
            Assert.AreEqual(1, applied);
            p.Bar.Update(new CommitBarInputs(true, false, true, false, true, false, null), 0);
            CollectionAssert.AreEquivalent(new[] { "Aggiorna documento" }, Labels(shell.CommitBar));
            Assert.AreEqual(UiStyle.For(CommitBarPhase.Stale), shell.CommitBar.GetComponentInChildren<Image>().color);
        }

        [Test]
        public void HudKeepsTheStatusBadgeApi()
        {
            var shell = Shell(Catalog(out _));
            shell.Hud.SetStatus("Online · rev 42");
            shell.Hud.Flash("3 corpi omessi");
            var texts = shell.Hud.GetComponentsInChildren<TextMeshProUGUI>().Select(t => t.text).ToArray();
            CollectionAssert.Contains(texts, "Online · rev 42");
            CollectionAssert.Contains(texts, "3 corpi omessi");
        }
    }
}
```

- [ ] **Step 2: Esegui e verifica il fallimento**

Run: `& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testFilter","InventorXrSo.Tests.UiShellTests","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"`
Expected: errore di compilazione (`UiShell` mancante).

- [ ] **Step 3: Implementa `PaletteView`**

```csharp
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Tavolozza sul controller sinistro: scheda corrente in griglia 2×4, oppure tastierino.</summary>
    public sealed class PaletteView : MonoBehaviour
    {
        public const float TextMm = 8f;
        private static readonly string[] Keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", "-", "0", ",", "←", "Annulla", "OK" };

        private ActionCatalog _catalog;
        private IActionProvider _lastProvider;
        private RectTransform _header, _grid;
        private TextMeshProUGUI _title, _chip;
        private NumericEntry _entry;
        private readonly List<string> _tabs = new List<string>();

        public Canvas Canvas { get; private set; }
        public string CurrentTab { get; private set; }
        public bool KeypadVisible => _entry != null;

        public static PaletteView Create(Transform parent)
        {
            var canvas = UiFactory.WorldCanvas(parent, "Tavolozza", new Vector2(120, 90));
            // Sopra la faccia del controller, inclinata verso chi guarda.
            canvas.transform.localPosition = new Vector3(0, 0.05f, 0.02f);
            canvas.transform.localRotation = Quaternion.Euler(45, 0, 0);
            var view = canvas.gameObject.AddComponent<PaletteView>();
            view.Canvas = canvas;
            var bg = UiFactory.Panel(canvas.transform, "Sfondo", UiFactory.Background);
            UiFactory.Stretch(bg);
            var column = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(4, 4, 4, 4);
            column.spacing = 3;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandHeight = false;
            view._header = UiFactory.Panel(bg, "Schede", Color.clear);
            view._header.gameObject.AddComponent<LayoutElement>().preferredHeight = 14;
            view._title = UiFactory.Text(view._header, "", TextMm, FontStyles.Bold);
            view._title.alignment = TextAlignmentOptions.Center;
            UiFactory.Stretch(view._title.rectTransform);
            view._chip = UiFactory.Text(bg, "", TextMm);
            view._chip.alignment = TextAlignmentOptions.Center;
            view._chip.gameObject.AddComponent<LayoutElement>().preferredHeight = 12;
            view._grid = UiFactory.Panel(bg, "Griglia", Color.clear);
            view._grid.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            return view;
        }

        public void Render(ActionCatalog catalog)
        {
            _catalog = catalog;
            // Un workspace appena attivato parte dalla sua prima scheda, non da quella del precedente.
            if (catalog.Active != _lastProvider) { _lastProvider = catalog.Active; CurrentTab = null; }
            _tabs.Clear();
            _tabs.AddRange(catalog.Tabs.Select(t => t.Id));
            if (CurrentTab == null || !_tabs.Contains(CurrentTab)) CurrentTab = _tabs.FirstOrDefault();
            Rebuild();
        }

        public void SelectTab(int delta)
        {
            if (_tabs.Count == 0 || KeypadVisible) return;
            int i = (_tabs.IndexOf(CurrentTab) + delta + _tabs.Count) % _tabs.Count;
            CurrentTab = _tabs[i];
            Rebuild();
        }

        public void ShowKeypad(NumericEntry entry)
        {
            if (_entry != null) _entry.Changed -= Rebuild;
            _entry = entry;
            _entry.Changed += Rebuild;
            _entry.BeginEdit();
            Rebuild();
        }

        public void HideKeypad()
        {
            if (_entry == null) return;
            _entry.Changed -= Rebuild;
            _entry.CancelEdit();
            _entry = null;
            Rebuild();
        }

        private void Rebuild()
        {
            UiFactory.ClearChildren(_grid);
            var grid = _grid.GetComponent<GridLayoutGroup>() ?? _grid.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(3, 3);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            if (KeypadVisible)
            {
                grid.constraintCount = 3;
                grid.cellSize = new Vector2(36, 11);
                _title.text = "Valore";
                _chip.gameObject.SetActive(true);
                _chip.text = _entry.Display;
                foreach (var key in Keys) { var k = key; UiFactory.TextButton(_grid, k, k == "OK" ? UiFactory.Accent : UiFactory.Key, TextMm, () => Press(k)); }
                return;
            }
            grid.constraintCount = 2;
            grid.cellSize = new Vector2(54, 15);
            _chip.gameObject.SetActive(false);
            var tab = _catalog?.Tabs.FirstOrDefault(t => t.Id == CurrentTab);
            _title.text = tab == null ? "" : "‹ " + tab.Label + " ›";
            if (_catalog == null || CurrentTab == null) return;
            foreach (var action in _catalog.Palette(CurrentTab))
            {
                var a = action;
                var b = UiFactory.TextButton(_grid, a.Label, a.IsOn ? UiFactory.Accent : UiFactory.Key, TextMm, () => a.TryInvoke());
                b.interactable = a.Enabled;
            }
        }

        private void Press(string key)
        {
            if (_entry == null) return;
            if (key == "OK") { if (_entry.Commit(out _)) { _entry.Changed -= Rebuild; _entry = null; Rebuild(); } return; }
            if (key == "Annulla") { HideKeypad(); return; }
            if (key == "←") { _entry.Backspace(); return; }
            _entry.Type(key[0]);
        }
    }
}
```

- [ ] **Step 4: Implementa `CommitBarView`**

```csharp
using InventorXrSo.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Barra di conferma unica sul bordo vicino del piano: l'unico posto da cui si applica.</summary>
    public sealed class CommitBarView : MonoBehaviour
    {
        public const float TextMm = 14f;
        private Image _background;
        private TextMeshProUGUI _message;
        private RectTransform _buttons;

        public Canvas Canvas { get; private set; }

        public static CommitBarView Create(Transform parent)
        {
            var canvas = UiFactory.WorldCanvas(parent, "Barra di conferma", new Vector2(360, 60));
            var view = canvas.gameObject.AddComponent<CommitBarView>();
            view.Canvas = canvas;
            var bg = UiFactory.Panel(canvas.transform, "Sfondo", UiFactory.Background);
            UiFactory.Stretch(bg);
            view._background = bg.GetComponent<Image>();
            var row = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 6, 6);
            row.spacing = 6;
            row.childControlWidth = row.childControlHeight = true;
            view._message = UiFactory.Text(bg, "", TextMm);
            view._message.alignment = TextAlignmentOptions.MidlineLeft;
            view._message.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            view._buttons = UiFactory.Panel(bg, "Pulsanti", Color.clear);
            var buttons = view._buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttons.spacing = 6;
            buttons.childControlWidth = buttons.childControlHeight = true;
            view._buttons.gameObject.AddComponent<LayoutElement>().preferredWidth = 230;
            canvas.gameObject.SetActive(false);
            return view;
        }

        public void Render(CommitBarState state, ActionCatalog catalog)
        {
            bool visible = state != null && state.Phase != CommitBarPhase.Empty;
            Canvas.gameObject.SetActive(visible);
            if (!visible) return;
            _background.color = UiStyle.For(state.Phase);
            _message.text = Message(state);
            UiFactory.ClearChildren(_buttons);
            if (state.RecoveryLabel != null) Add(catalog, CommitIds.Recover, state.RecoveryLabel);
            if (state.CanPreview) Add(catalog, CommitIds.Preview, null);
            if (state.CanApply) Add(catalog, CommitIds.Apply, null);
            if (state.CanCancel) Add(catalog, CommitIds.Cancel, null);
        }

        private void Add(ActionCatalog catalog, string id, string labelOverride)
        {
            var action = catalog.Find(id);
            if (action == null) return;
            var b = UiFactory.TextButton(_buttons, labelOverride ?? action.Label, UiFactory.Key, TextMm, () => action.TryInvoke());
            b.interactable = action.Enabled;
        }

        private static string Message(CommitBarState s)
        {
            switch (s.Phase)
            {
                case CommitBarPhase.Draft: return "Bozza";
                case CommitBarPhase.Previewing: return "Anteprima in corso…";
                case CommitBarPhase.Ready: return "Anteprima pronta";
                case CommitBarPhase.Stale: return "Documento cambiato";
                case CommitBarPhase.Uncertain: return "Esito non confermato";
                case CommitBarPhase.Error: return s.Message;
                case CommitBarPhase.Offline: return "Offline";
                case CommitBarPhase.Applied: return "Applicato";
                default: return "";
            }
        }
    }
}
```

Nota per il test: `GetComponentInChildren<Image>()` sulla vista restituisce l'`Image` dello sfondo, il primo figlio.

- [ ] **Step 5: Implementa `HudView` e `UiShell`**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>HUD di stato in periferia; segue lo yaw della testa solo oltre ±25° (spec M6).</summary>
    public sealed class HudView : MonoBehaviour
    {
        public const float TextMm = 14f, Distance = 1.2f, ElevationDeg = 15f, FollowBeyondDeg = 25f;
        private Transform _head;
        private TextMeshProUGUI _status, _flash;
        private float _flashUntil, _yaw;
        private bool _placed;

        public Canvas Canvas { get; private set; }

        public static HudView Create(Transform head)
        {
            var canvas = UiFactory.WorldCanvas(null, "HUD", new Vector2(300, 40));
            var hud = canvas.gameObject.AddComponent<HudView>();
            hud.Canvas = canvas;
            hud._head = head;
            var bg = UiFactory.Panel(canvas.transform, "Sfondo", UiFactory.Background);
            UiFactory.Stretch(bg);
            var row = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 4, 4);
            row.spacing = 8;
            row.childControlWidth = row.childControlHeight = true;
            hud._status = UiFactory.Text(bg, "", TextMm, FontStyles.Bold);
            hud._flash = UiFactory.Text(bg, "", TextMm);
            return hud;
        }

        public void SetStatus(string text) => _status.text = text;

        public void Flash(string text)
        {
            _flash.text = text;
            _flashUntil = Time.unscaledTime + 3f;
        }

        private void LateUpdate()
        {
            if (_flash.text.Length > 0 && Time.unscaledTime > _flashUntil) _flash.text = "";
            if (_head == null) return;
            float headYaw = _head.eulerAngles.y;
            if (!_placed || Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) > FollowBeyondDeg) { _yaw = headYaw; _placed = true; }
            var direction = Quaternion.Euler(-ElevationDeg, _yaw, 0) * Vector3.forward;
            var target = _head.position + direction * Distance;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            transform.rotation = Quaternion.LookRotation(transform.position - _head.position, Vector3.up);
        }
    }
}
```

```csharp
using InventorXrSo.Core.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>
    /// Guscio UI M6: tavolozza sul controller sinistro, barra di conferma, HUD. Si disegna solo dal
    /// catalogo; i workspace dichiarano azioni e stato, non costruiscono pannelli.
    /// </summary>
    public sealed class UiShell : MonoBehaviour
    {
        private CommitBarState _bar;

        public ActionCatalog Catalog { get; private set; }
        public PaletteView Palette { get; private set; }
        public CommitBarView CommitBar { get; private set; }
        public HudView Hud { get; private set; }

        public static UiShell Create(Transform paletteAnchor, Transform head, ActionCatalog catalog)
        {
            var shell = new GameObject("UiShell").AddComponent<UiShell>();
            shell.Catalog = catalog;
            shell.Palette = PaletteView.Create(paletteAnchor);
            shell.CommitBar = CommitBarView.Create(null);
            shell.Hud = HudView.Create(head);
            catalog.Changed += shell.Refresh;
            shell.Refresh();
            return shell;
        }

        public void Refresh()
        {
            var bar = Catalog.Active?.CommitBar;
            if (bar != _bar)
            {
                if (_bar != null) _bar.Changed -= RenderBar;
                _bar = bar;
                if (_bar != null) _bar.Changed += RenderBar;
            }
            Palette.Render(Catalog);
            RenderBar();
        }

        public void PlaceCommitBar(Vector3 position, Quaternion rotation) => CommitBar.transform.SetPositionAndRotation(position, rotation);

        private void RenderBar() => CommitBar.Render(_bar, Catalog);

        private void OnDestroy()
        {
            if (Catalog != null) Catalog.Changed -= Refresh;
            if (_bar != null) _bar.Changed -= RenderBar;
            if (CommitBar != null) Destroy(CommitBar.gameObject);
            if (Hud != null) Destroy(Hud.gameObject);
        }
    }
}
```

- [ ] **Step 6: Esegui e verifica che passi**

Run il comando dello Step 2.
Expected: 6/6 PASS. Se un test fallisce per il layout (dimensioni calcolate solo a frame), correggi il test chiamando `Canvas.ForceUpdateCanvases()` prima dell'asserzione, non la vista.

- [ ] **Step 7: Commit**

```bash
git add "Inventor XR SO/Assets/XrSo/Runtime/Ui/Shell"* "Inventor XR SO/Assets/XrSo/Tests/EditMode/UiShellTests.cs"*
git commit -m "feat(xr): UiShell with palette, keypad, commit bar and HUD"
```

---

### Task 8: Aggancio all'app: scheda «Spazi», HUD, input (Unity)

**Files:**
- Create: `Inventor XR SO/Assets/XrSo/Xr/SpacesActions.cs`
- Modify: `Inventor XR SO/Assets/XrSo/Xr/AppController.cs` (righe 34, 61–92, 121, 299, 307, 337, 350, 404, 410)
- Test: `Inventor XR SO/Assets/XrSo/Tests/EditMode/SpacesActionsTests.cs`

**Interfaces:**
- Consumes: `ActionCatalog`, `XrAction`, `XrTab` (Task 1); `UiShell`, `HudView` (Task 7); `XrInput` (Task 6).
- Produces: `SpacesActions : IActionProvider` con costruttore `SpacesActions(Action openInspect, Action openDesign, Action openLamiera, Action openAssembly, Action openConnection, Func<bool> inSession, Func<bool> canDesign, Func<bool> canLamiera, Func<bool> canAssembly)`; id `spaces.inspect`, `spaces.design`, `spaces.lamiera`, `spaces.assembly`, `spaces.connection`. In `AppController`: campi `_catalog`, `_shell`, `_input`; metodi privati `OpenDesign()`, `OpenAssembly()`, `OpenInspection()` estratti dalle lambda esistenti.

In questa fase il menù polso di `InspectWorkspace` **resta** (lo usa il runner M3). La scheda Spazi chiama gli stessi metodi; nessun workspace diventa ancora `IActionProvider`, quindi `_catalog.Active` resta `null` e la barra di conferma resta nascosta.

- [ ] **Step 1: Scrivi il test che fallisce**

```csharp
using System.Linq;
using InventorXrSo.Core.Ui;
using InventorXrSo.Xr;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public class SpacesActionsTests
    {
        [Test]
        public void SpacesExposeTheFiveEntriesInTheSpacesTab()
        {
            var s = new SpacesActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => true, () => true, () => true, () => true);
            Assert.AreEqual(ActionCatalog.SpacesTab, s.Tabs.Single().Id);
            CollectionAssert.AreEqual(
                new[] { "spaces.inspect", "spaces.design", "spaces.lamiera", "spaces.assembly", "spaces.connection" },
                s.Actions.Select(a => a.Id).ToArray());
            Assert.IsTrue(s.Actions.All(a => a.Tab == ActionCatalog.SpacesTab));
        }

        [Test]
        public void WorkspaceEntriesFollowSessionAndCanEnterGuards()
        {
            bool session = false, lamiera = false;
            int opened = 0;
            var s = new SpacesActions(() => { }, () => opened++, () => opened++, () => { }, () => { }, () => session, () => true, () => lamiera, () => true);
            var catalog = new ActionCatalog(s);
            Assert.IsFalse(catalog.TryInvoke("spaces.design"), "no session");
            Assert.IsTrue(catalog.Find("spaces.connection").Enabled, "connection always reachable");
            session = true;
            Assert.IsTrue(catalog.TryInvoke("spaces.design"));
            Assert.IsFalse(catalog.TryInvoke("spaces.lamiera"));
            Assert.IsFalse(string.IsNullOrEmpty(catalog.Find("spaces.lamiera").DisabledReason));
            lamiera = true;
            Assert.IsTrue(catalog.TryInvoke("spaces.lamiera"));
            Assert.AreEqual(2, opened);
        }
    }
}
```

- [ ] **Step 2: Esegui e verifica il fallimento**

Run: `& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testFilter","InventorXrSo.Tests.SpacesActionsTests","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"`
Expected: errore di compilazione.

- [ ] **Step 3: Implementa `SpacesActions`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Ui;

namespace InventorXrSo.Xr
{
    /// <summary>Scheda fissa "Spazi" della tavolozza: cambio workspace e connessione.</summary>
    public sealed class SpacesActions : IActionProvider
    {
        private readonly XrTab[] _tabs = { new XrTab(ActionCatalog.SpacesTab, "Spazi") };
        private readonly XrAction[] _actions;

        public SpacesActions(Action openInspect, Action openDesign, Action openLamiera, Action openAssembly, Action openConnection,
            Func<bool> inSession, Func<bool> canDesign, Func<bool> canLamiera, Func<bool> canAssembly)
        {
            string Offline() => "Nessuna sessione con Inventor.";
            string Blocked() => inSession() ? "Controlla prima la modifica CAD non confermata." : Offline();
            _actions = new[]
            {
                new XrAction("spaces.inspect", "Ispeziona", ActionCatalog.SpacesTab, inSession, openInspect, Offline, new[] { "esplora" }),
                new XrAction("spaces.design", "Progettazione", ActionCatalog.SpacesTab, () => inSession() && canDesign(), openDesign, Blocked),
                new XrAction("spaces.lamiera", "Lamiera", ActionCatalog.SpacesTab, () => inSession() && canLamiera(), openLamiera, Blocked),
                new XrAction("spaces.assembly", "Assieme", ActionCatalog.SpacesTab, () => inSession() && canAssembly(), openAssembly, Blocked),
                new XrAction("spaces.connection", "Connessione", ActionCatalog.SpacesTab, () => true, openConnection),
            };
        }

        public IReadOnlyList<XrTab> Tabs => _tabs;
        public IEnumerable<XrAction> Actions => _actions;
        public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Enumerable.Empty<XrAction>();
        public CommitBarState CommitBar => null;
    }
}
```

- [ ] **Step 4: Aggancia in `AppController`**

1. Estrai le lambda delle righe 82–83 e 87 in metodi privati, senza cambiarne il comportamento:

```csharp
        private void OpenDesign() { if (!_assembly.RequiresCadReview && !_lamiera.RequiresCadReview) { _assembly.Close(); _lamiera.Close(); _design.Open(); } }
        private void OpenAssembly() { if (!_design.RequiresCadReview && !_lamiera.RequiresCadReview) { _design.Close(); _lamiera.Close(); _assembly.Open(); } }
        private void OpenInspection() { _design.Close(); _assembly.Close(); _lamiera.Close(); NotifyVoiceModeChanged(); }
```

e sostituisci le sottoscrizioni con `_inspect.DesignRequested += OpenDesign;`, `_inspect.AssemblyRequested += OpenAssembly;`. Le quattro righe `_inspect.InspectionRequested += ...Close` / `NotifyVoiceModeChanged` restano come sono (la scheda Spazi chiama `OpenInspection`, che fa le stesse cose).

2. Sostituisci `StatusBadge _badge` con `HudView _badge` (tipo `InventorXrSo.Unity.Ui.HudView`) e `StatusBadge.Create(head)` con la creazione della shell, subito dopo `_lamiera.Initialize(...)` e le guardie `CanEnter`:

```csharp
            _catalog = new ActionCatalog(new SpacesActions(OpenInspection, OpenDesign, OpenLamiera, OpenAssembly, LeaveSession,
                () => _inSession, () => _design.CanEnter(), () => _lamiera.CanEnter(), () => _assembly.CanEnter()));
            _shell = UiShell.Create(left, head, _catalog);
            XrUi.MakeInteractive(_shell.Palette.Canvas, head.GetComponent<Camera>());
            XrUi.MakeInteractive(_shell.CommitBar.Canvas, head.GetComponent<Camera>());
            _badge = _shell.Hud;
            _badge.gameObject.SetActive(false);
            _input = gameObject.AddComponent<InventorXrSo.Xr.Input.XrInput>();
            _input.TabDelta += _shell.Palette.SelectTab;
            _input.Back += _shell.Palette.HideKeypad;
```

`left` è la variabile già calcolata alla riga 61. `LeaveSession` esiste già (riga 110): «Connessione» fa esattamente ciò che fa oggi il pulsante Menu del controller (torna a Home). Tutte le altre chiamate a `_badge` (`SetStatus`, `Flash`, `gameObject.SetActive`, `Destroy`) restano valide perché `HudView` ha la stessa API.

3. Il riferimento alla vecchia classe sparisce: lascia il file `StatusBadge.cs` al suo posto (verrà rimosso nella fase 4 insieme al menù polso) oppure, se `Grep` conferma che non ha altri usi nei test, cancellalo nello stesso commit.

4. Nelle notifiche di stato che cambiano abilitazioni (`OnStatusChanged`, `_design.ActiveChanged`, `_assembly.ActiveChanged`, `_lamiera.ActiveChanged`) aggiungi `_catalog.NotifyChanged();`, così la scheda Spazi aggiorna i pulsanti disabilitati.

5. In `OnDestroy` aggiungi `if (_shell != null) Destroy(_shell.gameObject);` e togli la riga che distruggeva `_badge` (la HUD è distrutta da `UiShell.OnDestroy`).

- [ ] **Step 5: Esegui l'intera suite EditMode**

Run: `& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"`
Expected: tutti PASS (182 esistenti + nuovi M6). In particolare `SceneConfigurationTests`, `HomePanelTests`, `QuestAcceptanceContractTests` e `WorkspaceVoiceTargetTests` restano verdi. Se `SceneConfigurationTests` controlla gli oggetti della scena generata, rigenera `Scenes/Main.unity` con il generatore in `Assets/XrSo/Editor/` e **non** modificarla a mano.

- [ ] **Step 6: Commit**

```bash
git add "Inventor XR SO/Assets/XrSo/Xr" "Inventor XR SO/Assets/XrSo/Runtime/Ui" "Inventor XR SO/Assets/XrSo/Tests/EditMode"
git commit -m "feat(xr): wire the M6 shell into the app with the Spaces tab and HUD"
```

---

### Task 9: Verifica sul Quest e verbale della Fase 1

**Files:**
- Create: `docs/xr-m6-verification.md`

- [ ] **Step 1: Suite complete**

```bash
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
```

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode-m6.xml`"" -Log "$env:TEMP\xrso-m6.log"
```

Expected: tutti PASS. Annota i conteggi.

- [ ] **Step 2: Build APK e regressioni sul Quest**

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoBuild.BuildApkBatch" -Log "$env:TEMP\xrso-build.log"
```

Poi esegui i runner M1–M5 come descritto in `docs/xr-quest-acceptance.md`, su fixture dedicate. Expected: ogni runner `PASS COMPLETE`, come prima della Fase 1 (i workspace non sono cambiati). Conserva manifest, log e screenshot; reinstalla l'APK ordinario e ripristina il documento Inventor precedente.

- [ ] **Step 3: Scrivi il verbale**

`docs/xr-m6-verification.md`:

```markdown
# Inventor XR SO — M6: verifica

Spec: [M6 UX spaziale](superpowers/specs/2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md).

## Fase 1 — Fondamenta (<data>)

Consegnato: catalogo azioni, stato della barra di conferma, inserimento numerico e layout della
postazione nel core; TextMeshPro; `XrInput` e aptica; `UiShell` con tavolozza sul controller
sinistro (sola scheda «Spazi»), barra di conferma (nascosta: nessun workspace migrato) e HUD al
posto di `StatusBadge`. I workspace e il menù polso sono invariati.

| Prova | Esito |
|---|---|
| Core `XrSo.Core.Tests` | <n>/<n> PASS |
| Unity EditMode | <n>/<n> PASS |
| Runner M1–M5 sul Quest | <esiti e file di log> |
| Tavolozza e HUD visti sul Quest | NOT COVERED: da osservare con la persona (leggibilità 8 mm, posa sul controller) |

Gate M6-01…M6-10: tutti **aperti**; la Fase 1 non ne chiude nessuno.
```

Sostituisci i segnaposto `<…>` con i valori reali dei passi 1–2 prima del commit.

- [ ] **Step 4: Commit**

```bash
git add docs/xr-m6-verification.md
git commit -m "docs(xr): M6 phase 1 verification record"
```

---

## Fasi successive (piani separati)

Ognuna avrà il suo piano in `docs/superpowers/plans/`, scritto quando la precedente è verificata, perché dipende dalla forma reale di `UiShell` e dal codice dei workspace in quel momento:

- **Fase 2 — Progettazione + Schizzo:** `DesignWorkspace` diventa `IActionProvider`; `SketchSheetView` (foglio sul tavolo, penna, «Vista modello»); chip valore + tastierino; anello contestuale; `Workbench` per le pose di parte e foglio.
- **Fase 3 — Lamiera:** `LamieraWorkspace` diventa `IActionProvider`; flangia con trascinamento a Trigger; sviluppo piano sul piano di lavoro; riverifica M5-08.
- **Fase 4 — Assieme + Ispezione:** assieme sollevato, `ComponentIsolation`, migrazione degli ultimi due workspace; la scheda Spazi sostituisce il menù polso; `WorkspaceVoiceTarget` usa `ActionCatalog.ResolveVoice`; runner migrati agli id azione.
- **Fase 5 — Collaudo:** runner M6, regressioni M1–M5, prova fisica M6-10.
