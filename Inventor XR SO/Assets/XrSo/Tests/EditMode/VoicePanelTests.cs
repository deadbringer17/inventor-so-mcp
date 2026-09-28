using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Voice;
using InventorXrSo.Xr.Voice;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class VoicePanelTests
    {
        private sealed class Target : IVoiceCommandTarget
        {
            public bool IsEnabled(string id) => id == CommandIds.Undo;
            public string DisabledReason(string id) => "";
            public bool Invoke(string id) => true;
            public DictationField ArmedField => null;
            public void SetField(string id, double v) { }
            public void ShowApplyConfirmation() { }
        }

        private sealed class Recognizer : ISpeechRecognizer
        {
            public Task<string> RecognizeAsync(short[] pcm16, CancellationToken ct) => Task.FromResult("annulla");
        }

        private GameObject _root;

        [TearDown]
        public void TearDown() { if (_root != null) UnityEngine.Object.DestroyImmediate(_root); }

        [Test]
        public void ThePanelIsHiddenWhenIdleAndShowsListeningThenTheConfirmation()
        {
            _root = new GameObject("root");
            var panel = VoicePanel.Create(_root.transform, null);
            var now = DateTimeOffset.UtcNow;
            using (var bridge = new VoiceCommandBridge(new Target(), new Recognizer(), () => now))
            {
                panel.Bind(bridge);
                panel.Apply(VoiceViewModel.Build(bridge));
                Assert.IsFalse(panel.IsShown);

                bridge.Controller.Press(); now += TimeSpan.FromMilliseconds(200); bridge.Controller.Tick();
                panel.Apply(VoiceViewModel.Build(bridge));
                Assert.IsTrue(panel.IsShown);
                Assert.AreEqual("Ascolto…", panel.TitleText);

                bridge.Controller.AppendAudio(new short[16000], 16000);
                bridge.Controller.Release();
                for (int i = 0; i < 300 && bridge.Controller.State == PushToTalkState.Processing; i++) Thread.Sleep(10);
                bridge.Pump();
                panel.Apply(VoiceViewModel.Build(bridge));
                StringAssert.Contains("annulla", panel.TranscriptText);
                Assert.AreEqual("Confermare?", panel.TitleText);
                Assert.IsTrue(panel.ConfirmVisible);
            }
        }

        [Test]
        public void ANoticeIsShownEvenWhenTheControllerIsIdle()
        {
            _root = new GameObject("root");
            var panel = VoicePanel.Create(_root.transform, null);
            panel.ShowNotice("Microfono non disponibile.");
            panel.Apply(new VoiceViewModel());
            Assert.IsTrue(panel.IsShown);
            StringAssert.Contains("Microfono non disponibile", panel.ErrorText);
        }
    }
}
