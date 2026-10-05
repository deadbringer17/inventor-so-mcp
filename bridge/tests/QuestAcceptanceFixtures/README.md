# Quest acceptance fixtures (M1, M2, M3, M5, M6, M7)

Windows only. Needs Inventor 2027 already open and available for automation. Creates the dedicated
documents the in-app Quest acceptance runners work on (plan `docs/superpowers/plans/2026-09-29-quest-acceptance-runners.md`,
component E). Modeled on `bridge/tests/M4LiveProbe/QuestFixture.cs`. It references only the Inventor
interop and Newtonsoft.Json.

```powershell
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest m3
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --inspect-quest m3
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --restore-quest m3
```

The milestone is `m1`, `m2`, `m3`, `m5`, `m6` or `m7`. The manifest is written relative to the current directory:
`artifacts/<m>-verification/quest-fixture.json` (document paths, `previous_document`, `document_id`,
`active_document`, `expected`).

- `--prepare-quest`: builds the fixture in `%TEMP%\xrso-<m>-quest-<guid>`, saves it and leaves it open and
  active. Refuses while a previous manifest's documents are still open. On failure it closes only what it
  created (without saving) and reactivates the previous document.
- `--inspect-quest`: the fixture must be active; prints one JSON line with measured values (mm) and `dirty`.
- `--restore-quest`: the fixture must be active; closes the manifest documents without saving (assembly
  before parts) and reactivates the previous document (error if it was recorded and is no longer open).

| Milestone | Active document | Content |
|---|---|---|
| m1 | `XR_M1_Quest_Acceptance.iam` | two occurrences of `XR_M1_Quest_Block.ipt` (100 x 60 x 20 mm), the second +150 mm in X, first grounded |
| m2 | `XR_M2_Quest_Acceptance.iam` | same, part `XR_M2_Quest_Acceptance_Block.ipt` also kept open |
| m3 | `XR_M3_Quest_Acceptance.ipt` | 40 x 30 x 10 mm block (sketch `Blocco`), unconsumed sketch `Base_M3` with a circle R 5 mm on the top face |
| m5 | `XR_M5_Quest_Acceptance.ipt` | sheet-metal Face 100 x 60 mm, no flat pattern, unconsumed sketch `Taglio_M5` (20 x 10 mm rectangle) |
| m6 | `XR_M6_Quest_Acceptance.iam` | two occurrences, three documents kept open: `XR_M6_Quest_Acceptance_Block.ipt` (40 x 30 x 10 mm centred block, unconsumed sketch `Base_M6` with a circle R 5 mm on the top face, grounded), `XR_M6_Quest_Acceptance_Sheet.ipt` (sheet-metal Face 100 x 60 mm, unconsumed sketch `Taglio_M6`, +60 mm in X, free) |
| m7 | `XR_M7_Quest_Acceptance.iam` | four 20 mm cubes: M7_A/M7_B overlap by 2000 mm³, M7_A/M7_C have a 30 mm gap, M7_D is free; unhealthy constraint M7_Sick and empty cube Description (`DESCRIPTION_MISSING` BOM warning) |
| m9n | `XR_M9N_Quest_Acceptance_Assieme3.iam` | nested assemblies, six documents saved and kept open: `..._Assieme1.iam` (PartA 40 x 30 x 10 mm and PartC 30 x 20 x 10 mm at +60 mm), `..._Assieme2.iam` (PartB 20 x 20 x 10 mm), `..._Assieme3.iam` holds Assieme1 (origin) and Assieme2 (+120 mm), all grounded. Guard prefix `XR_M9N_Quest_Acceptance`, used only by the nested M9 runner (`scripts/run-m9-nested-acceptance.ps1`); inspect also prints dirty flag and volume of every document |

M7 is read-only. `--probe-m7` independently measures the dedicated fixture;
`--probe-active` measures interference and health timings on an active real
assembly without modifying or saving it. Native M7 probes passed on Inventor
2027; see `docs/xr-m7-verification.md` for the separate PC, Quest and physical gates.

### What one m6 fixture serves

The M6 runner (`M6QuestAcceptance`) crosses all four workspaces on one fixture, reaching each document the way a user does:

| Workspace | Document the runner works on | How the runner gets there |
|---|---|---|
| Ispeziona, Assieme | the assembly (needs at least two components: here a block and a sheet) | active after `--prepare-quest m6` |
| Progettazione | the block part (planes, planar top face, straight edges, unconsumed sketch `Base_M6`) | "Apri in Progettazione" from the isolated block (the part must already be open in Inventor, hence the three kept-open documents) |
| Lamiera | the sheet-metal part (straight 100 and 60 mm edges, no flat pattern) | "Apri in Lamiera" from the isolated sheet |

Every document name starts with `XR_M6_Quest_Acceptance`, so the runner's fixture guard holds on each of them. The runner is
not read-only: it makes one real extrusion `Apply` on the block and reverts it with XR Undo, then leaves the assembly active
again. Nothing is saved, so `--restore-quest m6` (assembly active, closes the three documents without saving) always brings
the fixture back. Not covered by this fixture: sub-assemblies and assemblies with more than two components.

Naming exception: every document display name starts with `XR_<M>_Quest_Acceptance` except the M1 block part
`XR_M1_Quest_Block.ipt`, which is closed right after saving and only placed by path.

Not run in CI: this needs a live Inventor. Results are not acceptance evidence until recorded in the milestone
verification file.

## To confirm on a live Inventor

Written without Inventor; these calls in `Fixtures.cs` are unverified:

- `PrepareM5`: `FaceFeatures.CreateFaceFeatureDefinition(profile)` with the default rule thickness; thickness read late-bound (`((dynamic)def).Thickness.Value`).
- `TopPlanarFace`: picks the +Z planar face with the highest Z; for the sheet (m5) check that `Taglio_M5` lies on the intended side.
- `SketchOnTopFace`: `Sketches.Add(face)` and `ModelToSketchSpace` on a part and on a sheet-metal definition.
- `PrepareAssembly` (m2): `Documents.Open(partPath, true)` on a part already referenced by the assembly.
- `Inspect`: `Bends.Count` and `HasFlatPattern` on the sheet-metal definition.
- `PrepareM6`: a sheet-metal part (`BuildSheetMetalPart`, shared with m5) placed in an assembly with `Occurrences.Add(path, matrix)` while it is still open, and `Documents.Add` parts left open after `SaveAs` (as m2 does for its part). The runner finds the two components by the words `Block` and `Sheet` in the occurrence names, so check that Inventor names them `XR_M6_Quest_Acceptance_Block:1` and `XR_M6_Quest_Acceptance_Sheet:1`.
- `--restore-quest m6`: the assembly must be active; the runner reactivates it at its end, but after an interrupted run activate the assembly by hand first.
