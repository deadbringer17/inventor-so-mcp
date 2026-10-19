using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static void RunSplineCompatibility85(bool identityOnly87=false)
    {
        GuardDocuments73();
        var report = new JObject { ["schema"] = 1, ["run_id"] = Settings.RunId, ["target_id"] = TargetId,
            ["evidence"] = "OBSERVATION_ONLY: native spline tangent compatibility, owned transactions always aborted; no product/Quest/Desktop PASS.",
            ["product_pass"] = false, ["observations"] = new JArray(), ["status"] = "RUNNING",
            ["coverage_profile"]=identityOnly87 ? "handle_identity_only87" : "five_strategy_compatibility85",
            ["expected_observations"]=identityOnly87 ? 4 : 20,
            ["not_covered"] = new JArray("Product arc_tangent support", "Desktop equivalence", "Quest/UI", "Other fit methods", "Source updates", "Persistence", "3D") };
        WritePublic73("probe-spline85.json", report);
        try
        {
            // Cache identities before the first transaction; never reuse a source RCW
            // after Abort or rediscover a spline by collection position/proximity.
            var selected = SupplementFixtures83.Where(f => f.Family == "spline" && f.Side == -1 && f.AnchorMode == "coordinates")
                .Select(f => new { f.Document, f.Endpoint, Sheet = f.Document.ComponentDefinition is SheetMetalComponentDefinition,
                    SourceKey = Key73(f.Source), SketchKey = Key73(f.Sketch) }).ToArray();
            if (selected.Length != 4 || selected.GroupBy(s => s.Document.InternalName)
                .Any(g => g.Select(s => s.SourceKey).Distinct(StringComparer.Ordinal).Count() != 2))
                throw new HostFailure73("Probe requires distinct Start/End spline sources in both owned documents.");
            foreach (bool sheet in new[] { false, true })
            foreach (string endpoint in new[] { "Start", "End" })
            {
                var selection = selected.Single(s => s.Sheet == sheet && s.Endpoint == endpoint);
                var doc = selection.Document; string sourceKey = selection.SourceKey;
                GuardDocuments73(); doc.Activate();
                foreach (string variant in identityOnly87 ? new[]{"enabled_handle"} : new[] { "direct_spline", "enabled_spline", "enabled_handle", "temp_ground_spline", "temp_ground_enabled_handle" })
                {
                    GuardDocuments73(); RequireProbeIdle85();
                    if (DateTimeOffset.UtcNow >= ExpiresUtc) throw new HostFailure73("Owned probe lifetime expired before a case.");
                    var sketch = doc.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s => Key73(s) == selection.SketchKey);
                    var source = sketch.SketchSplines.Cast<SketchSpline>().Single(s => Key73(s) == sourceKey);
                    var point = endpoint == "Start" ? source.StartSketchPoint : source.EndSketchPoint;
                    var beforeFull = FullSplineNativeState85();
                    var row = new JObject { ["context"] = sheet ? "Sheet" : "Part", ["endpoint"] = endpoint, ["variant"] = variant,
                        ["source_key"] = sourceKey, ["selected_point_key"] = Key73(point), ["before_full_sha256"] = HashJson85(beforeFull),
                        ["before"] = SplineState85(source), ["api_failures"] = new JArray() };
                    ((JArray)report["observations"]!).Add(row);
                    Transaction? transaction = null; GroundConstraint? ground = null; SketchSplineHandle? handle = null; SketchArc? arc = null;
                    string stage = "start_owned_transaction"; bool abortExact = false;
                    try
                    {
                        transaction = Application!.TransactionManager.StartTransaction((_Document)doc, "S1 spline compatibility85 OBSERVATION aborted");
                        row["transaction_has_parent"] = transaction.HasParentTransaction;
                        if ((bool)row["transaction_has_parent"]!) throw new HostFailure73("Probe refuses an owned nested transaction.");
                        bool enabled = variant is "enabled_spline" or "enabled_handle" or "temp_ground_enabled_handle";
                        if (enabled)
                        {
                            stage = "set_handle_status"; source.SetHandleStatus(point, true);
                            stage = "get_handle"; handle = source.GetHandle(point);
                            if (handle == null || !SameCom73(handle.Spline, source) || !SameCom73(handle.FitPoint, point)
                                || !SameCom73(handle.Parent, sketch)) throw new HostFailure73("Native handle owner/fit-point identity differs.");
                            row["selected_handle"] = HandleState85(handle);
                        }
                        CaptureSpline85(row, "after_enable", source);
                        if (variant.StartsWith("temp_ground_", StringComparison.Ordinal))
                        {
                            stage = "add_temporary_ground"; ground = sketch.GeometricConstraints.AddGround((SketchEntity)source);
                            row["temporary_ground"] = new JObject { ["key"] = Key73(ground), ["entity_key"] = Key73(ground.Entity),
                                ["source_identity"] = SameCom73(ground.Entity, source) };
                        }
                        CaptureSpline85(row, "after_enable_ground", source);

                        // Derive the new circle from the CURRENT native evaluator after preparation.
                        // The experiment observes preparation drift separately from tangent-induced drift.
                        stage = "native_current_endpoint_tangent";
                        var evaluator = source.Geometry.Evaluator; evaluator.GetParamExtents(out double min, out double max);
                        double[] ends = { min, max }, xy = Array.Empty<double>(); evaluator.GetPointAtParam(ref ends, ref xy);
                        if (xy.Length != 4 || !xy.All(double.IsFinite)) throw new HostFailure73("Native source endpoint evaluation is malformed.");
                        var p = point.Geometry;
                        bool first = Math.Abs(p.X - xy[0]) <= 1e-6 && Math.Abs(p.Y - xy[1]) <= 1e-6;
                        bool last = Math.Abs(p.X - xy[2]) <= 1e-6 && Math.Abs(p.Y - xy[3]) <= 1e-6;
                        if (first == last) throw new HostFailure73("Source endpoint is not unambiguous in the native evaluator.");
                        double[] parameter = { first ? min : max }, tangent = Array.Empty<double>(); evaluator.GetTangent(ref parameter, ref tangent);
                        if (tangent.Length != 2 || !tangent.All(double.IsFinite)) throw new HostFailure73("Native tangent evaluation is malformed.");
                        double sign = first ? -1 : 1, norm = Math.Sqrt(tangent[0] * tangent[0] + tangent[1] * tangent[1]);
                        if (!double.IsFinite(norm) || norm <= 1e-12) throw new HostFailure73("Native tangent is degenerate.");
                        double tx = sign * tangent[0] / norm, ty = sign * tangent[1] / norm;
                        var g = Application.TransientGeometry;
                        var center = g.CreatePoint2d(p.X - ty, p.Y + tx);
                        var target = g.CreatePoint2d(p.X + tx - ty, p.Y + ty + tx);
                        row["arc_request"] = new JObject { ["source_point_mm"] = new JArray(p.X * 10, p.Y * 10),
                            ["outgoing_native_tangent"] = new JArray(tx, ty), ["center_mm"] = new JArray(center.X * 10, center.Y * 10),
                            ["target_mm"] = new JArray(target.X * 10, target.Y * 10), ["radius_mm"] = 10, ["quarter_ccw"] = true };
                        stage = "create_shared_source_arc";
                        arc = sketch.SketchArcs.AddByCenterStartEndPoint(center, point, target, true);
                        row["arc_before_tangent"] = Entity73((SketchEntity)arc);
                        row["arc_shares_selected_source_point"] = SameCom73(arc.StartSketchPoint, point) || SameCom73(arc.EndSketchPoint, point);
                        if (!(bool)row["arc_shares_selected_source_point"]!) throw new HostFailure73("Native arc did not share its explicit source point.");
                        CaptureSpline85(row, "after_arc_before_tangent", source);
                        stage = variant is "enabled_handle" or "temp_ground_enabled_handle" ? "add_tangent_handle_arc" : "add_tangent_spline_arc";
                        SketchEntity tangentSource = variant is "enabled_handle" or "temp_ground_enabled_handle" ? (SketchEntity)handle! : (SketchEntity)source;
                        var relation = sketch.GeometricConstraints.AddTangent(tangentSource, (SketchEntity)arc, point.Geometry.Copy());
                        row["added_tangent"] = NativeRelation85((GeometricConstraint)relation);
                        row["tangent_api_returned"] = true;
                        CaptureSpline85(row, "after_add_tangent_returned87", source);
                    }
                    catch (Exception error) { AddFailure85(row, stage, error); }
                    finally
                    {
                        if (transaction != null)
                        {
                            CaptureSpline85(row, "after_tangent", source);
                            // Always attempt deletion before the final Solve, including after an API failure.
                            bool groundDeleted = ground == null;
                            if (ground != null)
                            {
                                try { ground.Delete(); groundDeleted = true; }
                                catch (Exception error) { AddFailure85(row, "delete_temporary_ground", error); }
                            }
                            row["temporary_ground_deleted_before_final_solve"] = groundDeleted;
                            if (groundDeleted)
                            {
                                try
                                {
                                    sketch.Solve(); row["final_solve_returned"] = true;
                                    CaptureSpline85(row, "after_solve_before_update87", source);
                                }
                                catch (Exception error) { AddFailure85(row, "final_solve", error); }
                                if ((bool?)row["final_solve_returned"] == true)
                                {
                                    try { row["final_update_returned"] = doc.Update2(false); }
                                    catch (Exception error) { AddFailure85(row, "final_update", error); }
                                }
                            }
                            CaptureSpline85(row, "after_delete_solve", source);
                            if (arc != null)
                            {
                                try { row["arc_after_solve"] = Entity73((SketchEntity)arc); }
                                catch (Exception error) { AddFailure85(row, "arc_readback", error); }
                            }
                            try { transaction.Abort(); row["owned_abort_returned"] = true; }
                            catch (Exception error) { AddFailure85(row, "owned_abort", error); }
                        }
                        try
                        {
                            var afterFull = FullSplineNativeState85();
                            bool stateExact = JToken.DeepEquals(beforeFull, afterFull);
                            abortExact = stateExact && (bool?)row["owned_abort_returned"] == true;
                            row["after_abort_full_sha256"] = HashJson85(afterFull); row["rollback_full_native_exact"] = abortExact;
                            if (!abortExact) { row["before_full_native"] = beforeFull; row["after_abort_full_native"] = afterFull; }
                        }
                        catch (Exception error) { AddFailure85(row, "mandatory_full_rollback_readback", error); }
                        if (row["after_delete_solve"] is JObject final)
                        {
                            row["source_order_before"] = row["before"]!["order"];
                            row["source_order_after"] = final["order"];
                            row["source_max_101_sample_delta_mm"] = SampleDelta85((JObject)row["before"]!, final);
                            row["source_nurbs_definition_exact_after_solve"] = JToken.DeepEquals(GeometryOnly85((JObject)row["before"]!), GeometryOnly85(final));
                        }
                        row["observation_only"] = true; WritePublic73("probe-spline85.json", report);
                    }
                    if (!abortExact) throw new HostFailure73("Spline probe rollback differs or cannot be verified; stop all observations.");
                    if ((bool?)row["transaction_has_parent"] == true) throw new HostFailure73("Unexpected parent transaction prevents continuing the probe.");
                }
            }
            if (((JArray)report["observations"]!).Count != (identityOnly87 ? 4 : 20)) throw new HostFailure73("Spline observation matrix is incomplete.");
            report["status"] = "OBSERVATIONS_COMPLETE";
        }
        catch (Exception error)
        {
            report["status"] = "STOPPED_FAILURE"; report["failure"] = ApiError85("probe_fatal", error);
            WritePublic73("probe-spline85.json", report); throw;
        }
        WritePublic73("probe-spline85.json", report);
    }

    private static JObject FullSplineNativeState85()
    {
        GuardDocuments73(); var full = Inspect73(); full.Remove("captured_utc");
        var splines = new JArray();
        var before = Documents.ToDictionary(d => d.InternalName, d => (d.Document.DatabaseRevisionId, d.Document.Dirty));
        foreach (var owned in Documents)
        foreach (PlanarSketch sketch in owned.Document.ComponentDefinition.Sketches)
        foreach (SketchSpline spline in sketch.SketchSplines)
            splines.Add(new JObject { ["document_id"] = owned.DocumentId, ["sketch_key"] = Key73(sketch), ["source"] = SplineState85(spline,false) });
        var graphs=new JArray();
        foreach(var owned in Documents)
        foreach(PlanarSketch sketch in owned.Document.ComponentDefinition.Sketches)
            if(sketch.SketchSplines.Count>0) graphs.Add(NativeSplineIdentity87(sketch.SketchSplines[1]));
        full["native_identity_graphs87"]=graphs;
        if (Documents.Any(d => d.Document.DatabaseRevisionId != before[d.InternalName].DatabaseRevisionId || d.Document.Dirty != before[d.InternalName].Dirty))
            throw new HostFailure73("Native spline readback changed Dirty or DatabaseRevisionId.");
        full["all_fit_splines_101_and_handles"] = splines; GuardDocuments73(); return full;
    }
    private static void RequireProbeIdle85()
    {
        GuardDocuments73();
        if(Application!.CommandManager.ActiveCommand!="AppSelectNorthwestArrowCmd")
            throw new HostFailure73("Native command prevents a compatibility transaction.");
        var current=Application.TransactionManager.CurrentTransaction;
        if(current==null) return;
        bool identified=true;
        try{_=current.HasParentTransaction;}
        catch(System.Runtime.InteropServices.COMException error) when(error.ErrorCode==unchecked((int)0x80004005)){identified=false;}
        if(identified) throw new HostFailure73("Identified native transaction prevents a compatibility transaction.");
    }

    private static JObject SplineState85(SketchSpline source,bool captureIdentityGraph=true)
    {
        var geometry = source.Geometry;
        geometry.GetBSplineInfo(out int order, out int count, out int knotCount, out bool rational, out bool periodic, out bool closed);
        double[] poles = Array.Empty<double>(), knots = Array.Empty<double>(), weights = Array.Empty<double>();
        geometry.GetBSplineData(ref poles, ref knots, ref weights); weights ??= Array.Empty<double>();
        var evaluator = geometry.Evaluator; evaluator.GetParamExtents(out double min, out double max);
        double[] parameters = Enumerable.Range(0, 101).Select(i => min + (max - min) * i / 100).ToArray(), samples = Array.Empty<double>();
        evaluator.GetPointAtParam(ref parameters, ref samples);
        if (parameters.Length != 101 || samples.Length != 202 || !samples.All(double.IsFinite))
            throw new HostFailure73("Mandatory101 native sample readback is malformed.");
        var handles = new JArray();
        for (int i = 1; i <= source.FitPointCount; i++)
        {
            var point = source.get_FitPoint(i); bool enabled = source.GetHandleStatus(point);
            var row = new JObject { ["fit_point_key"] = Key73(point), ["enabled"] = enabled };
            if (enabled)
            {
                var handle = source.GetHandle(point);
                if (handle == null) throw new HostFailure73("Enabled native handle is unavailable during readback.");
                row["handle"] = HandleState85(handle);
            }
            handles.Add(row);
        }
        var sketch = (PlanarSketch)source.Parent;
        return new JObject { ["key"] = Key73(source), ["sketch_key"] = Key73(sketch), ["native_type"] = source.Type.ToString(),
            ["start_key"] = Key73(source.StartSketchPoint), ["end_key"] = Key73(source.EndSketchPoint),
            ["start_mm"] = new JArray(source.StartSketchPoint.Geometry.X * 10, source.StartSketchPoint.Geometry.Y * 10),
            ["end_mm"] = new JArray(source.EndSketchPoint.Geometry.X * 10, source.EndSketchPoint.Geometry.Y * 10),
            ["order"] = order, ["pole_count"] = count, ["knot_count"] = knotCount, ["rational"] = rational, ["periodic"] = periodic, ["closed"] = closed,
            ["poles_mm"] = new JArray(poles.Select(v => v * 10)), ["knots"] = new JArray(knots), ["weights"] = new JArray(weights),
            ["sample_parameters"] = new JArray(parameters), ["samples_mm"] = new JArray(samples.Select(v => v * 10)),
            ["fit_method"] = (int)source.FitMethod, ["fit_handles"] = handles,
            ["constraint_status"] = source.ConstraintStatus.ToString(),
            ["native_identity_graph87"] = captureIdentityGraph ? NativeSplineIdentity87(source) : null,
            ["native_relations"] = new JArray(sketch.GeometricConstraints.Cast<GeometricConstraint>().Select(NativeRelation85)) };
    }
    private static JObject HandleState85(SketchSplineHandle handle)
    {
        return new JObject { ["key"] = Key73(handle), ["type"] = handle.Type.ToString(), ["spline_key"] = Key73(handle.Spline),
            ["fit_point_key"] = Key73(handle.FitPoint), ["sketch_key"] = Key73(handle.Parent),
            ["start_key"] = Key73(handle.StartSketchPoint), ["end_key"] = Key73(handle.EndSketchPoint),
            ["start_mm"] = new JArray(handle.StartSketchPoint.Geometry.X * 10, handle.StartSketchPoint.Geometry.Y * 10),
            ["end_mm"] = new JArray(handle.EndSketchPoint.Geometry.X * 10, handle.EndSketchPoint.Geometry.Y * 10) };
    }
    private static JObject NativeRelation85(GeometricConstraint relation)
    {
        var row = new JObject { ["key"] = Key73(relation), ["type"] = relation.Type.ToString() };
        if (relation is TangentSketchConstraint tangent)
        {
            row["entity_one_key"] = Key73(tangent.EntityOne); row["entity_two_key"] = Key73(tangent.EntityTwo);
            row["entity_one_type"] = tangent.EntityOne.Type.ToString(); row["entity_two_type"] = tangent.EntityTwo.Type.ToString();
            if (tangent.EntityOne is SketchSplineHandle one) row["entity_one_handle_owner"] = HandleState85(one);
            if (tangent.EntityTwo is SketchSplineHandle two) row["entity_two_handle_owner"] = HandleState85(two);
        }
        if (relation is GroundConstraint ground) row["entity_key"] = Key73(ground.Entity);
        return row;
    }
    private static void CaptureSpline85(JObject row, string stage, SketchSpline source)
    { try { row[stage] = SplineState85(source); } catch (Exception error) { AddFailure85(row, "capture_" + stage, error); } }
    private static double SampleDelta85(JObject before, JObject after)
    {
        var a = ((JArray)before["samples_mm"]!).Values<double>().ToArray(); var b = ((JArray)after["samples_mm"]!).Values<double>().ToArray();
        if (a.Length != 202 || b.Length != a.Length) throw new HostFailure73("Comparison sample budget differs.");
        return Enumerable.Range(0, 101).Max(i => Math.Sqrt(Math.Pow(a[i * 2] - b[i * 2], 2) + Math.Pow(a[i * 2 + 1] - b[i * 2 + 1], 2)));
    }
    private static JObject GeometryOnly85(JObject state) => new(state.Properties().Where(p => new[] {
        "key", "start_key", "end_key", "start_mm", "end_mm", "order", "pole_count", "knot_count", "poles_mm", "knots", "weights",
        "rational", "periodic", "closed", "sample_parameters", "samples_mm", "fit_method" }.Contains(p.Name, StringComparer.Ordinal))
        .Select(p => new JProperty(p.Name, p.Value.DeepClone())));
    private static string HashJson85(JToken value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(value.ToString(Newtonsoft.Json.Formatting.None))));
    private static JObject ApiError85(string stage, Exception error) => new()
    { ["stage"] = stage, ["type"] = error.GetType().FullName, ["hresult"] = error.HResult };
    private static void AddFailure85(JObject row, string stage, Exception error) => ((JArray)row["api_failures"]!).Add(ApiError85(stage, error));
}
