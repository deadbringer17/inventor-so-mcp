using System.Linq;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Ui;
using InventorXrSo.Xr;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public class ViewActionsTests
    {
        [TearDown] public void TearDown() => PlayerPrefs.DeleteKey(ViewActions.LegendPrefKey);

        [Test]
        public void DeclaresTheOneSharedVistaTab()
        {
            var view = new ViewActions(() => true, () => { });
            Assert.AreEqual(new[] { "vista" }, view.Tabs.Select(t => t.Id).ToArray());
            Assert.True(view.Actions.All(a => a.Tab == "vista"));
            Assert.LessOrEqual(view.Actions.Count(), ActionCatalog.MaxPalette);
        }

        [Test]
        public void AdattaFitsTheActiveWorkspaceAndSaysWhyWhenItCannot()
        {
            int fits = 0; bool can = true;
            var view = new ViewActions(() => can, () => fits++);
            var fit = view.Actions.Single(a => a.Id == ViewActions.IdFit);
            Assert.True(fit.TryInvoke()); Assert.AreEqual(1, fits);
            can = false;
            fit = view.Actions.Single(a => a.Id == ViewActions.IdFit);
            Assert.False(fit.TryInvoke()); Assert.False(string.IsNullOrEmpty(fit.DisabledReason)); Assert.AreEqual(1, fits);
        }

        [Test]
        public void LegendIsOnByDefaultAndTheChoiceIsKeptOnTheHeadset()
        {
            PlayerPrefs.DeleteKey(ViewActions.LegendPrefKey);
            var view = new ViewActions(() => true, () => { });
            Assert.True(view.LegendOn, "default: on");
            var legend = view.Actions.Single(a => a.Id == ViewActions.IdLegend);
            StringAssert.Contains("sì", legend.Label); Assert.True(legend.IsOn);
            bool seen = true; view.LegendChanged += on => seen = on;
            Assert.True(legend.TryInvoke());
            Assert.False(view.LegendOn); Assert.False(seen);
            Assert.AreEqual(0, PlayerPrefs.GetInt(ViewActions.LegendPrefKey, 1), "preference saved under xrso.legend");
            legend = view.Actions.Single(a => a.Id == ViewActions.IdLegend);
            StringAssert.Contains("no", legend.Label); Assert.False(legend.IsOn);
            Assert.False(new ViewActions(() => true, () => { }).LegendOn, "a new session reads the saved choice");
        }

        [Test]
        public void ChangingTheLegendNotifiesTheCatalog()
        {
            bool legend = true; int changes = 0;
            var view = new ViewActions(() => true, () => { }, () => legend, on => legend = on) { Changed = () => changes++ };
            view.Actions.Single(a => a.Id == ViewActions.IdLegend).TryInvoke();
            Assert.False(legend); Assert.AreEqual(1, changes);
        }

        [Test]
        public void TheRealProvidersShareOneVistaTabAndKeepWithinEightActions()
        {
            var go = new GameObject("providers");
            var rig = new InspectRig(attachScene: false);
            try
            {
                var assembly = go.AddComponent<AssemblyWorkspace>();
                var design = go.AddComponent<DesignWorkspace>();
                var lamiera = go.AddComponent<LamieraWorkspace>();
                foreach (var (provider, context) in new (IActionProvider, DocContext)[]
                {
                    (assembly, DocContext.Assembly), (design, DocContext.Part), (lamiera, DocContext.SheetMetal),
                })
                {
                    Assert.False(provider.Tabs.Any(t => t.Id == "vista"), provider.GetType().Name + " no longer declares Vista");
                    rig.Context = context; rig.Catalog.SetActive(provider);
                    Assert.AreEqual(1, rig.Catalog.Tabs.Count(t => t.Id == "vista"), context.ToString());
                    Assert.LessOrEqual(rig.Catalog.Palette("vista").Count, ActionCatalog.MaxPalette, context + " Vista");
                    Assert.True(rig.Catalog.Palette("vista").Any(a => a.Id == ViewActions.IdFit && a.Label == "Adatta"));
                    foreach (var tab in rig.Catalog.Tabs) Assert.Greater(rig.Catalog.Palette(tab.Id).Count, 0, context + " " + tab.Id);
                }
            }
            finally { rig.Dispose(); Object.DestroyImmediate(go); }
        }
    }
}
