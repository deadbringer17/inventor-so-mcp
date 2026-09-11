# Inventor SO MCP — development status

Target: Inventor 2027 x64. Full functional scope remains the 80-point assessment in `analisi-spec-inventor-so-mcp.md`; the work below does not replace it with a smaller goal.

## Architecture decision

The production implementation lives in `bridge/`: an Apache-2.0 subtree imported from bimwright/ipt-mcp at d539a2ee7295747c7ef3b44a64aa870e8889deac. Its license and attribution are preserved. Root `src/` remains the MIT NeonGlay baseline/reference, not the production entry point. No hsavas code is incorporated.

Server runs separately from Inventor, using the MCP C# SDK and an authenticated local pipe to an STA-dispatched add-in. Inventor 2027 add-in targets .NET 10; server currently targets .NET 8 independently. Interop must come from the local Autodesk installation and is not redistributed.

## Delivery gates (not yet complete)

- [x] Import pinned C# implementation and preserve provenance.
- [x] Build server and actual 2027 add-in; run baseline tests.
- [ ] Fail-closed instance selection, read-only and scripting controls.
- [ ] MCP resources and state with document identity and revision.
- [ ] Persistent B-Rep keys, occurrence context, ambiguous/deleted outcomes.
- [ ] User selection, highlight, viewport, event subscriptions.
- [ ] Owned transactions, validation, checkpoints, dry run and diff.
- [ ] Complete assembly editing/constraints/clearance/motion use case.
- [ ] Complete parts/sketch/lamiera/drawing/export/release workflow.
- [ ] Rules, design intent, knowledge graph, library indexing and BATCH.
- [ ] Package under inventor-so-mcp identity; install and exercise Inventor 2027.
- [ ] Audit every specification row using real integration evidence.

No live Inventor result should be inferred from server tests or successful interop compilation. Tests that use mock transport explicitly prove protocol behavior only.

## Verified on 2026-09-11

- 188 tests passed after adding selection regression cases and resources/list to the stdio protocol test. Initial upstream run: 183 passed, 1 startup timeout at 5 seconds. The test now allows 30 seconds and always cleans up its child process on failure.
- Production add-in `bridge/src/plugin-so27` compiled against the installed 2027 interop with .NET SDK 10.0.401: zero warnings/errors. SDK installed locally in ignored `.tools/`, downloaded from Microsoft with SHA-512 verified against release metadata.
- Product add-in GUID `7EE3B69B-8A24-42DD-9422-50E8C5279F40`, pipe prefix `InventorSO`, discovery directory `%LOCALAPPDATA%/InventorSO/inventor-so-mcp`. No installation yet.
- Four resources registered: application, active-document, active-document/parameters, active-document/mass. These are snapshots, not subscriptions or revisioned persistent document URIs.
- Instance selection refuses ambiguous aliases and missing configured targets; losing a pinned instance cannot redirect commands to another instance. Explicit switching remains available.
- Inventor 2027 executable found at `Z:/Installati/Inventor 2027/Bin/Inventor.exe`. Live integration has not been performed.

Next work: install the separately identified add-in, run read-only end-to-end transport checks, then implement versioned semantic state, entity reference keys and owned transactions. Upstream STA queue timeout behavior also needs correction before autonomous writes: a request that expires while queued must not execute later. Keep the full delivery gates above open.
