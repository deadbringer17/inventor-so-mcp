using Bimwright.Ipt.Server;

namespace Inventor.So.Mcp.Http;

// Inventor.So.Mcp.Http - remote MCP host for Inventor SO (docs/INVENTOR_SO_MCP_IMPLEMENTATION_PLAN.md §21).
//   --generate-token <name>   print a new "name:token" line for the token file and exit
//   otherwise                 same configuration as the stdio server, plus the --http-* options
// An explicit class, not top-level statements: the stdio server already owns the global Program.
public static class HttpProgram
{
    public static async Task<int> Main(string[] args)
    {
        var generate = Array.IndexOf(args, "--generate-token");
        if (generate >= 0)
        {
            var name = generate + 1 < args.Length ? args[generate + 1] : "client";
            var registry = new TokenRegistry();
            var token = TokenRegistry.Generate();
            registry.Add(name, token);   // validates the name
            Console.WriteLine(name + ":" + token);
            return 0;
        }

        var config = InventorMcpConfig.Load(args);
        TokenRegistry tokens;
        WebApplication app;
        try
        {
            tokens = TokenRegistry.Load(config);
            app = HttpHost.Build(args, config, tokens);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Security.Cryptography.CryptographicException)
        {
            Console.Error.WriteLine("inventor-so-mcp-http: " + ex.Message);
            return 2;
        }
        Console.Error.WriteLine("inventor-so-mcp-http: listening on " + string.Join(", ", config.HttpUrls) +
            " for " + tokens.Count + " client token(s)" + (config.HttpAllowInsecureLan ? " (INSECURE LAN MODE)" : "") + ".");
        await app.RunAsync();
        return 0;
    }
}
