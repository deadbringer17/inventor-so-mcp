using System;
using System.Threading;
using System.Threading.Tasks;

namespace InventorXrSo.Core.Voice
{
    /// <summary>Motore di riconoscimento sostituibile. Riceve PCM16 mono 16 kHz; non deve conservare ne loggare audio o testo.</summary>
    public interface ISpeechRecognizer
    {
        Task<string> RecognizeAsync(short[] pcm16, CancellationToken cancellationToken);
    }

    public enum PushToTalkState { Idle, Pressed, Listening, Processing, Result, Error }

    public enum VoiceInterruption { TrackingLost, ModeChanged, Disconnected, PermissionDenied }

    /// <summary>
    /// Push-to-talk: il microfono e aperto solo in Listening. Rilascio, perdita tracking, cambio modalita,
    /// disconnessione, permesso negato e Dispose chiudono la cattura subito (StopCapture una sola volta),
    /// annullano il riconoscimento pendente e scartano i risultati tardivi per generazione.
    /// Il tempo e iniettato; il layer Unity chiama Tick() ad ogni frame e AppendAudio() dal microfono.
    /// Nessun audio o testo viene scritto nei log; i buffer sono azzerati a fine richiesta.
    /// </summary>
    public sealed class PushToTalkController : IDisposable
    {
        public const int SampleRate = 16000;
        public const int MaxCaptureSamples = SampleRate * 10; // come il limite di /voice/transcribe
        public const int MinCaptureSamples = SampleRate / 5;

        private readonly object _gate = new object();
        private readonly ISpeechRecognizer _recognizer;
        private readonly VoiceCommandRouter _router;
        private readonly ICommandAvailability _availability;
        private readonly DictationTarget _dictation;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Func<bool> _canCapture;
        private CancellationTokenSource _cts;
        private short[] _buffer = new short[SampleRate];
        private int _count, _generation;
        private bool _captureOpen, _disposed;
        private DateTimeOffset _stateAt;
        private PushToTalkState _state;
        private string _transcript, _error;
        private VoiceRouteResult _proposed;
        private DictationProposal _dictationProposal;

        public TimeSpan ArmingThreshold { get; }
        public TimeSpan ResultDisplayTime { get; }

        /// <summary>Il layer Unity apre il microfono.</summary>
        public event Action StartCapture;
        /// <summary>Il layer Unity chiude il microfono. Una sola volta per cattura.</summary>
        public event Action StopCapture;
        public event Action Changed;
        /// <summary>Pressione rifiutata perche la voce non e accettata (es. Home): il microfono non e stato aperto.</summary>
        public event Action CaptureRefused;

        public PushToTalkController(ISpeechRecognizer recognizer, VoiceCommandRouter router, ICommandAvailability availability,
            DictationTarget dictation = null, Func<DateTimeOffset> clock = null, TimeSpan? armingThreshold = null, TimeSpan? resultDisplayTime = null,
            Func<bool> canCapture = null)
        {
            _canCapture = canCapture;
            _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _availability = availability ?? throw new ArgumentNullException(nameof(availability));
            _dictation = dictation;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            ArmingThreshold = armingThreshold ?? TimeSpan.FromMilliseconds(150);
            ResultDisplayTime = resultDisplayTime ?? TimeSpan.FromSeconds(4);
        }

        public PushToTalkState State { get { lock (_gate) return _state; } }
        public bool IsListening => State == PushToTalkState.Listening;
        public bool IsProcessing => State == PushToTalkState.Processing;
        /// <summary>Vero solo mentre e in Listening.</summary>
        public bool MicrophoneOpen { get { lock (_gate) return _captureOpen; } }
        public string Transcript { get { lock (_gate) return _transcript; } }
        public string ErrorMessage { get { lock (_gate) return _error; } }
        /// <summary>Comando proposto (modalita comandi).</summary>
        public VoiceRouteResult ProposedCommand { get { lock (_gate) return _proposed; } }
        /// <summary>Valore proposto (modalita dettatura, campo armato).</summary>
        public DictationProposal ProposedDictation { get { lock (_gate) return _dictationProposal; } }
        public int Generation { get { lock (_gate) return _generation; } }

        /// <summary>Falso quando la voce non e accettata (Home, nessuna sessione): non si deve aprire ne chiedere il microfono.</summary>
        public bool CanCapture => _canCapture == null || _canCapture();

        /// <summary>Segnala all'utente che la pressione e stata rifiutata; nessuno stato cambia.</summary>
        public void RefuseCapture() { CaptureRefused?.Invoke(); }

        public void Press()
        {
            if (!CanCapture) { RefuseCapture(); return; }
            Action post = null;
            lock (_gate)
            {
                if (_disposed || _state == PushToTalkState.Pressed || _state == PushToTalkState.Listening) return;
                Discard(ref post); // richiesta precedente ancora in corso: scartata
                ClearOutput();
                SetState(PushToTalkState.Pressed);
                post += Changed;
            }
            post?.Invoke();
        }

        /// <summary>Da chiamare a ogni frame: supera la soglia di arming e fa scadere Result/Error.</summary>
        public void Tick()
        {
            Action post = null;
            lock (_gate)
            {
                if (_disposed) return;
                var now = _clock();
                if (_state == PushToTalkState.Pressed && now - _stateAt >= ArmingThreshold)
                {
                    _count = 0; _captureOpen = true;
                    SetState(PushToTalkState.Listening);
                    post += StartCapture; post += Changed;
                }
                else if ((_state == PushToTalkState.Result || _state == PushToTalkState.Error) && now - _stateAt >= ResultDisplayTime)
                {
                    ClearOutput();
                    SetState(PushToTalkState.Idle);
                    post += Changed;
                }
            }
            post?.Invoke();
        }

        public void Release()
        {
            Action post = null;
            lock (_gate)
            {
                if (_disposed) return;
                if (_state == PushToTalkState.Pressed) { SetState(PushToTalkState.Idle); post += Changed; }
                else if (_state == PushToTalkState.Listening) BeginProcessing(ref post);
            }
            post?.Invoke();
        }

        /// <summary>Campioni PCM16 dal microfono; ignorati se non in Listening.</summary>
        public void AppendAudio(short[] samples, int count)
        {
            Action post = null;
            lock (_gate)
            {
                if (_disposed || _state != PushToTalkState.Listening || samples == null || count <= 0) return;
                if (count > samples.Length) count = samples.Length;
                int room = MaxCaptureSamples - _count;
                int take = Math.Min(count, room);
                if (_count + take > _buffer.Length)
                {
                    var bigger = new short[Math.Min(MaxCaptureSamples, Math.Max(_buffer.Length * 2, _count + take))];
                    Array.Copy(_buffer, bigger, _count);
                    Array.Clear(_buffer, 0, _buffer.Length);
                    _buffer = bigger;
                }
                Array.Copy(samples, 0, _buffer, _count, take);
                _count += take;
                if (_count >= MaxCaptureSamples) BeginProcessing(ref post); // limite di durata: come un rilascio
            }
            post?.Invoke();
        }

        /// <summary>Tracking perso, cambio modalita, disconnessione o permesso negato: chiude subito.</summary>
        public void Interrupt(VoiceInterruption reason)
        {
            Action post = null;
            lock (_gate)
            {
                if (_disposed) return;
                Discard(ref post);
                ClearOutput();
                if (reason == VoiceInterruption.PermissionDenied)
                {
                    _error = "Permesso microfono negato. I comandi manuali restano disponibili.";
                    SetState(PushToTalkState.Error);
                }
                else SetState(PushToTalkState.Idle);
                post += Changed;
            }
            post?.Invoke();
        }

        /// <summary>Chiude Result/Error e torna Idle.</summary>
        public void Dismiss()
        {
            Action post = null;
            lock (_gate)
            {
                if (_disposed || (_state != PushToTalkState.Result && _state != PushToTalkState.Error)) return;
                ClearOutput(); SetState(PushToTalkState.Idle); post += Changed;
            }
            post?.Invoke();
        }

        public void Dispose()
        {
            Action post = null;
            lock (_gate)
            {
                if (_disposed) return;
                Discard(ref post);
                ClearOutput();
                _disposed = true;
                SetState(PushToTalkState.Idle);
                Array.Clear(_buffer, 0, _buffer.Length);
            }
            post?.Invoke();
        }

        // ---- interni (chiamati sotto lock) ----

        private void SetState(PushToTalkState s) { _state = s; _stateAt = _clock(); }

        private void ClearOutput() { _transcript = null; _error = null; _proposed = null; _dictationProposal = null; }

        /// <summary>Chiude la cattura (una volta), annulla il riconoscimento e invalida la generazione.</summary>
        private void Discard(ref Action post)
        {
            _generation++;
            if (_cts != null) { try { _cts.Cancel(); } catch (ObjectDisposedException) { } _cts = null; }
            if (_captureOpen) { _captureOpen = false; post += StopCapture; }
            Array.Clear(_buffer, 0, _count);
            _count = 0;
        }

        private void BeginProcessing(ref Action post)
        {
            _captureOpen = false;
            post += StopCapture;
            int gen = ++_generation;
            short[] audio = new short[_count];
            Array.Copy(_buffer, audio, _count);
            Array.Clear(_buffer, 0, _count);
            _count = 0;
            ClearOutput();
            if (audio.Length < MinCaptureSamples)
            {
                Array.Clear(audio, 0, audio.Length);
                _error = "Registrazione troppo breve. Tieni premuto mentre parli.";
                SetState(PushToTalkState.Error);
                post += Changed;
                return;
            }
            var cts = new CancellationTokenSource();
            _cts = cts;
            SetState(PushToTalkState.Processing);
            post += Changed;
            post += () => Recognize(audio, gen, cts.Token);
        }

        private void Recognize(short[] audio, int gen, CancellationToken ct)
        {
            Task<string> task;
            try { task = _recognizer.RecognizeAsync(audio, ct) ?? Task.FromException<string>(new InvalidOperationException()); }
            catch (Exception e) { task = Task.FromException<string>(e); }
            task.ContinueWith(t => OnRecognized(gen, audio, t), TaskScheduler.Default);
        }

        private void OnRecognized(int gen, short[] audio, Task<string> task)
        {
            Array.Clear(audio, 0, audio.Length); // buffer azzerato a fine richiesta
            string text = null; bool failed = false; string failure = null;
            if (task.IsCanceled || task.IsFaulted)
            {
                failed = true;
                var inner = task.Exception?.GetBaseException() as VoiceRecognitionException; // solo messaggi italiani gia sanificati
                failure = inner?.Message;
            }
            else text = task.Result;
            Action post = null;
            lock (_gate)
            {
                if (_disposed || gen != _generation || _state != PushToTalkState.Processing) return; // tardivo: scartato
                _cts = null;
                if (failed) { _error = failure ?? "Riconoscimento non riuscito. I comandi manuali restano disponibili."; SetState(PushToTalkState.Error); }
                else if (string.IsNullOrWhiteSpace(text)) { _error = "Nessuna frase riconosciuta. I comandi manuali restano disponibili."; SetState(PushToTalkState.Error); }
                else
                {
                    _transcript = text;
                    if (_dictation != null && _dictation.IsArmed) _dictationProposal = _dictation.Propose(text);
                    else _proposed = _router.Route(text, _availability);
                    SetState(PushToTalkState.Result);
                }
                post += Changed;
            }
            post?.Invoke();
        }
    }
}
