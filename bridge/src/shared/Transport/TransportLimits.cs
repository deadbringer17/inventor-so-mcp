namespace Bimwright.Ipt.Shared.Transport;

/// <summary>Limits shared by the TCP and Named-Pipe NDJSON listeners.</summary>
internal static class TransportLimits
{
    /// <summary>Longest request line, in UTF-8 bytes.</summary>
    public const int MaxLineBytes = 1024 * 1024; // 1 MiB

    /// <summary>
    /// Longest a connected client may stay silent (TCP: socket ReceiveTimeout; Pipe: time to deliver one full line)
    /// before the connection is dropped, so a silent client cannot hold the listener forever.
    /// </summary>
    public const int IdleReadTimeoutMs = 120000;
}
