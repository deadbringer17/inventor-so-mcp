using Bimwright.Ipt.Server;

namespace Bimwright.Ipt.Tests;

public sealed class SoToolPolicyTests
{
    [Theory]
    [InlineData(false, "assembly", true)]
    [InlineData(true, "assembly", false)]
    [InlineData(false, "query", false)]
    [InlineData(false, "all", true)]
    public void SafeAssemblyRespectsToolsetsAndReadOnly(bool readOnly, string toolset, bool visible)
    {
        var config = new InventorMcpConfig { ReadOnly = readOnly, Toolsets = { toolset } };
        Assert.Equal(visible, Program.ResolveToolTypesForRegistration(config).Contains(typeof(Bimwright.Ipt.Server.Tools.SafeAssemblyTools)));
        Assert.True(SoToolPolicy.IsExposed("inventor_edit_constraint_safe"));
        Assert.True(SoToolPolicy.IsExposed("inventor_create_constraint_safe"));
        Assert.True(SoToolPolicy.IsExposed("inventor_insert_component_safe"));
    }
    [Theory]
    [InlineData(false, "export", true)]
    [InlineData(true, "export", false)]
    [InlineData(false, "query", false)]
    [InlineData(false, "all", true)]
    public void ArtifactRespectsToolsetsAndReadOnly(bool readOnly, string toolset, bool visible)
    {
        var config = new InventorMcpConfig { ReadOnly = readOnly, Toolsets = { toolset } };
        Assert.Equal(visible, Program.ResolveToolTypesForRegistration(config).Contains(typeof(Bimwright.Ipt.Server.Tools.SafeArtifactTools)));
        Assert.True(SoToolPolicy.IsExposed("inventor_save_artifact"));
        Assert.True(SoToolPolicy.IsExposed("inventor_checkpoint_create"));
        Assert.True(SoToolPolicy.IsExposed("inventor_create_drawing_safe"));
        Assert.True(SoToolPolicy.IsExposed("inventor_checkpoint_restore"));
    }
    [Theory]
    [InlineData("inventor_set_parameter")]
    [InlineData("inventor_send_code")]
    [InlineData("inventor_run_baked_tool")]
    [InlineData("inventor_export_step")]
    [InlineData("inventor_future_write")]
    public void UnreviewedAndDirectWritesHidden(string name) => Assert.False(SoToolPolicy.IsExposed(name));

    [Theory]
    [InlineData("inventor_atomic_batch")]
    [InlineData("inventor_get_parameter")]
    [InlineData("inventor_get_selection")]
    [InlineData("inventor_checkpoint_list")]
    [InlineData("inventor_diff_checkpoint")]
    public void ReviewedToolsVisible(string name) => Assert.True(SoToolPolicy.IsExposed(name));
}
