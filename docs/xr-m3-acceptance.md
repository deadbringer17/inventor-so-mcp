# M3 acceptance audit

Status: completed 2026-09-27. All 15 controller cases have positive user reports,
with native/automated evidence for the underlying CAD and lifecycle contracts.
The user explicitly confirmed the final transparency, XY, hidden-constraint and
symmetric-distance details. The face-selection defect discovered in case 7 was
fixed, deployed and successfully retested. This is the current requirement audit; the verification log
preserves historical stages and must not be read as a single current status.

## Scope and evidence

The milestone is product specification §54: Design, basic sketches, parameter
input, Extrude, Hole, Fillet/Chamfer, Preview, Validation and Commit. Relevant
interaction contracts are §§19–27, 39, 47 and 57. Voice is scheduled in §56;
the plan defers near-hand interaction and treats ray/numeric input as M3 paths.

| Requirement | Implementation/automated evidence | Controller acceptance |
| --- | --- | --- |
| Part-only Design and plane/planar-face context | Core context parsing, Unity workspace guard, COM/HTTPS reads | Cases 3, 7, 13; XY confirmed in final response |
| Basic line/rectangle/circle, ray and precise numeric entry | SketchDraft and Unity controls; native persistent dimensions probe | Cases 3–4 passed: drawing, snap/A lock, keypad, quota text and persisted values |
| Selected face enters sketch directly (§20.1) | Native body/face ordinal mapping at same revision; distinct mesh/CAD IDs; regression and real COM identity checks | Case 7 defect fixed, APK/add-in installed, user retest passed |
| Automatic coincidence/H/V; hidden and selected/all constraints | Native H/V/shared endpoint identity; preview/rollback/commit for equal-length/equal-radius, tangent, symmetry, parallel, perpendicular, concentric | Cases 3, 5–6 passed, including rectangle-side selection and explicit final hidden-state confirmation |
| Spatial feature dimension and numeric field share value | Frame/direction tests and both-end drag regression | Cases 7–8 passed: Grip visual, Grip+Trigger dimension and preview on release |
| Extrude distance/operation/direction | Deployed join/cut/intersect exact volumes; new-body negative/symmetric bounds | Cases 1–2, 7–8 passed; 10 mm total = 5 per side and both ends explicitly confirmed |
| Hole position/diameter/depth | Deployed through/blind preview/commit/undo | Case 9 passed: face position, diameter 4 mm, blind 5 mm and through |
| Fillet/Chamfer edge dimensions | Deployed preview/commit/undo; visibility/occlusion regression | Case 10 passed: 1 mm, visible/hidden edges, handle location, final deselection |
| Existing parameter edits | Deployed parameter preview/commit/undo | Case 12 passed: quota +1 mm, CAD geometry/value and Undo |
| Actual solid preview, original translucent | Mesh captured before rollback; owned GLB parsed; renderer tests | Case 1 and final explicit transparency/cancel confirmation |
| Validation and editable failure | Rebuild/feature health; retained ghost; invalid-draft guard; orange command context; current sketch snapshots | Case 11 passed: radius 1 → 100 → 1 mm, error/details, highlight, disabled/re-enabled Apply |
| Explicit Apply, exact plan, no replay | Session/transport expiry and late-response tests; deployed commit checks | Cases 1–2, 11, 13–14 passed; no automatic mutation across interruption |
| Scoped Undo/Redo | Deployed round trip and native desktop-transaction guard | Cases 2, 12–13 passed: 20 → 10 → 20 mm, parameter Undo, desktop work protected |
| Stale/offline/rebind guards and return to Inspect | Session/Unity lifecycle tests; controlled server restart and unchanged COM snapshot | Cases 13–15 passed: context, tracking, reconnect, new preview, clean Inspect/Browser/Design |
| Build deliverables | Installed package/APK identified below; device APK hash matched; Unity 68/68 | Acceptance performed on installed candidates; focused face fix successfully retested |

## Live environment

Current package after the face-selection fix: `artifacts/inventor-so-mcp-20260927-192809`.
Installed APK SHA256: `B27D2D8618BB53557A5C1A5273B6C15DE4622D2E5348D2D5953DBF888C7D692B`.
Device hash matches the local APK. Evidence: `artifacts/m3-verification/face-selection-manifest.json`.
Unity EditMode 68/68 passed. Native probe confirmed matching COM identities for
all six planar faces despite differing opaque IDs; deployed HTTPS mapping passed.
After later controller operations, the read-only reconnect probe mapped 14 planar
faces in the evolved test part. HTTP server PID37732 targets Inventor PID16412.
The saved test part is `C:/Users/salva/AppData/Local/InventorSO/workspace/XR M3 Collaudo consolidato 20260927.ipt`.

Earlier consolidated integration baseline (before the focused face-selection fix):
Deployed integration output: `artifacts/m3-verification/http-consolidated-probe.log`.
The maintained reproduction is `bridge/tests/M3LiveProbe -- --http --consolidated`, using the
production core backend, pinned certificate, authenticated MCP, installed add-in
and real Inventor geometry. These checks passed, including the new snapshots and
typed constraints. The installed APK hash was read back from the device and matched.
The app process is running with its activity resumed; no AndroidRuntime crash was
reported in the inspected log. These checks do not operate headset controllers.

Joint controller acceptance is complete; actual user observations are recorded
in `docs/xr-m3-quest-collaudo.md`. Headset results are user-reported, while native
geometry, deployment hashes and test results are tool-verified.

## Completion gate

M3 completion gate passed: implementation, build/deployment, native verification,
guided controller acceptance and defect retest are recorded above. M2's separate
acceptance is not implicitly closed. Voice, assembly editing, sheet metal and
near-hand interaction remain later work under the original milestone plan.
