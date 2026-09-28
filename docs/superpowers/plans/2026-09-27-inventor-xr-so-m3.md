# M3 — Design workspace

Scope: product specification §19–27, §39, §47, §54 and development rules §57.
M3 adds CAD authoring to the existing Quest client. M2's pending acceptance
items remain open; its local changes are the implementation baseline.

Progress/evidence: [M3 verification](../../xr-m3-verification.md),
[current acceptance audit](../../xr-m3-acceptance.md) and
[Quest cases](../../xr-m3-quest-collaudo.md). Implementation and native handler
checks are in place, including explicit constraints and rectangle-side selection.
The consolidated Android build and deployment are complete and the expanded HTTPS
acceptance passed. Headset acceptance completed on 2026-09-27 with user confirmation
of all 15 cases. The face-selection defect was fixed, deployed and retested.

## Feature contract

- Design operates on the active Inventor part. Assembly context offers explicit
  activation of an already-open part; assembly authoring belongs to M4.
- Sketch basic: origin planes XY/XZ/YZ, available work planes and planar faces;
  lines, rectangles and circles, numeric coordinates/dimensions and ray input.
  Show candidate endpoint/horizontal/vertical snaps and their coordinates.
  Coincidence/horizontal/vertical relations are intentional and visible.
- Extrude supports profile, distance, operation and direction; Hole supports a
  planar face, location, diameter and depth/through; Fillet/Chamfer use selected
  edges and exact dimensions. Spatial manipulation and keypad share parameters.
- Normal grab remains visual. CAD manipulation requires Grip + Trigger. Apply
  is explicit and enabled only for the exact, successfully previewed draft.
- The original model is translucent; the actual Inventor preview is solid.
  Capture tessellation inside the owned preview transaction before aborting it;
  never obtain the preview by fetching the restored document afterward.
- Rebuild and feature health validation run on preview and commit. Invalid
  commands keep the draft, last ghost and editable parameters, show a concise
  error with optional details and affected selection.
- A new draft, revision, document, connection loss or expired plan invalidates
  Apply. Late responses cannot resurrect an invalid plan. A commit with an
  ambiguous network result is not automatically retried.
- Undo/Redo from the wrist must be scoped to known XR changes and refuse after
  intervening desktop edits; must not silently undo unrelated desktop work.
- Voice and near hand interaction are later milestone features; numeric/ray
  paths are complete without them. Stable command/parameter names permit later
  voice mapping.

## Implementation and evidence

1. [x] Backend preview mesh captured before rollback, published as an owned GLB
   asset, with revision/permission/expiry checks and no stale entity IDs.
2. [x] CAD context discovery: planes, sketches, parameters, stable edges and
   planar-face frames for accurate model-space input.
3. [x] Core Design draft/state machine and typed operation builders, tested for
   invalid input, stale responses, cancel, disconnect and commit ambiguity.
4. [x] Unity Design workspace, sketch ray/snap drawing, dimensional manipulator,
   feature panels, ghost rendering, errors and explicit Apply/Cancel.
5. [x] Scoped Undo/Redo and wrist integration.
6. [x] .NET/backend tests, Inventor 2027 add-in compilation, Unity EditMode tests
   and Android build; inspect logs and preserve existing scene configuration.
7. [x] Live Inventor preview/cancel/commit/undo verification on test documents;
   Quest acceptance for drawing, dimensions, selection, ghost, tracking/network
   loss and return to Inspect. Document evidence and remaining limitations.

No completion claim is supported until the feature paths and their verification
are implemented. Automated transport tests with FakeAddIn are not COM or Quest
acceptance evidence.
