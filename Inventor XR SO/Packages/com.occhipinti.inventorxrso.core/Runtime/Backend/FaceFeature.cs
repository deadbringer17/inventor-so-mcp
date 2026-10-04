using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class FeatureParameter
    {
        public string Name { get; set; }
        public string Role { get; set; }
        public string Unit { get; set; }
        public string Expression { get; set; }
        public double Value { get; set; }
        public bool Editable { get; set; }
    }

    /// <summary>The feature that owns a face, with its editable driving parameters (tool inventor_face_feature, experimental).</summary>
    public sealed class FaceFeatureInfo
    {
        public const string UnsupportedCode = "UNSUPPORTED_FEATURE";
        public const string NoOwningFeatureCode = "NO_OWNING_FEATURE";

        public string FeatureName { get; private set; }
        public string FeatureType { get; private set; }
        public string PreviousFeature { get; private set; }
        public bool Suppressed { get; private set; }
        public bool Healthy { get; private set; } = true;
        /// <summary>False when the feature type is outside the supported table: name and type are kept, parameters are empty.</summary>
        public bool Supported { get; private set; } = true;
        public IReadOnlyList<FeatureParameter> Parameters { get; private set; } = Array.Empty<FeatureParameter>();
        public bool CanEdit => Supported && !Suppressed && Healthy && Parameters.Any(p => p.Editable);

        public static FaceFeatureInfo FromJson(JObject json)
        {
            var feature = json?["feature"] as JObject;
            var parameters = (json?["parameters"] as JArray ?? new JArray()).OfType<JObject>().Select(p => new FeatureParameter
            {
                Name = (string)p["name"], Role = (string)p["role"], Unit = (string)p["unit"], Expression = (string)p["expression"],
                Value = (double?)p["value"] ?? 0, Editable = (bool?)p["editable"] ?? false
            }).ToArray();
            return new FaceFeatureInfo
            {
                FeatureName = (string)feature?["name"], FeatureType = (string)feature?["type"],
                Suppressed = (bool?)feature?["suppressed"] ?? false, Healthy = (bool?)feature?["healthy"] ?? true,
                PreviousFeature = (string)json?["previous_feature"], Parameters = parameters
            };
        }

        /// <summary>Builds the result for an UNSUPPORTED_FEATURE error payload; name and type are read from the error details.</summary>
        public static FaceFeatureInfo FromUnsupported(JObject details)
        {
            var feature = details?["feature"] as JObject ?? details?["details"]?["feature"] as JObject ?? details?["details"] as JObject ?? details;
            return new FaceFeatureInfo
            {
                FeatureName = (string)feature?["name"], FeatureType = (string)feature?["type"],
                PreviousFeature = (string)(details?["previous_feature"] ?? feature?["previous_feature"]),
                Suppressed = (bool?)feature?["suppressed"] ?? false, Healthy = (bool?)feature?["healthy"] ?? true,
                Supported = false
            };
        }

        /// <summary>UNSUPPORTED_FEATURE becomes an info with Supported=false; every other error (NO_OWNING_FEATURE, STALE_REVISION…) is rethrown.</summary>
        public static FaceFeatureInfo FromToolError(McpToolException ex)
        {
            if (ex.Code == UnsupportedCode) return FromUnsupported(ex.Details);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
            return null;
        }
    }
}
