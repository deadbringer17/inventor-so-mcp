using System;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Mcp
{
    public class McpException : Exception
    {
        public McpException(string code, string message) : base(message) { Code = code; }
        public string Code { get; }
    }

    /// <summary>A tool answered with an Inventor SO error (STALE_REVISION, MESH_TOO_LARGE, …).</summary>
    public sealed class McpToolException : McpException
    {
        public McpToolException(string tool, string code, string message, JObject details) : base(code, message)
        {
            Tool = tool;
            Details = details;
        }

        public string Tool { get; }
        public JObject Details { get; }
    }

    /// <summary>The PC rejected the token: the headset must pair again.</summary>
    public sealed class McpUnauthorizedException : McpException
    {
        public McpUnauthorizedException() : base("UNAUTHORIZED", "The PC no longer accepts this headset. Pair again.") { }
    }

    /// <summary>The server forgot the MCP session (restart): initialize again.</summary>
    public sealed class McpSessionExpiredException : McpException
    {
        public McpSessionExpiredException() : base("SESSION_EXPIRED", "The MCP session expired.") { }
    }
}
