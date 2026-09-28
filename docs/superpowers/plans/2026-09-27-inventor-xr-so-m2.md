# M2 — Inspect workspace

Scope: product specification §53. Extend M1 without replacing its connection, asset cache or session lifecycle.

Implementation delivered with 109 client tests, 895 backend tests, 41 Unity EditMode tests and a successful Android APK build. Device/live CAD acceptance remains explicitly pending in [the M2 verification report](../../xr-m2-verification.md).

- Read-only inspection snapshot: document/revision guard, occurrence identity, mass/material/properties, nullable constraint/DOF values. Explicit activation of already-open CAD documents only.
- Local Browser context with hierarchy and clickable breadcrumb. Hidden by default; wrist entry; panel can be pinned and repositioned during the session.
- Distance between picked surface points, in CAD millimetres independent of visual scale; temporary by default, explicit pin, invalidated on document/geometry revision. Mesh-derived measurement is labelled approximate.
- Local section plane with physical grab and numeric offset/angle; clipped geometry is excluded from ray selection. No CAD cut or persistent change.
- Explicit 1:1, Fit to room (user-configurable available extent), Table scale; never auto-shrink. Global visual grab has no backend write.
- MR/Studio switch in the same inspection workspace. Controller UI consumes clicks before CAD picks.
- On disconnect retain the model and local inspection tools; backend-dependent actions disabled. Cancel obsolete requests on scene/session change.

Validation: .NET core and backend tests, Inventor 2027 interop compile, Unity EditMode and Android build when available. Quest comfort, passthrough and live Inventor properties remain device acceptance checks unless actually exercised.

Existing M1 uncommitted files are preserved. No scene regeneration is needed: M2 composes its panels at runtime from AppController.
