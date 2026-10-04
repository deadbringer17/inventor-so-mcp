using InventorXrSo.Core.Navigation;

namespace XrSo.Core.Tests.Navigation
{
    public class ContextRouterTests
    {
        private static DocInfo Asm(string id = "a", string name = "Telaio.iam") => new DocInfo(id, name, "assembly", false);
        private static DocInfo Part(string id = "p", string name = "Staffa.ipt", bool sm = false) => new DocInfo(id, name, "part", sm);

        private static (NavigationStack s, ContextRouter r) Make()
        {
            var s = new NavigationStack();
            return (s, new ContextRouter(s));
        }

        [Fact]
        public void Empty_stack_resets_with_root_and_no_message()
        {
            var (s, r) = Make();
            var res = r.OnActiveDocument(Asm());
            Assert.Equal(RouteChange.Reset, res.Change);
            Assert.Equal(DocContext.Assembly, res.Context);
            Assert.Null(res.Message);
            Assert.Single(s.Levels);
            Assert.Equal("a", s.Top.DocumentId);
        }

        [Fact]
        public void Same_id_is_None()
        {
            var (s, r) = Make();
            r.OnActiveDocument(Asm());
            var res = r.OnActiveDocument(Asm());
            Assert.Equal(RouteChange.None, res.Change);
            Assert.Single(s.Levels);
        }

        [Fact]
        public void Same_id_reports_sheet_metal_subtype()
        {
            var (_, r) = Make();
            r.OnActiveDocument(Part("p", "Staffa.ipt", false));
            var res = r.OnActiveDocument(Part("p", "Staffa.ipt", true));
            Assert.Equal(RouteChange.None, res.Change);
            Assert.Equal(DocContext.SheetMetal, res.Context);
        }

        [Fact]
        public void ExpectEntry_then_active_pushes_with_occurrence_and_pose()
        {
            var (s, r) = Make();
            r.OnActiveDocument(Asm());
            var pose = new float[16]; pose[0] = 1;
            r.ExpectEntry("p", "occ7", pose);
            var res = r.OnActiveDocument(Part());
            Assert.Equal(RouteChange.Pushed, res.Change);
            Assert.Equal(DocContext.Part, res.Context);
            Assert.Equal(2, s.Levels.Count);
            Assert.Equal("occ7", s.Top.FromOccurrenceId);
            Assert.Same(pose, s.Top.OccurrencePose);
        }

        [Fact]
        public void ExpectEntry_is_consumed()
        {
            var (s, r) = Make();
            r.OnActiveDocument(Asm());
            r.ExpectEntry("p", "occ7", new float[16]);
            r.OnActiveDocument(Part());
            r.OnActiveDocument(Asm());                 // torna al padre
            var res = r.OnActiveDocument(Part());      // senza nuovo ExpectEntry
            Assert.Equal(RouteChange.Reset, res.Change);
            Assert.Single(s.Levels);
        }

        [Fact]
        public void Parent_id_pops()
        {
            var (s, r) = Make();
            r.OnActiveDocument(Asm());
            r.ExpectEntry("p", "occ1", new float[16]);
            r.OnActiveDocument(Part());
            var res = r.OnActiveDocument(Asm());
            Assert.Equal(RouteChange.Popped, res.Change);
            Assert.Equal(DocContext.Assembly, res.Context);
            Assert.Single(s.Levels);
            Assert.Equal("a", s.Top.DocumentId);
        }

        [Fact]
        public void Unknown_id_resets_with_message()
        {
            var (s, r) = Make();
            r.OnActiveDocument(Asm());
            var res = r.OnActiveDocument(Part("x", "Staffa.ipt"));
            Assert.Equal(RouteChange.Reset, res.Change);
            Assert.Equal("Documento cambiato dal PC: Staffa.ipt", res.Message);
            Assert.Single(s.Levels);
            Assert.Equal("x", s.Top.DocumentId);
        }

        [Fact]
        public void Sheet_metal_part_maps_to_SheetMetal()
        {
            Assert.Equal(DocContext.SheetMetal, ContextRouter.ContextOf(Part("p", "L.ipt", true)));
            Assert.Equal(DocContext.Part, ContextRouter.ContextOf(Part()));
            Assert.Equal(DocContext.Assembly, ContextRouter.ContextOf(Asm()));
        }

        [Fact]
        public void Drawing_is_unsupported_and_leaves_stack_unchanged()
        {
            var (s, r) = Make();
            r.OnActiveDocument(Asm());
            var res = r.OnActiveDocument(new DocInfo("d", "Tavola.idw", "drawing", false));
            Assert.Equal(RouteChange.None, res.Change);
            Assert.Null(res.Context);
            Assert.Equal("Documento non supportato: Tavola.idw", res.Message);
            Assert.Single(s.Levels);
            Assert.Equal("a", s.Top.DocumentId);
            Assert.Null(ContextRouter.ContextOf(new DocInfo("d", "Tavola.idw", "drawing", false)));
        }

        [Fact]
        public void Unconsumed_ExpectEntry_is_discarded_by_other_id()
        {
            var (s, r) = Make();
            r.OnActiveDocument(Asm());
            r.ExpectEntry("p", "occ1", new float[16]);
            var other = r.OnActiveDocument(Part("x", "Altro.ipt"));
            Assert.Equal(RouteChange.Reset, other.Change);
            // il pendente e stato scartato: "p" ora e sconosciuto, non Pushed
            var res = r.OnActiveDocument(Part("p", "Staffa.ipt"));
            Assert.Equal(RouteChange.Reset, res.Change);
            Assert.Single(s.Levels);
        }
    }
}
