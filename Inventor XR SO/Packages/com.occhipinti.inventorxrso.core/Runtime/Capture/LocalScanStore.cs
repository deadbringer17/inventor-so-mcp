using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Capture
{
    public sealed class CaptureStorageFullException : IOException
    {
        public CaptureStorageFullException() : base("Capture storage quota or free-space reserve reached.") { }
    }

    public sealed class StoredCaptureFrame
    {
        public int Sequence { get; }
        public CaptureFrame Frame { get; }
        public string Sha256 { get; }
        public long Size { get; }
        public string Encoding => "rgb8";
        [JsonConstructor]
        public StoredCaptureFrame(int sequence, CaptureFrame frame, string sha256, long size)
        {
            if (sequence < 0 || frame == null || sha256 == null || sha256.Length != 64 ||
                sha256.Any(c => !Uri.IsHexDigit(c)) || size != checked(frame.Intrinsics.Width * frame.Intrinsics.Height * 3L))
                throw new ArgumentException("Invalid stored frame.");
            Sequence = sequence; Frame = frame; Sha256 = sha256; Size = size;
        }
    }

    public sealed class ScanRecordingStatus
    {
        public CaptureState State { get; }
        public string Reason { get; }
        public DateTimeOffset UpdatedAt { get; }
        [JsonConstructor]
        public ScanRecordingStatus(CaptureState state, string reason, DateTimeOffset updatedAt)
        {
            if (!Enum.IsDefined(typeof(CaptureState), state) || reason == null || reason.Length > 256) throw new ArgumentException("Invalid capture status.");
            State = state; Reason = reason; UpdatedAt = updatedAt;
        }
    }

    /// <summary>
    /// Single writer. Immutable session metadata and frame directories, published by atomic rename.
    /// State is a separate append-only journal. A power loss can leave a pending directory, never a published half-frame.
    /// No network upload or implicit deletion of raw data.
    /// </summary>
    public sealed class LocalScanStore
    {
        private readonly string _root;
        private readonly long _quotaBytes, _reserveBytes;
        private readonly Func<long> _freeBytes;
        private readonly object _gate = new object();
        private long _usedBytes;
        private readonly Dictionary<string, List<StoredCaptureFrame>> _frameIndex = new Dictionary<string, List<StoredCaptureFrame>>();
        private const int MaxFrames = 5000;
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore, MaxDepth = 32 };

        public LocalScanStore(string root, long quotaBytes = 2147483648L, long reserveBytes = 268435456L, Func<long> freeBytes = null)
        {
            if (string.IsNullOrWhiteSpace(root) || quotaBytes < 1024 || reserveBytes < 0) throw new ArgumentException("Invalid storage configuration.");
            _root = Path.GetFullPath(root); _quotaBytes = quotaBytes; _reserveBytes = reserveBytes;
            Directory.CreateDirectory(_root);
            _freeBytes = freeBytes ?? (() => new DriveInfo(Path.GetPathRoot(_root)).AvailableFreeSpace);
            _usedBytes = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        }

        public void Create(ScanSession session)
        {
            lock (_gate)
            {
                string path = SessionPath(session.SessionId);
                if (Directory.Exists(path)) throw new IOException("Session already exists.");
                string pending = path + ".pending-" + Guid.NewGuid().ToString("N");
                try
                {
                    EnsureSpace(65536);
                    Directory.CreateDirectory(pending);
                    WriteNew(Path.Combine(pending, "capture_manifest.json"), Serialize(session));
                    Directory.CreateDirectory(Path.Combine(pending, "frames"));
                    Directory.CreateDirectory(Path.Combine(pending, "state"));
                    Directory.Move(pending, path);
                    SetStatus(session.SessionId, CaptureState.Ready, "created");
                }
                finally { if (Directory.Exists(pending)) Directory.Delete(pending, true); }
            }
        }

        public ScanSession Open(string id)
        {
            string path = Path.Combine(SessionPath(id), "capture_manifest.json");
            var header = Read<JObject>(path);
            if (header.Value<int?>("SchemaVersion") != 1) throw new InvalidDataException("Unsupported capture schema.");
            var session = header.ToObject<ScanSession>();
            if (session.SessionId != id || session.SchemaVersion != 1) throw new InvalidDataException("Session identity or version mismatch.");
            return session;
        }

        public IReadOnlyList<ScanSession> List()
        {
            var sessions = new List<ScanSession>();
            foreach (string dir in Directory.EnumerateDirectories(_root))
            {
                string id = Path.GetFileName(dir);
                if (Guid.TryParseExact(id, "N", out _)) sessions.Add(Open(id));
            }
            return sessions.OrderByDescending(s => s.CreatedAt).ToArray();
        }

        /// <summary>Call once when acquiring exclusive ownership of the capture root after application startup.</summary>
        public void RecoverInterruptedSessions()
        {
            lock (_gate)
            {
                foreach (var session in List())
                {
                    var state = GetStatus(session.SessionId).State;
                    if (state == CaptureState.Ready || state == CaptureState.Recording || state == CaptureState.Paused)
                        SetStatus(session.SessionId,CaptureState.Interrupted,"application_restarted");
                }
            }
        }

        public ScanRecordingStatus GetStatus(string id)
        {
            string path = Path.Combine(SessionPath(id), "state");
            string last = Directory.EnumerateFiles(path, "*.json").OrderBy(p => p, StringComparer.Ordinal).LastOrDefault();
            return last == null ? new ScanRecordingStatus(CaptureState.Interrupted, "missing_status", DateTimeOffset.UtcNow) : Read<ScanRecordingStatus>(last);
        }

        public void SetStatus(string id, CaptureState state, string reason)
        {
            lock (_gate)
            {
                Open(id);
                string dir = Path.Combine(SessionPath(id), "state");
                var files = Directory.EnumerateFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();
                int seq = files.Length == 0 ? 0 : int.Parse(Path.GetFileNameWithoutExtension(files[files.Length - 1]), System.Globalization.CultureInfo.InvariantCulture) + 1;
                string destination = Path.Combine(dir, seq.ToString("D8") + ".json");
                string pending = destination + ".pending";
                byte[] bytes = Serialize(new ScanRecordingStatus(state, reason, DateTimeOffset.UtcNow));
                // State/stop must still be publishable after the frame quota is exhausted.
                try { WriteNew(pending, bytes); File.Move(pending, destination); }
                finally { if (File.Exists(pending)) File.Delete(pending); }
            }
        }

        public StoredCaptureFrame Append(string id, CaptureFrame frame, byte[] rgb)
        {
            lock (_gate)
            {
                Open(id);
                if (GetStatus(id).State != CaptureState.Recording) throw new InvalidOperationException("Session is not recording.");
                if (!frame.TrackingValid || rgb == null || rgb.LongLength != checked(frame.Intrinsics.Width * frame.Intrinsics.Height * 3L)) throw new ArgumentException("Invalid frame data.");
                if (!_frameIndex.TryGetValue(id, out var frames))
                {
                    frames = ReadFrames(id).ToList(); _frameIndex.Add(id, frames);
                }
                if (frames.Count >= MaxFrames) throw new CaptureStorageFullException();
                if (frames.Count > 0 && frame.TimestampUnixMicroseconds <= frames[frames.Count - 1].Frame.TimestampUnixMicroseconds)
                    throw new ArgumentException("Frame timestamps must increase.");
                var stored = new StoredCaptureFrame(frames.Count, frame, Hash(rgb), rgb.LongLength);
                byte[] metadata = Serialize(stored);
                EnsureSpace(rgb.LongLength + metadata.Length + 65536);
                string directory = Path.Combine(SessionPath(id), "frames", stored.Sequence.ToString("D8"));
                string pending = directory + ".pending-" + Guid.NewGuid().ToString("N");
                try
                {
                    Directory.CreateDirectory(pending);
                    WriteNew(Path.Combine(pending, "image.rgb"), rgb);
                    WriteNew(Path.Combine(pending, "frame.json"), metadata);
                    Directory.Move(pending, directory);
                    frames.Add(stored);
                    return stored;
                }
                finally { if (Directory.Exists(pending)) Directory.Delete(pending, true); }
            }
        }

        public IReadOnlyList<StoredCaptureFrame> ReadFrames(string id)
        {
            var frames = new List<StoredCaptureFrame>();
            foreach (string directory in Directory.EnumerateDirectories(Path.Combine(SessionPath(id), "frames")).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!int.TryParse(Path.GetFileName(directory), out int sequence)) continue; // unpublished frames remain private
                var frame = Read<StoredCaptureFrame>(Path.Combine(directory, "frame.json"));
                if (sequence != frames.Count || frame.Sequence != sequence) throw new InvalidDataException("Frame sequence is incomplete.");
                frames.Add(frame);
            }
            return frames;
        }

        public byte[] ReadImage(string id, int sequence)
        {
            var frames = ReadFrames(id);
            if (sequence < 0 || sequence >= frames.Count) throw new ArgumentOutOfRangeException(nameof(sequence));
            byte[] image = File.ReadAllBytes(Path.Combine(SessionPath(id), "frames", sequence.ToString("D8"), "image.rgb"));
            if (image.LongLength != frames[sequence].Size || Hash(image) != frames[sequence].Sha256) throw new InvalidDataException("Raw frame integrity check failed.");
            return image;
        }

        public int Verify(string id)
        {
            Open(id);
            var frames = ReadFrames(id);
            foreach (var frame in frames)
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(Path.Combine(SessionPath(id), "frames", frame.Sequence.ToString("D8"), "image.rgb")))
                {
                    if (stream.Length != frame.Size || BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != frame.Sha256)
                        throw new InvalidDataException("Raw frame integrity check failed.");
                }
            }
            return frames.Count;
        }

        public IReadOnlyList<CaptureTransferFile> DescribeTransfer(string id)
        {
            var status = GetStatus(id);
            if (status.State == CaptureState.Recording || status.State == CaptureState.Paused || status.State == CaptureState.Ready)
                throw new InvalidOperationException("Finish capture before uploading.");
            Open(id);
            var files = new List<CaptureTransferFile>();
            Verify(id);
            void Add(string relative)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(SessionPath(id),relative.Replace('/',Path.DirectorySeparatorChar)));
                files.Add(new CaptureTransferFile(relative,bytes.LongLength,Hash(bytes)));
            }
            Add("capture_manifest.json");
            foreach (var frame in ReadFrames(id))
            {
                string prefix = "frames/" + frame.Sequence.ToString("D8") + "/";
                Add(prefix + "frame.json"); Add(prefix + "image.rgb");
            }
            return files;
        }

        public byte[] ReadTransferFile(string id,CaptureTransferFile file)
        {
            if (file.Path != "capture_manifest.json" && !System.Text.RegularExpressions.Regex.IsMatch(file.Path,@"\Aframes/[0-9]{8}/(?:image\.rgb|frame\.json)\z"))
                throw new ArgumentException("Invalid capture file path.");
            byte[] bytes = File.ReadAllBytes(Path.Combine(SessionPath(id),file.Path.Replace('/',Path.DirectorySeparatorChar)));
            if (bytes.LongLength != file.Size || Hash(bytes) != file.Sha256) throw new InvalidDataException("Capture file changed during upload.");
            return bytes;
        }

        private string SessionPath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid session id.");
            return Path.Combine(_root, id);
        }
        private void EnsureSpace(long bytes)
        {
            if (bytes > _quotaBytes - _usedBytes || bytes > _freeBytes() - _reserveBytes) throw new CaptureStorageFullException();
        }
        private void WriteNew(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            _usedBytes += bytes.Length;
        }
        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private static byte[] Serialize(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented, JsonSettings));
        private static T Read<T>(string path)
        {
            if (new FileInfo(path).Length > 1024*1024) throw new InvalidDataException("Capture metadata is too large.");
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path), JsonSettings) ?? throw new InvalidDataException("Empty capture metadata.");
        }
    }

    /// <summary>Explicit resume only within the same tracking epoch. Restarted applications may review raw, never append.</summary>
    public sealed class ScanRecorder
    {
        private readonly LocalScanStore _store;
        private readonly CaptureFrameSelector _selector = new CaptureFrameSelector();
        public ScanSession Session { get; }
        public CaptureState State { get; private set; } = CaptureState.Ready;
        public int Accepted { get; private set; }
        public int Rejected { get; private set; }
        public string LastReason { get; private set; } = "ready";
        public ScanRecorder(LocalScanStore store, ScanSession session)
        { _store = store; Session = session; _store.Create(session); }
        public void Start()
        {
            if (State != CaptureState.Ready && State != CaptureState.Paused) throw new InvalidOperationException("Cannot resume this capture.");
            Set(CaptureState.Recording, "recording");
        }
        public void Pause() { if (State == CaptureState.Recording) Set(CaptureState.Paused, "operator_pause"); }
        public void Stop(string reason = "operator_stop")
        { if (State != CaptureState.Completed && State != CaptureState.Interrupted) Set(CaptureState.Completed, reason); }
        public void Interrupt(string reason)
        { if (State != CaptureState.Completed && State != CaptureState.Interrupted) Set(CaptureState.Interrupted, reason); }
        public bool Record(CaptureFrame frame, byte[] rgb)
        {
            if (State != CaptureState.Recording) return false;
            if (!frame.TrackingValid) { Interrupt("tracking_lost"); return false; }
            string rejection = _selector.Evaluate(frame);
            if (rejection != null) { LastReason = rejection; Rejected++; return false; }
            try { _store.Append(Session.SessionId, frame, rgb); }
            catch (CaptureStorageFullException) { Interrupt("storage_full"); return false; }
            catch (IOException) { Interrupt("storage_error"); throw; }
            _selector.Commit(frame); Accepted++; LastReason = "accepted";
            return true;
        }
        private void Set(CaptureState state, string reason)
        {
            // Stop acquisition even if the disk cannot publish the final journal entry.
            State = state; LastReason = reason;
            _store.SetStatus(Session.SessionId, state, reason);
        }
    }
}
