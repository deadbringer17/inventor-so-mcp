using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Backend
{
    public enum SketchImportResourceKind { Image, Points, Acad }

    /// <summary>Declared metadata only. No content decoding, host lookup or CAD capability is implied.</summary>
    public sealed class SketchImportResource
    {
        public SketchImportResource(string assetId, SketchImportResourceKind kind, string format, long byteLength,
            IEnumerable<string> dependencies = null, SketchImportImageMetadata image = null,
            SketchImportGeometryMetadata geometry = null)
        {
            AssetId = assetId; Kind = kind; Format = format; ByteLength = byteLength;
            Dependencies = Array.AsReadOnly((dependencies ?? Array.Empty<string>()).ToArray());
            Image = image; Geometry = geometry;
        }
        public string AssetId { get; }
        public SketchImportResourceKind Kind { get; }
        /// <summary>Policy-defined format token, not a filename or MIME-sniffing result.</summary>
        public string Format { get; }
        public long ByteLength { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public SketchImportImageMetadata Image { get; }
        public SketchImportGeometryMetadata Geometry { get; }
    }

    public sealed class SketchImportImageMetadata
    {
        public SketchImportImageMetadata(int pixelWidth, int pixelHeight)
        { PixelWidth = pixelWidth; PixelHeight = pixelHeight; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
    }

    public sealed class SketchImportBounds
    {
        public SketchImportBounds(double minX, double minY, double maxX, double maxY)
        { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
    }

    public sealed class SketchImportGeometryMetadata
    {
        public SketchImportGeometryMetadata(string unit, double millimetersPerUnit, long? pointCount = null,
            SketchImportBounds bounds = null)
        { Unit = unit; MillimetersPerUnit = millimetersPerUnit; PointCount = pointCount; Bounds = bounds; }
        public string Unit { get; }
        public double MillimetersPerUnit { get; }
        /// <summary>Required for Points; absent for Acad. Does not replace decoding and validating each point.</summary>
        public long? PointCount { get; }
        /// <summary>Optional declared 2D bounds in source units; Z handling belongs to the import command.</summary>
        public SketchImportBounds Bounds { get; }
    }

    public sealed class SketchImportFormat
    {
        public SketchImportFormat(SketchImportResourceKind kind, string format)
        { Kind = kind; Format = format; }
        public SketchImportResourceKind Kind { get; }
        public string Format { get; }
    }

    /// <summary>Application limits and allow-lists supplied explicitly by the caller; no desktop limits or defaults.</summary>
    public sealed class SketchImportResourcePolicy
    {
        private readonly HashSet<(SketchImportResourceKind, string)> _formats = new HashSet<(SketchImportResourceKind, string)>();
        private readonly Dictionary<string, double> _units = new Dictionary<string, double>(StringComparer.Ordinal);
        public SketchImportResourcePolicy(int maxResources, int maxDependenciesPerResource, int maxTokenLength,
            long maxBytesPerResource, long maxTotalBytes, long maxImagePixels, long maxPoints,
            double maxAbsoluteCoordinateMm, IEnumerable<SketchImportFormat> formats,
            IEnumerable<KeyValuePair<string, double>> units)
        {
            if (maxResources < 1 || maxDependenciesPerResource < 0 || maxTokenLength < 1
                || maxBytesPerResource < 1 || maxTotalBytes < 1 || maxImagePixels < 1 || maxPoints < 1
                || !SketchImportResourceValidator.PositiveFinite(maxAbsoluteCoordinateMm))
                throw new ArgumentException("Import resource policy limits must be explicit and positive (dependency limit may be zero).");
            MaxResources = maxResources; MaxDependenciesPerResource = maxDependenciesPerResource; MaxTokenLength = maxTokenLength;
            MaxBytesPerResource = maxBytesPerResource; MaxTotalBytes = maxTotalBytes; MaxImagePixels = maxImagePixels;
            MaxPoints = maxPoints; MaxAbsoluteCoordinateMm = maxAbsoluteCoordinateMm;
            if (formats == null || units == null) throw new ArgumentNullException(formats == null ? nameof(formats) : nameof(units));
            foreach (var format in formats)
            {
                if (format == null || !Enum.IsDefined(typeof(SketchImportResourceKind), format.Kind)
                    || !SketchImportResourceValidator.Token(format.Format, maxTokenLength)
                    || !_formats.Add((format.Kind, format.Format)))
                    throw new ArgumentException("Import format allow-list contains an invalid or duplicate entry.", nameof(formats));
            }
            if (_formats.Count == 0) throw new ArgumentException("Declare at least one allowed import format.", nameof(formats));
            foreach (var unit in units)
            {
                if (!SketchImportResourceValidator.Token(unit.Key, maxTokenLength)
                    || !SketchImportResourceValidator.PositiveFinite(unit.Value) || _units.ContainsKey(unit.Key))
                    throw new ArgumentException("Import unit allow-list contains an invalid or duplicate entry.", nameof(units));
                _units.Add(unit.Key, unit.Value);
            }
        }
        public int MaxResources { get; }
        public int MaxDependenciesPerResource { get; }
        public int MaxTokenLength { get; }
        public long MaxBytesPerResource { get; }
        public long MaxTotalBytes { get; }
        public long MaxImagePixels { get; }
        public long MaxPoints { get; }
        public double MaxAbsoluteCoordinateMm { get; }
        internal bool Allows(SketchImportResourceKind kind, string format) => _formats.Contains((kind, format));
        internal bool AllowsUnit(string unit, double factor) => unit != null && _units.TryGetValue(unit, out var expected) && expected == factor;
    }

    public sealed class SketchImportResourceBundle
    {
        public SketchImportResourceBundle(string rootAssetId, IEnumerable<SketchImportResource> resources)
        {
            RootAssetId = rootAssetId;
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            Resources = Array.AsReadOnly(resources.ToArray());
        }
        public string RootAssetId { get; }
        public IReadOnlyList<SketchImportResource> Resources { get; }
    }

    public static class SketchImportResourceValidator
    {
        internal static bool PositiveFinite(double value) => value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
        internal static bool Token(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
            return true;
        }

        /// <summary>Checks immutable declarations, not actual bytes, authorization, native import support or CAD results.</summary>
        public static void Validate(SketchImportResourceBundle bundle, SketchImportResourcePolicy policy)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (!Token(bundle.RootAssetId, policy.MaxTokenLength) || bundle.Resources.Count < 1 || bundle.Resources.Count > policy.MaxResources)
                throw new ArgumentException("Invalid import bundle root or resource count.", nameof(bundle));
            var resources = new Dictionary<string, SketchImportResource>(StringComparer.Ordinal);
            long total = 0;
            foreach (var resource in bundle.Resources)
            {
                if (resource == null || !Token(resource.AssetId, policy.MaxTokenLength) || resources.ContainsKey(resource.AssetId))
                    throw new ArgumentException("Import resources must have unique opaque asset IDs.", nameof(bundle));
                if (!Enum.IsDefined(typeof(SketchImportResourceKind), resource.Kind)
                    || !Token(resource.Format, policy.MaxTokenLength) || !policy.Allows(resource.Kind, resource.Format))
                    throw new ArgumentException("Resource kind and format are not in the explicit import policy.", nameof(bundle));
                if (resource.ByteLength < 1 || resource.ByteLength > policy.MaxBytesPerResource
                    || resource.ByteLength > policy.MaxTotalBytes - total)
                    throw new ArgumentException("Declared import byte length exceeds policy or is empty.", nameof(bundle));
                total += resource.ByteLength;
                if (resource.Dependencies.Count > policy.MaxDependenciesPerResource)
                    throw new ArgumentException("Import resource dependency count exceeds policy.", nameof(bundle));
                var unique = new HashSet<string>(StringComparer.Ordinal);
                foreach (string dependency in resource.Dependencies)
                    if (!Token(dependency, policy.MaxTokenLength) || !unique.Add(dependency))
                        throw new ArgumentException("Dependency IDs must be opaque and unique within each resource.", nameof(bundle));
                ValidateMetadata(resource, policy);
                resources.Add(resource.AssetId, resource);
            }
            if (!resources.ContainsKey(bundle.RootAssetId)) throw new ArgumentException("Import root resource is missing.", nameof(bundle));
            var indegree = resources.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
            foreach (var resource in resources.Values)
                foreach (string dependency in resource.Dependencies)
                {
                    if (!resources.ContainsKey(dependency)) throw new ArgumentException("Import dependency resource is missing.", nameof(bundle));
                    indegree[dependency]++;
                }
            // Iterative traversal avoids a call-stack overflow on a caller-permitted deep graph.
            var queue = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            int visited = 0;
            while (queue.Count > 0)
            {
                string id = queue.Dequeue(); visited++;
                foreach (string dependency in resources[id].Dependencies)
                    if (--indegree[dependency] == 0) queue.Enqueue(dependency);
            }
            if (visited != resources.Count) throw new ArgumentException("Import dependencies contain a cycle.", nameof(bundle));
            var reachable = new HashSet<string>(StringComparer.Ordinal) { bundle.RootAssetId };
            queue.Enqueue(bundle.RootAssetId);
            while (queue.Count > 0)
                foreach (string dependency in resources[queue.Dequeue()].Dependencies)
                    if (reachable.Add(dependency)) queue.Enqueue(dependency);
            if (reachable.Count != resources.Count) throw new ArgumentException("Import bundle contains resources unrelated to its root.", nameof(bundle));
        }

        private static void ValidateMetadata(SketchImportResource resource, SketchImportResourcePolicy policy)
        {
            if (resource.Kind == SketchImportResourceKind.Image)
            {
                if (resource.Geometry != null || resource.Image == null || resource.Image.PixelWidth < 1 || resource.Image.PixelHeight < 1
                    || (long)resource.Image.PixelWidth * resource.Image.PixelHeight > policy.MaxImagePixels)
                    throw new ArgumentException("Image metadata requires positive pixel dimensions within policy and no geometry metadata.");
                return;
            }
            var geometry = resource.Geometry;
            if (resource.Image != null || geometry == null || !Token(geometry.Unit, policy.MaxTokenLength)
                || !PositiveFinite(geometry.MillimetersPerUnit) || !policy.AllowsUnit(geometry.Unit, geometry.MillimetersPerUnit))
                throw new ArgumentException("Geometry metadata requires an explicit policy-approved source unit and millimeter conversion.");
            if (resource.Kind == SketchImportResourceKind.Points)
            {
                if (!geometry.PointCount.HasValue || geometry.PointCount.Value < 1 || geometry.PointCount.Value > policy.MaxPoints)
                    throw new ArgumentException("Point metadata requires a positive point count within policy.");
            }
            else if (geometry.PointCount.HasValue) throw new ArgumentException("ACAD metadata must not masquerade as point-list metadata.");
            var bounds = geometry.Bounds;
            if (bounds == null) return;
            if (bounds.MinX > bounds.MaxX || bounds.MinY > bounds.MaxY)
                throw new ArgumentException("Declared import bounds are reversed.");
            foreach (double coordinate in new[] { bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY })
            {
                double mm = coordinate * geometry.MillimetersPerUnit;
                if (double.IsNaN(mm) || double.IsInfinity(mm) || Math.Abs(mm) > policy.MaxAbsoluteCoordinateMm)
                    throw new ArgumentException("Declared import bounds are non-finite or exceed the coordinate policy.");
            }
        }
    }
}
