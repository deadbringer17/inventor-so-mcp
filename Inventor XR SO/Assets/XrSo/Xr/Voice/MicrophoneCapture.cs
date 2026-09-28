using System;
using UnityEngine;

namespace InventorXrSo.Xr.Voice
{
    /// <summary>
    /// Cattura dal microfono solo tra Open e Close (il controller vocale li chiama con StartCapture/StopCapture).
    /// Da Update legge i nuovi campioni dal clip circolare a blocchi fissi (~10 ms), li porta a mono 16 kHz PCM16 e
    /// li passa al sink. Buffer azzerati alla chiusura. Nessun audio e scritto su disco o nei log.
    /// </summary>
    public sealed class MicrophoneCapture : MonoBehaviour
    {
        public const int TargetRate = 16000;
        private const int ClipSeconds = 10;

        private Action<short[], int> _sink;
        private AudioClip _clip;
        private string _device;
        private int _lastPos, _channels, _chunkFrames;
        private float[] _chunk, _mono;
        private short[] _pcm;
        private AudioResampler _resampler;

        /// <summary>Segnala che il microfono non si e aperto (nessun dispositivo, errore di avvio).</summary>
        public event Action OpenFailed;

        public bool IsOpen => _clip != null;
        public Action<short[], int> Sink { get => _sink; set => _sink = value; }

        /// <summary>Apre il microfono. False se non disponibile (OpenFailed viene sollevato).</summary>
        public bool Open()
        {
            if (_clip != null) return true;
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0) { OpenFailed?.Invoke(); return false; }
            _device = devices[0];
            Microphone.GetDeviceCaps(_device, out int min, out int max);
            int freq = TargetRate;
            if (min != 0 || max != 0) freq = Mathf.Clamp(TargetRate, min, max);   // (0,0) = qualunque frequenza
            _clip = Microphone.Start(_device, true, ClipSeconds, freq);
            if (_clip == null) { OpenFailed?.Invoke(); return false; }
            _channels = Mathf.Max(1, _clip.channels);
            int rate = _clip.frequency > 0 ? _clip.frequency : freq;
            _resampler = new AudioResampler(rate, TargetRate);
            _chunkFrames = Mathf.Max(1, rate / 100);
            _chunk = new float[_chunkFrames * _channels];
            _mono = new float[_chunkFrames];
            _pcm = new short[_resampler.MaxOutput(_chunkFrames)];
            _lastPos = 0;
            return true;
        }

        public void Close()
        {
            if (_clip == null && _chunk == null) return;
            if (_device != null && Microphone.IsRecording(_device)) Microphone.End(_device);
            if (_clip != null) Destroy(_clip);
            _clip = null;
            if (_chunk != null) Array.Clear(_chunk, 0, _chunk.Length);
            if (_mono != null) Array.Clear(_mono, 0, _mono.Length);
            if (_pcm != null) Array.Clear(_pcm, 0, _pcm.Length);
            _chunk = null; _mono = null; _pcm = null; _resampler = null;
        }

        private void Update() { if (_clip != null) Pump(); }

        /// <summary>Legge i blocchi completi disponibili dal clip circolare.</summary>
        public void Pump()
        {
            if (_clip == null || _sink == null) return;
            int total = _clip.samples;
            if (total <= 0) return;
            int pos = Microphone.GetPosition(_device);
            if (pos < 0) return;
            int available = pos - _lastPos;
            if (available < 0) available += total;                 // giro completo del clip
            if (available > total - _chunkFrames) { _lastPos = pos; return; }   // in ritardo oltre il clip: i dati sono persi, riparte da qui
            while (available >= _chunkFrames)
            {
                _clip.GetData(_chunk, _lastPos);
                AudioResampler.Downmix(_chunk, _chunkFrames, _channels, _mono);
                int n = _resampler.Process(_mono, _chunkFrames, ref _pcm);
                if (n > 0) _sink(_pcm, n);
                _lastPos = (_lastPos + _chunkFrames) % total;
                available -= _chunkFrames;
            }
        }

        private void OnDisable() { Close(); }
        private void OnDestroy() { Close(); }
    }
}
