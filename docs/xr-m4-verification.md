# M4 — Implementation evidence

Current Italian report (28 September): [M4 implementation and collaudo](xr-m4-collaudo.md).
The entries below are chronological work notes and may describe earlier,
superseded test states.

Date: 27 September 2026. Status: **in progress; not complete or accepted**.
Contract: [M4 specification](superpowers/specs/2026-09-27-inventor-xr-so-m4-design.md).
Sequence: [implementation plan](superpowers/plans/2026-09-27-inventor-xr-so-m4.md).

## Implemented foundation

- Experimental `get_assembly_context_xr`/MCP tool exposes revision-bound direct
  occurrences, native normalized DOF vectors and centers, face/edge proxy IDs,
  positions, directions and bounded edge strokes. Unsupported and incomplete
  information is explicit.
- `assembly_move`, `assembly_constraint` and `assembly_joint` are catalogued
  experimental assembly batch operations. A host-owned context marker verifies
  the exact atomic transaction; standalone calls cannot mutate the document.
- Movement uses the Inventor solver and rejects a final pose differing by more
  than 0.01 mm or 0.01 degrees. Existing constraints are not bypassed. Existing
  standalone safe handlers retain their original behavior.
- Assembly preview captures occurrence geometry in assembly coordinates before
  rollback, with a whole-assembly triangle budget and no transient face IDs.
  The existing server publishes it through the owned preview asset path.
- Core DTOs and typed builders cover occurrences, references, movement,
  compatible constraints and joints. The client selects assembly health and
  interference validators for assembly operations while retaining Design checks.
- Preview rendering accepts an assembly preview only for the loaded document.
  XR history now admits assemblies on the native handler and tool contract.

## Verified evidence

- Inventor 2027 experimental add-in compiled with **0 errors, 0 warnings** into
  `bridge/src/plugin-so27/bin/M4/net48/`, without replacing the installed add-in.
- Backend .NET suite: **924 passed**, zero failed. Result:
  `bridge/tests/Bimwright.Ipt.Tests/TestResults/m4-backend.trx`.
- Core .NET suite: **167 passed**, zero failed, including 12 M4 cases. Result:
  `Inventor XR SO/Tests~/XrSo.Core.Tests/TestResults/m4-core.trx`.
- Native Inventor probe passed free DOF 3+3, slider 1+0, grounded 0+0, native
  vector reads, permitted slider movement, forbidden transverse movement and
  transaction rollback. The second successful run also passed the actual context
  handler (5 cylinder proxy references), direct-write guard and atomic preview
  mesh capture. A tentative cylinder center at 6 cm was captured while the
  restored live occurrence remained at 5 cm with its original revision.
- Native fixture artifacts from that run:
  `%TEMP%/xrso-m4-probe-fbe5150f2bd64c5ab8a5401cbdb46209`.

Inventor was subsequently reopened with user authorization. The extended probe
passed all six joints (Rigid, Rotational, Slide, Cylindrical, Planar, Ball) with
rebuild/health/interference, preview mesh capture and rollback. All six constraints
(Mate, Flush, Mate Axis, Insert, Angle, Tangent) passed feasibility, preview mesh
and rollback. The constraint fixture does not establish interference acceptance
for every type. Artifacts: `%TEMP%/xrso-m4-probe-53dde38e515446d2b7d79dfb08d6c1cd`.

Native testing exposed and fixed two API distinctions: Cylindrical's axial offset
uses `LinearPosition` (the `Gap` setter fails); Ball has coincident origins and
rejects nonzero gap. The client builder also rejects a nonzero Ball gap.

## Initial Unity integration

- Added Assembly to the wrist and an AssemblyWorkspace with components, face/edge
  lists, contextual constraints, six joint choices, numerical input, DOF visuals,
  intentional controller manipulation and Preview/Apply/Cancel.
- The second selected reference starts preview for a previously chosen command.
  Native reference axes/planes and edge strokes are displayed alongside the DOF.
- Design and Assembly entry points refuse switching to the other authoring mode
  while a mutation or unresolved outcome requires CAD review.
- Unity compiled the initial integration and the existing **68 EditMode tests
  passed**. Six dedicated M4 UI tests have since been added and are being run;
  no pass is claimed for those until their result is inspected.

## Extended verification — 27 September, 23:43 local

- Native probe now verifies all six joint and constraint previews, rejection of
  same-occurrence relationships, persistent rotational joint and residual DOF
  0+1, and scoped assembly Undo/Redo. It also verifies document activation refuses
  an open user transaction and preserves native Redo. An empty transaction probe
  was rejected because Inventor clears Redo even on abort; the activation guard
  reads the Inventor 2027 unidentified-transaction sentinel instead.
- Native nested probe passed: two repeated subassemblies, one rotated 90 degrees;
  all four preview body centers in global coordinates; rollback/revision
  preservation; child mutation refused from parent; explicit activation exposes
  the subassembly's direct children. Its external COM event sink pumps activation
  notifications before capturing the next revision.
- Installed experimental package: `artifacts/inventor-so-mcp-20260927-233945`.
  Active installed add-in path: `%LOCALAPPDATA%/InventorSO/packages/20260927-234017-5564f0a6/addin/`.
  Inventor process 60248; authenticated pinned HTTPS host targets that exact
  process on port 8443. Only empty Inventor instances were closed for deployment.
- Real HTTPS probe **passed** context/reference reads, definition GLB, assembly
  preview GLB/rollback/revision, explicit `min_clearance:1mm`, movement commit,
  native pose checks, scoped Undo/Redo, stale-plan rejection after a native edit,
  rotational joint commit and residual DOF 0+1. Fixture:
  `%TEMP%/xrso-m4-http-cd59317471e04625a5a07d4ba62305d6`.
- Backend regression **924/924**, core regression **167/167** passed again.
- Unity's earlier 75-test pass is confirmed. The expanded 86-test run found
  three test-fixture issues (wrong renderer count and two rays aimed parallel to
  the handle); corrections and additional rotation tests are in progress. Do not
  interpret the expanded suite as passing yet.
- Quest 3 `2G0YC1ZFB407P1` is attached through ADB. Dedicated saved fixture
  `XR_M4_Quest_Acceptance.iam` is open; paths are recorded in
  `artifacts/m4-verification/quest-fixture.json`. A fixture-only opt-in Android
  acceptance runner is being prepared. Synthetic input tests do not establish
  physical controller tracking or ergonomics.

## Outstanding required work

- Complete and validate Unity interactions, especially spatial rotations,
  reference highlighting, subassembly context, and user-facing errors.
- Validate the mutation/uncertain-outcome guard across both authoring modes.
- Complete expanded Unity tests and Android build/deployment.
- Execute device acceptance runner and inspect device logs/rendering.
- Close remaining A01–A15 evidence gaps; keep physical interaction acceptance
  separate from synthetic inputs and programmatic device tests.

No delivery or milestone completion claim is supported by these foundation
checks alone. Existing local M1–M3 changes have been preserved.
