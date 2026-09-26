using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class CapabilitiesInfo
    {
        public string ServerVersion { get; private set; }
        public bool ServerExperimentalEnabled { get; private set; }
        public int? InventorYear { get; private set; }
        public bool Reachable { get; private set; }
        public string UnreachableReason { get; private set; }
        public bool AddInExperimentalEnabled { get; private set; }
        public string ActiveDocumentKind { get; private set; }
        public bool XrMesh { get; private set; }
        public bool SceneGraph { get; private set; }
        public bool Highlight { get; private set; }
        public bool EventSubscriptions { get; private set; }
        public bool IsXrReady => Reachable && XrMesh && SceneGraph && Highlight;

        public static CapabilitiesInfo FromJson(JObject json)
        {
            var server = json["server"] ?? new JObject();
            var target = json["target"] ?? new JObject();
            var flags = json["capabilities"] ?? new JObject();
            return new CapabilitiesInfo
            {
                ServerVersion = (string)server["version"],
                ServerExperimentalEnabled = (bool?)server["experimental_enabled"] ?? false,
                InventorYear = (int?)target["inventor_year"],
                Reachable = (bool?)target["reachable"] ?? false,
                UnreachableReason = (string)target["reason"],
                AddInExperimentalEnabled = (bool?)target["add_in_experimental_enabled"] ?? false,
                ActiveDocumentKind = (string)target["active_document_kind"],
                XrMesh = (bool?)flags["xr_mesh"] ?? false,
                SceneGraph = (bool?)flags["scene_graph"] ?? false,
                Highlight = (bool?)flags["highlight"] ?? false,
                EventSubscriptions = (bool?)flags["event_subscriptions"] ?? false,
            };
        }
    }

    public sealed class DocumentState
    {
        public DocumentState(string documentId, string revision, string visualRevision)
        {
            DocumentId = documentId;
            Revision = revision;
            VisualRevision = visualRevision;
        }

        public string DocumentId { get; }
        public string Revision { get; }
        public string VisualRevision { get; }

        public static DocumentState FromJson(JObject json) =>
            new DocumentState((string)json["document_id"], (string)json["revision"], (string)json["visual_revision"]);
    }

    public sealed class SceneNode
    {
        private static readonly float[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        public string Name { get; private set; }
        public string OccurrenceId { get; private set; }
        public string DefinitionDocumentId { get; private set; }
        public string DefinitionKind { get; private set; }
        public bool Visible { get; private set; }
        public bool Suppressed { get; private set; }
        /// <summary>Column-major, metres, top-level assembly space (identity for the root).</summary>
        public float[] MatrixGltf { get; private set; }
        public IReadOnlyList<SceneNode> Children { get; private set; }

        public static SceneNode FromJson(JToken json) => new SceneNode
        {
            Name = (string)json["name"],
            OccurrenceId = (string)json["occurrence_id"],
            DefinitionDocumentId = (string)json["definition_document_id"],
            DefinitionKind = (string)json["definition_kind"],
            Visible = (bool?)json["visible"] ?? true,
            Suppressed = (bool?)json["suppressed"] ?? false,
            MatrixGltf = json["matrix_gltf"] is JArray m && m.Count == 16 ? m.Select(v => (float)v).ToArray() : (float[])Identity.Clone(),
            Children = (json["children"] as JArray ?? new JArray()).Select(FromJson).ToList(),
        };
    }

    public sealed class PlacedPart
    {
        public PlacedPart(SceneNode node, float[] matrixGltf)
        {
            Node = node;
            MatrixGltf = matrixGltf;
        }

        public SceneNode Node { get; }
        public float[] MatrixGltf { get; }
    }

    public sealed class SceneGraph
    {
        public string DocumentId { get; private set; }
        public string Kind { get; private set; }
        public string Revision { get; private set; }
        public string VisualRevision { get; private set; }
        public bool Truncated { get; private set; }
        public SceneNode Root { get; private set; }
        public IReadOnlyList<string> DefinitionIds { get; private set; }
        public DocumentState State => new DocumentState(DocumentId, Revision, VisualRevision);

        public static SceneGraph FromJson(JObject json) => new SceneGraph
        {
            DocumentId = (string)json["document_id"],
            Kind = (string)json["kind"],
            Revision = (string)json["revision"],
            VisualRevision = (string)json["visual_revision"],
            Truncated = (bool?)json["truncated"] ?? false,
            Root = SceneNode.FromJson(json["root"] ?? new JObject()),
            DefinitionIds = (json["definition_document_ids"] as JArray ?? new JArray()).Select(v => (string)v).ToList(),
        };

        /// <summary>Every visible, unsuppressed part to draw, with its top-level transform (leaves already carry it).</summary>
        public IEnumerable<PlacedPart> PlacedParts() => Walk(Root);

        private static IEnumerable<PlacedPart> Walk(SceneNode node)
        {
            if (!node.Visible || node.Suppressed) yield break;
            if (node.DefinitionKind == "part" && node.DefinitionDocumentId != null)
            {
                yield return new PlacedPart(node, node.MatrixGltf);
                yield break;
            }
            foreach (var child in node.Children)
            foreach (var placed in Walk(child))
                yield return placed;
        }
    }

    public sealed class DefinitionMesh
    {
        public string DefinitionDocumentId { get; private set; }
        public string AssetId { get; private set; }
        public string AssetUrl { get; private set; }
        public string VisualRevision { get; private set; }

        public static DefinitionMesh FromJson(string definitionDocumentId, JObject json) => new DefinitionMesh
        {
            DefinitionDocumentId = definitionDocumentId,
            AssetId = (string)json["asset"]?["asset_id"],
            AssetUrl = (string)json["asset"]?["asset_url"],
            VisualRevision = (string)json["visual_revision"],
        };
    }
}
