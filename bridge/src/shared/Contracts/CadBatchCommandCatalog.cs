using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// The command vocabulary of <c>inventor_atomic_batch</c>, and the only place it is declared.
/// <c>AtomicCadBatch</c> admits exactly these names, so a command that is not catalogued here is not
/// executable and a catalogued one is always discoverable. Published as the
/// <c>inventor://batch-commands</c> resource: without it the arguments of a batch step can only be
/// guessed, and a guessed step costs a whole rolled-back transaction.
/// Lengths are millimetres and angles degrees unless the argument name says otherwise.
/// </summary>
public static class CadBatchCommandCatalog
{
    /// <summary>One batch command: its required and optional arguments, and what it does.</summary>
    public sealed class Entry
    {
        public Entry(string name, string summary, string[] required, string[] optional)
            : this(name, summary, required, optional, PartOnly, false) { }

        public Entry(string name, string summary, string[] required, string[] optional,
            string[] documents, bool experimental)
        {
            Name = name; Summary = summary; Required = required; Optional = optional;
            Documents = documents; Experimental = experimental;
        }
        public string Name { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Required { get; }
        public IReadOnlyList<string> Optional { get; }
        /// <summary>Document kinds the command may run in (<see cref="CadDocumentKinds"/>).</summary>
        public IReadOnlyList<string> Documents { get; }
        /// <summary>
        /// Implemented but not yet verified against a live Inventor: admitted only when both the
        /// server and the add-in run with the experimental tier enabled.
        /// </summary>
        public bool Experimental { get; }

        public bool AppliesTo(string documentKind) => Documents.Contains(documentKind);

        public JObject ToJson() => new()
        {
            ["command"] = Name,
            ["summary"] = Summary,
            ["required"] = new JArray(Required),
            ["optional"] = new JArray(Optional),
            ["documents"] = new JArray(Documents),
            ["stability"] = Experimental ? "experimental" : "stable",
        };
    }

    private static Entry E(string name, string summary, string[] required, string[] optional)
        => new(name, summary, required, optional);

    /// <summary>An experimental entry (see <see cref="Entry.Experimental"/>).</summary>
    private static Entry X(string name, string summary, string[] required, string[] optional, params string[] documents)
        => new(name, summary, required, optional, documents.Length == 0 ? PartOnly : documents, true);

    private static readonly string[] PartOnly = { CadDocumentKinds.Part };
    private const string P = CadDocumentKinds.Part, A = CadDocumentKinds.Assembly, D = CadDocumentKinds.Drawing;

    private static readonly string[] None = Array.Empty<string>();

    private static readonly Entry[] Entries =
    {
        // Parameters
        E("set_parameter", "Set an existing model or user parameter from an expression, e.g. value=\"25 mm\".",
            new[] { "name:string", "value:string" }, None),
        E("create_parameter", "Create a user parameter, e.g. unit=\"mm\", expression=\"10\".",
            new[] { "name:string", "expression:string", "unit:string" }, None),

        // Sketch
        E("create_sketch", "Start a sketch on XY|XZ|YZ, a 1-based work-plane index, or a planar face id.",
            new[] { "plane:string" }, None),
        E("close_sketch", "Finish the named sketch, or the most recent one when sketch_name is omitted.",
            None, new[] { "sketch_name:string" }),
        E("draw_line", "Sketch line between two sketch-space points in mm.",
            new[] { "x1:number", "y1:number", "x2:number", "y2:number" }, new[] { "sketch_name:string" }),
        E("draw_rectangle", "Sketch rectangle between two opposite corners in mm.",
            new[] { "x1:number", "y1:number", "x2:number", "y2:number" }, new[] { "sketch_name:string" }),
        E("draw_circle", "Sketch circle from centre and radius in mm.",
            new[] { "cx:number", "cy:number", "radius:number" }, new[] { "sketch_name:string" }),
        E("draw_arc", "Sketch arc from centre, radius and a start/end angle in degrees.",
            new[] { "cx:number", "cy:number", "radius:number", "start_deg:number", "end_deg:number" },
            new[] { "sketch_name:string" }),
        E("draw_point", "Sketch point from x,y in sketch space or model_point_mm [x,y,z] in model space; a hole centre by default.",
            new[] { "x:number+y:number | model_point_mm:number[3]" },
            new[] { "sketch_name:string", "hole_center:boolean (default true)" }),
        E("add_sketch_constraint", "Constrain sketch entities: type is coincident|parallel|perpendicular|horizontal|vertical|tangent|concentric|equal|collinear|symmetric; midpoint|fix|equal_radius need the experimental add-in build.",
            new[] { "type:string", "entity_ids:string[]" }, new[] { "sketch_name:string" }),
        E("add_sketch_dimension", "Drive one sketch entity to value_mm.",
            new[] { "entity_id:string", "value_mm:number" }, new[] { "sketch_name:string" }),
        E("project_geometry", "Project model edges into the sketch by portable edge id.",
            new[] { "edge_ids:string[]" }, new[] { "sketch_name:string" }),

        // Solid features
        E("extrude", "Extrude a sketch profile by distance_mm.",
            new[] { "sketch_name:string", "distance_mm:number>0" },
            new[] { "operation:join|cut|intersect|new_body (default join)", "direction:positive|negative|symmetric (default positive)" }),
        E("revolve", "Revolve a sketch profile about a work axis by angle_deg.",
            new[] { "sketch_name:string", "axis_id:string", "angle_deg:number" },
            new[] { "operation:join|cut|intersect|new_body (default join)" }),
        E("fillet", "Round model edges by portable edge id.",
            new[] { "edge_ids:string[]", "radius_mm:number>0" }, None),
        E("chamfer", "Chamfer model edges by portable edge id.",
            new[] { "edge_ids:string[]", "distance_mm:number>0" }, None),
        E("hole", "Place holes on a face at points_mm; face_id or a legacy face selector, never both.",
            new[] { "face_id:string | face:object", "points_mm:number[][]", "kind:drilled|counterbore|countersink", "diameter_mm:number>0" },
            new[] { "through:boolean", "depth_mm:number", "cbore_diameter_mm:number", "cbore_depth_mm:number",
                "csink_diameter_mm:number", "csink_angle_deg:number", "tapped_designation:string", "tapped_class:string",
                "tapped_thread_depth_mm:number", "tapped_full_depth:boolean", "tapped_right_handed:boolean",
                "parametric_positioning:boolean" }),
        E("circular_pattern", "Pattern named features about an axis.",
            new[] { "feature_names:string[]", "axis:string", "count:integer>1" },
            new[] { "angle_deg:number (default 360)", "natural_direction:boolean (default true)" }),
        E("rectangular_pattern", "Pattern named features along one or two directions.",
            new[] { "feature_names:string[]", "dir1:string", "count1:integer", "spacing_mm1:number" },
            new[] { "natural_direction1:boolean (default true)", "dir2:string", "count2:integer",
                "spacing_mm2:number", "natural_direction2:boolean (default true)" }),
        E("create_work_plane", "Work plane from refs; the offset type also needs offset_mm.",
            new[] { "type:offset|three_points|tangent", "refs:string[]" }, new[] { "offset_mm:number (required for offset)" }),
        E("create_work_axis", "Work axis from refs.",
            new[] { "type:string", "refs:string[]" }, None),

        // Sheet metal
        E("set_sheet_metal_rule", "Activate a rule already in the document and/or drive thickness; at least one argument.",
            new[] { "rule:string | thickness_mm:number | unfold_rule:string" }, None),
        E("sheet_metal_face", "Base panel from a closed sketch profile; thickness comes from the active rule.",
            new[] { "sketch_name:string" }, None),
        E("sheet_metal_flange", "Flange on model edges; height_mm is 0 to 10000.",
            new[] { "edge_ids:string[]", "height_mm:number>0" },
            new[] { "angle_degrees:number (default 90)", "height_datum:outer|inner|tangent (default outer)" }),
        E("sheet_metal_cut", "Cut from a sketch profile; the default extent follows the thickness parameter.",
            new[] { "sketch_name:string" },
            new[] { "extent:thickness|through_all (default thickness)", "direction:positive|negative|symmetric (default positive)",
                "across_bends:boolean (not with through_all)" }),
        E("sheet_metal_hem", "Hem on model edges; single/double take gap_mm and length_mm, teardrop/rolled take radius_mm.",
            new[] { "edge_ids:string[]" },
            new[] { "hem_type:single|double|teardrop|rolled (default single)", "gap_mm:number", "length_mm:number",
                "radius_mm:number", "angle_degrees:number (default 190)" }),
        E("sheet_metal_fold", "Fold along a sketched bend line.",
            new[] { "sketch_name:string" },
            new[] { "line_index:integer (default 1)", "angle_degrees:number (default 90)",
                "bend_location:centerline|start|end (default centerline)", "flip_direction:boolean", "flip_side:boolean" }),
        E("sheet_metal_contour_flange", "Sweep an open profile; give edge_ids to follow, width_mm to extrude, or both.",
            new[] { "sketch_name:string", "edge_ids:string[] | width_mm:number" }, None),
        E("sheet_metal_corner_round", "Round corner edges (the short edges running through the material).",
            new[] { "edge_ids:string[]", "radius_mm:number" }, None),
        E("sheet_metal_corner_chamfer", "Chamfer corner edges (the short edges running through the material).",
            new[] { "edge_ids:string[]", "distance_mm:number" }, None),
        E("sheet_metal_unfold", "Unfold bends, holding stationary_face_id still; every reachable bend unless bend_face_ids is given.",
            new[] { "stationary_face_id:string" }, new[] { "bend_face_ids:string[]" }),
        E("sheet_metal_refold", "Refold bends unfolded earlier, holding stationary_face_id still.",
            new[] { "stationary_face_id:string" }, new[] { "bend_face_ids:string[]" }),
        E("sheet_metal_rip", "Rip a wall face open; a point rip also needs the sketch holding its points.",
            new[] { "face_id:string" },
            new[] { "rip_type:face_extents|single_point|point_to_point (default face_extents)",
                "gap_side:positive|negative|symmetric (default positive)", "sketch_name:string (required for a point rip)" }),
        E("sheet_metal_lofted_flange", "Lofted flange between two different sketch profiles.",
            new[] { "sketch_one:string", "sketch_two:string" }, new[] { "output:die_formed|press_brake (default die_formed)" }),
        E("sheet_metal_punch", "Catalog punch at the centres of a sketch made on the sheet face.",
            new[] { "punch:string", "sketch_name:string" },
            new[] { "angle_degrees:number (-360 to 360)", "across_bends:boolean", "table_row:integer (1-based)", "parameters:object" }),
        E("create_flat_pattern", "Unfold the part to a flat pattern and return to the folded model; an existing one is reported, not rebuilt.",
            None,
            new[] { "align_to_edge_id:string", "alignment:horizontal|vertical (default horizontal)", "alignment_reversed:boolean" }),

        // ---- Experimental tier: implemented, awaiting live verification (see docs §26.3) ----

        // Sketch geometry
        X("draw_ellipse", "Sketch ellipse from centre, major/minor radius in mm and major-axis rotation.",
            new[] { "cx:number", "cy:number", "major_radius:number>0", "minor_radius:number>0" },
            new[] { "rotation_deg:number (default 0)", "sketch_name:string" }),
        X("draw_spline", "Sketch interpolating spline through 2 to 64 points in mm.",
            new[] { "points_mm:number[][]" }, new[] { "closed:boolean (default false)", "sketch_name:string" }),
        X("draw_slot", "Sketch straight slot between two centre points with width_mm.",
            new[] { "x1:number", "y1:number", "x2:number", "y2:number", "width_mm:number>0" }, new[] { "sketch_name:string" }),
        X("draw_polygon", "Sketch regular polygon of 3 to 64 sides around a centre.",
            new[] { "cx:number", "cy:number", "radius:number>0", "sides:integer" },
            new[] { "inscribed:boolean (default true)", "rotation_deg:number (default 0)", "sketch_name:string" }),
        X("offset_sketch_entities", "Offset connected sketch curves by distance_mm (sign picks the side).",
            new[] { "entity_ids:string[]", "distance_mm:number" }, new[] { "sketch_name:string" }),
        X("mirror_sketch_entities", "Mirror sketch curves across a sketch line.",
            new[] { "entity_ids:string[]", "mirror_line_id:string" }, new[] { "sketch_name:string" }),
        X("move_sketch_point", "Move the start, end or centre point of a sketch entity to x,y in mm (replaces trim/extend).",
            new[] { "entity_id:string", "point:start|end|center", "x:number", "y:number" }, new[] { "sketch_name:string" }),
        X("delete_sketch_entity", "Delete sketch entities by 1-based sketch entity id (highest first, so ids stay valid).",
            new[] { "entity_ids:string[]" }, new[] { "sketch_name:string" }),

        // Sketch dimensions
        X("add_dimension", "Dimension sketch entities: kind distance|horizontal_distance|vertical_distance (two points or a line), angle (two lines), radius|diameter (circle/arc), arc_length (arc).",
            new[] { "kind:string", "entity_ids:string[]" },
            new[] { "value:string (expression, e.g. \"25 mm\"; omitted keeps the drawn size)", "name:string", "driven:boolean (default false)",
                "text_x:number", "text_y:number", "sketch_name:string" }),
        X("edit_dimension", "Change a sketch dimension by parameter name: expression, driving/driven, or rename.",
            new[] { "dimension:string" }, new[] { "expression:string", "driven:boolean", "name:string", "sketch_name:string" }),
        X("delete_dimension", "Delete a sketch dimension by parameter name.",
            new[] { "dimension:string" }, new[] { "sketch_name:string" }),

        // Solid features
        X("sweep", "Sweep a closed profile along a path sketch.",
            new[] { "profile_sketch:string", "path_sketch:string" }, new[] { "operation:join|cut|intersect|new_body (default join)" }),
        X("loft", "Loft through two or more closed-profile sketches, in order.",
            new[] { "sketch_names:string[]" }, new[] { "operation:join|cut|intersect|new_body (default join)", "closed:boolean (default false)" }),
        X("shell", "Hollow the part to thickness_mm, removing the given faces.",
            new[] { "thickness_mm:number>0" }, new[] { "remove_face_ids:string[]", "direction:inside|outside|both (default inside)" }),
        X("draft", "Face draft of angle_deg against a fixed planar face.",
            new[] { "face_ids:string[]", "fixed_face_id:string", "angle_deg:number" }, None),
        X("split", "Split or trim a solid body with a work plane.",
            new[] { "work_plane:string" }, new[] { "body_index:integer (default 1)", "remove:none|positive|negative (default none)" }),
        X("thicken", "Thicken or offset faces by distance_mm.",
            new[] { "face_ids:string[]", "distance_mm:number>0" },
            new[] { "direction:positive|negative|symmetric (default positive)", "operation:join|cut|intersect|new_body (default join)" }),
        X("thread", "Cosmetic thread on a cylindrical face; designation as in the thread table, e.g. M8x1.25.",
            new[] { "face_id:string", "designation:string" },
            new[] { "internal:boolean (default false: external thread)", "full_length:boolean (default true)", "length_mm:number (when not full length)",
                "thread_type:string (default ISO Metric profile)", "thread_class:string (default 6H internal, 6g external)" }),
        X("mirror", "Mirror features by name or solid bodies by 1-based index across XY|XZ|YZ or a work plane name.",
            new[] { "plane:string", "feature_names:string[] | body_indices:integer[]" }, None),
        X("combine", "Combine tool bodies into a base body (1-based body indices).",
            new[] { "base_body:integer", "tool_bodies:integer[]" },
            new[] { "operation:join|cut|intersect (default join)", "keep_tools:boolean (default false)" }),
        X("move_body", "Translate solid bodies by dx/dy/dz in mm.",
            new[] { "body_indices:integer[]" }, new[] { "dx_mm:number", "dy_mm:number", "dz_mm:number" }),

        // Work geometry and parameters
        X("create_work_point", "Fixed work point at x,y,z mm.",
            new[] { "x_mm:number", "y_mm:number", "z_mm:number" }, new[] { "name:string" }),
        X("create_ucs", "User coordinate system from an origin and two orthogonal axis directions.",
            new[] { "origin_mm:number[3]", "x_axis:number[3]", "y_axis:number[3]" }, new[] { "name:string" }),
        X("rename_work_geometry", "Rename a work plane, axis, point or UCS.",
            new[] { "name:string", "new_name:string" }, None, P, A),
        X("delete_work_geometry", "Delete a user work plane, axis, point or UCS (origin geometry is refused).",
            new[] { "name:string" }, None, P, A),
        X("rename_parameter", "Rename a user or model parameter; expressions that use it follow.",
            new[] { "name:string", "new_name:string" }, None, P, A),
        X("delete_parameter", "Delete a user parameter that nothing depends on.",
            new[] { "name:string" }, None, P, A),
        X("set_document_iproperty", "Set a whitelisted iProperty of the active document (Part Number, Description, Stock Number, Revision Number, Project, Vendor, Designer, Title, Subject, Author, Keywords, Comments).",
            new[] { "name:string", "value:string" }, None, P, A, D),

        // Document state (not view state: these change what is saved)
        X("set_visibility", "Show or hide a solid body (part, body_index) or an occurrence (assembly, occurrence_id).",
            new[] { "visible:boolean", "body_index:integer | occurrence_id:string" }, None, P, A),
        X("activate_design_view", "Activate a design view representation by name.",
            new[] { "name:string" }, None, P, A),
        X("activate_model_state", "Activate a model state by name.",
            new[] { "name:string" }, None, P, A),
        X("create_model_state", "Create a model state (it becomes active).",
            new[] { "name:string" }, None, P, A),

        // Assembly
        X("suppress_component", "Suppress or unsuppress an occurrence.",
            new[] { "occurrence_id:string", "suppressed:boolean" }, None, A),
        X("replace_component", "Replace an occurrence with a document from the host workspace (file name, never a path).",
            new[] { "occurrence_id:string", "workspace_document:string" }, new[] { "replace_all:boolean (default false)" }, A),
        X("pattern_component", "Rectangular occurrence pattern along assembly X|Y|Z.",
            new[] { "occurrence_ids:string[]", "dir1:X|Y|Z", "count1:integer>1", "spacing_mm1:number" },
            new[] { "dir2:X|Y|Z", "count2:integer", "spacing_mm2:number" }, A),
        X("activate_positional_representation", "Activate a positional representation by name.",
            new[] { "name:string" }, None, A),
        X("set_bom_structure", "Per-occurrence BOM structure override, stored in the assembly.",
            new[] { "occurrence_id:string", "structure:normal|purchased|phantom|reference|inseparable" }, None, A),

        // Drawing (active drawing document)
        X("add_sheet", "Add a sheet; A-series size and orientation.",
            None, new[] { "size:A4|A3|A2|A1|A0 (default A3)", "orientation:landscape|portrait (default landscape)", "name:string" }, D),
        X("activate_sheet", "Activate a sheet by name.",
            new[] { "sheet:string" }, None, D),
        X("delete_sheet", "Delete a sheet by name; the last sheet is refused.",
            new[] { "sheet:string" }, None, D),
        X("add_base_view", "Base view of an open model document at x,y mm on the active sheet.",
            new[] { "document_id:string", "x_mm:number", "y_mm:number" },
            new[] { "scale:number (default 1)", "orientation:front|back|top|bottom|left|right|iso (default front)", "style:hidden (hidden lines removed)|visible (hidden lines shown)|shaded (default hidden)" }, D),
        X("add_projected_view", "Projected view of a parent view (by name) at x,y mm.",
            new[] { "parent_view:string", "x_mm:number", "y_mm:number" }, None, D),
        X("move_drawing_view", "Move a drawing view centre to x,y mm.",
            new[] { "view:string", "x_mm:number", "y_mm:number" }, None, D),
        X("set_view_scale", "Set a drawing view scale.",
            new[] { "view:string", "scale:number>0" }, None, D),
        X("add_note", "General note at x,y mm on the active sheet.",
            new[] { "text:string", "x_mm:number", "y_mm:number" }, None, D),
        X("add_parts_list", "Parts list for the assembly shown in a view, placed at x,y mm.",
            new[] { "view:string", "x_mm:number", "y_mm:number" }, None, D),
    };

    private static readonly Dictionary<string, Entry> ByName =
        Entries.ToDictionary(e => e.Name, StringComparer.Ordinal);

    /// <summary>Every stable (production) batch command name, in catalogue order.</summary>
    public static IReadOnlyList<string> Names { get; } = Entries.Where(e => !e.Experimental).Select(e => e.Name).ToArray();

    /// <summary>Every experimental batch command name, in catalogue order.</summary>
    public static IReadOnlyList<string> ExperimentalNames { get; } = Entries.Where(e => e.Experimental).Select(e => e.Name).ToArray();

    /// <summary>Every catalogued name, stable and experimental.</summary>
    public static IReadOnlyList<string> AllNames { get; } = Entries.Select(e => e.Name).ToArray();

    /// <summary>Every catalogued command, stable and experimental.</summary>
    public static IReadOnlyList<Entry> All => Entries;

    public static bool Contains(string? command) => command != null && ByName.ContainsKey(command);

    public static Entry? Find(string? command)
        => command != null && ByName.TryGetValue(command, out var entry) ? entry : null;

    /// <summary>
    /// The catalogue, as served by the <c>inventor://batch-commands</c> resource. Experimental
    /// entries are listed only when the caller can run them, so a client never plans with a
    /// command that would be refused.
    /// </summary>
    public static JObject Describe(bool includeExperimental = false) => new()
    {
        ["tool"] = "inventor_atomic_batch",
        ["units"] = "Lengths are millimetres and angles degrees unless the argument name says otherwise.",
        ["max_operations"] = 32,
        ["notes"] = new JArray(
            "Each operation is {\"command\": <name>, \"arguments\": {...}}; arguments must be a JSON object.",
            "An argument written as a | b means one of those forms is required.",
            "Sketch commands act on the most recently created sketch when sketch_name is omitted.",
            "Edge and face ids are the portable ids returned by inventor_list_topology.",
            "documents lists the active-document kinds a command runs in; a batch mixing kinds is refused.",
            "validate (optional batch argument) adds checks run once before commit; see validation.",
            "Scripting, file IO, document lifecycle and nested batches are outside this vocabulary."),
        ["validation"] = ValidationSpec.Describe(),
        ["commands"] = new JArray(Entries.Where(e => includeExperimental || !e.Experimental).Select(e => e.ToJson())),
    };
}
