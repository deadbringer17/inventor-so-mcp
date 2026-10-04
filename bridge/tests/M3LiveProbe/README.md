# M3 live Inventor handler probe

Requires running Inventor 2027 and its installed interop. Build the experimental
add-in first, then run from the repository root:

```powershell
dotnet build bridge/src/plugin-so27/Inventor.So.AddIn.csproj -p:SoExperimental=true -p:OutputPath=bin/M3/net48/ --no-restore
dotnet run --project bridge/tests/M3LiveProbe --artifacts-path artifacts/m3-live-probe
```

The STA process loads the built handler assembly and real CAD event tracker,
attaches to Inventor through COM and creates a new temporary part. It uses the
actual XR client operation builders, then invokes the real atomic/context/history
handlers. It tests preview mesh capture and clean rollback, persistent dimensions,
extrusion commit/undo/redo, and hole/fillet/chamfer preview/commit/undo with volume
checks. Finally it closes only its temporary part without saving and reactivates
the previously active document. It does not replace the installed add-in.

This proves handler/COM integration, not in-process dispatcher threading, the
deployed HTTP-to-Inventor connection, or Quest interaction. Run only when Inventor
is available for automation; the probe temporarily changes the active document.

## Deployed HTTPS mode

Run the same command with `-- --http` to exercise the production core client,
authenticated MCP, pinned TLS, GLB asset download and the installed add-in through
the HTTP server on localhost:8443. This local acceptance setup reads the certificate
and token registry from `%TEMP%/xrso-quest-live/server.pfx` and
`tokens-revocation.txt`; it never prints credentials. The host must target the
running Inventor instance with experimental support enabled.

It creates and closes its own temporary part and restores the previous active
document. Checks cover preview rollback and revision preservation, extrusion and
persistent dimensions, Undo/Redo, through/blind holes, fillet, chamfer, parameter
changes, and rejection of XR history after a native desktop transaction.
This is deployed integration evidence, not headset interaction acceptance.
After installing the consolidated server/add-in, add `--consolidated` after
`--http` to also verify current sketch snapshots and typed-reference constraints
(rectangle-side equal length and equal circle radius), including rollback,
downloaded constraint data, persisted context and guarded Undo.

## Native sketch-constraint mode

Run with `-- --constraints` to validate confirmed equal-length/equal-radius,
tangent, parallel, perpendicular, concentric and symmetry relations through the
built handlers and real Inventor. Each case checks the native constraint in the
captured preview, clean sketch/revision rollback, then the persisted constraint
after commit. It uses a new temporary part and restores the original active
document on exit. Compile-only validation does not establish native correctness.
Do not run while the user is interacting with the active Inventor/Quest document.

## face_feature (M9-08)

Run with `-- --face-feature` (same experimental add-in build as above). It builds a temporary part
(extrude, fillet, chamfer, hole), calls `face_feature` on one face per feature and checks name, type,
roles, `editable` vs the expression, `previous_feature`, an expression-driven distance (not editable)
and that the revision does not change. Covers extrude, fillet, chamfer, hole only; revolve, patterns and
sheet-metal flange still need a fixture case before the tool leaves the experimental tier.
