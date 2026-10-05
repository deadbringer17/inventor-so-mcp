using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Voice;

namespace XrSo.Core.Tests.Voice
{
    /// <summary>M9-10: i comandi di spazio non eseguono nulla e spiegano il nuovo modo; apri per nome; niente Applica a voce.</summary>
    public class SpaceCommandTests
    {
        private static readonly ICommandAvailability AllEnabled = new DelegateCommandAvailability(_ => CommandAvailability.Available);

        [Theory]
        [InlineData("vai in progettazione")] [InlineData("progettazione")] [InlineData("lamiera")] [InlineData("assieme")]
        [InlineData("assemblaggio")] [InlineData("ispeziona")] [InlineData("vai in lamiera")] [InlineData("Vai in assieme!")]
        [InlineData("vai in ispezione")] [InlineData("passa a progettazione")]
        public void Space_commands_are_rejected_with_an_explanation(string phrase)
        {
            var route = new VoiceCommandRouter().Route(phrase, AllEnabled);
            Assert.Equal(VoiceRouteKind.Rejected, route.Kind);
            Assert.Equal(VoiceRejectReason.SpaceCommand, route.RejectReason);
            Assert.False(route.CanInvoke);
            Assert.Contains("apri <componente>", route.Reason);
            Assert.Contains("torna", route.Reason);
            Assert.Contains("doppio Trigger", route.Reason);
        }

        [Fact]
        public void Space_phrases_are_in_the_stt_grammar_but_never_map_to_a_command()
        {
            var router = new VoiceCommandRouter();
            Assert.Contains("vai in progettazione", router.GrammarPhrases);
            foreach (var p in SpaceCommands.Phrases) Assert.False(CommandIds.IsKnown(p));
        }

        [Fact]
        public void Apply_is_never_invoked_by_voice_even_with_a_space_aware_router()
        {
            var route = new VoiceCommandRouter().Route("applica", AllEnabled);
            Assert.Equal(VoiceRouteKind.ShowApplyConfirmation, route.Kind);
            Assert.False(route.CanInvoke);
            Assert.True(CommandIds.RequiresPhysicalConfirmation(CommandIds.Apply));
        }

        private static readonly KeyValuePair<string, string>[] Parts =
        {
            new KeyValuePair<string, string>("o1", "Staffa:1"), new KeyValuePair<string, string>("o2", "Staffa:2"),
            new KeyValuePair<string, string>("o3", "Piastra base:1"), new KeyValuePair<string, string>("o4", "Vite M6:1"),
        };

        [Fact]
        public void Unique_name_is_found_with_or_without_the_instance_suffix()
        {
            Assert.Equal("o3", OccurrenceNameMatcher.Match(Parts, "piastra base").OccurrenceId);
            Assert.Equal("o3", OccurrenceNameMatcher.Match(Parts, "Piastra base 1").OccurrenceId);
            Assert.Equal("o4", OccurrenceNameMatcher.Match(Parts, "vite m6").OccurrenceId);
            Assert.Equal("o2", OccurrenceNameMatcher.Match(Parts, "staffa 2").OccurrenceId);
        }

        [Fact]
        public void Ambiguous_and_unknown_names_reply_without_a_match()
        {
            var ambiguous = OccurrenceNameMatcher.Match(Parts, "staffa");
            Assert.Equal(VoiceOpenStatus.Ambiguous, ambiguous.Status);
            Assert.Null(ambiguous.OccurrenceId);
            var none = OccurrenceNameMatcher.Match(Parts, "flangia");
            Assert.Equal(VoiceOpenStatus.NotFound, none.Status);
            Assert.False(none.Found);
            Assert.Equal(VoiceOpenStatus.NotFound, OccurrenceNameMatcher.Match(Parts, "").Status);
        }

        // ---- bridge: la risposta di un'azione contestuale rifiutata arriva al HUD, e un comando di spazio non esegue.

        private sealed class Target : IVoiceCommandTarget, IContextVoiceActions
        {
            public List<string> Invoked = new List<string>();
            public ContextVoiceAction Action;
            public bool AcceptsVoice => true;
            public bool TryResolveAction(string transcript, out ContextVoiceAction action)
            { action = transcript == "apri flangia" ? Action : null; return action != null; }
            public bool IsEnabled(string id) => true;
            public string DisabledReason(string id) => "";
            public bool Invoke(string id) { Invoked.Add(id); return true; }
            public DictationField ArmedField => null;
            public void SetField(string id, double v) { }
            public void ShowApplyConfirmation() { }
        }

        private sealed class Recognizer : ISpeechRecognizer
        {
            public string Text = "";
            public Task<string> RecognizeAsync(short[] pcm16, CancellationToken ct) => Task.FromResult(Text);
        }

        private static void Speak(VoiceCommandBridge b, Recognizer rec, ref DateTimeOffset now, string text)
        {
            rec.Text = text;
            b.Pump();
            b.Controller.Press(); now += TimeSpan.FromMilliseconds(200); b.Controller.Tick();
            b.Controller.AppendAudio(new short[16000], 16000);
            b.Controller.Release();
            for (int i = 0; i < 200 && b.Controller.State == PushToTalkState.Processing; i++) Thread.Sleep(10);
            Assert.Equal(PushToTalkState.Result, b.Controller.State);
            b.Pump();
        }

        [Fact]
        public void Spoken_space_command_executes_nothing_and_shows_the_explanation()
        {
            var target = new Target(); var rec = new Recognizer(); var now = DateTimeOffset.UtcNow;
            DateTimeOffset clock() => now;
            using var b = new VoiceCommandBridge(target, rec, clock);
            Speak(b, rec, ref now, "vai in progettazione");
            Assert.Equal(VoiceOutcomeKind.Rejected, b.Outcome);
            Assert.Equal(SpaceCommands.Explanation, b.OutcomeText);
            Assert.Empty(target.Invoked);
        }

        [Fact]
        public void Refused_context_action_shows_its_reply_and_never_invokes()
        {
            var target = new Target { Action = new ContextVoiceAction("reply:flangia", "Apri flangia", false, false, "Componente non trovato: flangia.") };
            var rec = new Recognizer(); var now = DateTimeOffset.UtcNow;
            DateTimeOffset clock() => now;
            using var b = new VoiceCommandBridge(target, rec, clock);
            Speak(b, rec, ref now, "apri flangia");
            Assert.Equal(VoiceOutcomeKind.NotExecuted, b.Outcome);
            Assert.Equal("Componente non trovato: flangia.", b.OutcomeText);
            Assert.Empty(target.Invoked);
        }
    }
}
