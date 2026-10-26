using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Capture;

public sealed record CaptureFile(string Path, long Size, string Sha256);
public sealed record CaptureUpload(string ScanId, CaptureFile[] Files);
public sealed class CaptureTransferException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>Resumable immutable binary exchange, isolated by the SHA256 of the bearer token, never by its display name.</summary>
public sealed class CaptureTransferStore
{
    public const int MaxChunkBytes = 4*1024*1024;
    public const long OwnerQuotaBytes = 2L*1024*1024*1024;
    public const long TotalQuotaBytes = 8L*1024*1024*1024;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly string _root;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string,int> _leases = new(StringComparer.Ordinal);
    private sealed record Entry(string Owner, CaptureUpload Manifest, DateTimeOffset CreatedAt)
    {
        public long Size => Manifest.Files.Sum(f => f.Size);
    }

    public CaptureTransferStore(string root, Func<DateTimeOffset>? now = null)
    {
        _root = System.IO.Path.GetFullPath(root); _now = now ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(_root);
        // The transfer root is application-owned. Only validated ids are used for any access or deletion.
        foreach (string ownerDir in Directory.EnumerateDirectories(_root))
        foreach (string sessionDir in Directory.EnumerateDirectories(ownerDir))
        {
            string owner = System.IO.Path.GetFileName(ownerDir), id = System.IO.Path.GetFileName(sessionDir);
            if (!ValidOwner(owner) || !ValidId(id)) continue;
            string path = System.IO.Path.Combine(sessionDir,"upload.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 1024*1024) continue;
            try
            {
                var entry = JsonConvert.DeserializeObject<Entry>(File.ReadAllText(path))!;
                Validate(entry.Manifest);
                if (entry.Owner != owner || entry.Manifest.ScanId != id) continue;
                _entries.Add(Key(owner,id),entry);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or CaptureTransferException) { }
        }
    }

    public static string TokenOwner(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static bool ValidId(string id) => Guid.TryParseExact(id,"N",out _);
    private static bool ValidOwner(string owner) => owner.Length == 64 && owner.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Key(string owner,string id) => owner + "/" + id;

    public object Create(string owner, CaptureUpload upload)
    {
        Validate(upload);
        lock (_gate)
        {
            Prune();
            if (!ValidOwner(owner)) throw new CaptureTransferException(401,"UNAUTHORIZED");
            if (_entries.TryGetValue(Key(owner,upload.ScanId),out var existing))
            {
                if (JsonConvert.SerializeObject(existing.Manifest) != JsonConvert.SerializeObject(upload)) throw new CaptureTransferException(409,"CAPTURE_MANIFEST_CONFLICT");
                return DescribeLocked(existing);
            }
            long size = upload.Files.Sum(f => f.Size);
            var owned = _entries.Values.Where(e => e.Owner == owner).ToArray();
            if (owned.Length >= 16 || _entries.Count >= 128 || size > OwnerQuotaBytes - owned.Sum(e => e.Size) ||
                size > TotalQuotaBytes - _entries.Values.Sum(e => e.Size)) throw new CaptureTransferException(413,"CAPTURE_QUOTA_EXCEEDED");
            var entry = new Entry(owner,upload,_now());
            string directory = DirectoryFor(owner,upload.ScanId);
            Directory.CreateDirectory(directory);
            string temporary = System.IO.Path.Combine(directory,"upload.json.pending");
            try
            {
                WriteNew(temporary,Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(entry)));
                File.Move(temporary,System.IO.Path.Combine(directory,"upload.json"));
                _entries.Add(Key(owner,upload.ScanId),entry);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return DescribeLocked(entry);
        }
    }

    public object Describe(string owner,string id)
    { lock (_gate) { Prune(); return DescribeLocked(Require(owner,id)); } }

    public void PruneExpired() { lock (_gate) Prune(); }
    internal string Root=>_root;

    internal IReadOnlyList<(string Owner,string Id,string Directory)> Sessions()
    {
        lock(_gate) return _entries.Values.Select(e=>(e.Owner,e.Manifest.ScanId,DirectoryFor(e.Owner,e.Manifest.ScanId))).ToArray();
    }

    public SnapshotLease AcquireFinalized(string owner,string id)
    {
        lock(_gate)
        {
            Prune(); var entry=Require(owner,id); string directory=DirectoryFor(owner,id);
            if(!File.Exists(System.IO.Path.Combine(directory,"finalized.json"))) throw new CaptureTransferException(409,"CAPTURE_NOT_FINALIZED");
            string key=Key(owner,id); _leases[key]=_leases.GetValueOrDefault(key)+1;
            return new SnapshotLease(this,key,directory,entry.Manifest.Files.ToArray());
        }
    }
    public sealed class SnapshotLease : IDisposable
    {
        private CaptureTransferStore? _store; private readonly string _key;
        internal string Directory {get;}
        private readonly CaptureFile[] _files;
        internal SnapshotLease(CaptureTransferStore store,string key,string directory,CaptureFile[] files)
        { _store=store; _key=key; Directory=directory; _files=files; }
        public async Task CopyToAsync(string destination,CancellationToken ct)
        {
            if(_store==null) throw new ObjectDisposedException(nameof(SnapshotLease));
            for(int i=0;i<_files.Length;i++)
            {
                ct.ThrowIfCancellationRequested(); string target=System.IO.Path.Combine(destination,_files[i].Path.Replace('/',System.IO.Path.DirectorySeparatorChar));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                await using(var source=new FileStream(ChunkPath(Directory,i),FileMode.Open,FileAccess.Read,FileShare.Read,65536,true))
                await using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                {
                    if(source.Length!=_files[i].Size) throw new CaptureTransferException(409,"CAPTURE_INTEGRITY_ERROR");
                    await source.CopyToAsync(output,ct); await output.FlushAsync(ct);
                }
                if(FileHash(target)!=_files[i].Sha256) throw new CaptureTransferException(409,"CAPTURE_INTEGRITY_ERROR");
            }
        }
        public void Dispose()
        {
            var store=Interlocked.Exchange(ref _store,null); if(store==null) return;
            lock(store._gate) { if(--store._leases[_key]==0) store._leases.Remove(_key); }
        }
    }

    private object DescribeLocked(Entry entry)
    {
        string dir = DirectoryFor(entry.Owner,entry.Manifest.ScanId);
        int[] received = Enumerable.Range(0,entry.Manifest.Files.Length).Where(i => File.Exists(ChunkPath(dir,i))).ToArray();
        return new { ok = true, session_id = entry.Manifest.ScanId, received, finalized = File.Exists(System.IO.Path.Combine(dir,"finalized.json")),
            expires_at = entry.CreatedAt + Retention, retention_days = 7, processing = "local", reconstruction_available = false };
    }

    public async Task PutAsync(string owner,string id,int sequence,string sha256,long? contentLength,Stream input,CancellationToken ct)
    {
        Entry entry; CaptureFile file; string path, key;
        lock (_gate)
        {
            Prune(); entry = Require(owner,id);
            if (sequence < 0 || sequence >= entry.Manifest.Files.Length) throw new CaptureTransferException(400,"CAPTURE_SEQUENCE_INVALID");
            file = entry.Manifest.Files[sequence];
            if (sha256 != file.Sha256) throw new CaptureTransferException(400,"CAPTURE_HASH_MISMATCH");
            if (contentLength.HasValue && contentLength != file.Size) throw new CaptureTransferException(400,"CAPTURE_SIZE_MISMATCH");
            string dir = DirectoryFor(owner,id);
            if (File.Exists(System.IO.Path.Combine(dir,"finalized.json"))) throw new CaptureTransferException(409,"CAPTURE_FINALIZED");
            path = ChunkPath(dir,sequence); key = path;
            if (!_pending.Add(key)) throw new CaptureTransferException(409,"CAPTURE_CHUNK_BUSY");
        }
        string temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            var buffer = new byte[65536]; long total = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {
                while (true)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(),ct);
                    if (read == 0) break;
                    total += read;
                    if (total > file.Size) throw new CaptureTransferException(413,"CAPTURE_SIZE_MISMATCH");
                    hash.AppendData(buffer,0,read); await output.WriteAsync(buffer.AsMemory(0,read),ct);
                }
                if (total != file.Size) throw new CaptureTransferException(400,"CAPTURE_SIZE_MISMATCH");
                if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != file.Sha256) throw new CaptureTransferException(400,"CAPTURE_HASH_MISMATCH");
                await output.FlushAsync(ct); output.Flush(true);
            }
            lock (_gate)
            {
                Require(owner,id);
                if (File.Exists(path))
                {
                    // Idempotent replay still consumes and verifies the body; stored raw is never overwritten.
                    if (FileHash(path) != file.Sha256) throw new CaptureTransferException(409,"CAPTURE_INTEGRITY_ERROR");
                }
                else File.Move(temporary,path);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            lock (_gate) _pending.Remove(key);
        }
    }

    public object Finalize(string owner,string id)
    {
        lock (_gate)
        {
            Prune(); var entry = Require(owner,id); string dir = DirectoryFor(owner,id);
            if (_pending.Any(p => p.StartsWith(dir + System.IO.Path.DirectorySeparatorChar,StringComparison.Ordinal)) || _leases.ContainsKey(Key(owner,id))) throw new CaptureTransferException(409,"CAPTURE_CHUNK_BUSY");
            for (int i = 0; i < entry.Manifest.Files.Length; i++)
            {
                string path = ChunkPath(dir,i); var file = entry.Manifest.Files[i];
                if (!File.Exists(path)) throw new CaptureTransferException(409,"CAPTURE_INCOMPLETE");
                if (new FileInfo(path).Length != file.Size || FileHash(path) != file.Sha256) throw new CaptureTransferException(409,"CAPTURE_INTEGRITY_ERROR");
            }
            string final = System.IO.Path.Combine(dir,"finalized.json");
            if (!File.Exists(final)) WriteNew(final,Encoding.UTF8.GetBytes("{\"raw_immutable\":true}"));
            return DescribeLocked(entry);
        }
    }

    public void Delete(string owner,string id)
    {
        lock (_gate)
        {
            Require(owner,id); string dir = DirectoryFor(owner,id);
            if (_pending.Any(p => p.StartsWith(dir + System.IO.Path.DirectorySeparatorChar,StringComparison.Ordinal)) || _leases.ContainsKey(Key(owner,id))) throw new CaptureTransferException(409,"CAPTURE_CHUNK_BUSY");
            Directory.Delete(dir,true); _entries.Remove(Key(owner,id));
        }
    }

    private Entry Require(string owner,string id)
    {
        if (!ValidOwner(owner) || !ValidId(id) || !_entries.TryGetValue(Key(owner,id),out var entry)) throw new CaptureTransferException(404,"CAPTURE_NOT_FOUND");
        return entry;
    }
    private string DirectoryFor(string owner,string id) => System.IO.Path.Combine(_root,owner,id);
    private static string ChunkPath(string dir,int sequence) => System.IO.Path.Combine(dir,sequence.ToString("D8") + ".bin");
    private void Prune()
    {
        foreach (var pair in _entries.Where(p => p.Value.CreatedAt + Retention <= _now()).ToArray())
        {
            string dir = DirectoryFor(pair.Value.Owner,pair.Value.Manifest.ScanId);
            if (_pending.Any(p => p.StartsWith(dir + System.IO.Path.DirectorySeparatorChar,StringComparison.Ordinal)) || _leases.ContainsKey(pair.Key)) continue;
            if (Directory.Exists(dir)) Directory.Delete(dir,true);
            _entries.Remove(pair.Key);
        }
    }
    private static void Validate(CaptureUpload upload)
    {
        if (upload == null || !ValidId(upload.ScanId) || upload.Files == null || upload.Files.Length is < 1 or > 10001) throw new CaptureTransferException(400,"CAPTURE_MANIFEST_INVALID");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in upload.Files)
        {
            if (file == null || file.Path == null || !Regex.IsMatch(file.Path,@"\A(?:capture_manifest\.json|frames/[0-9]{8}/(?:image\.rgb|frame\.json))\z") || !paths.Add(file.Path) ||
                file.Size <= 0 || file.Size > MaxChunkBytes || file.Sha256 == null || !ValidOwner(file.Sha256)) throw new CaptureTransferException(400,"CAPTURE_MANIFEST_INVALID");
        }
        if (!paths.Contains("capture_manifest.json")) throw new CaptureTransferException(400,"CAPTURE_MANIFEST_INVALID");
    }
    private static string FileHash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static void WriteNew(string path,byte[] bytes)
    { using var stream = new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None); stream.Write(bytes); stream.Flush(true); }
}
