using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class CommitBarStateTests
    {
        private static CommitBarInputs In(bool online = true, bool unknown = false, bool refresh = false, bool busy = false,
            bool preview = false, bool draft = false, string error = null)
            => new CommitBarInputs(online, unknown, refresh, busy, preview, draft, error);

        [Theory]
        [InlineData(false, true, true, true, true, true, "x", CommitBarPhase.Offline)]
        [InlineData(true, true, true, true, true, true, "x", CommitBarPhase.Uncertain)]
        [InlineData(true, false, true, true, true, true, "x", CommitBarPhase.Stale)]
        [InlineData(true, false, false, true, true, true, "x", CommitBarPhase.Previewing)]
        [InlineData(true, false, false, false, true, true, "x", CommitBarPhase.Error)]
        [InlineData(true, false, false, false, true, true, null, CommitBarPhase.Ready)]
        [InlineData(true, false, false, false, false, true, null, CommitBarPhase.Draft)]
        [InlineData(true, false, false, false, false, false, null, CommitBarPhase.Empty)]
        public void Derive_follows_the_priority_order(bool online, bool unknown, bool refresh, bool busy, bool preview, bool draft, string error, CommitBarPhase expected)
            => Assert.Equal(expected, CommitBarState.Derive(In(online, unknown, refresh, busy, preview, draft, error)));

        [Fact]
        public void Only_ready_allows_apply_and_only_draft_allows_preview()
        {
            var s = new CommitBarState();
            s.Update(In(draft: true), 0);
            Assert.True(s.CanPreview); Assert.False(s.CanApply); Assert.True(s.CanCancel);
            s.Update(In(preview: true), 0);
            Assert.False(s.CanPreview); Assert.True(s.CanApply); Assert.True(s.CanCancel);
            foreach (var bad in new[] { In(online: false, preview: true), In(unknown: true, preview: true), In(refresh: true, preview: true), In(busy: true, preview: true) })
            {
                s.Update(bad, 0);
                Assert.False(s.CanApply);
            }
        }

        [Fact]
        public void Recovery_label_matches_the_blocking_state()
        {
            var s = new CommitBarState();
            s.Update(In(refresh: true), 0);
            Assert.Equal("Aggiorna documento", s.RecoveryLabel);
            s.Update(In(unknown: true), 0);
            Assert.Equal("Ho controllato il CAD", s.RecoveryLabel);
            s.Update(In(draft: true), 0);
            Assert.Null(s.RecoveryLabel);
        }

        [Fact]
        public void Error_message_is_kept()
        {
            var s = new CommitBarState();
            s.Update(In(error: "Validazione fallita"), 0);
            Assert.Equal(CommitBarPhase.Error, s.Phase);
            Assert.Equal("Validazione fallita", s.Message);
        }

        [Fact]
        public void Applied_lasts_one_and_a_half_seconds_then_empties()
        {
            var s = new CommitBarState();
            s.MarkApplied(10);
            s.Update(In(), 10.5);
            Assert.Equal(CommitBarPhase.Applied, s.Phase);
            s.Tick(11.6);
            Assert.Equal(CommitBarPhase.Empty, s.Phase);
        }

        [Fact]
        public void Blocking_states_override_applied_immediately()
        {
            var s = new CommitBarState();
            s.MarkApplied(10);
            s.Update(In(online: false), 10.1);
            Assert.Equal(CommitBarPhase.Offline, s.Phase);
        }

        [Fact]
        public void Changed_fires_only_on_real_changes()
        {
            int n = 0;
            var s = new CommitBarState();
            s.Changed += () => n++;
            s.Update(In(draft: true), 0);
            s.Update(In(draft: true), 0);
            s.Update(In(preview: true), 0);
            Assert.Equal(2, n);
        }
    }
}
