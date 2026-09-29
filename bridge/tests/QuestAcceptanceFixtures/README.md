# Quest acceptance fixtures (M1, M2, M3, M5)

Windows only. Needs Inventor 2027 already open and available for automation. Creates the dedicated
documents the in-app Quest acceptance runners work on (plan `docs/superpowers/plans/2026-09-29-quest-acceptance-runners.md`,
component E). Modeled on `bridge/tests/M4LiveProbe/QuestFixture.cs`. It references only the Inventor
interop and Newtonsoft.Json.

```powershell
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest m3
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --inspect-quest m3
dotnet run --project bridge/tests/QuestAcceptanceFixtures -- --restore-quest m3
```

The milestone is `m1`, `m2`, `m3` or `m5`. The manifest is written relative to the current directory:
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
