#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Handlers.SheetMetal;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// <c>get_flat_pattern_mesh</c>: tessellation of the existing flat pattern of the active (or named)
/// sheet-metal part, as its own <see cref="MeshPayload"/> body set (M5 spec, "Flat Pattern XR").
/// Read-only and never creates the flat pattern: a part without one is refused. The flat pattern is
/// read through late binding because <c>FlatPattern.SurfaceBodies</c>/<c>Body</c> are not confirmed
/// against the 2027 interop; the source actually used is reported. The edit state is recorded before
/// and after, and if the read left the document in flat-pattern edit it is exited again (fail safe).
/// The result carries no face or edge tokens: the flat mesh is a display asset, not a reference.
/// <c>flat_pattern_identity</c> is a cache-key proposal that the live probe validates.
/// </summary>
public sealed class FlatPatternMeshHandler : ExperimentalHandler
{
    public override string Name => "get_flat_pattern_mesh";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Document(app, (string?)p["document_id"]);
        if (doc is not PartDocument part || part.ComponentDefinition is not SheetMetalComponentDefinition def)
            throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                "A flat pattern mesh needs a sheet-metal part; an ordinary part or an assembly has none.");
        double toleranceMm = X.Num(p, "tolerance_mm", 0.1);
        if (toleranceMm < 0.01 || toleranceMm > 5) throw new ArgumentException("tolerance_mm must be between 0.01 and 5.");
        int maxTriangles = p["max_triangles"]?.Type == JTokenType.Integer ? (int)p["max_triangles"]! : 500_000;
        double toleranceCm = UnitConvert.MmToCm(toleranceMm);

        int bodyCount = def.SurfaceBodies.Count;
        if (bodyCount != 1)
            throw new CodedFailureException(InventorErrorCodes.INVALID_ARGUMENT,
                "A flat pattern exists only for a single-body part; this part has " + bodyCount + " bodies.",
                new JObject { ["reason"] = "MULTI_BODY_PART", ["body_count"] = bodyCount });
        // Never unfold here: the caller decides when the flat pattern is created (create_flat_pattern).
        if (!def.HasFlatPattern)
            throw new CodedFailureException(InventorErrorCodes.INVALID_ARGUMENT,
                "The part has no flat pattern; create it first with the create_flat_pattern batch command. It is not created implicitly.",
                new JObject { ["reason"] = "FLAT_PATTERN_MISSING" });

        string id = EntityReferences.DocumentId(doc);
        var before = EditState(app, part);
        bool forced = false;
        bool? restored = null;
        JObject after = before;
        JObject flatInfo;
        string? source = null;
        var attempts = new JArray();
        var bodies = new JArray();
        int triangles = 0;
        var vertexSet = new HashSet<(long, long, long)>();
        var meshHash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        double[] min = { double.MaxValue, double.MaxValue, double.MaxValue }, max = { double.MinValue, double.MinValue, double.MinValue };
        JToken rangeBox = JValue.CreateNull();

        try
        {
            flatInfo = SheetMetalSupport.DescribeFlatPattern(def);
            var sourceBodies = FindBodies(def, attempts, out source);
            if (sourceBodies.Count == 0)
                throw new CodedFailureException(InventorErrorCodes.API_ERROR,
                    "The flat pattern exposes no readable body through SurfaceBodies or Body.",
                    new JObject { ["reason"] = "FLAT_PATTERN_BODY_UNAVAILABLE", ["attempts"] = attempts });
            rangeBox = RangeBoxMm(sourceBodies);

            int bodyIndex = 0;
            foreach (var sourceBody in sourceBodies)
            {
                bodyIndex++;
                dynamic bodyDynamic = sourceBody;
                var positions = new List<float>();
                var normals = new List<float>();
                var indices = new List<uint>();
                var mesh = new MeshPayload.Body { Index = bodyIndex, Name = Attempt(() => (string)bodyDynamic.Name) ?? "flat_pattern", Visible = true };
                int ordinal = 0;
                foreach (var face in Enumerate((object?)bodyDynamic.Faces))
                {
                    ordinal++;
                    X.Deadline(ctx, "while tessellating the flat pattern");
                    // Same late-bound call as get_display_mesh: the typed one passes null SAFEARRAYs.
                    dynamic facetSource = face;
                    int vertexCount = 0, facetCount = 0;
                    double[] coordinates = new double[0], normalVectors = new double[0];
                    int[] vertexIndices = new int[0];
                    facetSource.CalculateFacets(toleranceCm, out vertexCount, out facetCount,
                        out coordinates, out normalVectors, out vertexIndices);
                    if (facetCount <= 0 || vertexCount <= 0) continue;
                    triangles += facetCount;
                    if (triangles > maxTriangles)
                        throw new CodedFailureException(InventorErrorCodes.MESH_TOO_LARGE,
                            "The mesh exceeds " + maxTriangles + " triangles; raise tolerance_mm or max_triangles.",
                            new JObject { ["max_triangles"] = maxTriangles, ["tolerance_mm"] = toleranceMm });
                    int indexBase = MeshPayload.DetectIndexBase(vertexIndices, vertexCount);
                    uint offset = (uint)(positions.Count / 3);
                    int first = indices.Count;
                    for (int i = 0; i < vertexCount * 3; i++)
                    {
                        positions.Add((float)coordinates[i]);
                        int axis = i % 3;
                        double mm = UnitConvert.CmToMm(coordinates[i]);
                        if (mm < min[axis]) min[axis] = mm;
                        if (mm > max[axis]) max[axis] = mm;
                    }
                    for (int v = 0; v < vertexCount; v++)
                        vertexSet.Add((Micrometres(coordinates[v * 3]), Micrometres(coordinates[v * 3 + 1]), Micrometres(coordinates[v * 3 + 2])));
                    bool hasNormals = normalVectors != null && normalVectors.Length >= vertexCount * 3;
                    for (int i = 0; i < vertexCount * 3; i++) normals.Add(hasNormals ? (float)normalVectors![i] : 0f);
                    for (int i = 0; i < facetCount * 3; i++) indices.Add(offset + (uint)(vertexIndices[i] - indexBase));
                    mesh.Faces.Add(new MeshPayload.FaceRange
                    {
                        FaceId = null,
                        Ordinal = ordinal,
                        FirstIndex = first,
                        IndexCount = facetCount * 3,
                        Surface = Attempt(() => ((object)facetSource.SurfaceType).ToString()),
                    });
                }
                mesh.Positions = positions.ToArray();
                mesh.Normals = normals.ToArray();
                mesh.Indices = indices.ToArray();
                Append(meshHash, mesh.Positions);
                Append(meshHash, mesh.Indices);
                bodies.Add(MeshPayload.ToJson(mesh));
            }
        }
        finally
        {
            // Fail safe, also when the read threw: leave the document as it was found.
            try
            {
                after = EditState(app, part);
                bool wasEditing = (bool?)before["flat_pattern_edit_active"] == true;
                if (!wasEditing && (bool?)after["flat_pattern_edit_active"] == true)
                {
                    forced = true;
                    try { def.FlatPattern.ExitEdit(); }
                    catch { /* reported through left_edit_mode_restored below */ }
                    after = EditState(app, part);
                    restored = (bool?)after["flat_pattern_edit_active"] != true;
                }
            }
            catch { /* the state probe itself is best effort */ }
        }

        if (triangles == 0)
            throw new CodedFailureException(InventorErrorCodes.API_ERROR, "The flat pattern produced no triangles.",
                new JObject { ["reason"] = "FLAT_PATTERN_MESH_EMPTY", ["source"] = source });

        double[] size = { max[0] - min[0], max[1] - min[1], max[2] - min[2] };
        string thinAxis = size[0] <= size[1] && size[0] <= size[2] ? "X" : size[1] <= size[2] ? "Y" : "Z";
        string revision = ctx.Events?.Revision(id) ?? "";
        string vertexSetHash = HashVertices(vertexSet);
        var identity = Identity(id, revision, flatInfo, toleranceMm, triangles, vertexSetHash);
        return new JObject
        {
            ["document_id"] = id,
            ["definition_name"] = doc.DisplayName + " (flat pattern)",
            ["revision"] = revision,
            ["visual_revision"] = ctx.Events?.VisualRevision(id),
            ["units"] = "cm",
            ["tolerance_mm"] = toleranceMm,
            ["source"] = source,
            ["source_attempts"] = attempts,
            ["thickness_mm"] = SheetMetalSupport.EffectiveThicknessMm(def),
            ["triangle_count"] = triangles,
            ["mesh_bbox_mm"] = new JObject
            {
                ["min_mm"] = new JArray(min[0], min[1], min[2]),
                ["max_mm"] = new JArray(max[0], max[1], max[2]),
                ["size_mm"] = new JArray(size[0], size[1], size[2]),
                ["thin_axis"] = thinAxis,
            },
            ["range_box_mm"] = rangeBox,
            ["flat_pattern"] = flatInfo,
            ["flat_pattern_identity"] = identity,
            ["mesh_hash"] = ToHex(meshHash.GetHashAndReset()),
            ["edit_state_before"] = before,
            ["edit_state_after"] = after,
            ["edit_mode_forced_by_read"] = forced,
            ["left_edit_mode_restored"] = restored.HasValue ? restored.Value : JValue.CreateNull(),
            ["bodies"] = bodies,
        };
    }

    // ---------------- geometry source ----------------

    /// <summary>
    /// The flat pattern's bodies: <c>SurfaceBodies</c> first, then <c>Body</c>. Every failed attempt is
    /// recorded so the probe can tell which of the two exists on 2027.
    /// </summary>
    private static List<object> FindBodies(SheetMetalComponentDefinition def, JArray attempts, out string? source)
    {
        source = null;
        dynamic flat = def.FlatPattern;
        try
        {
            var found = Enumerate((object?)flat.SurfaceBodies);
            attempts.Add(new JObject { ["source"] = "SurfaceBodies", ["bodies"] = found.Count });
            if (found.Count > 0) { source = "SurfaceBodies"; return found; }
        }
        catch (Exception ex) { attempts.Add(new JObject { ["source"] = "SurfaceBodies", ["error"] = Short(ex) }); }
        try
        {
            object? single = flat.Body;
            var found = single == null ? new List<object>() : new List<object> { single };
            attempts.Add(new JObject { ["source"] = "Body", ["bodies"] = found.Count });
            if (found.Count > 0) { source = "Body"; return found; }
        }
        catch (Exception ex) { attempts.Add(new JObject { ["source"] = "Body", ["error"] = Short(ex) }); }
        return new List<object>();
    }

    /// <summary>Items of a COM collection: as an enumerable when it is one, else by 1-based Count/Item.</summary>
    private static List<object> Enumerate(object? collection)
    {
        var items = new List<object>();
        if (collection == null) return items;
        if (collection is IEnumerable enumerable)
        {
            foreach (var item in enumerable) if (item != null) items.Add(item);
            return items;
        }
        dynamic dynamicCollection = collection;
        int count = (int)dynamicCollection.Count;
        for (int i = 1; i <= count; i++) items.Add((object)dynamicCollection.Item(i));
        return items;
    }

    private static JToken RangeBoxMm(List<object> sourceBodies)
    {
        try
        {
            double[] lo = { double.MaxValue, double.MaxValue, double.MaxValue }, hi = { double.MinValue, double.MinValue, double.MinValue };
            foreach (var body in sourceBodies)
            {
                dynamic box = ((dynamic)body).RangeBox;
                dynamic a = box.MinPoint, b = box.MaxPoint;
                double[] low = { (double)a.X, (double)a.Y, (double)a.Z }, high = { (double)b.X, (double)b.Y, (double)b.Z };
                for (int i = 0; i < 3; i++) { lo[i] = Math.Min(lo[i], low[i]); hi[i] = Math.Max(hi[i], high[i]); }
            }
            return new JObject
            {
                ["min_mm"] = new JArray(UnitConvert.CmToMm(lo[0]), UnitConvert.CmToMm(lo[1]), UnitConvert.CmToMm(lo[2])),
                ["max_mm"] = new JArray(UnitConvert.CmToMm(hi[0]), UnitConvert.CmToMm(hi[1]), UnitConvert.CmToMm(hi[2])),
            };
        }
        catch { return JValue.CreateNull(); }
    }

    // ---------------- edit state ----------------

    /// <summary>
    /// What the API exposes about edit mode, each read guarded (a missing member is "unavailable", never
    /// a failure). <c>flat_pattern_edit_active</c> is true when the activated or active edit object is the
    /// flat pattern, null when neither could be read.
    /// </summary>
    private static JObject EditState(Application app, PartDocument part)
    {
        string? activated = ObjectKind(() => ((dynamic)part).ActivatedObject);
        string? editing = ObjectKind(() => ((dynamic)app).ActiveEditObject);
        bool? active = activated == "unavailable" && editing == "unavailable"
            ? null
            : (bool?)((activated ?? "").Contains("FlatPattern") || (editing ?? "").Contains("FlatPattern"));
        return new JObject
        {
            ["activated_object"] = activated,
            ["active_edit_object"] = editing,
            ["flat_pattern_edit_active"] = active.HasValue ? active.Value : JValue.CreateNull(),
        };
    }

    /// <summary>ObjectTypeEnum name of the object a property returns, "none" for null, "unavailable" when it cannot be read.</summary>
    private static string ObjectKind(Func<object?> read)
    {
        try
        {
            var value = read();
            if (value == null) return "none";
            dynamic typed = value;
            return ((object)typed.Type).ToString() ?? value.GetType().Name;
        }
        catch { return "unavailable"; }
    }

    // ---------------- identity ----------------

    /// <summary>
    /// Cache-key proposal: <c>content_hash</c> covers what defines the blank (Inventor's length, width,
    /// bend count, alignment and the vertex set of the mesh); <c>hash</c> adds document id and revision.
    /// </summary>
    private static JObject Identity(string documentId, string revision, JObject flat, double toleranceMm, int triangles, string vertexSetHash)
    {
        string length = Mm(flat["length_mm"]), width = Mm(flat["width_mm"]);
        string bends = flat["bend_count"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
        string alignment = flat["alignment"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
        string content = string.Join("|", length, width, bends, alignment,
            toleranceMm.ToString("R", CultureInfo.InvariantCulture), vertexSetHash);
        string contentHash = Sha256(content);
        return new JObject
        {
            ["document_id"] = documentId,
            ["revision"] = revision,
            ["length_mm"] = flat["length_mm"]?.DeepClone(),
            ["width_mm"] = flat["width_mm"]?.DeepClone(),
            ["bend_count"] = flat["bend_count"]?.DeepClone(),
            ["alignment"] = flat["alignment"]?.DeepClone(),
            ["tolerance_mm"] = toleranceMm,
            ["triangle_count"] = triangles,
            ["vertex_set_hash"] = vertexSetHash,
            ["content_hash"] = contentHash,
            ["hash"] = Sha256(documentId + "|" + revision + "|" + contentHash),
        };
    }

    private static string Mm(JToken? value)
        => value != null && value.Type is JTokenType.Float or JTokenType.Integer
            ? Math.Round((double)value, 3).ToString("F3", CultureInfo.InvariantCulture)
            : "null";

    /// <summary>Order-independent: unique vertices on a 1 micrometre grid, sorted, hashed.</summary>
    private static string HashVertices(HashSet<(long, long, long)> vertices)
    {
        var sorted = vertices.OrderBy(v => v.Item1).ThenBy(v => v.Item2).ThenBy(v => v.Item3).ToArray();
        var bytes = new byte[sorted.Length * 24];
        for (int i = 0; i < sorted.Length; i++)
        {
            BitConverter.GetBytes(sorted[i].Item1).CopyTo(bytes, i * 24);
            BitConverter.GetBytes(sorted[i].Item2).CopyTo(bytes, i * 24 + 8);
            BitConverter.GetBytes(sorted[i].Item3).CopyTo(bytes, i * 24 + 16);
        }
        using var sha = System.Security.Cryptography.SHA256.Create();
        return ToHex(sha.ComputeHash(bytes));
    }

    private static long Micrometres(double centimetres) => (long)Math.Round(centimetres * 10000.0);

    private static void Append(System.Security.Cryptography.IncrementalHash hash, float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void Append(System.Security.Cryptography.IncrementalHash hash, uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        hash.AppendData(bytes);
    }

    private static string Sha256(string text)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static string Short(Exception ex)
    {
        string message = ex.Message ?? ex.GetType().Name;
        return message.Length > 160 ? message.Substring(0, 160) : message;
    }

    private static string? Attempt(Func<string?> read)
    {
        try { return read(); }
        catch { return null; }
    }
}
#endif
