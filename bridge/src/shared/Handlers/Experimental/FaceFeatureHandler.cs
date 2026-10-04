#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// <c>face_feature</c> (M9 spec §4): the parametric feature that created a part face and its editable
/// parameters, for the Quest "double Trigger on a face" flow. Read-only: no transaction, no revision
/// change. The classification, units and JSON shape live in <see cref="FaceFeatureModel"/>; this class
/// only reads Inventor. Feature-specific properties go through <c>dynamic</c> (late binding) because
/// they are not yet confirmed against the installed interop: a wrong guess yields a feature with no
/// parameter for that role, never a build break (bridge/CLAUDE.md, Experimental tier).
/// </summary>
public sealed class FaceFeatureHandler : ExperimentalHandler
{
    public override string Name => "face_feature";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var active = X.Active(app);
        string id = EntityReferences.DocumentId(active);
        if (X.Str(p, "document_id") != id) throw ConcurrencyFailure.DocumentChanged((string?)p["document_id"], id);
        if (ctx.Events == null || X.Str(p, "expected_revision") != ctx.Events.Revision(id))
            throw ConcurrencyFailure.StaleRevision((string?)p["expected_revision"], ctx.Events?.Revision(id));
        var part = active as PartDocument
            ?? throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, Name + " needs an active part document.");

        var face = EntityReferences.ResolvePartEntityFace(active, X.Str(p, "face_id"));
        PartFeature? feature = null;
        try { feature = ((dynamic)face).CreatedByFeature as PartFeature; }
        catch { /* base body, derived or imported geometry: no owning feature */ }
        if (feature == null) throw FaceFeatureModel.NoOwningFeature();

        string objectType = feature.Type.ToString();
        if (FaceFeatureModel.IsBodyWithoutOperation(objectType)) throw FaceFeatureModel.NoOwningFeature();

        string name = feature.Name;
        bool suppressed = ReadBool(() => feature.Suppressed, false);
        bool healthy = ReadBool(() => feature.HealthStatus == HealthStatusEnum.kUpToDateHealth, false);
        string? previous = FaceFeatureModel.PreviousOf(FeatureNames(part.ComponentDefinition), name);

        string? type = FaceFeatureModel.SupportedType(objectType);
        if (type == null)
            throw FaceFeatureModel.Unsupported(name, FaceFeatureModel.GenericType(objectType), suppressed, healthy, previous);

        var parameters = FeatureParameterReader.Read(feature, type);
        return FaceFeatureModel.Result(name, type, suppressed, healthy, parameters, previous);
    }

    private static bool ReadBool(Func<bool> read, bool fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private static List<string> FeatureNames(PartComponentDefinition definition)
    {
        var names = new List<string>();
        foreach (PartFeature f in definition.Features)
        {
            try { names.Add(f.Name); }
            catch { /* an unreadable entry keeps its slot out of the order; never guessed */ }
        }
        return names;
    }
}

/// <summary>Reads the parameters of one supported feature type from Inventor (late bound).</summary>
internal static class FeatureParameterReader
{
    public static List<FaceFeatureModel.RawParameter> Read(PartFeature feature, string type)
    {
        var found = new List<FaceFeatureModel.RawParameter>();
        dynamic f = feature;
        switch (type)
        {
            case "extrude":
                AddDistanceExtent(found, Probe(() => f.Extent), FaceFeatureModel.Distance);
                break;
            case "revolve":
                if (Probe(() => f.Extent) is AngleExtent angleExtent) Add(found, FaceFeatureModel.Angle, Probe(() => angleExtent.Angle));
                break;
            case "fillet":
                foreach (var edgeSet in Items(Probe(() => f.Definition.EdgeSets) ?? Probe(() => f.Definition.ConstantRadiusEdgeSets)))
                    Add(found, FaceFeatureModel.Radius, Probe(() => ((dynamic)edgeSet).Radius));
                break;
            case "chamfer":
                foreach (var edgeSet in Items(Probe(() => f.Definition.ChamferEdgeSets) ?? Probe(() => f.Definition.EdgeSets)))
                {
                    dynamic set = edgeSet;
                    Add(found, FaceFeatureModel.Distance, Probe(() => set.Distance));
                    Add(found, FaceFeatureModel.Distance, Probe(() => set.DistanceOne));
                    Add(found, FaceFeatureModel.Distance, Probe(() => set.DistanceTwo));
                }
                break;
            case "hole":
                Add(found, FaceFeatureModel.Diameter, Probe(() => f.HoleDiameter));
                AddDistanceExtent(found, Probe(() => f.Extent), FaceFeatureModel.Depth);
                break;
            case "rectangular_pattern":
                Add(found, FaceFeatureModel.Count, Probe(() => f.XCount));
                Add(found, FaceFeatureModel.Spacing, Probe(() => f.XSpacing));
                break;
            case "circular_pattern":
                Add(found, FaceFeatureModel.Count, Probe(() => f.Count));
                Add(found, FaceFeatureModel.Angle, Probe(() => f.Angle));
                break;
            case "flange":
                AddDistanceExtent(found, Probe(() => f.Definition.HeightExtent), FaceFeatureModel.Distance);
                Add(found, FaceFeatureModel.Angle, Probe(() => f.Definition.Angle));
                break;
        }
        return found;
    }

    private static void AddDistanceExtent(List<FaceFeatureModel.RawParameter> found, object? extent, string role)
    {
        if (extent is DistanceExtent distance) Add(found, role, Probe(() => distance.Distance));
    }

    /// <summary>An Inventor model parameter (name, internal value, expression); silently skipped when unreadable.</summary>
    private static void Add(List<FaceFeatureModel.RawParameter> found, string role, object? parameter)
    {
        if (parameter == null) return;
        try
        {
            dynamic p = parameter;
            string name = p.Name;
            double value = Convert.ToDouble((object)p.Value);
            string? expression = null;
            try { expression = (string?)p.Expression; } catch { /* leave null: then not editable */ }
            found.Add(new FaceFeatureModel.RawParameter(name, role, value, expression));
        }
        catch { /* not a parameter on this Inventor build */ }
    }

    private static object? Probe(Func<object?> read)
    {
        try { return read(); }
        catch { return null; }
    }

    private static IEnumerable<object> Items(object? collection)
    {
        var items = new List<object>();
        if (collection is System.Collections.IEnumerable sequence)
        {
            try { foreach (var item in sequence) if (item != null) items.Add(item); }
            catch { /* partial list is better than none; unreadable sets stay out */ }
        }
        return items;
    }
}
#endif
