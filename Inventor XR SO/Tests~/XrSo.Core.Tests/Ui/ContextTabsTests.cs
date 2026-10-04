using System.Linq;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class ContextTabsTests
    {
        [Fact]
        public void Assembly_main_tabs()
            => Assert.Equal(new[] { "componenti", "vincoli", "ispeziona", "vista", "documento" },
                ContextTabs.Main(DocContext.Assembly, new TabState()));

        [Fact]
        public void Part_main_tabs()
            => Assert.Equal(new[] { "schizzo", "feature", "parametri", "ispeziona", "vista", "documento" },
                ContextTabs.Main(DocContext.Part, new TabState()));

        [Fact]
        public void SheetMetal_main_tabs()
            => Assert.Equal(new[] { "lamiera", "schizzo", "sviluppo", "ispeziona", "vista", "documento" },
                ContextTabs.Main(DocContext.SheetMetal, new TabState()));

        [Fact]
        public void Inspect_constant()
            => Assert.Equal("ispeziona", ContextTabs.Inspect);

        [Fact]
        public void Inspect_group_per_context()
        {
            Assert.Equal(new[] { "misura", "sezione", "visibilita", "verifica" }, ContextTabs.InspectGroup(DocContext.Assembly));
            Assert.Equal(new[] { "misura", "sezione" }, ContextTabs.InspectGroup(DocContext.Part));
            Assert.Equal(new[] { "misura", "sezione" }, ContextTabs.InspectGroup(DocContext.SheetMetal));
        }

        [Theory]
        [InlineData(DocContext.Part)]
        [InlineData(DocContext.SheetMetal)]
        public void Sketch_constraints_tab_only_with_open_sketch(DocContext c)
        {
            Assert.DoesNotContain("vincoli_schizzo", ContextTabs.Main(c, new TabState()));
            Assert.Contains("vincoli_schizzo", ContextTabs.Main(c, new TabState { SketchOpen = true }));
        }

        [Fact]
        public void Sketch_constraints_never_in_assembly()
            => Assert.DoesNotContain("vincoli_schizzo",
                ContextTabs.Main(DocContext.Assembly, new TabState { SketchOpen = true }));

        [Theory]
        [InlineData(DocContext.Assembly)]
        [InlineData(DocContext.Part)]
        [InlineData(DocContext.SheetMetal)]
        public void Feature_options_comes_first(DocContext c)
        {
            var tabs = ContextTabs.Main(c, new TabState { FeatureInProgress = true });
            Assert.Equal("opzioni_feature", tabs[0]);
            Assert.Equal(ContextTabs.Main(c, new TabState()).Count + 1, tabs.Count);
        }

        [Fact]
        public void Feature_edit_tab_named_after_feature()
        {
            var tabs = ContextTabs.Main(DocContext.Part, new TabState { FeatureEditOpen = true, FeatureName = "Estrusione1" });
            Assert.Contains("feature_Estrusione1", tabs);
        }

        [Theory]
        [InlineData(DocContext.Assembly)]
        [InlineData(DocContext.Part)]
        [InlineData(DocContext.SheetMetal)]
        public void No_duplicates_with_everything_on(DocContext c)
        {
            var s = new TabState { SketchOpen = true, FeatureInProgress = true, FeatureEditOpen = true, FeatureName = "F1" };
            var tabs = ContextTabs.Main(c, s);
            Assert.Equal(tabs.Count, tabs.Distinct().Count());
        }

        [Fact]
        public void Visibility_and_verify_absent_outside_assembly()
        {
            foreach (var c in new[] { DocContext.Part, DocContext.SheetMetal })
            {
                Assert.DoesNotContain("visibilita", ContextTabs.InspectGroup(c));
                Assert.DoesNotContain("verifica", ContextTabs.InspectGroup(c));
            }
        }
    }
}
