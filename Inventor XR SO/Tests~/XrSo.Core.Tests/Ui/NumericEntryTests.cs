using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;

namespace XrSo.Core.Tests.Ui
{
    public class NumericEntryTests
    {
        private static NumericEntry Mm(double v = 20) => new NumericEntry("flange.height", QuantityUnit.Millimeters, v, 0.01, 1000);

        [Fact]
        public void Default_step_is_one_and_cycles_between_tenth_and_ten()
        {
            var e = Mm();
            Assert.Equal(1, e.Step);
            e.CycleStep(+1); Assert.Equal(10, e.Step);
            e.CycleStep(+1); Assert.Equal(10, e.Step);
            e.CycleStep(-1); e.CycleStep(-1); Assert.Equal(0.1, e.Step);
            e.CycleStep(-1); Assert.Equal(0.1, e.Step);
        }

        [Fact]
        public void Nudge_moves_by_step_and_clamps()
        {
            var e = Mm(999.5);
            Assert.True(e.Nudge(+1));
            Assert.Equal(1000, e.Value);
            Assert.False(e.Nudge(+1));
            e.CycleStep(-1);
            var small = Mm(0.05);
            small.CycleStep(-1);
            Assert.True(small.Nudge(-1));
            Assert.Equal(0.01, small.Value, 9);
        }

        [Fact]
        public void Nudge_avoids_floating_point_drift()
        {
            var e = Mm(0);
            e.CycleStep(-1);
            var start = new NumericEntry("x", QuantityUnit.Millimeters, 0.1, 0, 10);
            start.CycleStep(-1);
            start.Nudge(+1); start.Nudge(+1);
            Assert.Equal(0.3, start.Value, 12);
        }

        [Fact]
        public void Keypad_builds_a_comma_decimal_and_commits()
        {
            var e = Mm();
            e.BeginEdit();
            foreach (var c in "12.35") e.Type(c);
            Assert.Equal("12,35", e.Buffer);
            Assert.True(e.Commit(out var reason), reason);
            Assert.Equal(12.35, e.Value, 9);
            Assert.False(e.Editing);
        }

        [Fact]
        public void Keypad_ignores_a_second_separator_and_toggles_sign()
        {
            var e = new NumericEntry("a", QuantityUnit.Degrees, 0, -180, 180);
            e.BeginEdit();
            foreach (var c in "4,5,6") e.Type(c);
            Assert.Equal("4,56", e.Buffer);
            e.Type('-');
            Assert.Equal("-4,56", e.Buffer);
            e.Type('-');
            Assert.Equal("4,56", e.Buffer);
            e.Backspace();
            Assert.Equal("4,5", e.Buffer);
        }

        [Fact]
        public void Out_of_range_commit_is_refused_and_keeps_editing()
        {
            var e = Mm();
            e.BeginEdit();
            foreach (var c in "5000") e.Type(c);
            Assert.False(e.Commit(out var reason));
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.True(e.Editing);
            Assert.Equal(20, e.Value);
        }

        [Fact]
        public void Empty_commit_is_refused()
        {
            var e = Mm();
            e.BeginEdit();
            Assert.False(e.Commit(out _));
        }

        [Fact]
        public void Cancel_restores_the_previous_display()
        {
            var e = Mm();
            e.BeginEdit();
            e.Type('7');
            e.CancelEdit();
            Assert.False(e.Editing);
            Assert.Equal("20 mm", e.Display);
        }

        [Fact]
        public void Display_uses_comma_and_unit()
        {
            Assert.Equal("12,5 mm", Mm(12.5).Display);
            Assert.Equal("90°", new NumericEntry("a", QuantityUnit.Degrees, 90, 0, 180).Display);
            var e = Mm(); e.BeginEdit(); e.Type('3');
            Assert.Equal("3", e.Display);
        }

        [Fact]
        public void SetValue_checks_the_range()
        {
            var e = Mm();
            Assert.True(e.SetValue(33.3, out _));
            Assert.Equal(33.3, e.Value);
            Assert.False(e.SetValue(-1, out var reason));
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Fact]
        public void Changed_fires_on_every_visible_change()
        {
            int n = 0;
            var e = Mm();
            e.Changed += () => n++;
            e.Nudge(+1); e.CycleStep(+1); e.BeginEdit(); e.Type('1'); e.Backspace(); e.CancelEdit();
            Assert.Equal(6, n);
        }

        [Fact]
        public void Committed_fires_only_on_a_confirmed_keypad_value()
        {
            int n = 0;
            var e = Mm();
            e.Committed += () => n++;
            e.Nudge(+1); e.SetValue(5, out _); e.BeginEdit(); e.Type('7'); e.CancelEdit();
            Assert.Equal(0, n);
            e.BeginEdit(); e.Type('7');
            Assert.True(e.Commit(out _));
            Assert.Equal(1, n);
            Assert.Equal(7, e.Value);
        }

        [Fact]
        public void CommitValue_validates_and_confirms_an_open_keypad()
        {
            int n = 0;
            var e = Mm();
            e.Committed += () => n++;
            e.BeginEdit();
            Assert.False(e.CommitValue(-1, out var reason));
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.True(e.Editing); Assert.Equal(0, n);
            Assert.True(e.CommitValue(12, out _));
            Assert.False(e.Editing); Assert.Equal(12, e.Value); Assert.Equal(1, n);
        }
    }
}
