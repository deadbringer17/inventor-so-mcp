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

## Verified on 2026-09-12

- Installed and activated the initial SO add-in in an already running Inventor 2027 (7 documents). `scripts/smoke-mcp.py --live` verified MCP initialize, read-only tool filtering, four initial resources, actual pipe health and active assembly metadata. No user document modified or closed.
- Fixed expired queued commands: DeadlineOperation prevents execution after a queue deadline. If a COM operation already started, timeout explicitly reports unknown outcome. Four regression tests cover expiration, delayed timer, in-flight work and one-shot execution.
- Added get_selection and resolve_entity tools plus selection/events resources. Full tool surface is now 60 by default, 61 with scripting opt-in; six resources. Unit/protocol suite: 201 passing tests.
- Portable reference tokens contain document InternalName, entity kind, Inventor reference key and saved context. Supported adapters: face/edge/vertex, their proxies, occurrence and occurrence proxy. They are resolvable handles, not yet a canonical short-ID registry; repeated captures are not guaranteed identical tokens. Legacy modeling tools still need migration away from positional indices. Ambiguous/unresolved/document-not-open outcomes are explicit.
- LiveProbe inspected an occurrence in the current assembly without changing dirty flag, database revision or open-document count. Fixture mode created a separate invisible cylinder IPT under ignored artifacts, captured a face key, changed extrusion length, rebuilt, saved, closed and reopened it: the key resolved at every stage. Fixture documents were closed; the prior active document and document count were restored.
- CadEventTracker subscribes to real Inventor document change/create/open/save/activate/close events. A bounded journal exposes epoch, cursor, document revision and resync_required. Live fixture observed change/save/close/open and confirmed a revision change after editing. Closing-document identity is captured before COM disconnects. Selection and viewport events, MCP push subscriptions and concurrency enforcement remain pending.
- `database_revision` is Inventor's property; `revision` is the separate event-journal token. Tokens invalidate on add-in restart. Duplicate document InternalNames are treated as ambiguous during reference resolution.
- Inventor retains the loaded DLL after Deactivate. Initial build was reactivated; updates are staged in immutable local version directories with the installed manifest pointing to the next build. A normal restart after user saves work is required to validate the new handlers end-to-end inside the add-in. No automatic restart performed. LiveProbe validates the same adapter source externally, not the updated in-process deployment.
- `scripts/install-inventor-so.ps1` stages add-in/server, preserves previous manifest outside scanned add-in folders, and updates only SO's manifest. The Autodesk interop is not redistributed.

Next: verify updated add-in after restart; implement owned transactions and mutating-command guards using the event revision; expand semantic state and migrate model tools to persistent references. All incomplete delivery gates remain open; the full 80-point scope is not yet achieved.
