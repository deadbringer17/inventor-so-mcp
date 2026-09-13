#if INVENTOR2027
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Core;

internal static class EntityReferences
{
    public static string DocumentId(global::Inventor.Document doc) => "doc_" + doc.InternalName;

    public static JObject Describe(global::Inventor.Document doc, object entity)
    {
        var manager = doc.ReferenceKeyManager;
        int context = manager.CreateKeyContext();
        try
        {
            byte[] key = Array.Empty<byte>();
            string type;
            switch (entity)
            {
                case FaceProxy face: face.GetReferenceKey(ref key, context); type = "face_proxy"; break;
                case Face face: face.GetReferenceKey(ref key, context); type = "face"; break;
                case EdgeProxy edge: edge.GetReferenceKey(ref key, context); type = "edge_proxy"; break;
                case Edge edge: edge.GetReferenceKey(ref key, context); type = "edge"; break;
                case VertexProxy vertex: vertex.GetReferenceKey(ref key, context); type = "vertex_proxy"; break;
                case Vertex vertex: vertex.GetReferenceKey(ref key, context); type = "vertex"; break;
                case ComponentOccurrenceProxy occurrence: occurrence.GetReferenceKey(ref key, context); type = "occurrence_proxy"; break;
                case ComponentOccurrence occurrence: occurrence.GetReferenceKey(ref key, context); type = "occurrence"; break;
                case AssemblyConstraint constraint: constraint.GetReferenceKey(ref key, context); type = "assembly_constraint"; break;
                default: return new JObject { ["supported"] = false, ["reason"] = "Entity type is not supported by this reference adapter yet." };
            }
            byte[] data = Array.Empty<byte>();
            manager.SaveContextToArray(context, ref data);
            var reference = new PersistentEntityReference { DocumentId = DocumentId(doc), EntityType = type, Key = key, Context = data };
            return new JObject { ["id"] = reference.Encode(), ["document_id"] = reference.DocumentId,
                ["type"] = type, ["supported"] = true };
        }
        finally { manager.ReleaseKeyContext(context); }
    }

    public static JObject Resolve(Application app, string id)
    {
        var reference = PersistentEntityReference.Decode(id);
        var matches = new List<global::Inventor.Document>();
        foreach (global::Inventor.Document document in app.Documents)
            if (DocumentId(document) == reference.DocumentId) matches.Add(document);
        if (matches.Count != 1) return new JObject { ["status"] = matches.Count == 0 ? "document_not_open" : "ambiguous_document", ["id"] = id };
        var doc = matches[0];
        var manager = doc.ReferenceKeyManager;
        var bytes = reference.Context;
        int context = manager.LoadContextFromArray(ref bytes);
        try
        {
            var key = reference.Key;
            object entity = null!;
            object details = null!;
            bool bound = manager.CanBindKeyToObject(ref key, context, out entity, out details);
            var result = new JObject { ["id"] = id, ["document_id"] = reference.DocumentId,
                ["type"] = reference.EntityType, ["status"] = bound ? "resolved" : "unresolved" };
            if (entity is ObjectCollection candidates)
            {
                result["status"] = "ambiguous";
                result["candidate_count"] = candidates.Count;
            }
            if (details is NameValueMap map)
            {
                foreach (var field in new[] { "ObjectSuppressed", "ObjectMissing", "ObjectDeleted", "MatchType" })
                {
                    try { result[field] = JToken.FromObject(map.Value[field]); }
                    catch { /* Optional key absent, not an invented default. */ }
                }
            }
            return result;
        }
        finally { manager.ReleaseKeyContext(context); }
    }

    public static Edge ResolvePartEdge(global::Inventor.Document document, string id)
        => ResolvePartEntity(document, id, "edge") as Edge
            ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: resolved object is not an edge.");

    public static ComponentOccurrence ResolveOccurrence(global::Inventor.Document document, string id)
        => ResolvePartEntity(document, id, "occurrence") as ComponentOccurrence
            ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: resolved object is not an occurrence.");

    public static AssemblyConstraint ResolveAssemblyConstraint(global::Inventor.Document document, string id)
        => ResolvePartEntity(document, id, "assembly_constraint") as AssemblyConstraint
            ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: resolved object is not an assembly constraint.");

    public static FaceProxy ResolvePlanarAssemblyFace(global::Inventor.Document document, string id)
    {
        var face = ResolvePartEntity(document, id, "face_proxy") as FaceProxy
            ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: an assembly face proxy is required.");
        if (face.SurfaceType != SurfaceTypeEnum.kPlaneSurface)
            throw new ArgumentException("REFERENCE_NOT_PLANAR: a planar assembly face is required.");
        return face;
    }

    /// <summary>Any part face by portable id. Bend faces are cylindrical, so planarity is not required.</summary>
    public static Face ResolvePartEntityFace(global::Inventor.Document document, string id)
        => ResolvePartEntity(document, id, "face") as Face
            ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: resolved object is not a face.");

    public static Face ResolvePlanarPartFace(global::Inventor.Document document, string id)
    {
        var face = ResolvePartEntity(document, id, "face") as Face
            ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: resolved object is not a face.");
        if (face.SurfaceType != SurfaceTypeEnum.kPlaneSurface)
            throw new ArgumentException("REFERENCE_NOT_PLANAR: sketch support must be planar.");
        return face;
    }

    private static object ResolvePartEntity(global::Inventor.Document document, string id, string entityType)
    {
        var reference = PersistentEntityReference.Decode(id);
        if (reference.DocumentId != DocumentId(document))
            throw new ArgumentException("REFERENCE_DOCUMENT_MISMATCH: entity belongs to another document.");
        if (reference.EntityType != entityType)
            throw new ArgumentException("REFERENCE_TYPE_MISMATCH: a native part " + entityType + " is required.");
        var manager = document.ReferenceKeyManager;
        var bytes = reference.Context;
        int context = manager.LoadContextFromArray(ref bytes);
        try
        {
            var key = reference.Key;
            object entity;
            object details;
            if (!manager.CanBindKeyToObject(ref key, context, out entity, out details))
                throw new ArgumentException("REFERENCE_UNRESOLVED: inspect the model again.");
            if (entity is ObjectCollection)
                throw new ArgumentException("REFERENCE_AMBIGUOUS: refusing to choose an entity.");
            return entity;
        }
        finally { manager.ReleaseKeyContext(context); }
    }
}
#endif
