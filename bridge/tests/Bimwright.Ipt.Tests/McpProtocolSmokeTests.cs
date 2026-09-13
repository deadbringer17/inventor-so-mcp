using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class McpProtocolSmokeTests
{
    [Fact]
    public async Task Server_exposes_tools_list_over_stdio()
    {
        var responses = await RunProtocolHandshake();

        var initialize = Assert.Single(responses, r => (int?)r["id"] == 1);
        Assert.NotNull(initialize["result"]?["capabilities"]?["tools"]);
        Assert.NotNull(initialize["result"]?["capabilities"]?["resources"]);

        var toolsList = Assert.Single(responses, r => (int?)r["id"] == 2);
        var tools = Assert.IsAssignableFrom<JArray>(toolsList["result"]?["tools"]);
        Assert.NotEmpty(tools);
        Assert.Contains(tools, t => (string?)t["name"] == "inventor_list_available_targets");
        var resources = Assert.IsType<JArray>(responses[2]["result"]?["resources"]);
        foreach (var uri in new[] { "inventor://application", "inventor://active-document", "inventor://active-document/parameters", "inventor://active-document/mass", "inventor://selection", "inventor://events", "inventor://batch-commands" })
            Assert.Contains(resources, r => (string?)r["uri"] == uri);
    }

    /// <summary>
    /// A schema that omits a parameter the runtime validator then demands leaves the caller guessing
    /// after a rejected call, so every argument without a default is listed as required.
    /// </summary>
    [Theory]
    [InlineData("inventor_atomic_batch", "document_id", "expected_revision", "operations")]
    [InlineData("inventor_new_document_safe", "name", "kind")]
    [InlineData("inventor_save_document_safe", "document_id", "expected_revision")]
    [InlineData("inventor_create_drawing_safe", "document_id", "expected_revision", "scale")]
    [InlineData("inventor_save_artifact", "document_id", "expected_revision", "format")]
    public async Task Tool_schemas_mark_every_argument_without_a_default_required(string tool, params string[] expected)
    {
        var responses = await RunProtocolHandshake();
        var tools = Assert.IsAssignableFrom<JArray>(responses[1]["result"]?["tools"]);
        var schema = Assert.Single(tools, t => (string?)t["name"] == tool)["inputSchema"]!;
        var required = Assert.IsType<JArray>(schema["required"]).Select(r => (string?)r).ToArray();
        foreach (var name in expected) Assert.Contains(name, required);
        // Anything with a default stays optional so the caller is not forced to restate a default.
        foreach (var property in ((JObject)schema["properties"]!).Properties())
            if (property.Value["default"] != null) Assert.DoesNotContain(property.Name, required);
    }

    private static async Task<JObject[]> RunProtocolHandshake()
    {
        var serverAssembly = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(serverAssembly)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(serverAssembly);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ipt-mcp server.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke-test","version":"0.0"}}}""");
        var initialize = await ReadJsonResponse(process, "initialize");

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
        var toolsList = await ReadJsonResponse(process, "tools/list");
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":3,"method":"resources/list","params":{}}""");
        var resourcesList = await ReadJsonResponse(process, "resources/list");
        process.StandardInput.Close();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("ipt-mcp server did not exit after stdin closed.");
        }

        var stderr = await stderrTask;
        Assert.True(process.ExitCode == 0, "ipt-mcp server exited with code " + process.ExitCode + ". Stderr: " + stderr);

        var responses = new[] { initialize, toolsList, resourcesList };

        Assert.DoesNotContain(responses, r => r["error"]?["code"]?.Value<int>() == -32601);
        Assert.DoesNotContain(responses, r => r["error"] is not null);
        return responses;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static async Task<JObject> ReadJsonResponse(Process process, string label)
    {
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException("No MCP response received for " + label + ".");
        }

        return JObject.Parse(line);
    }
}
