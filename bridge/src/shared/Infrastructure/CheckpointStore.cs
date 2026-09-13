using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>Host-owned, no-overwrite standalone IPT snapshots. Never trusts paths from metadata.</summary>
public sealed class CheckpointStore
{
    private readonly string _root;
    public CheckpointStore(string root) => _root = Path.GetFullPath(root);
    public static void ValidateLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 120 || label.Any(char.IsControl))
            throw new ArgumentException("Checkpoint label must contain 1-120 printable characters.");
    }
    public JObject Create(string documentId, string revision, string label, Action<string> write, Func<bool>? expired = null, JObject? semantic = null)
    {
        ValidateLabel(label);
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(revision)) throw new ArgumentException("Document identity/revision required.");
        string file = SafeArtifactWriter.Write(_root, ".ipt", write, expired);
        string directory = Path.GetDirectoryName(file)!;
        string id = "cp_" + Path.GetFileName(directory);
        var metadata = new JObject { ["id"] = id, ["document_id"] = documentId, ["source_revision"] = revision,
            ["label"] = label, ["created_utc"] = DateTime.UtcNow.ToString("O"), ["sha256"] = Hash(file), ["bytes"] = new FileInfo(file).Length };
        if (semantic != null)
        {
            string semanticPath = Path.Combine(directory, "semantic.json");
            using (var writer = new StreamWriter(new FileStream(semanticPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))) writer.Write(semantic.ToString());
            if (new FileInfo(semanticPath).Length > 4 * 1024 * 1024) throw new IOException("Semantic snapshot exceeds 4 MiB; checkpoint not cataloged.");
            metadata["semantic_sha256"] = Hash(semanticPath);
        }
        string pending = Path.Combine(directory, "pending.json");
        using (var writer = new StreamWriter(new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))) writer.Write(metadata.ToString());
        if (expired?.Invoke() == true) throw new TimeoutException("Checkpoint catalog not published; snapshot retained.");
        File.Move(pending, Path.Combine(directory, "checkpoint.json"));
        return metadata;
    }
    private string DirectoryFor(string id)
    {
        if (id == null || !id.StartsWith("cp_", StringComparison.Ordinal) || !Guid.TryParseExact(id.Substring(3), "N", out var key))
            throw new ArgumentException("Invalid checkpoint ID.");
        var directory = Path.Combine(_root, key.ToString("N"));
        SafeArtifactWriter.CheckAncestors(directory);
        return directory;
    }
    public JObject Read(string id, bool verify = true)
    {
        string directory = DirectoryFor(id), metadataPath = Path.Combine(directory, "checkpoint.json");
        SafeArtifactWriter.CheckAncestors(metadataPath);
        var size = new FileInfo(metadataPath).Length;
        if (size == 0 || size > 32768) throw new IOException("Invalid checkpoint metadata size.");
        var metadata = JObject.Parse(File.ReadAllText(metadataPath));
        if ((string?)metadata["id"] != id || metadata["document_id"]?.Type != JTokenType.String ||
            string.IsNullOrWhiteSpace((string?)metadata["document_id"]) || metadata["sha256"]?.Type != JTokenType.String ||
            ((string)metadata["sha256"]!).Length != 64 || metadata["bytes"]?.Type != JTokenType.Integer)
            throw new IOException("Invalid checkpoint metadata.");
        string file = Path.Combine(directory, "model.ipt");
        SafeArtifactWriter.CheckAncestors(file);
        if (verify && (new FileInfo(file).Length != (long)metadata["bytes"]! || Hash(file) != (string?)metadata["sha256"]))
            throw new IOException("CHECKPOINT_INTEGRITY_FAILED");
        return metadata;
    }
    public JObject List(string? documentId = null)
    {
        SafeArtifactWriter.CheckAncestors(_root);
        var entries = new JArray();
        if (!Directory.Exists(_root)) return new JObject { ["checkpoints"] = entries, ["truncated"] = false };
        bool truncated = false;
        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            string name = Path.GetFileName(directory);
            if (!Guid.TryParseExact(name, "N", out _) || !File.Exists(Path.Combine(directory, "checkpoint.json"))) continue;
            if (entries.Count == 100) { truncated = true; break; }
            try
            {
                var entry = Read("cp_" + name, verify: false);
                if (documentId == null || (string?)entry["document_id"] == documentId)
                { entry["integrity"] = "not_checked"; entries.Add(entry); }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is Newtonsoft.Json.JsonException || ex is UnauthorizedAccessException)
            {
                if (documentId == null) entries.Add(new JObject { ["id"] = "cp_" + name, ["integrity"] = "invalid_metadata" });
            }
        }
        return new JObject { ["checkpoints"] = entries, ["truncated"] = truncated };
    }
    public string RecoveryCopy(string id, string outputRoot, Func<bool>? expired = null)
    {
        var metadata = Read(id);
        string source = Path.Combine(DirectoryFor(id), "model.ipt");
        return SafeArtifactWriter.Write(outputRoot, ".ipt", output =>
        {
            File.Copy(source, output, overwrite: false);
            if (Hash(output) != (string?)metadata["sha256"]) throw new IOException("CHECKPOINT_INTEGRITY_FAILED");
        }, expired);
    }
    public JObject ReadSemantic(string id)
    {
        var metadata = Read(id);
        if (metadata["semantic_sha256"]?.Type != JTokenType.String) throw new IOException("SEMANTIC_SNAPSHOT_UNAVAILABLE: create a new checkpoint with this version.");
        string path = Path.Combine(DirectoryFor(id), "semantic.json");
        SafeArtifactWriter.CheckAncestors(path);
        if (new FileInfo(path).Length > 4 * 1024 * 1024 || Hash(path) != (string?)metadata["semantic_sha256"])
            throw new IOException("SEMANTIC_INTEGRITY_FAILED");
        var snapshot = JObject.Parse(File.ReadAllText(path));
        if ((string?)snapshot["document_id"] != (string?)metadata["document_id"]) throw new IOException("SEMANTIC_DOCUMENT_MISMATCH");
        return snapshot;
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
