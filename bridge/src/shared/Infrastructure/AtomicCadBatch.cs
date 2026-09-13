using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

public interface ICadBatchBackend
{
    string DocumentId { get; }
    string Revision { get; }
    void Begin(string name);
    JObject Execute(string command, JObject arguments);
    void Validate();
    void Commit();
    void Rollback();
}

/// <summary>A bounded single-request transaction, never held open across client calls.</summary>
public static class AtomicCadBatch
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "set_parameter", "create_parameter", "create_sketch", "draw_line", "draw_circle",
        "draw_rectangle", "draw_arc", "add_sketch_constraint", "add_sketch_dimension",
        "project_geometry", "close_sketch", "extrude", "revolve", "fillet", "chamfer",
        "hole", "circular_pattern", "rectangular_pattern", "create_work_plane", "create_work_axis",
        "set_sheet_metal_rule", "sheet_metal_face", "sheet_metal_flange", "sheet_metal_cut", "create_flat_pattern",
        "sheet_metal_hem", "sheet_metal_fold", "sheet_metal_contour_flange",
        "sheet_metal_corner_round", "sheet_metal_corner_chamfer", "sheet_metal_unfold", "sheet_metal_refold",
        "sheet_metal_punch", "draw_point", "sheet_metal_rip", "sheet_metal_lofted_flange"
    };

    public static JObject Run(ICadBatchBackend backend, string documentId, string expectedRevision,
        JArray operations, bool preview, Func<bool>? expired = null)
    {
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(expectedRevision))
            throw new ArgumentException("document_id and expected_revision are required.");
        if (operations.Count < 1 || operations.Count > 32) throw new ArgumentException("Use 1 to 32 operations per transaction.");
        // Validate the entire command surface before beginning or executing any operation.
        foreach (var token in operations)
            if (token is not JObject step || step["command"]?.Type != JTokenType.String ||
                !Allowed.Contains((string)step["command"]!) || step["arguments"] is not JObject)
                throw new ArgumentException("Every operation needs an allowed CAD command and an arguments object. Scripting, file IO, document lifecycle and nested batches are excluded.");
        if (backend.DocumentId != documentId) throw new InvalidOperationException("DOCUMENT_CHANGED: active document differs from the plan.");
        if (backend.Revision != expectedRevision) throw new InvalidOperationException("STALE_REVISION: read the document again and replan.");
        if (expired?.Invoke() == true) throw new TimeoutException("Batch expired before execution.");

        backend.Begin("Inventor SO atomic batch");
        var results = new JArray();
        int index = 0;
        try
        {
            foreach (JObject step in operations)
            {
                if (expired?.Invoke() == true) throw new TimeoutException("Batch deadline exceeded.");
                if (backend.DocumentId != documentId) throw new InvalidOperationException("DOCUMENT_CHANGED during batch.");
                results.Add(backend.Execute((string)step["command"]!, (JObject)step["arguments"]!));
                index++;
            }
            backend.Validate();
            if (expired?.Invoke() == true) throw new TimeoutException("Batch deadline exceeded before commit.");
            if (backend.DocumentId != documentId) throw new InvalidOperationException("DOCUMENT_CHANGED before commit.");
            if (preview) backend.Rollback(); else backend.Commit();
            return new JObject { ["status"] = preview ? "preview_rolled_back" : "committed", ["steps"] = results,
                ["document_id"] = documentId, ["revision"] = backend.Revision };
        }
        catch (Exception error)
        {
            try { backend.Rollback(); }
            catch (Exception rollbackError)
            {
                throw new InvalidOperationException("ROLLBACK_FAILED: inspect CAD before continuing. Step " + index + ": " + error.Message + "; rollback: " + rollbackError.Message, error);
            }
            throw new InvalidOperationException("ROLLED_BACK: step " + index + ": " + error.Message, error);
        }
    }
}
