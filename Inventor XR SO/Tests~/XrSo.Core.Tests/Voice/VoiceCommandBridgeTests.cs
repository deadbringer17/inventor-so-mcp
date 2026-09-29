using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Voice;

namespace XrSo.Core.Tests.Voice
{
    public class VoiceCommandBridgeTests
    {
        private sealed class Target : IVoiceCommandTarget, IContextVoiceActions
        {
            public HashSet<string> Enabled = new HashSet<string>();
            public List<string> Invoked = new List<string>();
            public bool InvokeResult = true;
            public bool Accepts = true;
            public bool AcceptsVoice => Accepts;
            public int ApplyShown;
            public DictationField Field;
            public Dictionary<string, double> Fields = new Dictionary<string, double>();
            public ContextVoiceAction ContextAction;
            public bool TryResolveAction(string transcript, out ContextVoiceAction action)
            { action = transcript == "browser" || transcript == "annulla modifica xr" ? ContextAction : null; return action != null; }
            public bool IsEnabled(string id) => Enabled.Contains(id);
            public string DisabledReason(string id) => "Seleziona prima uno spigolo.";
            public bool Invoke(string id) { Invoked.Add(id); return InvokeResult; }
            public DictationField ArmedField => Field;
            public void SetField(string id, double v) => Fields[id] = v;
            public void ShowApplyConfirmation() => ApplyShown++;
        }

        private sealed class Recognizer : ISpeechRecognizer
        {
            public string Text = "flangia";
            public Task<string> RecognizeAsync(short[] pcm16, CancellationToken ct) => Task.FromResult(Text);
        }

        private readonly Target _target = new Target();
        private readonly Recognizer _rec = new Recognizer();
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        private VoiceCommandBridge Make() => new VoiceCommandBridge(_target, _rec, () => _now);

        private void Speak(VoiceCommandBridge b, string text)
        {
            _rec.Text = text;
            b.Pump(); // il frame principale pubblica la snapshot di abilitazione prima dell'STT
            b.Controller.Press(); _now += TimeSpan.FromMilliseconds(200); b.Controller.Tick();
            b.Controller.AppendAudio(new short[16000], 16000);
            b.Controller.Release();
            for (int i = 0; i < 200 && b.Controller.State == PushToTalkState.Processing; i++) Thread.Sleep(10);
            Assert.Equal(PushToTalkState.Result, b.Controller.State);
        }

        [Fact]
        public void Visible_workspace_action_runs_on_Pump_and_history_requires_confirmation()
        {
            using var b = Make();
            _target.ContextAction = new ContextVoiceAction("ui:Browser", "Browser", true, false);
            _target.Enabled.Add("ui:Browser");
            Speak(b, "browser");
            Assert.Empty(_target.Invoked);
            b.Pump();
            Assert.Equal(new[] { "ui:Browser" }, _target.Invoked);

            _target.ContextAction = new ContextVoiceAction("ui:Annulla modifica XR", "Annulla modifica XR", true, true);
            _target.Enabled.Add("ui:Annulla modifica XR");
            Speak(b, "annulla modifica xr");
            b.Pump();
            Assert.Equal(VoiceOutcomeKind.AwaitingConfirmation, b.Outcome);
            Assert.Single(_target.Invoked);
            Assert.True(b.ConfirmPendingAny());
            Assert.Equal("ui:Annulla modifica XR", _target.Invoked[1]);
        }

        [Fact]
        public void Recognized_enabled_command_is_invoked_only_from_Pump_like_the_button()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Flange);
            Speak(b, "flangia");
            Assert.Empty(_target.Invoked);              // nessun effetto fuori dal Pump (thread principale)
            b.Pump();
            Assert.Equal(new[] { CommandIds.Flange }, _target.Invoked);
            Assert.Equal(VoiceOutcomeKind.Executed, b.Outcome);
            b.Pump(); b.Pump();
            Assert.Single(_target.Invoked);              // una sola volta
        }

        [Fact]
        public void Disabled_command_is_not_invoked_and_shows_the_reason()
        {
            using var b = Make();
            Speak(b, "flangia");
            b.Pump();
            Assert.Empty(_target.Invoked);
            Assert.Equal(VoiceOutcomeKind.NotExecuted, b.Outcome);
            Assert.Equal("Seleziona prima uno spigolo.", b.OutcomeText);
        }

        [Fact]
        public void Enablement_is_reread_at_execution_time()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Flange);
            Speak(b, "flangia");
            _target.Enabled.Clear();                      // la selezione e cambiata mentre si elaborava
            b.Pump();
            Assert.Empty(_target.Invoked);
            Assert.Equal(VoiceOutcomeKind.NotExecuted, b.Outcome);
        }

        [Fact]
        public void A_button_that_refuses_is_reported_not_executed()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Flange); _target.InvokeResult = false;
            Speak(b, "flangia");
            b.Pump();
            Assert.Equal(VoiceOutcomeKind.NotExecuted, b.Outcome);
        }

        [Fact]
        public void Applica_shows_the_confirmation_and_never_invokes_the_command()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Apply);
            Speak(b, "applica");
            b.Pump(); b.Pump();
            Assert.Equal(1, _target.ApplyShown);
            Assert.Empty(_target.Invoked);
            Assert.Equal(VoiceOutcomeKind.ApplyConfirmationShown, b.Outcome);
        }

        [Fact]
        public void Annulla_without_draft_asks_for_a_physical_confirmation_of_undo()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Undo);
            Speak(b, "annulla");
            b.Pump();
            Assert.Empty(_target.Invoked);
            Assert.Equal(VoiceOutcomeKind.AwaitingConfirmation, b.Outcome);
            Assert.Equal(CommandIds.Undo, b.PendingConfirmationCommandId);
            Assert.True(b.ConfirmPending());
            Assert.Equal(new[] { CommandIds.Undo }, _target.Invoked);
            Assert.Null(b.PendingConfirmationCommandId);
            Assert.False(b.ConfirmPending());
        }

        [Fact]
        public void Dismissing_a_pending_confirmation_never_invokes()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Undo);
            Speak(b, "annulla");
            b.Pump();
            b.DismissPending();
            Assert.False(b.ConfirmPending());
            Assert.Empty(_target.Invoked);
        }

        [Fact]
        public void Unknown_phrase_is_rejected_without_action()
        {
            using var b = Make();
            Speak(b, "cancella tutto");
            b.Pump();
            Assert.Equal(VoiceOutcomeKind.Rejected, b.Outcome);
            Assert.Empty(_target.Invoked);
        }

        [Fact]
        public void Dictation_is_a_proposal_until_confirmed_and_touches_only_the_armed_field()
        {
            using var b = Make();
            _target.Field = new DictationField("length", QuantityUnit.Millimeters, 0, 5000);
            _target.Fields["length"] = 1; _target.Fields["angle"] = 2;
            b.Pump();                                    // arma il campo
            Speak(b, "dodici virgola cinque millimetri");
            b.Pump();
            Assert.Equal(VoiceOutcomeKind.DictationProposed, b.Outcome);
            Assert.Equal(1, _target.Fields["length"]);
            Assert.Empty(_target.Invoked);
            Assert.True(b.ConfirmDictation());
            Assert.Equal(12.5, _target.Fields["length"]);
            Assert.Equal(2, _target.Fields["angle"]);
        }

        [Fact]
        public void Dictation_without_an_armed_field_changes_nothing()
        {
            using var b = Make();
            _target.Fields["length"] = 1;
            Speak(b, "dodici millimetri");
            b.Pump();
            Assert.False(b.ConfirmDictation());
            Assert.Equal(1, _target.Fields["length"]);
        }

        [Fact]
        public void Changing_the_armed_field_discards_the_proposal()
        {
            using var b = Make();
            _target.Field = new DictationField("length", QuantityUnit.Millimeters, 0, 5000);
            b.Pump();
            Speak(b, "dieci millimetri");
            b.Pump();
            _target.Field = new DictationField("angle", QuantityUnit.Degrees, 0, 180);
            b.Pump();
            Assert.False(b.ConfirmDictation());
        }

        [Fact]
        public void Mode_change_closes_the_microphone_and_drops_proposals()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Undo);
            int stops = 0; b.Controller.StopCapture += () => stops++;
            b.Controller.Press(); _now += TimeSpan.FromMilliseconds(200); b.Controller.Tick();
            Assert.True(b.Controller.MicrophoneOpen);
            b.NotifyModeChanged();
            Assert.False(b.Controller.MicrophoneOpen);
            Assert.Equal(1, stops);
            Assert.Equal(PushToTalkState.Idle, b.Controller.State);
            Assert.Equal(VoiceOutcomeKind.None, b.Outcome);
        }

        [Fact]
        public void Disconnection_drops_a_pending_confirmation()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Undo);
            Speak(b, "annulla"); b.Pump();
            b.NotifyDisconnected();
            Assert.Null(b.PendingConfirmationCommandId);
            Assert.False(b.ConfirmPending());
            Assert.Empty(_target.Invoked);
        }

        [Fact]
        public void A_pending_confirmation_expires()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Undo);
            Speak(b, "annulla"); b.Pump();
            Assert.True(b.HasPending);
            _now += b.PendingTimeout + TimeSpan.FromSeconds(1);
            b.Pump();
            Assert.False(b.HasPending);
            Assert.False(b.ConfirmPending());
            Assert.Empty(_target.Invoked);
        }

        [Fact]
        public void Speaking_again_drops_the_previous_pending_confirmation()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Undo);
            Speak(b, "annulla"); b.Pump();
            b.Controller.Press(); b.Pump();
            Assert.False(b.HasPending);
            Assert.False(b.ConfirmPending());
        }

        [Fact]
        public void ViewModel_follows_the_state_and_keeps_confirm_visible_after_the_result_expires()
        {
            using var b = Make();
            Assert.False(VoiceViewModel.Build(b).Visible);
            _target.Enabled.Add(CommandIds.Undo);
            b.Pump();
            b.Controller.Press(); _now += TimeSpan.FromMilliseconds(200); b.Controller.Tick();
            var listening = VoiceViewModel.Build(b);
            Assert.True(listening.Visible); Assert.True(listening.Listening); Assert.Equal("Ascolto…", listening.Title);
            b.Controller.AppendAudio(new short[16000], 16000); _rec.Text = "annulla";
            b.Controller.Release();
            for (int i = 0; i < 200 && b.Controller.State == PushToTalkState.Processing; i++) Thread.Sleep(10);
            b.Pump();
            var result = VoiceViewModel.Build(b);
            Assert.Equal("\u201Cannulla\u201D", result.Transcript);
            Assert.True(result.ShowConfirm);
            Assert.Contains("Conferma", result.ConfirmLabel);
            _now += b.Controller.ResultDisplayTime + TimeSpan.FromSeconds(1); b.Controller.Tick();
            Assert.Equal(PushToTalkState.Idle, b.Controller.State);
            var later = VoiceViewModel.Build(b);
            Assert.True(later.Visible); Assert.True(later.ShowConfirm);
        }

        [Fact]
        public void ViewModel_shows_the_readable_error()
        {
            using var b = Make();
            b.Controller.Interrupt(VoiceInterruption.PermissionDenied);
            var m = VoiceViewModel.Build(b);
            Assert.True(m.Visible);
            Assert.Contains("Permesso microfono negato", m.Error);
        }

        [Fact]
        public void Physical_buttons_confirm_or_cancel_whichever_is_pending()
        {
            using var b = Make();
            _target.Enabled.Add(CommandIds.Undo);
            Speak(b, "annulla"); b.Pump();
            b.CancelPendingAny();
            Assert.False(b.HasPending);
            Assert.False(b.ConfirmPendingAny());
            Assert.Empty(_target.Invoked);
            Speak(b, "annulla"); b.Pump();
            Assert.True(b.ConfirmPendingAny());
            Assert.Equal(new[] { CommandIds.Undo }, _target.Invoked);
        }

        [Fact]
        public void Press_is_refused_without_opening_the_microphone_when_voice_is_not_accepted()
        {
            using var b = Make();
            _target.Accepts = false;
            int starts = 0, refused = 0;
            b.Controller.StartCapture += () => starts++;
            b.Controller.CaptureRefused += () => refused++;
            Assert.False(b.Controller.CanCapture);
            b.Controller.Press(); _now += TimeSpan.FromMilliseconds(500); b.Controller.Tick();
            Assert.Equal(PushToTalkState.Idle, b.Controller.State);
            Assert.False(b.Controller.MicrophoneOpen);
            Assert.Equal(0, starts);
            Assert.Equal(1, refused);
            Assert.Equal("Voce disponibile solo in sessione", VoiceCommandBridge.NotInSessionText);
        }

        [Fact]
        public void Press_works_again_once_the_target_accepts_voice()
        {
            using var b = Make();
            _target.Accepts = false;
            b.Controller.Press();
            Assert.Equal(PushToTalkState.Idle, b.Controller.State);
            _target.Accepts = true;
            Assert.True(b.Controller.CanCapture);
            b.Controller.Press(); _now += TimeSpan.FromMilliseconds(200); b.Controller.Tick();
            Assert.Equal(PushToTalkState.Listening, b.Controller.State);
            Assert.True(b.Controller.MicrophoneOpen);
        }
    }
}
