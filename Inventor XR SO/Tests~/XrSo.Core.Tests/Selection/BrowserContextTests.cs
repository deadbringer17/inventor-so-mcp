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

    [Fact]
    public void DeepTreeTargetsTheTopLevelChildAndHonoursSuppressionAndFirstMatch()
    {
        // Root -> [top0 (suppressed, holds "dup"), top1 -> chain of 60 sub-assemblies ending in "deep", top2 (holds "dup")]
        JObject Chain(int depth)
        {
            var node = new JObject { ["occurrence_id"] = "deep", ["definition_kind"] = "part" };
            for (int i = depth - 1; i >= 0; i--)
                node = new JObject { ["occurrence_id"] = "n" + i, ["definition_kind"] = "assembly", ["children"] = new JArray(node) };
            return node;
        }
        var top1 = Chain(60); top1["occurrence_id"] = "top1";
        var graph = SceneGraph.FromJson(new JObject
        {
            ["document_id"] = "a", ["kind"] = "assembly", ["root"] = new JObject
            {
                ["name"] = "Root", ["definition_kind"] = "assembly", ["children"] = new JArray(
                    new JObject { ["occurrence_id"] = "top0", ["suppressed"] = true, ["definition_kind"] = "assembly",
                        ["children"] = new JArray(new JObject { ["occurrence_id"] = "dup", ["definition_kind"] = "part" }) },
                    top1,
                    new JObject { ["occurrence_id"] = "top2", ["definition_kind"] = "assembly",
                        ["children"] = new JArray(new JObject { ["occurrence_id"] = "dup", ["definition_kind"] = "part" }) })
            }
        });
        var context = new BrowserContext(); context.SetGraph(graph);
        Assert.Equal("top1", context.SelectionTarget("deep").OccurrenceId);
        Assert.Equal("top1", context.SelectionTarget("n59").OccurrenceId);
        Assert.Equal("top2", context.SelectionTarget("dup").OccurrenceId);   // top0 is suppressed
        Assert.Null(context.SelectionTarget("top0"));
        Assert.Null(context.SelectionTarget("missing"));
        Assert.Equal("top2", context.SelectionTarget("top2").OccurrenceId);

        // Entering a sub-assembly changes the context: the cached map must follow it.
        Assert.True(context.Enter(context.Current.Children[1]));
        Assert.Equal("n1", context.SelectionTarget("deep").OccurrenceId);
        Assert.Null(context.SelectionTarget("dup"));
        // A new graph drops the cached map.
        context.SetGraph(graph);
        Assert.Equal("n1", context.SelectionTarget("deep").OccurrenceId);
    }
}
