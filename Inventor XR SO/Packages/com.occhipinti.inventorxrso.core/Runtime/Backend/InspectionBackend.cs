using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class OpenDocument
    {
        public string Id { get; }
        public string Name { get; }
        public string Kind { get; }
        public OpenDocument(string id, string name, string kind) { Id = id; Name = name; Kind = kind; }
    }

    public sealed class InspectionInfo
    {
        public string Name { get; private set; }
        public string Material { get; private set; }
        public double? MassKg { get; private set; }
        public double? VolumeMm3 { get; private set; }
        public double? AreaMm2 { get; private set; }
        public int? Constraints { get; private set; }
        public int? TranslationDof { get; private set; }
        public int? RotationDof { get; private set; }
        public static InspectionInfo FromJson(JObject json) => new InspectionInfo
        {
            Name = (string)json["name"], Material = (string)json["material"],
            MassKg = Number(json["mass_kg"]), VolumeMm3 = Number(json["volume_mm3"]), AreaMm2 = Number(json["area_mm2"]),
            Constraints = (int?)json["constraints"], TranslationDof = (int?)json["dof_translation"], RotationDof = (int?)json["dof_rotation"]
        };
        private static double? Number(JToken token)
        {
            double? value = (double?)token;
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) ? value : null;
        }
    }

    public interface IInspectionBackend
    {
        Task<InspectionInfo> InspectAsync(DocumentState state, string occurrenceId, CancellationToken ct);
        Task<IReadOnlyList<OpenDocument>> ListOpenAsync(CancellationToken ct);
        Task ActivateOpenAsync(string documentId, CancellationToken ct);
    }

    public sealed partial class InventorBackend : IInspectionBackend
    {
        public async Task<InspectionInfo> InspectAsync(DocumentState state, string occurrenceId, CancellationToken ct) =>
            InspectionInfo.FromJson(await _mcp.CallToolAsync("inventor_inspect_xr", new JObject
            {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision,
                ["occurrence_id"] = occurrenceId
            }, ct));

        public async Task<IReadOnlyList<OpenDocument>> ListOpenAsync(CancellationToken ct)
        {
            var json = await _mcp.CallToolAsync("inventor_list_open_documents", new JObject(), ct);
            return (json["documents"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(d => d["document_id"]?.Type == JTokenType.String)
                .Select(d => new OpenDocument((string)d["document_id"], (string)d["title"], (string)d["document_type"]))
                .ToArray();
        }

        public Task ActivateOpenAsync(string documentId, CancellationToken ct) =>
            _mcp.CallToolAsync("inventor_activate_open_document_xr", new JObject { ["document_id"] = documentId }, ct);
    }
}
