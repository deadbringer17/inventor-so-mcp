# M7 Ispeziona: verifica ingegneristica degli assiemi — piano di implementazione

> **Per gli agenti esecutori:** SUB-SKILL OBBLIGATORIA: usa superpowers:subagent-driven-development (consigliata) o superpowers:executing-plans per eseguire il piano un task alla volta. I passi usano le checkbox (`- [ ]`) per il tracciamento. Regola del repository (`CLAUDE.md`): il codice lo scrivono subagent Sonnet 5.5; l'agente principale rilegge ogni diff e riesegue i test prima del commit.

**Obiettivo:** aggiungere a Ispeziona, sul Quest, quattro verifiche di un assieme in sola lettura: visibilità (X-Ray, isola, nascondi), interferenze, distanza minima e salute dell'assieme con BOM. Interferenze, distanza e salute sono calcolate da Inventor.

**Architettura:** tre tool XR nuovi nel tier sperimentale del bridge, legati a revisione e id portabili. Nel core del client: DTO, backend e una macchina a stati per le verifiche, senza dipendenze da Unity. In Unity: `ComponentVisibility`, `VerifyOverlay` e due schede nuove di Ispeziona (Visibilità, Verifica). Infine fixture, sonde dal vivo e runner M7 sul Quest.

**Stack:** C# .NET 8 (server MCP), .NET 10 (add-in SO 2027, interop Inventor), netstandard2.1 / C# 9 (core XR), Unity 6000.6.3f1 (URP, EditMode NUnit), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-03-inventor-xr-so-m7-inspect-verifica-design.md`. Leggila prima di ogni task.

## Vincoli globali

- Branch: `feat/m7-inspect-verifica`.
- Ispeziona resta in sola lettura: nessun handler o azione M7 apre transazioni o cambia la revisione del documento.
- Unità: l'API Inventor lavora in cm; ogni input e output dei tool è in mm (volumi in mm³). Conversione al confine dell'handler: lunghezza mm = cm × 10, volume mm³ = cm³ × 1000.
- Coordinate dal bridge alla scena Unity: mm assieme → metri → X negata (`Handedness`): `new Vector3(-x/1000, y/1000, z/1000)` nello spazio locale della radice di `CadSceneView`.
- I tool nuovi sono sperimentali: handler in `bridge/src/shared/Handlers/Experimental/` sotto `#if INVENTOR2027 && SO_EXPERIMENTAL`, base `ExperimentalHandler`, contratto `Tier = Experimental` in `ToolContracts`, voce in `ExperimentalSourceTests`.
- Ogni tool XR richiede `document_id` + `expected_revision` e rifiuta con `DOCUMENT_CHANGED` / `STALE_REVISION` (`ConcurrencyFailure`). Mai scrivere un codice di errore nel messaggio (`SourcePolicyTests`).
- Interferenze e distanza lavorano solo sulle occorrenze di primo livello (fase 1).
- Testi UI in italiano; codice, commenti di codice e messaggi di commit in inglese, come il codice esistente. La documentazione di milestone è in italiano.
- Palette: massimo 8 azioni per scheda (`ActionCatalog.MaxPalette`). Le azioni M7 hanno `voiceInvokes: false`.
- Evidenze: tre esiti separati (test automatici, runner sul Quest con input **sintetico**, prova fisica). Una prova fisica non eseguita resta **aperta**. Fixture dedicata, mai documenti dell'utente.
- Commit: messaggi in stile del repo (`feat(xr): …`, `feat(bridge): …`, `test(…)`, `docs(xr): …`), chiusi da `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Comandi di test

```bash
dotnet test bridge/tests/Bimwright.Ipt.Tests
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
dotnet build bridge/src/plugin-so27 -c Debug -p:SoExperimental=true    # richiede Windows + interop Inventor 2027
dotnet build bridge/tests/QuestAcceptanceFixtures                       # richiede interop Inventor 2027
```

Test Unity EditMode (Windows, Unity chiuso):

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode.xml`""
```

Per un solo fixture di test aggiungi `"-testFilter","InventorXrSo.Tests.<Classe>"`.

## Mappa dei file

| File | Stato | Responsabilità |
|---|---|---|
| `bridge/tests/QuestAcceptanceFixtures/Fixtures.cs` | modifica | fixture `m7` (prepare/inspect/restore) |
| `bridge/tests/QuestAcceptanceFixtures/ProbeM7.cs` | nuovo | sonde dal vivo dei quattro rischi della spec |
| `bridge/tests/QuestAcceptanceFixtures/Program.cs` | modifica | `m7`, `--probe-m7`, `--probe-active` |
| `bridge/src/shared/Handlers/Experimental/VerifyXrHandlers.cs` | nuovo | `VerifyXr` (controlli comuni) + tre handler |
| `bridge/src/shared/Handlers/Experimental/AssemblyHandlers.cs` | modifica | estrae `AssemblyHealthReader` da `GetAssemblyHealthHandler` |
| `bridge/src/shared/Plugin/InventorCommandRegistry.Experimental.cs` | modifica | registra i tre handler |
| `bridge/src/server/Tools/XrTools.cs` | modifica | tre tool MCP `inventor_*_xr` |
| `bridge/src/server/ToolContracts.cs` | modifica | tre contratti sperimentali |
| `bridge/tests/Bimwright.Ipt.Tests/FakeAddIn.cs` | modifica | risposte finte dei comandi nuovi e di `get_assembly_bom` |
| `bridge/tests/Bimwright.Ipt.Tests/ExperimentalSourceTests.cs` | modifica | mappatura tool → handler |
| `bridge/tests/Bimwright.Ipt.Tests/RegistrationCountTests.cs` | modifica | superficie attesa 115 → 118 |
| `…core/Runtime/Backend/VerifyBackend.cs` | nuovo | DTO e `IVerifyBackend`, implementazione in `InventorBackend` |
| `…core/Runtime/Verify/VerifyJob.cs` | nuovo | `VerifyStatus`, `VerifyGate`, `IVerifyJob`, `VerifyJob<T>`, `VerifySession` |
| `…core/Runtime/Verify/VerifyFindings.cs` | nuovo | `VerifyFinding`, `VerifyFindings`, `VerifyMessages` |
| `Tests~/XrSo.Core.Tests/Backend/VerifyBackendTests.cs` | nuovo | end-to-end HTTPS + FakeAddIn |
| `Tests~/XrSo.Core.Tests/Verify/VerifyJobTests.cs` | nuovo | macchina a stati |
| `Tests~/XrSo.Core.Tests/Verify/VerifyFindingsTests.cs` | nuovo | righe e messaggi |
| `Assets/XrSo/Runtime/Scene/GhostBodies.cs` | nuovo | corpi in fantasma (estratto da `ComponentIsolation`) |
| `Assets/XrSo/Runtime/Scene/ComponentIsolation.cs` | modifica | usa `GhostBodies` |
| `Assets/XrSo/Runtime/Scene/ComponentVisibility.cs` | nuovo | Normale / Fantasma / Nascosto per occorrenza |
| `Assets/XrSo/Runtime/Scene/VerifyOverlay.cs` | nuovo | tinta rossa, box, linea di distanza, punti indicativi |
| `Assets/XrSo/Runtime/Scene/InspectionGeometry.cs` | modifica | `InstancesBounds` |
| `Assets/XrSo/Xr/InspectVerify.cs` | nuovo | parte M7 di `InspectWorkspace` (stato, job, focus) |
| `Assets/XrSo/Xr/InspectActions.cs` | modifica | schede Visibilità e Verifica, azioni M7 |
| `Assets/XrSo/Xr/InspectWorkspace.cs` | modifica | aggancio M7, etichette punto-punto, `Back` |
| `Assets/XrSo/Tests/EditMode/ComponentVisibilityTests.cs` | nuovo | |
| `Assets/XrSo/Tests/EditMode/VerifyOverlayTests.cs` | nuovo | |
| `Assets/XrSo/Tests/EditMode/InspectVerifyTests.cs` | nuovo | |
| `Assets/XrSo/Xr/Acceptance/M7QuestAcceptance.cs` | nuovo | runner M7 |
| `Assets/XrSo/Tests/EditMode/QuestAcceptanceContractTests.cs` | modifica | `M7QuestAcceptance` |
| `scripts/run-quest-acceptance.ps1` | modifica | `m7` |
| `docs/xr-m7-verification.md` | nuovo | verbale |
| `docs/xr-quest-acceptance.md`, `docs/DEVELOPMENT.md`, `CLAUDE.md` | modifica | documentazione |

`…core` = `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core`. Il progetto `Tests~/XrSo.Core/XrSo.Core.csproj` compila tutto `Runtime/**/*.cs`: nessuna modifica di progetto per i file nuovi del core.

---

### Task 1: fixture M7 e sonde dal vivo dei rischi

Prima del codice di produzione si costruisce la fixture e si misurano i quattro rischi della spec (durata, punti della distanza, box, occorrenze dei vincoli). L'esito decide due dettagli dei task 2–4, già previsti nel codice con un ripiego.

**File:**
- Modifica: `bridge/tests/QuestAcceptanceFixtures/Fixtures.cs`
- Nuovo: `bridge/tests/QuestAcceptanceFixtures/ProbeM7.cs`
- Modifica: `bridge/tests/QuestAcceptanceFixtures/Program.cs`
- Nuovo: `docs/xr-m7-verification.md`

**Interfacce:**
- Produce: fixture `XR_M7_Quest_Acceptance.iam` con occorrenze `M7_A`, `M7_B`, `M7_C`, `M7_D` di `XR_M7_Quest_Acceptance_Cube.ipt` (cubo 20 mm) e vincolo `M7_Sick`. Valori attesi: 1 interferenza `M7_A`–`M7_B` da 2000 mm³; distanza minima `M7_A`–`M7_C` = 30 mm; unico non vincolato `M7_D`; vincolo in errore `M7_Sick`; BOM con `PART_NUMBER_MISSING`.

- [ ] **Passo 1: aggiungi la fixture `m7` a `Fixtures.cs`**

Nello `switch` di `Prepare` aggiungi `"m7" => PrepareM7(app, directory, created),`. Poi aggiungi il metodo dopo `PrepareM6`:

```csharp
    /// <summary>
    /// M7: four 20 mm cubes of one part (blank part number). M7_A grounded at the origin; M7_B grounded and overlapping M7_A by
    /// 5 mm in X (2000 mm3); M7_C grounded 30 mm from M7_A along Y; M7_D free and unconstrained at X = 100 mm. M7_Sick is a flush
    /// constraint between the M7_Ref work planes of the two grounded cubes M7_B and M7_C with a 5 mm offset they cannot satisfy.
    /// </summary>
    private static JObject PrepareM7(global::Inventor.Application app, string directory, List<object> created)
    {
        var cube = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
        created.Add(cube);
        CreateBlock(app, cube, 20, 20, 20, false, "Cubo");   // x, y in [0, 20] mm, z in [0, 20] mm
        var def = cube.ComponentDefinition;
        var reference = def.WorkPlanes.AddByPlaneAndOffset(def.WorkPlanes[3], 1.0, false);   // z = 10 mm
        reference.Name = "M7_Ref";
        cube.PropertySets["Design Tracking Properties"]["Part Number"].Value = "";
        var cubePath = Path.Combine(directory, "XR_M7_Quest_Acceptance_Cube.ipt");
        cube.SaveAs(cubePath, false);

        var assembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
        created.Add(assembly);
        var tg = app.TransientGeometry;
        var occurrences = assembly.ComponentDefinition.Occurrences;
        ComponentOccurrence Place(string name, double xCm, double yCm, bool grounded)
        {
            var pose = tg.CreateMatrix();
            pose.SetTranslation(tg.CreateVector(xCm, yCm, 0));
            var occurrence = occurrences.Add(cubePath, pose);
            occurrence.Name = name;
            occurrence.Grounded = grounded;
            return occurrence;
        }
        Place("M7_A", 0, 0, true);
        var b = Place("M7_B", 1.5, 0, true);
        var c = Place("M7_C", 0, 5, true);
        Place("M7_D", 10, 0, false);

        object RefProxy(ComponentOccurrence occurrence)
        {
            occurrence.CreateGeometryProxy(((PartComponentDefinition)occurrence.Definition).WorkPlanes["M7_Ref"], out object proxy);
            return proxy;
        }
        var constraints = assembly.ComponentDefinition.Constraints;
        var flush = constraints.AddFlushConstraint(RefProxy(b), RefProxy(c), 0.5);
        flush.Name = "M7_Sick";
        assembly.Update();
        if (flush.HealthStatus == HealthStatusEnum.kUpToDateHealth)
            throw new InvalidOperationException("M7_Sick is healthy: the fixture needs a failing constraint. Record this in the probe log.");

        var assemblyPath = Path.Combine(directory, "XR_M7_Quest_Acceptance.iam");
        assembly.SaveAs(assemblyPath, false);
        assembly.Activate();
        var expected = new JObject
        {
            ["occurrences"] = 4, ["interference_pairs"] = 1, ["interference_volume_mm3"] = 2000,
            ["distance_a_c_mm"] = 30, ["unconstrained"] = new JArray("M7_D"), ["failing_constraint"] = "M7_Sick",
            ["bom_finding"] = "PART_NUMBER_MISSING", ["fixture_documents"] = 2,
        };
        return new JObject
        {
            ["assembly"] = assemblyPath, ["part"] = cubePath, ["documents"] = new JArray(assemblyPath, cubePath), ["expected"] = expected,
        };
    }
```

In `Inspect`, alla fine del ramo `active is AssemblyDocument assembly`, aggiungi:

```csharp
            if (m == "m7") ProbeM7.AddInspection(app, assembly, result);
```

- [ ] **Passo 2: crea `ProbeM7.cs`**

```csharp
using System.Diagnostics;
using Inventor;
using Newtonsoft.Json.Linq;

/// <summary>
/// Live probes for the M7 spec risks (read-only): interference duration, interference body boxes, closest points of the minimum
/// distance, occurrences of a failing constraint, and AnalyzeInterference with two sets. Prints JSON; nothing is saved.
/// </summary>
internal static class ProbeM7
{
    private static ComponentOccurrence Occurrence(AssemblyDocument assembly, string name) =>
        assembly.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>().First(o => o.Name == name);

    private static JArray Mm(Point p) => new JArray(p.X * 10, p.Y * 10, p.Z * 10);

    /// <summary>Independent Inventor reading of the values the Quest runner checks.</summary>
    internal static void AddInspection(global::Inventor.Application app, AssemblyDocument assembly, JObject result)
    {
        var def = assembly.ComponentDefinition;
        var all = app.TransientObjects.CreateObjectCollection();
        foreach (ComponentOccurrence o in def.Occurrences) all.Add(o);
        var interference = def.AnalyzeInterference(all);
        double volume = 0;
        for (int i = 1; i <= interference.Count; i++) volume += interference[i].Volume * 1000;
        result["interference_bodies"] = interference.Count;
        result["interference_volume_mm3"] = volume;
        result["distance_a_c_mm"] = app.MeasureTools.GetMinimumDistance(Occurrence(assembly, "M7_A"), Occurrence(assembly, "M7_C")) * 10;
        result["constraints"] = new JArray(def.Constraints.Cast<AssemblyConstraint>()
            .Select(c => (JToken)new JObject { ["name"] = c.Name, ["health"] = c.HealthStatus.ToString() }));
    }

    /// <summary>--probe-m7: the four risks on the open M7 fixture.</summary>
    internal static int Probe(global::Inventor.Application app)
    {
        if (app.ActiveDocument is not AssemblyDocument assembly || !assembly.DisplayName.StartsWith("XR_M7_Quest_Acceptance", StringComparison.Ordinal))
            throw new InvalidOperationException("Activate the M7 fixture first (--prepare-quest m7).");
        var def = assembly.ComponentDefinition;
        var output = new JObject();

        // Risk 1 and 3: duration and boxes of the interference bodies.
        var all = app.TransientObjects.CreateObjectCollection();
        foreach (ComponentOccurrence o in def.Occurrences) all.Add(o);
        var watch = Stopwatch.StartNew();
        var results = def.AnalyzeInterference(all);
        output["interference_ms"] = watch.ElapsedMilliseconds;
        var bodies = new JArray();
        for (int i = 1; i <= results.Count; i++)
        {
            var r = results[i];
            var item = new JObject { ["a"] = r.OccurrenceOne?.Name, ["b"] = r.OccurrenceTwo?.Name, ["volume_mm3"] = r.Volume * 1000 };
            try { var box = r.InterferenceBody.RangeBox; item["min_mm"] = Mm(box.MinPoint); item["max_mm"] = Mm(box.MaxPoint); }
            catch (Exception ex) { item["box_error"] = ex.GetType().Name + ": " + ex.Message; }
            bodies.Add(item);
        }
        output["interference"] = bodies;

        // Two sets: M7_B against the others (the "solo selezione" scope).
        try
        {
            var set1 = app.TransientObjects.CreateObjectCollection();
            var set2 = app.TransientObjects.CreateObjectCollection();
            foreach (ComponentOccurrence o in def.Occurrences) (o.Name == "M7_B" ? set1 : set2).Add(o);
            output["two_sets_count"] = def.AnalyzeInterference(set1, set2).Count;
        }
        catch (Exception ex) { output["two_sets_error"] = ex.GetType().Name + ": " + ex.Message; }

        // Risk 2: closest points from the context of GetMinimumDistance.
        try
        {
            var context = app.TransientObjects.CreateNameValueMap();
            double cm = app.MeasureTools.GetMinimumDistance(Occurrence(assembly, "M7_A"), Occurrence(assembly, "M7_C"),
                InferredTypeEnum.kNoInference, InferredTypeEnum.kNoInference, context);
            output["distance_mm"] = cm * 10;
            var entries = new JObject();
            for (int i = 1; i <= context.Count; i++)
            {
                string name = context.Name[i];
                object value = context.Value[name];
                entries[name] = value is Point p ? Mm(p) : JToken.FromObject(value?.ToString() ?? "null");
            }
            output["distance_context"] = entries;
        }
        catch (Exception ex) { output["distance_context_error"] = ex.GetType().Name + ": " + ex.Message; }

        // Risk 4: the occurrences of the failing constraint.
        var constraints = new JArray();
        foreach (AssemblyConstraint c in def.Constraints)
        {
            var item = new JObject { ["name"] = c.Name, ["health"] = c.HealthStatus.ToString() };
            try { item["occurrence_one"] = c.OccurrenceOne?.Name; item["occurrence_two"] = c.OccurrenceTwo?.Name; }
            catch (Exception ex) { item["occurrence_error"] = ex.GetType().Name + ": " + ex.Message; }
            constraints.Add(item);
        }
        output["constraints"] = constraints;
        Console.WriteLine(output.ToString());
        return 0;
    }

    /// <summary>--probe-active: duration only, on whatever assembly the user activated. Read-only, nothing saved.</summary>
    internal static int ProbeActive(global::Inventor.Application app)
    {
        if (app.ActiveDocument is not AssemblyDocument assembly) throw new InvalidOperationException("Activate an assembly first.");
        var def = assembly.ComponentDefinition;
        var all = app.TransientObjects.CreateObjectCollection();
        int count = 0;
        foreach (ComponentOccurrence o in def.Occurrences) if (!o.Suppressed) { all.Add(o); count++; }
        var watch = Stopwatch.StartNew();
        var results = def.AnalyzeInterference(all);
        long interferenceMs = watch.ElapsedMilliseconds;
        watch.Restart();
        int failing = def.Constraints.Cast<AssemblyConstraint>().Count(c => !c.Suppressed && c.HealthStatus != HealthStatusEnum.kUpToDateHealth);
        foreach (ComponentOccurrence o in def.Occurrences)
            if (!o.Suppressed) o.GetDegreesOfFreedom(out int _, out ObjectsEnumerator _, out int _, out ObjectsEnumerator _, out Point _);
        long healthMs = watch.ElapsedMilliseconds;
        Console.WriteLine(new JObject
        {
            ["document"] = assembly.DisplayName, ["top_level_occurrences"] = count, ["interference_bodies"] = results.Count,
            ["interference_ms"] = interferenceMs, ["failing_constraints"] = failing, ["health_ms"] = healthMs,
        }.ToString());
        return 0;
    }
}
```

- [ ] **Passo 3: aggiorna `Program.cs`**

Sostituisci il corpo del `try` di `Main` con:

```csharp
            var clsid = Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            if (args.Contains("--probe-m7") || args.Contains("--probe-active"))
            {
                GetActiveObject(ref clsid, IntPtr.Zero, out var probed);
                var probeApp = (global::Inventor.Application)probed;
                return args.Contains("--probe-m7") ? ProbeM7.Probe(probeApp) : ProbeM7.ProbeActive(probeApp);
            }
            var mode = args.FirstOrDefault(a => a is "--prepare-quest" or "--inspect-quest" or "--restore-quest");
            var index = Array.IndexOf(args, mode);
            var milestone = mode == null || index + 1 >= args.Length ? null : args[index + 1].ToLowerInvariant();
            if (mode == null || milestone is not ("m1" or "m2" or "m3" or "m5" or "m6" or "m7"))
            {
                Console.Error.WriteLine("Usage: QuestAcceptanceFixtures (--prepare-quest|--inspect-quest|--restore-quest) <m1|m2|m3|m5|m6|m7> | --probe-m7 | --probe-active");
                return 64;
            }
            GetActiveObject(ref clsid, IntPtr.Zero, out var active);
            var app = (global::Inventor.Application)active;
            return mode switch
            {
                "--prepare-quest" => Fixtures.Prepare(app, milestone),
                "--inspect-quest" => Fixtures.Inspect(app, milestone),
                _ => Fixtures.Restore(app, milestone),
            };
```

- [ ] **Passo 4: compila**

Run: `dotnet build bridge/tests/QuestAcceptanceFixtures`
Expected: `Build succeeded`. Se `InferredTypeEnum`, `context.Name[i]` o `AnalyzeInterference(set1, set2)` non compilano contro l'interop installata, passa per `dynamic` quella sola chiamata (`((dynamic)app.MeasureTools).GetMinimumDistance(...)`) e annota l'errore nel verbale.

- [ ] **Passo 5: esegui fixture e sonde (richiede Inventor 2027 aperto)**

Se Inventor non è aperto, fermati e chiedi all'utente di aprirlo. Non attivare documenti dell'utente.

```bash
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest m7
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --inspect-quest m7
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --probe-m7
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --restore-quest m7
```

Expected:
- `--inspect-quest`: `interference_volume_mm3` ≈ 2000, `distance_a_c_mm` ≈ 30, `M7_Sick` con salute diversa da `kUpToDateHealth`.
- `--probe-m7`: stampa `interference_ms`, i box, `two_sets_count`, il contenuto di `distance_context` e le occorrenze di `M7_Sick`.

Se `PrepareM7` fallisce con "M7_Sick is healthy", sostituisci la costruzione del vincolo: crea il flush con offset `0`, poi elimina `M7_Ref` dalla parte (`def.WorkPlanes["M7_Ref"].Delete()`), `assembly.Update()`, ricontrolla la salute. Registra quale metodo funziona.

`--probe-active` richiede un assieme reale dell'utente aperto e attivo: chiedi all'utente di aprirlo, eseguilo, registra il risultato. Se l'utente non può, registra `NOT COVERED [M7-07] assieme reale non misurato`.

- [ ] **Passo 6: crea `docs/xr-m7-verification.md`**

```markdown
# Inventor XR SO — verifica M7 (Ispeziona, verifica ingegneristica)

Spec: [M7](superpowers/specs/2026-10-03-inventor-xr-so-m7-inspect-verifica-design.md).
Piano: [M7](superpowers/plans/2026-10-03-inventor-xr-so-m7-inspect-verifica.md).

## Stato dei gate

| Gate | Esito | Evidenza |
|---|---|---|
| M7-01 | APERTO | |
| M7-02 | APERTO | |
| M7-03 | APERTO | |
| M7-04 | APERTO | |
| M7-05 | APERTO | |
| M7-06 | APERTO | |
| M7-07 | APERTO | |
| M7-08 | APERTO (prova fisica) | |

## Sonde dal vivo (task 1)

Data, versione Inventor, comando eseguito e output JSON di `--inspect-quest m7`,
`--probe-m7` e, se eseguito, `--probe-active`. Per ognuno dei quattro rischi
della spec: esito e decisione presa.
```

Compila la sezione "Sonde dal vivo" con l'output reale. Per ogni rischio scrivi una riga: "Durata: X ms su fixture, Y ms su <assieme> (N occorrenze)"; "Box: leggibili sì/no"; "Punti distanza: chiavi `<nomi>` oppure non disponibili"; "Occorrenze del vincolo: sì/no"; "Due insiemi: sì/no".

- [ ] **Passo 7: commit**

```bash
git add bridge/tests/QuestAcceptanceFixtures docs/xr-m7-verification.md
git commit -m "test(xr): M7 Quest fixture and live probes of the spec risks"
```

**Decisioni che i task successivi leggono dal verbale:**
- chiavi dei punti più vicini nel `NameValueMap` (default nel codice: `ClosestPointOne` / `ClosestPointTwo`);
- se `AnalyzeInterference(set1, set2)` funziona (se no, "solo selezione" passa la selezione più tutti gli altri come insieme unico e filtra le coppie che contengono la selezione).

---

### Task 2: bridge, `inventor_check_interference_xr`

**File:**
- Nuovo: `bridge/src/shared/Handlers/Experimental/VerifyXrHandlers.cs`
- Modifica: `bridge/src/shared/Plugin/InventorCommandRegistry.Experimental.cs`
- Modifica: `bridge/src/server/Tools/XrTools.cs`
- Modifica: `bridge/src/server/ToolContracts.cs`
- Modifica: `bridge/tests/Bimwright.Ipt.Tests/FakeAddIn.cs`
- Test: `bridge/tests/Bimwright.Ipt.Tests/ExperimentalSourceTests.cs`, `bridge/tests/Bimwright.Ipt.Tests/RegistrationCountTests.cs`

**Interfacce:**
- Produce (wire `check_interference_xr`, tool `inventor_check_interference_xr`): input `document_id`, `expected_revision`, `occurrence_ids?: string[]`. Output `{document_id, revision, analyzed, count, total_volume_mm3, elapsed_ms, pairs: [{a_occurrence_id, b_occurrence_id, a_name, b_name, volume_mm3, boxes: [{min_mm:[x,y,z], max_mm:[x,y,z]}]}]}`.
- Produce: `VerifyXr` (static interno) con `Assembly(...)`, `Direct(...)`, `DirectOccurrence(...)`, `Top(...)`, `Mm(Point)`, usato dai task 3 e 4.

- [ ] **Passo 1: test che falliscono**

In `ExperimentalSourceTests.ExperimentalToolsReachAnExperimentalHandler` aggiungi:

```csharp
    [InlineData("inventor_check_interference_xr", "check_interference_xr")]
```

In `RegistrationCountTests`, nella lista `expected`, sostituisci la riga `"inventor_get_assembly_context_xr",` con:

```csharp
            "inventor_get_assembly_context_xr",
            "inventor_check_interference_xr",
```

aggiorna il commento `// xr (13)` in `// xr (14)` e `Assert.Equal(115, expected.Length);` in `Assert.Equal(116, expected.Length);`.

- [ ] **Passo 2: verifica che falliscano**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~ExperimentalSourceTests|FullyQualifiedName~RegistrationCountTests"`
Expected: FAIL (tier non sperimentale per il tool nuovo; tool mancante nella superficie).

- [ ] **Passo 3: crea `VerifyXrHandlers.cs` con `VerifyXr` e `CheckInterferenceXrHandler`**

```csharp
#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Shared checks of the M7 verification handlers: active assembly, document id and revision, direct occurrences.</summary>
internal static class VerifyXr
{
    public static AssemblyDocument Assembly(InventorCommandContext ctx, Application app, JObject p, string command, out string documentId)
    {
        var assembly = X.ActiveAssembly(app, command);
        documentId = EntityReferences.DocumentId((global::Inventor.Document)assembly);
        if (X.Str(p, "document_id") != documentId) throw ConcurrencyFailure.DocumentChanged((string?)p["document_id"], documentId);
        string revision = X.Str(p, "expected_revision");
        if (ctx.Events == null || ctx.Events.Revision(documentId) != revision)
            throw ConcurrencyFailure.StaleRevision(revision, ctx.Events?.Revision(documentId));
        return assembly;
    }

    public static ComponentOccurrence[] Direct(AssemblyDocument assembly) =>
        assembly.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>().ToArray();

    public static ComponentOccurrence DirectOccurrence(AssemblyDocument assembly, ComponentOccurrence[] direct, string? id)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("An occurrence id is required.");
        var occurrence = EntityReferences.ResolveOccurrence((global::Inventor.Document)assembly, id);
        if (!direct.Any(o => ReferenceEquals(o, occurrence)))
            throw new ArgumentException("M7 verifications take direct occurrences of the active assembly: " + id);
        return occurrence;
    }

    /// <summary>The direct occurrence that contains <paramref name="occurrence"/> (itself when it is direct); null if unreadable.</summary>
    public static ComponentOccurrence? Top(ComponentOccurrence? occurrence)
    {
        try { while (occurrence?.ParentOccurrence != null) occurrence = occurrence.ParentOccurrence; }
        catch { return null; }
        return occurrence;
    }

    public static string? Id(AssemblyDocument assembly, ComponentOccurrence? occurrence) =>
        occurrence == null ? null : X.Describe((global::Inventor.Document)assembly, occurrence);

    public static JArray Mm(Point p) => new JArray(p.X * 10, p.Y * 10, p.Z * 10);
}

/// <summary>
/// <c>check_interference_xr</c>: Inventor interference analysis of direct occurrences, revision-bound, with portable ids and the
/// range box of every interference body. Without occurrence_ids every unsuppressed direct occurrence is analysed against the
/// others; with occurrence_ids those occurrences are analysed against every other unsuppressed direct occurrence. Read-only.
/// </summary>
public sealed class CheckInterferenceXrHandler : ExperimentalHandler
{
    public override string Name => "check_interference_xr";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = VerifyXr.Assembly(ctx, app, p, Name, out string documentId);
        var def = assembly.ComponentDefinition;
        var direct = VerifyXr.Direct(assembly).Where(o => !o.Suppressed).ToArray();
        var selected = (p["occurrence_ids"] as JArray)?.Select(t => (string?)t).Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? Array.Empty<string?>();

        var set1 = app.TransientObjects.CreateObjectCollection();
        var set2 = app.TransientObjects.CreateObjectCollection();
        if (selected.Length == 0) foreach (var o in direct) set1.Add(o);
        else
        {
            var chosen = selected.Select(id => VerifyXr.DirectOccurrence(assembly, VerifyXr.Direct(assembly), id)).ToArray();
            foreach (var o in direct) (chosen.Any(c => ReferenceEquals(c, o)) ? set1 : set2).Add(o);
        }
        int analyzed = set1.Count + set2.Count;
        var watch = Stopwatch.StartNew();
        var result = new JObject { ["document_id"] = documentId, ["revision"] = ctx.Events!.Revision(documentId), ["analyzed"] = analyzed };
        if (set1.Count == 0 || (selected.Length == 0 && set1.Count < 2) || (selected.Length > 0 && set2.Count == 0))
        {
            result["count"] = 0; result["total_volume_mm3"] = 0.0; result["elapsed_ms"] = 0; result["pairs"] = new JArray();
            return result;
        }
        X.Deadline(ctx, "before interference analysis");
        InterferenceResults results = selected.Length == 0 ? def.AnalyzeInterference(set1) : def.AnalyzeInterference(set1, set2);

        var pairs = new Dictionary<(string, string), JObject>();
        double total = 0;
        for (int i = 1; i <= results.Count; i++)
        {
            InterferenceResult r = results[i];
            var one = VerifyXr.Top(r.OccurrenceOne);
            var two = VerifyXr.Top(r.OccurrenceTwo);
            string? oneId = VerifyXr.Id(assembly, one), twoId = VerifyXr.Id(assembly, two);
            var (aId, bId, aName, bName) = string.CompareOrdinal(oneId, twoId) <= 0
                ? (oneId, twoId, one?.Name, two?.Name) : (twoId, oneId, two?.Name, one?.Name);
            double volume = r.Volume * 1000;
            total += volume;
            var key = (aId ?? "?", bId ?? "?");
            if (!pairs.TryGetValue(key, out var pair))
            {
                pair = new JObject
                {
                    ["a_occurrence_id"] = aId, ["b_occurrence_id"] = bId, ["a_name"] = aName, ["b_name"] = bName,
                    ["volume_mm3"] = 0.0, ["boxes"] = new JArray(),
                };
                pairs[key] = pair;
            }
            pair["volume_mm3"] = (double)pair["volume_mm3"]! + volume;
            try
            {
                Box box = r.InterferenceBody.RangeBox;
                ((JArray)pair["boxes"]!).Add(new JObject { ["min_mm"] = VerifyXr.Mm(box.MinPoint), ["max_mm"] = VerifyXr.Mm(box.MaxPoint) });
            }
            catch { /* the box is optional: the pair is still reported */ }
        }
        result["count"] = pairs.Count;
        result["total_volume_mm3"] = total;
        result["elapsed_ms"] = watch.ElapsedMilliseconds;
        result["pairs"] = new JArray(pairs.Values);
        return result;
    }
}
#endif
```

Se il task 1 ha registrato che `AnalyzeInterference(set1, set2)` non funziona, sostituisci il ramo con selezione: un solo insieme con tutte le occorrenze dirette non soppresse e, dopo l'aggregazione, tieni solo le coppie in cui `a_occurrence_id` o `b_occurrence_id` è tra gli id selezionati.

- [ ] **Passo 4: registra handler, tool e contratto**

In `InventorCommandRegistry.Experimental.cs`, dopo `add(new ActivateOpenDocumentXrHandler());`:

```csharp
        add(new CheckInterferenceXrHandler());
```

In `XrTools.cs`, dopo il tool `inventor_activate_open_document_xr`:

```csharp
    [McpServerTool(Name = "inventor_check_interference_xr"), Description("Revision-bound Inventor interference analysis of the active assembly's direct occurrences for XR. Without occurrence_ids every unsuppressed direct occurrence is analysed; with occurrence_ids (portable ids of direct occurrences) those are analysed against all the others. Returns analyzed, count, total_volume_mm3, elapsed_ms and pairs with portable a/b occurrence ids, names, volume_mm3 and the range box (min_mm/max_mm, assembly millimetres) of each interference body. A subassembly counts as one unit. No CAD changes. Experimental.")]
    public Task<string> CheckInterferenceXr(string document_id, string expected_revision, string[]? occurrence_ids = null, CancellationToken ct = default)
        => Call("check_interference_xr", new JObject
        {
            ["document_id"] = document_id, ["expected_revision"] = expected_revision,
            ["occurrence_ids"] = occurrence_ids is null ? null : new JArray(occurrence_ids),
        }, ct);
```

In `ToolContracts.cs`, dopo la riga di `inventor_get_assembly_context_xr`:

```csharp
        new() { Name = "inventor_check_interference_xr", Tier = Experimental, Access = "query", RequiresRevision = true, Documents = As, Verification = Pending },
```

- [ ] **Passo 5: risposta finta in `FakeAddIn.cs`**

Nello `switch` di `Handle`, dopo `case "inspect_xr":` (blocco completo), aggiungi:

```csharp
            case "check_interference_xr":
                if ((string?)p["document_id"] != AssemblyId) return Fail(InventorErrorCodes.DOCUMENT_CHANGED, "Active document changed.");
                if ((string?)p["expected_revision"] != Revision) return Fail(InventorErrorCodes.STALE_REVISION, "Revision changed.");
                return Ok(new JObject
                {
                    ["document_id"] = AssemblyId, ["revision"] = Revision, ["analyzed"] = 3, ["count"] = 1, ["total_volume_mm3"] = 2000.0, ["elapsed_ms"] = 12,
                    ["pairs"] = new JArray(new JObject
                    {
                        ["a_occurrence_id"] = "ent_occ_1", ["b_occurrence_id"] = "ent_occ_3", ["a_name"] = "Bolt:1", ["b_name"] = "Plate:1",
                        ["volume_mm3"] = 2000.0,
                        ["boxes"] = new JArray(new JObject { ["min_mm"] = new JArray(15, 0, 0), ["max_mm"] = new JArray(20, 20, 20) }),
                    }),
                });
```

- [ ] **Passo 6: test verdi e build dell'add-in**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests`
Expected: PASS (su Linux falliscono solo i 10 test di semantica dei path Windows elencati in `bridge/CLAUDE.md`).

Run: `dotnet build bridge/src/plugin-so27 -c Debug -p:SoExperimental=true`
Expected: `Build succeeded`.

- [ ] **Passo 7: commit**

```bash
git add bridge/src bridge/tests/Bimwright.Ipt.Tests
git commit -m "feat(bridge): inventor_check_interference_xr, revision-bound interference with portable ids and boxes"
```

---

### Task 3: bridge, `inventor_measure_min_distance_xr`

**File:**
- Modifica: `bridge/src/shared/Handlers/Experimental/VerifyXrHandlers.cs`
- Modifica: `InventorCommandRegistry.Experimental.cs`, `XrTools.cs`, `ToolContracts.cs`, `FakeAddIn.cs`
- Test: `ExperimentalSourceTests.cs`, `RegistrationCountTests.cs`

**Interfacce:**
- Consuma: `VerifyXr` (task 2).
- Produce (wire `measure_min_distance_xr`, tool `inventor_measure_min_distance_xr`): input `document_id`, `expected_revision`, `a_occurrence_id`, `b_occurrence_id`. Output `{document_id, revision, distance_mm, point_a: [x,y,z] | null, point_b: [x,y,z] | null, points_source: "inventor" | "unavailable"}`.

- [ ] **Passo 1: test che falliscono**

`ExperimentalSourceTests`: aggiungi `[InlineData("inventor_measure_min_distance_xr", "measure_min_distance_xr")]`.
`RegistrationCountTests`: aggiungi `"inventor_measure_min_distance_xr",` dopo `"inventor_check_interference_xr",`, commento `// xr (15)`, `Assert.Equal(117, expected.Length);`.

- [ ] **Passo 2: verifica che falliscano**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~ExperimentalSourceTests|FullyQualifiedName~RegistrationCountTests"`
Expected: FAIL.

- [ ] **Passo 3: aggiungi l'handler in `VerifyXrHandlers.cs`** (prima di `#endif`)

```csharp
/// <summary>
/// <c>measure_min_distance_xr</c>: Inventor minimum distance between two direct occurrences, revision-bound. The closest points come
/// from the NameValueMap context of GetMinimumDistance when the interop fills it (late bound: a failure leaves them null and never
/// fails the measurement). Read-only.
/// </summary>
public sealed class MeasureMinDistanceXrHandler : ExperimentalHandler
{
    // Keys confirmed or replaced by the live probe recorded in docs/xr-m7-verification.md (task 1).
    private const string PointOneKey = "ClosestPointOne", PointTwoKey = "ClosestPointTwo";

    public override string Name => "measure_min_distance_xr";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = VerifyXr.Assembly(ctx, app, p, Name, out string documentId);
        var direct = VerifyXr.Direct(assembly);
        var a = VerifyXr.DirectOccurrence(assembly, direct, (string?)p["a_occurrence_id"]);
        var b = VerifyXr.DirectOccurrence(assembly, direct, (string?)p["b_occurrence_id"]);
        if (ReferenceEquals(a, b)) throw new ArgumentException("Choose two different occurrences.");
        if (a.Suppressed || b.Suppressed) throw new ArgumentException("A suppressed occurrence has no geometry to measure.");
        X.Deadline(ctx, "before minimum distance");

        JToken pointA = JValue.CreateNull(), pointB = JValue.CreateNull();
        double cm;
        try
        {
            var context = app.TransientObjects.CreateNameValueMap();
            cm = ((dynamic)app.MeasureTools).GetMinimumDistance(a, b, InferredTypeEnum.kNoInference, InferredTypeEnum.kNoInference, context);
            if (context.Value[PointOneKey] is Point one && context.Value[PointTwoKey] is Point two)
            {
                pointA = VerifyXr.Mm(one); pointB = VerifyXr.Mm(two);
            }
        }
        catch (Exception ex) when (ex is not CodedFailureException)
        {
            cm = app.MeasureTools.GetMinimumDistance(a, b);
        }
        bool points = pointA.Type == JTokenType.Array && pointB.Type == JTokenType.Array;
        return new JObject
        {
            ["document_id"] = documentId, ["revision"] = ctx.Events!.Revision(documentId), ["distance_mm"] = cm * 10,
            ["point_a"] = pointA, ["point_b"] = pointB, ["points_source"] = points ? "inventor" : "unavailable",
        };
    }
}
```

Se il task 1 ha registrato chiavi diverse, aggiorna le due costanti. Se ha registrato che il contesto non restituisce punti, lascia il codice: il risultato sarà sempre `points_source: "unavailable"` e il client disegna la linea indicativa.

- [ ] **Passo 4: registra handler, tool e contratto**

Registrar: `add(new MeasureMinDistanceXrHandler());` dopo `CheckInterferenceXrHandler`.

`XrTools.cs`:

```csharp
    [McpServerTool(Name = "inventor_measure_min_distance_xr"), Description("Revision-bound Inventor minimum distance (mm) between two direct occurrences of the active assembly (portable ids) for XR. Returns distance_mm and, when Inventor provides them, the closest points point_a/point_b in assembly millimetres (points_source inventor), otherwise null points (points_source unavailable). Expect 0 on touching or interfering parts. No CAD changes. Experimental.")]
    public Task<string> MeasureMinDistanceXr(string document_id, string expected_revision, string a_occurrence_id, string b_occurrence_id, CancellationToken ct = default)
        => Call("measure_min_distance_xr", new JObject
        {
            ["document_id"] = document_id, ["expected_revision"] = expected_revision,
            ["a_occurrence_id"] = a_occurrence_id, ["b_occurrence_id"] = b_occurrence_id,
        }, ct);
```

`ToolContracts.cs`:

```csharp
        new() { Name = "inventor_measure_min_distance_xr", Tier = Experimental, Access = "query", RequiresRevision = true, Documents = As, Verification = Pending },
```

- [ ] **Passo 5: risposta finta in `FakeAddIn.cs`**

```csharp
            case "measure_min_distance_xr":
                if ((string?)p["document_id"] != AssemblyId) return Fail(InventorErrorCodes.DOCUMENT_CHANGED, "Active document changed.");
                if ((string?)p["expected_revision"] != Revision) return Fail(InventorErrorCodes.STALE_REVISION, "Revision changed.");
                if ((string?)p["a_occurrence_id"] == (string?)p["b_occurrence_id"]) return Fail(InventorErrorCodes.INVALID_ARGUMENT, "Choose two different occurrences.");
                return Ok(new JObject
                {
                    ["document_id"] = AssemblyId, ["revision"] = Revision, ["distance_mm"] = 30.0,
                    ["point_a"] = new JArray(0, 0, 0), ["point_b"] = new JArray(30, 0, 0), ["points_source"] = "inventor",
                });
```

- [ ] **Passo 6: test verdi e build dell'add-in**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests` → PASS.
Run: `dotnet build bridge/src/plugin-so27 -c Debug -p:SoExperimental=true` → `Build succeeded`.

- [ ] **Passo 7: commit**

```bash
git add bridge/src bridge/tests/Bimwright.Ipt.Tests
git commit -m "feat(bridge): inventor_measure_min_distance_xr with optional closest points"
```

---

### Task 4: bridge, `inventor_assembly_health_xr`

**File:**
- Modifica: `bridge/src/shared/Handlers/Experimental/AssemblyHandlers.cs` (estrae `AssemblyHealthReader`)
- Modifica: `bridge/src/shared/Handlers/Experimental/VerifyXrHandlers.cs`
- Modifica: registrar, `XrTools.cs`, `ToolContracts.cs`, `FakeAddIn.cs`
- Test: `ExperimentalSourceTests.cs`, `RegistrationCountTests.cs`

**Interfacce:**
- Produce (wire `assembly_health_xr`): `{document_id, revision, healthy, occurrence_count, unconstrained_occurrences, constraint_count, joint_count, failing_constraints: [{name, health, a_occurrence_id, b_occurrence_id}], failing_joints: [stesso formato], occurrences: [{name, occurrence_id, suppressed, grounded, dof_translation, dof_rotation, unconstrained}]}`.
- Produce (tool `inventor_assembly_health_xr`): l'output del wire più `bom: {valid, findings: [{severity, code, message, …}], …}` da `BomAnalysis.Validate`. Se la revisione cambia tra le letture: errore `STALE_REVISION`.

- [ ] **Passo 1: test che falliscono**

`ExperimentalSourceTests`: `[InlineData("inventor_assembly_health_xr", "assembly_health_xr")]`.
`RegistrationCountTests`: `"inventor_assembly_health_xr",` dopo `"inventor_measure_min_distance_xr",`, commento `// xr (16)`, `Assert.Equal(118, expected.Length);`.

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~ExperimentalSourceTests|FullyQualifiedName~RegistrationCountTests"` → FAIL.

- [ ] **Passo 2: estrai `AssemblyHealthReader` in `AssemblyHandlers.cs`**

Sostituisci il corpo di `GetAssemblyHealthHandler.Run` con una chiamata al lettore condiviso, e aggiungi il lettore subito dopo la classe. L'output di `get_assembly_health` non cambia quando `withIds` è falso.

```csharp
public sealed class GetAssemblyHealthHandler : ExperimentalHandler
{
    public override string Name => "get_assembly_health";
    public override bool IsReadOnly => true;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
        => AssemblyHealthReader.Read(ctx, X.ActiveAssembly(app, Name), p, withIds: false);
}

/// <summary>DOF, grounding and constraint/joint health of an assembly. With ids, failing relationships carry the direct occurrences they bind.</summary>
internal static class AssemblyHealthReader
{
    public static JObject Read(InventorCommandContext ctx, AssemblyDocument assembly, JObject p, bool withIds)
    {
        var def = assembly.ComponentDefinition;
        int max = p["max_occurrences"]?.Type == JTokenType.Integer ? Math.Max(1, Math.Min(20000, (int)p["max_occurrences"]!)) : 2000;
        var occurrences = new JArray();
        int unconstrained = 0;
        foreach (ComponentOccurrence occurrence in def.Occurrences)
        {
            if (occurrences.Count >= max) break;
            X.Deadline(ctx, "while reading assembly health");
            var item = new JObject { ["name"] = occurrence.Name, ["occurrence_id"] = X.Describe((global::Inventor.Document)assembly, occurrence),
                ["suppressed"] = occurrence.Suppressed, ["grounded"] = occurrence.Grounded };
            bool free = false;
            if (!occurrence.Suppressed)
            {
                try
                {
                    occurrence.GetDegreesOfFreedom(out int translations, out ObjectsEnumerator _, out int rotations, out ObjectsEnumerator _, out Point _);
                    item["dof_translation"] = translations;
                    item["dof_rotation"] = rotations;
                    free = !occurrence.Grounded && translations + rotations == 6;
                    if (free) unconstrained++;
                }
                catch { item["dof_translation"] = null; }
            }
            if (withIds) item["unconstrained"] = free;
            occurrences.Add(item);
        }
        var failingConstraints = new JArray();
        int constraintCount = 0;
        foreach (AssemblyConstraint constraint in def.Constraints)
        {
            constraintCount++;
            if (constraint.Suppressed || constraint.HealthStatus == HealthStatusEnum.kUpToDateHealth) continue;
            var item = new JObject { ["name"] = constraint.Name, ["health"] = constraint.HealthStatus.ToString() };
            if (withIds) AddPair(assembly, item, () => constraint.OccurrenceOne, () => constraint.OccurrenceTwo);
            failingConstraints.Add(item);
        }
        var failingJoints = new JArray();
        int jointCount = 0;
        foreach (AssemblyJoint joint in def.Joints)
        {
            jointCount++;
            if (joint.Suppressed || joint.HealthStatus == HealthStatusEnum.kUpToDateHealth) continue;
            var item = new JObject { ["name"] = joint.Name, ["health"] = joint.HealthStatus.ToString() };
            if (withIds) AddPair(assembly, item, () => joint.OccurrenceOne, () => joint.OccurrenceTwo);
            failingJoints.Add(item);
        }
        return new JObject
        {
            ["healthy"] = failingConstraints.Count == 0 && failingJoints.Count == 0,
            ["occurrence_count"] = def.Occurrences.Count,
            ["unconstrained_occurrences"] = unconstrained,
            ["constraint_count"] = constraintCount,
            ["joint_count"] = jointCount,
            ["failing_constraints"] = failingConstraints,
            ["failing_joints"] = failingJoints,
            ["occurrences"] = occurrences,
        };
    }

    // A sick relationship may not expose its occurrences: the ids stay null and the row is shown without highlight.
    private static void AddPair(AssemblyDocument assembly, JObject item, Func<ComponentOccurrence?> one, Func<ComponentOccurrence?> two)
    {
        item["a_occurrence_id"] = Safe(assembly, one);
        item["b_occurrence_id"] = Safe(assembly, two);
    }

    private static JToken Safe(AssemblyDocument assembly, Func<ComponentOccurrence?> read)
    {
        try { return (JToken?)VerifyXr.Id(assembly, VerifyXr.Top(read())) ?? JValue.CreateNull(); }
        catch { return JValue.CreateNull(); }
    }
}
```

`VerifyXr` vive in `VerifyXrHandlers.cs`, nello stesso namespace e sotto lo stesso `#if`: nessun `using` da aggiungere.

- [ ] **Passo 3: aggiungi `AssemblyHealthXrHandler` in `VerifyXrHandlers.cs`**

```csharp
/// <summary><c>assembly_health_xr</c>: revision-bound assembly health with portable ids on failing relationships. Read-only.</summary>
public sealed class AssemblyHealthXrHandler : ExperimentalHandler
{
    public override string Name => "assembly_health_xr";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = VerifyXr.Assembly(ctx, app, p, Name, out string documentId);
        var result = AssemblyHealthReader.Read(ctx, assembly, p, withIds: true);
        result["document_id"] = documentId;
        result["revision"] = ctx.Events!.Revision(documentId);
        return result;
    }
}
```

Registrar: `add(new AssemblyHealthXrHandler());` dopo `MeasureMinDistanceXrHandler`.

- [ ] **Passo 4: tool server con BOM e ricontrollo della revisione**

In `XrTools.cs` aggiungi `using Bimwright.Ipt.Server.Planning;` in testa e il tool:

```csharp
    [McpServerTool(Name = "inventor_assembly_health_xr"), Description("Revision-bound health of the active assembly for XR: per-occurrence DOF, grounding and an unconstrained flag, failing constraints and joints with the portable ids of the direct occurrences they bind (null when Inventor does not expose them), plus bom = the inventor_validate_bom result. Refuses with STALE_REVISION if the document changed while reading. No CAD changes. Experimental.")]
    public async Task<string> AssemblyHealthXr(string document_id, string expected_revision, int max_occurrences = 2000, CancellationToken ct = default)
    {
        try
        {
            var health = (JObject)await _client.SendAsync("assembly_health_xr", new JObject
            {
                ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["max_occurrences"] = max_occurrences,
            }, ct);
            var bom = await _client.SendAsync("get_assembly_bom", new JObject { ["max_rows"] = 2000 }, ct);
            health["bom"] = BomAnalysis.Validate(BomAnalysis.ReadRows(bom), (bool?)bom["truncated"] ?? false);
            var after = (JObject)await _client.SendAsync("get_visual_revision", new JObject { ["document_id"] = document_id }, ct);
            if ((string?)after["revision"] != expected_revision)
                return Error(InventorErrorCodes.STALE_REVISION, "The assembly changed while its health was read.");
            return health.ToString(Formatting.None);
        }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
        catch (ArgumentException ex) { return Error(InventorErrorCodes.API_ERROR, ex.Message); }
    }
```

`ToolContracts.cs`:

```csharp
        new() { Name = "inventor_assembly_health_xr", Tier = Experimental, Access = "query", RequiresRevision = true, Documents = As, Verification = Pending },
```

- [ ] **Passo 5: risposte finte in `FakeAddIn.cs`**

```csharp
            case "assembly_health_xr":
                if ((string?)p["document_id"] != AssemblyId) return Fail(InventorErrorCodes.DOCUMENT_CHANGED, "Active document changed.");
                if ((string?)p["expected_revision"] != Revision) return Fail(InventorErrorCodes.STALE_REVISION, "Revision changed.");
                return Ok(new JObject
                {
                    ["document_id"] = AssemblyId, ["revision"] = Revision, ["healthy"] = false, ["occurrence_count"] = 3,
                    ["unconstrained_occurrences"] = 1, ["constraint_count"] = 1, ["joint_count"] = 0,
                    ["failing_constraints"] = new JArray(new JObject
                        { ["name"] = "M7_Sick", ["health"] = "kInconsistentHealth", ["a_occurrence_id"] = "ent_occ_1", ["b_occurrence_id"] = "ent_occ_3" }),
                    ["failing_joints"] = new JArray(),
                    ["occurrences"] = new JArray(
                        new JObject { ["name"] = "Bolt:1", ["occurrence_id"] = "ent_occ_1", ["suppressed"] = false, ["grounded"] = true, ["dof_translation"] = 0, ["dof_rotation"] = 0, ["unconstrained"] = false },
                        new JObject { ["name"] = "Bolt:2", ["occurrence_id"] = "ent_occ_2", ["suppressed"] = false, ["grounded"] = false, ["dof_translation"] = 3, ["dof_rotation"] = 3, ["unconstrained"] = true },
                        new JObject { ["name"] = "Plate:1", ["occurrence_id"] = "ent_occ_3", ["suppressed"] = false, ["grounded"] = true, ["dof_translation"] = 0, ["dof_rotation"] = 0, ["unconstrained"] = false }),
                });
            case "get_assembly_bom":
                return Ok(new JObject
                {
                    ["truncated"] = false,
                    ["bom"] = new JArray(
                        new JObject { ["part_number"] = "", ["path"] = "C:\\fake\\Bolt.ipt", ["qty"] = 2, ["description"] = "Bolt" },
                        new JObject { ["part_number"] = "PL-1", ["path"] = "C:\\fake\\Plate.ipt", ["qty"] = 1, ["description"] = "Plate" }),
                });
```

Controlla in `BomAnalysis.ReadRows` i nomi dei campi letti (`part_number`, `path`, `qty`, `description`) e allinea la risposta finta se differiscono.

- [ ] **Passo 6: test verdi e build dell'add-in**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests` → PASS (inclusi i test esistenti di `get_assembly_health`).
Run: `dotnet build bridge/src/plugin-so27 -c Debug -p:SoExperimental=true` → `Build succeeded`.

- [ ] **Passo 7: commit**

```bash
git add bridge/src bridge/tests/Bimwright.Ipt.Tests
git commit -m "feat(bridge): inventor_assembly_health_xr with relationship occurrences and BOM validation"
```

---

### Task 5: core, DTO e `IVerifyBackend`

**File:**
- Nuovo: `…core/Runtime/Backend/VerifyBackend.cs`
- Test: `Inventor XR SO/Tests~/XrSo.Core.Tests/Backend/VerifyBackendTests.cs`

**Interfacce:**
- Consuma: i tre tool dei task 2–4 via `McpClient.CallToolAsync`.
- Produce:

```csharp
namespace InventorXrSo.Core.Backend
{
    public interface IVerifyResult { string Revision { get; } }
    public sealed class VerifyBox { double[] MinMm; double[] MaxMm; }
    public sealed class InterferencePair { string AOccurrenceId, BOccurrenceId, AName, BName; double VolumeMm3; IReadOnlyList<VerifyBox> Boxes; }
    public sealed class InterferenceReport : IVerifyResult { string Revision; int Analyzed; int Count; double TotalVolumeMm3; long? ElapsedMs; IReadOnlyList<InterferencePair> Pairs; }
    public sealed class DistanceReport : IVerifyResult { string Revision; double DistanceMm; double[] PointAMm; double[] PointBMm; bool HasPoints; }
    public sealed class HealthIssue { string Kind; string Name; string Health; string AOccurrenceId; string BOccurrenceId; }
    public sealed class UnconstrainedOccurrence { string OccurrenceId; string Name; }
    public sealed class BomIssue { string Severity; string Code; string Message; }
    public sealed class HealthReport : IVerifyResult { string Revision; bool Healthy; int OccurrenceCount; IReadOnlyList<UnconstrainedOccurrence> Unconstrained; IReadOnlyList<HealthIssue> Issues; bool BomValid; IReadOnlyList<BomIssue> BomIssues; }
    public interface IVerifyBackend
    {
        Task<InterferenceReport> CheckInterferenceAsync(DocumentState state, IReadOnlyList<string> occurrenceIds, CancellationToken ct);
        Task<DistanceReport> MeasureMinDistanceAsync(DocumentState state, string aOccurrenceId, string bOccurrenceId, CancellationToken ct);
        Task<HealthReport> GetAssemblyHealthAsync(DocumentState state, CancellationToken ct);
    }
}
```

- [ ] **Passo 1: test che fallisce**

```csharp
using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class VerifyBackendTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _fixture;
    public VerifyBackendTests(BackendFixture fixture) { _fixture = fixture; }

    private async Task<(InventorBackend backend, DocumentState state)> Connect()
    {
        var backend = new InventorBackend(_fixture.Transport(), _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        return (backend, await backend.GetDocumentStateAsync(default));
    }

    [Fact]
    public async Task InterferenceCarriesIdsVolumeAndBoxesAndIsRevisionBound()
    {
        var (backend, state) = await Connect();
        var report = await backend.CheckInterferenceAsync(state, null, default);
        Assert.Equal(state.Revision, report.Revision);
        Assert.Equal(1, report.Count); Assert.Equal(3, report.Analyzed);
        var pair = Assert.Single(report.Pairs);
        Assert.Equal("ent_occ_1", pair.AOccurrenceId); Assert.Equal("ent_occ_3", pair.BOccurrenceId);
        Assert.Equal(2000.0, pair.VolumeMm3);
        var box = Assert.Single(pair.Boxes);
        Assert.Equal(new[] { 15.0, 0, 0 }, box.MinMm); Assert.Equal(new[] { 20.0, 20, 20 }, box.MaxMm);
        var stale = await Assert.ThrowsAsync<McpToolException>(() => backend.CheckInterferenceAsync(new DocumentState(state.DocumentId, "old", "v"), null, default));
        Assert.Equal("STALE_REVISION", stale.Code);
        Assert.DoesNotContain("atomic_batch", _fixture.AddIn.Commands);
    }

    [Fact]
    public async Task DistanceReportsPointsWhenInventorGivesThem()
    {
        var (backend, state) = await Connect();
        var report = await backend.MeasureMinDistanceAsync(state, "ent_occ_1", "ent_occ_2", default);
        Assert.Equal(30.0, report.DistanceMm);
        Assert.True(report.HasPoints);
        Assert.Equal(new[] { 30.0, 0, 0 }, report.PointBMm);
        var same = await Assert.ThrowsAsync<McpToolException>(() => backend.MeasureMinDistanceAsync(state, "ent_occ_1", "ent_occ_1", default));
        Assert.Equal("INVALID_ARGUMENT", same.Code);
    }

    [Fact]
    public async Task HealthMergesRelationshipsUnconstrainedAndBom()
    {
        var (backend, state) = await Connect();
        var report = await backend.GetAssemblyHealthAsync(state, default);
        Assert.False(report.Healthy);
        var issue = Assert.Single(report.Issues);
        Assert.Equal("constraint", issue.Kind); Assert.Equal("M7_Sick", issue.Name);
        Assert.Equal("ent_occ_1", issue.AOccurrenceId); Assert.Equal("ent_occ_3", issue.BOccurrenceId);
        Assert.Equal("ent_occ_2", Assert.Single(report.Unconstrained).OccurrenceId);
        Assert.False(report.BomValid);
        Assert.Contains(report.BomIssues, b => b.Code == "PART_NUMBER_MISSING");
    }

    [Fact]
    public async Task HealthRefusesWhenTheDocumentChangesWhileReading()
    {
        var (backend, state) = await Connect();
        _fixture.AddIn.RaiseDocumentChanged(geometry: false);
        var stale = await Assert.ThrowsAsync<McpToolException>(() => backend.GetAssemblyHealthAsync(state, default));
        Assert.Equal("STALE_REVISION", stale.Code);
    }

    [Fact]
    public void MissingPointsAndBoxesStayUnknown()
    {
        var distance = DistanceReport.FromJson(new JObject { ["revision"] = "r", ["distance_mm"] = 4.5, ["point_a"] = null, ["points_source"] = "unavailable" });
        Assert.False(distance.HasPoints); Assert.Null(distance.PointAMm);
        var interference = InterferenceReport.FromJson(new JObject { ["revision"] = "r", ["count"] = 1,
            ["pairs"] = new JArray(new JObject { ["a_occurrence_id"] = "a", ["b_occurrence_id"] = "b", ["volume_mm3"] = 1.0, ["boxes"] = new JArray(new JObject { ["min_mm"] = new JArray(1, 2) }) }) });
        Assert.Empty(Assert.Single(interference.Pairs).Boxes);
    }
}
```

`RaiseDocumentChanged` cambia la revisione per tutta la classe di test: il test `HealthRefusesWhenTheDocumentChangesWhileReading` legge lo stato prima di chiamarlo, quindi resta indipendente dall'ordine.

- [ ] **Passo 2: verifica che fallisca**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~VerifyBackendTests"`
Expected: errore di compilazione (`CheckInterferenceAsync` e i tipi non esistono).

- [ ] **Passo 3: implementa `VerifyBackend.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>A verification result is valid only for the document revision it was computed on.</summary>
    public interface IVerifyResult { string Revision { get; } }

    internal static class VerifyJson
    {
        public static double? Number(JToken token)
        {
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) return null;
            double value = (double)token;
            return double.IsNaN(value) || double.IsInfinity(value) ? (double?)null : value;
        }

        /// <summary>Exactly three finite numbers, else null.</summary>
        public static double[] Vector(JToken token)
        {
            if (!(token is JArray array) || array.Count != 3) return null;
            var values = array.Select(Number).ToArray();
            return values.All(v => v.HasValue) ? values.Select(v => v.Value).ToArray() : null;
        }

        public static IEnumerable<JObject> Objects(JToken token) => (token as JArray ?? new JArray()).OfType<JObject>();
    }

    /// <summary>Axis-aligned box in assembly millimetres.</summary>
    public sealed class VerifyBox
    {
        public VerifyBox(double[] minMm, double[] maxMm) { MinMm = minMm; MaxMm = maxMm; }
        public double[] MinMm { get; }
        public double[] MaxMm { get; }

        internal static VerifyBox FromJson(JObject json)
        {
            var min = VerifyJson.Vector(json["min_mm"]); var max = VerifyJson.Vector(json["max_mm"]);
            return min == null || max == null ? null : new VerifyBox(min, max);
        }
    }

    public sealed class InterferencePair
    {
        public string AOccurrenceId { get; private set; }
        public string BOccurrenceId { get; private set; }
        public string AName { get; private set; }
        public string BName { get; private set; }
        public double VolumeMm3 { get; private set; }
        public IReadOnlyList<VerifyBox> Boxes { get; private set; }

        internal static InterferencePair FromJson(JObject json) => new InterferencePair
        {
            AOccurrenceId = (string)json["a_occurrence_id"], BOccurrenceId = (string)json["b_occurrence_id"],
            AName = (string)json["a_name"] ?? (string)json["a_occurrence_id"], BName = (string)json["b_name"] ?? (string)json["b_occurrence_id"],
            VolumeMm3 = VerifyJson.Number(json["volume_mm3"]) ?? 0,
            Boxes = VerifyJson.Objects(json["boxes"]).Select(VerifyBox.FromJson).Where(b => b != null).ToArray(),
        };
    }

    public sealed class InterferenceReport : IVerifyResult
    {
        public string Revision { get; private set; }
        public int Analyzed { get; private set; }
        public int Count { get; private set; }
        public double TotalVolumeMm3 { get; private set; }
        public long? ElapsedMs { get; private set; }
        public IReadOnlyList<InterferencePair> Pairs { get; private set; }

        public static InterferenceReport FromJson(JObject json) => new InterferenceReport
        {
            Revision = (string)json["revision"], Analyzed = (int?)json["analyzed"] ?? 0, Count = (int?)json["count"] ?? 0,
            TotalVolumeMm3 = VerifyJson.Number(json["total_volume_mm3"]) ?? 0, ElapsedMs = (long?)json["elapsed_ms"],
            Pairs = VerifyJson.Objects(json["pairs"]).Select(InterferencePair.FromJson).ToArray(),
        };
    }

    public sealed class DistanceReport : IVerifyResult
    {
        public string Revision { get; private set; }
        public double DistanceMm { get; private set; }
        public double[] PointAMm { get; private set; }
        public double[] PointBMm { get; private set; }
        public bool HasPoints => PointAMm != null && PointBMm != null;

        public static DistanceReport FromJson(JObject json) => new DistanceReport
        {
            Revision = (string)json["revision"], DistanceMm = VerifyJson.Number(json["distance_mm"]) ?? double.NaN,
            PointAMm = VerifyJson.Vector(json["point_a"]), PointBMm = VerifyJson.Vector(json["point_b"]),
        };
    }

    public sealed class HealthIssue
    {
        public HealthIssue(string kind, string name, string health, string aOccurrenceId, string bOccurrenceId)
        { Kind = kind; Name = name; Health = health; AOccurrenceId = aOccurrenceId; BOccurrenceId = bOccurrenceId; }
        /// <summary>"constraint" or "joint".</summary>
        public string Kind { get; }
        public string Name { get; }
        public string Health { get; }
        public string AOccurrenceId { get; }
        public string BOccurrenceId { get; }
    }

    public sealed class UnconstrainedOccurrence
    {
        public UnconstrainedOccurrence(string occurrenceId, string name) { OccurrenceId = occurrenceId; Name = name; }
        public string OccurrenceId { get; }
        public string Name { get; }
    }

    public sealed class BomIssue
    {
        public BomIssue(string severity, string code, string message) { Severity = severity; Code = code; Message = message; }
        public string Severity { get; }
        public string Code { get; }
        public string Message { get; }
    }

    public sealed class HealthReport : IVerifyResult
    {
        public string Revision { get; private set; }
        public bool Healthy { get; private set; }
        public int OccurrenceCount { get; private set; }
        public IReadOnlyList<UnconstrainedOccurrence> Unconstrained { get; private set; }
        public IReadOnlyList<HealthIssue> Issues { get; private set; }
        public bool BomValid { get; private set; }
        public IReadOnlyList<BomIssue> BomIssues { get; private set; }

        public static HealthReport FromJson(JObject json)
        {
            IEnumerable<HealthIssue> Issues(string key, string kind) => VerifyJson.Objects(json[key]).Select(o =>
                new HealthIssue(kind, (string)o["name"], (string)o["health"], (string)o["a_occurrence_id"], (string)o["b_occurrence_id"]));
            var bom = json["bom"] as JObject ?? new JObject();
            return new HealthReport
            {
                Revision = (string)json["revision"], Healthy = (bool?)json["healthy"] ?? false,
                OccurrenceCount = (int?)json["occurrence_count"] ?? 0,
                Unconstrained = VerifyJson.Objects(json["occurrences"]).Where(o => (bool?)o["unconstrained"] == true)
                    .Select(o => new UnconstrainedOccurrence((string)o["occurrence_id"], (string)o["name"])).ToArray(),
                Issues = Issues("failing_constraints", "constraint").Concat(Issues("failing_joints", "joint")).ToArray(),
                BomValid = (bool?)bom["valid"] ?? true,
                BomIssues = VerifyJson.Objects(bom["findings"]).Select(o => new BomIssue((string)o["severity"], (string)o["code"], (string)o["message"])).ToArray(),
            };
        }
    }

    public interface IVerifyBackend
    {
        /// <summary>Null or empty ids: every direct occurrence against the others; else those occurrences against all the others.</summary>
        Task<InterferenceReport> CheckInterferenceAsync(DocumentState state, IReadOnlyList<string> occurrenceIds, CancellationToken ct);
        Task<DistanceReport> MeasureMinDistanceAsync(DocumentState state, string aOccurrenceId, string bOccurrenceId, CancellationToken ct);
        Task<HealthReport> GetAssemblyHealthAsync(DocumentState state, CancellationToken ct);
    }

    public sealed partial class InventorBackend : IVerifyBackend
    {
        public async Task<InterferenceReport> CheckInterferenceAsync(DocumentState state, IReadOnlyList<string> occurrenceIds, CancellationToken ct) =>
            InterferenceReport.FromJson(await _mcp.CallToolAsync("inventor_check_interference_xr", new JObject
            {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision,
                ["occurrence_ids"] = occurrenceIds == null || occurrenceIds.Count == 0 ? null : new JArray(occurrenceIds.Cast<object>().ToArray()),
            }, ct));

        public async Task<DistanceReport> MeasureMinDistanceAsync(DocumentState state, string aOccurrenceId, string bOccurrenceId, CancellationToken ct) =>
            DistanceReport.FromJson(await _mcp.CallToolAsync("inventor_measure_min_distance_xr", new JObject
            {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision,
                ["a_occurrence_id"] = aOccurrenceId, ["b_occurrence_id"] = bOccurrenceId,
            }, ct));

        public async Task<HealthReport> GetAssemblyHealthAsync(DocumentState state, CancellationToken ct) =>
            HealthReport.FromJson(await _mcp.CallToolAsync("inventor_assembly_health_xr", new JObject
            {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision,
            }, ct));
    }
}
```

- [ ] **Passo 4: test verdi**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`
Expected: PASS (nessuna regressione negli altri test del core).

- [ ] **Passo 5: commit**

```bash
git add "Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Backend/VerifyBackend.cs" "Inventor XR SO/Tests~/XrSo.Core.Tests/Backend/VerifyBackendTests.cs"
git commit -m "feat(xr): verification backend for interference, minimum distance and assembly health"
```

---

### Task 6: core, macchina a stati, righe di risultato e messaggi

**File:**
- Nuovo: `…core/Runtime/Verify/VerifyJob.cs`
- Nuovo: `…core/Runtime/Verify/VerifyFindings.cs`
- Test: `Tests~/XrSo.Core.Tests/Verify/VerifyJobTests.cs`, `Tests~/XrSo.Core.Tests/Verify/VerifyFindingsTests.cs`

**Interfacce:**
- Consuma: `IVerifyResult`, `InterferenceReport`, `HealthReport`, `VerifyBox` (task 5); `McpException` (`InventorXrSo.Core.Mcp`).
- Produce (namespace `InventorXrSo.Core.Verify`):
  - `enum VerifyStatus { Idle, Running, Done, Failed, Stale }`
  - `VerifyGate(Func<DateTime> now = null)`: `bool Busy`, `bool CoolingDown`, `bool CanStart`, `string Reason`, `static TimeSpan TimeoutCooldown` (30 s).
  - `interface IVerifyJob { VerifyStatus Status; string ErrorMessage; void Ignore(); void Reset(); void OnDocumentState(DocumentState state); event Action Changed; }`
  - `VerifyJob<T> : IVerifyJob where T : class, IVerifyResult`: `T Result`, `string Revision`, `string ErrorCode`, `Task<bool> RunAsync(Func<CancellationToken, Task<T>> run, CancellationToken ct)`.
  - `VerifySession(Func<DateTime> now = null)`: `Gate`, `Interference`, `Distance`, `Health`, `IReadOnlyList<IVerifyJob> Jobs`, `IVerifyJob Running`, `OnDocumentState(DocumentState)`, `Reset()`.
  - `enum FindingSeverity { Error, Warning }`; `VerifyFinding(severity, title, detail, occurrenceIds, boxes)`.
  - `VerifyFindings.FromInterference(InterferenceReport)`, `VerifyFindings.FromHealth(HealthReport)`, `VerifyFindings.Summary(InterferenceReport)`, `VerifyFindings.Summary(HealthReport)`, `VerifyFindings.Millimetres(double)`.
  - `VerifyMessages.For(Exception)`, `VerifyMessages.Timeout`, `VerifyMessages.Busy`.

- [ ] **Passo 1: test della macchina a stati**

```csharp
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Verify;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Verify;

public class VerifyJobTests
{
    private static DistanceReport Report(string revision) => DistanceReport.FromJson(new JObject { ["revision"] = revision, ["distance_mm"] = 30 });
    private static DocumentState State(string revision) => new DocumentState("doc", revision, "v");

    [Fact]
    public async Task RunMovesFromRunningToDoneAndKeepsTheRevision()
    {
        var session = new VerifySession();
        var pending = new TaskCompletionSource<DistanceReport>();
        var run = session.Distance.RunAsync(_ => pending.Task, default);
        Assert.Equal(VerifyStatus.Running, session.Distance.Status);
        Assert.True(session.Gate.Busy); Assert.Same(session.Distance, session.Running);
        pending.SetResult(Report("r1"));
        Assert.True(await run);
        Assert.Equal(VerifyStatus.Done, session.Distance.Status);
        Assert.Equal("r1", session.Distance.Revision);
        Assert.False(session.Gate.Busy);
    }

    [Fact]
    public async Task ASecondVerificationIsRefusedWhileOneRuns()
    {
        var session = new VerifySession();
        var pending = new TaskCompletionSource<DistanceReport>();
        _ = session.Distance.RunAsync(_ => pending.Task, default);
        Assert.False(await session.Health.RunAsync(_ => throw new InvalidOperationException("must not start"), default));
        Assert.Equal(VerifyStatus.Idle, session.Health.Status);
        Assert.Equal(VerifyMessages.Busy, session.Gate.Reason);
        pending.SetResult(Report("r1"));
    }

    [Fact]
    public async Task IgnoreDiscardsTheAnswerButKeepsTheGateBusyUntilItArrives()
    {
        var session = new VerifySession();
        var pending = new TaskCompletionSource<DistanceReport>();
        var run = session.Distance.RunAsync(_ => pending.Task, default);
        session.Distance.Ignore();
        Assert.Equal(VerifyStatus.Idle, session.Distance.Status);
        Assert.True(session.Gate.Busy, "Inventor is still computing the ignored request");
        pending.SetResult(Report("r1"));
        await run;
        Assert.Equal(VerifyStatus.Idle, session.Distance.Status);
        Assert.Null(session.Distance.Result);
        Assert.False(session.Gate.Busy);
    }

    [Fact]
    public async Task ADifferentRevisionMakesADoneResultStale()
    {
        var session = new VerifySession();
        await session.Distance.RunAsync(_ => Task.FromResult(Report("r1")), default);
        session.OnDocumentState(State("r1"));
        Assert.Equal(VerifyStatus.Done, session.Distance.Status);
        session.OnDocumentState(State("r2"));
        Assert.Equal(VerifyStatus.Stale, session.Distance.Status);
        Assert.NotNull(session.Distance.Result);
    }

    [Fact]
    public async Task ATimeoutFailsAndBlocksNewVerificationsForThirtySeconds()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        var session = new VerifySession(() => now);
        await session.Health.RunAsync(_ => Task.FromException<HealthReport>(new McpToolException("t", "TIMEOUT", "late", null)), default);
        Assert.Equal(VerifyStatus.Failed, session.Health.Status);
        Assert.Equal(VerifyMessages.Timeout, session.Health.ErrorMessage);
        Assert.False(session.Gate.CanStart);
        Assert.Equal(VerifyMessages.Timeout, session.Gate.Reason);
        now = now.AddSeconds(31);
        Assert.True(session.Gate.CanStart);
    }

    [Fact]
    public async Task StaleRevisionErrorsBecomeAShortItalianMessage()
    {
        var session = new VerifySession();
        await session.Distance.RunAsync(_ => Task.FromException<DistanceReport>(new McpToolException("t", "STALE_REVISION", "x", null)), default);
        Assert.Equal(VerifyStatus.Failed, session.Distance.Status);
        Assert.Equal("STALE_REVISION", session.Distance.ErrorCode);
        Assert.Equal("Il modello è cambiato, rilancia.", session.Distance.ErrorMessage);
    }

    [Fact]
    public async Task ResetForgetsResultsAndAnInFlightAnswer()
    {
        var session = new VerifySession();
        await session.Distance.RunAsync(_ => Task.FromResult(Report("r1")), default);
        var pending = new TaskCompletionSource<HealthReport>();
        var run = session.Health.RunAsync(_ => pending.Task, default);
        session.Reset();
        pending.SetResult(HealthReport.FromJson(new JObject { ["revision"] = "r1" }));
        await run;
        Assert.All(session.Jobs, j => Assert.Equal(VerifyStatus.Idle, j.Status));
        Assert.Null(session.Distance.Result); Assert.Null(session.Health.Result);
    }
}
```

- [ ] **Passo 2: test delle righe e dei messaggi**

```csharp
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Verify;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Verify;

public class VerifyFindingsTests
{
    [Fact]
    public void InterferencePairsBecomeErrorsSortedByVolumeWithIdsAndBoxes()
    {
        var report = InterferenceReport.FromJson(new JObject
        {
            ["revision"] = "r", ["analyzed"] = 4, ["count"] = 2, ["total_volume_mm3"] = 2010.5,
            ["pairs"] = new JArray(
                new JObject { ["a_occurrence_id"] = "a", ["b_occurrence_id"] = "c", ["a_name"] = "M7_A", ["b_name"] = "M7_C", ["volume_mm3"] = 10.5 },
                new JObject { ["a_occurrence_id"] = "a", ["b_occurrence_id"] = "b", ["a_name"] = "M7_A", ["b_name"] = "M7_B", ["volume_mm3"] = 2000.0,
                    ["boxes"] = new JArray(new JObject { ["min_mm"] = new JArray(15, 0, 0), ["max_mm"] = new JArray(20, 20, 20) }) }),
        });
        var rows = VerifyFindings.FromInterference(report);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Interferenza: M7_A ↔ M7_B", rows[0].Title);
        Assert.Equal("2000 mm³", rows[0].Detail);
        Assert.Equal(FindingSeverity.Error, rows[0].Severity);
        Assert.Equal(new[] { "a", "b" }, rows[0].OccurrenceIds);
        Assert.Single(rows[0].Boxes);
        Assert.Equal("10,5 mm³", rows[1].Detail);
        Assert.Equal("2 interferenze su 4 occorrenze, 2010,5 mm³ in totale.", VerifyFindings.Summary(report));
    }

    [Fact]
    public void NoInterferenceIsAnExplicitResult()
    {
        var report = InterferenceReport.FromJson(new JObject { ["revision"] = "r", ["analyzed"] = 4, ["count"] = 0, ["pairs"] = new JArray() });
        Assert.Empty(VerifyFindings.FromInterference(report));
        Assert.Equal("Nessuna interferenza su 4 occorrenze.", VerifyFindings.Summary(report));
    }

    [Fact]
    public void HealthRowsCoverRelationshipsUnconstrainedAndBom()
    {
        var report = HealthReport.FromJson(new JObject
        {
            ["revision"] = "r", ["healthy"] = false, ["occurrence_count"] = 4,
            ["failing_constraints"] = new JArray(new JObject { ["name"] = "M7_Sick", ["health"] = "kInconsistentHealth", ["a_occurrence_id"] = "b", ["b_occurrence_id"] = null }),
            ["failing_joints"] = new JArray(),
            ["occurrences"] = new JArray(new JObject { ["name"] = "M7_D", ["occurrence_id"] = "d", ["unconstrained"] = true }),
            ["bom"] = new JObject { ["valid"] = false, ["findings"] = new JArray(new JObject { ["severity"] = "error", ["code"] = "PART_NUMBER_MISSING", ["message"] = "Row has no part number." }) },
        });
        var rows = VerifyFindings.FromHealth(report);
        Assert.Equal(new[] { "Vincolo in errore: M7_Sick", "Non vincolato: M7_D", "Distinta: numero di parte mancante" }, rows.Select(r => r.Title).ToArray());
        Assert.Equal(new[] { "b" }, rows[0].OccurrenceIds);
        Assert.Equal(FindingSeverity.Warning, rows[1].Severity);
        Assert.Empty(rows[2].OccurrenceIds);
        Assert.Equal("Assieme con 3 problemi: 1 vincoli o giunti in errore, 1 componenti non vincolati, 1 righe di distinta.", VerifyFindings.Summary(report));
    }

    [Fact]
    public void AHealthyAssemblySaysSo()
    {
        var report = HealthReport.FromJson(new JObject { ["revision"] = "r", ["healthy"] = true, ["occurrence_count"] = 2, ["bom"] = new JObject { ["valid"] = true } });
        Assert.Empty(VerifyFindings.FromHealth(report));
        Assert.Equal("Assieme sano: nessun vincolo in errore, nessun componente libero, distinta valida.", VerifyFindings.Summary(report));
    }
}
```

- [ ] **Passo 3: verifica che falliscano**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~Verify"`
Expected: errore di compilazione (namespace `InventorXrSo.Core.Verify` inesistente).

- [ ] **Passo 4: implementa `VerifyJob.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;

namespace InventorXrSo.Core.Verify
{
    public enum VerifyStatus { Idle, Running, Done, Failed, Stale }

    /// <summary>
    /// One Inventor verification at a time for the whole workspace. Inventor computes on its single UI thread and cannot be
    /// interrupted: an ignored request keeps the gate busy until its answer arrives, and after a TIMEOUT the gate refuses new
    /// verifications for <see cref="TimeoutCooldown"/> because the client cannot know when Inventor is done.
    /// </summary>
    public sealed class VerifyGate
    {
        public static readonly TimeSpan TimeoutCooldown = TimeSpan.FromSeconds(30);
        private readonly Func<DateTime> _now;
        private DateTime? _timedOutAt;

        public VerifyGate(Func<DateTime> now = null) { _now = now ?? (() => DateTime.UtcNow); }

        public bool Busy { get; internal set; }
        public bool CoolingDown => _timedOutAt.HasValue && _now() - _timedOutAt.Value < TimeoutCooldown;
        public bool CanStart => !Busy && !CoolingDown;
        public string Reason => Busy ? VerifyMessages.Busy : CoolingDown ? VerifyMessages.Timeout : null;

        internal void MarkTimeout() => _timedOutAt = _now();
    }

    public interface IVerifyJob
    {
        VerifyStatus Status { get; }
        string ErrorMessage { get; }
        event Action Changed;
        /// <summary>Running: the answer will be discarded; the gate stays busy until it arrives.</summary>
        void Ignore();
        void Reset();
        /// <summary>A Done result computed on another revision becomes Stale.</summary>
        void OnDocumentState(DocumentState state);
    }

    public sealed class VerifyJob<T> : IVerifyJob where T : class, IVerifyResult
    {
        private readonly VerifyGate _gate;
        private int _ticket;

        public VerifyJob(VerifyGate gate) { _gate = gate ?? throw new ArgumentNullException(nameof(gate)); }

        public VerifyStatus Status { get; private set; }
        public T Result { get; private set; }
        public string Revision { get; private set; }
        public string ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public event Action Changed;

        /// <summary>False, and nothing runs, when the gate refuses. True once the run has finished (or was ignored).</summary>
        public async Task<bool> RunAsync(Func<CancellationToken, Task<T>> run, CancellationToken ct)
        {
            if (!_gate.CanStart) return false;
            int ticket = ++_ticket;
            _gate.Busy = true;
            Status = VerifyStatus.Running; Result = null; Revision = null; ErrorCode = null; ErrorMessage = null;
            Changed?.Invoke();
            try
            {
                var result = await run(ct);
                if (ticket == _ticket) { Result = result; Revision = result?.Revision; Status = VerifyStatus.Done; }
            }
            catch (OperationCanceledException) { if (ticket == _ticket) Status = VerifyStatus.Idle; }
            catch (Exception ex)
            {
                if (ex is McpException mcp && mcp.Code == "TIMEOUT") _gate.MarkTimeout();
                if (ticket == _ticket)
                {
                    Status = VerifyStatus.Failed;
                    ErrorCode = (ex as McpException)?.Code;
                    ErrorMessage = VerifyMessages.For(ex);
                }
            }
            finally
            {
                _gate.Busy = false;
                Changed?.Invoke();
            }
            return true;
        }

        public void Ignore()
        {
            if (Status != VerifyStatus.Running) return;
            _ticket++;
            Status = VerifyStatus.Idle;
            Changed?.Invoke();
        }

        public void Reset()
        {
            _ticket++;
            Status = VerifyStatus.Idle; Result = null; Revision = null; ErrorCode = null; ErrorMessage = null;
            Changed?.Invoke();
        }

        public void OnDocumentState(DocumentState state)
        {
            if (Status != VerifyStatus.Done || state == null || state.Revision == Revision) return;
            Status = VerifyStatus.Stale;
            Changed?.Invoke();
        }
    }

    /// <summary>The three Inventor verifications of Ispeziona, sharing one gate.</summary>
    public sealed class VerifySession
    {
        public VerifySession(Func<DateTime> now = null)
        {
            Gate = new VerifyGate(now);
            Interference = new VerifyJob<InterferenceReport>(Gate);
            Distance = new VerifyJob<DistanceReport>(Gate);
            Health = new VerifyJob<HealthReport>(Gate);
            Jobs = new IVerifyJob[] { Interference, Distance, Health };
        }

        public VerifyGate Gate { get; }
        public VerifyJob<InterferenceReport> Interference { get; }
        public VerifyJob<DistanceReport> Distance { get; }
        public VerifyJob<HealthReport> Health { get; }
        public IReadOnlyList<IVerifyJob> Jobs { get; }
        public IVerifyJob Running => Jobs.FirstOrDefault(j => j.Status == VerifyStatus.Running);

        public void OnDocumentState(DocumentState state) { foreach (var job in Jobs) job.OnDocumentState(state); }
        public void Reset() { foreach (var job in Jobs) job.Reset(); }
    }
}
```

- [ ] **Passo 5: implementa `VerifyFindings.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;

namespace InventorXrSo.Core.Verify
{
    public enum FindingSeverity { Error, Warning }

    /// <summary>One row of the Risultati list: what is wrong, a value, and the direct occurrences (and boxes) to bring into focus.</summary>
    public sealed class VerifyFinding
    {
        public VerifyFinding(FindingSeverity severity, string title, string detail, IReadOnlyList<string> occurrenceIds, IReadOnlyList<VerifyBox> boxes = null)
        {
            Severity = severity; Title = title; Detail = detail ?? "";
            OccurrenceIds = occurrenceIds ?? Array.Empty<string>();
            Boxes = boxes ?? Array.Empty<VerifyBox>();
        }
        public FindingSeverity Severity { get; }
        public string Title { get; }
        public string Detail { get; }
        public IReadOnlyList<string> OccurrenceIds { get; }
        public IReadOnlyList<VerifyBox> Boxes { get; }
    }

    public static class VerifyFindings
    {
        private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

        public static string Millimetres(double value) => value.ToString("0.###", It);

        private static string[] Ids(params string[] ids) => ids.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();

        public static IReadOnlyList<VerifyFinding> FromInterference(InterferenceReport report) => report.Pairs
            .OrderByDescending(p => p.VolumeMm3)
            .Select(p => new VerifyFinding(FindingSeverity.Error, "Interferenza: " + p.AName + " ↔ " + p.BName,
                Millimetres(p.VolumeMm3) + " mm³", Ids(p.AOccurrenceId, p.BOccurrenceId), p.Boxes))
            .ToArray();

        public static string Summary(InterferenceReport report) => report.Count == 0
            ? "Nessuna interferenza su " + report.Analyzed + " occorrenze."
            : report.Count + " interferenze su " + report.Analyzed + " occorrenze, " + Millimetres(report.TotalVolumeMm3) + " mm³ in totale.";

        public static IReadOnlyList<VerifyFinding> FromHealth(HealthReport report)
        {
            var rows = new List<VerifyFinding>();
            foreach (var issue in report.Issues)
                rows.Add(new VerifyFinding(FindingSeverity.Error, (issue.Kind == "joint" ? "Giunto in errore: " : "Vincolo in errore: ") + issue.Name,
                    issue.Health, Ids(issue.AOccurrenceId, issue.BOccurrenceId)));
            foreach (var free in report.Unconstrained)
                rows.Add(new VerifyFinding(FindingSeverity.Warning, "Non vincolato: " + free.Name, "6 gradi di libertà", Ids(free.OccurrenceId)));
            foreach (var bom in report.BomIssues)
                rows.Add(new VerifyFinding(bom.Severity == "error" ? FindingSeverity.Error : FindingSeverity.Warning,
                    "Distinta: " + BomText(bom.Code), bom.Message, Array.Empty<string>()));
            return rows;
        }

        public static string Summary(HealthReport report)
        {
            int relationships = report.Issues.Count, free = report.Unconstrained.Count, bom = report.BomIssues.Count;
            if (relationships + free + bom == 0) return "Assieme sano: nessun vincolo in errore, nessun componente libero, distinta valida.";
            return "Assieme con " + (relationships + free + bom) + " problemi: " + relationships + " vincoli o giunti in errore, "
                + free + " componenti non vincolati, " + bom + " righe di distinta.";
        }

        public static string BomText(string code)
        {
            switch (code)
            {
                case "PART_NUMBER_MISSING": return "numero di parte mancante";
                case "PART_NUMBER_DUPLICATE": return "numero di parte duplicato";
                case "DESCRIPTION_CONFLICT": return "descrizioni in conflitto";
                case "DESCRIPTION_MISSING": return "descrizione mancante";
                case "QUANTITY_INVALID": return "quantità non valida";
                case "BOM_TRUNCATED": return "distinta troncata";
                default: return code ?? "problema";
            }
        }
    }

    public static class VerifyMessages
    {
        public const string Busy = "Un'altra verifica è in corso.";
        public const string Timeout = "Inventor sta ancora calcolando. Riprova tra poco o restringi alla selezione.";

        public static string For(Exception ex)
        {
            if (!(ex is McpException mcp)) return "Verifica non riuscita.";
            switch (mcp.Code)
            {
                case "STALE_REVISION":
                case "DOCUMENT_CHANGED": return "Il modello è cambiato, rilancia.";
                case "TIMEOUT": return Timeout;
                case "WRONG_DOCUMENT_TYPE": return "Serve un assieme.";
                case "EXPERIMENTAL_DISABLED": return "Verifiche non attive sul server.";
                default: return "Verifica non riuscita (" + mcp.Code + ").";
            }
        }
    }
}
```

- [ ] **Passo 6: test verdi**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`
Expected: PASS.

- [ ] **Passo 7: commit**

```bash
git add "Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/Verify" "Inventor XR SO/Tests~/XrSo.Core.Tests/Verify"
git commit -m "feat(xr): verification state machine, result rows and messages"
```

---

### Task 7: Unity, `GhostBodies` e `ComponentVisibility`

**File:**
- Nuovo: `Inventor XR SO/Assets/XrSo/Runtime/Scene/GhostBodies.cs`
- Modifica: `Inventor XR SO/Assets/XrSo/Runtime/Scene/ComponentIsolation.cs`
- Nuovo: `Inventor XR SO/Assets/XrSo/Runtime/Scene/ComponentVisibility.cs`
- Test: `Inventor XR SO/Assets/XrSo/Tests/EditMode/ComponentVisibilityTests.cs` (nuovo), `ComponentIsolationTests.cs` (deve restare verde senza modifiche)

**Interfacce:**
- Produce: `GhostBodies(Material)`: `Add(CadBody)`, `Remove(CadBody)`, `Contains(CadBody)`, `Count`, `Clear()`, `Forget()`, `static Material CreateMaterial(string name, Color color)`.
- Produce: `enum OccurrenceVisibility { Normal, Ghost, Hidden }`; `ComponentVisibility : MonoBehaviour`: `Initialize(CadSceneView)`, `Get(string)`, `bool AnyChanged`, `XRay(IEnumerable<string>)`, `Hide(IEnumerable<string>)`, `Isolate(IEnumerable<string>)`, `ShowAll()`, `Snapshot()`, `Restore(IReadOnlyDictionary<string, OccurrenceVisibility>)`, `event Action Changed`. Gli id sono quelli delle istanze in scena (occorrenze di parte).

- [ ] **Passo 1: test di `ComponentVisibility`**

```csharp
using System.Linq;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public class ComponentVisibilityTests
    {
        private GameObject _root;
        private CadSceneView _view;
        private ComponentVisibility _visibility;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Model");
            _view = _root.AddComponent<CadSceneView>();
            _view.Show(CadSceneViewTests.BoltScene());
            _visibility = _root.AddComponent<ComponentVisibility>();
            _visibility.Initialize(_view);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        private CadBody Body(string id) => _view.Find(id).Bodies[0];
        private static bool HasGhost(CadBody body) => body.GetComponentsInChildren<MeshRenderer>().Any(r => r != body.Renderer && r.enabled);

        [Test]
        public void XRayDrawsAGhostAndKeepsTheColliderSoTheRayStillHits()
        {
            _visibility.XRay(new[] { "ent_occ_1" });
            var body = Body("ent_occ_1");
            Assert.AreEqual(OccurrenceVisibility.Ghost, _visibility.Get("ent_occ_1"));
            Assert.False(body.Renderer.enabled); Assert.True(HasGhost(body));
            Assert.True(body.GetComponent<Collider>().enabled);
            Assert.AreEqual(OccurrenceVisibility.Normal, _visibility.Get("ent_occ_2"));
        }

        [Test]
        public void HideTurnsOffRendererAndColliderSoTheRayPassesThrough()
        {
            _visibility.Hide(new[] { "ent_occ_1" });
            var body = Body("ent_occ_1");
            Assert.False(body.Renderer.enabled); Assert.False(HasGhost(body));
            Assert.False(body.GetComponent<Collider>().enabled);
            Physics.SyncTransforms();
            Assert.False(Physics.Raycast(new Vector3(-0.005f, 1f, 0.005f), Vector3.down, out var hit, 5f) && hit.collider.GetComponent<CadBody>() == body);
        }

        [Test]
        public void IsolateGhostsEverythingElseAndShowAllRestores()
        {
            _visibility.Isolate(new[] { "ent_occ_2" });
            Assert.AreEqual(OccurrenceVisibility.Ghost, _visibility.Get("ent_occ_1"));
            Assert.AreEqual(OccurrenceVisibility.Normal, _visibility.Get("ent_occ_2"));
            _visibility.ShowAll();
            Assert.False(_visibility.AnyChanged);
            foreach (var instance in _view.Instances)
            {
                Assert.True(instance.Bodies[0].Renderer.enabled); Assert.True(instance.Bodies[0].GetComponent<Collider>().enabled);
                Assert.False(HasGhost(instance.Bodies[0]));
            }
        }

        [Test]
        public void SnapshotAndRestoreReturnToThePreviousState()
        {
            _visibility.Hide(new[] { "ent_occ_1" });
            var snapshot = _visibility.Snapshot();
            _visibility.Isolate(new[] { "ent_occ_1" });
            _visibility.Restore(snapshot);
            Assert.AreEqual(OccurrenceVisibility.Hidden, _visibility.Get("ent_occ_1"));
            Assert.AreEqual(OccurrenceVisibility.Normal, _visibility.Get("ent_occ_2"));
        }

        [Test]
        public void ASceneRebuildForgetsEveryState()
        {
            int changed = 0; _visibility.Changed += () => changed++;
            _visibility.Hide(new[] { "ent_occ_1" });
            _view.Show(CadSceneViewTests.BoltScene());
            Assert.False(_visibility.AnyChanged);
            Assert.AreEqual(2, changed);
            Assert.True(_view.Find("ent_occ_1").Bodies[0].Renderer.enabled);
        }

        [Test]
        public void UnknownOccurrencesAreIgnored()
        {
            _visibility.Hide(new[] { "missing" });
            Assert.False(_visibility.AnyChanged);
        }
    }
}
```

Il raggio del secondo test usa gli stessi valori del test `CadSceneViewTests` che colpisce `ent_occ_1`.

- [ ] **Passo 2: verifica che falliscano**

Run (Unity chiuso): `Invoke-Unity.ps1` con `-testFilter InventorXrSo.Tests.ComponentVisibilityTests`.
Expected: errore di compilazione (`ComponentVisibility` non esiste).

- [ ] **Passo 3: crea `GhostBodies.cs`**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// Bodies drawn as translucent ghosts: a copy of the mesh with the XrSo/HighlightOverlay shader replaces the body's renderer.
    /// The collider stays, so the ray still hits a ghost. No other material is swapped.
    /// </summary>
    public sealed class GhostBodies
    {
        private readonly Material _material;
        private readonly List<(CadBody body, GameObject ghost)> _ghosts = new List<(CadBody, GameObject)>();

        public GhostBodies(Material material) { _material = material ?? throw new ArgumentNullException(nameof(material)); }

        public int Count => _ghosts.Count;

        public static Material CreateMaterial(string name, Color color)
        {
            var shader = Shader.Find("XrSo/HighlightOverlay");
            if (shader == null) throw new InvalidOperationException("CAD ghost shader is unavailable.");
            var material = new Material(shader) { name = name };
            material.SetColor("_Color", color);
            return material;
        }

        public bool Contains(CadBody body) => _ghosts.Exists(g => g.body == body);

        public void Add(CadBody body)
        {
            if (body == null || body.Renderer == null || Contains(body)) return;
            var ghost = new GameObject("Ghost");
            ghost.transform.SetParent(body.transform, false);
            ghost.AddComponent<MeshFilter>().sharedMesh = body.Mesh;
            ghost.AddComponent<MeshRenderer>().sharedMaterial = _material;
            body.Renderer.enabled = false;
            _ghosts.Add((body, ghost));
        }

        public void Remove(CadBody body)
        {
            int index = _ghosts.FindIndex(g => g.body == body);
            if (index < 0) return;
            Restore(_ghosts[index]);
            _ghosts.RemoveAt(index);
        }

        public void Clear()
        {
            foreach (var entry in _ghosts) Restore(entry);
            _ghosts.Clear();
        }

        /// <summary>After a scene rebuild the bodies are already destroyed: drop them without touching them.</summary>
        public void Forget() => _ghosts.Clear();

        private static void Restore((CadBody body, GameObject ghost) entry)
        {
            if (entry.body != null && entry.body.Renderer != null) entry.body.Renderer.enabled = true;
            if (entry.ghost == null) return;
            entry.ghost.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(entry.ghost); else UnityEngine.Object.DestroyImmediate(entry.ghost);
        }
    }
}
```

- [ ] **Passo 4: `ComponentIsolation` usa `GhostBodies`**

In `ComponentIsolation.cs`:
- sostituisci il campo `_faded` con `private GhostBodies _ghosts;`;
- in `Initialize`, al posto della creazione manuale del materiale: `_fade = GhostBodies.CreateMaterial("Isolation fade", FadeColor); _ghosts = new GhostBodies(_fade);`;
- `IsFaded`: `return instance != null && instance.Bodies.Any(_ghosts.Contains);` (aggiungi `using System.Linq;`);
- `FadedBodies => _ghosts?.Count ?? 0;`;
- in `Isolate`: `foreach (var body in other.Bodies) _ghosts.Add(body);`;
- `Release`: chiama `_ghosts.Clear()` al posto di `Unfade()`;
- `OnRebuilt`: `_ghosts.Forget()` al posto di `_faded.Clear()`;
- elimina i metodi `Fade`, `Unfade` e l'helper `Destroy(GameObject)` se non più usati.

Il comportamento non cambia: `ComponentIsolationTests` deve passare senza modifiche.

- [ ] **Passo 5: crea `ComponentVisibility.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    public enum OccurrenceVisibility { Normal, Ghost, Hidden }

    /// <summary>
    /// Ispeziona visibility (spec M7): per scene occurrence Normal, Ghost (translucent, still hit by the ray) or Hidden (renderer and
    /// collider off, the ray passes through). View state only: Inventor is never touched. A scene rebuild forgets every state.
    /// </summary>
    public sealed class ComponentVisibility : MonoBehaviour
    {
        public static readonly Color GhostColor = new Color(0.72f, 0.74f, 0.77f, 0.2f);

        private readonly Dictionary<string, OccurrenceVisibility> _states = new Dictionary<string, OccurrenceVisibility>();
        private CadSceneView _view;
        private Material _material;
        private GhostBodies _ghosts;

        public event Action Changed;
        public bool AnyChanged => _states.Count > 0;

        public void Initialize(CadSceneView view)
        {
            if (_view != null) throw new InvalidOperationException("Visibility is already initialized.");
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _material = GhostBodies.CreateMaterial("Visibility ghost", GhostColor);
            _ghosts = new GhostBodies(_material);
            _view.Rebuilt += OnRebuilt;
        }

        public OccurrenceVisibility Get(string occurrenceId) =>
            occurrenceId != null && _states.TryGetValue(occurrenceId, out var state) ? state : OccurrenceVisibility.Normal;

        public void XRay(IEnumerable<string> occurrenceIds) => Set(occurrenceIds, OccurrenceVisibility.Ghost);
        public void Hide(IEnumerable<string> occurrenceIds) => Set(occurrenceIds, OccurrenceVisibility.Hidden);

        /// <summary>The given occurrences stay Normal, every other one becomes a ghost.</summary>
        public void Isolate(IEnumerable<string> keep)
        {
            var kept = new HashSet<string>(keep ?? Enumerable.Empty<string>());
            foreach (var instance in _view.Instances)
                if (instance != null) Apply(instance.OccurrenceId, kept.Contains(instance.OccurrenceId) ? OccurrenceVisibility.Normal : OccurrenceVisibility.Ghost);
            Changed?.Invoke();
        }

        public void ShowAll()
        {
            bool had = _states.Count > 0;
            foreach (var id in _states.Keys.ToList()) Apply(id, OccurrenceVisibility.Normal);
            if (had) Changed?.Invoke();
        }

        public IReadOnlyDictionary<string, OccurrenceVisibility> Snapshot() => new Dictionary<string, OccurrenceVisibility>(_states);

        public void Restore(IReadOnlyDictionary<string, OccurrenceVisibility> snapshot)
        {
            foreach (var id in _states.Keys.ToList()) Apply(id, OccurrenceVisibility.Normal);
            if (snapshot != null) foreach (var pair in snapshot) Apply(pair.Key, pair.Value);
            Changed?.Invoke();
        }

        private void Set(IEnumerable<string> occurrenceIds, OccurrenceVisibility state)
        {
            bool any = false;
            foreach (var id in occurrenceIds ?? Enumerable.Empty<string>()) any |= Apply(id, state);
            if (any) Changed?.Invoke();
        }

        private bool Apply(string occurrenceId, OccurrenceVisibility state)
        {
            var instance = _view?.Find(occurrenceId);
            if (instance == null) { _states.Remove(occurrenceId ?? ""); return false; }
            foreach (var body in instance.Bodies)
            {
                _ghosts.Remove(body);
                if (body.Renderer != null) body.Renderer.enabled = state == OccurrenceVisibility.Normal;
                var collider = body.GetComponent<Collider>();
                if (collider != null) collider.enabled = state != OccurrenceVisibility.Hidden;
                if (state == OccurrenceVisibility.Ghost) _ghosts.Add(body);
            }
            if (state == OccurrenceVisibility.Normal) _states.Remove(occurrenceId); else _states[occurrenceId] = state;
            return true;
        }

        // The instances are already destroyed: the states no longer mean anything.
        private void OnRebuilt()
        {
            _ghosts.Forget();
            bool had = _states.Count > 0;
            _states.Clear();
            if (had) Changed?.Invoke();
        }

        private void OnDestroy()
        {
            if (_view != null) _view.Rebuilt -= OnRebuilt;
            _ghosts?.Clear();
            if (_material != null) { if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material); }
        }
    }
}
```

`Apply` prima riporta il corpo allo stato normale (via il fantasma, renderer e collider accesi), poi applica il nuovo stato: così passare da Nascosto a Fantasma o viceversa non lascia residui.

- [ ] **Passo 6: test verdi**

Run: EditMode con filtro `InventorXrSo.Tests.ComponentVisibilityTests` e poi `InventorXrSo.Tests.ComponentIsolationTests`.
Expected: PASS entrambi.

- [ ] **Passo 7: commit**

```bash
git add "Inventor XR SO/Assets/XrSo/Runtime/Scene" "Inventor XR SO/Assets/XrSo/Tests/EditMode/ComponentVisibilityTests.cs"
git commit -m "feat(xr): component visibility for Inspect (ghost, hidden) sharing the ghost bodies of isolation"
```

Includi i file `.meta` generati da Unity per i file nuovi.

---

### Task 8: Unity, `VerifyOverlay` e limiti delle istanze

**File:**
- Nuovo: `Inventor XR SO/Assets/XrSo/Runtime/Scene/VerifyOverlay.cs`
- Modifica: `Inventor XR SO/Assets/XrSo/Runtime/Scene/InspectionGeometry.cs`
- Test: `Inventor XR SO/Assets/XrSo/Tests/EditMode/VerifyOverlayTests.cs`

**Interfacce:**
- Consuma: `VerifyBox` (task 5).
- Produce:
  - `VerifyOverlay : MonoBehaviour`: `Initialize(Material lineMaterial, Transform head)`, `static Vector3 ToLocal(double[] mm)`, `Tint(IEnumerable<CadInstance>)`, `ShowBoxes(IEnumerable<VerifyBox>)`, `ShowDistance(Vector3 aLocal, Vector3 bLocal, string label)`, `Clear()`, `ClearDistance()`, `int BoxCount`, `int TintedBodies`, `bool HasDistance`, `string DistanceLabel`, `static bool ClosestVertices(IEnumerable<CadInstance> a, IEnumerable<CadInstance> b, Transform root, out Vector3 aLocal, out Vector3 bLocal)`.
  - `InspectionGeometry.InstancesBounds(Transform root, IEnumerable<CadInstance> instances)` → `Bounds?` nello spazio locale non scalato di `root`.

- [ ] **Passo 1: test**

```csharp
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public class VerifyOverlayTests
    {
        private GameObject _root;
        private CadSceneView _view;
        private VerifyOverlay _overlay;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Model");
            _view = _root.AddComponent<CadSceneView>();
            _view.Show(CadSceneViewTests.BoltScene());
            var head = new GameObject("Head"); head.transform.SetParent(_root.transform);
            _overlay = new GameObject("Overlay").AddComponent<VerifyOverlay>();
            _overlay.transform.SetParent(_root.transform, false);
            _overlay.Initialize(new Material(Shader.Find("Sprites/Default")), head.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void InventorMillimetresMapToTheSceneLikeTheMeshes()
        {
            // Bolt:2 is placed at glTF x = +0.03 m and shows at local x = -0.03 (CadSceneViewTests): 30 mm on X maps the same way.
            var p = VerifyOverlay.ToLocal(new[] { 30.0, 10, 5 });
            Assert.AreEqual(_view.Find("ent_occ_2").transform.localPosition.x, p.x, 1e-6f);
            Assert.AreEqual(0.010f, p.y, 1e-6f); Assert.AreEqual(0.005f, p.z, 1e-6f);
        }

        [Test]
        public void BoxesAndTintAreDrawnAndCleared()
        {
            _overlay.ShowBoxes(new[] { new VerifyBox(new[] { 0.0, 0, 0 }, new[] { 5.0, 20, 20 }) });
            _overlay.Tint(new[] { _view.Find("ent_occ_1") });
            Assert.AreEqual(1, _overlay.BoxCount);
            Assert.AreEqual(1, _overlay.TintedBodies);
            var line = _overlay.GetComponentsInChildren<LineRenderer>().Single();
            Assert.AreEqual(16, line.positionCount, "one polyline traces the 12 edges");
            _overlay.Clear();
            Assert.AreEqual(0, _overlay.BoxCount); Assert.AreEqual(0, _overlay.TintedBodies);
            Assert.True(_view.Find("ent_occ_1").Bodies[0].Renderer.enabled, "the tint is an overlay, the body stays visible");
        }

        [Test]
        public void DistanceLineCarriesItsLabel()
        {
            _overlay.ShowDistance(Vector3.zero, new Vector3(-0.03f, 0, 0), "30 mm");
            Assert.True(_overlay.HasDistance); Assert.AreEqual("30 mm", _overlay.DistanceLabel);
            _overlay.ClearDistance();
            Assert.False(_overlay.HasDistance);
        }

        [Test]
        public void ClosestVerticesFindTheGapBetweenTwoInstances()
        {
            Assert.True(VerifyOverlay.ClosestVertices(new[] { _view.Find("ent_occ_1") }, new[] { _view.Find("ent_occ_2") }, _view.transform, out var a, out var b));
            Assert.Less(Vector3.Distance(a, b), 0.03f, "closer than the 30 mm between the two origins");
            Assert.Greater(a.x, b.x, "Bolt:2 lies towards -X in Unity");
        }

        [Test]
        public void InstancesBoundsEncloseTheChosenInstancesOnly()
        {
            var one = InspectionGeometry.InstancesBounds(_view.transform, new[] { _view.Find("ent_occ_1") }).Value;
            var both = InspectionGeometry.InstancesBounds(_view.transform, _view.Instances).Value;
            Assert.Less(one.size.x, both.size.x);
            Assert.IsNull(InspectionGeometry.InstancesBounds(_view.transform, Enumerable.Empty<CadInstance>()));
        }
    }
}
```

- [ ] **Passo 2: verifica che falliscano**

Run: EditMode con filtro `InventorXrSo.Tests.VerifyOverlayTests` → errore di compilazione.

- [ ] **Passo 3: `InstancesBounds` in `InspectionGeometry.cs`**

Aggiungi alla classe statica (con `using System.Collections.Generic;`):

```csharp
        /// <summary>Bounds of the given instances in the unscaled local space of <paramref name="root"/>; null when there is no body.</summary>
        public static Bounds? InstancesBounds(Transform root, IEnumerable<CadInstance> instances)
        {
            Bounds? result = null;
            foreach (var instance in instances)
            {
                if (instance == null) continue;
                foreach (var body in instance.Bodies)
                {
                    if (body == null || body.Mesh == null) continue;
                    var b = body.Mesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var local = b.center + Vector3.Scale(b.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        var point = root.InverseTransformPoint(body.transform.TransformPoint(local));
                        if (result == null) result = new Bounds(point, Vector3.zero);
                        else { var grown = result.Value; grown.Encapsulate(point); result = grown; }
                    }
                }
            }
            return result;
        }
```

`InverseTransformPoint` toglie anche la scala della radice: il risultato è nello stesso spazio di `ScenePlacement.LocalBounds`.

- [ ] **Passo 4: crea `VerifyOverlay.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// M7 drawings in model space (child of the CadSceneView root, model metres): red overlay on interfering occurrences, red
    /// wireframe boxes of the interference bodies, and the minimum-distance line with its label. Never touches Inventor.
    /// </summary>
    public sealed class VerifyOverlay : MonoBehaviour
    {
        public static readonly Color Red = new Color(0.95f, 0.2f, 0.15f, 1f);
        // Corner order: bottom 0..3 (z min), top 4..7 (z max); this path walks all 12 edges in one polyline.
        private static readonly int[] BoxPath = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };

        private readonly List<GameObject> _boxes = new List<GameObject>();
        private readonly List<GameObject> _tints = new List<GameObject>();
        private GameObject _distance;
        private Material _line, _tint;
        private Transform _head;

        public int BoxCount => _boxes.Count;
        public int TintedBodies => _tints.Count;
        public bool HasDistance => _distance != null;
        public string DistanceLabel { get; private set; }

        public void Initialize(Material lineMaterial, Transform head)
        {
            _line = lineMaterial; _head = head;
            _tint = GhostBodies.CreateMaterial("Verify red", new Color(Red.r, Red.g, Red.b, 0.45f));
        }

        /// <summary>Assembly millimetres (Inventor axes, as the GLB) to model metres in Unity: X mirrored like every mesh.</summary>
        public static Vector3 ToLocal(double[] mm) => new Vector3(-(float)(mm[0] / 1000.0), (float)(mm[1] / 1000.0), (float)(mm[2] / 1000.0));

        public void Tint(IEnumerable<CadInstance> instances)
        {
            foreach (var instance in instances.Where(i => i != null))
                foreach (var body in instance.Bodies)
                {
                    var overlay = new GameObject("VerifyTint");
                    overlay.transform.SetParent(body.transform, false);
                    overlay.AddComponent<MeshFilter>().sharedMesh = body.Mesh;
                    overlay.AddComponent<MeshRenderer>().sharedMaterial = _tint;
                    _tints.Add(overlay);
                }
        }

        public void ShowBoxes(IEnumerable<VerifyBox> boxes)
        {
            foreach (var box in boxes ?? Enumerable.Empty<VerifyBox>())
            {
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    corners[i] = ToLocal(new[]
                    {
                        (i == 1 || i == 2 || i == 5 || i == 6) ? box.MaxMm[0] : box.MinMm[0],
                        (i == 2 || i == 3 || i == 6 || i == 7) ? box.MaxMm[1] : box.MinMm[1],
                        i >= 4 ? box.MaxMm[2] : box.MinMm[2],
                    });
                var go = new GameObject("InterferenceBox");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = _line; line.useWorldSpace = false; line.widthMultiplier = 0.0015f;
                line.startColor = line.endColor = Red;
                line.positionCount = BoxPath.Length;
                for (int i = 0; i < BoxPath.Length; i++) line.SetPosition(i, corners[BoxPath[i]]);
                _boxes.Add(go);
            }
        }

        public void ShowDistance(Vector3 aLocal, Vector3 bLocal, string label)
        {
            ClearDistance();
            _distance = new GameObject("MinimumDistance");
            _distance.transform.SetParent(transform, false);
            var line = _distance.AddComponent<LineRenderer>();
            line.sharedMaterial = _line; line.useWorldSpace = false; line.widthMultiplier = 0.002f; line.positionCount = 2;
            line.SetPosition(0, aLocal); line.SetPosition(1, bLocal);
            var canvas = UiFactory.WorldCanvas(_distance.transform, "Distanza minima", new Vector2(420, 38));
            canvas.transform.localPosition = (aLocal + bLocal) * 0.5f;
            var text = UiFactory.Label(canvas.transform, label, 24);
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            UiFactory.Stretch(text.rectTransform);
            DistanceLabel = label;
        }

        public void ClearDistance()
        {
            if (_distance != null) Release(_distance);
            _distance = null; DistanceLabel = null;
        }

        public void Clear()
        {
            foreach (var go in _boxes) Release(go);
            foreach (var go in _tints) Release(go);
            _boxes.Clear(); _tints.Clear();
            ClearDistance();
        }

        /// <summary>
        /// Indicative closest points between two groups of instances, from mesh vertices (at most ~2000 per group). Used only when
        /// Inventor gives no points: the value shown is always Inventor's.
        /// </summary>
        public static bool ClosestVertices(IEnumerable<CadInstance> a, IEnumerable<CadInstance> b, Transform root, out Vector3 aLocal, out Vector3 bLocal)
        {
            var left = Sample(a, root); var right = Sample(b, root);
            aLocal = bLocal = default;
            if (left.Count == 0 || right.Count == 0) return false;
            float best = float.MaxValue;
            foreach (var p in left)
                foreach (var q in right)
                {
                    float d = (p - q).sqrMagnitude;
                    if (d < best) { best = d; aLocal = p; bLocal = q; }
                }
            return true;
        }

        private static List<Vector3> Sample(IEnumerable<CadInstance> instances, Transform root)
        {
            var bodies = instances.Where(i => i != null).SelectMany(i => i.Bodies).Where(b => b != null && b.Mesh != null).ToList();
            int total = bodies.Sum(b => b.Mesh.vertexCount);
            int stride = Mathf.Max(1, total / 2000);
            var points = new List<Vector3>();
            foreach (var body in bodies)
            {
                var vertices = body.Mesh.vertices;
                for (int i = 0; i < vertices.Length; i += stride) points.Add(root.InverseTransformPoint(body.transform.TransformPoint(vertices[i])));
            }
            return points;
        }

        private void LateUpdate()
        {
            if (_distance == null || _head == null) return;
            var canvas = _distance.GetComponentInChildren<Canvas>();
            if (canvas != null) canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - _head.position);
        }

        private void OnDestroy()
        {
            Clear();
            if (_tint != null) { if (Application.isPlaying) Destroy(_tint); else DestroyImmediate(_tint); }
        }

        private static void Release(GameObject go)
        {
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
```

Controlla le firme di `UiFactory.WorldCanvas`, `UiFactory.Label` e `UiFactory.Stretch` in `Runtime/Ui/UiFactory.cs` (sono quelle usate da `MeasurementView.Draw`) e allinea se differiscono.

- [ ] **Passo 5: test verdi**

Run: EditMode con filtro `InventorXrSo.Tests.VerifyOverlayTests` → PASS.

- [ ] **Passo 6: commit**

```bash
git add "Inventor XR SO/Assets/XrSo/Runtime/Scene" "Inventor XR SO/Assets/XrSo/Tests/EditMode/VerifyOverlayTests.cs"
git commit -m "feat(xr): verification overlay (red tint, interference boxes, distance line) and instance bounds"
```

---

### Task 9: Ispeziona, schede Visibilità e Verifica

**File:**
- Nuovo: `Inventor XR SO/Assets/XrSo/Xr/InspectVerify.cs` (parte di `InspectWorkspace`)
- Modifica: `Inventor XR SO/Assets/XrSo/Xr/InspectActions.cs`
- Modifica: `Inventor XR SO/Assets/XrSo/Xr/InspectWorkspace.cs`
- Test: `Inventor XR SO/Assets/XrSo/Tests/EditMode/InspectVerifyTests.cs` (nuovo); aggiornamenti in `InspectWorkspaceTests.cs` e nei runner M2 e M6

**Interfacce:**
- Consuma: `IVerifyBackend` e DTO (task 5), `VerifySession`, `VerifyFindings`, `VerifyMessages` (task 6), `ComponentVisibility` (task 7), `VerifyOverlay`, `InspectionGeometry.InstancesBounds` (task 8).
- Produce (costanti pubbliche di `InspectWorkspace`, usate dal runner M7):
  - schede `TabVisibility = "visibilita"` ("Visibilità"), `TabVerify = "verifica"` ("Verifica");
  - azioni `IdXRay = "inspect.visibility.xray"`, `IdIsolate = "inspect.visibility.isolate"`, `IdHide = "inspect.visibility.hide"`, `IdShowAll = "inspect.visibility.showall"`, `IdInterference = "inspect.verify.interference"`, `IdScope = "inspect.verify.scope"`, `IdDistance = "inspect.verify.distance"`, `IdHealth = "inspect.verify.health"`, `IdResults = "inspect.verify.results"`, `IdIgnore = "inspect.verify.ignore"`;
  - campi privati letti dal runner: `_verify`, `_verifySession`, `_visibility`, `_overlay`, `_findings`, `_findingsStale`, `_distanceA`, `_focusSnapshot`.

- [ ] **Passo 1: test di integrazione che falliscono**

Crea `InspectVerifyTests.cs`. Riusa la stessa costruzione di `InspectWorkspaceTests.SetUp` (copiala: rig, `CadSceneView` con `BoltScene()`, `SelectionVisuals`, `ControllerRay`, `UiShell`, `ActionCatalog`, `XrInput` sintetico). Il backend finto implementa sia `IInspectionBackend` sia `IVerifyBackend`:

```csharp
        private sealed class VerifyBackend : IInspectionBackend, IVerifyBackend
        {
            public TaskCompletionSource<InterferenceReport> Interference = new TaskCompletionSource<InterferenceReport>();
            public IReadOnlyList<string> LastScope;
            public string LastA, LastB;
            public DistanceReport Distance = DistanceReport.FromJson(new JObject { ["revision"] = "r1", ["distance_mm"] = 30.0,
                ["point_a"] = new JArray(0, 0, 0), ["point_b"] = new JArray(30, 0, 0) });
            public HealthReport Health = HealthReport.FromJson(new JObject { ["revision"] = "r1", ["healthy"] = false,
                ["failing_constraints"] = new JArray(new JObject { ["name"] = "M7_Sick", ["health"] = "kInconsistentHealth", ["a_occurrence_id"] = "ent_occ_1" }),
                ["bom"] = new JObject { ["valid"] = true } });

            public Task<InspectionInfo> InspectAsync(DocumentState state, string occurrenceId, CancellationToken ct) => Task.FromResult(InspectionInfo.FromJson(new JObject()));
            public Task<IReadOnlyList<OpenDocument>> ListOpenAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OpenDocument>>(Array.Empty<OpenDocument>());
            public Task ActivateOpenAsync(string documentId, CancellationToken ct) => Task.CompletedTask;
            public Task<InterferenceReport> CheckInterferenceAsync(DocumentState state, IReadOnlyList<string> ids, CancellationToken ct) { LastScope = ids; return Interference.Task; }
            public Task<DistanceReport> MeasureMinDistanceAsync(DocumentState state, string a, string b, CancellationToken ct) { LastA = a; LastB = b; return Task.FromResult(Distance); }
            public Task<HealthReport> GetAssemblyHealthAsync(DocumentState state, CancellationToken ct) => Task.FromResult(Health);
        }

        private static InterferenceReport OnePair() => InterferenceReport.FromJson(new JObject
        {
            ["revision"] = "r1", ["analyzed"] = 2, ["count"] = 1, ["total_volume_mm3"] = 2000.0,
            ["pairs"] = new JArray(new JObject { ["a_occurrence_id"] = "ent_occ_1", ["b_occurrence_id"] = "ent_occ_2", ["a_name"] = "Bolt:1", ["b_name"] = "Bolt:2",
                ["volume_mm3"] = 2000.0, ["boxes"] = new JArray(new JObject { ["min_mm"] = new JArray(0, 0, 0), ["max_mm"] = new JArray(5, 5, 5) }) }),
        });

        private VerifyBackend Online()
        {
            var backend = new VerifyBackend();
            _workspace.Bind(backend, null);
            var scene = CadSceneViewTests.BoltScene(); _view.Show(scene); _workspace.SetScene(scene);
            _workspace.SetOnline(true);
            return backend;
        }

        private void Select(string occurrenceId) =>
            typeof(InspectWorkspace).GetField("_selected", Flags).SetValue(_workspace,
                ((BrowserContext)typeof(InspectWorkspace).GetField("_context", Flags).GetValue(_workspace)).Find(occurrenceId));

        private T Field<T>(string name) => (T)typeof(InspectWorkspace).GetField(name, Flags).GetValue(_workspace);
```

Verifica che `CadSceneViewTests.BoltScene()` abbia `Graph.Kind == "assembly"`; se no, aggiungi nel test una scena di assieme equivalente (stesso JSON con `"kind":"assembly"`).

I test:

```csharp
        [Test]
        public void TabsAddVisibilityAndVerifyAfterViewWithAtMostEightActions()
        {
            CollectionAssert.AreEqual(new[] { "misura", "sezione", "vista", "visibilita", "verifica" }, _workspace.Tabs.Select(t => t.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "Misura", "Sezione", "Vista", "Visibilità", "Verifica", "Spazi" }, _catalog.Tabs.Select(t => t.Label).ToArray());
            foreach (var tab in _workspace.Tabs) Assert.That(_catalog.Palette(tab.Id).Count, Is.InRange(1, 8), tab.Id);
            Assert.True(_workspace.Actions.Where(a => a.Id.StartsWith("inspect.verify.") || a.Id.StartsWith("inspect.visibility.")).All(a => !a.VoiceInvokes));
        }

        [Test]
        public void VisibilityWorksOnTheSelectionWithoutBackendCalls()
        {
            var backend = Online(); Select("ent_occ_1"); _workspace.SetOnline(false);
            Do(InspectWorkspace.IdXRay);
            var visibility = Field<ComponentVisibility>("_visibility");
            Assert.AreEqual(OccurrenceVisibility.Ghost, visibility.Get("ent_occ_1"));
            Do(InspectWorkspace.IdHide);
            Assert.AreEqual(OccurrenceVisibility.Hidden, visibility.Get("ent_occ_1"));
            Do(InspectWorkspace.IdIsolate);
            Assert.AreEqual(OccurrenceVisibility.Ghost, visibility.Get("ent_occ_2"));
            Do(InspectWorkspace.IdShowAll);
            Assert.False(visibility.AnyChanged);
            Assert.IsNull(backend.LastScope, "no Inventor call");
        }

        [Test]
        public void InventorVerificationsAreDisabledWithAReasonOfflineOrOnAPart()
        {
            Online(); _workspace.SetOnline(false);
            foreach (var id in new[] { InspectWorkspace.IdInterference, InspectWorkspace.IdHealth, InspectWorkspace.IdDistance })
            {
                Assert.False(Enabled(id), id);
                Assert.False(string.IsNullOrEmpty(Act(id).DisabledReason), id);
            }
        }

        [Test]
        public async Task InterferenceRunsOnceListsResultsAndFocusesARow()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            Assert.False(Enabled(InspectWorkspace.IdHealth), "one Inventor verification at a time");
            Assert.True(Enabled(InspectWorkspace.IdIgnore));
            backend.Interference.SetResult(OnePair());
            await Task.Yield();
            var findings = Field<IReadOnlyList<VerifyFinding>>("_findings");
            Assert.AreEqual(1, findings.Count);
            Assert.That(AllHud(), Does.Contain("1 interferenze su 2 occorrenze"));
            Do(InspectWorkspace.IdResults);
            var row = _workspace.Actions.First(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix) && a.Label.Contains("Bolt:1 ↔ Bolt:2"));
            Assert.True(row.TryInvoke());
            Assert.AreEqual(1, Field<VerifyOverlay>("_overlay").BoxCount);
            Assert.AreEqual(OccurrenceVisibility.Normal, Field<ComponentVisibility>("_visibility").Get("ent_occ_1"));
            _workspace.Back();
            Assert.AreEqual(0, Field<VerifyOverlay>("_overlay").BoxCount, "Back leaves the focus and restores the visibility");
            Assert.False(Field<ComponentVisibility>("_visibility").AnyChanged);
        }

        [Test]
        public async Task ScopeSelectionSendsTheSelectedOccurrence()
        {
            var backend = Online(); Select("ent_occ_1");
            Do(InspectWorkspace.IdScope);
            Do(InspectWorkspace.IdInterference);
            CollectionAssert.AreEqual(new[] { "ent_occ_1" }, backend.LastScope);
            backend.Interference.SetResult(OnePair());
            await Task.Yield();
        }

        [Test]
        public async Task ANewRevisionMarksTheResultsStale()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            backend.Interference.SetResult(OnePair());
            await Task.Yield();
            var state = Field<DocumentState>("_documentState");
            _workspace.SetDocumentState(new DocumentState(state.DocumentId, "r2", state.VisualRevision));
            Assert.True(Field<bool>("_findingsStale"));
            Do(InspectWorkspace.IdResults);
            Assert.True(_workspace.Actions.Any(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix) && a.Label.StartsWith("[obsoleto]")));
        }

        [Test]
        public async Task IgnoreDropsTheAnswer()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            Do(InspectWorkspace.IdIgnore);
            backend.Interference.SetResult(OnePair());
            await Task.Yield();
            Assert.AreEqual(0, Field<IReadOnlyList<VerifyFinding>>("_findings").Count);
        }

        [Test]
        public async Task DistanceTakesTwoSelectionsAndDrawsTheInventorLine()
        {
            var backend = Online();
            Select("ent_occ_1"); Do(InspectWorkspace.IdDistance);
            Select("ent_occ_2"); Do(InspectWorkspace.IdDistance);
            await Task.Yield();
            Assert.AreEqual("ent_occ_1", backend.LastA); Assert.AreEqual("ent_occ_2", backend.LastB);
            var overlay = Field<VerifyOverlay>("_overlay");
            Assert.True(overlay.HasDistance);
            Assert.AreEqual("30 mm", overlay.DistanceLabel);
            Assert.That(AllHud(), Does.Contain("Distanza minima (Inventor): 30 mm"));
        }

        [Test]
        public async Task WithoutInventorPointsTheLineIsIndicative()
        {
            var backend = Online();
            backend.Distance = DistanceReport.FromJson(new JObject { ["revision"] = "r1", ["distance_mm"] = 30.0 });
            Select("ent_occ_1"); Do(InspectWorkspace.IdDistance);
            Select("ent_occ_2"); Do(InspectWorkspace.IdDistance);
            await Task.Yield();
            Assert.AreEqual("30 mm (linea indicativa)", Field<VerifyOverlay>("_overlay").DistanceLabel);
        }

        [Test]
        public void TheLocalMeasureIsCalledPointToPoint()
        {
            Online();
            Do(InspectWorkspace.IdMeasure);
            Assert.That(AllHud(), Does.Contain("Punto-punto (locale)"));
            Assert.That(AllHud(), Does.Not.Contain("approssimata"));
        }
```

`Task.Yield()` in EditMode: se la continuazione `async void` del workspace non è ancora eseguita dopo un solo `Yield`, usa un helper che chiama `await Task.Yield()` fino a 10 volte finché la condizione non è vera (gli altri test EditMode del repo usano lo stesso schema con `async Task` e NUnit).

- [ ] **Passo 2: verifica che falliscano**

Run: EditMode con filtro `InventorXrSo.Tests.InspectVerifyTests` → errore di compilazione.

- [ ] **Passo 3: crea `InspectVerify.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Verify;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// M7 part of Ispeziona: visibility (X-Ray, isolate, hide) and the Inventor verifications (interference, minimum distance,
    /// health and BOM). Read-only: nothing here changes the CAD. Results are bound to the revision they were computed on.
    /// </summary>
    public sealed partial class InspectWorkspace
    {
        private IVerifyBackend _verify;
        private readonly VerifySession _verifySession = new VerifySession();
        private CancellationTokenSource _verifyRequests = new CancellationTokenSource();
        private ComponentVisibility _visibility;
        private VerifyOverlay _overlay;
        private IReadOnlyList<VerifyFinding> _findings = Array.Empty<VerifyFinding>();
        private IVerifyJob _findingsJob;
        private bool _findingsStale, _scopeSelection;
        private SceneNode _distanceA;
        private IReadOnlyDictionary<string, OccurrenceVisibility> _focusSnapshot;
        private float _runningSince;
        private int _runningShown = -1;

        private void InitializeVerify(Material lineMaterial)
        {
            _visibility = _view.gameObject.AddComponent<ComponentVisibility>();
            _visibility.Initialize(_view);
            _overlay = new GameObject("Verifiche").AddComponent<VerifyOverlay>();
            _overlay.transform.SetParent(_view.transform, false);
            _overlay.Initialize(lineMaterial, _head);
            _verifySession.Interference.Changed += () => OnFindingsJob(_verifySession.Interference, VerifyFindings.FromInterference, VerifyFindings.Summary);
            _verifySession.Health.Changed += () => OnFindingsJob(_verifySession.Health, VerifyFindings.FromHealth, VerifyFindings.Summary);
            _verifySession.Distance.Changed += OnDistanceChanged;
        }

        private void BindVerify(IInspectionBackend backend)
        {
            _verify = backend as IVerifyBackend;
            ResetVerify();
        }

        /// <summary>New document or new backend: in-flight answers are ignored, results and drawings go away.</summary>
        private void ResetVerify()
        {
            _verifySession.Reset();
            _findings = Array.Empty<VerifyFinding>(); _findingsJob = null; _findingsStale = false;
            _distanceA = null; _focusSnapshot = null;
            _overlay?.Clear(); _visibility?.ShowAll();
        }

        /// <summary>Leaving Ispeziona: the scene goes back to normal; results stay listed.</summary>
        private void LeaveVerifyView()
        {
            _focusSnapshot = null; _distanceA = null;
            _overlay?.Clear(); _visibility?.ShowAll();
        }

        // ---------------------------------------------------------------- availability

        private bool IsAssembly => _scene?.Graph.Kind == "assembly";

        private bool VerifyReady => Backend && _verify != null && IsAssembly && _documentState != null && _verifySession.Gate.CanStart;

        private string VerifyReason()
        {
            if (!Active) return "Ispeziona non è aperto.";
            if (!_online) return "Offline — verifiche di Inventor non disponibili.";
            if (_scene == null) return "Nessun documento disponibile.";
            if (!IsAssembly) return "Serve un assieme.";
            if (_verify == null) return "Verifiche non disponibili su questo server.";
            return _verifySession.Gate.Reason ?? BackendReason();
        }

        /// <summary>A direct occurrence of the root, selected at the root context: what the M7 tools accept.</summary>
        private bool DirectSelection => _selected != null && _context.Path.Count == 1 && _context.Current != null
            && _context.Current.Children.Contains(_selected) && !_selected.Suppressed;

        private IEnumerable<string> LeafIds(SceneNode node) =>
            node == null ? Enumerable.Empty<string>() : BrowserContext.Descendants(node).Where(n => n.DefinitionKind == "part").Select(n => n.OccurrenceId);

        private IEnumerable<string> LeafIds(string occurrenceId) => LeafIds(_context.Find(occurrenceId));

        private IEnumerable<CadInstance> Instances(IEnumerable<string> leafIds) => leafIds.Select(_view.Find).Where(i => i != null);

        // ---------------------------------------------------------------- visibility

        private void VisibilityOnSelection(Action<IEnumerable<string>> apply, string notice)
        {
            ClearFocus();
            apply(LeafIds(_selected).ToArray());
            SetNotice(notice + ": " + _selected.Name);
            Refresh();
        }

        private void ShowAllComponents()
        {
            ClearFocus();
            _visibility.ShowAll();
            SetNotice("Tutti i componenti visibili.");
            Refresh();
        }

        // ---------------------------------------------------------------- verifications

        private void BeginRun()
        {
            ClearFocus(); ClosePicker(false);
            _runningSince = Time.unscaledTime; _runningShown = -1;
        }

        private async void RunInterference()
        {
            var state = _documentState;
            IReadOnlyList<string> scope = _scopeSelection && DirectSelection ? new[] { _selected.OccurrenceId } : null;
            BeginRun();
            await _verifySession.Interference.RunAsync(ct => _verify.CheckInterferenceAsync(state, scope, ct), _verifyRequests.Token);
        }

        private async void RunHealth()
        {
            var state = _documentState;
            BeginRun();
            await _verifySession.Health.RunAsync(ct => _verify.GetAssemblyHealthAsync(state, ct), _verifyRequests.Token);
        }

        private async void Distance()
        {
            if (_distanceA == null)
            {
                _distanceA = _selected;
                _overlay.ClearDistance();
                SetNotice("Distanza minima da " + _distanceA.Name + ": seleziona il secondo componente e premi di nuovo.");
                Refresh();
                return;
            }
            if (_selected == _distanceA) { SetNotice("Scegli un componente diverso da " + _distanceA.Name + "."); Refresh(); return; }
            var state = _documentState; string a = _distanceA.OccurrenceId, b = _selected.OccurrenceId;
            BeginRun();
            await _verifySession.Distance.RunAsync(ct => _verify.MeasureMinDistanceAsync(state, a, b, ct), _verifyRequests.Token);
        }

        private void IgnoreRunning()
        {
            _verifySession.Running?.Ignore();
            SetNotice("Risultato ignorato. Inventor completa comunque il calcolo in corso.");
            Refresh();
        }

        private void OnFindingsJob<T>(VerifyJob<T> job, Func<T, IReadOnlyList<VerifyFinding>> rows, Func<T, string> summary) where T : class, IVerifyResult
        {
            switch (job.Status)
            {
                case VerifyStatus.Done:
                    _findings = rows(job.Result); _findingsJob = job; _findingsStale = false;
                    SetNotice(summary(job.Result) + (_findings.Count > 0 ? "\nApri Risultati per vederli uno a uno." : ""));
                    break;
                case VerifyStatus.Failed: SetNotice(job.ErrorMessage); break;
                case VerifyStatus.Stale:
                    if (_findingsJob == job) _findingsStale = true;
                    SetNotice("Modello cambiato: rilancia la verifica.");
                    break;
            }
            Refresh();
        }

        private void OnDistanceChanged()
        {
            var job = _verifySession.Distance;
            switch (job.Status)
            {
                case VerifyStatus.Done:
                {
                    var report = job.Result;
                    var a = _context.Find(_distanceA?.OccurrenceId ?? ""); var b = _selected;
                    string value = VerifyFindings.Millimetres(report.DistanceMm) + " mm";
                    if (report.HasPoints) _overlay.ShowDistance(VerifyOverlay.ToLocal(report.PointAMm), VerifyOverlay.ToLocal(report.PointBMm), value);
                    else if (VerifyOverlay.ClosestVertices(Instances(LeafIds(a)), Instances(LeafIds(b)), _view.transform, out var pa, out var pb))
                        _overlay.ShowDistance(pa, pb, value + " (linea indicativa)");
                    SetNotice("Distanza minima (Inventor): " + value + (report.HasPoints ? "" : "\nLinea indicativa: punti calcolati sulle mesh del visore."));
                    _distanceA = null;
                    break;
                }
                case VerifyStatus.Failed: SetNotice(job.ErrorMessage); _distanceA = null; break;
                case VerifyStatus.Stale: _overlay.ClearDistance(); SetNotice("Modello cambiato: rilancia la distanza minima."); break;
            }
            Refresh();
        }

        /// <summary>HUD clock while Inventor computes.</summary>
        private void TickVerify()
        {
            if (_verifySession.Running == null) return;
            int seconds = (int)(Time.unscaledTime - _runningSince);
            if (seconds == _runningShown) return;
            _runningShown = seconds;
            SetNotice("Verifica in corso in Inventor… " + seconds + " s");
        }

        // ---------------------------------------------------------------- results

        private void OpenResults()
        {
            OpenPicker("Risultati", _findings.Select(f =>
            {
                var finding = f;
                string label = (_findingsStale ? "[obsoleto] " : "") + (finding.Severity == FindingSeverity.Error ? "● " : "○ ") + finding.Title;
                return new PickerItem(label, () => FocusFinding(finding));
            }));
        }

        private void FocusFinding(VerifyFinding finding)
        {
            if (_focusSnapshot == null) _focusSnapshot = _visibility.Snapshot();
            _overlay.Clear();
            var leaves = finding.OccurrenceIds.SelectMany(LeafIds).Distinct().ToArray();
            if (leaves.Length > 0)
            {
                _visibility.Isolate(leaves);
                var instances = Instances(leaves).ToArray();
                _overlay.Tint(instances);
                var bounds = InspectionGeometry.InstancesBounds(_view.transform, instances);
                if (bounds.HasValue) FocusOn(bounds.Value);
            }
            _overlay.ShowBoxes(finding.Boxes);
            SetNotice(finding.Title + "\n" + finding.Detail + (_findingsStale ? "\nRisultato obsoleto: rilancia la verifica." : "") + "\nIndietro per tornare alla vista.");
            Refresh();
        }

        /// <summary>True when a row was in focus and the view went back to its previous state.</summary>
        private bool ClearFocus()
        {
            if (_focusSnapshot == null) return false;
            _visibility.Restore(_focusSnapshot);
            _focusSnapshot = null;
            _overlay.Clear();
            return true;
        }

        /// <summary>Brings the given model-local bounds in front of the user. View only.</summary>
        private void FocusOn(Bounds bounds)
        {
            if (_head == null) return;
            var root = _view.transform;
            var scaled = new Bounds(bounds.center * root.localScale.x, bounds.size * root.localScale.x);
            var pose = ScenePlacement.InFront(scaled, _head.position, _head.forward);
            root.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private void DisposeVerify()
        {
            _verifyRequests.Cancel(); _verifyRequests.Dispose();
        }
    }
}
```

Note per l'implementatore:
- `PickerItem` e `OpenPicker` sono già in `InspectActions.cs` (classe annidata privata della stessa partial): nessuna modifica.
- `ScenePlacement.InFront(Bounds, Vector3, Vector3)` è la stessa chiamata di `Recenter()`.
- `SetNotice` durante `TickVerify` passa da `HudMessage` ogni secondo: è voluto (il badge mostra il tempo).

- [ ] **Passo 4: aggancia M7 in `InspectWorkspace.cs`**

Modifiche puntuali:
- `Initialize(...)`: dopo `_ray.PointPicked += OnPointPicked;` aggiungi `InitializeVerify(ray.LineMaterial);`.
- `Bind(...)`: dopo `_backend = backend; _selection = selection;` aggiungi `BindVerify(backend);`.
- `SetScene(...)`: dopo il calcolo di `changedDocument` aggiungi `if (changedDocument) ResetVerify(); else { _overlay?.Clear(); _focusSnapshot = null; }`.
- `SetDocumentState(...)`: subito dopo il `return` di uguaglianza aggiungi
  ```csharp
            if (_documentState?.DocumentId != state?.DocumentId) ResetVerify();
            else _verifySession.OnDocumentState(state);
  ```
  Il blocco esistente cancella `_selected`: annulla anche la distanza a metà, aggiungendo `_distanceA = null;` nello stesso blocco.
- `OthersChanged()` e `SetVisible(bool)`: nel ramo "non attivo" aggiungi `LeaveVerifyView();`.
- `Back()`: dopo `if (_picker != null) { ClosePicker(); return; }` aggiungi `if (ClearFocus()) { SetNotice(""); Refresh(); return; }` e `if (_distanceA != null) { _distanceA = null; SetNotice("Distanza annullata."); Refresh(); return; }`.
- `Update()`: dopo `DropClosedKeypad();` aggiungi `TickVerify();`.
- `OnDestroy()`: aggiungi `DisposeVerify();`.
- Etichette della misura locale:
  - `BeginMeasure`: `SetNotice("Punto-punto (locale): seleziona il primo punto sulla mesh.");`
  - `OnPointPicked`: `: "Punto-punto (locale): ≈ " + Format(_measure.DistanceMm, "mm"));`

- [ ] **Passo 5: schede e azioni in `InspectActions.cs`**

Costanti (accanto alle esistenti):

```csharp
        public const string TabVisibility = "visibilita", TabVerify = "verifica";
        public const string IdXRay = "inspect.visibility.xray", IdIsolate = "inspect.visibility.isolate", IdHide = "inspect.visibility.hide",
            IdShowAll = "inspect.visibility.showall",
            IdInterference = "inspect.verify.interference", IdScope = "inspect.verify.scope", IdDistance = "inspect.verify.distance",
            IdHealth = "inspect.verify.health", IdResults = "inspect.verify.results", IdIgnore = "inspect.verify.ignore";
```

`StaticTabs`:

```csharp
        private static readonly XrTab[] StaticTabs =
        {
            new XrTab(TabMeasure, "Misura"), new XrTab(TabSection, "Sezione"), new XrTab(TabView, "Vista"),
            new XrTab(TabVisibility, "Visibilità"), new XrTab(TabVerify, "Verifica"),
        };
```

Etichetta dell'azione `IdMeasure`: da `"Misura"` a `"Punto-punto (locale)"` (id e sinonimi invariati).

In `BuildActions`, dopo il blocco "Vista" e prima del blocco `if (_picker != null)`:

```csharp
                // Visibilità (local, view only)
                new XrAction(IdXRay, "X-Ray", TabVisibility, () => Local && _selected != null,
                    () => VisibilityOnSelection(_visibility.XRay, "X-Ray"), () => !Active ? LocalReason() : "Seleziona prima un componente.",
                    voiceInvokes: false),
                new XrAction(IdIsolate, "Isola", TabVisibility, () => Local && _selected != null,
                    () => VisibilityOnSelection(_visibility.Isolate, "Isolato"), () => !Active ? LocalReason() : "Seleziona prima un componente.",
                    voiceInvokes: false),
                new XrAction(IdHide, "Nascondi", TabVisibility, () => Local && _selected != null,
                    () => VisibilityOnSelection(_visibility.Hide, "Nascosto"), () => !Active ? LocalReason() : "Seleziona prima un componente.",
                    voiceInvokes: false),
                new XrAction(IdShowAll, "Mostra tutto", TabVisibility, () => Local && (_visibility.AnyChanged || _focusSnapshot != null), ShowAllComponents,
                    () => !Active ? LocalReason() : "Tutti i componenti sono già visibili.", voiceInvokes: false),

                // Verifica (Inventor, read-only)
                new XrAction(IdInterference, _scopeSelection && _selected != null ? "Interferenze di " + _selected.Name : "Interferenze", TabVerify,
                    () => VerifyReady && (!_scopeSelection || DirectSelection), RunInterference,
                    () => VerifyReady ? "Seleziona un componente di primo livello o togli Solo selezione." : VerifyReason(), voiceInvokes: false),
                new XrAction(IdScope, "Solo selezione", TabVerify, () => Local && (_scopeSelection || DirectSelection),
                    () => { _scopeSelection = !_scopeSelection; Refresh(); },
                    () => !Active ? LocalReason() : "Seleziona un componente di primo livello.", kind: XrActionKind.Toggle,
                    isOn: () => _scopeSelection, voiceInvokes: false),
                new XrAction(IdDistance, _distanceA == null ? "Distanza minima" : "Distanza minima da " + _distanceA.Name, TabVerify,
                    () => VerifyReady && DirectSelection, Distance,
                    () => VerifyReady ? "Seleziona un componente di primo livello." : VerifyReason(), voiceInvokes: false),
                new XrAction(IdHealth, "Salute assieme", TabVerify, () => VerifyReady, RunHealth, VerifyReason, voiceInvokes: false),
                new XrAction(IdResults, "Risultati (" + _findings.Count + ")", TabVerify, () => Active && _findings.Count > 0, OpenResults,
                    () => !Active ? LocalReason() : "Nessun risultato da mostrare.", voiceInvokes: false),
                new XrAction(IdIgnore, "Ignora risultato", TabVerify, () => Active && _verifySession.Running != null, IgnoreRunning,
                    () => "Nessuna verifica in corso.", voiceInvokes: false),
```

Il campo `_visibility` è creato in `Initialize`: nei test headless senza `Initialize` le lambda non vengono valutate prima di `Initialize`, come per `_section`.

- [ ] **Passo 6: aggiorna test e runner esistenti**

Cerca le occorrenze da allineare:

```bash
grep -rn "\"Spazi\" }\|approssimata\|\"Misura\", \"Sezione\", \"Vista\"" "Inventor XR SO/Assets/XrSo"
```

- `InspectWorkspaceTests.TabsAreMeasureSectionViewWithAtMostEightActionsEachAndUniqueIds`: id attesi `{ "misura", "sezione", "vista", "visibilita", "verifica" }`, etichette `{ "Misura", "Sezione", "Vista", "Visibilità", "Verifica", "Spazi" }`. Rinomina il test in `TabsAreMeasureSectionViewVisibilityVerifyWithAtMostEightActionsEachAndUniqueIds`. Le id devono iniziare con `inspect.`: già vero.
- `M2QuestAcceptance.cs` (riga con `tabs.SequenceEqual(new[] { "Misura", "Sezione", "Vista", "Spazi" })`): nuova sequenza `{ "Misura", "Sezione", "Vista", "Visibilità", "Verifica", "Spazi" }`; aggiorna anche il messaggio di `Pass("M2-Inspect", …)`.
- `M6QuestAcceptance.cs`: stesso aggiornamento dove controlla le schede di Ispeziona.
- Ogni test che si aspetta "approssimata" nel testo della misura locale: ora "Punto-punto (locale)".

- [ ] **Passo 7: tutti i test EditMode**

Run: EditMode completo (senza filtro).
Expected: PASS, con il totale precedente più i test nuovi. Se qualche test esistente fallisce per l'etichetta `Punto-punto (locale)` o per le schede, aggiornalo come al passo 6, mai cambiando il comportamento.

- [ ] **Passo 8: commit**

```bash
git add "Inventor XR SO/Assets/XrSo"
git commit -m "feat(xr): Inspect Visibility and Verify tabs with interference, minimum distance, health and results"
```

---

### Task 10: runner M7 sul Quest

**File:**
- Nuovo: `Inventor XR SO/Assets/XrSo/Xr/Acceptance/M7QuestAcceptance.cs`
- Modifica: `Inventor XR SO/Assets/XrSo/Tests/EditMode/QuestAcceptanceContractTests.cs`
- Modifica: `scripts/run-quest-acceptance.ps1`
- Modifica: `docs/xr-quest-acceptance.md`

**Interfacce:**
- Consuma: costanti e campi del task 9; fixture del task 1 (`XR_M7_Quest_Acceptance`, occorrenze `M7_A`…`M7_D`).
- Produce: intent `xr_m7_acceptance`, log `m7-acceptance.txt`, screenshot `m7-acceptance-*.png`.

- [ ] **Passo 1: contratto (test che fallisce)**

In `QuestAcceptanceContractTests`: aggiungi `"M7QuestAcceptance",` a `RunnerTypeNames` e `[TestCase("M7QuestAcceptance")]` a `ReflectedMembersExist` e a `ReflectedMembersListIsComplete`.

Run: EditMode con filtro `InventorXrSo.Tests.QuestAcceptanceContractTests` → FAIL (sorgente del runner M7 assente).

- [ ] **Passo 2: crea il runner**

```csharp
#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Verify;
using ActionCatalog = InventorXrSo.Core.Ui.ActionCatalog;
using XrAction = InventorXrSo.Core.Ui.XrAction;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Opt-in, fixture-scoped M7 acceptance runner (Ispeziona, engineering verification). Every action is invoked by id through the
    /// catalog: SYNTHETIC input. Interference, distance and health run against the real Inventor fixture.
    /// </summary>
    internal sealed class M7QuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers =
        {
            "AppController._inspect",
            "AppController._catalog",
            "AppController.sceneView",
            "AppController.EnterSession",
            "InspectWorkspace._context",
            "InspectWorkspace._busy",
            "InspectWorkspace._documentState",
            "InspectWorkspace._verifySession",
            "InspectWorkspace._visibility",
            "InspectWorkspace._overlay",
            "InspectWorkspace._findings",
            "InspectWorkspace._findingsStale",
        };

        private const double VolumeMm3 = 2000, DistanceMm = 30;

        protected override string Milestone => "m7";
        protected override int TimeoutSeconds => 420;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M7QuestAcceptance>("xr_m7_acceptance");

        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private ActionCatalog Catalog => Read<ActionCatalog>(App, "_catalog");
        private InspectWorkspace Inspect => Read<InspectWorkspace>(App, "_inspect");

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            RequireFixture();
            Check(fixture.Graph.Kind == "assembly", "fixture scene is the assembly");
            Call(App, "EnterSession", EnvironmentMode.MixedReality);
            var inspect = Inspect;
            var view = Read<CadSceneView>(App, "sceneView");
            var context = Read<BrowserContext>(inspect, "_context");
            var session = Read<VerifySession>(inspect, "_verifySession");
            var visibility = Read<ComponentVisibility>(inspect, "_visibility");
            var overlay = Read<VerifyOverlay>(inspect, "_overlay");
            Check(view.Instances.Count == 4, "the fixture shows 4 cubes, found " + view.Instances.Count);
            Node(context, "M7_A"); Node(context, "M7_B"); Node(context, "M7_C"); Node(context, "M7_D");
            Record("Actions are invoked by id through the catalog (synthetic input); Inventor answers are real");

            // M7-01: visibility, local only.
            await SelectByBrowser(inspect, "M7_C", ct);
            var c = view.Find(Node(context, "M7_C").OccurrenceId);
            RunAction(InspectWorkspace.IdHide);
            Check(visibility.Get(c.OccurrenceId) == OccurrenceVisibility.Hidden, "M7_C is hidden");
            Check(!RayHits(view, c), "the ray passes through the hidden M7_C");
            RunAction(InspectWorkspace.IdXRay);
            Check(visibility.Get(c.OccurrenceId) == OccurrenceVisibility.Ghost && RayHits(view, c), "the ghost M7_C is still hit by the ray");
            RunAction(InspectWorkspace.IdIsolate);
            Check(view.Instances.Where(i => i != c).All(i => visibility.Get(i.OccurrenceId) == OccurrenceVisibility.Ghost), "isolate ghosts the other three cubes");
            RunAction(InspectWorkspace.IdShowAll);
            Check(!visibility.AnyChanged, "Mostra tutto restores every cube");
            Pass("M7-01", "hide (ray passes), X-Ray (ray hits), isolate and show all on the scene, no Inventor call");

            // M7-02: interference against the real fixture.
            Check(!session.Gate.Busy, "no verification is running");
            RunAction(InspectWorkspace.IdInterference);
            await WaitUntil(() => session.Interference.Status != VerifyStatus.Running, ct);
            Check(session.Interference.Status == VerifyStatus.Done, "interference finished: " + session.Interference.ErrorMessage);
            var interference = session.Interference.Result;
            Check(interference.Count == 1, "exactly one interfering pair, found " + interference.Count);
            var pair = interference.Pairs[0];
            Check(new[] { pair.AName, pair.BName }.OrderBy(n => n).SequenceEqual(new[] { "M7_A", "M7_B" }), "the pair is M7_A / M7_B: " + pair.AName + " / " + pair.BName);
            Check(Math.Abs(pair.VolumeMm3 - VolumeMm3) <= VolumeMm3 * 0.01, "volume 2000 mm3 +/- 1%, found " + F(pair.VolumeMm3));
            RunAction(InspectWorkspace.IdResults);
            PickRow("M7_A");
            Check(view.Instances.Count(i => visibility.Get(i.OccurrenceId) == OccurrenceVisibility.Ghost) == 2, "the row focuses the pair and ghosts the other two cubes");
            if (pair.Boxes.Count > 0) Check(overlay.BoxCount == pair.Boxes.Count, "one red box per interference body");
            else NotCovered("M7-02", "Inventor returned no interference body box: the pair is shown in red without boxes");
            await CaptureScreenshot("interference", ct);
            inspect.Back();
            Pass("M7-02", "1 pair M7_A/M7_B, " + F(pair.VolumeMm3) + " mm3, " + pair.Boxes.Count + " box(es), elapsed " + (interference.ElapsedMs?.ToString() ?? "?") + " ms; row focus and Back verified");

            // M7-03: minimum distance M7_A - M7_C.
            await SelectByBrowser(inspect, "M7_A", ct);
            RunAction(InspectWorkspace.IdDistance);
            await SelectByBrowser(inspect, "M7_C", ct);
            RunAction(InspectWorkspace.IdDistance);
            await WaitUntil(() => session.Distance.Status != VerifyStatus.Running, ct);
            Check(session.Distance.Status == VerifyStatus.Done, "distance finished: " + session.Distance.ErrorMessage);
            var distance = session.Distance.Result;
            Check(Math.Abs(distance.DistanceMm - DistanceMm) <= 0.01, "distance 30 mm +/- 0.01, found " + F(distance.DistanceMm));
            Check(overlay.HasDistance, "the distance line is drawn");
            Check(overlay.DistanceLabel.EndsWith("(linea indicativa)", StringComparison.Ordinal) == !distance.HasPoints, "the label says whether the line is Inventor's or indicative");
            await CaptureScreenshot("distance", ct);
            Pass("M7-03", "M7_A-M7_C " + F(distance.DistanceMm) + " mm; line " + (distance.HasPoints ? "from Inventor points" : "indicative (no Inventor points)"));

            // M7-04: health and BOM.
            RunAction(InspectWorkspace.IdHealth);
            await WaitUntil(() => session.Health.Status != VerifyStatus.Running, ct);
            Check(session.Health.Status == VerifyStatus.Done, "health finished: " + session.Health.ErrorMessage);
            var health = session.Health.Result;
            Check(health.Issues.Any(i => i.Name == "M7_Sick"), "M7_Sick is reported as failing");
            Check(health.Unconstrained.Select(u => u.Name).SequenceEqual(new[] { "M7_D" }), "M7_D is the only unconstrained cube");
            Check(health.BomIssues.Any(b => b.Code == "PART_NUMBER_MISSING"), "the BOM reports the blank part number");
            var findings = Read<IReadOnlyList<VerifyFinding>>(inspect, "_findings");
            Check(findings.Any(f => f.Title == "Vincolo in errore: M7_Sick"), "the results list carries the failing constraint");
            var sick = health.Issues.First(i => i.Name == "M7_Sick");
            if (sick.AOccurrenceId == null && sick.BOccurrenceId == null)
                NotCovered("M7-04", "Inventor did not expose the occurrences of M7_Sick: its row has no highlight");
            Pass("M7-04", "M7_Sick failing, M7_D unconstrained, PART_NUMBER_MISSING; " + findings.Count + " rows");

            // M7-05: stale (synthetic revision), ignore and refusal of a concurrent run.
            var state = Read<DocumentState>(inspect, "_documentState");
            inspect.SetDocumentState(new DocumentState(state.DocumentId, state.Revision + ":synthetic", state.VisualRevision));
            Check(session.Health.Status == VerifyStatus.Stale && ReadBoolean(inspect, "_findingsStale"), "a new revision marks the health result stale");
            Record("M7-05 stale used a SYNTHETIC revision on the client; the real revision check is covered by XrSo.Core.Tests (FakeAddIn)");
            await WaitFor(() => Session.Document?.Revision == state.Revision ? Session.Document : null, ct);
            inspect.SetDocumentState(Session.Document);
            await WaitUntil(() => VerifyEnabled(InspectWorkspace.IdInterference), ct);
            RunAction(InspectWorkspace.IdInterference);
            Check(!Catalog.Find(InspectWorkspace.IdHealth).Enabled, "a second verification is refused while one runs");
            RunAction(InspectWorkspace.IdIgnore);
            Check(session.Interference.Status == VerifyStatus.Idle, "ignored: no result will be shown");
            await WaitUntil(() => !session.Gate.Busy, ct);
            Check(session.Interference.Result == null, "the ignored answer was discarded");
            Pass("M7-05", "stale on revision change (synthetic), concurrent run refused, ignored answer discarded after Inventor answered");

            // M7-07: timings on the fixture only.
            Pass("M7-07", "fixture timings: interference " + (interference.ElapsedMs?.ToString() ?? "?") + " ms");
            NotCovered("M7-07", "timing on a real user assembly is measured by the --probe-active PC probe, not by this runner");
            NotCovered("M7-06", "offline and part-document enablement is covered by EditMode tests, not on the headset");
            NotCovered("M7-08", "physical: readability of red, ghosts, boxes, line and list while seated, real controller tracking");

            RunAction(InspectWorkspace.IdShowAll);
        }

        private static SceneNode Node(BrowserContext context, string name)
        {
            var node = BrowserContext.Descendants(context.Graph.Root).FirstOrDefault(n => n.Name == name);
            Check(node != null, "the fixture has occurrence " + name);
            return node;
        }

        private bool VerifyEnabled(string id) => Catalog.Find(id)?.Enabled == true;

        private void RunAction(string id)
        {
            var action = Catalog.Find(id);
            Check(action != null, "the action catalog has no action '" + id + "'");
            Check(action.Enabled, "action '" + id + "' is disabled: " + action.DisabledReason);
            Check(action.TryInvoke(), "action '" + id + "' did not run");
        }

        private XrAction FindPick(string text) => Inspect.Actions
            .FirstOrDefault(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal) && a.Label.Contains(text));

        private void PickRow(string text)
        {
            var action = FindPick(text);
            Check(action != null && action.TryInvoke(), "the open list has an entry containing '" + text + "'");
        }

        /// <summary>Esplora, then the component by name: the same path as the palette (synthetic).</summary>
        private async Task SelectByBrowser(InspectWorkspace inspect, string name, CancellationToken ct)
        {
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
            RunAction(InspectWorkspace.IdBrowse);
            var action = Inspect.Actions.FirstOrDefault(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal) && a.Label.EndsWith(name, StringComparison.Ordinal));
            Check(action != null && action.TryInvoke(), "Esplora lists " + name);
            await WaitUntil(() => !ReadBoolean(inspect, "_busy"), ct);
        }

        private static bool RayHits(CadSceneView view, CadInstance instance)
        {
            Physics.SyncTransforms();
            var bounds = InspectionGeometry.InstancesBounds(view.transform, new[] { instance }).Value;
            var center = view.transform.TransformPoint(bounds.center);
            return CadRaycaster.TryPick(new Ray(center + Vector3.up, Vector3.down), 3f, out var body, out _, out _) && body.Instance == instance;
        }
    }
}
#endif
```

Controlla che `SessionController` esponga `Document` (è la proprietà usata in `RefreshAsync`) e che `CadRaycaster.TryPick` abbia la firma usata dal runner M2; allinea se differiscono, poi riesegui il test di contratto.

- [ ] **Passo 3: script e documentazione dei runner**

In `scripts/run-quest-acceptance.ps1`: `[ValidateSet('m1', 'm2', 'm3', 'm4', 'm5', 'm6', 'm7')]` e, dopo la riga di `m6`:

```powershell
# The M7 runner waits for three Inventor computations on the fixture.
if ($Milestone -eq 'm7' -and -not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 480 }
```

In `docs/xr-quest-acceptance.md`: aggiungi M7 alla tabella delle fixture (`bridge/tests/QuestAcceptanceFixtures`, `m7`) e una sezione breve con la sequenza:

```bash
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest m7
# run-quest-acceptance.ps1 -Milestone m7 -Apk <apk QA> -OrdinaryApk <apk ordinario>
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --inspect-quest m7
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --restore-quest m7
```

Ricorda nella sezione: la workstation non ha PowerShell 7, si usa la copia per PS 5.1 come nelle run M6; `adb` non è nel PATH; il visore deve essere sveglio.

- [ ] **Passo 4: test di contratto verdi**

Run: EditMode con filtro `InventorXrSo.Tests.QuestAcceptanceContractTests` → PASS.

- [ ] **Passo 5: commit**

```bash
git add "Inventor XR SO/Assets/XrSo/Xr/Acceptance/M7QuestAcceptance.cs" "Inventor XR SO/Assets/XrSo/Tests/EditMode/QuestAcceptanceContractTests.cs" scripts/run-quest-acceptance.ps1 docs/xr-quest-acceptance.md
git commit -m "feat(xr): M7 Quest acceptance runner for Inspect verification (not yet run live)"
```

---

### Task 11: esecuzione dal vivo e verbale

Richiede Windows, Inventor 2027, host HTTPS `--target 2027` con `--enable-experimental`, add-in SO compilato con `-p:SoExperimental=true` e avviato con `INVENTOR_SO_EXPERIMENTAL=1`, Quest associato e sveglio. Se un prerequisito manca, fermati e chiedi all'utente; non segnare gate come passati.

- [ ] **Passo 1: suite automatiche complete**

```bash
dotnet test bridge/tests/Bimwright.Ipt.Tests
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
```

Più EditMode completo. Registra i totali nel verbale.

- [ ] **Passo 2: pacchetto e add-in**

Chiudi Inventor, compila e installa con `scripts/build-inventor-so.ps1` e `scripts/install-inventor-so.ps1` (variante sperimentale, come per M6), riavvia Inventor e l'host HTTPS. Verifica con `inventor_get_capabilities` che `experimental_enabled` sia vero e che i tre tool compaiano.

- [ ] **Passo 3: APK QA e run**

Compila l'APK di accettazione (`InventorXrSo.Editor.XrSoBuild.BuildAcceptanceApkBatch`) e l'APK ordinario, prepara la fixture `m7`, esegui il runner, ispeziona e ripristina la fixture (sequenza del task 10). Conserva manifest, log e screenshot in `artifacts/m7-verification/`.

Un timeout `before_runner_start` non è un fallimento del test: sveglia il visore e ripeti.

- [ ] **Passo 4: compila `docs/xr-m7-verification.md`**

Per ogni gate: esito (`PASS`, `NOT COVERED`, `APERTO`), livello di evidenza (test, runner sintetico, fisico) e riferimento a log o manifest. M7-08 resta `APERTO (prova fisica)` finché l'utente non la esegue. Aggiungi la richiesta fisica ridotta: "Con il visore addosso, da seduto: il rosso delle interferenze, i fantasmi, i box, la linea della distanza e la lista Risultati sono leggibili? Il controller seleziona i componenti senza fatica?"

- [ ] **Passo 5: commit**

```bash
git add docs/xr-m7-verification.md
git commit -m "docs(xr): record M7 runs and open gates"
```

---

### Task 12: documentazione di progetto

**File:**
- Modifica: `CLAUDE.md` (elenco milestone XR)
- Modifica: `docs/DEVELOPMENT.md` (tool nuovi, tier sperimentale, gate aperti)

- [ ] **Passo 1: `CLAUDE.md`**

Nel paragrafo "Milestone XR" aggiungi dopo M6: `· **M7 Ispeziona, verifica ingegneristica degli assiemi** (visibilità, interferenze, distanza minima, salute e BOM; stato in `docs/xr-m7-verification.md`; spec: `docs/superpowers/specs/2026-10-03-inventor-xr-so-m7-inspect-verifica-design.md`)`. Lo stato scritto deve corrispondere al verbale (implementato / collaudo aperto).

- [ ] **Passo 2: `docs/DEVELOPMENT.md`**

Aggiungi i tre tool `inventor_check_interference_xr`, `inventor_measure_min_distance_xr`, `inventor_assembly_health_xr` tra i sperimentali, con lo stato del collaudo dal vivo (promozione solo dopo M7-02, M7-03, M7-04 dal vivo) e l'esito delle sonde del task 1.

- [ ] **Passo 3: commit**

```bash
git add CLAUDE.md docs/DEVELOPMENT.md
git commit -m "docs: M7 Inspect verification in the project map and development status"
```

---

## Copertura della spec

| Requisito della spec | Task |
|---|---|
| Visibilità: X-Ray, isola, nascondi, mostra tutto; collider; reset su `Rebuilt` e uscita | 7, 9 |
| `inventor_check_interference_xr` con id, box, due insiemi, `elapsed_ms` | 2 |
| `inventor_measure_min_distance_xr` con punti opzionali | 3 |
| `inventor_assembly_health_xr` con occorrenze dei vincoli e BOM, ricontrollo revisione | 4 |
| DTO e backend client | 5 |
| Stati Idle/Running/Done/Failed/Stale, una verifica alla volta, Ignora, TIMEOUT 30 s | 6, 9 |
| Messaggi di errore in italiano | 6 |
| `VerifyFinding`, lista Risultati, focus su riga, Indietro | 6, 9 |
| Tinta rossa, box, linea Inventor o indicativa | 8, 9 |
| Schede Visibilità e Verifica, max 8 azioni, niente voce | 9 |
| Etichette "Punto-punto (locale)" / "Distanza minima (Inventor)" | 9 |
| Disabilitazione offline, su parte, durante altra verifica | 9 |
| Rischi da verificare prima del piano | 1 |
| Fixture e runner, gate M7-01…M7-08 | 1, 10, 11 |
| Documenti | 1, 10, 11, 12 |
