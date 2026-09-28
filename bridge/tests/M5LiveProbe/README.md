# M5 Flat Pattern probe (technical decisions 1 and 4)

Windows only. Needs Inventor 2027 already open and available for automation. Build the experimental
add-in into its own output directory, then run the probe:

```powershell
dotnet build bridge/src/plugin-so27/Inventor.So.AddIn.csproj -p:SoExperimental=true -p:OutputPath=bin/M5/net48/ --no-restore
dotnet run --project bridge/tests/M5LiveProbe -- --artifacts-path artifacts/m5-live-probe
```

Options: `--repetitions N` (timing runs per measurement, default 5), `--keep` (leave the temporary
fixtures open and unsaved instead of closing them).

The probe preserves the active document and reactivates it at the end. It builds its own temporary
sheet-metal parts (a 100 x 60 mm base face from a rectangle sketch plus one flange on a real edge; a
two-face part for the multi-body case; an ordinary part), never touches the M4 fixture, and closes
its documents without saving. It loads the handler assembly into its own STA process, like the M3/M4
probes; the installed Inventor add-in is not replaced. Do not run it while you edit in Inventor.

Output in `--artifacts-path`: `m5-live-probe-report.json` (checks, measurements, findings) and
`m5-live-probe-summary.txt`. Every check starts as `NOT_RUN` and turns `PASS` or `FAIL` only when the
code that exercises it says so. Exit code 0 means no failure and nothing left unexercised, 1 a failure,
2 at least one `NOT_RUN`. A `NOT_RUN` is not acceptance evidence.

## Decision 1: real flat pattern geometry as a separate mesh

Handler under test: `get_flat_pattern_mesh` (experimental tier, `shared/Handlers/Experimental/FlatPatternMeshHandler.cs`).

| Check | Proves | Does not prove |
|---|---|---|
| `D1A_REFUSED_WITHOUT_FLAT_PATTERN` | With no flat pattern the call fails with `INVALID_ARGUMENT` / `details.reason=FLAT_PATTERN_MISSING`, `HasFlatPattern` stays false and the folded model is unchanged | Behaviour with a flat pattern that is out of date |
| `D1B_MESH_PRESENT`, `D1B_GEOMETRY_SOURCE` | After `create_flat_pattern` (atomic batch) the read returns triangles; which of `FlatPattern.SurfaceBodies` / `FlatPattern.Body` worked on 2027 (all attempts listed) | That both members exist; only the one reported is confirmed |
| `D1B_EDIT_STATE` | Edit state read by the handler (`ActivatedObject`, `Application.ActiveEditObject`) and by the probe independently is not Flat Pattern Edit after the read; `forced_by_read` / `left_edit_mode_restored` are reported | The fail-safe `ExitEdit` path if the read never forced edit mode (it is then not exercised); NOT_RUN when neither member is readable |
| `D1B_FOLDED_MODEL_UNCHANGED` | Body count, volume, feature count, range box, revision and visual revision of the folded part are identical before and after the read | That the flat pattern itself is untouched |
| `D1B_BBOX_VS_INVENTOR`, `D1B_THICKNESS` | Mesh extents (sorted) match Inventor's `Length`/`Width` and the sheet thickness within 0.05 mm; deltas are in the report | Accuracy on curved outlines or holes (fixture is a rectangle with one flange) |
| `D1B_ORIENTATION_REPORTED` | Which plane the flat pattern lies in, which axis is thin, on which side of 0 the thickness lies, where the mesh sits relative to the origin | A stable orientation across parts: it is measured on one fixture. Always PASS when determinable; read the detail |
| `D1B_UNITS_AND_GLB` | Payload is centimetres (`units`) and the GLB the server would publish has the same extents in metres | Anything about the HTTP host or the asset store |
| `D1C_IDENTITY_STABLE`, `D1C_MESH_HASH_STABLE` | Two reads without an edit give identical `flat_pattern_identity` (incl. `hash`, `content_hash`, `vertex_set_hash`), the same ordered `mesh_hash` and byte-identical GLB | Stability across Inventor sessions or after save/reopen |
| `D1D_IDENTITY_CHANGES_AFTER_EDIT` | A committed flange height edit (23.7 to 30 mm; `set_parameter` on the parameter found by value, else a native definition edit) changes the identity and Inventor's length/width | That every kind of edit that changes the flat pattern changes the identity |
| `D1E_MULTI_BODY_REFUSED` | A two-body sheet-metal part is refused with `MULTI_BODY_PART` and gets no flat pattern | NOT_RUN if Inventor will not build a second disjoint face; the refusal is then untested |
| `D1E_NON_SHEET_METAL_REFUSED` | An ordinary part is refused with `WRONG_DOCUMENT_TYPE` | Assemblies (covered by the server tests with the FakeAddIn) |

`flat_pattern_identity` is a proposal: document id, revision, length, width, bend count, alignment,
tolerance, triangle count and the hash of the unique vertices on a 1 micrometre grid, plus
`content_hash` (everything except document id and revision) and `hash` (with them). The probe reports
whether it is stable and whether it moves; whether it is the right cache key is a decision for the
result.

## Decision 4: interaction budget

Timings are wall-clock of the handler call (Stopwatch, direct call into the add-in code on the STA
thread). They exclude the named pipe, the MCP server, HTTP and the Quest; GLB build time and byte sizes
are measured separately on the same payload with the server's own `GlbBuilder`.

| Check | Measures |
|---|---|
| `D4_FLANGE_PREVIEW_TIMING` | Atomic batch preview of the flange (transaction, rebuild and feature-health validation, preview mesh capture, rollback) on the base face; also verifies the model is unchanged after every preview. First sample is the cold one |
| `D4_DISPLAY_MESH_TIMING` | `get_display_mesh` of the folded part at 0.1 and 0.5 mm, face ids on |
| `D4_CREATE_FLAT_PATTERN_TIMING` | `create_flat_pattern` as a preview (rolled back) repeated N times, plus one committed creation (`create_flat_pattern_commit`). Also records whether a preview rolls the flat pattern back cleanly (`findings.create_flat_pattern_preview_rolled_back_cleanly`) |
| `D4_FLAT_MESH_TIMING` | `get_flat_pattern_mesh` at 0.1 and 0.5 mm |
| `D4_SIZES` | Triangles, JSON payload bytes, GLB bytes and GLB build time for the four mesh reads (flat and folded, 0.1 and 0.5 mm); the flange preview mesh payload is in `measurements.flange_preview_mesh_payload` |

The fixture is small (one flange, no holes), so absolute times and sizes are a lower bound: a part with
punches or many bends is slower and larger.

## API assumptions the probe confirms or refutes

The experimental code has not been compiled against the 2027 interop, so these are open until the run:
`FlatPattern.SurfaceBodies` and/or `FlatPattern.Body` exist and give bodies with `Faces`;
`Face.CalculateFacets` late-bound works on flat pattern faces; `Document.ActivatedObject` and
`Application.ActiveEditObject` are readable and name the flat pattern while it is being edited;
`FlatPattern.ExitEdit` works from outside the flat pattern command; a `create_flat_pattern` preview is
rolled back; `FlangeFeatures.Item(1).Definition.SetDistanceHeightExtent` (only used when the height is
not a parameter). Read `findings` in the JSON report for what each attempt returned.
