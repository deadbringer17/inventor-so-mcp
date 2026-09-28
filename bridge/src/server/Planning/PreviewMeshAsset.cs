using System;
using System.Linq;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Planning;

/// <summary>Only publish a captured, rolled-back preview. Never use the live mesh cache.</summary>
public static class PreviewMeshAsset
{
    public static byte[] Build(JObject preview, string documentId, string revision)
    {
        if ((string?)preview["status"] != "preview_rolled_back"
            || (string?)preview["document_id"] != documentId || (string?)preview["revision"] != revision)
            throw new FormatException("Preview did not confirm rollback to the requested document/revision.");
        if (preview["preview_mesh"] is not JObject mesh || mesh["bodies"] is not JArray bodies
            || (string?)mesh["document_id"] != documentId || (string?)mesh["units"] != "cm")
            throw new FormatException("Add-in did not return the requested preview geometry.");
        if (bodies.Any(b => b is not JObject)) throw new FormatException("Invalid preview body.");
        var decoded = bodies.Cast<JObject>().Select(MeshPayload.FromJson).ToArray();
        // Even a misconfigured add-in must not expose references created in an aborted transaction.
        foreach (var body in decoded)
            foreach (var face in body.Faces) face.FaceId = null;
        return GlbBuilder.BuildDefinition(new GlbBuilder.MeshSource
        {
            DocumentId = documentId, Name = (string?)mesh["definition_name"] ?? "Preview", Bodies = decoded,
        });
    }
}
