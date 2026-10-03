using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// L2 stand-in for the Inventor SO add-in: a real named-pipe NDJSON endpoint with a real target
/// descriptor, answering the wire commands of the XR / planning flow from a tiny in-memory
/// assembly (two bolts and a plate). It proves the MCP server, pipe transport, GLB pipeline, asset
/// store, HTTP host and viewer work together; it proves nothing about Inventor itself.
/// </summary>
public sealed class FakeAddIn : System.IAsyncDisposable
{
    private readonly string _pipe = "InventorSO-fake-" + System.Guid.NewGuid().ToString("N")[..8];
    private readonly string _token = System.Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly Timer _heartbeat;
    private readonly object _gate = new();
    private int _sequence = 1;
    private int _visual = 1;
    private double _boltLengthCm = 1.0;
    private readonly List<(string Owner, double Before, double After)> _history = new();
    private int _historyPosition;
    private string? _historyRevision, _historyTicket;
    private readonly List<JObject> _events = new();

    public string DescriptorDirectory { get; }
    public List<string> Commands { get; } = new();
    public bool DesignPartMode { get; set; }
    public string ActiveDocumentId => DesignPartMode ? BoltId : AssemblyId;

    public const string AssemblyId = "doc_asm";
    public const string BoltId = "doc_bolt";
    public const string PlateId = "doc_plate";

    public FakeAddIn(string descriptorDirectory)
    {
        DescriptorDirectory = descriptorDirectory;
        Directory.CreateDirectory(descriptorDirectory);
        var descriptor = new TargetDescriptor
        {
            TargetId = "inventor-2027-" + System.Environment.ProcessId,
            InventorYear = 2027,
            ProcessId = System.Environment.ProcessId,
            HostApp = "Inventor",
            Transport = "pipe",
            PipeName = _pipe,
            AuthToken = _token,
            DocumentTitle = "Fake.iam",
            LastHeartbeatUtc = System.DateTimeOffset.UtcNow,
        };
        void RefreshDescriptor(object? _)
        {
            lock (_gate)
            {
                descriptor.LastHeartbeatUtc = System.DateTimeOffset.UtcNow;
                File.WriteAllText(Path.Combine(descriptorDirectory, "inventor-2027-fake.json"), JsonConvert.SerializeObject(descriptor));
            }
        }
        RefreshDescriptor(null);
        _heartbeat = new Timer(RefreshDescriptor, null, 30_000, 30_000);
        _loop = Task.Run(() => Serve(_stop.Token));
    }

    public string Revision { get { lock (_gate) return "fake:" + _sequence; } }
    private string Visual { get { lock (_gate) return "fake:v" + _visual; } }

    /// <summary>Simulate an edit in Inventor: advances the revision (and the visual revision for geometry) and journals it.</summary>
    public void RaiseDocumentChanged(bool geometry)
    {
        lock (_gate)
        {
            _sequence++;
            if (geometry) _visual++;
            _events.Add(new JObject { ["seq"] = _events.Count + 1, ["type"] = "document_changed", ["document_id"] = AssemblyId, ["geometry"] = geometry });
        }
    }

    private async Task Serve(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(_pipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await server.WaitForConnectionAsync(ct); }
            catch (System.OperationCanceledException) { await server.DisposeAsync(); break; }
            _ = Task.Run(async () =>
            {
                await using var stream = server;
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var line = await reader.ReadLineAsync();
                if (line == null) return;
                var envelope = JsonConvert.DeserializeObject<InventorCommandEnvelope>(line)!;
                InventorCommandResult result;
                if (envelope.AuthToken != _token)
                    result = InventorCommandResult.Fail(envelope.Id, InventorErrorCodes.UNAUTHORIZED, "bad token", new InventorResponseMeta());
                else
                {
                    lock (_gate) Commands.Add(envelope.Command);
                    result = Handle(envelope);
                }
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result) + "\n");
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            });
        }
    }

    private InventorCommandResult Handle(InventorCommandEnvelope envelope)
    {
        var p = envelope.Params ?? new JObject();
        var meta = new InventorResponseMeta { InventorYear = 2027 };
        InventorCommandResult Ok(JToken data) => InventorCommandResult.Success(envelope.Id, data, meta);
        InventorCommandResult Fail(string code, string message) => InventorCommandResult.Fail(envelope.Id, code, message, meta);
        switch (envelope.Command)
        {
            case "get_capabilities":
                return Ok(new JObject
                {
                    ["inventor_year"] = 2027, ["experimental_build"] = true, ["experimental_enabled"] = true,
                    ["active_document_kind"] = DesignPartMode ? "part" : "assembly",
                    ["commands"] = new JArray("atomic_batch", "get_capabilities", "get_display_mesh", "get_flat_pattern_mesh", "get_scene_graph", "get_visual_revision",
                        "highlight_entity", "pick_entity", "get_events", "list_parameters", "get_document_info", "set_parameter", "save_artifact"),
                });
            case "get_document_info":
                return Ok(new JObject { ["id"] = AssemblyId, ["revision"] = Revision, ["document_type"] = "kAssemblyDocumentObject", ["title"] = "Fake.iam", ["dirty"] = false });
            case "list_parameters":
                return Ok(new JObject { ["parameters"] = new JArray(new JObject { ["name"] = "BoltLength", ["expression"] = (_boltLengthCm * 10) + " mm" }) });
            case "get_visual_revision":
                return Ok(new JObject { ["document_id"] = (string?)p["document_id"] ?? ActiveDocumentId, ["revision"] = Revision, ["visual_revision"] = Visual });
            case "get_design_context_xr":
                if (!DesignPartMode) return Fail(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "Part required.");
                if ((string?)p["document_id"] != ActiveDocumentId) return Fail(InventorErrorCodes.DOCUMENT_CHANGED, "Document changed.");
                if ((string?)p["expected_revision"] != Revision) return Fail(InventorErrorCodes.STALE_REVISION, "Revision changed.");
                return Ok(new JObject
                {
                    ["document_id"] = ActiveDocumentId, ["revision"] = Revision, ["kind"] = "part",
                    ["planes"] = new JArray(new JObject { ["reference"] = "3", ["name"] = "XY", ["origin_mm"] = new JArray(0,0,0), ["x_axis"] = new JArray(1,0,0), ["y_axis"] = new JArray(0,1,0) }),
                    ["sketches"] = new JArray(), ["faces"] = new JArray(new JObject { ["id"] = "ent_face", ["point_mm"] = new JArray(0,0,0), ["normal"] = new JArray(0,0,1) }),
                    ["edges"] = new JArray(new JObject { ["id"] = "ent_edge", ["kind"] = "line", ["points_mm"] = new JArray(0,0,0,10,0,0) }),
                    ["parameters"] = new JArray(), ["truncated"] = false,
                });
            case "get_events":
                lock (_gate)
                {
                    long after = (long?)p["after"] ?? 0;
                    return Ok(new JObject
                    {
                        ["epoch"] = "fake", ["cursor"] = _events.Count, ["resync_required"] = false,
                        ["events"] = new JArray(_events.Where(e => (long)e["seq"]! > after).Select(e => e.DeepClone())),
                    });
                }
            case "highlight_entity":
                return Ok(new JObject { ["mode"] = p["mode"], ["highlighted"] = (p["entity_ids"] as JArray)?.Count ?? 0 });
            case "pick_entity":
                return Ok(new JObject { ["entity_id"] = "ent_proxy_" + p["face_id"], ["type"] = "face_proxy" });
            case "get_scene_graph":
                return Ok(Scene());
            case "list_open_documents":
                return Ok(new JObject { ["documents"] = new JArray(new JObject
                { ["document_id"] = AssemblyId, ["title"] = "Fake.iam", ["document_type"] = "kAssemblyDocumentObject", ["is_active"] = true }) });
            case "activate_open_document_xr":
                return (string?)p["document_id"] == AssemblyId
                    ? Ok(new JObject { ["document_id"] = AssemblyId, ["status"] = "activated" })
                    : Fail(InventorErrorCodes.NO_DOCUMENT, "Document is not open.");
            case "inspect_xr":
                if ((string?)p["document_id"] != AssemblyId) return Fail("DOCUMENT_CHANGED", "Active document changed.");
                if ((string?)p["expected_revision"] != Revision) return Fail(InventorErrorCodes.STALE_REVISION, "Revision changed.");
                return Ok(new JObject { ["document_id"] = AssemblyId, ["revision"] = Revision,
                    ["name"] = p["occurrence_id"]?.Type == JTokenType.String ? "Bolt:1" : "Fake.iam", ["material"] = "Steel",
                    ["mass_kg"] = 1.28, ["volume_mm3"] = 160000, ["area_mm2"] = 20000,
                    ["constraints"] = 3, ["dof_translation"] = 1, ["dof_rotation"] = 0 });
            case "check_interference_xr":
                if ((string?)p["document_id"] != AssemblyId) return Fail(InventorErrorCodes.DOCUMENT_CHANGED, "Active document changed.");
                if ((string?)p["expected_revision"] != Revision) return Fail(InventorErrorCodes.STALE_REVISION, "Revision changed.");
                return Ok(new JObject
                {
                    ["document_id"] = AssemblyId, ["revision"] = Revision, ["analyzed"] = 3, ["count"] = 1, ["total_volume_mm3"] = 2000.0, ["elapsed_ms"] = 12,
                    ["pairs"] = new JArray(new JObject
                    {
                        ["a_occurrence_id"] = "ent_occ_1", ["b_occurrence_id"] = "ent_occ_3", ["a_name"] = "Bolt:1", ["b_name"] = "Plate:1",
                        ["volume_mm3"] = 2000.0,
                        ["boxes"] = new JArray(new JObject { ["min_mm"] = new JArray(15, 0, 0), ["max_mm"] = new JArray(20, 20, 20) }),
                    }),
                });
            case "measure_min_distance_xr":
                if ((string?)p["document_id"] != AssemblyId) return Fail(InventorErrorCodes.DOCUMENT_CHANGED, "Active document changed.");
                if ((string?)p["expected_revision"] != Revision) return Fail(InventorErrorCodes.STALE_REVISION, "Revision changed.");
                if ((string?)p["a_occurrence_id"] == (string?)p["b_occurrence_id"]) return Fail(InventorErrorCodes.INVALID_ARGUMENT, "Choose two different occurrences.");
                return Ok(new JObject
                {
                    ["document_id"] = AssemblyId, ["revision"] = Revision, ["distance_mm"] = 30.0,
                    ["point_a"] = new JArray(0, 0, 0), ["point_b"] = new JArray(30, 0, 0), ["points_source"] = "inventor",
                });
            case "get_display_mesh":
            {
                string id = (string?)p["document_id"] ?? AssemblyId;
                if (id == AssemblyId) return Fail(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "assembly");
                double size = id == BoltId ? _boltLengthCm : 5.0;
                return Ok(new JObject
                {
                    ["document_id"] = id, ["definition_name"] = id, ["revision"] = Revision, ["visual_revision"] = Visual,
                    ["face_ids_complete"] = true, ["bodies"] = new JArray(MeshPayload.ToJson(Box(size, id))),
                });
            }
            case "get_flat_pattern_mesh":
            {
                string id = (string?)p["document_id"] ?? PlateId;
                if (id == AssemblyId) return Fail(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "assembly");
                if (id == BoltId)
                    return InventorCommandResult.Fail(envelope.Id, InventorErrorCodes.INVALID_ARGUMENT, "No flat pattern.",
                        new JObject { ["reason"] = "FLAT_PATTERN_MISSING" }, meta);
                var flat = Box(5.0, id);
                flat.Faces.ForEach(f => f.FaceId = null);
                return Ok(new JObject
                {
                    ["document_id"] = id, ["definition_name"] = id + " (flat pattern)", ["revision"] = Revision, ["visual_revision"] = Visual,
                    ["source"] = "SurfaceBodies", ["thickness_mm"] = 2.0,
                    ["mesh_bbox_mm"] = new JObject { ["size_mm"] = new JArray(50, 50, 50), ["thin_axis"] = "Z" },
                    ["flat_pattern"] = new JObject { ["exists"] = true, ["length_mm"] = 50.0, ["width_mm"] = 50.0, ["bend_count"] = 1 },
                    ["flat_pattern_identity"] = new JObject { ["content_hash"] = "fake-content", ["hash"] = "fake-" + Revision },
                    ["edit_state_before"] = new JObject { ["flat_pattern_edit_active"] = false },
                    ["edit_state_after"] = new JObject { ["flat_pattern_edit_active"] = false },
                    ["left_edit_mode_restored"] = null,
                    ["bodies"] = new JArray(MeshPayload.ToJson(flat)),
                });
            }
            case "history_xr":
            {
                lock (_gate)
                {
                    if ((string?)p["document_id"] != ActiveDocumentId) return Fail("DOCUMENT_CHANGED","Document changed.");
                    if ((string?)p["expected_revision"] != Revision) return Fail("STALE_REVISION","Revision changed.");
                    if (_historyRevision != Revision) { _history.Clear(); _historyPosition = 0; _historyTicket = null; }
                    string owner = (string)p["owner"]!, action = (string)p["action"]!;
                    bool Undo() => _historyPosition > 0 && _history[_historyPosition-1].Owner == owner;
                    bool Redo() => _historyPosition < _history.Count && _history[_historyPosition].Owner == owner;
                    if (action != "status")
                    {
                        if ((string?)p["ticket"] != _historyTicket || !(action == "redo" ? Redo() : Undo()))
                            return Fail("HISTORY_CHANGED","No matching XR history.");
                        if (action == "redo") _boltLengthCm = _history[_historyPosition++].After;
                        else _boltLengthCm = _history[--_historyPosition].Before;
                        _sequence++; _visual++; _historyRevision = Revision; _historyTicket = System.Guid.NewGuid().ToString("N");
                    }
                    return Ok(new JObject { ["document_id"] = ActiveDocumentId, ["revision"] = Revision, ["visual_revision"] = Visual,
                        ["status"] = action == "status" ? "history" : "committed", ["can_undo"] = Undo(), ["can_redo"] = Redo(),
                        ["ticket"] = Undo() || Redo() ? _historyTicket : null });
                }
            }
            case "atomic_batch":
            {
                if ((string?)p["document_id"] != ActiveDocumentId)
                    return Fail(InventorErrorCodes.DOCUMENT_CHANGED, "Active document differs.");
                if ((string?)p["expected_revision"] != Revision)
                    return Fail(InventorErrorCodes.STALE_REVISION, "STALE_REVISION: read the document again and replan.");
                bool preview = (bool?)p["preview"] ?? false;
                var ops = (JArray)p["operations"]!;
                var set = ops.OfType<JObject>().FirstOrDefault(o => (string?)o["command"] == "set_parameter");
                if (!preview && set != null)
                {
                    lock (_gate)
                    {
                        string beforeRevision = Revision; double beforeLength = _boltLengthCm;
                        _boltLengthCm = double.Parse(((string)set["arguments"]!["value"]!).Replace("mm", "").Trim(), System.Globalization.CultureInfo.InvariantCulture) / 10;
                        _sequence++;
                        _visual++;
                        if (p["history_owner"]?.Type == JTokenType.String)
                        {
                            if (_historyRevision != beforeRevision) { _history.Clear(); _historyPosition = 0; }
                            _history.RemoveRange(_historyPosition,_history.Count-_historyPosition);
                            _history.Add(((string)p["history_owner"]!,beforeLength,_boltLengthCm)); _historyPosition = _history.Count;
                            _historyRevision = Revision; _historyTicket = System.Guid.NewGuid().ToString("N");
                        }
                    }
                }
                if (preview) { lock (_gate) _history.RemoveRange(_historyPosition,_history.Count-_historyPosition); }
                var outcome = new JObject
                {
                    ["status"] = preview ? "preview_rolled_back" : "committed", ["document_id"] = ActiveDocumentId, ["revision"] = Revision,
                    ["steps"] = new JArray(ops.Select(o => new JObject { ["command"] = o["command"], ["data"] = new JObject() })),
                    ["validated"] = new JArray("rebuild", DesignPartMode ? "feature_health" : "constraint_health"),
                };
                if ((bool?)p["include_preview_mesh"] == true)
                {
                    if (!preview || !DesignPartMode) return Fail(InventorErrorCodes.INVALID_ARGUMENT, "Part preview required.");
                    double tentative = set == null ? _boltLengthCm : double.Parse(((string)set["arguments"]!["value"]!).Replace("mm", "").Trim(), System.Globalization.CultureInfo.InvariantCulture) / 10;
                    outcome["preview_mesh"] = new JObject
                    {
                        ["document_id"] = ActiveDocumentId, ["definition_name"] = "Preview", ["units"] = "cm",
                        ["bodies"] = new JArray(MeshPayload.ToJson(Box(tentative, ActiveDocumentId))),
                    };
                    var created = ops.OfType<JObject>().FirstOrDefault(o => (string?)o["command"] == "create_sketch");
                    var snapshots = new JArray();
                    if (created != null)
                    {
                        var entities = new JArray();
                        foreach (var circle in ops.OfType<JObject>().Where(o => (string?)o["command"] == "draw_circle"))
                            entities.Add(new JObject { ["type"] = "circle", ["center_mm"] = new JArray(circle["arguments"]!["cx"],circle["arguments"]!["cy"]), ["radius_mm"] = circle["arguments"]!["radius"] });
                        snapshots.Add(new JObject { ["sketch_name"] = created["arguments"]!["name"], ["visible"] = true,
                            ["frame"] = new JObject { ["origin_mm"] = new JArray(0,0,0), ["x_axis"] = new JArray(1,0,0), ["y_axis"] = new JArray(0,1,0) }, ["entities"] = entities });
                    }
                    outcome["preview_mesh"]!["sketches"] = snapshots;
                }
                return Ok(outcome);
            }
            default:
                return Fail(InventorErrorCodes.INVALID_ARGUMENT, "unknown command: " + envelope.Command);
        }
    }

    private static double[] Translate(double x, double y, double z) => new[] { 1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z, 0, 0, 0, 1.0 };

    private JObject Scene() => new()
    {
        ["document_id"] = AssemblyId, ["kind"] = "assembly", ["revision"] = Revision, ["visual_revision"] = Visual, ["matrix_space"] = "assembly",
        ["root"] = new JObject
        {
            ["name"] = "Fake.iam", ["definition_document_id"] = AssemblyId, ["definition_kind"] = "assembly",
            ["children"] = new JArray(
                Leaf("Bolt:1", "ent_occ_1", BoltId, Translate(0, 0, 0)),
                Leaf("Bolt:2", "ent_occ_2", BoltId, Translate(3, 0, 0)),
                Leaf("Plate:1", "ent_occ_3", PlateId, Translate(-1, -1, -1))),
        },
    };

    private static JObject Leaf(string name, string occurrence, string definition, double[] matrix) => new()
    {
        ["name"] = name, ["occurrence_id"] = occurrence, ["definition_document_id"] = definition, ["definition_kind"] = "part",
        ["visible"] = true, ["suppressed"] = false, ["matrix_rowmajor_cm"] = new JArray(matrix.Cast<object>().ToArray()), ["children"] = new JArray(),
    };

    /// <summary>A closed box of edge <paramref name="s"/> cm: six faces, twelve triangles, per-face vertices.</summary>
    internal static MeshPayload.Body Box(double s, string id)
    {
        var f = (float)s;
        var faces = new (float[] n, float[][] v)[]
        {
            (new float[] { 0, 0, -1 }, new[] { new float[] { 0, 0, 0 }, new[] { f, 0, 0 }, new[] { f, f, 0 }, new[] { 0, f, 0f } }),
            (new float[] { 0, 0, 1 }, new[] { new float[] { 0, 0, f }, new[] { 0, f, f }, new[] { f, f, f }, new[] { f, 0, f } }),
            (new float[] { 0, -1, 0 }, new[] { new float[] { 0, 0, 0 }, new[] { 0, 0, f }, new[] { f, 0, f }, new[] { f, 0, 0f } }),
            (new float[] { 0, 1, 0 }, new[] { new float[] { 0, f, 0 }, new[] { f, f, 0 }, new[] { f, f, f }, new[] { 0, f, f } }),
            (new float[] { -1, 0, 0 }, new[] { new float[] { 0, 0, 0 }, new[] { 0, f, 0 }, new[] { 0, f, f }, new[] { 0, 0, f } }),
            (new float[] { 1, 0, 0 }, new[] { new float[] { f, 0, 0 }, new[] { f, 0, f }, new[] { f, f, f }, new[] { f, f, 0f } }),
        };
        var body = new MeshPayload.Body { Index = 1, Name = "Solid1" };
        var positions = new List<float>(); var normals = new List<float>(); var indices = new List<uint>();
        int ordinal = 0;
        foreach (var (n, v) in faces)
        {
            ordinal++;
            uint start = (uint)(positions.Count / 3);
            foreach (var vertex in v) { positions.AddRange(vertex); normals.AddRange(n); }
            int first = indices.Count;
            indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            body.Faces.Add(new MeshPayload.FaceRange { FaceId = "ent_" + id + "_f" + ordinal, Ordinal = ordinal, FirstIndex = first, IndexCount = 6, Surface = "kPlaneSurface" });
        }
        body.Positions = positions.ToArray(); body.Normals = normals.ToArray(); body.Indices = indices.ToArray();
        return body;
    }

    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await _heartbeat.DisposeAsync();
        _stop.Cancel();
        try { await _loop; } catch { }
    }
}
