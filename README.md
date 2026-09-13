# inventor-so-mcp

Development target: **Autodesk Inventor 2027, Windows x64**.

The C# implementation is in [`bridge/`](bridge/), derived from Apache-2.0
[bimwright/ipt-mcp](https://github.com/bimwright/ipt-mcp). Its license and
attribution are preserved separately from this original MIT Python project.
The server and add-in are separate processes. The new 2027 add-in has its own
GUID, pipe namespace and discovery directory.

Current verified progress and outstanding scope: [development status](docs/DEVELOPMENT.md).
Detailed requirements comparison: [80-point analysis](docs/analisi-spec-inventor-so-mcp.md).

## Installation

Everything below runs on the machine where Inventor is installed. Nothing here
modifies your CAD documents.

### 1. Requirements

| Needed for | What |
|---|---|
| The add-in | Windows x64 and **Autodesk Inventor 2027** (version 31.x) |
| Building the add-in | **.NET 10 SDK** |
| Building and running the server | **.NET 8 SDK** (or the .NET 8 runtime to run only) |
| Running the live smoke tests | Python 3 with `pywin32` |

The Inventor interop assembly is read from your local Inventor installation and is
never redistributed. If `dotnet` on your PATH is not .NET 10, put a private SDK in
`.tools\dotnet\` and the build script will prefer it.

### 2. Build the package

```powershell
git clone https://github.com/deadbringer17/inventor-so-mcp.git
cd inventor-so-mcp
./scripts/build-inventor-so.ps1
```

This writes a fresh `artifacts/inventor-so-mcp-<timestamp>/` containing `server/`
and `addin/`. It only builds: no add-in is registered and no document is touched.
Run the unit and protocol tests at any time — they need no Inventor:

```powershell
dotnet test bridge/tests/Bimwright.Ipt.Tests
```

### 3. Install the add-in

```powershell
./scripts/install-inventor-so.ps1 -Package ./artifacts/inventor-so-mcp-<timestamp>
```

The installer copies the package to `%LOCALAPPDATA%\InventorSO\packages\<version>\`,
writes the add-in manifest to
`%APPDATA%\Autodesk\Inventor 2027\Addins\InventorSO\Inventor.So.addin`, and keeps a
copy of the previous manifest. It never closes documents and never restarts Inventor.
It prints the exact server path to use in step 6 — keep that line.

### 4. Restart Inventor, then check the add-in actually loaded

Close Inventor normally (save your work first) and start it again: a running Inventor
keeps the previously loaded assembly.

```powershell
Get-ChildItem "$env:LOCALAPPDATA\InventorSO\inventor-so-mcp"
```

A file named `inventor-2027-<pid>.json` for the **running** process means the add-in is
loaded and listening. If no such file appears, go to step 5.

### 5. If the add-in does not load: unblock it

Inventor keeps a per-add-in load rule, and an unsigned add-in can end up marked
**Blocked** — in which case it silently never loads and writes no discovery file. This
is the most common installation problem.

In Inventor: **Tools → Add-In Manager** (Italian: *Strumenti → Gestione moduli aggiuntivi*),
select **Inventor SO MCP (2027)**, then under *Load Behavior*:

1. clear **Blocked**,
2. tick **Load Automatically**,
3. tick **Loaded/Unloaded** to load it right away,
4. **OK**.

The row should now read *Automatic/Loaded*. Re-run the check in step 4.

Other reasons the add-in stays absent:

- the manifest points at a package directory that was deleted — re-run step 3;
- Inventor is not 2027 — the manifest only accepts version 31.x;
- Inventor was started before the manifest was written — restart it.

### 6. Point your MCP client at the server

The server is a normal stdio MCP server. Use the path printed by the installer:

```powershell
claude mcp add inventor-so -- dotnet "$env:LOCALAPPDATA\InventorSO\packages\<version>\server\Inventor.So.Mcp.Server.dll" --target 2027 --read-only --disable-toolbaker
```

For a client configured by JSON (for example `claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "inventor-so": {
      "command": "dotnet",
      "args": [
        "C:\\Users\\<you>\\AppData\\Local\\InventorSO\\packages\\<version>\\server\\Inventor.So.Mcp.Server.dll",
        "--target", "2027",
        "--read-only",
        "--disable-toolbaker"
      ]
    }
  }
}
```

Start with `--read-only`: only query tools are exposed and nothing can be written.
Drop that flag when you want the write tools (atomic batch, workspace documents,
safe assembly and artifact operations). `inventor_health` is the first thing to call;
with several Inventor instances open, `inventor_list_available_targets` and
`inventor_switch_target` pick one explicitly instead of guessing.

### 7. Optional: put the workspace inside your Inventor project

Documents are created and saved in place only inside a managed workspace, by default
`%LOCALAPPDATA%\InventorSO\workspace`. Point it inside your active Inventor project so
drawings and native copies resolve their references:

```powershell
[Environment]::SetEnvironmentVariable('INVENTOR_SO_WORKSPACE', 'C:\Projects\MyProject\so-workspace', 'User')
```

The variable is read by the **add-in**, so set it before starting Inventor. It must be an
absolute path outside Windows and Program Files; an unusable value fails loudly rather
than silently falling back.

### 8. Verify against the real application

These scripts create, use and delete only their own workspace documents, and restore
your original set of open documents:

```powershell
python scripts/smoke-mcp.py --live --server <server-dll>
python scripts/smoke-workspace-mcp.py --server <server-dll>
python scripts/smoke-sheetmetal-mcp.py --server <server-dll>
```

### Updating

Repeat steps 2 to 4. Each install goes to its own version directory, so the previous
package stays on disk and the manifest can be pointed back at it.

### Atomic part edits (development)

`inventor_atomic_batch` accepts `document_id`, `expected_revision`, an `operations`
array of `{ "command": "set_parameter", "arguments": { "name": "Width", "value": "25 mm" } }`,
and optional `preview`. Read identity/revision from the active-document resource
immediately before planning. Up to 32 allowlisted modeling operations are rebuilt
and checked before commit; errors trigger rollback. Preview makes real temporary
changes and rolls them back, so it still requires write permission. It is not a
general dry-run, and individual write tools do not yet enforce this transaction
policy in upstream targets. The SO add-in rejects legacy direct writes outside
the atomic batch and the dedicated safe artifact/assembly operations described below. The server hides unreviewed tools through a reviewed
allowlist; the add-in also enforces ATOMIC_REQUIRED independently. Live batch verification passed on an isolated Inventor 2027 part;
the updated add-in is installed and the real MCP-to-add-in path has now passed
parameter commit/preview/rollback/stale-revision and direct-write-denial checks.
Reproduce on an isolated temporary part with:

```powershell
python scripts/smoke-atomic-mcp.py --server <path-to-Inventor.So.Mcp.Server.dll>
```

This explicit live-write test creates and closes only its own part and requires
Python with pywin32. It does not save or close user documents.

### Workspace document lifecycle

`inventor_new_document_safe(name, kind)` creates a part or assembly from the host
default template and saves it once into a managed workspace, leaving it open and
active. `inventor_save_document_safe(document_id, expected_revision)` saves the
active document in place, and with `name` performs the first save of a document
that has never been on disk — a draft from `inventor_create_drawing_safe`, saved
as IDW or Inventor DWG depending on the host template. `name` is refused for a
document already on disk: nothing is renamed, copied or relocated.
`inventor_open_document_safe(file)` reopens one workspace document by file name so
a later session continues the work; `inventor_close_document_safe(document_id)`
closes one without ever saving it; `inventor_list_workspace_documents` lists the
workspace and survives read-only mode.

In-place writing is restricted to that workspace: a document the user opened from
anywhere else fails with `OUTSIDE_WORKSPACE` and still uses `inventor_save_artifact`
copies. Dependents are never saved automatically — a dirty reference fails with
`REFERENCE_NOT_SAVED` so each document is saved explicitly. Closing refuses unsaved
changes unless `discard_changes=true`, refuses documents still referenced by another
open document, and never writes on close. Names accept 1-60 letters, digits, space,
underscore and hyphen, and never overwrite an existing workspace file.

The root defaults to `%LOCALAPPDATA%/InventorSO/workspace`. Set
`INVENTOR_SO_WORKSPACE` in the Inventor process to move it — for example inside an
active Inventor project, which is what drawing and native-copy workflows need. The
variable must be an absolute path outside Windows/Program Files; an unusable value
fails with `WORKSPACE_NOT_CONFIGURED` instead of silently falling back. Results
report `workspace_root` and `in_active_project`, and reopening a workspace assembly
from another project can still require Inventor reference resolution.

Reported `sha256` is null when Inventor holds the saved file open (it does for
Inventor DWG); size and write time still back the untouched-source checks.

This closes the loop that previously had no safe path: create a part, model it with
`inventor_atomic_batch`, save it, create an assembly, insert the saved part with
`inventor_insert_component_safe`, save it, draw it with `inventor_create_drawing_safe`,
save the drawing into the workspace, close everything and reopen it later. Reproduce
on host-owned documents only, with Inventor running:

```powershell
python scripts/smoke-workspace-mcp.py --server <path-to-Inventor.So.Mcp.Server.dll>
```

### Sheet metal

`inventor_new_document_safe(name, kind='sheet_metal')` creates a part from Inventor's
sheet-metal template; an ordinary part is never converted, and a template that does
not produce a sheet-metal part is rejected instead of returning a plain one. The
modeling commands run inside `inventor_atomic_batch`:

- `set_sheet_metal_rule` — activate a rule present in the document and/or set the
  driving thickness. A library-only rule is copied into the document first; the
  style library itself is never modified, and the applied thickness is read back.
- `sheet_metal_face` — base panel from a closed sketch profile, thickness from the rule.
- `sheet_metal_flange` — flange on portable edge ids, `height_mm` and `angle_degrees`.
- `sheet_metal_cut` — cut from a sketch profile. The default extent follows the
  thickness parameter for a sketch on a panel face; a sketch on a work plane needs
  `extent='through_all'`, otherwise Inventor produces a driverless feature.
  `across_bends=true` wraps the cut around bends.
- `sheet_metal_hem` — single, double, teardrop or rolled hem on open edges.
- `sheet_metal_fold` — fold along a sketch line whose endpoints land exactly on the
  edges of the face; a line that stops short or overshoots is refused by Inventor,
  and the bridge says so instead of passing the bare error through.
- `sheet_metal_contour_flange` — sweep an open profile along edges or to a width.
- `sheet_metal_corner_round` / `sheet_metal_corner_chamfer` — corner edges only: a
  corner edge runs through the material, so its length is the sheet thickness.
- `sheet_metal_unfold` / `sheet_metal_refold` — flatten and restore bends around a
  stationary face, so a feature can cross a bend. These are model features, separate
  from the flat pattern. Without `bend_face_ids` every reachable bend moves; with it
  only the named bends do. A bend carries no reference key of its own, so it is named
  by one of its cylindrical faces, which does.
- `sheet_metal_rip` — open a wall so a closed section can unfold (`face_extents`,
  `single_point`, `point_to_point`).
- `sheet_metal_lofted_flange` — a wall between two open profiles, die formed or press
  brake with a chord tolerance.
- `sheet_metal_punch` — a punch from Inventor's own catalog at the points of a sketch
  made on the face; `draw_point` places those points, with `model_point_mm` mapping
  model coordinates into the face sketch. Only catalog file names are accepted,
  because an iFeature is executable content.
- `create_flat_pattern` — unfold, then return to the folded model. Unfolding is the
  real test of sheet-metal geometry: extruded "walls" look identical and do not unfold.
  `align_to_edge_id` rotates the blank so a chosen model edge runs horizontally or
  vertically, which is what decides how it sits on the sheet.

`set_sheet_metal_rule` also selects the unfold rule (`unfold_rule`), which is what
decides the developed length through its K-factor or equation.

`inventor_get_sheet_metal_info` reports the active rule and its K-factor, thickness,
available rules and unfold rules, bends, flat-pattern extents and alignment, the per-bend
table (angle, inner radius, direction, K-factor) and the rule's manufacturing values
(minimum remnant, bend and corner reliefs, gap, material) read-only. Those values are reported, never judged: satisfying them does not make a
part manufacturable. `inventor_save_artifact(format='dxf', dxf_version=..., dxf_layers_json=...)`
exports an existing flat pattern through Inventor's flat-pattern translator, with the
AutoCAD version and the layer option keys limited to allowlists and layer names refused
if they carry the option string's own separators; it is refused for a folded model or a part that is
not sheet metal.

`inventor_list_topology` lists geometry with the same portable ids the modeling commands
take, so nothing has to be selected by hand in Inventor. On a part: `kind='edge'` or
`'face'` of one body. On an assembly: `kind='occurrence'` for the components, or
`kind='face'`/`'edge'` for one component's assembly-context proxies — and a planar face
proxy is exactly what `inventor_create_constraint_safe` accepts, which is what makes an
assembly constrainable unattended. Planar faces report `outward_normal` alongside the raw
surface `normal`, because the two differ whenever the face parameterization is reversed. Reproduce the whole chain, on host-owned
documents only:

```powershell
python scripts/smoke-sheetmetal-mcp.py --server <path-to-Inventor.So.Mcp.Server.dll>
python scripts/smoke-sheetmetal-features-mcp.py --server <path-to-Inventor.So.Mcp.Server.dll>
python scripts/smoke-sheetmetal-shop-mcp.py --server <path-to-Inventor.So.Mcp.Server.dll>
python scripts/smoke-assembly-autonomous-mcp.py --server <path-to-Inventor.So.Mcp.Server.dll>
```

Two sheet-metal capabilities are **not** available through the API, established by
inspecting the 2027 interop: **contour roll** has no `Add` or definition factory at all
(only enumeration of existing ones), and a **closed-profile contour flange** is refused
by Inventor. A positive `sheet_metal_rip` therefore has no reachable closed-section part
to work on yet; the command is implemented and refuses an open section explicitly.

### Safe assembly edits

`inventor_insert_component_safe` inserts a saved, clean, already-open part by
document ID. `inventor_move_component_safe` translates/rotates unconstrained
direct occurrences. `inventor_create_constraint_safe` creates planar mate/flush
constraints from persistent selected face proxies; `inventor_edit_constraint_safe`
edits existing driving offsets/angles. These require current document/revision,
explicit minimum clearance and owned transactions. Preview defaults true.
Checks are endpoint-only, not collision-free motion planning. Assembly constraint
validation checks all top-level unsuppressed pairs, with no contact exemptions.
See development status for exact tested cases and remaining restrictions.

### Safe output

`inventor_create_drawing_safe` creates an A3 draft with a front view, two
projections and an isometric view at a requested scale. Preview closes only the
new drawing; commit leaves it open. Bounds/overlap checks reserve the actual
title-block height. No dimensions or tolerances are invented: the result is not
manufacturing-ready. PDF output is available through `inventor_save_artifact`
with `format=pdf` and exports all sheets of an updated active drawing.

`inventor_save_artifact(document_id, expected_revision, format)` creates either
a native IPT/IDW/Inventor-DWG copy (`native`) or part/assembly STEP (`step`) in a unique folder under
`%LOCALAPPDATA%/InventorSO/artifacts`. It cannot overwrite a supplied path or save
the source in place. It belongs to the `export` toolset and is hidden in read-only
mode. Filesystem output is separate from CAD transactions: failures can leave a
reported partial folder. Native assembly dependency packaging and the full
DXF/BOM release package are not implemented yet. Drawing PDF alone is not a
complete release package or a dimensioned manufacturing drawing. Native drawings
require saved, clean model references inside the active Inventor project. Results
include `required_project` and `required_references`: dependencies are not copied,
and the result is not portable. Reopen using that project with the original models
available. The tool never changes project settings; out-of-project references are
rejected before writing. DWG output uses `SaveAsInventorDWG`, not generic DWG export.

### Part checkpoints

`inventor_checkpoint_create` stores a standalone single-model-state IPT snapshot
with a label, source identity/revision and SHA-256. `inventor_checkpoint_list` is
available in read-only mode. `inventor_checkpoint_restore` verifies the snapshot
and opens a new recovery copy under `InventorSO/recoveries`; it never overwrites
the original or closes user documents. The source identity must no longer be
open. This is recovery-as-copy, not an in-place or assembly dependency restore.
The catalog persists under `%LOCALAPPDATA%/InventorSO/checkpoints` and currently
lists at most 100 entries. Original artifact snapshots and recovery files are retained.

`inventor_diff_checkpoint` compares the active part with a new-format checkpoint
without opening the old CAD file or rebuilding. It reports parameter changes,
feature inventory/health changes and mass/volume/area/centre deltas with explicit
tolerances. Matching is by exact name: renames appear as removal/addition.
It does not prove B-Rep equivalence. Older checkpoints remain recoverable but
cannot be compared if they lack a semantic snapshot.

## Original NeonGlay project documentation (reference only, not the install path)

The Python source below is preserved as a modeling reference, not the new
production entry point. Its unrestricted `execute_python` tool is unsuitable
for the controlled production mode described in the specification.

**MCP server for parametric 3D modeling in Autodesk Inventor — drive Inventor with Claude (or any MCP client) in natural language.**

Build real parametric parts — flanges, shafts, nuts, brackets, sheet-metal enclosures — by talking to an AI assistant. The server wraps Inventor's COM API with high-level, millimeter-based tools and ships with battle-tested knowledge of Inventor 2026 API quirks that aren't documented anywhere else.

```
You:    "Build a DIN 934 M16 hex nut with proper conical chamfers"
Claude: creates sketch → hexagon → extrude → tapped M16×2 hole →
        revolve-cut chamfers → done. Fully parametric, dimensioned sketches.
```

### Features

- **34 MCP tools**: sketching, extrude/revolve, native hole features (drilled / tapped / counterbore), fillets, chamfers, circular patterns, sheet metal (Face / Flange / Cut with Flat Pattern support), parameters
- **`execute_python` power tool** — run arbitrary Python against the live COM connection with a persistent namespace (escape hatch for anything not covered by dedicated tools)
- **Auto-reporting** — every feature operation returns a volume/topology delta (`Hole1 | V 23497 (-503 mm³) | F7 E14`), so the AI can self-verify each step
- **Transactions** — wrap multi-step builds, roll back everything with one call
- **Hot reload** — edit the API wrapper and reload without restarting the MCP client
- **Topology helpers** — find edges/faces by coordinates instead of guessing indices
- **Parametric discipline** — projected origin points, symmetry constraints, dimensioned sketches that survive parameter changes

### Requirements

- Windows with **Autodesk Inventor** (developed and tested on Inventor 2026; older versions may need enum adjustments — see [docs/inventor-api-notes.md](docs/inventor-api-notes.md))
- **Python 3.12+**
- `pip install "mcp[cli]" pywin32`

### Installation

1. Clone this repository:
   ```
   git clone https://github.com/NeonGlay/inventor-mcp.git
   ```

2. Install dependencies:
   ```
   pip install "mcp[cli]" pywin32
   ```

3. Register the server with your MCP client. For Claude Code, add to `.mcp.json` in your project root (see `.mcp.json.example`):
   ```json
   {
     "mcpServers": {
       "inventor": {
         "command": "python",
         "args": ["-m", "src.server"],
         "cwd": "C:/path/to/inventor-mcp"
       }
     }
   }
   ```

4. Start Inventor, then ask your AI assistant to build something.

> **Do NOT use `win32com.client.gencache.EnsureDispatch`** in your own scripts against the same Python install — the generated `gen_py` cache breaks `GetActiveObject`. If it happens: delete `%LOCALAPPDATA%\Temp\gen_py`. See the API notes for the full story.

### Skills (optional, recommended)

The `skills/` directory contains two [Agent Skills](https://code.claude.com/docs/en/skills) that teach Claude the workflow and the Inventor 2026 API quirks:

- **inventor-modeling** — core patterns: units, sketch discipline, hole placement, sheet metal, diagnostic verification, common failure modes
- **inventor-din-parts** — parametric recipes for DIN/ISO standard parts (hex nuts DIN 934, bolts DIN 933, washers DIN 125, flanges DIN 2573) with dimension tables

Install by copying into your skills directory:
```
cp -r skills/inventor-modeling ~/.claude/skills/
cp -r skills/inventor-din-parts ~/.claude/skills/
```

### Why this exists

Inventor's COM API documentation is wrong or silent about many things in recent versions. This project encodes empirically verified knowledge:

- Correct Inventor 2026 enum values (`kPartDocumentObject = 12290`, dimension orientation `19201/19202/19203`, …)
- `ChamferFeatures.AddUsingDistance(EdgeCollection, d)` — the 2026 replacement for the removed `CreateChamferDefinition`
- Sheet-metal `FlangeDefinition`: the Distance argument is **silently ignored**; the real height is set via `feature.Definition.HeightExtent.Distance.Expression` (and the angle is in radians — pass `90` and you get 5156°)
- `CreateLinearPlacementDefinition` requires a `BiasPoint` argument that dynamic dispatch won't tell you about
- Edge indices renumber after every feature; fillets shift adjacent edges
- …and more in [docs/inventor-api-notes.md](docs/inventor-api-notes.md)

### Architecture

```
src/server.py        FastMCP server — tool definitions (stdio transport)
src/inventor_api.py  InventorConnection — COM wrapper, all geometry logic (mm units)
skills/              Agent Skills for Claude
docs/                Hard-won API knowledge
```

### Contributing

Contributions welcome! Especially valuable:
- Testing on Inventor 2024/2025 (enum values may differ — please report)
- Assembly (IAM) and drawing (IDW) support
- More DIN/ISO part recipes
- Bug reports with the exact COM error and Inventor version

### License

MIT — see [LICENSE](LICENSE).
