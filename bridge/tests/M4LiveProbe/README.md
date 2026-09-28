# M4 Inventor and HTTPS probes

Build the experimental add-in in an isolated output directory, then run with
Inventor 2027 already open and available for automation:

```powershell
dotnet build bridge/src/plugin-so27/Inventor.So.AddIn.csproj -p:SoExperimental=true -p:OutputPath=bin/M4/net48/ --no-restore
dotnet run --project bridge/tests/M4LiveProbe -- --artifacts-path artifacts/m4-live-probe
```

The probe creates its own cylinder part and temporary assembly, preserving the
previously active document. It closes only its own documents without saving
the assembly. A unique temporary directory retains the saved fixture part.
It loads the new handler assembly into its own STA process; the installed
Inventor add-in is not replaced.

Checks: free/slider/grounded DOF and native axis vectors, allowed and forbidden
slider movement, transaction rollback, revision-bound assembly context and
proxy references, refusal of direct assembly writes outside atomic batch,
capture of the tentative assembly mesh before rollback, absence of transient
face IDs, and restored pose/revision.

The extended probe additionally tests the six joint types with interference
validation and six constraint types with rebuild/health validation, checking
preview mesh capture and rollback for each. The coaxial constraint fixture is
intentionally allowed to overlap in some cases; those cases prove solver
feasibility, not the complete XR interference policy. A failed or unexecuted
case is not acceptance evidence.

It also verifies rejection of same-occurrence pairs for each relationship type,
persistent rotational joint DOF, 30-degree movement about the residual axis,
forbidden translation rollback, scoped Undo/Redo, suppressed context, and
activation that preserves native Redo and refuses an open user transaction.

```powershell
# Repeated/rotated nested instances, global preview coordinates and explicit activation
dotnet run --project bridge/tests/M4LiveProbe -- --nested
# Installed add-in + real HTTPS host, production client and pinned TLS
dotnet run --project bridge/tests/M4LiveProbe -- --http
# Creates and leaves open ONLY a new named fixture for opt-in device acceptance
dotnet run --project bridge/tests/M4LiveProbe -- --prepare-quest

# Preview-only diagnosis of the A/B Planar origins on the active dedicated Quest fixture.
# With the corrected add-in, Faccia 3/Faccia 2 at 30 mm passes; zero-gap
# combinations are rejected by the interference validator.
dotnet run --project bridge/tests/M4LiveProbe -- --quest-planar

# Controlled integrated test: commit the same Planar joint, check residual DOF,
# then XR Undo; leaves the fixture at its original pose.
dotnet run --project bridge/tests/M4LiveProbe -- --quest-planar-commit

# After a batch created by --prepare-quest, close only that fixture without saving
# and reactivate the previously open Inventor document.
dotnet run --project bridge/tests/M4LiveProbe -- --restore-quest
```

The HTTPS probe expects the existing paired test host on localhost:8443 and
reads its local certificate/token registry without printing credentials. It
checks context and assets, min-clearance preview, 10 mm commit/Undo/Redo,
stale-plan rejection after a desktop edit, and rotational joint commit/DOF.
Its fixture documents are closed and the previous document restored.

The Android fixture harness is compiled only by
`InventorXrSo.Editor.XrSoBuild.BuildAcceptanceApkBatch`, with
`XR_SO_ACCEPTANCE`. It starts only for the Android intent extra
`xr_m4_acceptance=true` and only edits an active document whose name starts with
`XR_M4_Quest_Acceptance`. Ordinary `BuildApkBatch` excludes the harness. Device
logs and screenshots go to the app's persistent files directory. This proves
programmatic execution on the device; it does not claim physical input tracking.
The extended QA runner also verifies A/B selection and the compatible constraint
list. The first batch recorded the Planar failure from the previous add-in;
the integrated probe above verifies the corrected add-in. The QA runner then
runs the move/Cancel/Apply/Undo/Redo suite.
Horizon OS may show a controller-required launch dialog when the headset is off
the user's face; the automated runner starts only after that system gate is
cleared. ADB installation alone does not count as a device test pass.

The default native probe does not test production dispatcher threading or Quest
behavior; use the separate HTTPS and device paths. Do not run while the user is editing
Inventor documents or using the connected headset.
