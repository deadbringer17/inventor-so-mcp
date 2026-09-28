# Inventor XR SO — M3 implementation evidence

Date: 27 September 2026. Status: **in progress, not delivered or accepted**.
Scope and remaining work: [M3 plan](superpowers/plans/2026-09-27-inventor-xr-so-m3.md).
Current requirement-by-requirement status: [M3 acceptance audit](xr-m3-acceptance.md).
The sections below are chronological evidence; earlier limitations may be superseded
by later entries.

## Implemented foundation

- `inventor_plan_change(include_preview_mesh: true)` captures the tentative part
  inside the owned atomic transaction after rebuild/feature validation, then
  aborts and restores the revision. The server publishes an owner-scoped GLB
  asset only after checking rollback/document/revision, strips transient face
  references and does not contaminate the live definition mesh cache.
- The optional mesh capability rejects non-part, commit and non-experimental
  requests. Capture/timeout failures roll back; an old add-in that omits the
  requested mesh does not produce an applicable Design plan.
- Core `DesignSession` clones drafts, requires a rendered preview, checks expiry
  at Apply, invalidates late preview responses and prevents double commit.
  Unknown commit outcomes require a CAD refresh and explicit review, never an
  automatic retry. Validation failure retains the last ghost and editable draft.
- `DesignOperations` creates validated requests for named sketches, lines,
  rectangles, circles, extrusion, drilled holes, fillet, chamfer and numeric
  parameters. `create_sketch` now optionally accepts a unique name so a batch
  can target the same named sketch on preview and commit.
- Unity `DesignPreviewView` renders a solid part result with a translucent
  original using the existing stereo shaders. Preview geometry has no colliders
  or selectable CAD references. Clearing/rebuilding releases owned meshes and
  restores original materials. It is now wired to AppController and the Design
  wrist menu; end-to-end UI and device acceptance are still pending.
- `inventor_get_design_context_xr` is registered as an experimental,
  revision-bound read. It exposes work-plane/sketch frames, planar face IDs,
  bounded model-edge strokes and parameters. Compiled and registration checked;
  client DTOs and Design UI consume these references. No live COM invocation yet.

## Design integration continuation

- The wrist menu opens the Design workspace with sketch ray input, endpoint /
  midpoint / centre / horizontal / vertical snap candidates, numeric editing,
  feature panels and explicit Preview / Apply / Cancel. Sketch-only previews
  carry Inventor sketch frames and line/circle geometry through rollback.
- Grip + Trigger edits the dimension handle; plain grip moves the visual model.
  Existing-sketch extrusion now uses its actual offset frame and selected
  direction. Hole depth follows the inward planar-face normal; ray-selected
  hole points are projected onto the exact CAD plane.
- Core/client tests now **138/138 passed**, including transport context reads,
  sketch drafts and projection onto an offset oblique plane. Unity EditMode
  now **46/46 passed**, including the existing-sketch frame, negative extrusion
  direction and inward hole-depth manipulator regression. Results:
  `%TEMP%/xrso-m3-design-editmode.xml`; log with the same basename. This also
  compiles the current AppController/DesignWorkspace integration.
- Remaining work includes scoped Undo/Redo, complete UI lifecycle coverage,
  persistent sketch dimension/constraint behavior, Android build and live
  Inventor/Quest acceptance. The controls above are implemented, not accepted
  on device.

## Verified in this stage

- Backend .NET tests: **908/908 passed** after context-tool registration.
- Core/client .NET tests: **130/130 passed**, including 21 new Design cases and
  real HTTPS/MCP/pipe/GLB transport against FakeAddIn. These do not prove CAD
  geometry construction in Inventor.
- Unity EditMode: **45/45 passed**, including four new preview renderer tests.
  Results: `%TEMP%/xrso-m3-editmode.xml`; log: `%TEMP%/xrso-m3-editmode.log`.
- Inventor 2027 experimental add-in: compiled against installed interop with
  zero errors/warnings; output isolated under `bridge/src/plugin-so27/bin/M3`.
- `git diff --check`: no whitespace errors. Existing M1/M2 local changes have
  been preserved. Unity generated metadata for the new imported C# files.

The first full backend run inherited `INVENTOR_SO_EXPERIMENTAL=1`, causing the
test asserting the default-disabled configuration to fail. Rerunning with the
documented test-process setting `INVENTOR_SO_EXPERIMENTAL=0` passed. Tests were
not weakened. The existing xUnit2013 warning in ExperimentalSourceTests remains.

Commands from repository root:

```powershell
$env:INVENTOR_SO_EXPERIMENTAL='0'
dotnet test bridge/tests/Bimwright.Ipt.Tests -p:OutputPath=bin/M3/net8.0/ --no-restore
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --artifacts-path "Inventor XR SO/Tests~/artifacts/m3"
dotnet build bridge/src/plugin-so27/Inventor.So.AddIn.csproj -p:SoExperimental=true -p:OutputPath=bin/M3/net48/ --no-restore
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-m3-editmode.xml`"" -Log "$env:TEMP/xrso-m3-editmode.log"
```

## Required continuation

### Undo/Redo policy implementation

`XrUndoHistory` now provides the bounded add-in receipt chain (16 documents,
64 transactions per document). It checks caller ownership, active document,
revision, one-use ticket and the exact next native transaction identity before
invoking its backend. It cannot skip desktop/foreign transactions. New commits
discard the redo branch; an intervening revision starts a new chain. An
exception after native execution or an unexpected resulting stack invalidates
the chain and prevents replay.

Backend tests: **921/921 passed**, including **13 history tests** for multiple
Undo/Redo, intervening edits, different callers, busy commands, eviction and
uncertain native outcomes. Experimental Inventor 2027 add-in compilation:
**zero warnings/errors**. These policy tests use an in-memory native-stack
adapter. Subsequent integration adds the COM adapter, successful-commit receipt
capture and `inventor_history_xr` server tool. The server supplies caller identity;
the public method exposes no owner override. The integrated handler has not yet
been deployed/live-tested.

Client integration now adds `DesignHistory` receipts and transport methods.
The Design tools page reached from the wrist exposes available Undo/Redo only
with an empty draft. History mutations reuse the Apply state machine, block
concurrent edits, require an authoritative refresh and never retry an uncertain
outcome without explicit CAD inspection. Context/document/connection changes
and preview start invalidate displayed history. The UI explains that creating a
new preview clears native Redo. Latest core/client tests: **141/141 passed**,
including history draft/revision gates, lost-response handling and context
switch during a pending history mutation. Unity EditMode: **46/46 passed** for
the integration (before the subsequent notice-text and history-read reset edits).
The new HTTPS history test exercises the actual server, token-based owner
identity, pipe and client with FakeAddIn: foreign client refusal, Undo/Redo,
consumed/stale receipts and a desktop revision change all pass. This remains
transport evidence, not integrated COM or UI history acceptance.

### Persistent sketch dimensions

`Quota ultimo elemento` now marks the element for driving dimensions in Inventor.
The draw-line/rectangle/circle handlers accept optional `add_dimensions` and
create respectively length, width+height or radius constraints in the same owned
batch as the geometry. Defaults remain false for existing callers. The handlers
return the resulting parameter names. No transient entity IDs are needed across
preview and commit. Native dimension labels and existing-dimension selection in
XR still need work; geometric behavior on the updated live add-in is unverified.

After these changes: **145/145 core/client tests**, **922/922 backend tests**,
experimental Inventor add-in builds with **zero errors/warnings**. Core tests
cover the dimension flag for all three shapes while preserving the batch bound.
Unity EditMode after persistent dimension integration: **46/46 passed**;
`%TEMP%/xrso-m3-design-editmode.xml` and matching log.

### Unity workspace integration verification

Latest EditMode suite: **50/50 passed**. Four new tests exercise the actual
Design panel buttons: preview without writes, explicit Apply, cancellation and
original-material restoration, Undo/Redo and offline action removal, a late
preview after closing Design, and panel bounds with history actions present.
They found a real initialization failure: `Text.canvas` is null while its canvas
is inactive. Design now retains the canvas reference explicitly through reset,
cursor updates and disposal. The first run also found a missing asset-map entry
in the test fixture, corrected before the runtime failure was investigated.
These tests simulate the backend and do not establish Quest acceptance.

The first M3 Android build has been started using `XrSoBuild.BuildApkBatch`;
log `%TEMP%/xrso-m3-build.log`. Its completion remains to be verified.

**Build outcome subsequently verified:** Unity process exited 0; Android APK
`Inventor XR SO/Builds/InventorXrSo.apk`, 50,914,878 bytes, SHA-256
`525F0356E2671510F2383ECC456CF474D02AC290CCBBF866DBE0535E88589DDC`.
This first M3 build predates the following ray dimension-placement UI changes;
it has not been installed on Quest. A final build remains necessary.

Native dimension probe on a new unsaved Inventor part created four driving
dimensions (line length, circle radius, rectangle width/height). Editing the
length to 30 mm and radius to 12 mm changed the actual geometry accordingly.
Aborting the transaction returned the part to zero sketches. The temporary
document was closed without saving and the original reactivated. Evidence:
`artifacts/m3-verification/native-dimensions-probe.json`, script in the same
directory. This tests native APIs directly, not the deployed M3 handlers.

The draft sketch UI now supports `Quota geometria`: ray-select any draft line,
rectangle edge or circle, indicate a sketch-plane text position, then enter the
dimension numerically. The selected geometry is highlighted with a thicker line.
Core analytic picking and placement serialization tests pass (**146/146** client
tests). Existing committed sketch editing and dimension-label rendering still
require continuation; this workflow currently targets the editable local draft.
Unity EditMode after ray dimension placement: **50/50 passed**. APK inspection
also confirms ARM64 `libil2cpp.so`, `libunity.so` and `libOVRPlugin.so` entries.

### Native preview dimension labels

The client now parses native dimension names, expressions, reference/driving
state and sketch-space text points. Preview labels are anchored through the exact
Inventor sketch frame, face the headset and maintain a constant display size at
Table/1:1 scale. They do not intercept controller rays and are destroyed with the
preview. **147/147 client tests** and **51/51 Unity tests passed**, including an
offset rotated sketch frame, scale changes, expression preservation and label
cleanup. The APK above predates these changes. The sketch query also now returns
constraint types per entity, preparing contextual constraint display; the add-in
compiles with zero warnings/errors, but that display is not implemented yet.

### Constraint readout integration

The sketch panel now exposes `Vincoli della geometria` (ray selection against
the current native snapshot) and `Mostra tutti i vincoli` (grouped, paginated
native geometric-constraint types). Common types have Italian labels; unknown
native types remain explicit rather than being guessed. Constraints stay hidden
until requested. Changing the draft invalidates access to the previous snapshot.
The readout does not create or remove constraints.

Latest verification: **148/148 client tests**, **53/53 Unity EditMode tests**.
New tests cover per-entity constraint association, empty-space selection,
native grouped readout, invalidation after draft changes and the sketch panel's
button bounds. This closes the initial readout gap for the local draft preview;
native constraint editing and device readability still require live acceptance.

Lifecycle audit follow-up: preserve the unknown/pending mutation guard across
`DesignWorkspace.Bind` (disconnect/re-pair), where replacing DesignSession can
currently discard it. Test late completion against a replaced backend before
live deployment. Existing same-session tests do not prove this transition.

**Rebind guard implemented and verified:** `DesignWorkspace` now retains a CAD
review requirement across null/replacement backend bindings when a mutation was
pending or still required refresh. It counts pending mutation tasks, withholds
the review action until they settle, and captures the owning session so late
completion cannot refresh a replacement backend. Refresh responses also check
backend/session/generation identity before changing UI state. The new session
requires explicit CAD inspection; reconnect alone cannot clear the guard.

Latest tests: **149/149 core/client**, **55/55 Unity EditMode**. The two new
Unity coroutine tests rebind during Apply and Undo, complete the former with a
late success and the latter with a lost-response exception, and verify no call
is repeated and no replacement-backend refresh occurs before explicit review.
This verifies the in-process rebind lifecycle, not persistence across app restart.

### Live handler/COM integration

`bridge/tests/M3LiveProbe` loads the current experimental add-in assembly and its
actual event tracker into a separate STA automation process. It uses the same
core operation builders as the XR client and calls the real atomic, context and
history handlers against a newly created temporary part in Inventor 2027.

Verified in the successful run (`artifacts/m3-verification/handler-live-probe.log`):

- Rectangle sketch with two driving dimensions and 10 mm extrusion: tentative
  solid mesh captured before rollback; zero residual sketches/bodies afterward;
  original revision restored.
- Commit created one solid and persistent dimensions. The context query returned
  12 native edges. Scoped Undo removed the solid; Redo restored it.
- Through drilled hole, one-edge fillet and one-edge chamfer each passed preview,
  commit and scoped Undo. Preview preserved original volume; commit changed it;
  Undo restored it.

The run exposed a client bug: `new JArray(new JArray(x,y,z))` selected the copy
constructor and flattened hole locations. The builder now emits a nested point
array with a collection initializer. Its regression test now asserts the actual
array structure rather than repeating the same faulty constructor. **149/149
client tests pass** after the fix.

The probe closes its own unsaved part and reactivates the prior document. No
installed add-in replacement or original-document geometry edit was performed.
This is actual handler/COM evidence, but does not cover the deployed dispatcher,
HTTP-to-live-add-in path or Quest interaction. The existing APK predates the hole
fix and must not be used as the final M3 build.

While that build runs, the backend now accepts sketch-space `text_x`/`text_y`
in millimetres for line/rectangle/circle driving dimensions, and sketch snapshots
include native dimension `text_mm`. Both coordinates must be present and finite.
This prepares explicit dimension placement; the ray-selection/placement UI is
still pending. Experimental add-in compilation passes with zero warnings/errors.
API reference: [DimensionConstraint.TextPoint](https://help.autodesk.com/cloudhelp/2025/ENU/Inventor-API/files/DimensionConstraint_TextPoint.htm).

Latest backend suite after tool registration: **922/922 passed**. Add-in builds
with zero warnings/errors. History only records successfully committed part
plans carrying an XR preview asset. Native transactions explicitly disable
merge-with-previous. The adapter checks the application-wide native stack's top
document and identity. Busy detection requires the idle transaction ID observed
after an owned root transaction ended, plus Inventor's default selection command;
aborting a rejected nested transaction never authorizes its parent as idle.

Live native API probe on 27 September used a new unsaved part, created three
temporary user parameters in distinct transactions, undid twice and redid once.
Both enumerators return the next executable transaction at `Count`; transaction
IDs remain unchanged across Undo/Redo. Crucially, an aborted preview **clears the
native redo stack**. The adapter checks actual availability and must not promise
Redo after preview. The temporary part was closed without saving and the original
Assieme3.iam reactivated. Evidence: `artifacts/m3-verification/native-history-probe.jsonl`,
script `probe-history.ps1` in that directory. No original-document geometry was
edited. This probe verifies native behavior, not the integrated XR handler.

Native integration must verify transaction enumeration order, transaction IDs
after Undo/Redo, idle versus active unidentified transactions, and effects of
an aborted preview on the redo stack on real Inventor test documents. The API
documents [committed transactions](https://help.autodesk.com/cloudhelp/2022/ENU/Inventor-API/files/TransactionManager_CommittedTransactions.htm)
as undoable in reverse sequence, and exposes each
[transaction's identity and merge flag](https://help.autodesk.com/cloudhelp/2025/ENU/Inventor-API/files/Transaction.htm).
Do not substitute a global Undo command for the guarded adapter.

1. Connect context reads to client DTOs and the session; add model-space edge
   picking and exact sketch frames. For sketches created on a face, obtain the
   actual Inventor sketch frame in the preview transaction rather than guessing
   axes from the face normal. Preview must also carry sketch-only geometry.
2. Implement sketch draft/ray/snap workflow and numeric input, the spatial
   dimensional manipulator, all feature panels and error/Apply/Cancel UX.
3. Bind DesignSession and DesignPreviewView through AppController and the wrist
   menu, covering scene reload, Home, tracking loss, reconnect and pending commit.
4. Implement document/revision-bound XR Undo/Redo without undoing unrelated
   desktop edits. This is not supplied by the existing plan protocol.
5. Validate complete paths in Unity, build the Android APK, then exercise real
   COM preview/rollback/commit on test documents and perform Quest acceptance.

No M3 APK has been built or installed, and the running M2 host/add-in was not
replaced in this stage. No live user CAD document has been edited.

Technical references used for context geometry:
[WorkPlane.GetPosition](https://help.autodesk.com/cloudhelp/2024/ENU/Inventor-API/files/WorkPlane_GetPosition.htm)
defines the work-plane frame inherited by a new sketch;
[CurveEvaluator.GetStrokes](https://help.autodesk.com/cloudhelp/2022/ENU/Inventor-API/files/CurveEvaluator_GetStrokes.htm)
returns curve strokes in model-space centimetres, converted here to millimetres.

## Deployed candidate after authorized Inventor restart — 2026-09-27

The user saved the document and explicitly authorized restarting Inventor. Package
`artifacts/inventor-so-mcp-20260927-180934` was installed; Inventor was closed normally
and restarted from `Z:/Installati/Inventor 2027/Bin/Inventor.exe` (PID 53560).
The recorded open documents were restored, with `XR M1 Assembly 20260927 0830.iam`
active. COM reports `Inventor SO MCP (2027) Activated=True`.

The HTTPS host was replaced with this candidate on port 8443, targeting the new
Inventor process and retaining the certificate and token registry. Authenticated
MCP initialization and `inventor_get_capabilities` succeeded: target reachable,
experimental add-in build and runtime enabled. Evidence: `deployed-capabilities.txt`
in `artifacts/m3-verification`. This is connection verification, not full deployed
preview/commit/history acceptance.

The updated Android build completed with exit code 0. APK SHA256:
`4F519EE271ED7981676EF450094C02CFEFBCF43253B60957CB99881D22DE81B8`.
`adb install -r` on the connected Quest returned `Success`. The full deployed
editing flow and headset interaction acceptance remain outstanding. Historical
statements below/above about no APK or unchanged M2 deployment describe earlier
stages and are superseded by this entry.

## Deployed HTTP authoring acceptance — 2026-09-27

`dotnet run --project bridge/tests/M3LiveProbe --artifacts-path artifacts/m3-live-probe -- --http`
completed successfully against the installed candidate and real Inventor. It uses
the production `InventorBackend`, pinned TLS and authenticated MCP, downloads and
parses the owned GLB, and checks native geometry through COM. Passed: clean preview
rollback/revision preservation; extrusion with two persistent dimensions;
context read; commit/Undo/Redo; through and blind holes; fillet; chamfer; parameter
editing; history disabled after an intervening native desktop transaction.
Evidence: `artifacts/m3-verification/http-live-probe.log`. The temporary document
was closed and the previous active document restored.

Quest APK update was independently observed through Android package metadata
(lastUpdateTime 2026-09-27 18:16:26). User agreed to controller acceptance. A separate
unsaved `XR M3 Quest - prova` part with a 40 x 30 x 10 mm base was opened for that
purpose; app launched and device log reports the new document Online. Do not close
this part while the user is testing. Headset interaction acceptance is pending.

Selected-planar-face sketch entry correction: Unity EditMode passed 57/57 with zero failures (results: %TEMP%/xrso-m3-design-editmode.xml). Covers direct entry for a current face and fallback to plane selection for a stale reference. This change is source/test verified but is not yet in the APK being tested by the user.

Local validation correction: DesignSession.RejectDraft preserves the previous ghost while removing executable operations and invalidating Apply/in-flight preview responses. Unity uses this path on operation-builder failure. Core tests passed 151/151; Unity EditMode passed 58/58, including invalid dimension, retained ghost, zero remote calls until corrected, and a fresh valid preview before Apply. This source change and selected-face entry are not yet installed on the Quest.

## Candidate 2 and edge selection refinement

Candidate 2 Android build completed exit 0. SHA256:
`5053EDB2382C2D09D8EFCFD3F6FD5C556836637E2E16DE45D7D02E0B9A3B50C3`.
Archived as `artifacts/m3-verification/InventorXrSo-candidate2.apk`. Includes direct
selected-face sketch entry and local validation ghost retention. Not installed
while the user is testing the earlier APK.

Subsequent source refinement: Fillet/Chamfer edge picking rejects candidates hidden
by a nearer CAD surface, casting toward the closest edge point rather than merely
the controller's centre ray. Non-CAD colliders are ignored; dimensional handles
retain the unrestricted picking path. Visibility raycasts are restricted to
candidates within the selection tolerance that can improve the current match.
The first Unity regression run passed 59/59; the final candidate-distance filter
is being reverified. This refinement is not in candidate 2.
Final edge candidate-distance filter verified: Unity EditMode 59/59 passed, zero failures.

## Edge feature manipulator placement

Fillet/Chamfer handles now anchor to the arc-length midpoint of a selected edge
in CAD model coordinates, rather than a previous face point or the model origin.
A handle is available only with a valid feature target. Removing the last edge
clears the selected-edge overlay and handle even though the draft is invalid;
the previous preview remains a reference and cannot be applied.
Unity EditMode passed 62/62 (zero failures), covering offset/nonuniformly sampled
edges for both commands and deselection through the actual workspace controls.
These changes remain source-only while headset acceptance of the installed build
is underway. No document or device state was changed in this stage.

## Spatial validation feedback

Design errors now retain an owned orange outline for command geometry: selected
fillet/chamfer edges, the current sketch draft, or an existing extrusion sketch
when its previous native snapshot is available. Hole errors restore selected-face
highlight. The panel calls this command geometry to review, not a claim that the
backend identified the exact failing entity. Details remain available; the error
outline is cleared on correction/retry, cancellation and lifecycle cleanup.
Unity EditMode passed 63/63, including a rejected fillet with retained ghost,
disabled Apply, orange context, technical Details and successful correction.
No installed APK or live CAD state was changed. Existing profiles without a prior
snapshot and parameter-to-geometry error attribution still lack precise outlines.

## Explicit sketch constraint confirmation — source integration

Added a draft constraint model with operation-budget enforcement, compatibility
and duplicate checks, dependent-relation removal after geometry deletion/type
change, and typed line/circle references independent of automatic sketch points.
The backend resolver now accepts `line:N` and `circle:N` within the named sketch;
legacy positional entity references remain supported. Add-in build passed with
zero warnings/errors. Catalogue documents the new reference forms.

The Unity flow selects type and standalone lines/circles, requires explicit
Confirm in draft, then Preview and Apply. Supports tangent, equal length/radius,
symmetry (third line axis), parallel, perpendicular and concentric. Removing the
last relation invalidates Apply. Rectangle subedge selection is not yet supported.
Core tests passed 154/154; Unity EditMode 64/64 including confirmation/cancel/no
remote call before Preview and layout. Native constraints on real Inventor and
deployment remain unverified; the installed add-in/APK do not include this change.
The user's active Quest test document was not switched or modified.

## Native constraint acceptance and user sequencing update

The user requested completing all changes before interactive Quest acceptance.
The first M3 APK was installed, but subsequent source improvements were intentionally
not installed during the proposed headset test. Finish all changes and automated/
native checks, then install a consolidated candidate before joint acceptance.

With interactive testing deferred, the maintained `--constraints` probe ran on real
Inventor: equal length, equal radius, tangent, parallel, perpendicular, concentric
and symmetry all passed preview/native relation inspection, clean rollback with
revision restoration, and persistent native relation after commit. Temporary part
closed and previous active document restored. Evidence:
`artifacts/m3-verification/native-constraints-probe.log`.
Backend regression suite also passed 922/922 after the typed-reference change.
This is real COM handler evidence, not deployment or controller acceptance.

## Rectangle-side constraints and native automatic relations

Individual rectangle sides are now selectable and highlighted in the constraint
picker. Draft references reserve four keys per element; native line indices account
for preceding rectangles, lines and circles separately. Deleting/changing a parent
shape removes its dependent relations. Tests cover reference mapping, selected-side
highlight, explicit confirmation and invalidation.
Core suite passed 155/155; Unity EditMode passed 65/65. Native `--constraints` also
passed a rectangle-side equal-length case (persisted relation and measured matching
length), automatic H/V constraints and shared COM identity of connected endpoints.
All nine native cases passed preview/rollback/persistent commit. Evidence remains
`artifacts/m3-verification/native-constraints-probe.log`. No deployment yet; complete
remaining source work before installing the consolidated candidate as requested.

## Extrusion variants and symmetric handle semantics

Expanded deployed HTTPS acceptance passed join/cut/intersect with exact expected
volumes (6.25/5.5/1 cm3 on the 6 cm3 baseline), preview rollback and Undo restoration.
New-body negative/symmetric extrusion also passed: 10 mm gives native Z bounds
-10..0 and -5..5 mm respectively. Evidence: `http-live-probe.log`.

The symmetric handle previously represented the total distance on one side.
It now spans both half-extents; controller displacement from either selected end
changes total distance by twice the outward displacement. The panel explicitly
labels total distance. Existing directional/frame regression now verifies both
symmetric endpoints and outward dragging from either end. Unity EditMode 65/65
passed. This source correction awaits the consolidated Android build.

## Current sketch context and consolidated builds

Design context now includes bounded native sketch snapshots (256 sketches / 20,000
entities) with geometry and dimensions. Excess or unavailable snapshots set the
truncation flag and warnings. Native handler acceptance verified a committed sketch
and its two dimensions in the current context. Existing-profile errors can draw
context before the first successful preview; dimension parameter errors identify
their sketch, while other parameter errors identify model edges without claiming
precise failure attribution. Core 155/155 and Unity 66/66 passed.

Consolidated builds started: package `artifacts/inventor-so-mcp-20260927-185415`
(exec session 56149) and Android `%TEMP%/xrso-m3-consolidated-build.log` (session
27350). Do not restart these while live. Deployment not performed at this stage.
Inventor inventory shows the saved-path `XR M1 Assembly 20260927 0830.iam` is dirty
again; `XR M1 Test 20260927 0830.ipt` is clean. The temporary Quest test part is no
longer open. Preserve the user's unsaved assembly before any required restart.

Consolidated acceptance preparation: maintained HTTP probe now supports --http --consolidated to assert current sketch/dimension context plus rectangle-side equal length and equal-radius constraints through plan/asset/commit/history. Harness build passed (existing nullable test-transport warning). Execution requires the new installed server/add-in and has not yet been claimed. Italian controller cases are in docs/xr-m3-quest-collaudo.md. Build session 27350 remains live; native clang++ compilation observed. Inventor restart still awaits the user's save confirmation.

## Consolidated APK installed and independently verified

Android build completed exit 0; log reports `Build Finished, Result: Success`.
Archived APK: `artifacts/m3-verification/InventorXrSo-consolidated.apk`.
SHA256: `E724DB9F923F48F01F0BC88A1EBE9DB6E10B2C3167B5DBAACCF7A0F0BECFDC6C`.
ARM64 native libraries inspected (IL2CPP, Unity, OVRPlugin).
`adb install -r` returned Success. A subsequent `sha256sum` of the Quest's actual
installed `/data/app/.../base.apk` matched the archived APK exactly. Android
lastUpdateTime: 2026-09-27 19:00:38. This confirms the consolidated app is installed,
not merely built. No headset/controller acceptance is claimed.

Server/add-in package 185415 was staged via the standard installer; manifest now
points to `%LOCALAPPDATA%/InventorSO/packages/20260927-185933-7c79b242/addin/Inventor.So.AddIn.dll`.
The running Inventor and HTTP host still use the previous version until restart.
Backend regression passed 922/922. Save confirmation for the dirty M1 assembly is
still pending; do not discard its changes. After restart, replace the HTTP host
with the consolidated package and run `--http --consolidated` before joint testing.

## Consolidated deployment and HTTPS acceptance completed

Inventor was found already closed (no process and ROT unavailable), so no unsaved
session was terminated. Restarted normally with the staged add-in, reopened the
recorded saved documents and restored `XR M1 Assembly 20260927 0830.iam` active.
New Inventor PID 57356; add-in reports Activated=True. Replaced the old HTTP host
with package 185415, same pinned certificate/token registry, target 57356; host
PID 59464.

`--http --consolidated` completed exit 0 against this deployment. Passed all prior
feature paths plus current sketch/dimension snapshots, rectangle-side equal length
and equal circle radius through preview/GLB snapshot/commit/context/Undo. Desktop
edit guard passed. Evidence: `artifacts/m3-verification/http-consolidated-probe.log`.
The probe closed only its temporary part and restored the original document.
Consolidated APK remains installed with device hash verification already recorded.
Quest app launched for reconnection check. Controller acceptance is still pending;
no completion claim follows from deployment and automated integration alone.

## Quest face-selection defect and fix (2026-09-27)

User reports no planar face selection in Design during acceptance case 7.
Read-only native reproduction on the active test part: describing the same face
produces equal reference keys but different saved reference contexts. Thus exact
comparison of opaque mesh face IDs with freshly described Design IDs is invalid.
All six planar faces reproduced this difference. Resolving both IDs confirmed
identical native COM identity for each pair; no document revision changed.

The Design context now emits body_index and face_ordinal using the same complete
B-rep enumeration as the display mesh. Design maps the picked triangle using
these indices only when scene and context match the current document/revision.
It retains the mesh face ID for highlighting and uses the current context ID for
sketch/hole commands. An unavailable/nonplanar face now produces visible feedback.

Validation: add-in build 0 warnings/errors; Unity EditMode 68/68, including actual
triangle-to-face selection with differing opaque IDs, mesh highlight, sketch
support and rejection of a stale scene. Maintained native probe
`--face-selection`: 6/6 identical COM identities, all 6 opaque references differ.
Maintained `--http --face-selection`: 6/6 mapped through deployed pinned HTTPS.
The linked test transport still emits its pre-existing CS8600 warning.

Package: artifacts/inventor-so-mcp-20260927-192809.
Installed add-in: C:/Users/salva/AppData/Local/InventorSO/packages/20260927-192914-dbd46560/addin/Inventor.So.AddIn.dll.
Saved the separate test part to the InventorSO workspace, verified every open
document clean, then restarted Inventor under prior authorization. Inventor PID
16412, add-in Activated=True; HTTP host PID17460. Reopened original assembly and
test part. Quest controller retest remains required.

Android build completed exit 0; adb install -r returned Success. Device APK SHA256 matches local B27D2D8618BB53557A5C1A5273B6C15DE4622D2E5348D2D5953DBF888C7D692B. App relaunched. Deployment evidence: artifacts/m3-verification/face-selection-manifest.json.

Controlled server interruption during Quest acceptance (case 14): stopped verified
HTTP host PID17460, waited 20 seconds, restarted the same package, certificate,
token registry and Inventor target under PID37732. Pinned HTTPS read-only probe
passed after restart (14 planar faces in the current evolved test part).
COM snapshots disconnect-before.json and disconnect-after.json confirm identical
document ID, sketch count, feature count and volume. No CAD mutations were sent.
Quest offline/online indication and recovery still require user observation.

## M3 completion — 2026-09-27

All 15 guided Quest acceptance cases are now recorded. The user explicitly
confirmed the final outstanding transparency/cancel, XY/snap, hidden constraint
list and symmetric 10 mm total/both-end drag details. Case 7's native face-ID
mismatch was reproduced, fixed, deployed and retested successfully. Controller
reports are user observations; native COM/HTTPS probes and build/test evidence
remain separately identified. See docs/xr-m3-acceptance.md for the final mapping
of milestone requirements to evidence and docs/xr-m3-quest-collaudo.md for results.
No further code changes were made after the verified face-selection APK build.
M3 is complete; M2 acceptance and later product milestones remain separate.
