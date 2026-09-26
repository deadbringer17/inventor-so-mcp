namespace Bimwright.Ipt.Server;

/// <summary>
/// Who is calling, as far as the host can authenticate it. The stdio host has exactly one local
/// client; the HTTP host answers with the name of the bearer token the request presented. This is
/// the identity used for asset ownership and the audit log - never the client's self-declared
/// <c>clientInfo</c>, which is recorded separately as <c>declared_client</c>.
/// </summary>
public interface ICallerIdentity
{
    string Client { get; }
    string? SessionId { get; }
}

public sealed class StdioCallerIdentity : ICallerIdentity
{
    public string Client => "stdio";
    public string? SessionId => null;
}
