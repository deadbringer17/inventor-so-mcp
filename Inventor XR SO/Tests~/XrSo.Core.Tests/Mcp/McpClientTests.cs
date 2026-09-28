using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Mcp;

public class McpClientTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _f;
    public McpClientTests(BackendFixture fixture) { _f = fixture; }

    private async Task<McpClient> Connected()
    {
        var client = new McpClient(_f.Transport(), _f.BaseUrl, _f.EditorToken);
        await client.InitializeAsync(CancellationToken.None);
        return client;
    }

    [Fact]
    public async Task InitializeOpensASession()
    {
        var client = await Connected();
        Assert.False(string.IsNullOrEmpty(client.SessionId));
    }

    [Fact]
    public async Task ToolCallReturnsThePayload()
    {
        var client = await Connected();
        var caps = await client.CallToolAsync("inventor_get_capabilities", new JObject(), CancellationToken.None);
        Assert.True((bool)caps["capabilities"]["xr_mesh"]);
        Assert.Equal(2027, (int)caps["target"]["inventor_year"]);
    }

    [Fact]
    public async Task ToolErrorsCarryTheirCode()
    {
        var client = await Connected();
        var ex = await Assert.ThrowsAsync<McpToolException>(() =>
            client.CallToolAsync("inventor_get_display_mesh", new JObject { ["document_id"] = "doc_asm" }, CancellationToken.None));
        Assert.Equal("WRONG_DOCUMENT_TYPE", ex.Code);
        Assert.Equal("inventor_get_display_mesh", ex.Tool);
    }

    [Fact]
    public async Task AnUnknownTokenIsUnauthorized()
    {
        var client = new McpClient(_f.Transport(), _f.BaseUrl, new string('x', 43));
        await Assert.ThrowsAsync<McpUnauthorizedException>(() => client.InitializeAsync(CancellationToken.None));
    }
}
