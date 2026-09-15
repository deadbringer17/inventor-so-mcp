# Inventor Manual Smoke Testing Checklist

Follow this numbered checklist on a machine with a runnable Autodesk Inventor desktop
(2022–2027) plus the matching .NET SDK installed, to verify the add-in, transport, and server
integration end to end.

> **Not runnable in CI.** The build machine has only Inventor interop assemblies and no confirmed
> runnable `Inventor.exe`. Do **not** claim packaging is production-ready until this checklist
> passes on a real Inventor install (see the Non-Goals in the design spec).
>
> Tool names below use the `inventor_` MCP prefix. Lengths are millimetres at the tool boundary;
> the add-in converts to Inventor's internal centimetres.

---

1. **Install the add-in bundle**
   Run the packaging script for your installed version so the per-user bundle lands at
   `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle\` (run with `-DryRun` first to
   preview the plan without writing anything):
   ```powershell
   pwsh -File .\scripts\package-bundle.ps1 -Years 2025 -Configuration Release
   ```
   **Expected:** `Bimwright.Ipt.bundle\` exists with `PackageContents.xml`, a per-version
   subfolder under `Contents\`, the `Bimwright.Ipt.Plugin.InvNN.dll`, and the matching
   `.addin` manifest.

2. **Launch Inventor**
   Start the Inventor desktop version you deployed for. Close any older instance first so the right
   add-in loads.
   **Expected:** Inventor starts; the add-in loads with no error dialog.

3. **Confirm add-in initialization + descriptor**
   Confirm the add-in loaded (no load error in Inventor's Add-In Manager) and that a session
   descriptor file `inventor-<year>-<pid>.json` was written under:
   ```text
   %LOCALAPPDATA%\Bimwright\ipt-mcp\
   ```
   **Expected:** the JSON contains `inventor_year`, `process_id`, `host_app: "Inventor"`,
   `transport` (`tcp` for 2022–2024, `pipe` for 2025–2027), `port` or `pipe_name`, `auth_token`,
   and a recent `last_heartbeat_utc`.
   The descriptor file contains the private token; `inventor_list_available_targets` and
   `inventor_get_current_target` must not return `auth_token`.

4. **Start the MCP server**
   In a separate terminal, start the stdio MCP server:
   ```powershell
   .\src\server\bin\Debug\net8.0\Bimwright.Ipt.Server.exe
   ```
   **Expected:** the server boots and waits on stdio (register it with your MCP client per
   `.mcp.json.example`).

5. **List targets** — `inventor_list_available_targets`
   **Expected:** the running Inventor instance is listed with its `target_id`
   (`inventor-<year>-<pid>`), year, pid, and transport. (`inventor_get_current_target` reports the
   pinned one, or `NO_TARGET` if none.)

6. **Health** — `inventor_health`
   **Expected:** `ok: true` with `inventor_year`, `process_id`, `has_active_document`, and
   `document_type`.

7. **New part** — `inventor_new_part`
   **Expected:** `ok: true`; a new throwaway part (`.ipt`) becomes the active document.

8. **Create a sketch and draw geometry**
   - `inventor_create_sketch` with `plane="XY"` → **Expected:** a new sketch is created and named;
     the response returns its `sketch_name`.
   - `inventor_draw_line` with `x1=0, y1=0, x2=50, y2=0` → **Expected:** a line segment is added.
   - `inventor_draw_circle` with `cx=25, cy=25, radius=10` → **Expected:** a circle is added.
   - `inventor_draw_rectangle` with `x1=0, y1=0, x2=40, y2=20` → **Expected:** a 4-line rectangle
     is added.
   **Expected overall:** each call returns `ok: true` and the geometry appears in the sketch.

9. **Add a sketch dimension** — `inventor_add_sketch_dimension`
   Dimension one of the entities from step 8 (e.g. the rectangle width to `40`).
   **Expected:** `ok: true`; the sketch shows the constraining dimension.

10. **Extrude** — `inventor_extrude`
    Close the sketch (`inventor_close_sketch`) if required, then extrude the profile, e.g.
    `sketchName="<from step 8>", distance=10, operation="join", direction="positive"`.
    **Expected:** `ok: true` with the created `feature_name`; a solid body appears.

11. **Parameters + mass properties**
    - `inventor_list_parameters` → **Expected:** model + user parameters with names, expressions,
      values, and units.
    - `inventor_get_mass_properties` → **Expected:** mass, volume, surface area, and centre of mass
      for the part.

12. **Export STEP and STL**
    - `inventor_export_step` to a temp path, e.g. `%TEMP%\smoke.step`.
    - `inventor_export_stl` to a temp path, e.g. `%TEMP%\smoke.stl`.
    **Expected:** `ok: true` for each, and both files exist on disk afterward.

13. **send_code absent by default**
    With the server started normally (no `--enable-send-code`), confirm `inventor_send_code` is
    **not** offered by the client. If the client forces the call, the dispatcher returns
    `SEND_CODE_DISABLED`.
    **Expected:** the tool is not listed / is rejected with `SEND_CODE_DISABLED`.

14. **Enable the two-sided opt-in and run a harmless snippet**
    Set `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1` in the environment **before** launching
    Inventor, and restart the server with `--enable-send-code` (or
    `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`). On the throwaway model, call `inventor_send_code`
    with a harmless read-only snippet:
    ```csharp
    System.Console.WriteLine("Active doc: " + app.ActiveDocument.DisplayName);
    ```
    **Expected:** the response contains `ok: true` and the captured `stdout` with the document name.
    (A snippet referencing a banned token such as `System.IO` must be rejected with
    `INVALID_ARGUMENT`.)

15. **Baked-tool registry initialized** — `inventor_list_baked_tools`
    **Expected:** returns an initialized registry (empty `tools` array on a fresh install) read from
    `bake.db` under `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked`.

16. **Multi-target listing and switching**
    If licensing permits, open a SECOND Inventor instance (same or different supported version).
    - `inventor_list_available_targets` → **Expected:** BOTH instances are listed with distinct
      `target_id`s.
    - `inventor_switch_target` with the second instance's id → **Expected:** `ok: true`; subsequent
      commands (e.g. `inventor_health`) route to the chosen target. Note this changes the
      server-side target selection only, not any Inventor document.

17. **Assembly batch smoke** (place / constrain / verify / part features / view)
    Fixtures `A.ipt` (plate 50×50×5 with a planar iMate `IF_MATE_TOP`) and `B.ipt` (Ø20×30 cylinder
    with an insert iMate `IF_INSERT_SHAFT`) from `C:\Temp\bimwright-spike\`.
    1. On the fixture parts, run `inventor_list_interfaces`, then create an additional named interface
       with `inventor_create_imate`. Intentionally submit one ambiguous selector first.
       **Expected:** the failure is `INVALID_ARGUMENT` with `candidates[{centroid_mm,area_mm2}]`; retry
       with `near_mm` succeeds and returns the iMate name.
    2. Run `inventor_new_assembly`, then `inventor_place_occurrence` for grounded A and two B occurrences.
       Exercise `inventor_add_constraint` with `mate`, `flush`, `insert`, and `angle` using compatible
       iMate/origin refs; use a fresh occurrence or assembly where needed to avoid over-constraint.
       **Expected:** every successful response has `health: "up_to_date"`; an unknown ref returns
       `INVALID_ARGUMENT` with the structured `available` names.
    3. Run `inventor_list_constraints`, `inventor_check_interference`,
       `inventor_measure_min_distance`, `inventor_get_assembly_bom`, and assembly-level
       `inventor_get_mass_properties`. **Expected:** all constraints are `up_to_date`, interference
       `count: 0`, mated-face distance `0`, grounded A has DOF `0/0`, and assembly mass approximates
       the sum of its occurrences.
    4. Open A and run `inventor_hole` with tapped `M6x1`, then both `inventor_circular_pattern` and
       `inventor_rectangular_pattern` using the returned hole feature name. **Expected:** `tapped: true`
       and both pattern calls return a pattern name with the requested instance count.
    5. Run `inventor_set_view_orientation` for at least two orientations, `inventor_view_fit`, and
       `inventor_capture_view` in output-path mode. **Expected:** each orientation is echoed, fit reports
       `fitted: true`, and every PNG exists, has non-zero size, and is visually non-blank.

## Drawing sheet layout (D1)

Run against a live Inventor 2027 with a saved, up-to-date part open.

Most of this section is now automated and should be run first, from the repository root, against a
live Inventor with **no documents open**:

```
python scripts/smoke-drawing-mcp.py --server <package>\server\Inventor.So.Mcp.Server.dll --format pdf
python scripts/smoke-projection-mcp.py <package>\server\Inventor.So.Mcp.Server.dll
```

The first covers items 1, 4, 5, 6, 7 and the argument-validation half of 3. The second covers the
geometry half of 3, plus 8 and 9: it creates one drawing per convention and asserts the plan view
lands on opposite sides of the front view, and it snapshots every standard style's
`FirstAngleProjection` before and after a `preview=true` call to prove the style library is
untouched. Item 2 (linearity) remains a manual reading, though a passing auto-scale in the first
script is strong indirect evidence.

Both scripts require the add-in **installed and Inventor restarted afterwards** — a running Inventor
keeps the previously loaded assembly, so a stale add-in silently answers with the old contract.

1. **Reference-scale measurement.** Call `inventor_create_drawing_safe` with `preview=true` and the
   defaults. It must return `scale_mode="auto"` and a scale from the ISO ladder. If the call fails
   while measuring, views cannot be measured at 1:500 and the reference scale must be raised to the
   smallest step that measures reliably.
   **Expected:** the call succeeds and returns a valid scale from the ladder (e.g. 1:100, 1:50).

2. **Linearity.** Call twice with `scale=1` and `scale=0.5` explicitly, and read the view extents
   from the resulting drawing in the Inventor UI. Halving the scale must halve width and height.
   If it does not, the closed-form solve is invalid: replace the single measurement pass with a
   create-measure-retry loop per ladder step. `SheetPlanner` is unchanged either way — only the
   handler's feeding of it changes.
   **Expected:** both calls succeed; view extents scale linearly with the requested scale.

3. **Projection is actually written.** Call with `projection="first"`, commit with `preview=false`,
   and check in the Inventor UI that the drawing standard reports first-angle projection and that
   the plan view sits **below** the front view. Repeat with `projection="third"` and confirm the
   plan view sits above. If the standard cannot be written, the tool must fail with
   `PROJECTION_UNAVAILABLE` rather than produce a drawing.
   **Expected:** first-angle projection shows plan below front; third-angle shows plan above front.

4. **Fit failure.** Call with `sheet_size="A4"` on a large assembly and confirm the error names a
   larger sheet size instead of asking the caller to guess a scale.
   **Expected:** the error code is `NO_FITTING_SCALE` and the response suggests a larger sheet
   (e.g. "Try sheet_size='A3'").

5. **Guards intact.** Confirm that a stale `expected_revision` still returns `STALE_REVISION`, and
   that a failed call leaves no orphan drawing document open.
   **Expected:** stale revision rejected with `STALE_REVISION`; failed calls do not leave unsaved
   drawing documents behind.

6. **View order preservation.** Call with `views="top,front,right"` (with `front` not first).
   Confirm the tool succeeds and that the response's `views` field echoes the caller's order
   (`top,front,right`), not an internal creation order. A defect found and fixed during Task 4
   rejected this exact input with "Projected views require 'front'" despite `front` being present.
   **Expected:** the call succeeds; the response `views` field reads `["top","front","right"]`.

7. **Back is a real back view, not a second side view.** Call with `views="front,right,back"` under
   `projection="first"` and check the resulting drawing in the Inventor UI. Before the fix, `back`
   was routed through `AddProjectedView` off the front view, which derives orientation from the
   *direction* to its parent, not the distance — so `back` and `right` landed as two identical
   right-side views while the response still claimed a back view.
   **Expected:** the drawing shows three distinct views — front, a right-side view, and a genuine
   back view (mirrored front, not a duplicate right view).

8. **Drawing standard write behaviour.** The handler sets `DrawingStandardStyle.FirstAngleProjection`.
   Confirm it takes effect immediately without requiring an edit bracket. Test against a
   library-resident style — it must be converted to a local copy automatically and the setting must
   take effect, not fail. Test separately against a genuinely non-writable style (e.g. a read-only
   style library on disk) — the expected failure there is `PROJECTION_UNAVAILABLE`, not silently
   producing a drawing in the host's own convention.
   **Expected:** on a writable or library-resident standard, the setting takes effect immediately
   (a library-resident style is converted to local automatically, matching
   `SetSheetMetalRuleHandler`'s guard). On a genuinely non-writable style, the tool fails with
   `PROJECTION_UNAVAILABLE`.

9. **Preview leaves the style library untouched.** With the active drawing standard style
   library-resident, call `inventor_create_drawing_safe` with `preview=true` and any `projection`.
   After the call returns, check the style in the Inventor UI (or open a fresh drawing from the same
   template) rather than trusting the tool's own report.
   **Expected:** the style is still library-resident, with its original projection convention. A
   style-library write is not part of the document transaction, so `transaction.Abort()` alone does
   not undo it — before the fix, `preview=true`, documented as leaving nothing behind, could
   permanently flip the projection convention for every future drawing on this machine.


10. **Company template drives the sheet.** Install a real company `.idw` in the template library with
    its manifest, then call `inventor_create_drawing_safe` with `template="<file>.idw"` and
    `preview=false`. Check in the Inventor UI that the sheet is the company sheet — its border and
    title block, its size — and that no view touches the title block or the border.
    **Expected:** the response echoes `template`, `sheet` from the manifest, and a `usable_area_mm`
    matching the manifest. The drawing shows the company frame, not the stock Inventor one.

11. **The sheet cannot be overridden.** Repeat with `template` plus `sheet_size="A4"`.
    **Expected:** `INVALID_ARGUMENT` naming `sheet_size`. Nothing is created. The reason is physical,
    not stylistic: a company border does not rescale when `Sheet.Size` changes, so honouring the
    override would put the frame in the wrong place while reporting success.

12. **A wrong manifest is refused, not applied.** Copy the A3 template's manifest next to the A2
    template (so it declares `sheet_size: "A3"` for an A2 sheet) and call with that template.
    **Expected:** `TEMPLATE_SHEET_MISMATCH` naming both sizes, and no drawing left open. This is the
    failure the declared area exists to prevent: applied silently, the views would be planned into
    A3 space on A2 paper.

13. **Missing pieces name what to fix.** Call with a template name that is not installed; then with
    one installed but with no `.json` beside it; then with a `.json` containing `x_max` smaller than
    `x_min`.
    **Expected:** `TEMPLATE_NOT_FOUND`, `TEMPLATE_MANIFEST_MISSING` (naming the expected file name)
    and `TEMPLATE_MANIFEST_INVALID` (naming the offending field) respectively.

14. **Paths are never accepted.** Call with `template="..\..\Windows\win.ini"`, with an absolute
    path, and with `template="sub\Company_A3.idw"`.
    **Expected:** `INVALID_ARGUMENT` for each, with no file access attempted outside the library.

15. **Title block fields reach the cartouche.** Call with `title_block_json` setting both a standard
    field (`Title`) and a custom one the company block reads (for example `Commessa`). Commit and
    read the title block in the Inventor UI, not the tool's own report.
    **Expected:** both values appear in the printed title block. The response reports
    `title_block_fields_set` for properties the document already had and
    `title_block_fields_created` for ones added as user-defined properties. A field the template
    binds to a typed property (a date, a count) must fail `TITLE_BLOCK_FIELD_REJECTED` naming the
    field rather than an opaque API error.

16. **Preview with a template leaves nothing behind.** Call with `template` and `title_block_json`
    at `preview=true`, then check the template file's timestamp and open a fresh drawing from the
    same template.
    **Expected:** the template file is unmodified and the fresh drawing has an empty title block.
    iProperty writes are not part of the document transaction, so this check is proving the discard
    of the whole draft document, not the transaction abort.

17. **Discovery matches reality.** Call `inventor_list_drawing_templates`.
    **Expected:** every installed template is listed with its declared sheet and usable area; the one
    with the broken manifest from check 13 appears with `usable: false` and the reason, rather than
    being hidden.
