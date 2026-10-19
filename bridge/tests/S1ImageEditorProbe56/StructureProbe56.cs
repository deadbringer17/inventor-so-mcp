using Bimwright.Ipt.Server.Imports;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static string Reference56(string documentId, object entity, string kind) => new PersistentEntityReference {
        DocumentId = documentId, EntityType = kind, Key = Convert.FromBase64String(Identity(entity)), Context = Array.Empty<byte>() }.Encode();

    private static void RunStructure56(Inventor.Application app, PartDocument doc, PlanarSketch sketch, InventorBackend backend,
        ProbeTransport transport, PairedServer paired, ImportResourceStore imports, string scratch, JObject report, JArray cases, bool sheet,
        TargetDescriptor expectedTarget, string descriptors, PluginClient plugin)
    {
        string kind = sheet ? "sheetmetal" : "part";
        report["phase"] = kind + "_structure_fixture";
        string documentId = DocumentIdComposer.Compose(doc.InternalName, doc.FullFileName, false);
        string sketchId = Reference56(documentId, sketch, "sketch");
        var mcp = new McpClient(transport, paired.BaseUrl, paired.Token);
        mcp.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        string source = Path.Combine(scratch, "structure-source-" + kind + ".bmp"); WriteBmp(source);
        byte[] sourceBytes = File.ReadAllBytes(source);
        ImportResourceRecord resource;
        try
        {
            resource = imports.Put(paired.ClientName, sourceBytes, "bmp", new JObject {
                ["kind"] = "Image", ["dependencies"] = new JArray(), ["image"] = new JObject { ["pixel_width"] = 3, ["pixel_height"] = 2 } });
        }
        finally { Array.Clear(sourceBytes); }
        var legacy = sketch.SketchImages.Add(source, app.TransientGeometry.CreatePoint2d(1.3, 2.1), false);
        double lx = legacy.Position.X * 10, ly = legacy.Position.Y * 10, lw = legacy.Width * 10, lh = legacy.Height * 10;
        var raw = new[] { new[] { lx, ly }, new[] { lx + lw, ly }, new[] { lx + lw, ly - lh }, new[] { lx, ly - lh } };
        var objects = app.TransientObjects.CreateObjectCollection(); objects.Add(legacy);
        sketch.RotateSketchObjects(objects, app.TransientGeometry.CreatePoint2d(lx / 10, ly / 10), 37 * Math.PI / 180, false, false);
        legacy.MirrorHorizontal();
        File.Delete(source);
        if (File.Exists(source) || legacy.LinkedToFile || legacy.Transparent) throw new ProbeFailure("Legacy structure fixture is not source independent and opaque.");
        var ordinary = sketch.SketchLines.AddByTwoPoints(app.TransientGeometry.CreatePoint2d(10, 10), app.TransientGeometry.CreatePoint2d(12, 10.5));
        var legacyPoint = legacy.BoundaryGeometry.Cast<SketchLine>().First().StartSketchPoint;
        var shared = sketch.SketchLines.AddByTwoPoints(legacyPoint, app.TransientGeometry.CreatePoint2d(4, 4.5));
        if (Identity(shared.StartSketchPoint) != Identity(legacyPoint)) throw new ProbeFailure("Shared-anchor fixture did not reuse the exact native image point.");
        if (!doc.Update2(true)) throw new ProbeFailure("Structure fixture update failed.");
        report["phase"] = kind + "_initial_owned_target_read";
        var observations = report["target_observations"] as JArray;
        if (observations == null) { observations = new JArray(); report["target_observations"] = observations; }
        var startup = System.Diagnostics.Stopwatch.StartNew(); DocumentState state;
        while (true)
        {
            var observation = new JObject { ["context"] = kind, ["attempt"] = observations.Count + 1,
                ["expected_target_id"] = expectedTarget.TargetId, ["expected_pid"] = expectedTarget.ProcessId };
            observations.Add(observation);
            string descriptorPath = Path.Combine(descriptors, $"inventor-2027-{expectedTarget.ProcessId}.json");
            observation["descriptor_exists"] = File.Exists(descriptorPath);
            if (File.Exists(descriptorPath))
            {
                try
                {
                    var current = JsonConvert.DeserializeObject<TargetDescriptor>(File.ReadAllText(descriptorPath));
                    observation["descriptor_parse_ok"] = current != null;
                    if (current != null)
                    {
                        observation["pid"] = current.ProcessId; observation["year"] = current.InventorYear;
                        observation["host"] = current.HostApp; observation["transport"] = current.Transport;
                        observation["target_id_match"] = current.TargetId == expectedTarget.TargetId;
                        observation["heartbeat_age_seconds"] = (DateTimeOffset.UtcNow - current.LastHeartbeatUtc).TotalSeconds;
                        bool exactEndpoint = current.ProcessId == expectedTarget.ProcessId && current.TargetId == expectedTarget.TargetId
                            && current.AuthToken == expectedTarget.AuthToken && current.PipeName == expectedTarget.PipeName
                            && current.InventorYear == 2027 && current.HostApp == "Inventor" && current.Transport == "pipe";
                        observation["exact_owned_endpoint_match"] = exactEndpoint;
                        if (!exactEndpoint) throw new ProbeFailure("Owned target descriptor changed before startup baseline.");
                    }
                }
                catch (IOException) { observation["descriptor_read_io_race"] = true; }
                catch (JsonException) { observation["descriptor_json_race"] = true; }
            }
            var selected = plugin.CurrentTarget;
            observation["production_resolved_target_match"] = selected?.TargetId == expectedTarget.TargetId && selected.ProcessId == expectedTarget.ProcessId;
            try { state = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult(); observation["state_read"] = "PASS"; break; }
            catch (McpToolException error) when (error.Code is "NO_TARGET" or "TARGET_UNAVAILABLE")
            {
                observation["state_read"] = error.Code;
                if (startup.Elapsed >= TimeSpan.FromSeconds(15)) throw;
                // Startup discovery only: no revision has been accepted, no product mutation has
                // been issued, and the host remains pinned to the same exact owned target id.
                Thread.Sleep(500);
            }
        }
        if (state.DocumentId != documentId) throw new ProbeFailure("Structure HTTP host targets another document.");
        JObject Operation(string variant, JObject options) => new() { ["command"] = "edit_sketch", ["arguments"] = new JObject {
            ["sketch_id"] = sketchId, ["variant"] = variant, ["options"] = options } };
        JObject Batch(DocumentState current, JArray operations, bool preview = false) => mcp.CallToolAsync("inventor_atomic_batch", new JObject {
            ["document_id"] = current.DocumentId, ["expected_revision"] = current.Revision,
            ["operations"] = operations, ["preview"] = preview }, CancellationToken.None).GetAwaiter().GetResult();
        report["phase"] = kind + "_managed_image_http_commit";
        var insertion = Batch(state, new JArray(Operation("insert_image", new JObject { ["asset_id"] = resource.Id,
            ["position_mm"] = new JArray(60, 70), ["width_mm"] = 30, ["quarter_turns"] = 0, ["link"] = false })));
        if ((string?)insertion["status"] != "committed" || sketch.SketchImages.Count != 2) throw new ProbeFailure("Managed image was not committed by the production HTTP batch path.");
        var managed = sketch.SketchImages.Cast<SketchImage>().Single(image => Identity(image) != Identity(legacy));
        if (!managed.AttributeSets.NameIsUsed["InventorSO_SketchImagePayload"] || !managed.AttributeSets.NameIsUsed["InventorSO_SketchImageFrame"])
            throw new ProbeFailure("Committed managed image lacks production source/frame attributes.");
        state = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
        var row = new JObject { ["kind"] = kind, ["managed_insertion_http"] = true,
            ["resource_setup"] = "Generated BMP registered directly in dedicated server fixture catalog; no upload endpoint claim.",
            ["source_deleted"] = true };
        cases.Add(row);
        var before = WholeState56(doc, sketch);
        if (!JToken.DeepEquals(before, WholeState56(doc, sketch))) throw new ProbeFailure("Structure persistent-key baseline is not stable.");
        report["phase"] = kind + "_native_snapshot_core56";
        var wire = mcp.CallToolAsync("inventor_get_sketch_editor_xr", new JObject { ["document_id"] = state.DocumentId,
            ["expected_revision"] = state.Revision, ["sketch_id"] = sketchId }, CancellationToken.None).GetAwaiter().GetResult();
        var snapshot = NativeSketchSnapshot.Parse(wire, state, sketchId);
        row["snapshot"] = wire.DeepClone();
        if (!snapshot.Partial || snapshot.Entities.Count(e => e.Type == "image") != 2) throw new ProbeFailure("Images must preserve the partial native editor contract.");
        var imageIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var image in new[] { legacy, managed })
        {
            string id = Reference56(documentId, image, "sketch_image"); imageIds.Add(Identity(image), id);
            var typed = snapshot.Entities.Single(entity => entity.Reference?.EntityId == id);
            if (typed.Type != "image" || typed.Supported || typed.Selectable || typed.SelectionState != "read_only_image" || typed.CurrentImage == null
                || typed.CurrentImage.SourceKind != "native_bitmap" || typed.CurrentImage.AssetState != "deferred" || typed.CurrentImage.Provenance != "native_boundary_roles")
                throw new ProbeFailure("Image current-frame read-only contract differs.");
            bool isLegacy = Identity(image) == Identity(legacy);
            if ((string?)typed.Data["managed_original_state"] != (isLegacy ? "absent" : "available")
                || isLegacy && typed.Image != null || !isLegacy && typed.Image == null) throw new ProbeFailure("Legacy/managed original-source contracts were conflated.");
            double[][] expected;
            if (isLegacy)
            {
                expected = new[] { 1, 0, 3, 2 }.Select(i => (double[])raw[i].Clone()).ToArray();
                double c = Math.Cos(37 * Math.PI / 180), s = Math.Sin(37 * Math.PI / 180);
                foreach (var p in expected) { double x = p[0] - lx, y = p[1] - ly; p[0] = lx + x * c - y * s; p[1] = ly + x * s + y * c; }
            }
            else expected = new[] { new[] { 60d, 70d }, new[] { 90d, 70d }, new[] { 90d, 50d }, new[] { 60d, 50d } };
            for (int i = 0; i < 4; i++) { Near(typed.CurrentImage.CornersMm[i].X, expected[i][0]); Near(typed.CurrentImage.CornersMm[i].Y, expected[i][1]); }
            using var asset = backend.GetNativeSketchImageAsync(state, sketchId, id, CancellationToken.None).GetAwaiter().GetResult();
            for (int i = 0; i < 4; i++) { Near(asset.CornersMm[i].X, expected[i][0]); Near(asset.CornersMm[i].Y, expected[i][1]); }
            byte[] png = asset.CopyPngBytes(); try { VerifyPng(png); } finally { Array.Clear(png); }
        }
        var nativeEntities = sketch.SketchEntities.Cast<SketchEntity>().ToDictionary(Identity, StringComparer.Ordinal);
        var structural = new JArray(); row["structural_boundaries"] = structural;
        var boundaryEntities = new List<(object entity, string type, string owner)>();
        foreach (var image in new[] { legacy, managed })
        {
            var edges = image.BoundaryGeometry.Cast<SketchLine>().ToArray();
            var points = edges.SelectMany(edge => new[] { edge.StartSketchPoint, edge.EndSketchPoint }).GroupBy(Identity).Select(group => group.First()).ToArray();
            if (edges.Length != 4 || points.Length != 4) throw new ProbeFailure("Native image must have exactly four edges and four corner points.");
            string ownerId = imageIds[Identity(image)]; int enumeratedEdges = 0, enumeratedPoints = 0;
            foreach (var pair in edges.Cast<object>().Select(e => (entity: e, type: "line")).Concat(points.Cast<object>().Select(p => (entity: p, type: "point"))))
            {
                boundaryEntities.Add((pair.entity, pair.type, ownerId));
                if (!nativeEntities.ContainsKey(Identity(pair.entity))) continue;
                if (pair.type == "line") enumeratedEdges++; else enumeratedPoints++;
                string id = Reference56(documentId, pair.entity, "sketch_entity");
                var typed = snapshot.Entities.Single(entity => entity.Reference?.EntityId == id);
                if (!typed.Supported || typed.Selectable || typed.SelectionState != "image_boundary" || typed.StructuralOwnerImageId != ownerId || typed.Type != pair.type)
                    throw new ProbeFailure("Enumerated image boundary was not structurally owned and excluded from selection.");
                if (snapshot.PickAll(typed.Strokes[0], 0.001).Any(entity => entity.Reference?.EntityId == id)
                    || snapshot.PickTargets(typed.Strokes[0], 0.001).Any(hit => hit.Reference.EntityId == id)) throw new ProbeFailure("Image boundary remains in Core hit testing.");
            }
            structural.Add(new JObject { ["image_id"] = ownerId, ["native_edges"] = edges.Length, ["native_points"] = points.Length,
                ["enumerated_edges"] = enumeratedEdges, ["enumerated_points"] = enumeratedPoints, ["exact_owners_and_selection"] = true });
        }
        string ordinaryId = Reference56(documentId, ordinary, "sketch_entity"), sharedId = Reference56(documentId, shared, "sketch_entity");
        var ordinaryTyped = snapshot.Entities.Single(entity => entity.Reference?.EntityId == ordinaryId);
        var sharedTyped = snapshot.Entities.Single(entity => entity.Reference?.EntityId == sharedId);
        if (!ordinaryTyped.Selectable || !ordinaryTyped.CanSelectSubpart(SketchSubpart.Start) || !ordinaryTyped.CanSelectSubpart(SketchSubpart.End)
            || !snapshot.PickAll(new CadPoint(110, 102.5), 0.001).Any(entity => entity.Reference?.EntityId == ordinaryId))
            throw new ProbeFailure("Unrelated native line is no longer selectable.");
        if (!sharedTyped.Selectable || sharedTyped.CanSelectSubpart(SketchSubpart.Start) || !sharedTyped.CanSelectSubpart(SketchSubpart.End)
            || (string?)sharedTyped.Data["structural_subparts"]?[0]?["owner_image_id"] != imageIds[Identity(legacy)])
            throw new ProbeFailure("Shared image anchor is not excluded while ordinary line/end remain selectable.");
        var sharedPoint = new CadPoint(shared.StartSketchPoint.Geometry.X * 10, shared.StartSketchPoint.Geometry.Y * 10);
        if (snapshot.PickTargets(sharedPoint, 0.001, "line", SketchSubpart.Start).Any(hit => hit.Reference.EntityId == sharedId))
            throw new ProbeFailure("Shared image anchor remains in Core subpart hit testing.");
        row["ordinary_line_selectable"] = true; row["shared_anchor_filtered"] = true;
        row["current_frame_and_pixels_exact"] = true; row["image_unsupported_partial"] = true;
        var afterRead = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (!SameState56(state, afterRead) || !JToken.DeepEquals(before, WholeState56(doc, sketch))) throw new ProbeFailure("Native image snapshot/pixel reads changed the fixture.");
        row["snapshot_read_only_exact"] = true;
        var refusals = new JArray(); row["guard_refusals"] = refusals;
        JObject Selection(object entity, string type, string subpart = "Entity") => new SketchEntityRef(documentId, state.Revision, sketchId,
            Reference56(documentId, entity, "sketch_entity"), type, Enum.Parse<SketchSubpart>(subpart)).ToJson();
        void Refuse(string name, string variant, JObject options, bool requireStructuralGuard = true, string nativeBinding = "ordinary_parent_entity")
        {
            report["phase"] = kind + "_guard_" + name;
            var nativeBefore = WholeState56(doc, sketch); var revisionBefore = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
            McpToolException? failure = null; JObject? unexpectedSuccess = null;
            try { unexpectedSuccess = Batch(state, new JArray(Operation(variant, options))); }
            catch (McpToolException error) { failure = error; }
            var revisionAfter = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
            var nativeAfter = WholeState56(doc, sketch);
            bool exact = SameState56(revisionBefore, revisionAfter) && JToken.DeepEquals(nativeBefore, nativeAfter);
            bool guardReached = failure?.Message.Contains("Image boundary geometry is structural", StringComparison.Ordinal) == true;
            // McpToolException.Details is the complete error object, matching production Core;
            // the machine-readable native step fields are its nested "details" member.
            var stepDetails = failure?.Details?["details"] as JObject;
            var refusal = new JObject { ["case"] = name, ["variant"] = variant, ["code"] = failure?.Code,
                ["step_code"] = stepDetails?["step_code"]?.DeepClone(), ["step_index"] = stepDetails?["step_index"]?.DeepClone(),
                ["command_matches"] = (string?)stepDetails?["command"] == "edit_sketch", ["native_and_revision_exact"] = exact,
                ["explicit_structural_guard_reached"] = guardReached, ["native_reference_binding"] = nativeBinding,
                ["evidence"] = guardReached ? "native_structural_guard" : "native_reference_binding_refused_before_structural_guard" };
            if (unexpectedSuccess != null) refusal["unexpected_status"] = unexpectedSuccess["status"]?.DeepClone();
            refusals.Add(refusal);
            if (!exact) { refusal["native_before"] = nativeBefore; refusal["native_after"] = nativeAfter; }
            if (failure == null || failure.Code != "ROLLED_BACK" || (int?)stepDetails?["step_index"] != 0
                || (string?)stepDetails?["command"] != "edit_sketch" || (string?)stepDetails?["step_code"] is not ("INVALID_ARGUMENT" or "API_ERROR"))
                throw new ProbeFailure("Constructed structural reference did not produce the expected first-step rollback refusal.");
            if (requireStructuralGuard && (!guardReached || (string?)stepDetails?["step_code"] != "INVALID_ARGUMENT"))
                throw new ProbeFailure("Resolvable structural selection did not reach the explicit native image guard.");
            if (!exact) throw new ProbeFailure("Structural reference refusal changed native state or revision.");
        }
        int boundaryIndex = 0;
        foreach (var entry in boundaryEntities)
        {
            string binding = NativeBinding56(doc, entry.entity);
            Refuse("boundary_" + (++boundaryIndex), "format_construction", new JObject { ["references"] = new JArray(Selection(entry.entity, entry.type)), ["value"] = true },
                requireStructuralGuard: binding == "exact", nativeBinding: binding);
        }
        Refuse("shared_start_ground", "constraint_ground", new JObject { ["references"] = new JArray(Selection(shared, "line", "Start")) });
        Refuse("shared_start_stretch", "transform_stretch", new JObject { ["references"] = new JArray(Selection(shared, "line", "Start")),
            ["delta_mm"] = new JArray(1, 2), ["remove_constraints"] = false });
        Refuse("shared_whole_move", "transform_move", new JObject { ["references"] = new JArray(Selection(shared, "line")),
            ["delta_mm"] = new JArray(1, 2), ["remove_constraints"] = false });
        row["constructed_refusals"] = refusals.Count;
        report["phase"] = kind + "_ordinary_format_preview";
        var ordinaryOptions = new JObject { ["references"] = new JArray(Selection(ordinary, "line")), ["value"] = true };
        var preview = Batch(state, new JArray(Operation("format_construction", ordinaryOptions)), preview: true);
        if ((string?)preview["status"] != "preview_rolled_back" || !JToken.DeepEquals(before, WholeState56(doc, sketch))
            || !SameState56(state, backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult())) throw new ProbeFailure("Unrelated line preview did not pass and restore exactly.");
        report["phase"] = kind + "_ordinary_format_commit";
        var commit = Batch(state, new JArray(Operation("format_construction", ordinaryOptions)));
        var committed = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
        if ((string?)commit["status"] != "committed" || committed.Revision == state.Revision || !ordinary.Construction
            || legacy.LinkedToFile || managed.LinkedToFile || sketch.SketchImages.Count != 2) throw new ProbeFailure("Unrelated native line format did not commit while preserving the images.");
        var imageBeforeCommit = (JArray)before["images"]!.DeepClone();
        var imageAfterCommit = (JArray)WholeState56(doc, sketch)["images"]!.DeepClone();
        foreach (JObject imageState in imageBeforeCommit.Concat(imageAfterCommit)) imageState.Remove("revision");
        if (!JToken.DeepEquals(imageBeforeCommit, imageAfterCommit)) throw new ProbeFailure("Unrelated line commit changed native image state beyond document revision.");
        row["images_preserved_after_ordinary_commit"] = true;
        row["ordinary_preview_exact"] = true; row["ordinary_commit"] = true; row["outcome"] = "PASS";
    }

    private static bool SameState56(DocumentState a, DocumentState b) => a.DocumentId == b.DocumentId && a.Revision == b.Revision && a.VisualRevision == b.VisualRevision;
    private static string NativeBinding56(PartDocument doc, object entity)
    {
        byte[] key = Convert.FromBase64String(Identity(entity));
        try
        {
            bool resolved = doc.ReferenceKeyManager.CanBindKeyToObject(ref key, 0, out object target, out object details);
            if (!resolved) return "unresolved";
            if (target is ObjectCollection || Identity(target) != Identity(entity)) throw new ProbeFailure("Boundary reference resolved to a different or ambiguous native entity.");
            return "exact";
        }
        catch (System.Runtime.InteropServices.COMException error) when (error.HResult == unchecked((int)0x80070057)) { return "native_E_INVALIDARG"; }
    }
    private static JObject WholeState56(PartDocument doc, PlanarSketch sketch) => new() {
        ["document_id"] = doc.InternalName, ["database_revision"] = doc.DatabaseRevisionId, ["dirty"] = doc.Dirty,
        ["images"] = new JArray(sketch.SketchImages.Cast<SketchImage>().Select(image => State(doc, image, sketch))),
        ["lines"] = new JArray(sketch.SketchLines.Cast<SketchLine>().Select(line => new JObject {
            ["id"] = Identity(line), ["start_id"] = Identity(line.StartSketchPoint), ["end_id"] = Identity(line.EndSketchPoint),
            ["start"] = new JArray(line.StartSketchPoint.Geometry.X, line.StartSketchPoint.Geometry.Y),
            ["end"] = new JArray(line.EndSketchPoint.Geometry.X, line.EndSketchPoint.Geometry.Y),
            ["construction"] = line.Construction, ["centerline"] = line.Centerline })),
        ["points"] = new JArray(sketch.SketchPoints.Cast<SketchPoint>().Select(point => new JObject {
            ["id"] = Identity(point), ["position"] = new JArray(point.Geometry.X, point.Geometry.Y),
            ["construction"] = point.Construction, ["hole_center"] = point.HoleCenter })),
        ["constraints"] = new JArray(sketch.GeometricConstraints.Cast<GeometricConstraint>().Select(Identity)),
        ["dimensions"] = new JArray(sketch.DimensionConstraints.Cast<DimensionConstraint>().Select(Identity)) };
}
