#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Properties;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

// Batch commands (plan §8-§12). They run inside the atomic batch's transaction and never start
// their own. Calls whose interop signature has not been checked against the installed 2027
// interop go through `dynamic` (IDispatch late binding): a wrong guess fails at run time inside
// the transaction - and is rolled back - instead of breaking the add-in build.

internal static class FeatureX
{
    public static PartComponentDefinition Def(Application app, string command) => X.ActivePart(app, command).ComponentDefinition;

    public static JObject Feature(object feature)
    {
        dynamic f = feature;
        var result = new JObject();
        try { result["feature_name"] = (string)f.Name; } catch { }
        try { result["health"] = f.HealthStatus.ToString(); } catch { }
        return result;
    }

    public static FaceCollection Faces(Application app, global::Inventor.Document doc, IEnumerable<string> ids)
    {
        var faces = app.TransientObjects.CreateFaceCollection();
        foreach (var id in ids)
            faces.Add(X.Resolve(doc, id, out _) as Face ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: " + id + " is not a face."));
        return faces;
    }

    public static ObjectCollection Bodies(Application app, PartComponentDefinition def, IEnumerable<int> indices)
    {
        var bodies = app.TransientObjects.CreateObjectCollection();
        foreach (var index in indices) bodies.Add(X.Body(def, index));
        return bodies;
    }

    /// <summary>The first curve of a sketch, which seeds a path through its connected curves.</summary>
    public static SketchEntity FirstCurve(PlanarSketch sketch)
    {
        foreach (SketchEntity entity in sketch.SketchEntities)
            if (entity is SketchLine or SketchArc or SketchSpline or SketchCircle or SketchEllipse) return entity;
        throw new ArgumentException("Sketch " + sketch.Name + " has no curve to follow.");
    }
}

public sealed class SweepHandler : ExperimentalHandler
{
    public override string Name => "sweep";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var def = FeatureX.Def(app, Name);
        var profile = X.Profile(def, X.Str(p, "profile_sketch"));
        var pathSketch = EntityResolver.FindSketch(def, X.Str(p, "path_sketch")) ?? throw new ArgumentException("No sketch named " + p["path_sketch"] + ".");
        dynamic features = def.Features;
        object path = features.CreatePath(FeatureX.FirstCurve(pathSketch));
        dynamic sweeps = def.Features.SweepFeatures;
        object definition = sweeps.CreateSweepDefinition(SweepTypeEnum.kPathSweepType, profile, path, X.Operation((string?)p["operation"]));
        return FeatureX.Feature(sweeps.Add(definition));
    }
}

public sealed class LoftHandler : ExperimentalHandler
{
    public override string Name => "loft";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var def = FeatureX.Def(app, Name);
        var names = X.Strings(p, "sketch_names", 32);
        if (names.Length < 2) throw new ArgumentException("A loft needs at least two sketches.");
        var sections = app.TransientObjects.CreateObjectCollection();
        foreach (var name in names) sections.Add(X.Profile(def, name));
        dynamic lofts = def.Features.LoftFeatures;
        dynamic definition = lofts.CreateLoftDefinition(sections, X.Operation((string?)p["operation"]));
        if (X.Bool(p, "closed", false)) definition.Closed = true;
        return FeatureX.Feature(lofts.Add(definition));
    }
}

public sealed class ShellHandler : ExperimentalHandler
{
    public override string Name => "shell";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var part = X.ActivePart(app, Name);
        var def = part.ComponentDefinition;
        double thickness = UnitConvert.MmToCm(X.Positive(p, "thickness_mm"));
        var removed = p["remove_face_ids"] is JArray ? X.Strings(p, "remove_face_ids") : Array.Empty<string>();
        var faces = FeatureX.Faces(app, (global::Inventor.Document)part, removed);
        var direction = ((string?)p["direction"] ?? "inside").ToLowerInvariant() switch
        {
            "inside" => ShellDirectionEnum.kInsideShellDirection,
            "outside" => ShellDirectionEnum.kOutsideShellDirection,
            "both" => ShellDirectionEnum.kBothSidesShellDirection,
            _ => throw new ArgumentException("direction must be inside, outside or both."),
        };
        dynamic shells = def.Features.ShellFeatures;
        object definition = shells.CreateShellDefinition(faces, thickness, direction);
        return FeatureX.Feature(shells.Add(definition));
    }
}

public sealed class DraftHandler : ExperimentalHandler
{
    public override string Name => "draft";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var part = X.ActivePart(app, Name);
        var faces = FeatureX.Faces(app, (global::Inventor.Document)part, X.Strings(p, "face_ids"));
        var fixedFace = X.Resolve((global::Inventor.Document)part, X.Str(p, "fixed_face_id"), out _) as Face
            ?? throw new ArgumentException("fixed_face_id must be a face.");
        double angle = X.Num(p, "angle_deg");
        if (Math.Abs(angle) >= 89) throw new ArgumentException("angle_deg must be within (-89, 89).");
        var drafts = part.ComponentDefinition.Features.FaceDraftFeatures;
        var definition = drafts.CreateFaceDraftDefinition();
        definition.SetFixedPlane(faces, fixedFace, angle * Math.PI / 180.0,
            DraftAngleConstraintTypeEnum.kOneWayDraftAngle, Type.Missing, false);
        return FeatureX.Feature(drafts.Add(definition));
    }
}

public sealed class SplitHandler : ExperimentalHandler
{
    public override string Name => "split";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var def = FeatureX.Def(app, Name);
        var plane = X.Plane(def, X.Str(p, "work_plane"));
        var body = X.Body(def, p["body_index"]?.Type == JTokenType.Integer ? (int)p["body_index"]! : 1);
        dynamic splits = def.Features.SplitFeatures;
        string remove = ((string?)p["remove"] ?? "none").ToLowerInvariant();
        object feature = remove switch
        {
            "none" => splits.SplitBody(plane, body),
            "positive" => splits.TrimSolid(plane, body, true),
            "negative" => splits.TrimSolid(plane, body, false),
            _ => throw new ArgumentException("remove must be none, positive or negative."),
        };
        return FeatureX.Feature(feature);
    }
}

public sealed class ThickenHandler : ExperimentalHandler
{
    public override string Name => "thicken";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var part = X.ActivePart(app, Name);
        var faces = FeatureX.Faces(app, (global::Inventor.Document)part, X.Strings(p, "face_ids"));
        dynamic thickens = part.ComponentDefinition.Features.ThickenFeatures;
        object feature = thickens.Add(faces, UnitConvert.MmToCm(X.Positive(p, "distance_mm")),
            X.Direction((string?)p["direction"]), X.Operation((string?)p["operation"]));
        return FeatureX.Feature(feature);
    }
}

public sealed class ThreadHandler : ExperimentalHandler
{
    public override string Name => "thread";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var part = X.ActivePart(app, Name);
        var face = X.Resolve((global::Inventor.Document)part, X.Str(p, "face_id"), out _) as Face ?? throw new ArgumentException("face_id must be a face.");
        if (face.SurfaceType != SurfaceTypeEnum.kCylinderSurface) throw new ArgumentException("A thread needs a cylindrical face.");
        dynamic threads = part.ComponentDefinition.Features.ThreadFeatures;
        // Internal (tapped hole) or external (bolt shank): the caller says which; the face alone
        // does not tell without evaluating its normal against the axis.
        bool internalThread = X.Bool(p, "internal", false);
        object info = threads.CreateStandardThreadInfo(internalThread, true,
            (string?)p["thread_type"] ?? "ISO Metric profile", X.Str(p, "designation"), (string?)p["thread_class"] ?? (internalThread ? "6H" : "6g"));
        Edge? startEdge = null;
        foreach (Edge edge in face.Edges) { startEdge = edge; break; }
        bool fullLength = X.Bool(p, "full_length", true);
        object feature = fullLength
            ? threads.Add(face, startEdge, info, false, true)
            : threads.Add(face, startEdge, info, false, false, UnitConvert.MmToCm(X.Positive(p, "length_mm")));
        return FeatureX.Feature(feature);
    }
}

public sealed class MirrorHandler : ExperimentalHandler
{
    public override string Name => "mirror";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var def = FeatureX.Def(app, Name);
        var plane = X.Plane(def, X.Str(p, "plane"));
        var parents = app.TransientObjects.CreateObjectCollection();
        bool bodies = p["body_indices"] is JArray;
        if (bodies)
            foreach (var index in X.Ints(p, "body_indices")) parents.Add(X.Body(def, index));
        else
            foreach (var name in X.Strings(p, "feature_names"))
            {
                PartFeature? found = null;
                foreach (PartFeature feature in def.Features) if (feature.Name == name) { found = feature; break; }
                parents.Add(found ?? throw new ArgumentException("No feature named " + name + "."));
            }
        // MirrorOfBody is read-only: Inventor infers it from bodies in the parent collection.
        dynamic mirrors = def.Features.MirrorFeatures;
        dynamic definition = mirrors.CreateDefinition(parents, plane, PatternComputeTypeEnum.kIdenticalCompute);
        return FeatureX.Feature(mirrors.AddByDefinition(definition));
    }
}

public sealed class CombineHandler : ExperimentalHandler
{
    public override string Name => "combine";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var def = FeatureX.Def(app, Name);
        int baseIndex = X.Int(p, "base_body", 1, 1000);
        var tools = X.Ints(p, "tool_bodies");
        if (tools.Contains(baseIndex)) throw new ArgumentException("The base body cannot also be a tool body.");
        var operation = ((string?)p["operation"] ?? "join").ToLowerInvariant() switch
        {
            "join" => PartFeatureOperationEnum.kJoinOperation,
            "cut" => PartFeatureOperationEnum.kCutOperation,
            "intersect" => PartFeatureOperationEnum.kIntersectOperation,
            _ => throw new ArgumentException("operation must be join, cut or intersect."),
        };
        dynamic combines = def.Features.CombineFeatures;
        object feature = combines.Add(X.Body(def, baseIndex), FeatureX.Bodies(app, def, tools), operation, X.Bool(p, "keep_tools", false));
        return FeatureX.Feature(feature);
    }
}

public sealed class MoveBodyHandler : ExperimentalHandler
{
    public override string Name => "move_body";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var def = FeatureX.Def(app, Name);
        double dx = X.Num(p, "dx_mm", 0), dy = X.Num(p, "dy_mm", 0), dz = X.Num(p, "dz_mm", 0);
        if (dx == 0 && dy == 0 && dz == 0) throw new ArgumentException("A move needs a non-zero dx_mm, dy_mm or dz_mm.");
        dynamic moves = def.Features.MoveFeatures;
        dynamic definition = moves.CreateMoveDefinition(FeatureX.Bodies(app, def, X.Ints(p, "body_indices")));
        definition.AddFreeDrag(UnitConvert.MmToCm(dx), UnitConvert.MmToCm(dy), UnitConvert.MmToCm(dz));
        return FeatureX.Feature(moves.Add(definition));
    }
}

// ---------------- work geometry ----------------

public sealed class CreateWorkPointHandler : ExperimentalHandler
{
    public override string Name => "create_work_point";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var def = FeatureX.Def(app, Name);
        var point = def.WorkPoints.AddFixed(X.P3Mm(app, new[] { X.Num(p, "x_mm"), X.Num(p, "y_mm"), X.Num(p, "z_mm") }), false);
        if (!string.IsNullOrWhiteSpace((string?)p["name"])) point.Name = ((string)p["name"]!).Trim();
        return new JObject { ["name"] = point.Name };
    }
}

public sealed class CreateUcsHandler : ExperimentalHandler
{
    public override string Name => "create_ucs";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var def = FeatureX.Def(app, Name);
        var origin = X.Vec3(p, "origin_mm");
        var xAxis = X.Vec3(p, "x_axis");
        var yAxis = X.Vec3(p, "y_axis");
        double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        double Len(double[] a) => Math.Sqrt(Dot(a, a));
        if (Len(xAxis) < 1e-9 || Len(yAxis) < 1e-9 || Math.Abs(Dot(xAxis, yAxis)) > 1e-6 * Len(xAxis) * Len(yAxis))
            throw new ArgumentException("x_axis and y_axis must be non-zero and orthogonal.");
        var tg = app.TransientGeometry;
        var ux = tg.CreateVector(xAxis[0], xAxis[1], xAxis[2]); ux.Normalize();
        var uy = tg.CreateVector(yAxis[0], yAxis[1], yAxis[2]); uy.Normalize();
        var uz = ux.CrossProduct(uy);
        var matrix = tg.CreateMatrix();
        matrix.SetCoordinateSystem(X.P3Mm(app, origin), ux, uy, uz);
        dynamic systems = def.UserCoordinateSystems;
        dynamic definition = systems.CreateDefinition();
        definition.Transformation = matrix;
        dynamic ucs = systems.Add(definition);
        if (!string.IsNullOrWhiteSpace((string?)p["name"])) ucs.Name = ((string)p["name"]!).Trim();
        return new JObject { ["name"] = (string)ucs.Name };
    }
}

internal static class WorkGeometryX
{
    /// <summary>A work plane, axis, point or UCS by name in the active part or assembly.</summary>
    public static dynamic Find(global::Inventor.Document doc, string name)
    {
        dynamic def = doc switch
        {
            PartDocument part => part.ComponentDefinition,
            AssemblyDocument assembly => assembly.ComponentDefinition,
            _ => throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "Work geometry lives in parts and assemblies."),
        };
        foreach (dynamic collection in new object[] { def.WorkPlanes, def.WorkAxes, def.WorkPoints, def.UserCoordinateSystems })
            foreach (dynamic item in collection)
                if ((string)item.Name == name) return item;
        throw new ArgumentException("No work plane, axis, point or UCS named '" + name + "'.");
    }
}

public sealed class RenameWorkGeometryHandler : ExperimentalHandler
{
    public override string Name => "rename_work_geometry";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var item = WorkGeometryX.Find(X.Active(app), X.Str(p, "name"));
        item.Name = X.Str(p, "new_name");
        return new JObject { ["name"] = (string)item.Name };
    }
}

public sealed class DeleteWorkGeometryHandler : ExperimentalHandler
{
    public override string Name => "delete_work_geometry";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        string name = X.Str(p, "name");
        var item = WorkGeometryX.Find(X.Active(app), name);
        bool origin = false;
        try { origin = (bool)item.IsCoordinateSystemElement; } catch { /* UCS objects have no such flag */ }
        if (origin) throw new ArgumentException("'" + name + "' is origin geometry and cannot be deleted.");
        item.Delete();
        return new JObject { ["deleted"] = name };
    }
}

// ---------------- parameters and iProperties ----------------

public sealed class RenameParameterHandler : ExperimentalHandler
{
    public override string Name => "rename_parameter";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var parameters = X.ParametersOf(X.Active(app));
        string name = X.Str(p, "name"), newName = X.Str(p, "new_name");
        if (X.FindParameter(parameters, newName) != null) throw new ArgumentException("A parameter named '" + newName + "' already exists.");
        var parameter = X.FindParameter(parameters, name) ?? throw new ArgumentException("No parameter named '" + name + "'.");
        parameter.Name = newName;
        return new JObject { ["name"] = parameter.Name, ["expression"] = parameter.Expression };
    }
}

public sealed class DeleteParameterHandler : ExperimentalHandler
{
    public override string Name => "delete_parameter";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var parameters = X.ParametersOf(X.Active(app));
        string name = X.Str(p, "name");
        var parameter = X.FindParameter(parameters, name) ?? throw new ArgumentException("No parameter named '" + name + "'.");
        if (parameter is not UserParameter user) throw new ArgumentException("Only user parameters can be deleted; '" + name + "' is " + parameter.ParameterType + ".");
        if (parameter.Dependents.Count > 0) throw new ArgumentException("'" + name + "' has " + parameter.Dependents.Count + " dependents; remove them first.");
        user.Delete();
        return new JObject { ["deleted"] = name };
    }
}

/// <summary>set_document_iproperty on the ACTIVE document only (F15): writes to other documents are outside the transaction.</summary>
public sealed class SetIPropertyBatchHandler : ExperimentalHandler
{
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Part Number"] = "Design Tracking Properties", ["Description"] = "Design Tracking Properties",
        ["Stock Number"] = "Design Tracking Properties", ["Project"] = "Design Tracking Properties",
        ["Vendor"] = "Design Tracking Properties", ["Designer"] = "Design Tracking Properties",
        ["Revision Number"] = "Inventor Summary Information", ["Title"] = "Inventor Summary Information",
        ["Subject"] = "Inventor Summary Information", ["Author"] = "Inventor Summary Information",
        ["Keywords"] = "Inventor Summary Information", ["Comments"] = "Inventor Summary Information",
    };
    public override string Name => "set_document_iproperty";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Active(app);
        string name = X.Str(p, "name");
        if (!Allowed.TryGetValue(name, out var setName)) throw new ArgumentException("'" + name + "' is not a whitelisted iProperty: " + string.Join(", ", Allowed.Keys) + ".");
        string value = (string?)p["value"] ?? throw new ArgumentException("value is required.");
        if (value.Length > 1000) throw new ArgumentException("value is limited to 1000 characters.");
        var set = PropertyAccess.FindSet(doc, setName) ?? throw new InvalidOperationException("Property set " + setName + " not found.");
        var property = PropertyAccess.FindProperty(set, name) ?? throw new InvalidOperationException("Property " + name + " not found.");
        property.Value = value;
        return new JObject { ["name"] = name, ["value"] = value };
    }
}

// ---------------- document state ----------------

public sealed class SetVisibilityHandler : ExperimentalHandler
{
    public override string Name => "set_visibility";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Active(app);
        if (p["visible"]?.Type != JTokenType.Boolean) throw new ArgumentException("visible must be true or false.");
        bool visible = (bool)p["visible"]!;
        if (doc is PartDocument part && p["body_index"] != null)
        {
            var body = X.Body(part.ComponentDefinition, X.Int(p, "body_index", 1, 10000));
            body.Visible = visible;
            return new JObject { ["body"] = body.Name, ["visible"] = body.Visible };
        }
        if (doc is AssemblyDocument && p["occurrence_id"] != null)
        {
            var occurrence = X.Resolve(doc, X.Str(p, "occurrence_id"), out _) as ComponentOccurrence
                ?? throw new ArgumentException("occurrence_id is not an occurrence.");
            occurrence.Visible = visible;
            return new JObject { ["occurrence"] = occurrence.Name, ["visible"] = occurrence.Visible };
        }
        throw new ArgumentException("Use body_index in a part or occurrence_id in an assembly.");
    }
}

internal static class RepresentationX
{
    public static dynamic Manager(global::Inventor.Document doc) => doc switch
    {
        PartDocument part => part.ComponentDefinition.RepresentationsManager,
        AssemblyDocument assembly => assembly.ComponentDefinition.RepresentationsManager,
        _ => throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "Representations live in parts and assemblies."),
    };

    public static dynamic ModelStates(global::Inventor.Document doc) => doc switch
    {
        PartDocument part => ((dynamic)part.ComponentDefinition).ModelStates,
        AssemblyDocument assembly => ((dynamic)assembly.ComponentDefinition).ModelStates,
        _ => throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "Model states live in parts and assemblies."),
    };

    public static dynamic Named(dynamic collection, string name, string what)
    {
        foreach (dynamic item in collection)
            if ((string)item.Name == name) return item;
        throw new ArgumentException("No " + what + " named '" + name + "'.");
    }

    public static JArray Names(dynamic collection)
    {
        var names = new JArray();
        foreach (dynamic item in collection) names.Add((string)item.Name);
        return names;
    }
}

public sealed class ActivateDesignViewHandler : ExperimentalHandler
{
    public override string Name => "activate_design_view";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var manager = RepresentationX.Manager(X.Active(app));
        RepresentationX.Named(manager.DesignViewRepresentations, X.Str(p, "name"), "design view").Activate();
        return new JObject { ["active_design_view"] = (string)manager.ActiveDesignViewRepresentation.Name };
    }
}

public sealed class ActivateModelStateHandler : ExperimentalHandler
{
    public override string Name => "activate_model_state";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var states = RepresentationX.ModelStates(X.Active(app));
        RepresentationX.Named(states, X.Str(p, "name"), "model state").Activate();
        return new JObject { ["active_model_state"] = (string)states.ActiveModelState.Name };
    }
}

public sealed class CreateModelStateHandler : ExperimentalHandler
{
    public override string Name => "create_model_state";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var states = RepresentationX.ModelStates(X.Active(app));
        string name = X.Str(p, "name");
        foreach (dynamic state in states) if ((string)state.Name == name) throw new ArgumentException("Model state '" + name + "' already exists.");
        dynamic created = states.Add(name);
        return new JObject { ["model_state"] = (string)created.Name };
    }
}
#endif
