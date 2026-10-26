using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.RealityCapture;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

[McpServerToolType]
public sealed class RealityCaptureTools(IEnumerable<ICaptureControl> controls)
{
    private Task<string> Call(string operation,JObject args,CancellationToken ct)=>Invoke(controls.FirstOrDefault(),operation,args,ct);
    private static async Task<string> Invoke(ICaptureControl? control,string operation,JObject args,CancellationToken ct)
    {
        var result=control==null ? new JObject { ["ok"]=false,["error"]=new JObject { ["code"]="CAPTURE_HTTP_REQUIRED",["message"]="Capture control requires the authenticated experimental HTTP host." } }
            : await control.InvokeAsync(operation,args,ct);
        return result.ToString(Formatting.None);
    }
    [McpServerTool(Name="inventor_reality_capture_create_session"),Description("Experimental: create/resume a token-owned upload from manifest JSON (ScanId, Files path/size/sha256). Upload raw bytes using /capture/v1, never MCP/base64. No CAD changes.")]
    public Task<string> CreateSession(string manifest_json,CancellationToken ct=default)=>Call("create_session",new JObject { ["manifest_json"]=manifest_json },ct);
    [McpServerTool(Name="inventor_reality_capture_get_session"),Description("Experimental: read owned capture upload status, received chunk indexes and retention. Requires the original bearer token.")]
    public Task<string> GetSession(string session_id,CancellationToken ct=default)=>Call("get_session",new JObject { ["session_id"]=session_id },ct);
    [McpServerTool(Name="inventor_reality_capture_add_constraint"),Description("Experimental: add/update a metric distance (two point IDs) or rim diameter (three point IDs), value/uncertainty in mm, role calibration/validation, enabled, independent, source and MeasurementId. Independent validation requires distinct measurement IDs not used for calibration. Pass expected_version from list_constraints; preserves raw and previous solves.")]
    public Task<string> AddConstraint(string session_id,string constraint_json,int expected_version,CancellationToken ct=default)=>Call("add_constraint",new JObject { ["session_id"]=session_id,["constraint_json"]=constraint_json,["expected_version"]=expected_version },ct);
    [McpServerTool(Name="inventor_reality_capture_remove_constraint"),Description("Experimental: remove a constraint in a new version. Does not change raw or existing solve revisions; expected_version prevents lost edits.")]
    public Task<string> RemoveConstraint(string session_id,string constraint_id,int expected_version,CancellationToken ct=default)=>Call("remove_constraint",new JObject { ["session_id"]=session_id,["constraint_id"]=constraint_id,["expected_version"]=expected_version },ct);
    [McpServerTool(Name="inventor_reality_capture_list_constraints"),Description("Experimental: list owned capture constraints, measured calibration artifacts and version. Validation dimensions never enter the solver.")]
    public Task<string> ListConstraints(string session_id,CancellationToken ct=default)=>Call("list_constraints",new JObject { ["session_id"]=session_id },ct);
    [McpServerTool(Name="inventor_reality_capture_set_artifact"),Description("Experimental: add/update a measured ArUco DICT_4X4_50 calibration artifact. Provide measured corner coordinates in mm, uncertainty, measurement method/date, geometry version and calibration/validation role. Nominal printed dimensions are not calibration evidence.")]
    public Task<string> SetArtifact(string session_id,string artifact_json,int expected_version,CancellationToken ct=default)=>Call("set_artifact",new JObject { ["session_id"]=session_id,["artifact_json"]=artifact_json,["expected_version"]=expected_version },ct);
    [McpServerTool(Name="inventor_reality_capture_start_solve"),Description("Experimental: start isolated local RGB multi-view reconstruction and joint metric bundle adjustment of finalized raw. Returns a revision immediately; poll get_solution. No cloud, no CAD changes. Sparse surface only; physical validation pending.")]
    public Task<string> StartSolve(string session_id,int expected_version,CancellationToken ct=default)=>Call("start_solve",new JObject { ["session_id"]=session_id,["expected_version"]=expected_version },ct);
    [McpServerTool(Name="inventor_reality_capture_get_solution"),Description("Experimental: read an owned immutable solve revision, state/error and authenticated artifact URLs. Failed/partial output is never published.")]
    public Task<string> GetSolution(string session_id,int revision,CancellationToken ct=default)=>Call("get_solution",new JObject { ["session_id"]=session_id,["revision"]=revision },ct);
    [McpServerTool(Name="inventor_reality_capture_get_quality"),Description("Experimental: get reprojection, calibration and independent validation residuals with explicit limitations. Synthetic tests are not physical accuracy evidence.")]
    public Task<string> GetQuality(string session_id,int revision,CancellationToken ct=default)=>Call("get_quality",new JObject { ["session_id"]=session_id,["revision"]=revision },ct);
    [McpServerTool(Name="inventor_reality_capture_cancel_solve"),Description("Experimental: cancel an owned solve; the host stops the worker process and preserves raw and previous revisions.")]
    public Task<string> CancelSolve(string session_id,int revision,CancellationToken ct=default)=>Call("cancel_solve",new JObject { ["session_id"]=session_id,["revision"]=revision },ct);
    [McpServerTool(Name="inventor_reality_capture_export"),Description("Experimental: return an authenticated artifact URL for glb (metres), ply/obj (mm plus units metadata), or points JSON. No arbitrary filesystem path and no Inventor import.")]
    public Task<string> Export(string session_id,int revision,string format="glb",CancellationToken ct=default)=>Call("export",new JObject { ["session_id"]=session_id,["revision"]=revision,["format"]=format },ct);
    [McpServerTool(Name="inventor_reality_capture_get_review"),Description("Experimental: get versioned geometric annotations bound to an owned immutable solve. RMS is support scatter; measurement uncertainty is not estimated.")]
    public Task<string> GetReview(string session_id,int revision,CancellationToken ct=default)=>Call("get_review",new JObject { ["session_id"]=session_id,["revision"]=revision },ct);
    [McpServerTool(Name="inventor_reality_capture_fit_primitive"),Description("Experimental: fit point, axis, plane, circle or cylinder to selected reconstructed point IDs (JSON array, at most 2000). Versioned annotation only, no CAD or raw changes. Degenerate/insufficient support is rejected; RMS is not accuracy. expected_version comes from get_review.")]
    public Task<string> FitPrimitive(string session_id,int revision,string primitive_id,string kind,string point_ids_json,int expected_version,CancellationToken ct=default)=>Call("fit_primitive",new JObject { ["session_id"]=session_id,["revision"]=revision,["primitive_id"]=primitive_id,["kind"]=kind,["point_ids_json"]=point_ids_json,["expected_version"]=expected_version },ct);
    [McpServerTool(Name="inventor_reality_capture_remove_primitive"),Description("Experimental: remove a geometric annotation in a new review version. Previous solve artifacts and raw remain immutable.")]
    public Task<string> RemovePrimitive(string session_id,int revision,string primitive_id,int expected_version,CancellationToken ct=default)=>Call("remove_primitive",new JObject { ["session_id"]=session_id,["revision"]=revision,["primitive_id"]=primitive_id,["expected_version"]=expected_version },ct);
    [McpServerTool(Name="inventor_reality_capture_get_alignment"),Description("Experimental: read the token-owned scan-to-CAD registration, bound to solve, annotation version, CAD document and revision. Old tracking poses are not physical anchors.")]
    public Task<string> GetAlignment(string session_id,int revision,CancellationToken ct=default)=>Call("get_alignment",new JObject {["session_id"]=session_id,["revision"]=revision},ct);
    [McpServerTool(Name="inventor_reality_capture_align_to_cad"),Description("Experimental: persist a rigid scan LH to Inventor RH registration in mm. registration_json includes document_id, cad_revision, review_version, expected_alignment_version, method (three_points or plane_axis), three reference_ids and CAD point triples or origin/normal/axis. No scale fitting or CAD writes; CAD revision is verified again by the preview/commit workflow.")]
    public Task<string> AlignToCad(string session_id,int revision,string registration_json,CancellationToken ct=default)=>Call("align_to_cad",new JObject {["session_id"]=session_id,["revision"]=revision,["registration_json"]=registration_json},ct);
    [McpServerTool(Name="inventor_reality_capture_create_reference"),Description("Experimental: prepare one bounded create_reality_reference operation from owned immutable points, current annotations and exact alignment version. No CAD write yet: run inventor_plan_change and commit_plan on the intended reference part. Produces native work geometry, parameters and a point-cloud 3D sketch; no B-Rep conversion.")]
    public Task<string> CreateReference(string session_id,int revision,int alignment_version,CancellationToken ct=default)=>Call("create_reference",new JObject {["session_id"]=session_id,["revision"]=revision,["alignment_version"]=alignment_version},ct);
}
