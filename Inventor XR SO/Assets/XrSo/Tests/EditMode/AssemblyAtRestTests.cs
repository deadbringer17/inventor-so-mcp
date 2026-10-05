using System.Threading.Tasks;
using InventorXrSo.Xr;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    /// <summary>M9-04: AtRest mirrors the Back chain (Torna only when X has nothing else to close).</summary>
    public sealed partial class AssemblyWorkspaceTests
    {
        [Test] public void AtRest_TrueWhenOpenAndIdle() => Assert.True(_workspace.AtRest);

        [Test] public async Task AtRest_FalseWithAnIsolatedComponent()
        {
            await Select(); Do(AssemblyWorkspace.IdIsolate);
            Assert.True(_workspace.Isolation.Active);
            Assert.False(_workspace.AtRest);
        }

        [Test] public async Task AtRest_FalseWhileACommandIsBeingDrafted()
        {
            await Select(); _workspace.BeginMove();
            Assert.False(_workspace.AtRest);
        }

        [Test] public void AtRest_FalseWhenClosed()
        {
            _workspace.Close();
            Assert.False(_workspace.AtRest);
        }
    }
}
