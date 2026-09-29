using System.Linq;
using InventorXrSo.Core.Ui;
using InventorXrSo.Xr;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public class SpacesActionsTests
    {
        [Test]
        public void SpacesExposeTheFiveEntriesInTheSpacesTab()
        {
            var s = new SpacesActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => true, () => true, () => true, () => true);
            Assert.AreEqual(ActionCatalog.SpacesTab, s.Tabs.Single().Id);
            CollectionAssert.AreEqual(
                new[] { "spaces.inspect", "spaces.design", "spaces.lamiera", "spaces.assembly", "spaces.connection" },
                s.Actions.Select(a => a.Id).ToArray());
            Assert.IsTrue(s.Actions.All(a => a.Tab == ActionCatalog.SpacesTab));
        }

        [Test]
        public void WorkspaceEntriesFollowSessionAndCanEnterGuards()
        {
            bool session = false, lamiera = false;
            int opened = 0;
            var s = new SpacesActions(() => { }, () => opened++, () => opened++, () => { }, () => { }, () => session, () => true, () => lamiera, () => true);
            var catalog = new ActionCatalog(s);
            Assert.IsFalse(catalog.TryInvoke("spaces.design"), "no session");
            Assert.IsTrue(catalog.Find("spaces.connection").Enabled, "connection always reachable");
            session = true;
            Assert.IsTrue(catalog.TryInvoke("spaces.design"));
            Assert.IsFalse(catalog.TryInvoke("spaces.lamiera"));
            Assert.IsFalse(string.IsNullOrEmpty(catalog.Find("spaces.lamiera").DisabledReason));
            lamiera = true;
            Assert.IsTrue(catalog.TryInvoke("spaces.lamiera"));
            Assert.AreEqual(2, opened);
        }
    }
}
