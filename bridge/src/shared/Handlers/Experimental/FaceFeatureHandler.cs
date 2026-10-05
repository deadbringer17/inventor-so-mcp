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
        // Live-verified (M3LiveProbe --face-feature): hole walls report no CreatedByFeature. Fall back to the feature
        // whose own face collections (Faces, SideFaces, EndFaces, StartFaces) contain this face; latest feature first.
        feature ??= FeatureOwningFace(part.ComponentDefinition, face);
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

    private static PartFeature? FeatureOwningFace(PartComponentDefinition definition, object face)
    {
        for (int i = definition.Features.Count; i >= 1; i--)
        {
            PartFeature candidate;
            try { candidate = definition.Features[i]; } catch { continue; }
            dynamic f = candidate;
            foreach (var faces in new Func<object?>[] { () => f.Faces, () => f.SideFaces, () => f.EndFaces, () => f.StartFaces })
            {
                try
                {
                    if (faces() is System.Collections.IEnumerable sequence)
                        foreach (var item in sequence)
                            if (ReferenceEquals(item, face) || Equals(item, face)) return candidate;
                }
                catch { /* this feature type has no such collection */ }
            }
        }
        return null;
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
                // FilletFeature.FilletDefinition.EdgeSetItem(i) (1-based); only constant-radius sets carry a Radius parameter.
                int edgeSets = Convert.ToInt32(Probe(() => f.FilletDefinition.EdgeSetCount) ?? 0);
                for (int i = 1; i <= edgeSets; i++)
                {
                    int index = i;
                    var set = Probe(() => f.FilletDefinition.EdgeSetItem(index));
                    if (set is FilletConstantRadiusEdgeSet constant) Add(found, FaceFeatureModel.Radius, Probe(() => constant.Radius));
                }
                break;
            case "chamfer":
                // ChamferFeature.Definition exposes the active parameters; unused ones throw and are skipped.
                Add(found, FaceFeatureModel.Distance, Probe(() => f.Definition.Distance));
                Add(found, FaceFeatureModel.Distance, Probe(() => f.Definition.DistanceOne));
                Add(found, FaceFeatureModel.Distance, Probe(() => f.Definition.DistanceTwo));
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
                Add(found, FaceFeatureModel.Angle, Probe(() => f.Definition.FlangeAngle));
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
