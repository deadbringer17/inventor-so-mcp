using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using InventorXrSo.Core.Capture;
using UnityEngine;

namespace InventorXrSo.Xr.Capture
{
    /// <summary>Owns the camera and a single outstanding disk write. No CAD or pairing dependency.</summary>
    public sealed class RealityCaptureController : MonoBehaviour
    {
        private QuestCameraSource _source;
        private LocalScanStore _store;
        private ScanRecorder _recorder;
        private Task _write = Task.CompletedTask;
        private string _root;
        private bool _transition;
        private Matrix4x4 _trackingToCapture;
        private int _originEpoch;
        public bool Busy => _transition || !_write.IsCompleted;
        public bool CanResume => _recorder != null && (_recorder.State == CaptureState.Ready || _recorder.State == CaptureState.Paused);
        public ScanSession Current => _recorder?.Session;
        public CaptureState State => _recorder?.State ?? CaptureState.Ready;
        public int Accepted => _recorder?.Accepted ?? 0;
        public int Rejected => _recorder?.Rejected ?? 0;
        public string Feedback { get; private set; } = "ready";
        public event Action Changed;

        private void Awake()
        {
            _root = Path.Combine(Application.persistentDataPath, "RealityCapture", "v1");
            _source = gameObject.AddComponent<QuestCameraSource>();
        }

        public async Task CreateAsync(CaptureMode mode, CaptureVolume volume, Matrix4x4 captureToTracking)
        {
            if (_transition) return;
            _transition = true;
            try
            {
                _source.Stop(); await _write;
                var previous = _recorder;
                string device = SystemInfo.deviceModel, version = Application.version;
                var session = new ScanSession(Guid.NewGuid().ToString("N"), mode, device, version, DateTimeOffset.UtcNow,
                    volume, QuestCameraSource.ToContract(captureToTracking));
                _recorder = await Task.Run(() =>
                {
                    previous?.Stop("new_session");
                    if (_store == null) _store = CreateStore();
                    return new ScanRecorder(_store, session);
                });
                _trackingToCapture = captureToTracking.inverse;
                _originEpoch = OVRPlugin.GetLocalTrackingSpaceRecenterCount();
                Feedback = "ready";
            }
            catch (Exception ex) { Feedback = StorageReason(ex); }
            finally { _transition = false; Changed?.Invoke(); }
        }

        public async Task StartAsync()
        {
            if (_transition || !CanResume) return;
            if (!QuestCameraSource.TrackingValid()) { Feedback = "tracking_lost"; Changed?.Invoke(); return; }
            if (_originEpoch != OVRPlugin.GetLocalTrackingSpaceRecenterCount()) { await EndAsync("tracking_origin_changed", true); return; }
            _transition = true;
            try
            {
                await _write;
                await Task.Run(() => _recorder.Start());
                Feedback = "camera_starting";
                _source.Begin(_trackingToCapture, OnFrame, reason => _ = EndAsync(reason, true));
            }
            catch (Exception ex) { await InterruptAfterFailure(ex); }
            finally { _transition = false; Changed?.Invoke(); }
        }

        private void OnFrame(CaptureFrame frame, byte[] rgb)
        {
            if (_transition || !_write.IsCompleted || State != CaptureState.Recording) return;
            _write = SaveFrameAsync(frame, rgb);
        }

        private async Task SaveFrameAsync(CaptureFrame frame, byte[] rgb)
        {
            try { await Task.Run(() => _recorder.Record(frame, rgb)); Feedback = _recorder.LastReason; }
            catch (Exception ex) { await InterruptAfterFailure(ex); }
            if (_recorder.State == CaptureState.Interrupted) _source.Stop();
            Changed?.Invoke();
        }

        public async Task PauseAsync()
        {
            if (_transition || State != CaptureState.Recording) return;
            _transition = true; _source.Stop();
            try { await _write; await Task.Run(() => _recorder.Pause()); Feedback = _recorder.LastReason; }
            catch (Exception ex) { Feedback = StorageReason(ex); }
            finally { _transition = false; Changed?.Invoke(); }
        }

        public async Task EndAsync(string reason = "operator_stop", bool interrupted = false)
        {
            // Called immediately by Home/menu/lifecycle. Native camera stops even while storage drains.
            _source?.Stop();
            if (_transition)
            {
                // In particular, a permission/pause callback must not be lost during StartAsync.
                while (_transition) await Task.Yield();
            }
            if (_recorder == null) return;
            _transition = true;
            try
            {
                await _write;
                await Task.Run(() => { if (interrupted) _recorder.Interrupt(reason); else _recorder.Stop(reason); });
                Feedback = _recorder.LastReason;
            }
            catch (Exception ex) { Feedback = StorageReason(ex); }
            finally { _transition = false; Changed?.Invoke(); }
        }

        public async Task<IReadOnlyList<ScanSession>> ListAsync()
        {
            await _write;
            return await Task.Run(() => { if (_store == null) _store = CreateStore(); return _store.List(); });
        }

        public async Task<(int count, string status)> ReviewAsync(string id)
        {
            await _write;
            return await Task.Run(() =>
            {
                if (_store == null) _store = CreateStore();
                _store.Open(id);
                int count = _store.Verify(id);
                var status = _store.GetStatus(id);
                // Recording/paused on disk after process death belongs to an old tracking epoch.
                string state = status.State == CaptureState.Recording || status.State == CaptureState.Paused || status.State == CaptureState.Ready
                    ? "Interrotta: sessione di un avvio precedente" : status.State == CaptureState.Completed ? "Terminata" : "Interrotta: " + ReasonText(status.Reason);
                return (count, state);
            });
        }

        public async Task UploadAsync(CaptureSessionClient client, string id, CancellationToken ct)
        {
            if (client == null) throw new InvalidOperationException("Pair a PC before transfer.");
            if (_transition) return;
            await _write;
            if (_store == null) _store = await Task.Run(CreateStore,ct);
            await client.UploadAsync(_store,id,(count,total) => { Feedback = $"Trasferimento: {count}/{total} file"; Changed?.Invoke(); },ct);
        }

        private void Update()
        {
            if (_recorder == null || _transition || State != CaptureState.Recording && State != CaptureState.Paused) return;
            if (_originEpoch != OVRPlugin.GetLocalTrackingSpaceRecenterCount()) _ = EndAsync("tracking_origin_changed", true);
            else if (!QuestCameraSource.TrackingValid()) _ = EndAsync("tracking_lost", true);
        }
        private void OnApplicationPause(bool paused) { if (paused) _ = EndAsync("application_suspended", true); }
        private void OnDisable() { _source?.Stop(); if (_recorder != null) _ = EndAsync("workspace_closed", true); }

        private async Task InterruptAfterFailure(Exception failure)
        {
            _source.Stop();
            string reason = StorageReason(failure);
            try { await Task.Run(() => _recorder?.Interrupt(reason)); }
            catch (Exception) { /* A full/unavailable disk may also refuse the stop journal; startup recovery handles this. */ }
            Feedback = reason;
        }

        private LocalScanStore CreateStore()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // DriveInfo can report the read-only system partition on Android; query the actual app-data filesystem.
            // Store operations run on Task.Run workers, which must attach to JNI for StatFs.
            var store = new LocalScanStore(_root,freeBytes: () =>
            {
                AndroidJNI.AttachCurrentThread();
                try { using (var stat = new AndroidJavaObject("android.os.StatFs",_root)) return stat.Call<long>("getAvailableBytes"); }
                finally { AndroidJNI.DetachCurrentThread(); }
            });
#else
            var store = new LocalScanStore(_root);
#endif
            store.RecoverInterruptedSessions();
            return store;
        }
        private static string StorageReason(Exception ex) => ex is CaptureStorageFullException ? "storage_full" : "storage_error";
        public static string ReasonText(string reason)
        {
            switch (reason)
            {
                case "ready": return "Pronta. Nessuna immagine acquisita prima di Avvia.";
                case "recording": case "accepted": return "Frame salvato. Muoviti lentamente e cambia punto di vista.";
                case "camera_starting": return "Avvio camera e verifica permessi…";
                case "operator_pause": return "In pausa. Premi Riprendi per continuare.";
                case "operator_stop": case "new_session": return "Acquisizione terminata; raw conservati localmente.";
                case "tracking_lost": return "Tracking perso: acquisizione fermata. Inizia una nuova sessione.";
                case "tracking_origin_changed": return "Origine tracking cambiata: inizia una nuova sessione.";
                case "application_suspended": case "application_focus_lost": return "App sospesa: acquisizione interrotta.";
                case "application_restarted": return "Acquisizione recuperata dopo riavvio dell’app; raw conservati.";
                case "workspace_closed": return "Sezione chiusa: acquisizione fermata.";
                case "camera_permission_denied": return "Permesso camera negato. Abilitalo nelle impostazioni del visore.";
                case "camera_requires_quest": return "Acquisizione RGB disponibile sul Quest; nessuna camera simulata nell’Editor.";
                case "camera_not_supported": return "Fotocamera non supportata: serve Quest 3/3S con accesso alle camere passthrough.";
                case "camera_start_failed": return "La camera non si avvia. Verifica permessi e disponibilità sul visore.";
                case "camera_metadata_invalid": case "camera_pose_unavailable": return "Posa o intrinseci della camera non disponibili: acquisizione fermata.";
                case "camera_readback_failed": case "camera_image_size_invalid": return "Immagine camera non leggibile: acquisizione fermata.";
                case "camera_frame_timeout": return "La camera non invia nuovi frame: acquisizione fermata.";
                case "storage_full": return "Spazio o quota esauriti: acquisizione fermata; raw già salvati conservati.";
                case "storage_error": return "Errore di lettura/scrittura locale. Controlla lo spazio e riprova.";
                case "exposure": return "Esposizione insufficiente o zone sature: cambia illuminazione.";
                case "low_sharpness": return "Immagine poco nitida: rallenta e verifica la superficie.";
                case "low_texture": return "Pochi dettagli visivi: usa riferimenti o cambia vista.";
                case "redundant": return "Vista simile alla precedente: spostati lentamente.";
                case "motion": return "Movimento troppo rapido: rallenta.";
                case "rate_limit": return "Attendi il prossimo frame.";
                case "timestamp_not_increasing": return "Frame fuori sequenza scartato. Attendi un nuovo frame della camera.";
                default: return "Acquisizione interrotta. Verifica camera, tracking e spazio disponibile.";
            }
        }
    }
}
