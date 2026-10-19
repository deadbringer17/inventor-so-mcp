using System.Linq;
using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class SketchCommandCatalogTests
    {
        [Fact]
        public void Every_native_command_has_one_destination_and_an_explicit_development_reason()
        {
            var commands = SketchCommandCatalog.Commands;
            Assert.Equal(77, commands.Count);
            Assert.Equal(77, commands.Select(c => c.NativeId).Distinct().Count());
            foreach (var command in commands)
            {
                Assert.Contains(command.Group, SketchCommandCatalog.Groups);
                Assert.Contains(SketchCommandCatalog.Tabs, t => t.Id == (command.Group == "Crea" ? command.FamilyTab : command.Tab));
                Assert.False(string.IsNullOrWhiteSpace(command.UnavailableReason));
            }
            foreach (var action in SketchCommandCatalog.UnavailableActions())
            {
                Assert.False(action.Enabled); Assert.False(action.TryInvoke());
                Assert.Contains("S1", action.DisabledReason);
            }
        }

        [Fact]
        public void Contextual_commands_and_sketch_operations_do_not_alias_feature_commands()
        {
            Assert.Equal(6, SketchCommandCatalog.Commands.Count(c => c.Advanced));
            Assert.Contains(SketchCommandCatalog.Commands, c => c.NativeId == "SketchFilletCmd" && c.Id != "design.fillet");
            Assert.Contains(SketchCommandCatalog.Commands, c => c.NativeId == "SketchRectangularPatternCmd" && c.Phase == "F5");
        }
    }
}
