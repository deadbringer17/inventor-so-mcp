namespace Bimwright.Ipt.Server;

public static class ServerInstructions
{
    public const string Text = "Inventor SO MCP for Inventor 2027. Query the active document and its revision before planning. " +
        "Use inventor_atomic_batch for supported part edits, validated and committed together or rolled back. " +
        "Read inventor://batch-commands first: it lists every batch command with its required and optional arguments; a command absent from it cannot run in a batch. " +
        "Each operation is {command, arguments}; arguments must be a JSON object. A batch failure carries a code (INVALID_ARGUMENT, STALE_REVISION, DOCUMENT_CHANGED, ROLLED_BACK, ROLLBACK_FAILED) plus the failing step_index and command. " +
        "inventor_move_component_safe translates/rotates an unconstrained direct occurrence in an owned transaction with mandatory endpoint clearance and interference checks, not swept-path checks. " +
        "inventor_edit_constraint_safe changes existing driving offsets/angles; inventor_create_constraint_safe creates mate/flush between planar faces, mate_axis between cylindrical faces, insert between circular edges, angle between faces and tangent, from proxy ids that inventor_list_topology supplies. These validate all unsuppressed top-level pairs with no intended-contact exemptions. " +
        "inventor_list_topology also works on an assembly: kind=occurrence lists components, kind=face returns their assembly face proxies, and a planar proxy is what inventor_create_constraint_safe takes, so constraining needs no user selection. Use outward_normal, not normal, to reason about which way a face points. " +
        "inventor_insert_component_safe inserts an already-open saved clean single-model-state part into the active assembly with endpoint validation and rollback; it accepts a source document ID, not arbitrary paths. " +
        "Checkpoint create/list/restore supports standalone single-state parts. Restore verifies the snapshot hash and opens a new recovery copy, not an in-place overwrite; refuses while the source identity is open. Never close unsaved user documents to bypass this guard. " +
        "preview=true makes temporary changes and aborts; it is not a read-only simulation, but an atomic batch preview leaves the revision unchanged so several batches can be planned from one read. " +
        "Document lifecycle is limited to the host-owned InventorSO workspace: inventor_new_document_safe creates a part or assembly there, inventor_open_document_safe reopens one by file name, inventor_save_document_safe with a name writes a never-saved document such as a drawing draft into the workspace once, inventor_save_document_safe saves such a document in place, inventor_close_document_safe closes it without saving, inventor_list_workspace_documents lists them. " +
        "Documents the user opened from anywhere else are never saved or closed in place: they fail with OUTSIDE_WORKSPACE and use inventor_save_artifact copies instead. Dependents are never saved automatically; save each referenced workspace document first. " +
        "Sheet metal: create the part with inventor_new_document_safe kind='sheet_metal' (an ordinary part is never converted), then use atomic batch operations set_sheet_metal_rule, sheet_metal_face, sheet_metal_flange, sheet_metal_cut and create_flat_pattern. " +
        "Also sheet_metal_hem, sheet_metal_fold, sheet_metal_contour_flange, sheet_metal_corner_round, sheet_metal_corner_chamfer, sheet_metal_unfold, sheet_metal_refold and sheet_metal_punch (catalog punches only, on a sketch made on the face, with draw_point model_point_mm). " +
        "sheet_metal_rip and sheet_metal_lofted_flange are also available; unfold/refold take bend_face_ids to move only named bends, and save_artifact format=dxf takes dxf_version and allowlisted layer names. " +
        "A fold bend line must end exactly on the edges of the face; a corner edge is as long as the sheet is thick; set_sheet_metal_rule also selects the unfold rule that decides developed length. " +
        "Thickness comes from the active rule, so cuts stay correct when it changes; a library rule is copied locally before editing and the style library itself is never modified. Flange edges are portable entity ids from inventor_list_topology or inventor_get_selection, never indices; height is in mm and angle in degrees. " +
        "inventor_get_sheet_metal_info reports the rule, thickness, bends and flat-pattern extents; inventor_save_artifact format=dxf exports the existing flat pattern only, and is refused for a folded model or a non-sheet-metal part. " +
        "Read-only mode hides write tools. inventor_save_artifact creates a new part copy or part/assembly STEP in a host-controlled folder without overwriting; it is separate from CAD rollback. Native assembly dependency packaging, legacy direct writes, scripting and save-in-place outside the workspace are unavailable. " +
        "Do not claim unsupported operations succeeded. Entity reference tokens are portable handles, not positional indices. " +
        "Resources are snapshots; poll events with its cursor, not realtime MCP subscriptions. " +
        "inventor_create_drawing_safe lays out a production drawing sheet: sheet size A4 to A0, landscape or portrait, first-angle (ISO/UNI) or third-angle (ANSI) projection, a chosen set of front/back/top/bottom/left/right/iso views, and an automatic ISO scale that leaves a dimensioning gutter around every view. It adds no dimensions, no title-block content and no parts list. " +
        "Select the intended Inventor instance explicitly if multiple targets exist.";

    public const string LegacyReferenceText =
        "ipt-mcp - MCP gateway for Autodesk Inventor 2022-2027. " +
        "Tools are prefixed inventor_*. Use whenever the user works with Inventor, " +
        ".ipt part files, .iam assembly files, or .idw/.dwg drawings. " +
        "Capabilities: create and open parts and assemblies; list open documents and get document info; " +
        "create and edit 2D sketches (lines, circles, rectangles, constraints) on a part; " +
        "build solid features from sketch profiles - extrude, revolve, fillet, chamfer, hole, circular_pattern, rectangular_pattern; " +
        "create work geometry such as a work plane and work axis; " +
        "place components (IPT/IAM) in assembly, constrain them (mate/flush/insert/angle) using named refs/proxies, create iMates on parts; " +
        "query assembly relationships, BOM, degrees of freedom, interference (clash analysis), and min distance; " +
        "zoom-fit and orient camera views; " +
        "read and write model parameters and user parameters; " +
        "read and write iProperties (iproperty: title, author, part number, description, custom properties); " +
        "compute mass properties (mass, volume, surface area, center of gravity, bounding box) and material; " +
        "export the model to STEP (step), STL (stl), and DXF (dxf, sketch or sheet-metal flat pattern). " +
        "Lengths are in millimeters (the Inventor API internal length unit is centimeters; the server converts). " +
        "Multi-instance: if more than one Inventor may be open, call inventor_list_available_targets " +
        "then inventor_switch_target to select the target session by year, pid, or descriptor id; " +
        "inventor_get_current_target reports the selected target. Versions are 4-digit years (2022..2027). " +
        "ToolBaker: inventor_list_baked_tools and inventor_run_baked_tool govern reusable baked tools; " +
        "read-only mode hides write tools but keeps query, target switching, and read-only baked tool inspection. " +
        "inventor_send_code is DISABLED unless the server is started with --enable-send-code " +
        "(or BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1) AND the add-in opts in via " +
        "BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1.";
}
