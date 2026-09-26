#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// Base of every experimental-tier handler (docs/INVENTOR_SO_MCP_IMPLEMENTATION_PLAN.md §26.3):
/// compiled only with <c>-p:SoExperimental=true</c> and refused at run time unless the add-in was
/// started with <c>INVENTOR_SO_EXPERIMENTAL=1</c>. Argument errors surface as INVALID_ARGUMENT,
/// coded failures under their own code, anything else as a sanitized API_ERROR.
/// </summary>
public abstract class ExperimentalHandler : HandlerBase, IInventorCommand
{
    public abstract string Name { get; }
    public abstract bool IsReadOnly { get; }

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ctx.AllowExperimental)
            return Fail(ctx, InventorErrorCodes.EXPERIMENTAL_DISABLED,
                Name + " is experimental (implemented, not yet live-verified); start Inventor with INVENTOR_SO_EXPERIMENTAL=1 to use it.");
        try { return Ok(ctx, Run(ctx, (Application)ctx.Application!, p)); }
        catch (CodedFailureException failure) { return Fail(ctx, failure); }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (TimeoutException ex) { return Fail(ctx, InventorErrorCodes.TIMEOUT, ex.Message); }
    }

    protected abstract JToken Run(InventorCommandContext ctx, Application app, JObject p);
}

internal static class X
{
    // ---------------- documents ----------------

    public static global::Inventor.Document Active(Application app)
        => app.ActiveDocument ?? throw new CodedFailureException(InventorErrorCodes.NO_DOCUMENT, "No active Inventor document.");

    /// <summary>An open document by id (default the active one). Duplicate ids are refused, never guessed.</summary>
    public static global::Inventor.Document Document(Application app, string? documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return Active(app);
        var matches = new List<global::Inventor.Document>();
        foreach (global::Inventor.Document document in app.Documents)
            if (EntityReferences.DocumentId(document) == documentId) matches.Add(document);
        if (matches.Count == 0) throw new CodedFailureException(InventorErrorCodes.NO_DOCUMENT, "Document " + documentId + " is not open.");
        if (matches.Count > 1) throw new ArgumentException("Document id " + documentId + " is ambiguous.");
        return matches[0];
    }

    public static string Kind(global::Inventor.Document doc) => doc.DocumentType switch
    {
        DocumentTypeEnum.kPartDocumentObject => CadDocumentKinds.Part,
        DocumentTypeEnum.kAssemblyDocumentObject => CadDocumentKinds.Assembly,
        DocumentTypeEnum.kDrawingDocumentObject => CadDocumentKinds.Drawing,
        _ => "other",
    };

    public static PartDocument ActivePart(Application app, string command)
        => Active(app) as PartDocument ?? throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, command + " needs an active part document.");

    public static AssemblyDocument ActiveAssembly(Application app, string command)
        => Active(app) as AssemblyDocument ?? throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, command + " needs an active assembly document.");

    public static DrawingDocument ActiveDrawing(Application app, string command)
        => Active(app) as DrawingDocument ?? throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, command + " needs an active drawing document.");

    /// <summary>Parameters of the active part or assembly.</summary>
    public static Parameters ParametersOf(global::Inventor.Document doc) => doc switch
    {
        PartDocument part => part.ComponentDefinition.Parameters,
        AssemblyDocument assembly => assembly.ComponentDefinition.Parameters,
        _ => throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "Parameters exist in part and assembly documents."),
    };

    public static Parameter? FindParameter(Parameters parameters, string name)
    {
        foreach (Parameter parameter in parameters)
            if (string.Equals(parameter.Name, name, StringComparison.Ordinal)) return parameter;
        return null;
    }

    // ---------------- entities ----------------

    /// <summary>
    /// Resolve any portable id (ent_...) to its object in <paramref name="doc"/>: the same
    /// ReferenceKeyManager round trip as EntityReferences, without its per-type restriction.
    /// Ambiguous and unresolved references are refused.
    /// </summary>
    public static object Resolve(global::Inventor.Document doc, string? id, out string type)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An entity id is required.");
        var reference = PersistentEntityReference.Decode(id!);
        if (reference.DocumentId != EntityReferences.DocumentId(doc))
            throw new ArgumentException("REFERENCE_DOCUMENT_MISMATCH: entity belongs to another document.");
        type = reference.EntityType;
        var manager = doc.ReferenceKeyManager;
        var bytes = reference.Context;
        int context = manager.LoadContextFromArray(ref bytes);
        try
        {
            var key = reference.Key;
            object entity;
            object details;
            if (!manager.CanBindKeyToObject(ref key, context, out entity, out details))
                throw new ArgumentException("REFERENCE_UNRESOLVED: the entity no longer exists or was changed; inspect the model again.");
            if (entity is ObjectCollection)
                throw new ArgumentException("REFERENCE_AMBIGUOUS: refusing to choose an entity.");
            return entity;
        }
        finally { manager.ReleaseKeyContext(context); }
    }

    public static string? Describe(global::Inventor.Document doc, object entity)
    {
        var described = EntityReferences.Describe(doc, entity);
        return (bool?)described["supported"] == true ? (string?)described["id"] : null;
    }

    /// <summary>Solid body by 1-based index.</summary>
    public static SurfaceBody Body(PartComponentDefinition def, int index)
    {
        var bodies = def.SurfaceBodies;
        if (index < 1 || index > bodies.Count) throw new ArgumentException("body index " + index + " is outside 1.." + bodies.Count + ".");
        return bodies[index];
    }

    /// <summary>A named sketch of the part, or the most recent when name is empty.</summary>
    public static PlanarSketch Sketch(PartComponentDefinition def, string? name)
        => Bimwright.Ipt.Shared.Handlers.Sketch.SketchSupport.ResolveTargetSketch(def, name);

    /// <summary>A work plane by origin alias (XY/XZ/YZ) or name.</summary>
    public static WorkPlane Plane(PartComponentDefinition def, string reference)
        => Bimwright.Ipt.Shared.Handlers.Feature.FeatureSupport.ResolvePlaneRef(def, reference) as WorkPlane
           ?? throw new ArgumentException("'" + reference + "' is not a work plane.");

    // ---------------- arguments ----------------

    public static string Str(JObject p, string name)
    {
        var value = (string?)p[name];
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required.");
        return value!.Trim();
    }

    public static double Num(JObject p, string name)
    {
        var token = p[name];
        if (token == null || token.Type is not (JTokenType.Integer or JTokenType.Float))
            throw new ArgumentException(name + " must be a number.");
        double value = (double)token;
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException(name + " must be finite.");
        return value;
    }

    public static double Num(JObject p, string name, double fallback) => p[name] == null || p[name]!.Type == JTokenType.Null ? fallback : Num(p, name);

    public static double Positive(JObject p, string name)
    {
        double value = Num(p, name);
        if (value <= 0) throw new ArgumentException(name + " must be greater than 0.");
        return value;
    }

    public static int Int(JObject p, string name, int min, int max)
    {
        var token = p[name];
        if (token == null || token.Type != JTokenType.Integer) throw new ArgumentException(name + " must be an integer.");
        int value = (int)token;
        if (value < min || value > max) throw new ArgumentException(name + " must be between " + min + " and " + max + ".");
        return value;
    }

    public static bool Bool(JObject p, string name, bool fallback)
        => p[name]?.Type == JTokenType.Boolean ? (bool)p[name]! : fallback;

    public static string[] Strings(JObject p, string name, int max = 256)
    {
        if (p[name] is not JArray array || array.Count == 0) throw new ArgumentException(name + " must be a non-empty array.");
        if (array.Count > max) throw new ArgumentException(name + " accepts at most " + max + " items.");
        return array.Select(t => t.Type == JTokenType.String ? (string)t! : throw new ArgumentException(name + " items must be strings.")).ToArray();
    }

    public static int[] Ints(JObject p, string name, int max = 256)
    {
        if (p[name] is not JArray array || array.Count == 0) throw new ArgumentException(name + " must be a non-empty array.");
        if (array.Count > max) throw new ArgumentException(name + " accepts at most " + max + " items.");
        return array.Select(t => t.Type == JTokenType.Integer ? (int)t : throw new ArgumentException(name + " items must be integers.")).ToArray();
    }

    public static double[] Vec3(JObject p, string name)
    {
        if (p[name] is not JArray array || array.Count != 3 || array.Any(t => t.Type is not (JTokenType.Integer or JTokenType.Float)))
            throw new ArgumentException(name + " must be [x, y, z].");
        var values = array.Select(t => (double)t).ToArray();
        if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v))) throw new ArgumentException(name + " must be finite.");
        return values;
    }

    public static double[][] Points(JObject p, string name, int min, int max)
    {
        if (p[name] is not JArray array || array.Count < min || array.Count > max)
            throw new ArgumentException(name + " needs " + min + " to " + max + " points.");
        return array.Select(t => t is JArray xy && xy.Count >= 2 && xy.Take(2).All(v => v.Type is JTokenType.Integer or JTokenType.Float)
            ? new[] { (double)xy[0], (double)xy[1] }
            : throw new ArgumentException(name + " items must be [x, y].")).ToArray();
    }

    // ---------------- geometry ----------------

    public static Point2d P2(Application app, double xMm, double yMm)
        => app.TransientGeometry.CreatePoint2d(UnitConvert.MmToCm(xMm), UnitConvert.MmToCm(yMm));

    public static Point P3Mm(Application app, double[] mm)
        => app.TransientGeometry.CreatePoint(UnitConvert.MmToCm(mm[0]), UnitConvert.MmToCm(mm[1]), UnitConvert.MmToCm(mm[2]));

    public static JArray Mm(Point point) => new(UnitConvert.CmToMm(point.X), UnitConvert.CmToMm(point.Y), UnitConvert.CmToMm(point.Z));

    public static JArray Mm2(Point2d point) => new(UnitConvert.CmToMm(point.X), UnitConvert.CmToMm(point.Y));

    public static JObject Box(Box box) => new()
    {
        ["min_mm"] = Mm(box.MinPoint),
        ["max_mm"] = Mm(box.MaxPoint),
    };

    /// <summary>Row-major 4x4 of an Inventor matrix (translation in centimetres).</summary>
    public static JArray RowMajor(Matrix matrix)
    {
        var values = new JArray();
        for (int row = 1; row <= 4; row++)
            for (int column = 1; column <= 4; column++)
                values.Add(matrix.get_Cell(row, column));
        return values;
    }

    public static PartFeatureOperationEnum Operation(string? op) => Bimwright.Ipt.Shared.Handlers.Feature.FeatureSupport.Operation(op);

    public static PartFeatureExtentDirectionEnum Direction(string? dir) => Bimwright.Ipt.Shared.Handlers.Feature.FeatureSupport.Direction(dir);

    /// <summary>Solid profile of a named sketch, created when the sketch has none yet.</summary>
    public static Profile Profile(PartComponentDefinition def, string sketchName)
        => Bimwright.Ipt.Shared.Handlers.Feature.FeatureSupport.SolidProfile(def, sketchName);

    public static void Deadline(InventorCommandContext ctx, string what)
    {
        if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Deadline exceeded " + what + ".");
    }
}
#endif
