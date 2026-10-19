using System;
using System.Collections.Generic;
using System.Globalization;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>Internal authenticated transport staging; never accesses Inventor or creates files.</summary>
public sealed class StageSketchImportHandler : IInventorCommand
{
    public const int MaxChunkBytes = 192 * 1024;
    public const int MaxBase64Characters = 262144;
    public string Name => "stage_sketch_import";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (context.ReadOnly) return Fail(InventorErrorCodes.READ_ONLY, "Staging writes are disabled.", "read_only");
        if (!context.AllowExperimental) return Fail(InventorErrorCodes.EXPERIMENTAL_DISABLED, "Experimental staging is disabled.", "experimental_disabled");
        if (context.IsDeadlineExceeded?.Invoke() == true) return Timeout();
        if (context.SketchImports == null) return Invalid();
        byte[]? decoded = null;
        try
        {
            string action = Text(parameters, "action");
            string owner = Text(parameters, "owner");
            string session = Text(parameters, "session");
            string[] fields;
            switch (action)
            {
                case "begin": fields = new[] { "byte_length" }; break;
                case "chunk": fields = new[] { "transfer_id", "offset", "data_base64" }; break;
                case "seal": fields = new[] { "transfer_id", "sha256" }; break;
                case "discard": fields = new[] { "transfer_id" }; break;
                default: return Invalid();
            }
            var allowed = new HashSet<string>(fields, StringComparer.Ordinal) { "action", "owner", "session" };
            foreach (var property in parameters.Properties()) if (!allowed.Contains(property.Name)) return Invalid();
            foreach (string field in fields) if (parameters[field] == null) return Invalid();
            JObject data;
            if (action == "begin")
            {
                int length = Integer(parameters, "byte_length");
                if (context.IsDeadlineExceeded?.Invoke() == true) return Timeout();
                data = new JObject { ["transfer_id"] = context.SketchImports.Begin(owner, session, length) };
            }
            else
            {
                string id = Text(parameters, "transfer_id");
                switch (action)
                {
                    case "chunk":
                        int offset = Integer(parameters, "offset");
                        string encoded = Text(parameters, "data_base64");
                        if (encoded.Length > MaxBase64Characters) return Invalid();
                        decoded = Convert.FromBase64String(encoded);
                        if (decoded.Length < 1 || decoded.Length > MaxChunkBytes) return Invalid();
                        if (context.IsDeadlineExceeded?.Invoke() == true) return Timeout();
                        context.SketchImports.Append(owner, session, id, offset, decoded);
                        data = new JObject { ["transfer_id"] = id, ["received_bytes"] = checked(offset + decoded.Length) };
                        break;
                    case "seal":
                        string digest = Text(parameters, "sha256");
                        if (context.IsDeadlineExceeded?.Invoke() == true) return Timeout();
                        context.SketchImports.Seal(owner, session, id, digest);
                        data = new JObject { ["sealed"] = true, ["transfer_id"] = id };
                        break;
                    default:
                        if (context.IsDeadlineExceeded?.Invoke() == true) return Timeout();
                        data = new JObject { ["discarded"] = context.SketchImports.Discard(owner, session, id) };
                        break;
                }
            }
            return InventorCommandResult.Success(Guid.Empty, data, new InventorResponseMeta());
        }
        catch (KeyNotFoundException) { return Fail(InventorErrorCodes.ASSET_NOT_FOUND, "Transfer is unavailable.", "transfer_unavailable"); }
        catch (ArgumentException) { return Invalid(); }
        catch (FormatException) { return Invalid(); }
        catch (OverflowException) { return Invalid(); }
        catch (InvalidOperationException) { return Invalid(); }
        finally { if (decoded != null) Array.Clear(decoded, 0, decoded.Length); }
    }

    private static string Text(JObject value, string name)
    {
        JToken? token = value[name];
        if (token?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)token)) throw new ArgumentException();
        return (string)token!;
    }
    private static int Integer(JObject value, string name)
    {
        JToken? token = value[name];
        if (token?.Type != JTokenType.Integer) throw new ArgumentException();
        if (!int.TryParse(token.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) throw new ArgumentException();
        return number;
    }
    private static InventorCommandResult Invalid() => Fail(InventorErrorCodes.INVALID_ARGUMENT, "Staging request is invalid or exceeds its limits.", "invalid_staging_request");
    private static InventorCommandResult Timeout() => Fail(InventorErrorCodes.TIMEOUT, "Staging deadline was exceeded.", "deadline_exceeded");
    private static InventorCommandResult Fail(string code, string message, string reason)
        => InventorCommandResult.Fail(Guid.Empty, code, message, new JObject { ["reason"] = reason }, new InventorResponseMeta());
}



