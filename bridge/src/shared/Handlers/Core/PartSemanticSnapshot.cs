#if INVENTOR2027
using System;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Core;

internal static class PartSemanticSnapshot
{
    public static JObject Capture(PartDocument part, Func<bool>? expired = null)
    {
        if (part.RequiresUpdate) throw new InvalidOperationException("MODEL_REQUIRES_UPDATE: rebuild before snapshot/diff.");
        bool dirty = part.Dirty;
        string version = part.ComponentDefinition.ModelGeometryVersion;
        var parameters = new JArray(); var features = new JArray();
        void Check(int count)
        {
            if (count > 10000) throw new InvalidOperationException("Snapshot entity limit exceeded.");
            if (expired?.Invoke() == true) throw new TimeoutException("Expired during semantic capture.");
        }
        foreach (Parameter parameter in part.ComponentDefinition.Parameters)
        {
            Check(parameters.Count + 1);
            parameters.Add(new JObject { ["name"] = parameter.Name, ["expression"] = parameter.Expression,
                ["declared_units"] = parameter.get_Units(), ["kind"] = parameter.ParameterType.ToString(),
                ["database_value"] = JToken.FromObject(parameter.Value) });
        }
        foreach (PartFeature feature in part.ComponentDefinition.Features)
        {
            Check(features.Count + 1);
            features.Add(new JObject { ["name"] = feature.Name, ["type"] = feature.Type.ToString(),
                ["suppressed"] = feature.Suppressed, ["health"] = feature.HealthStatus.ToString() });
        }
        var mass = part.ComponentDefinition.MassProperties;
        bool cache = mass.CacheResultsOnCompute;
        JObject physical;
        try
        {
            mass.CacheResultsOnCompute = false;
            var center = mass.CenterOfMass;
            physical = new JObject { ["mass_kg"] = mass.Mass, ["volume_mm3"] = mass.Volume * 1000, ["area_mm2"] = mass.Area * 100,
                ["center_x_mm"] = center.X * 10, ["center_y_mm"] = center.Y * 10, ["center_z_mm"] = center.Z * 10 };
        }
        finally { mass.CacheResultsOnCompute = cache; }
        Check(0);
        if (dirty != part.Dirty || version != part.ComponentDefinition.ModelGeometryVersion)
            throw new InvalidOperationException("DOCUMENT_CHANGED_DURING_CAPTURE");
        return new JObject { ["schema_version"] = 1, ["document_id"] = "doc_" + part.InternalName,
            ["parameters"] = parameters, ["features"] = features, ["physical"] = physical,
            ["database_units"] = "Inventor internal units: cm, kg, rad and derived combinations; text/boolean retain their types" };
    }
}
#endif
