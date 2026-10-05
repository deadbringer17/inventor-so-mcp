using InventorXrSo.Core.Navigation;

namespace XrSo.Core.Tests.Navigation
{
    public class NavigationStackTests
    {
        private static NavLevel Asm(string id = "a", string name = "Telaio.iam") => new NavLevel(id, DocContext.Assembly, name);
        private static NavLevel Part(string id = "p", string name = "Staffa.ipt") => new NavLevel(id, DocContext.Part, name, "occ1", new float[16]);

        [Fact]
        public void Reset_sets_single_root_and_cannot_pop()
        {
            var s = new NavigationStack();
            s.Reset(Asm());
            Assert.Single(s.Levels);
            Assert.False(s.CanPop);
            Assert.Null(s.Parent);
            Assert.Throws<InvalidOperationException>(() => s.Pop());
        }

        [Fact]
        public void Push_and_pop_track_top_and_parent()
        {
            var s = new NavigationStack(); s.Reset(Asm());
            s.Push(Part());
            Assert.Equal("p", s.Top.DocumentId);
            Assert.Equal("a", s.Parent.DocumentId);
            Assert.Equal("p", s.Pop().DocumentId);
            Assert.Equal("a", s.Top.DocumentId);
        }

        [Fact]
        public void Breadcrumb_marks_dirty_levels()
        {
            var s = new NavigationStack(); s.Reset(Asm()); s.Push(Part());
            Assert.Equal("Telaio.iam › Staffa.ipt", s.Breadcrumb);
            s.Top.Dirty = true;
            Assert.Equal("Telaio.iam › Staffa.ipt ●", s.Breadcrumb);
        }

        [Fact]
        public void PopTo_keeps_prefix_and_Reset_clears_everything()
        {
            var s = new NavigationStack(); s.Reset(Asm());
            s.Push(new NavLevel("s", DocContext.Assembly, "Sub.iam", "o", new float[16]));
            s.Push(Part());
            s.PopTo(0);
            Assert.Single(s.Levels);
            s.Push(Part()); s.Reset(Asm("z", "Altro.iam"));
            Assert.Single(s.Levels); Assert.Equal("z", s.Top.DocumentId);
        }

        [Fact]
        public void Changed_fires_on_every_mutation()
        {
            var s = new NavigationStack(); int n = 0; s.Changed += () => n++;
            s.Reset(Asm()); s.Push(Part()); s.Pop();
            Assert.Equal(3, n);
        }

        [Fact]
        public void Clear_empties_the_stack_and_notifies()
        {
            var s = new NavigationStack(); int n = 0;
            s.Reset(Asm()); s.Changed += () => n++;
            s.Clear(); s.Clear();
            Assert.Null(s.Top); Assert.Equal(1, n);
        }
    }
}
