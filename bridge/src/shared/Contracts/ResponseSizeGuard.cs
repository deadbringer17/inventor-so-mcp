using System;

namespace Bimwright.Ipt.Shared.Contracts;

public static class ResponseSizeGuard
{
    /// <summary>
    /// Room kept for the envelope around <c>result.Data</c> (id, meta, error, JSON framing). The client
    /// limit applies to the whole serialized line, so a payload accepted at exactly the limit would
    /// still be refused on arrival.
    /// </summary>
    public const int EnvelopeMarginBytes = 64 * 1024;

    /// <summary>
    /// The largest <c>Data</c> payload that still fits a client limit of <paramref name="clientLimitBytes"/>
    /// once the envelope is added. A limit too small to spare the margin keeps half of itself.
    /// </summary>
    public static int DataLimit(int clientLimitBytes)
        => clientLimitBytes <= 0 ? clientLimitBytes : Math.Max(clientLimitBytes - EnvelopeMarginBytes, clientLimitBytes / 2);

    public static bool Check(string serialized, int maxBytes, out InventorError? error)
    {
        var size = System.Text.Encoding.UTF8.GetByteCount(serialized);
        if (size <= maxBytes) { error = null; return true; }
        error = new InventorError
        {
            Code = "RESPONSE_TOO_LARGE",
            Message = $"Response {size} bytes exceeds the configured limit of {maxBytes} bytes. " +
                      "Narrow the query (max_items/max_depth) and retry."
        };
        return false;
    }
}
