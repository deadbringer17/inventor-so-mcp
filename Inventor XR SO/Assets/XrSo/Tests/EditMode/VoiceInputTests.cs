using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Voice;
using InventorXrSo.Xr.Voice;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class VoiceInputTests
    {
        private sealed class Source : IVoiceButtonSource
        {
            public bool Connected { get; set; } = true;
            public bool Tracked { get; set; } = true;
            public bool Down { get; set; }
            public bool Held { get; set; }
        }

        private sealed class Permission : IMicrophonePermission
        {
            public bool IsGranted { get; set; } = true;
            public int Requests;
            public Action<bool> Pending;
            public void Request(Action<bool> done) { Requests++; Pending = done; }
        }

        private sealed class Recognizer : ISpeechRecognizer
        {
            public Task<string> RecognizeAsync(short[] pcm16, CancellationToken ct) => Task.FromResult("flangia");
        }

        private sealed class Availability : ICommandAvailability
        {
            public CommandAvailability GetAvailability(string id) => CommandAvailability.Available;
        }

        private GameObject _go;
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        private Source _source;
        private Permission _permission;
        private PushToTalkController _controller;
        private PushToTalkInput _input;
        private int _starts, _stops;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("voice-input");
            _source = new Source();
            _permission = new Permission();
            _controller = new PushToTalkController(new Recognizer(), new VoiceCommandRouter(), new Availability(), clock: () => _now);
            _controller.StartCapture += () => _starts++;
            _controller.StopCapture += () => _stops++;
            _input = _go.AddComponent<PushToTalkInput>();
            _input.Bind(_controller, _source, _permission);
        }

        [TearDown]
        public void TearDown()
        {
            _controller.Dispose();
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        private void Frame(bool down = false, bool held = false, int advanceMs = 16)
        {
            _source.Down = down; _source.Held = held;
            _now += TimeSpan.FromMilliseconds(advanceMs);
            _input.Step();
        }

        [Test]
        public void DefaultControllerIsTheRightHand()
        {
            Assert.AreEqual(OVRInput.Controller.RTouch, _input.Controller);
        }

        [Test]
        public void HoldingBOpensTheMicrophoneOnlyAfterTheArmingThreshold()
        {
            Frame(down: true, held: true);
            Assert.AreEqual(PushToTalkState.Pressed, _controller.State);
            Assert.AreEqual(0, _starts);
            Frame(held: true, advanceMs: 200);
            Assert.AreEqual(PushToTalkState.Listening, _controller.State);
            Assert.AreEqual(1, _starts);
        }

        [Test]
        public void AQuickTapNeverOpensTheMicrophone()
        {
            Frame(down: true, held: true);
            Frame(held: false, advanceMs: 50);
            Assert.AreEqual(0, _starts);
            Assert.AreEqual(PushToTalkState.Idle, _controller.State);
        }

        [Test]
        public void ReleasingClosesTheMicrophoneOnce()
        {
            Frame(down: true, held: true);
            Frame(held: true, advanceMs: 200);
            Frame(held: false);
            Assert.AreEqual(1, _stops);
            Assert.IsFalse(_controller.MicrophoneOpen);
        }

        [Test]
        public void LosingTrackingWhileListeningInterruptsAndNeedsARelease()
        {
            Frame(down: true, held: true);
            Frame(held: true, advanceMs: 200);
            _source.Tracked = false;
            Frame(held: true);
            Assert.AreEqual(1, _stops);
            Assert.AreEqual(PushToTalkState.Idle, _controller.State);
            _source.Tracked = true;
            Frame(down: true, held: true);              // ancora tenuto: non riparte
            Assert.AreEqual(PushToTalkState.Idle, _controller.State);
            Frame(held: false);
            Frame(down: true, held: true);
            Assert.AreEqual(PushToTalkState.Pressed, _controller.State);
        }

        [Test]
        public void ADisconnectedControllerStopsListening()
        {
            Frame(down: true, held: true);
            Frame(held: true, advanceMs: 200);
            _source.Connected = false;
            Frame(held: true);
            Assert.IsFalse(_controller.MicrophoneOpen);
        }

        [Test]
        public void PressesWithoutTrackingAreIgnored()
        {
            _source.Tracked = false;
            Frame(down: true, held: true);
            Assert.AreEqual(PushToTalkState.Idle, _controller.State);
            Assert.AreEqual(0, _permission.Requests);
        }

        [Test]
        public void PermissionIsRequestedOnFirstPressAndDenialShowsAnErrorWithoutOpeningTheMicrophone()
        {
            _permission.IsGranted = false;
            Frame(down: true, held: true);
            Assert.AreEqual(1, _permission.Requests);
            Assert.AreEqual(PushToTalkState.Idle, _controller.State);
            Frame(held: true);                          // richiesta in corso: nessuna seconda richiesta
            Assert.AreEqual(1, _permission.Requests);
            _permission.Pending(false);
            Frame(held: false);
            Assert.AreEqual(PushToTalkState.Error, _controller.State);
            StringAssert.Contains("Permesso microfono negato", _controller.ErrorMessage);
            Assert.AreEqual(0, _starts);
        }

        [Test]
        public void AGrantedPermissionLetsTheNextPressStartListening()
        {
            _permission.IsGranted = false;
            Frame(down: true, held: true);
            _permission.IsGranted = true;
            _permission.Pending(true);
            Frame(held: false);
            Frame(down: true, held: true);
            Frame(held: true, advanceMs: 200);
            Assert.AreEqual(PushToTalkState.Listening, _controller.State);
        }

        [Test]
        public void ModeChangeAndDisconnectCloseTheMicrophone()
        {
            Frame(down: true, held: true);
            Frame(held: true, advanceMs: 200);
            _input.NotifyModeChanged();
            Assert.AreEqual(1, _stops);
            Assert.IsFalse(_input.Pressing);
            Frame(held: true);                          // tasto ancora giu: serve un rilascio
            Assert.AreEqual(PushToTalkState.Idle, _controller.State);
            Frame(held: false);
            Frame(down: true, held: true);
            Frame(held: true, advanceMs: 200);
            _input.NotifyDisconnected();
            Assert.AreEqual(2, _stops);
        }
    }

    public class AudioResamplerTests
    {
        [Test]
        public void SameRateIsAPassThroughWithClamping()
        {
            var r = new AudioResampler(16000, 16000);
            short[] output = null;
            int n = r.Process(new[] { 0f, 0.5f, -0.5f, 2f, -2f }, 5, ref output);
            Assert.AreEqual(5, n);
            CollectionAssert.AreEqual(new short[] { 0, 16384, -16384, 32767, -32767 }, output.Take(n).ToArray());
        }

        [Test]
        public void DownsamplingByThreeKeepsTheDurationAndAConstantSignal()
        {
            var r = new AudioResampler(48000, 16000);
            short[] output = null;
            var input = Enumerable.Repeat(0.25f, 4800).ToArray();
            int n = r.Process(input, input.Length, ref output);
            Assert.That(n, Is.InRange(1599, 1601));
            Assert.That(output[n / 2], Is.InRange(8100, 8300));
        }

        [Test]
        public void ChunkedProcessingEqualsOneShotProcessing()
        {
            var signal = Enumerable.Range(0, 4410).Select(i => Mathf.Sin(i * 0.05f) * 0.6f).ToArray();
            var whole = new AudioResampler(44100, 16000);
            short[] a = null;
            int na = whole.Process(signal, signal.Length, ref a);
            var chunked = new AudioResampler(44100, 16000);
            var all = new List<short>();
            short[] b = null;
            for (int i = 0; i < signal.Length; i += 441)
            {
                var part = signal.Skip(i).Take(441).ToArray();
                int nb = chunked.Process(part, part.Length, ref b);
                all.AddRange(b.Take(nb));
            }
            Assert.AreEqual(na, all.Count);
            CollectionAssert.AreEqual(a.Take(na).ToArray(), all.ToArray());
        }

        [Test]
        public void DownmixAveragesChannels()
        {
            var mono = new float[3];
            int frames = AudioResampler.Downmix(new[] { 1f, 0f, 0.5f, 0.5f, -1f, 1f }, 3, 2, mono);
            Assert.AreEqual(3, frames);
            CollectionAssert.AreEqual(new[] { 0.5f, 0.5f, 0f }, mono);
        }

        [Test]
        public void ToPcm16HandlesNaNAndRange()
        {
            Assert.AreEqual(0, AudioResampler.ToPcm16(float.NaN));
            Assert.AreEqual(32767, AudioResampler.ToPcm16(5f));
            Assert.AreEqual(-32767, AudioResampler.ToPcm16(-5f));
        }
    }
}
