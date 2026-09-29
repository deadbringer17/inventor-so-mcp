using System;

namespace InventorXrSo.Xr.Voice
{
    /// <summary>
    /// Downmix e ricampionamento in streaming verso PCM16 mono 16 kHz. Puro C# (nessuna API Unity): testabile
    /// in EditMode. Interpolazione lineare; sotto-campionando (>= 1,5x) applica prima un filtro [1 2 1]/4
    /// per ridurre l'aliasing. Lo stato tra i blocchi evita discontinuita.
    /// </summary>
    public sealed class AudioResampler
    {
        private readonly double _step;      // campioni sorgente per campione in uscita
        private readonly bool _smooth;
        private double _next;               // posizione sorgente del prossimo campione in uscita
        private long _index;                // indice del prossimo campione sorgente
        private float _prev, _s1, _s2;
        private bool _hasPrev;

        public int SourceRate { get; }
        public int TargetRate { get; }

        public AudioResampler(int sourceRate, int targetRate = 16000)
        {
            if (sourceRate <= 0) throw new ArgumentOutOfRangeException(nameof(sourceRate));
            if (targetRate <= 0) throw new ArgumentOutOfRangeException(nameof(targetRate));
            SourceRate = sourceRate; TargetRate = targetRate;
            _step = (double)sourceRate / targetRate;
            _smooth = _step >= 1.5;
        }

        /// <summary>Capacita di uscita sufficiente per <paramref name="inputCount"/> campioni in ingresso.</summary>
        public int MaxOutput(int inputCount) => (int)(inputCount / _step) + 3;

        /// <summary>Converte campioni float mono [-1,1]; scrive in output (ridimensionato se serve). Ritorna i campioni scritti.</summary>
        public int Process(float[] mono, int count, ref short[] output)
        {
            if (mono == null || count <= 0) return 0;
            if (count > mono.Length) count = mono.Length;
            int need = MaxOutput(count);
            if (output == null || output.Length < need) output = new short[need];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                float x = mono[i];
                if (_smooth) { float y = (x + 2f * _s1 + _s2) * 0.25f; _s2 = _s1; _s1 = x; x = y; }
                if (!_hasPrev) { _prev = x; _hasPrev = true; }
                while (_next <= _index)
                {
                    double frac = _next - (_index - 1);
                    if (frac < 0) frac = 0; else if (frac > 1) frac = 1;
                    float v = (float)(_prev + (x - _prev) * frac);
                    if (written >= output.Length) break;
                    output[written++] = ToPcm16(v);
                    _next += _step;
                }
                _prev = x;
                _index++;
            }
            return written;
        }

        public void Reset()
        {
            _next = 0; _index = 0; _prev = _s1 = _s2 = 0; _hasPrev = false;
        }

        public static short ToPcm16(float v)
        {
            if (float.IsNaN(v)) return 0;
            if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
            return (short)Math.Round(v * 32767f);
        }

        /// <summary>Media dei canali di dati interleaved in mono. Ritorna i frame scritti.</summary>
        public static int Downmix(float[] interleaved, int frames, int channels, float[] mono)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            if (frames > mono.Length) frames = mono.Length;
            if (channels == 1) { Array.Copy(interleaved, mono, frames); return frames; }
            float inv = 1f / channels;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                int b = f * channels;
                for (int c = 0; c < channels; c++) sum += interleaved[b + c];
                mono[f] = sum * inv;
            }
            return frames;
        }
    }
}
