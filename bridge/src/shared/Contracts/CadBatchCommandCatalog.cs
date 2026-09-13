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
        {
            Name = name; Summary = summary; Required = required; Optional = optional;
        }
        public string Name { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Required { get; }
        public IReadOnlyList<string> Optional { get; }

        public JObject ToJson() => new()
        {
            ["command"] = Name,
            ["summary"] = Summary,
            ["required"] = new JArray(Required),
            ["optional"] = new JArray(Optional),
        };
    }

    private static Entry E(string name, string summary, string[] required, string[] optional)
        => new(name, summary, required, optional);

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
        E("add_sketch_constraint", "Constrain sketch entities: type is the constraint kind, entity_ids the entities it binds.",
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
    };

    private static readonly Dictionary<string, Entry> ByName =
        Entries.ToDictionary(e => e.Name, StringComparer.Ordinal);

    /// <summary>Every batch command name, in catalogue order.</summary>
    public static IReadOnlyList<string> Names { get; } = Entries.Select(e => e.Name).ToArray();

    /// <summary>Every catalogued command.</summary>
    public static IReadOnlyList<Entry> All => Entries;

    public static bool Contains(string? command) => command != null && ByName.ContainsKey(command);

    public static Entry? Find(string? command)
        => command != null && ByName.TryGetValue(command, out var entry) ? entry : null;

    /// <summary>The whole catalogue, as served by the <c>inventor://batch-commands</c> resource.</summary>
    public static JObject Describe() => new()
    {
        ["tool"] = "inventor_atomic_batch",
        ["units"] = "Lengths are millimetres and angles degrees unless the argument name says otherwise.",
        ["max_operations"] = 32,
        ["notes"] = new JArray(
            "Each operation is {\"command\": <name>, \"arguments\": {...}}; arguments must be a JSON object.",
            "An argument written as a | b means one of those forms is required.",
            "Sketch commands act on the most recently created sketch when sketch_name is omitted.",
            "Edge and face ids are the portable ids returned by inventor_list_topology.",
            "Scripting, file IO, document lifecycle and nested batches are outside this vocabulary."),
        ["commands"] = new JArray(Entries.Select(e => e.ToJson())),
    };
}
