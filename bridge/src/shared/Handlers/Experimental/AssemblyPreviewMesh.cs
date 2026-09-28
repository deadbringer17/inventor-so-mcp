#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Captures occurrence B-Rep in assembly coordinates, including solver-driven neighbors.</summary>
internal static class AssemblyPreviewMesh
{
    public static JObject Capture(InventorCommandContext ctx, AssemblyDocument assembly)
    {
        var bodies = new JArray();
        int triangles = 0, nodes = 0;
        void Visit(ComponentOccurrence occurrence, bool visible)
        {
            X.Deadline(ctx, "capturing assembly preview");
            if (++nodes > 2000) throw new ArgumentException("Assembly preview exceeds 2000 occurrences.");
            if (occurrence.Suppressed) return;
            visible &= occurrence.Visible;
            if (occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject)
            {
                foreach (ComponentOccurrence child in occurrence.SubOccurrences) Visit(child, visible);
                return;
            }
            if (occurrence.Definition is VirtualComponentDefinition) return;
            foreach (SurfaceBody body in occurrence.SurfaceBodies)
            {
                var positions = new List<float>(); var normals = new List<float>(); var indices = new List<uint>();
                var mesh = new MeshPayload.Body { Index = bodies.Count + 1, Name = occurrence.Name + "/" + body.Name,
                    Visible = visible && body.Visible };
                int ordinal = 0;
                foreach (Face face in body.Faces)
                {
                    X.Deadline(ctx, "tessellating assembly preview"); ordinal++;
                    dynamic source = face;
                    int vertices = 0, facets = 0;
                    double[] coordinates = new double[0], vectors = new double[0]; int[] nativeIndices = new int[0];
                    source.CalculateFacets(0.01, out vertices, out facets, out coordinates, out vectors, out nativeIndices);
                    if (vertices == 0 || facets == 0) continue;
                    triangles += facets;
                    if (triangles > 500000) throw new CodedFailureException(InventorErrorCodes.MESH_TOO_LARGE, "Assembly preview exceeds 500000 triangles.");
                    uint offset = (uint)(positions.Count / 3);
                    int first = indices.Count, indexBase = MeshPayload.DetectIndexBase(nativeIndices, vertices);
                    for (int i = 0; i < vertices * 3; i++)
                    { positions.Add((float)coordinates[i]); normals.Add(vectors.Length > i ? (float)vectors[i] : 0); }
                    for (int i = 0; i < facets * 3; i++) indices.Add(offset + (uint)(nativeIndices[i] - indexBase));
                    mesh.Faces.Add(new MeshPayload.FaceRange { Ordinal = ordinal, FirstIndex = first, IndexCount = facets * 3,
                        Surface = face.SurfaceType.ToString() });
                }
                mesh.Positions = positions.ToArray(); mesh.Normals = normals.ToArray(); mesh.Indices = indices.ToArray();
                bodies.Add(MeshPayload.ToJson(mesh));
            }
        }
        foreach (ComponentOccurrence occurrence in assembly.ComponentDefinition.Occurrences) Visit(occurrence, true);
        return new JObject { ["document_id"] = EntityReferences.DocumentId((global::Inventor.Document)assembly),
            ["definition_name"] = assembly.DisplayName, ["units"] = "cm", ["bodies"] = bodies,
            ["triangle_count"] = triangles, ["sketches"] = new JArray(), ["kind"] = "assembly" };
    }
}
#endif
