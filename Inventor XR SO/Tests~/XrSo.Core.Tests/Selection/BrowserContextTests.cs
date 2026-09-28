using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Selection;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Selection;

public class BrowserContextTests
{
    private static SceneGraph Graph(string document = "a", bool nested = true) => SceneGraph.FromJson(new JObject
    {
        ["document_id"] = document, ["kind"] = "assembly", ["root"] = new JObject
        {
            ["name"] = "Root", ["definition_kind"] = "assembly", ["children"] = new JArray(new JObject
            {
                ["occurrence_id"] = "sub", ["definition_kind"] = "assembly", ["children"] = nested ? new JArray(new JObject
                { ["occurrence_id"] = "leaf", ["definition_kind"] = "part", ["name"] = "Leaf" }) : new JArray()
            })
        }
    });
    [Fact]
    public void PickTargetsDirectChildOfCurrentContext()
    {
        var context = new BrowserContext(); context.SetGraph(Graph());
        Assert.Equal("sub", context.SelectionTarget("leaf").OccurrenceId);
        Assert.True(context.Enter(context.Current.Children[0]));
        Assert.Equal("leaf", context.SelectionTarget("leaf").OccurrenceId);
        Assert.Null(context.SelectionTarget("elsewhere"));
        Assert.True(context.Enter(context.Current.Children[0]));
        Assert.Equal(context.Current, context.SelectionTarget("leaf"));
        context.GoTo(0); Assert.Single(context.Path);
        context.Back(); Assert.Single(context.Path);
    }
    [Fact]
    public void RefreshKeepsValidPathButDocumentSwitchResetsIt()
    {
        var context = new BrowserContext(); context.SetGraph(Graph());
        context.Enter(context.Current.Children[0]); context.Enter(context.Current.Children[0]);
        context.SetGraph(Graph()); Assert.Equal(3, context.Path.Count);
        context.SetGraph(Graph(nested: false)); Assert.Equal(2, context.Path.Count);
        context.SetGraph(Graph("b")); Assert.Single(context.Path);
        context.SetGraph(null); Assert.Empty(context.Path);
    }
}
