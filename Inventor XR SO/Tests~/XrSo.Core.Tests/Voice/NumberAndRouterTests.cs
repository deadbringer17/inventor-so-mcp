using System;
using System.Collections.Generic;
using InventorXrSo.Core.Voice;

namespace XrSo.Core.Tests.Voice
{
    public class NumberParserTests
    {
        [Theory]
        [InlineData("12,5 mm", 12.5, QuantityUnit.Millimeters)]
        [InlineData("12.5 mm", 12.5, QuantityUnit.Millimeters)]
        [InlineData("12,5mm", 12.5, QuantityUnit.Millimeters)]
        [InlineData("-12,5 mm", -12.5, QuantityUnit.Millimeters)]
        [InlineData("- 12 mm", -12, QuantityUnit.Millimeters)]
        [InlineData("+5 mm", 5, QuantityUnit.Millimeters)]
        [InlineData("45°", 45, QuantityUnit.Degrees)]
        [InlineData("90 gradi", 90, QuantityUnit.Degrees)]
        [InlineData("Piu dieci millimetri", 10, QuantityUnit.Millimeters)]
        [InlineData("Più dieci millimetri", 10, QuantityUnit.Millimeters)]
        [InlineData("centoventi", 120, QuantityUnit.None)]
        [InlineData("millecinquanta", 1050, QuantityUnit.None)]
        [InlineData("mille e cinquanta", 1050, QuantityUnit.None)]
        [InlineData("centouno", 101, QuantityUnit.None)]
        [InlineData("trentotto", 38, QuantityUnit.None)]
        [InlineData("centotto", 108, QuantityUnit.None)]
        [InlineData("tremilaquattrocentocinquantasei", 3456, QuantityUnit.None)]
        [InlineData("meno zero virgola cinque gradi", -0.5, QuantityUnit.Degrees)]
        [InlineData("zero virgola zero cinque", 0.05, QuantityUnit.None)]
        [InlineData("dodici virgola venticinque", 12.25, QuantityUnit.None)]
        public void Parses(string text, double value, QuantityUnit unit)
        {
            var r = ItalianNumberParser.Parse(text);
            Assert.True(r.Ok, r.Reason);
            Assert.Equal(value, r.Value, 9);
            Assert.Equal(unit, r.Unit);
        }

        [Fact]
        public void Minus_zero_is_plain_zero()
        {
            var r = ItalianNumberParser.Parse("meno zero");
            Assert.True(r.Ok);
            Assert.False(double.IsNegative(r.Value));
        }

        [Theory]
        [InlineData("dodici centimetri", NumberParseError.AmbiguousUnit)]
        [InlineData("12 cm", NumberParseError.AmbiguousUnit)]
        [InlineData("dodici pollici", NumberParseError.AmbiguousUnit)]
        [InlineData("12\"", NumberParseError.AmbiguousUnit)]
        [InlineData("dodici metri", NumberParseError.AmbiguousUnit)]
        [InlineData("1.500 mm", NumberParseError.AmbiguousSeparator)]
        [InlineData("1.234,5 mm", NumberParseError.AmbiguousSeparator)]
        [InlineData("1.5.3", NumberParseError.AmbiguousSeparator)]
        [InlineData("12,5,3", NumberParseError.AmbiguousSeparator)]
        [InlineData("", NumberParseError.Empty)]
        [InlineData("millimetri", NumberParseError.NoNumber)]
        [InlineData("virgola cinque", NumberParseError.NoIntegerPart)]
        [InlineData("dodici virgola", NumberParseError.BadFraction)]
        [InlineData("dodici virgola cinque virgola tre", NumberParseError.MultipleDecimalSeparators)]
        [InlineData("duemille", NumberParseError.NotANumber)]
        [InlineData("cinque venti", NumberParseError.NotANumber)]
        [InlineData("cento cento", NumberParseError.NotANumber)]
        [InlineData("nan", NumberParseError.NotANumber)]
        [InlineData("infinito", NumberParseError.NotANumber)]
        [InlineData("un milione", NumberParseError.OutOfRange)]
        [InlineData("99999999999999999999", NumberParseError.OutOfRange)]
        [InlineData("1e308", NumberParseError.NotANumber)]
        public void Rejects(string text, NumberParseError error)
        {
            var r = ItalianNumberParser.Parse(text);
            Assert.False(r.Ok);
            Assert.Equal(error, r.Error);
            Assert.False(string.IsNullOrEmpty(r.Reason));
        }

        [Fact]
        public void Range_comes_from_armed_field()
        {
            Assert.True(ItalianNumberParser.Parse("cento gradi", 0, 180).Ok);
            var r = ItalianNumberParser.Parse("duecento gradi", 0, 180);
            Assert.Equal(NumberParseError.OutOfRange, r.Error);
            Assert.Equal(NumberParseError.OutOfRange, ItalianNumberParser.Parse("meno uno", 0, 180).Error);
            Assert.Throws<ArgumentException>(() => ItalianNumberParser.Parse("1", 5, 1));
        }

        [Fact]
        public void Garbage_is_bounded()
        {
            Assert.False(ItalianNumberParser.Parse(new string('c', 500)).Ok);
            Assert.False(ItalianNumberParser.Parse(string.Join(" ", new string[40].Length > 0 ? new[] { "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno", "uno" } : null)).Ok);
        }
    }

    public class NormalizerTests
    {
        [Theory]
        [InlineData("  Crea   SVILUPPO! ", "crea sviluppo")]
        [InlineData("Più, però", "piu pero")]
        [InlineData("l'ultima", "l ultima")]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("...", "")]
        [InlineData("12,5 mm", "12 5 mm")]
        public void Normalizes(string input, string expected) => Assert.Equal(expected, ItalianTextNormalizer.Normalize(input));
    }

    public class CommandRouterTests
    {
        private readonly VoiceCommandRouter _router = new VoiceCommandRouter();
        private static ICommandAvailability Only(params string[] enabled)
        {
            var set = new HashSet<string>(enabled);
            return new DelegateCommandAvailability(id => set.Contains(id) ? CommandAvailability.Available : CommandAvailability.Disabled("Non disponibile: " + id));
        }
        private static readonly ICommandAvailability All = new DelegateCommandAvailability(_ => CommandAvailability.Available);

        [Fact]
        public void Command_ids_unique_and_every_id_has_aliases()
        {
            Assert.Equal(CommandIds.All.Count, new HashSet<string>(CommandIds.All).Count);
            foreach (var id in CommandIds.All) Assert.NotEmpty(_router.AliasesOf(id));
            Assert.Contains("sheetmetal.flange", CommandIds.All);
            Assert.False(CommandIds.IsKnown("nope"));
        }

        [Fact]
        public void Every_alias_routes_to_its_command()
        {
            foreach (var id in CommandIds.All)
                foreach (var a in _router.AliasesOf(id))
                {
                    foreach (var text in new[] { a, a.ToUpperInvariant() + ".", "  " + a + "  " })
                    {
                        var r = _router.Route(text, All);
                        Assert.NotEqual(VoiceRouteKind.Rejected, r.Kind);
                        if (r.Kind == VoiceRouteKind.NeedsConfirmation && id == CommandIds.CancelDraft)
                            Assert.Equal(CommandIds.Undo, r.CommandId);
                        else Assert.Equal(id, r.CommandId);
                    }
                }
        }

        [Theory]
        [InlineData("")] [InlineData("crea")] [InlineData("sviluppo")] [InlineData("flangia smusso")]
        [InlineData("fai una flangia e uno smusso")] [InlineData("annulla e applica")] [InlineData("cancella tutto")]
        [InlineData("flangia per favore")] [InlineData("dodici")] [InlineData("boh")] [InlineData("flangia flangia")]
        public void Ambiguous_or_free_text_is_rejected_and_never_invokable(string text)
        {
            var r = _router.Route(text, All);
            Assert.Equal(VoiceRouteKind.Rejected, r.Kind);
            Assert.False(r.CanInvoke);
            Assert.Null(r.CommandId);
            Assert.False(string.IsNullOrEmpty(r.Reason));
        }

        [Fact]
        public void Disabled_command_is_recognized_with_reason_and_not_invokable()
        {
            var r = _router.Route("flangia", Only(CommandIds.Measure));
            Assert.Equal(VoiceRouteKind.Recognized, r.Kind);
            Assert.Equal(CommandIds.Flange, r.CommandId);
            Assert.False(r.Enabled);
            Assert.False(r.CanInvoke);
            Assert.Contains("Non disponibile", r.Reason);
            Assert.True(_router.Route("misura", Only(CommandIds.Measure)).CanInvoke);
        }

        [Fact]
        public void Disabled_without_reason_gets_default_reason()
        {
            var r = _router.Route("misura", new DelegateCommandAvailability(_ => CommandAvailability.Disabled(null)));
            Assert.False(r.Enabled);
            Assert.False(string.IsNullOrEmpty(r.Reason));
        }

        [Fact]
        public void Annulla_cancels_active_draft()
        {
            var r = _router.Route("annulla", Only(CommandIds.CancelDraft, CommandIds.Undo));
            Assert.Equal(VoiceRouteKind.Recognized, r.Kind);
            Assert.Equal(CommandIds.CancelDraft, r.CommandId);
            Assert.True(r.CanInvoke);
        }

        [Fact]
        public void Annulla_without_draft_asks_confirmation_for_undo_and_does_not_invoke()
        {
            var r = _router.Route("annulla", Only(CommandIds.Undo));
            Assert.Equal(VoiceRouteKind.NeedsConfirmation, r.Kind);
            Assert.Equal(CommandIds.Undo, r.CommandId);
            Assert.False(r.CanInvoke);
            Assert.True(r.RequiresPhysicalConfirmation);
            Assert.Contains("Annullare l'ultima modifica", r.Reason);
        }

        [Fact]
        public void Annulla_with_nothing_to_cancel_is_disabled()
        {
            var r = _router.Route("annulla", Only());
            Assert.Equal(VoiceRouteKind.Recognized, r.Kind);
            Assert.False(r.CanInvoke);
        }

        [Fact]
        public void Explicit_undo_and_redo_use_their_own_ids()
        {
            Assert.Equal(CommandIds.Undo, _router.Route("annulla ultima modifica", All).CommandId);
            Assert.Equal(CommandIds.Redo, _router.Route("ripeti", All).CommandId);
            Assert.True(_router.Route("annulla bozza", All).CanInvoke);
        }

        [Fact]
        public void Applica_never_invokes_only_shows_confirmation()
        {
            foreach (var a in _router.AliasesOf(CommandIds.Apply))
            {
                var r = _router.Route(a, All);
                Assert.Equal(VoiceRouteKind.ShowApplyConfirmation, r.Kind);
                Assert.Equal(CommandIds.Apply, r.CommandId);
                Assert.False(r.CanInvoke);
                Assert.True(r.RequiresPhysicalConfirmation);
            }
            var off = _router.Route("applica", Only());
            Assert.False(off.CanInvoke);
            Assert.NotEqual(VoiceRouteKind.ShowApplyConfirmation, off.Kind);
            Assert.True(CommandIds.RequiresPhysicalConfirmation(CommandIds.Apply));
        }

        [Fact]
        public void Null_availability_throws() => Assert.Throws<ArgumentNullException>(() => _router.Route("flangia", null));
    }
}
