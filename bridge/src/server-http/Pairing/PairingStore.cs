using System.Security.Cryptography;
using System.Text;

namespace Inventor.So.Mcp.Http.Pairing;

/// <summary>One open pairing window: who may pair, with which secrets, until when.</summary>
public sealed class PairingWindow
{
    internal PairingWindow(string clientName, string oneTimeToken, string code, DateTimeOffset expiresUtc)
    {
        ClientName = clientName;
        OneTimeToken = oneTimeToken;
        Code = code;
        ExpiresUtc = expiresUtc;
    }

    public string ClientName { get; }
    /// <summary>Secret carried by the QR code.</summary>
    public string OneTimeToken { get; }
    /// <summary>Six digits for manual entry on the headset.</summary>
    public string Code { get; }
    public DateTimeOffset ExpiresUtc { get; }
}

public sealed class PairingOutcome
{
    private PairingOutcome(string? clientName, string? errorCode, string message)
    {
        ClientName = clientName;
        ErrorCode = errorCode;
        Message = message;
    }

    public bool Ok => ErrorCode == null;
    public string? ClientName { get; }
    public string? ErrorCode { get; }
    public string Message { get; }

    internal static PairingOutcome Success(string clientName) => new(clientName, null, "paired");
    internal static PairingOutcome Failure(string code, string message) => new(null, code, message);
}

/// <summary>
/// The pairing window of the remote host (Inventor XR SO M1 design §3.1): one client name, a one-time
/// token for the QR code and a 6-digit code for manual entry. Either secret pairs once, only before
/// expiry; five wrong secrets close the window so the short code cannot be brute-forced.
/// </summary>
public sealed class PairingStore
{
    public const string Expired = "PAIRING_EXPIRED";
    public const string Used = "PAIRING_USED";
    public const string Invalid = "PAIRING_INVALID";
    public const int MaxFailures = 5;
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(2);

    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private PairingWindow? _window;
    private bool _used;
    private int _failures;

    public PairingStore(Func<DateTimeOffset> clock) => _clock = clock;

    /// <summary>Open (or replace) the window; the previous secrets stop working.</summary>
    public PairingWindow Open(string clientName, TimeSpan ttl)
    {
        if (!TokenRegistry.IsValidName(clientName))
            throw new InvalidOperationException("Pairing: the client name must be 1-40 letters, digits, '.', '_' or '-'.");
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
        var window = new PairingWindow(clientName, TokenRegistry.Generate(),
            RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"), _clock() + ttl);
        lock (_gate)
        {
            _window = window;
            _used = false;
            _failures = 0;
        }
        return window;
    }

    public PairingOutcome Redeem(string? secret)
    {
        lock (_gate)
        {
            if (_window == null) return PairingOutcome.Failure(Expired, "No pairing is open on this PC.");
            if (_used) return PairingOutcome.Failure(Used, "This pairing code was already used. Start a new pairing on the PC.");
            if (_clock() >= _window.ExpiresUtc || _failures >= MaxFailures)
                return PairingOutcome.Failure(Expired, "The pairing code expired. Start a new pairing on the PC.");
            if (!Matches(secret, _window))
            {
                _failures++;
                return _failures >= MaxFailures
                    ? PairingOutcome.Failure(Expired, "Too many wrong codes: pairing closed. Start a new pairing on the PC.")
                    : PairingOutcome.Failure(Invalid, "Wrong pairing code.");
            }
            _used = true;
            return PairingOutcome.Success(_window.ClientName);
        }
    }

    private static bool Matches(string? secret, PairingWindow window)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length > 256) return false;
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        // Both compared every time, in constant time over fixed-length hashes.
        bool token = CryptographicOperations.FixedTimeEquals(presented, SHA256.HashData(Encoding.UTF8.GetBytes(window.OneTimeToken)));
        bool code = CryptographicOperations.FixedTimeEquals(presented, SHA256.HashData(Encoding.UTF8.GetBytes(window.Code)));
        return token | code;
    }
}
