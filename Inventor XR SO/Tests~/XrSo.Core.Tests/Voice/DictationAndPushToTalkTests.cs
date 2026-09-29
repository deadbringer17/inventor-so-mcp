using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Voice;

namespace XrSo.Core.Tests.Voice
{
    public class DictationTargetTests
    {
        private readonly Dictionary<string, double> _fields = new Dictionary<string, double> { { "length", 1 }, { "angle", 2 }, { "radius", 3 } };
        private DictationTarget Make() => new DictationTarget((id, v) => _fields[id] = v);

        [Fact]
        public void Unarmed_is_rejected_and_touches_nothing()
        {
            var d = Make();
            var p = d.Propose("dodici millimetri");
            Assert.False(p.Accepted);
            Assert.False(d.Confirm(p));
            Assert.False(d.Confirm());
            Assert.Equal(1, _fields["length"]); Assert.Equal(2, _fields["angle"]); Assert.Equal(3, _fields["radius"]);
        }

        [Fact]
        public void Proposal_does_not_change_field_until_confirmed_then_only_armed_field_changes()
        {
            var d = Make();
            d.Arm("length", QuantityUnit.Millimeters, 0, 5000);
            var p = d.Propose("dodici virgola cinque millimetri");
            Assert.True(p.Accepted);
            Assert.Equal(12.5, p.Value);
            Assert.Equal("length", p.FieldId);
            Assert.Equal("dodici virgola cinque millimetri", p.OriginalText);
            Assert.Contains("12,5", p.DisplayText);
            Assert.Equal(1, _fields["length"]);
            Assert.True(d.Confirm());
            Assert.Equal(12.5, _fields["length"]);
            Assert.Equal(2, _fields["angle"]); Assert.Equal(3, _fields["radius"]);
            Assert.False(d.Confirm()); // una sola volta
        }

        [Fact]
        public void Rearming_another_field_discards_pending_proposal()
        {
            var d = Make();
            d.Arm("length", QuantityUnit.Millimeters, 0, 5000);
            var p = d.Propose("dieci millimetri");
            d.Arm("angle", QuantityUnit.Degrees, 0, 180);
            Assert.False(d.Confirm(p));
            Assert.False(d.Confirm());
            Assert.Equal(1, _fields["length"]); Assert.Equal(2, _fields["angle"]);
        }

        [Fact]
        public void Disarm_discards_pending()
        {
            var d = Make();
            d.Arm("length", QuantityUnit.Millimeters, 0, 5000);
            var p = d.Propose("dieci millimetri");
            d.Disarm();
            Assert.False(d.Confirm(p));
            Assert.Equal(1, _fields["length"]);
        }

        [Fact]
        public void Out_of_range_unit_mismatch_and_ambiguous_are_not_proposed()
        {
            var d = Make();
            d.Arm("angle", QuantityUnit.Degrees, 0, 180);
            Assert.Equal(NumberParseError.OutOfRange, d.Propose("duecento gradi").Error);
            Assert.Equal(NumberParseError.UnitMismatch, d.Propose("dieci millimetri").Error);
            Assert.Equal(NumberParseError.AmbiguousUnit, d.Propose("dieci pollici").Error);
            Assert.False(d.Propose("flangia").Accepted);
            Assert.Null(d.Pending);
            Assert.False(d.Confirm());
            Assert.Equal(2, _fields["angle"]);
        }

        [Fact]
        public void Missing_unit_is_assumed_from_field_and_flagged()
        {
            var d = Make();
            d.Arm("length", QuantityUnit.Millimeters, 0, 5000);
            var p = d.Propose("venticinque");
            Assert.True(p.Accepted);
            Assert.True(p.UnitAssumed);
            Assert.Equal(QuantityUnit.Millimeters, p.Unit);
        }

        [Fact]
        public void Metres_are_accepted_only_for_an_armed_visual_scale_field()
        {
            var d = Make();
            d.Arm("length", QuantityUnit.Meters, 0.2, 20);
            var p = d.Propose("zero virgola cinque metri");
            Assert.True(p.Accepted);
            Assert.Equal(0.5, p.Value);
            Assert.False(p.UnitAssumed);
            Assert.Contains(" m", p.DisplayText);
            Assert.Equal(1, _fields["length"]);
            Assert.True(d.Confirm());
            Assert.Equal(0.5, _fields["length"]);
            Assert.Equal(NumberParseError.AmbiguousUnit, ItalianNumberParser.Parse("zero virgola cinque metri").Error);
        }

        [Fact]
        public void Proposal_from_another_target_instance_cannot_confirm()
        {
            var a = Make(); var b = Make();
            a.Arm("length", QuantityUnit.Millimeters, 0, 100);
            b.Arm("length", QuantityUnit.Millimeters, 0, 100);
            var p = a.Propose("dieci");
            b.Propose("venti");
            Assert.False(b.Confirm(p));
        }
    }

    public class PushToTalkTests
    {
        private sealed class Clock { public DateTimeOffset Now = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero); public void Ms(int ms) => Now = Now.AddMilliseconds(ms); }

        private sealed class FakeRecognizer : ISpeechRecognizer
        {
            public readonly List<TaskCompletionSource<string>> Pending = new List<TaskCompletionSource<string>>();
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public readonly List<short[]> Audio = new List<short[]>();
            public Func<Task<string>> Override;
            public Task<string> RecognizeAsync(short[] pcm16, CancellationToken ct)
            {
                Audio.Add(pcm16); Tokens.Add(ct);
                if (Override != null) return Override();
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                Pending.Add(tcs);
                return tcs.Task;
            }
        }

        private sealed class Rig
        {
            public Clock Clock = new Clock();
            public FakeRecognizer Rec = new FakeRecognizer();
            public HashSet<string> Enabled = new HashSet<string>(CommandIds.All);
            public int Starts, Stops, Changes;
            public DictationTarget Dictation;
            public Dictionary<string, double> Fields = new Dictionary<string, double> { { "length", 1 }, { "angle", 2 } };
            public PushToTalkController Ptt;
            public Rig(bool dictation = false)
            {
                if (dictation) Dictation = new DictationTarget((id, v) => Fields[id] = v);
                Ptt = new PushToTalkController(Rec, new VoiceCommandRouter(),
                    new DelegateCommandAvailability(id => Enabled.Contains(id) ? CommandAvailability.Available : CommandAvailability.Disabled("Non disponibile ora.")),
                    Dictation, () => Clock.Now);
                Ptt.StartCapture += () => Starts++;
                Ptt.StopCapture += () => Stops++;
                Ptt.Changed += () => Changes++;
            }
            public void HoldToListening() { Ptt.Press(); Clock.Ms(150); Ptt.Tick(); }
            public void Speak(int samples = 16000) => Ptt.AppendAudio(new short[samples], samples);
            public void HoldSpeakRelease() { HoldToListening(); Speak(); Ptt.Release(); }
            public static void Wait(Func<bool> cond) { var sw = System.Diagnostics.Stopwatch.StartNew(); while (!cond() && sw.ElapsedMilliseconds < 3000) Thread.Sleep(5); Assert.True(cond()); }
            public void Complete(int index, string text) { Rec.Pending[index].SetResult(text); }
        }

        [Fact]
        public void Mic_opens_only_after_arming_threshold_and_short_tap_never_opens()
        {
            var r = new Rig();
            r.Ptt.Press();
            Assert.Equal(PushToTalkState.Pressed, r.Ptt.State);
            r.Clock.Ms(149); r.Ptt.Tick();
            Assert.Equal(PushToTalkState.Pressed, r.Ptt.State);
            Assert.False(r.Ptt.MicrophoneOpen);
            r.Ptt.Release();
            Assert.Equal(PushToTalkState.Idle, r.Ptt.State);
            Assert.Equal(0, r.Starts); Assert.Equal(0, r.Stops);
            r.Ptt.Press(); r.Clock.Ms(150); r.Ptt.Tick();
            Assert.Equal(PushToTalkState.Listening, r.Ptt.State);
            Assert.True(r.Ptt.MicrophoneOpen);
            Assert.Equal(1, r.Starts);
        }

        [Fact]
        public void Audio_is_ignored_outside_listening()
        {
            var r = new Rig();
            r.Speak(); r.Ptt.Press(); r.Speak();
            r.Clock.Ms(150); r.Ptt.Tick(); r.Ptt.Release(); // nessun audio catturato
            Assert.Equal(PushToTalkState.Error, r.Ptt.State);
            Assert.Empty(r.Rec.Audio);
        }

        [Fact]
        public void Release_closes_capture_once_and_produces_proposed_command()
        {
            var r = new Rig();
            r.HoldSpeakRelease();
            Assert.Equal(1, r.Stops);
            Assert.False(r.Ptt.MicrophoneOpen);
            Assert.Equal(PushToTalkState.Processing, r.Ptt.State);
            Assert.True(r.Ptt.IsProcessing);
            r.Ptt.Release(); // secondo rilascio ignorato
            Assert.Equal(1, r.Stops);
            r.Complete(0, "Crea sviluppo");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result);
            Assert.Equal("Crea sviluppo", r.Ptt.Transcript);
            Assert.Equal(CommandIds.FlatPatternCreate, r.Ptt.ProposedCommand.CommandId);
            Assert.True(r.Ptt.ProposedCommand.CanInvoke);
            Assert.Equal(1, r.Stops);
        }

        [Theory]
        [InlineData(VoiceInterruption.TrackingLost)]
        [InlineData(VoiceInterruption.ModeChanged)]
        [InlineData(VoiceInterruption.Disconnected)]
        [InlineData(VoiceInterruption.PermissionDenied)]
        public void Interruption_while_listening_closes_capture_exactly_once(VoiceInterruption why)
        {
            var r = new Rig();
            r.HoldToListening(); r.Speak();
            r.Ptt.Interrupt(why);
            Assert.Equal(1, r.Stops);
            Assert.False(r.Ptt.MicrophoneOpen);
            Assert.Equal(why == VoiceInterruption.PermissionDenied ? PushToTalkState.Error : PushToTalkState.Idle, r.Ptt.State);
            r.Ptt.Interrupt(why); r.Ptt.Release(); r.Ptt.Dispose();
            Assert.Equal(1, r.Stops);
            Assert.Empty(r.Rec.Audio); // niente inviato al riconoscitore
        }

        [Fact]
        public void Permission_denied_before_listening_leaves_manual_commands_intact()
        {
            var r = new Rig();
            r.Ptt.Press();
            r.Ptt.Interrupt(VoiceInterruption.PermissionDenied);
            Assert.Equal(PushToTalkState.Error, r.Ptt.State);
            Assert.Contains("manuali", r.Ptt.ErrorMessage);
            Assert.Equal(0, r.Stops); Assert.Equal(0, r.Starts);
            Assert.Null(r.Ptt.ProposedCommand);
            r.Clock.Ms(4000); r.Ptt.Tick();
            Assert.Equal(PushToTalkState.Idle, r.Ptt.State);
            r.Ptt.Press(); // si puo riprovare
            Assert.Equal(PushToTalkState.Pressed, r.Ptt.State);
        }

        [Fact]
        public void Interruption_while_processing_cancels_recognition_and_discards_late_result()
        {
            var r = new Rig();
            r.HoldSpeakRelease();
            r.Ptt.Interrupt(VoiceInterruption.ModeChanged);
            Assert.True(r.Rec.Tokens[0].IsCancellationRequested);
            Assert.Equal(PushToTalkState.Idle, r.Ptt.State);
            r.Complete(0, "flangia");
            Thread.Sleep(100);
            Assert.Equal(PushToTalkState.Idle, r.Ptt.State);
            Assert.Null(r.Ptt.Transcript); Assert.Null(r.Ptt.ProposedCommand);
            Assert.Equal(1, r.Stops);
        }

        [Fact]
        public void Late_result_from_previous_generation_is_discarded()
        {
            var r = new Rig();
            r.HoldSpeakRelease();
            int gen1 = r.Ptt.Generation;
            r.HoldSpeakRelease(); // nuova pressione mentre la prima e in elaborazione
            Assert.True(r.Ptt.Generation > gen1);
            Assert.True(r.Rec.Tokens[0].IsCancellationRequested);
            Assert.Equal(2, r.Stops);
            r.Complete(0, "misura");   // tardivo
            Thread.Sleep(100);
            Assert.Equal(PushToTalkState.Processing, r.Ptt.State);
            Assert.Null(r.Ptt.ProposedCommand);
            r.Complete(1, "flangia");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result);
            Assert.Equal(CommandIds.Flange, r.Ptt.ProposedCommand.CommandId);
        }

        [Fact]
        public void Dispose_closes_capture_cancels_and_ignores_everything_after()
        {
            var r = new Rig();
            r.HoldSpeakRelease();
            r.Ptt.Dispose();
            Assert.True(r.Rec.Tokens[0].IsCancellationRequested);
            r.Complete(0, "flangia"); Thread.Sleep(100);
            r.Ptt.Press(); r.Ptt.Tick();
            Assert.Equal(PushToTalkState.Idle, r.Ptt.State);
            Assert.Equal(1, r.Stops); Assert.Equal(1, r.Starts);

            var r2 = new Rig();
            r2.HoldToListening();
            r2.Ptt.Dispose(); r2.Ptt.Dispose();
            Assert.Equal(1, r2.Stops);
        }

        [Fact]
        public void Recognizer_failure_or_empty_transcript_leaves_no_proposal()
        {
            var r = new Rig();
            r.Rec.Override = () => Task.FromException<string>(new InvalidOperationException("segreto"));
            r.HoldSpeakRelease();
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Error);
            Assert.Null(r.Ptt.ProposedCommand);
            Assert.DoesNotContain("segreto", r.Ptt.ErrorMessage);
            Assert.Contains("manuali", r.Ptt.ErrorMessage);

            var r2 = new Rig();
            r2.Rec.Override = () => Task.FromResult("   ");
            r2.HoldSpeakRelease();
            Rig.Wait(() => r2.Ptt.State == PushToTalkState.Error);
            Assert.Null(r2.Ptt.ProposedCommand);

            var r3 = new Rig();
            r3.Rec.Override = () => throw new InvalidOperationException();
            r3.HoldSpeakRelease();
            Rig.Wait(() => r3.Ptt.State == PushToTalkState.Error);
        }

        [Fact]
        public void Too_short_recording_does_not_reach_recognizer()
        {
            var r = new Rig();
            r.HoldToListening(); r.Speak(100); r.Ptt.Release();
            Assert.Equal(PushToTalkState.Error, r.Ptt.State);
            Assert.Empty(r.Rec.Audio);
            Assert.Equal(1, r.Stops);
        }

        [Fact]
        public void Audio_buffer_is_cleared_after_request()
        {
            var r = new Rig();
            r.HoldToListening();
            r.Ptt.AppendAudio(Fill(16000, 7), 16000); r.Ptt.Release();
            var audio = r.Rec.Audio[0];
            Assert.Equal(16000, audio.Length);
            Assert.Equal(7, audio[0]);
            r.Complete(0, "flangia");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result);
            Assert.All(audio, s => Assert.Equal(0, s));
        }

        private static short[] Fill(int n, short v) { var a = new short[n]; Array.Fill(a, v); return a; }

        [Fact]
        public void Capture_length_limit_acts_like_release()
        {
            var r = new Rig();
            r.HoldToListening();
            r.Ptt.AppendAudio(new short[PushToTalkController.MaxCaptureSamples + 5], PushToTalkController.MaxCaptureSamples + 5);
            Assert.Equal(PushToTalkState.Processing, r.Ptt.State);
            Assert.Equal(1, r.Stops);
            Assert.Equal(PushToTalkController.MaxCaptureSamples, r.Rec.Audio[0].Length);
        }

        [Fact]
        public void Disabled_command_transcript_is_proposed_disabled_and_ambiguous_is_rejected()
        {
            var r = new Rig();
            r.Enabled.Remove(CommandIds.Flange);
            r.HoldSpeakRelease(); r.Complete(0, "flangia");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result);
            Assert.Equal(CommandIds.Flange, r.Ptt.ProposedCommand.CommandId);
            Assert.False(r.Ptt.ProposedCommand.CanInvoke);
            Assert.NotEmpty(r.Ptt.ProposedCommand.Reason);

            r.HoldSpeakRelease(); r.Complete(1, "annulla e applica");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result && r.Ptt.Transcript == "annulla e applica");
            Assert.Equal(VoiceRouteKind.Rejected, r.Ptt.ProposedCommand.Kind);
            Assert.False(r.Ptt.ProposedCommand.CanInvoke);
        }

        [Fact]
        public void Voice_apply_never_commits()
        {
            var r = new Rig();
            r.HoldSpeakRelease(); r.Complete(0, "Applica");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result);
            var p = r.Ptt.ProposedCommand;
            Assert.Equal(VoiceRouteKind.ShowApplyConfirmation, p.Kind);
            Assert.Equal(CommandIds.Apply, p.CommandId);
            Assert.False(p.CanInvoke);
            Assert.True(p.RequiresPhysicalConfirmation);
        }

        [Fact]
        public void Armed_field_routes_transcript_to_dictation_only_and_confirm_updates_only_that_field()
        {
            var r = new Rig(dictation: true);
            r.Dictation.Arm("length", QuantityUnit.Millimeters, 0, 5000);
            r.HoldSpeakRelease(); r.Complete(0, "dodici virgola cinque millimetri");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result);
            Assert.Null(r.Ptt.ProposedCommand);
            Assert.Equal(12.5, r.Ptt.ProposedDictation.Value);
            Assert.Equal(1, r.Fields["length"]);
            Assert.True(r.Dictation.Confirm());
            Assert.Equal(12.5, r.Fields["length"]); Assert.Equal(2, r.Fields["angle"]);

            // con campo armato un comando parlato non e un valore
            r.HoldSpeakRelease(); r.Complete(1, "flangia");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result && r.Ptt.Transcript == "flangia");
            Assert.False(r.Ptt.ProposedDictation.Accepted);
            Assert.Null(r.Ptt.ProposedCommand);
        }

        [Fact]
        public void Unarmed_dictation_number_is_not_a_command()
        {
            var r = new Rig(dictation: true);
            r.HoldSpeakRelease(); r.Complete(0, "dodici millimetri");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result);
            Assert.Equal(VoiceRouteKind.Rejected, r.Ptt.ProposedCommand.Kind);
            Assert.Null(r.Ptt.ProposedDictation);
            Assert.Equal(1, r.Fields["length"]);
        }

        [Fact]
        public void Result_expires_to_idle_and_clears_text()
        {
            var r = new Rig();
            r.HoldSpeakRelease(); r.Complete(0, "flangia");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result);
            r.Clock.Ms(3000); r.Ptt.Tick();
            Assert.Equal(PushToTalkState.Result, r.Ptt.State);
            r.Clock.Ms(1500); r.Ptt.Tick();
            Assert.Equal(PushToTalkState.Idle, r.Ptt.State);
            Assert.Null(r.Ptt.Transcript); Assert.Null(r.Ptt.ProposedCommand);
        }

        [Fact]
        public void Press_during_result_starts_new_request_and_clears_previous_output()
        {
            var r = new Rig();
            r.HoldSpeakRelease(); r.Complete(0, "flangia");
            Rig.Wait(() => r.Ptt.State == PushToTalkState.Result);
            r.Ptt.Press();
            Assert.Equal(PushToTalkState.Pressed, r.Ptt.State);
            Assert.Null(r.Ptt.Transcript); Assert.Null(r.Ptt.ProposedCommand);
        }
    }
}
