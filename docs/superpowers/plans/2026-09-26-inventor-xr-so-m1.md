# Inventor XR SO — Milestone 1 Implementation Plan

**Execution update (2026-09-27):** implementation resumed after committed C2;
C3–C9 source, scene generation and build tooling implemented. See
[verification record](../../xr-m1-verification.md) for final test/build evidence
and the completed physical Quest 3 / Inventor 2027 acceptance checklist. The original
step checkboxes below are historical instructions, not current test results.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** First real loop Quest 3 ↔ Inventor SO MCP ↔ Inventor 2027: pair the headset with the PC, load the active document at 1:1, select an occurrence or a face, highlight it on the headset and in Inventor, follow active-document changes, survive network loss.

**Architecture:** Three layers. (1) Backend (`bridge/`): the existing HTTP host gains a self-signed certificate and a `/pair` endpoint; everything else uses existing experimental XR tools. (2) Core (`Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/`): engine-independent C# (netstandard2.1, C# 9) with MCP client, pairing, GLB reader, face map, session state machine, selection logic — compiled by Unity **and** by a plain .NET test project that runs it end to end against the real HTTP host and `FakeAddIn`. (3) Unity app (`Inventor XR SO/Assets/XrSo/`): UnityWebRequest transport with certificate pinning, mesh building, highlight, controller ray, Home/pairing UI, QR scan, Meta XR rig.

**Tech Stack:** .NET 8 / ASP.NET Core (host), xUnit, QRCoder 1.6.0, Newtonsoft.Json 13, Unity 6000.6.3f1 + URP + Meta XR Core SDK + Oculus XR Plugin, Android IL2CPP ARM64, ZXing.Net 0.16.9.

**Spec:** [`docs/superpowers/specs/2026-09-26-inventor-xr-so-m1-design.md`](../specs/2026-09-26-inventor-xr-so-m1-design.md) (product spec: `Inventor XR SO/inventor_meta_product.md`).

**Deviation from the spec (to be written into it in Task B5):** glTFast is dropped. Our GLBs come only from `GlbBuilder` in this repo (positions, normals, uint32 indices, one triangle primitive per body, `extras.faces`). Core parses them directly (`GlbModel`), tested against `GlbBuilder` output in the same repo. This removes the spec §6 risk (glTFast reordering indices) instead of spiking it, and drops a dependency.

## Global Constraints

- Core: `netstandard2.1`, `LangVersion 9.0`, nullable **disabled**, no `UnityEngine` reference, only dependency Newtonsoft.Json. No `System.Text.Json`, no `ConfigureAwait(false)` (continuations must return to Unity's main thread).
- Core namespace root `InventorXrSo.Core`; Unity namespace root `InventorXrSo.Unity` (no Meta dependency) and `InventorXrSo.Xr` (Meta dependency).
- Unity editor: `C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe`. Android: ARM64, IL2CPP, min API 32, Vulkan, Linear color space, application id `com.occhipinti.inventorxrso`.
- Unity batchmode fails if the project is open in the Editor: close the Editor before running batch commands.
- Pairing: one-time token + 6-digit code, TTL **2 minutes**, single use, window closes after **5** wrong secrets. Error codes `PAIRING_EXPIRED`, `PAIRING_USED`, `PAIRING_INVALID`.
- TLS: the client trusts only the certificate whose SHA-256 (of the DER bytes, lowercase hex) it pinned. Hostname is not checked (the pin is the identity).
- Never log or display a bearer token or one-time token (the 6-digit code is shown on purpose, on the PC only).
- Units: GLB and `matrix_gltf` are metres, Y-up, right-handed. Unity conversion = negate X of positions/normals, reverse triangle winding, `S·M·S` for matrices with `S = diag(-1,1,1,1)`. Triangle order is preserved, so face index ranges stay valid.
- UI language Italian; every user-visible string lives in `UiText`.
- Backend code follows `bridge/CLAUDE.md` (error codes never inside messages, Newtonsoft on the wire, tests in `bridge/tests/Bimwright.Ipt.Tests`).
- Commands: backend tests `dotnet test bridge/tests/Bimwright.Ipt.Tests`; core tests `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Work on branch `feat/xr-so-m1` (create from `feat/xr-so-m1-design`).

## File map

```text
bridge/src/server/InventorMcpConfig.cs                 modify: self-signed, --pair, --pair-host
bridge/src/server-http/HttpHostSecurity.cs             modify: TokenRegistry thread-safe + IsValidName, TokenFile, BindingPolicy
bridge/src/server-http/HttpHost.cs                     modify: HostOptions, /pair
bridge/src/server-http/Program.cs                      modify: pairing startup
bridge/src/server-http/Inventor.So.Mcp.Http.csproj     modify: QRCoder
bridge/src/server-http/Pairing/PairingStore.cs         create
bridge/src/server-http/Pairing/SelfSignedCertificate.cs create
bridge/src/server-http/Pairing/PairingEndpoint.cs      create (+ HostOptions)
bridge/src/server-http/Pairing/PairingSetup.cs         create
bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs       create
bridge/tests/Bimwright.Ipt.Tests/FakeAddIn.cs          modify: event journal
Inventor XR SO/.gitignore, .gitattributes              create
Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/package.json
Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/InventorXrSo.Core.asmdef
  Runtime/Net/        TransportRequest.cs TransportResponse.cs IHttpTransport.cs TransportException.cs CertificatePin.cs ServerTrust.cs SseParser.cs
  Runtime/Pairing/    PairingPayload.cs PairedServer.cs PairingClient.cs PairingException.cs CredentialStore.cs
  Runtime/Mcp/        McpClient.cs McpExceptions.cs
  Runtime/Backend/    IInventorBackend.cs InventorBackend.cs Dto.cs AssetCache.cs ToolNames.cs
  Runtime/Glb/        GlbModel.cs FaceMap.cs Handedness.cs
  Runtime/Session/    SceneLoader.cs SceneDiff.cs Backoff.cs Delay.cs SessionController.cs
  Runtime/Selection/  Selection.cs SelectionService.cs
Inventor XR SO/Tests~/XrSo.Core/XrSo.Core.csproj              (netstandard2.1 compile check)
Inventor XR SO/Tests~/XrSo.Core.Tests/                        (xUnit, net8.0)
Inventor XR SO/Tests~/XrSo.TestHost/                          (console: FakeAddIn + HTTPS host + pairing)
Inventor XR SO/Assets/XrSo/Editor/Bootstrap/                  (package + player setup, no package deps)
Inventor XR SO/Assets/XrSo/Runtime/                           (InventorXrSo.Unity)
Inventor XR SO/Assets/XrSo/Xr/                                (InventorXrSo.Xr, Meta SDK)
Inventor XR SO/Assets/XrSo/Tests/EditMode/                    (Unity EditMode tests + fixtures)
Inventor XR SO/Assets/Plugins/Android/CredentialVault.java
```

Task order: A1 → A4 (backend), B1 → B9 (core), C1 → C9 (Unity). Every B task after B1 needs A4 for its end-to-end tests. C tasks need the Android module installed in Unity Hub.

---

## Part A — Backend pairing

### Task A1: PairingStore

**Files:**
- Create: `bridge/src/server-http/Pairing/PairingStore.cs`
- Modify: `bridge/src/server-http/HttpHostSecurity.cs` (add `TokenRegistry.IsValidName`)
- Test: `bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs`

**Interfaces:**
- Produces: `PairingStore(Func<DateTimeOffset> clock)`, `PairingWindow Open(string clientName, TimeSpan ttl)`, `PairingOutcome Redeem(string? secret)`; constants `PairingStore.Expired/Used/Invalid`, `MaxFailures = 5`, `DefaultTtl = 2 min`; `PairingWindow { ClientName, OneTimeToken, Code, ExpiresUtc }`; `PairingOutcome { Ok, ClientName, ErrorCode, Message }`; `TokenRegistry.IsValidName(string)`.

- [ ] **Step 1: Branch**

```bash
git checkout feat/xr-so-m1-design
git checkout -b feat/xr-so-m1
```

- [ ] **Step 2: Write the failing tests** — create `bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs`:

```csharp
using System;
using System.Linq;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;

namespace Bimwright.Ipt.Tests;

public sealed class PairingStoreTests
{
    private DateTimeOffset _now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private PairingStore Store() => new(() => _now);

    [Fact]
    public void OneTimeTokenPairsExactlyOnce()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        var first = store.Redeem(window.OneTimeToken);
        Assert.True(first.Ok);
        Assert.Equal("quest3", first.ClientName);
        Assert.Equal(PairingStore.Used, store.Redeem(window.OneTimeToken).ErrorCode);
    }

    [Fact]
    public void SixDigitCodePairsToo()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        Assert.Matches("^[0-9]{6}$", window.Code);
        Assert.True(store.Redeem(window.Code).Ok);
    }

    [Fact]
    public void ExpiredWindowIsRefused()
    {
        var store = Store();
        var window = store.Open("quest3", TimeSpan.FromMinutes(2));
        _now += TimeSpan.FromMinutes(2);
        Assert.Equal(PairingStore.Expired, store.Redeem(window.OneTimeToken).ErrorCode);
    }

    [Fact]
    public void FiveWrongSecretsCloseTheWindow()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        for (int i = 0; i < PairingStore.MaxFailures - 1; i++)
            Assert.Equal(PairingStore.Invalid, store.Redeem("not-a-code").ErrorCode);
        Assert.Equal(PairingStore.Expired, store.Redeem("not-a-code").ErrorCode);
        Assert.Equal(PairingStore.Expired, store.Redeem(window.OneTimeToken).ErrorCode);
    }

    [Fact]
    public void NoWindowMeansExpired() => Assert.Equal(PairingStore.Expired, Store().Redeem("anything").ErrorCode);

    [Fact]
    public void BadClientNameIsRefused() =>
        Assert.Throws<InvalidOperationException>(() => Store().Open("bad name", PairingStore.DefaultTtl));

    [Fact]
    public void ConcurrentRedeemsPairExactlyOnce()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        var outcomes = Enumerable.Range(0, 32).AsParallel().Select(_ => store.Redeem(window.OneTimeToken)).ToList();
        Assert.Single(outcomes, o => o.Ok);
    }

    [Fact]
    public void ReopeningInvalidatesThePreviousSecrets()
    {
        var store = Store();
        var old = store.Open("quest3", PairingStore.DefaultTtl);
        store.Open("quest3", PairingStore.DefaultTtl);
        Assert.Equal(PairingStore.Invalid, store.Redeem(old.OneTimeToken).ErrorCode);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~PairingStoreTests"`
Expected: build FAILS, `The type or namespace name 'Pairing' does not exist in the namespace 'Inventor.So.Mcp.Http'`.

- [ ] **Step 4: Add `IsValidName`** — in `bridge/src/server-http/HttpHostSecurity.cs`, inside `TokenRegistry`, after `public IEnumerable<string> Names ...`:

```csharp
    public static bool IsValidName(string name) => name != null && NamePattern.IsMatch(name);
```

- [ ] **Step 5: Implement** — create `bridge/src/server-http/Pairing/PairingStore.cs`:

```csharp
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
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~PairingStoreTests"`
Expected: 8 passed.

- [ ] **Step 7: Commit**

```bash
git add bridge/src/server-http/Pairing/PairingStore.cs bridge/src/server-http/HttpHostSecurity.cs bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs
git commit -m "feat(http): pairing window with one-time token and 6-digit code"
```

### Task A2: SelfSignedCertificate

**Files:**
- Create: `bridge/src/server-http/Pairing/SelfSignedCertificate.cs`
- Test: `bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs` (append class)

**Interfaces:**
- Produces: `SelfSignedCertificate.LoadOrCreate(string pfxPath, IEnumerable<string> hostNames, DateTimeOffset? now = null) : X509Certificate2`, `Sha256Hex(X509Certificate2) : string` (64 lowercase hex), `Display(string sha256Hex) : string` ("ABCD 0123 …"), `Validity`, `RenewBefore`.

- [ ] **Step 1: Write the failing tests** — append to `PairingTests.cs` (add `using System.IO; using System.Net; using System.Security.Cryptography.X509Certificates;` at the top):

```csharp
public sealed class SelfSignedCertificateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-cert-" + Guid.NewGuid().ToString("N"));
    private string Pfx => Path.Combine(_dir, "server.pfx");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void CreatesAServerCertificateWithKeyAndSubjectAltNames()
    {
        using var cert = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost", "127.0.0.1", "192.168.1.20" });
        Assert.True(cert.HasPrivateKey);
        Assert.True(File.Exists(Pfx));
        var san = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains(IPAddress.Parse("192.168.1.20"), san.EnumerateIPAddresses());
        Assert.Contains("localhost", san.EnumerateDnsNames());
        Assert.Matches("^[0-9a-f]{64}$", SelfSignedCertificate.Sha256Hex(cert));
    }

    [Fact]
    public void ReloadKeepsTheFingerprintEvenIfTheHostsChange()
    {
        string first, second;
        using (var a = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost" })) first = SelfSignedCertificate.Sha256Hex(a);
        using (var b = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost", "10.0.0.9" })) second = SelfSignedCertificate.Sha256Hex(b);
        Assert.Equal(first, second);
    }

    [Fact]
    public void AnExpiringCertificateIsReplaced()
    {
        string first;
        var longAgo = DateTimeOffset.UtcNow - SelfSignedCertificate.Validity + TimeSpan.FromDays(10);
        using (var old = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost" }, longAgo)) first = SelfSignedCertificate.Sha256Hex(old);
        using var renewed = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost" });
        Assert.NotEqual(first, SelfSignedCertificate.Sha256Hex(renewed));
    }

    [Fact]
    public void DisplayGroupsByFourUppercase() => Assert.Equal("ABCD 0123", SelfSignedCertificate.Display("abcd0123"));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~SelfSignedCertificateTests"`
Expected: build FAILS, `The name 'SelfSignedCertificate' does not exist`.

- [ ] **Step 3: Implement** — create `bridge/src/server-http/Pairing/SelfSignedCertificate.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Inventor.So.Mcp.Http.Pairing;

/// <summary>
/// The host's own TLS certificate when no PFX is configured: ECDSA P-256, created once and kept,
/// because paired clients pin its SHA-256. Only a certificate about to expire is replaced (which
/// means pairing again). Host names go into the SAN for completeness; clients trust the pin, not names.
/// </summary>
public static class SelfSignedCertificate
{
    public static readonly TimeSpan Validity = TimeSpan.FromDays(5 * 365);
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    public static X509Certificate2 LoadOrCreate(string pfxPath, IEnumerable<string> hostNames, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        if (File.Exists(pfxPath))
        {
            var existing = Load(File.ReadAllBytes(pfxPath));
            if (existing.NotAfter.ToUniversalTime() > (clock + RenewBefore).UtcDateTime) return existing;
            existing.Dispose();
        }
        var pfx = Create(hostNames, clock);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfxPath))!);
        File.WriteAllBytes(pfxPath, pfx);
        return Load(pfx);
    }

    public static string Sha256Hex(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    /// <summary>"ABCD 0123 …": the form both the PC console and the headset show for a visual check.</summary>
    public static string Display(string sha256Hex) =>
        string.Join(" ", Enumerable.Range(0, sha256Hex.Length / 4).Select(i => sha256Hex.Substring(i * 4, 4).ToUpperInvariant()));

    private static byte[] Create(IEnumerable<string> hostNames, DateTimeOffset now)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Inventor SO MCP", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        foreach (var host in hostNames.Where(h => !string.IsNullOrWhiteSpace(h) && h != "localhost").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IPAddress.TryParse(host, out var ip)) san.AddIpAddress(ip);
            else san.AddDnsName(host);
        }
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now + Validity);
        return certificate.Export(X509ContentType.Pkcs12);
    }

    // UserKeySet, not EphemeralKeySet: Windows TLS (SChannel) cannot serve with an ephemeral key.
    private static X509Certificate2 Load(byte[] pfx) => new(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~SelfSignedCertificateTests"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add bridge/src/server-http/Pairing/SelfSignedCertificate.cs bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs
git commit -m "feat(http): persistent self-signed certificate for pinned clients"
```


### Task A3: Runtime token registration

A token issued by `/pair` is added to the live `TokenRegistry` (while other requests authenticate) and appended to the token file so it survives a restart.

**Files:**
- Modify: `bridge/src/server-http/HttpHostSecurity.cs` (`TokenRegistry` locking; new `TokenFile`)
- Test: `bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs` (append class)

**Interfaces:**
- Produces: `TokenFile.Append(string path, string name, string token)`; `TokenRegistry.Add/Authenticate/Count/Names` safe under concurrency.

- [ ] **Step 1: Write the failing tests** — append to `PairingTests.cs` (add `using System.Threading.Tasks; using Bimwright.Ipt.Server;`):

```csharp
public sealed class TokenFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-tokens-" + Guid.NewGuid().ToString("N"));
    private string File1 => Path.Combine(_dir, "sub", "tokens.txt");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void AppendCreatesTheFileAndLoadReadsIt()
    {
        var token = TokenRegistry.Generate();
        TokenFile.Append(File1, "quest3", token);
        var registry = TokenRegistry.Load(new InventorMcpConfig { HttpTokenFile = File1 });
        Assert.Equal("quest3", registry.Authenticate(token));
    }

    [Fact]
    public void AppendAfterALineWithoutNewlineStartsANewLine()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File1)!);
        var first = TokenRegistry.Generate();
        File.WriteAllText(File1, "laptop:" + first);
        var second = TokenRegistry.Generate();
        TokenFile.Append(File1, "quest3", second);
        var registry = TokenRegistry.Load(new InventorMcpConfig { HttpTokenFile = File1 });
        Assert.Equal(new[] { "laptop", "quest3" }, registry.Names);
    }

    [Fact]
    public void AddWhileAuthenticatingIsSafe()
    {
        var registry = new TokenRegistry();
        var known = TokenRegistry.Generate();
        registry.Add("known", known);
        Parallel.For(0, 200, i =>
        {
            if (i % 2 == 0) registry.Add("c" + i, TokenRegistry.Generate());
            else Assert.Equal("known", registry.Authenticate(known));
        });
        Assert.Equal(101, registry.Count);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~TokenFileTests"`
Expected: build FAILS, `The name 'TokenFile' does not exist`.

- [ ] **Step 3: Implement** — in `HttpHostSecurity.cs`, replace the body of `TokenRegistry` fields/members so every access holds a lock:

```csharp
    private readonly object _gate = new();
    private readonly List<(string name, byte[] hash)> _tokens = new();

    public int Count { get { lock (_gate) return _tokens.Count; } }
    public IEnumerable<string> Names { get { lock (_gate) return _tokens.Select(t => t.name).ToArray(); } }
```

In `Add`, wrap the duplicate checks and `_tokens.Add` in `lock (_gate) { ... }` (validation of name/length may stay outside). In `Authenticate`, take a snapshot: `(string name, byte[] hash)[] snapshot; lock (_gate) snapshot = _tokens.ToArray();` and loop over `snapshot`.

Then add, below `TokenRegistry`:

```csharp
/// <summary>The token file of the remote host: "name:token" per line.</summary>
public static class TokenFile
{
    /// <summary>Append one line, creating the file and its directory if needed.</summary>
    public static void Append(string path, string name, string token)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var prefix = File.Exists(full) && new FileInfo(full).Length > 0 && !EndsWithNewline(full) ? Environment.NewLine : "";
        File.AppendAllText(full, prefix + name + ":" + token + Environment.NewLine);
    }

    private static bool EndsWithNewline(string path)
    {
        using var stream = File.OpenRead(path);
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == '\n';
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~TokenFileTests|FullyQualifiedName~TokenRegistryTests"`
Expected: all pass (3 new + 3 existing).

- [ ] **Step 5: Commit**

```bash
git add bridge/src/server-http/HttpHostSecurity.cs bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs
git commit -m "feat(http): thread-safe token registry and token file append"
```

### Task A4: `/pair` endpoint, self-signed HTTPS and startup pairing

**Files:**
- Modify: `bridge/src/server/InventorMcpConfig.cs`
- Modify: `bridge/src/server-http/HttpHostSecurity.cs` (`BindingPolicy.Check`)
- Modify: `bridge/src/server-http/HttpHost.cs`
- Modify: `bridge/src/server-http/Program.cs`
- Modify: `bridge/src/server-http/Inventor.So.Mcp.Http.csproj`
- Create: `bridge/src/server-http/Pairing/PairingEndpoint.cs`, `bridge/src/server-http/Pairing/PairingSetup.cs`
- Test: `bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs` (append classes), `bridge/tests/Bimwright.Ipt.Tests/HttpHostTests.cs` (one new BindingPolicy case)

**Interfaces:**
- Consumes: A1 `PairingStore`, A2 `SelfSignedCertificate`, A3 `TokenFile`.
- Produces:
  - config: `HttpSelfSignedCertificate` (`--http-self-signed`, env `INVENTOR_SO_HTTP_SELF_SIGNED`), `HttpSelfSignedPath`, `PairClientName` (`--pair <name>`), `PairHost` (`--pair-host <host>`).
  - `HostOptions { X509Certificate2? Certificate; PairingEndpoint? Pairing }`
  - `HttpHost.Build(string[] args, InventorMcpConfig config, TokenRegistry tokens, HostOptions? options = null)`
  - `PairingEndpoint(PairingStore store, TokenRegistry tokens, string tokenFile)`, `event Action<string, string?>? Paired`
  - `POST /pair` body `{"secret": "...", "device_name": "..."}` → 200 `{"ok":true,"client_name":"...","token":"..."}`; 400/403 `{"ok":false,"error":{"code":"PAIRING_*","message":"..."}}`
  - `PairingSetup.ResolveCertificate(config) : X509Certificate2?`, `CertificateHosts(config)`, `LanAddresses()`, `HttpsPort(config) : int?`, `QrPayload(host, port, ott, sha) : string` (`{"v":1,"host","port","ott","cert_sha256"}`), `Announce(TextWriter, PairingWindow, host, port, sha, pngPath)`.

- [ ] **Step 1: Write the failing tests** — append to `PairingTests.cs` (add `using System.Net.Http; using System.Net.Http.Headers; using System.Text; using Microsoft.AspNetCore.Builder; using Microsoft.AspNetCore.Hosting.Server; using Microsoft.AspNetCore.Hosting.Server.Features; using Microsoft.Extensions.DependencyInjection; using Newtonsoft.Json.Linq;`):

```csharp
/// <summary>L2: the real host over HTTPS with its self-signed certificate and an open pairing window.</summary>
public sealed class PairingEndpointTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "so-pair-" + Guid.NewGuid().ToString("N"));
    private readonly TokenRegistry _tokens = new();
    private PairingStore _store = null!;
    private WebApplication _app = null!;
    private string _base = "";
    private string _sha = "";
    private string TokenFilePath => Path.Combine(_root, "tokens.txt");

    public async Task InitializeAsync()
    {
        var config = new InventorMcpConfig
        {
            HttpUrls = { "https://127.0.0.1:0" },
            HttpSelfSignedCertificate = true,
            HttpSelfSignedPath = Path.Combine(_root, "server.pfx"),
            HttpTokenFile = TokenFilePath,
            DescriptorDirectory = Path.Combine(_root, "targets"),
            AssetDirectory = Path.Combine(_root, "assets"),
            AuditDirectory = Path.Combine(_root, "audit"),
        };
        var certificate = PairingSetup.ResolveCertificate(config)!;
        _sha = SelfSignedCertificate.Sha256Hex(certificate);
        _store = new PairingStore(() => DateTimeOffset.UtcNow);
        _app = HttpHost.Build(Array.Empty<string>(), config, _tokens,
            new HostOptions { Certificate = certificate, Pairing = new PairingEndpoint(_store, _tokens, TokenFilePath) });
        await _app.StartAsync();
        _base = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        try { Directory.Delete(_root, true); } catch { }
    }

    private HttpClient Client(string? pin = null) => new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null && SelfSignedCertificate.Sha256Hex(cert) == (pin ?? _sha),
    }) { BaseAddress = new Uri(_base) };

    private static StringContent Json(object body) => new(JObject.FromObject(body).ToString(), Encoding.UTF8, "application/json");

    [Fact]
    public async Task QrTokenPairsOnceAndTheNewTokenOpensMcp()
    {
        var window = _store.Open("quest3", PairingStore.DefaultTtl);
        using var http = Client();
        var response = await http.PostAsync("/pair", Json(new { secret = window.OneTimeToken, device_name = "Quest 3" }));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("quest3", (string?)body["client_name"]);
        var token = (string)body["token"]!;
        Assert.Equal("quest3", _tokens.Authenticate(token));
        Assert.Contains("quest3:" + token, File.ReadAllText(TokenFilePath));

        using var init = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}", Encoding.UTF8, "application/json"),
        };
        init.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        init.Headers.Accept.ParseAdd("application/json");
        init.Headers.Accept.ParseAdd("text/event-stream");
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(init)).StatusCode);

        var again = await http.PostAsync("/pair", Json(new { secret = window.OneTimeToken }));
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Equal(PairingStore.Used, (string?)JObject.Parse(await again.Content.ReadAsStringAsync())["error"]!["code"]);
    }

    [Fact]
    public async Task SixDigitCodePairs()
    {
        var window = _store.Open("quest-code", PairingStore.DefaultTtl);
        using var http = Client();
        var response = await http.PostAsync("/pair", Json(new { secret = window.Code }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MalformedBodyIsRefused()
    {
        _store.Open("quest-bad", PairingStore.DefaultTtl);
        using var http = Client();
        var response = await http.PostAsync("/pair", new StringContent("not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PairingStore.Invalid, (string?)JObject.Parse(await response.Content.ReadAsStringAsync())["error"]!["code"]);
    }

    [Fact]
    public async Task AClientPinningAnotherCertificateCannotConnect()
    {
        using var http = Client(pin: new string('0', 64));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("/healthz"));
    }
}

public sealed class PairingSetupTests
{
    [Fact]
    public void QrPayloadCarriesEverythingTheHeadsetNeeds()
    {
        var json = JObject.Parse(PairingSetup.QrPayload("192.168.1.20", 8443, "ott-value", new string('a', 64)));
        Assert.Equal(1, (int)json["v"]!);
        Assert.Equal("192.168.1.20", (string?)json["host"]);
        Assert.Equal(8443, (int)json["port"]!);
        Assert.Equal("ott-value", (string?)json["ott"]);
        Assert.Equal(new string('a', 64), (string?)json["cert_sha256"]);
    }

    [Theory]
    [InlineData("https://0.0.0.0:8443", 8443)]
    [InlineData("https://*:9443", 9443)]
    [InlineData("https://[::]:7443/", 7443)]
    public void HttpsPortComesFromTheFirstHttpsUrl(string url, int port) =>
        Assert.Equal(port, PairingSetup.HttpsPort(new InventorMcpConfig { HttpUrls = { "http://127.0.0.1:8787", url } }));

    [Fact]
    public void NoHttpsUrlMeansNoPort() => Assert.Null(PairingSetup.HttpsPort(new InventorMcpConfig()));
}
```

And in `HttpHostTests.cs`, inside `BindingPolicyTests`, add:

```csharp
    [Fact]
    public void HttpsWithTheSelfSignedCertificateIsAllowed()
    {
        var config = Config("https://0.0.0.0:8443");
        config.HttpSelfSignedCertificate = true;
        Assert.Empty(BindingPolicy.Check(config));
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~PairingEndpointTests|FullyQualifiedName~PairingSetupTests|FullyQualifiedName~BindingPolicyTests"`
Expected: build FAILS (`HostOptions`, `PairingEndpoint`, `PairingSetup`, `HttpSelfSignedCertificate` missing).

- [ ] **Step 3: Config** — in `InventorMcpConfig.cs`, after `PublicBaseUrl`:

```csharp
    /// <summary>Serve HTTPS with the host's own self-signed certificate (created once, pinned by paired clients).</summary>
    public bool HttpSelfSignedCertificate { get; set; }
    public string HttpSelfSignedPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "inventor-so-mcp", "http", "server.pfx");
    /// <summary>Open a pairing window at startup for this client name (--pair).</summary>
    public string? PairClientName { get; set; }
    /// <summary>Host written into the pairing QR code; default: this PC's first LAN IPv4 address.</summary>
    public string? PairHost { get; set; }
```

In the environment layer, next to `INVENTOR_SO_HTTP_ALLOW_INSECURE_LAN`:

```csharp
        if (Bool("INVENTOR_SO_HTTP_SELF_SIGNED") is { } selfSigned) c.HttpSelfSignedCertificate = selfSigned;
```

In `ApplyCli`, next to `--http-cert`:

```csharp
                case "--http-self-signed":     c.HttpSelfSignedCertificate = true; break;
                case "--pair":                 c.PairClientName = Next(args, ref i); break;
                case "--pair-host":            c.PairHost = Next(args, ref i); break;
```

- [ ] **Step 4: Binding policy** — in `BindingPolicy.Check` replace the HTTPS line with:

```csharp
            if (scheme == "https" && string.IsNullOrWhiteSpace(config.HttpCertificatePath) && !config.HttpSelfSignedCertificate)
                problems.Add("'" + url + "' needs a certificate: a PFX (--http-cert / INVENTOR_SO_HTTP_CERT) or --http-self-signed.");
```

- [ ] **Step 5: QRCoder** — in `Inventor.So.Mcp.Http.csproj`, in the ItemGroup with `ModelContextProtocol.AspNetCore`:

```xml
    <PackageReference Include="QRCoder" Version="1.6.0" />
```

- [ ] **Step 6: PairingEndpoint** — create `bridge/src/server-http/Pairing/PairingEndpoint.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Pairing;

/// <summary>Optional parts of the remote host that the CLI and the test host decide on.</summary>
public sealed class HostOptions
{
    /// <summary>TLS certificate to serve; overrides loading --http-cert.</summary>
    public X509Certificate2? Certificate { get; init; }
    /// <summary>When set, POST /pair is mapped (anonymous, rate limited).</summary>
    public PairingEndpoint? Pairing { get; init; }
}

/// <summary>
/// POST /pair: trades the one-time secret of the open <see cref="PairingStore"/> window for a new
/// permanent bearer token, registered live and appended to the token file.
/// </summary>
public sealed class PairingEndpoint
{
    public const int MaxBodyChars = 4096;
    private readonly PairingStore _store;
    private readonly TokenRegistry _tokens;
    private readonly string _tokenFile;

    public PairingEndpoint(PairingStore store, TokenRegistry tokens, string tokenFile)
    {
        _store = store;
        _tokens = tokens;
        _tokenFile = tokenFile;
    }

    /// <summary>Raised after a successful pairing with the client name and the device name it sent.</summary>
    public event Action<string, string?>? Paired;

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        string body;
        using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8))
        {
            var buffer = new char[MaxBodyChars + 1];
            int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            if (read > MaxBodyChars) return Error(StatusCodes.Status400BadRequest, PairingStore.Invalid, "Request too large.");
            body = new string(buffer, 0, read);
        }
        JObject? request = null;
        try { request = JObject.Parse(body); }
        catch (JsonReaderException) { }
        var secret = (request?["secret"] as JValue)?.Value as string;
        if (string.IsNullOrEmpty(secret))
            return Error(StatusCodes.Status400BadRequest, PairingStore.Invalid, "The body must be {\"secret\": \"...\", \"device_name\": \"...\"}.");

        var outcome = _store.Redeem(secret);
        if (!outcome.Ok) return Error(StatusCodes.Status403Forbidden, outcome.ErrorCode!, outcome.Message);

        var token = TokenRegistry.Generate();
        _tokens.Add(outcome.ClientName!, token, "pairing");
        TokenFile.Append(_tokenFile, outcome.ClientName!, token);
        var device = (request!["device_name"] as JValue)?.Value as string;
        Paired?.Invoke(outcome.ClientName!, device == null ? null : device[..Math.Min(device.Length, 80)]);
        return Results.Json(new { ok = true, client_name = outcome.ClientName, token });
    }

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { ok = false, error = new { code, message } }, statusCode: status);
}
```

- [ ] **Step 7: PairingSetup** — create `bridge/src/server-http/Pairing/PairingSetup.cs`:

```csharp
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Bimwright.Ipt.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QRCoder;

namespace Inventor.So.Mcp.Http.Pairing;

/// <summary>Startup helpers shared by the CLI host and the Inventor XR SO test host.</summary>
public static class PairingSetup
{
    /// <summary>The certificate to serve: --http-cert as before, else the self-signed one, else none.</summary>
    public static X509Certificate2? ResolveCertificate(InventorMcpConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.HttpCertificatePath))
            return new X509Certificate2(config.HttpCertificatePath!, config.HttpCertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
        return config.HttpSelfSignedCertificate
            ? SelfSignedCertificate.LoadOrCreate(config.HttpSelfSignedPath, CertificateHosts(config))
            : null;
    }

    public static IReadOnlyList<string> CertificateHosts(InventorMcpConfig config)
    {
        var hosts = new List<string> { "localhost", "127.0.0.1", Environment.MachineName };
        hosts.AddRange(LanAddresses());
        if (!string.IsNullOrWhiteSpace(config.PairHost)) hosts.Add(config.PairHost!);
        return hosts;
    }

    /// <summary>IPv4 addresses of the interfaces that are up, excluding loopback and tunnels.</summary>
    public static IEnumerable<string> LanAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString());

    /// <summary>Port of the first https URL, or null.</summary>
    public static int? HttpsPort(InventorMcpConfig config)
    {
        foreach (var url in config.HttpUrls)
        {
            if (!BindingPolicy.TryParse(url, out var scheme, out _) || scheme != "https") continue;
            var authority = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];
            int slash = authority.IndexOf('/');
            if (slash >= 0) authority = authority[..slash];
            int colon = authority.LastIndexOf(':');
            if (colon > authority.LastIndexOf(']') && int.TryParse(authority[(colon + 1)..], out var port)) return port;
            return 443;
        }
        return null;
    }

    public static string QrPayload(string host, int port, string oneTimeToken, string certSha256) =>
        new JObject { ["v"] = 1, ["host"] = host, ["port"] = port, ["ott"] = oneTimeToken, ["cert_sha256"] = certSha256 }
            .ToString(Formatting.None);

    /// <summary>Print the QR code, the manual code and the fingerprint; save the QR as PNG.</summary>
    public static void Announce(TextWriter output, PairingWindow window, string host, int port, string certSha256, string pngPath)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(QrPayload(host, port, window.OneTimeToken, certSha256), QRCodeGenerator.ECCLevel.M);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pngPath))!);
        File.WriteAllBytes(pngPath, new PngByteQRCode(data).GetGraphic(8));
        output.WriteLine(new AsciiQRCode(data).GetGraphic(1));
        output.WriteLine("Pairing open for '" + window.ClientName + "' until " + window.ExpiresUtc.ToLocalTime().ToString("HH:mm:ss") + ".");
        output.WriteLine("  Scan the QR code (also saved to " + pngPath + ") or enter on the headset:");
        output.WriteLine("  PC: " + host + ":" + port + "   code: " + window.Code);
        output.WriteLine("  Certificate: " + SelfSignedCertificate.Display(certSha256));
    }
}
```

- [ ] **Step 8: HttpHost** — in `HttpHost.cs` add `using Inventor.So.Mcp.Http.Pairing;`, change the signature and the token check:

```csharp
    public static WebApplication Build(string[] args, InventorMcpConfig config, TokenRegistry tokens, HostOptions? options = null)
    {
        config.Transport = "http";
        var problems = BindingPolicy.Check(config);
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        if (tokens.Count == 0 && options?.Pairing == null)
            throw new InvalidOperationException("No client token configured. Create one with --generate-token <name> and pass the file with --http-token-file, or pair a headset with --pair <name>.");
```

Replace the certificate block inside `ConfigureKestrel` with:

```csharp
            var certificate = options?.Certificate;
            if (certificate == null && !string.IsNullOrWhiteSpace(config.HttpCertificatePath))
                certificate = new X509Certificate2(config.HttpCertificatePath!, config.HttpCertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            if (certificate != null) kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate);
```

After `app.MapGet("/healthz", ...)` add:

```csharp
        if (options?.Pairing is { } pairing)
            app.MapPost("/pair", (HttpContext http) => pairing.HandleAsync(http)).RequireRateLimiting(RatePolicy);
```

- [ ] **Step 9: Program** — replace the body of `HttpProgram.Main` after the `--generate-token` block with:

```csharp
        var config = InventorMcpConfig.Load(args);
        TokenRegistry tokens;
        WebApplication app;
        PairingWindow? window = null;
        PairingEndpoint? pairing = null;
        System.Security.Cryptography.X509Certificates.X509Certificate2? certificate;
        try
        {
            tokens = TokenRegistry.Load(config);
            certificate = PairingSetup.ResolveCertificate(config);
            if (config.PairClientName != null)
            {
                if (string.IsNullOrWhiteSpace(config.HttpTokenFile))
                    throw new InvalidOperationException("--pair needs --http-token-file: the new token is appended there.");
                if (certificate == null)
                    throw new InvalidOperationException("--pair needs HTTPS: use --http-self-signed (or --http-cert) with an https:// URL.");
                if (tokens.Names.Contains(config.PairClientName))
                    throw new InvalidOperationException("--pair: client '" + config.PairClientName + "' already has a token; choose another name.");
                var store = new PairingStore(() => DateTimeOffset.UtcNow);
                window = store.Open(config.PairClientName, PairingStore.DefaultTtl);
                pairing = new PairingEndpoint(store, tokens, config.HttpTokenFile!);
            }
            app = HttpHost.Build(args, config, tokens, new HostOptions { Certificate = certificate, Pairing = pairing });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Security.Cryptography.CryptographicException)
        {
            Console.Error.WriteLine("inventor-so-mcp-http: " + ex.Message);
            return 2;
        }
        Console.Error.WriteLine("inventor-so-mcp-http: listening on " + string.Join(", ", config.HttpUrls) +
            " for " + tokens.Count + " client token(s)" + (config.HttpAllowInsecureLan ? " (INSECURE LAN MODE)" : "") + ".");
        if (window != null && pairing != null)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var host = config.PairHost ?? PairingSetup.LanAddresses().FirstOrDefault() ?? "127.0.0.1";
            var sha = SelfSignedCertificate.Sha256Hex(certificate!);
            var png = Path.Combine(Path.GetDirectoryName(config.HttpSelfSignedPath)!, "pairing-qr.png");
            PairingSetup.Announce(Console.Error, window, host, PairingSetup.HttpsPort(config) ?? 443, sha, png);
            pairing.Paired += (client, device) =>
            {
                Console.Error.WriteLine("inventor-so-mcp-http: paired '" + client + "'" + (device == null ? "" : " (" + device + ")") + ".");
                try { File.Delete(png); } catch (IOException) { }
            };
        }
        await app.RunAsync();
        return 0;
```

Add `using Inventor.So.Mcp.Http.Pairing;` at the top of `Program.cs`.

- [ ] **Step 10: Run the new tests**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests --filter "FullyQualifiedName~Pairing|FullyQualifiedName~BindingPolicyTests|FullyQualifiedName~TokenFileTests"`
Expected: all pass. If `QrTokenPairsOnce…` fails with a TLS error ("credentials supplied to the package were not recognized"), the certificate was loaded with an ephemeral key: confirm `SelfSignedCertificate.Load` uses `UserKeySet`.

- [ ] **Step 11: Run the whole backend suite**

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests`
Expected: no new failures (on Windows the path-semantics tests pass too).

- [ ] **Step 12: Manual smoke** — build and start the host with a pairing window:

```powershell
dotnet build bridge/src/server-http -c Debug
dotnet bridge/src/server-http/bin/Debug/net8.0/Inventor.So.Mcp.Http.dll --http-urls https://0.0.0.0:8443 --http-self-signed --http-token-file "$env:LOCALAPPDATA\InventorSO\inventor-so-mcp\http\tokens.txt" --pair quest3 --enable-experimental
```

Expected on stderr: "listening on https://0.0.0.0:8443 for 0 client token(s)", an ASCII QR code, the PC address with port 8443, a 6-digit code, the certificate fingerprint in groups of four; `pairing-qr.png` exists next to `server.pfx`. Stop with Ctrl+C.

- [ ] **Step 13: Commit**

```bash
git add bridge/src/server/InventorMcpConfig.cs bridge/src/server-http bridge/tests/Bimwright.Ipt.Tests/PairingTests.cs bridge/tests/Bimwright.Ipt.Tests/HttpHostTests.cs
git commit -m "feat(http): /pair endpoint, self-signed HTTPS and QR pairing at startup"
```


---

## Part B — Core (engine-independent, tested with `dotnet test`)

All Core files live under `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/Runtime/` (abbreviated `Core/` below). Tests live under `Inventor XR SO/Tests~/XrSo.Core.Tests/` (abbreviated `CoreTests/`). Unity ignores folders ending in `~`.

### Task B1: Scaffold the client folder and the Core test harness

**Files:**
- Create: `Inventor XR SO/.gitignore`, `Inventor XR SO/.gitattributes`
- Create: `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/package.json`
- Create: `Core/InventorXrSo.Core.asmdef`
- Create: `Core/Net/CertificatePin.cs`
- Create: `Inventor XR SO/Tests~/XrSo.Core/XrSo.Core.csproj`
- Create: `CoreTests/XrSo.Core.Tests.csproj`, `CoreTests/Net/CertificatePinTests.cs`

**Interfaces:**
- Produces: `CertificatePin.Sha256Hex(byte[] der)`, `Normalize(string)`, `IsValid(string)`, `FixedTimeEquals(string, string)`, `Display(string)` (same grouping as the server's `SelfSignedCertificate.Display`).

- [ ] **Step 1: Ignore rules** — `Inventor XR SO/.gitignore`:

```gitignore
# Unity (root only: the .NET projects under Tests~ keep their csproj)
/[Ll]ibrary/
/[Tt]emp/
/[Oo]bj/
/[Bb]uild/
/[Bb]uilds/
/[Ll]ogs/
/[Uu]ser[Ss]ettings/
/[Mm]emoryCaptures/
/[Rr]ecordings/
/*.csproj
/*.sln
/.vs/
/.idea/
*.apk
*.aab
# .NET projects under Tests~
Tests~/**/bin/
Tests~/**/obj/
```

`Inventor XR SO/.gitattributes`:

```gitattributes
*.cs text eol=lf
*.asmdef text eol=lf
*.json text eol=lf
*.unity text eol=lf merge=unityyamlmerge
*.prefab text eol=lf merge=unityyamlmerge
*.asset text eol=lf merge=unityyamlmerge
*.meta text eol=lf
*.png filter=lfs diff=lfs merge=lfs -text
*.jpg filter=lfs diff=lfs merge=lfs -text
*.psd filter=lfs diff=lfs merge=lfs -text
*.fbx filter=lfs diff=lfs merge=lfs -text
*.wav filter=lfs diff=lfs merge=lfs -text
```

Run `git lfs version`. If it fails, stop and ask the user to install Git LFS (`git lfs install`) before continuing; no LFS-tracked file is committed in this milestone, so this does not block the other tasks.

- [ ] **Step 2: Core package** — `Inventor XR SO/Packages/com.occhipinti.inventorxrso.core/package.json`:

```json
{
  "name": "com.occhipinti.inventorxrso.core",
  "version": "0.1.0",
  "displayName": "Inventor XR SO Core",
  "description": "Engine-independent client core of Inventor XR SO: MCP client, pairing, GLB reader, session state, selection.",
  "unity": "6000.0",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

`Core/InventorXrSo.Core.asmdef`:

```json
{
  "name": "InventorXrSo.Core",
  "rootNamespace": "InventorXrSo.Core",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": true
}
```

- [ ] **Step 3: Compile-check project** — `Inventor XR SO/Tests~/XrSo.Core/XrSo.Core.csproj` (netstandard2.1 + C# 9 = what Unity accepts):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <AssemblyName>InventorXrSo.Core</AssemblyName>
    <RootNamespace>InventorXrSo.Core</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\..\Packages\com.occhipinti.inventorxrso.core\Runtime\**\*.cs" LinkBase="Runtime" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Test project** — `CoreTests/XrSo.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <NoWarn>$(NoWarn);CS8632</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
    <ProjectReference Include="..\XrSo.Core\XrSo.Core.csproj" />
    <ProjectReference Include="..\..\..\bridge\src\server\Bimwright.Ipt.Server.csproj" />
    <ProjectReference Include="..\..\..\bridge\src\server-http\Inventor.So.Mcp.Http.csproj" />
    <Compile Include="..\..\..\bridge\tests\Bimwright.Ipt.Tests\FakeAddIn.cs" Link="Bridge\FakeAddIn.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Write the failing test** — `CoreTests/Net/CertificatePinTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Tests.Net;

public class CertificatePinTests
{
    [Fact]
    public void HashIsLowercaseHexOfTheDerBytes()
    {
        var der = Encoding.ASCII.GetBytes("certificate");
        var expected = Convert.ToHexString(SHA256.HashData(der)).ToLowerInvariant();
        Assert.Equal(expected, CertificatePin.Sha256Hex(der));
    }

    [Theory]
    [InlineData("AB12 CD34")]
    [InlineData("ab:12:cd:34")]
    public void NormalizeStripsSeparatorsAndCase(string raw) =>
        Assert.Equal(new string('a', 56) + "ab12cd34", CertificatePin.Normalize(new string('A', 56) + raw));

    [Fact]
    public void InvalidFingerprintsAreRejected()
    {
        Assert.False(CertificatePin.IsValid("abc"));
        Assert.False(CertificatePin.IsValid(new string('g', 64)));
        Assert.False(CertificatePin.IsValid(null));
        Assert.True(CertificatePin.IsValid(new string('0', 64)));
        Assert.Throws<ArgumentException>(() => CertificatePin.Normalize("abc"));
    }

    [Fact]
    public void FixedTimeEqualsComparesContent()
    {
        Assert.True(CertificatePin.FixedTimeEquals(new string('a', 64), new string('a', 64)));
        Assert.False(CertificatePin.FixedTimeEquals(new string('a', 64), new string('b', 64)));
        Assert.False(CertificatePin.FixedTimeEquals(new string('a', 64), "a"));
    }

    [Fact]
    public void DisplayMatchesTheServerFormat() =>
        Assert.Equal("ABCD 0123", CertificatePin.Display("abcd0123"));
}
```

- [ ] **Step 6: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`
Expected: build FAILS, `The type or namespace name 'Net' does not exist in the namespace 'InventorXrSo.Core'`.

- [ ] **Step 7: Implement** — `Core/Net/CertificatePin.cs`:

```csharp
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace InventorXrSo.Core.Net
{
    /// <summary>SHA-256 fingerprints of DER certificates: how the headset recognises its PC.</summary>
    public static class CertificatePin
    {
        public static string Sha256Hex(byte[] der)
        {
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(der));
        }

        public static bool IsValid(string hex)
        {
            if (hex == null) return false;
            var clean = Strip(hex);
            return clean.Length == 64 && clean.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }

        /// <summary>Lowercase hex without separators; throws on anything that is not 64 hex digits.</summary>
        public static string Normalize(string hex)
        {
            if (!IsValid(hex)) throw new ArgumentException("Not a SHA-256 fingerprint.", nameof(hex));
            return Strip(hex);
        }

        public static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>"ABCD 0123 …", the grouping the PC console prints.</summary>
        public static string Display(string hex)
        {
            var builder = new StringBuilder();
            for (int i = 0; i + 4 <= hex.Length; i += 4)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(hex.Substring(i, 4).ToUpperInvariant());
            }
            return builder.ToString();
        }

        private static string Strip(string hex) => hex.Replace(":", "").Replace(" ", "").ToLowerInvariant();

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}
```

- [ ] **Step 8: Run to verify pass**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`
Expected: 6 passed. (`FakeAddIn.cs` compiles into the test assembly; if it needs `using` of types the server does not expose publicly, report it instead of changing `FakeAddIn` visibility.)

- [ ] **Step 9: Commit**

```bash
git add "Inventor XR SO/.gitignore" "Inventor XR SO/.gitattributes" "Inventor XR SO/Packages" "Inventor XR SO/Tests~"
git commit -m "feat(xr): scaffold Inventor XR SO core package and test harness"
```

### Task B2: Transport contract, trust and pairing client

**Files:**
- Create: `Core/Net/TransportRequest.cs`, `Core/Net/TransportResponse.cs`, `Core/Net/IHttpTransport.cs`, `Core/Net/TransportException.cs`, `Core/Net/ServerTrust.cs`
- Create: `Core/Pairing/PairingPayload.cs`, `Core/Pairing/PairedServer.cs`, `Core/Pairing/PairingException.cs`, `Core/Pairing/PairingClient.cs`
- Create: `CoreTests/Support/SystemHttpTransport.cs`, `CoreTests/Support/BackendFixture.cs`
- Test: `CoreTests/Pairing/PairingPayloadTests.cs`, `CoreTests/Pairing/PairingClientTests.cs`

**Interfaces:**
- Consumes: B1 `CertificatePin`; A4 `HttpHost.Build`, `HostOptions`, `PairingEndpoint`, `PairingSetup`, `PairingStore`.
- Produces:
  - `TransportRequest(string method, string url)` with `Headers` (case-insensitive), `byte[] Body`, `TimeSpan Timeout` (default 30 s; `System.Threading.Timeout.InfiniteTimeSpan` for streams); `static TransportRequest Json(string method, string url, string json)`.
  - `TransportResponse(int status, IDictionary<string,string> headers, byte[] body)` with `Status`, `Body`, `Text`, `Header(string)`, `IsSuccess`.
  - `IHttpTransport { Task<TransportResponse> SendAsync(TransportRequest, CancellationToken); Task<int> StreamLinesAsync(TransportRequest, Action<string> onLine, CancellationToken); }` — `StreamLinesAsync` returns the HTTP status once the server closes the stream; it throws `TransportException` on network failure, `CertificateRejectedException` on pin mismatch, `OperationCanceledException` on cancel.
  - `ServerTrust.Pinned(string sha)`, `ServerTrust.FirstUse()`, `bool Validate(byte[] der)`, `PinnedSha256`, `LastPresentedSha256`, `LastRejected`.
  - `TransportException(string message, Exception inner = null)`, `CertificateRejectedException(string presentedSha256)`.
  - `PairingPayload.Parse(string)` → `Host, Port, OneTimeToken, CertSha256`.
  - `PairedServer(host, port, certSha256, clientName, token)` with `BaseUrl`, `static MakeBaseUrl(host, port)`, `ToJson()`, `static FromJson(string)`.
  - `PairingClient(Func<ServerTrust, IHttpTransport> transportFactory)`: `PairWithQrAsync(PairingPayload, string deviceName, CancellationToken)`, `ProbeFingerprintAsync(string host, int port, CancellationToken) : Task<string>`, `PairAsync(string host, int port, string secret, ServerTrust trust, string deviceName, CancellationToken)`.
  - `PairingException(string code, string message)` with `Code`.
  - Test support: `SystemHttpTransport(ServerTrust)`, `BackendFixture` (`AddIn`, `Tokens`, `Pairing`, `BaseUrl`, `Base`, `CertSha256`, `EditorToken`, `EditorServer`, `Transport()`).

- [ ] **Step 1: Write the failing payload tests** — `CoreTests/Pairing/PairingPayloadTests.cs`:

```csharp
using InventorXrSo.Core.Pairing;

namespace InventorXrSo.Core.Tests.Pairing;

public class PairingPayloadTests
{
    private static readonly string Sha = new string('a', 64);

    [Fact]
    public void ParsesTheServerQrPayload()
    {
        var p = PairingPayload.Parse("{\"v\":1,\"host\":\"192.168.1.20\",\"port\":8443,\"ott\":\"secret\",\"cert_sha256\":\"" + Sha + "\"}");
        Assert.Equal("192.168.1.20", p.Host);
        Assert.Equal(8443, p.Port);
        Assert.Equal("secret", p.OneTimeToken);
        Assert.Equal(Sha, p.CertSha256);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("{\"v\":2,\"host\":\"h\",\"port\":1,\"ott\":\"s\",\"cert_sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\"}")]
    [InlineData("{\"v\":1,\"port\":1,\"ott\":\"s\",\"cert_sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\"}")]
    [InlineData("{\"v\":1,\"host\":\"h\",\"port\":70000,\"ott\":\"s\",\"cert_sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\"}")]
    [InlineData("{\"v\":1,\"host\":\"h\",\"port\":1,\"ott\":\"s\",\"cert_sha256\":\"abc\"}")]
    public void RejectsAnythingElse(string text) => Assert.Throws<FormatException>(() => PairingPayload.Parse(text));

    [Fact]
    public void PairedServerRoundTripsAndBracketsIpv6()
    {
        var server = new PairedServer("fe80::1", 8443, Sha, "quest3", "token-value-token-value-token-value");
        Assert.Equal("https://[fe80::1]:8443", server.BaseUrl);
        var back = PairedServer.FromJson(server.ToJson());
        Assert.Equal(server.Host, back.Host);
        Assert.Equal(server.Port, back.Port);
        Assert.Equal(server.CertSha256, back.CertSha256);
        Assert.Equal(server.ClientName, back.ClientName);
        Assert.Equal(server.Token, back.Token);
        Assert.Throws<FormatException>(() => PairedServer.FromJson("{}"));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~PairingPayloadTests"`
Expected: build FAILS, `The type or namespace name 'Pairing' does not exist`.

- [ ] **Step 3: Transport contract** — `Core/Net/TransportRequest.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace InventorXrSo.Core.Net
{
    public sealed class TransportRequest
    {
        public TransportRequest(string method, string url)
        {
            Method = method;
            Url = url;
        }

        public string Method { get; }
        public string Url { get; }
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body { get; set; }
        /// <summary>Whole-request timeout; <c>Timeout.InfiniteTimeSpan</c> for long-lived streams.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        public static TransportRequest Json(string method, string url, string json)
        {
            var request = new TransportRequest(method, url) { Body = Encoding.UTF8.GetBytes(json) };
            request.Headers["Content-Type"] = "application/json";
            return request;
        }
    }
}
```

`Core/Net/TransportResponse.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace InventorXrSo.Core.Net
{
    public sealed class TransportResponse
    {
        private readonly Dictionary<string, string> _headers;

        public TransportResponse(int status, IDictionary<string, string> headers, byte[] body)
        {
            Status = status;
            _headers = new Dictionary<string, string>(headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            Body = body ?? Array.Empty<byte>();
        }

        public int Status { get; }
        public byte[] Body { get; }
        public string Text => Encoding.UTF8.GetString(Body);
        public bool IsSuccess => Status >= 200 && Status < 300;
        public string Header(string name) => _headers.TryGetValue(name, out var value) ? value : null;
    }
}
```

`Core/Net/IHttpTransport.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace InventorXrSo.Core.Net
{
    /// <summary>
    /// HTTPS as the core needs it. Unity implements it with UnityWebRequest, tests with HttpClient.
    /// Both validate the server certificate with a <see cref="ServerTrust"/>.
    /// </summary>
    public interface IHttpTransport
    {
        Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct);

        /// <summary>
        /// Send the request and hand each line of the response body to <paramref name="onLine"/> as it
        /// arrives (without the trailing newline). Completes with the HTTP status when the server closes.
        /// </summary>
        Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct);
    }
}
```

`Core/Net/TransportException.cs`:

```csharp
using System;

namespace InventorXrSo.Core.Net
{
    /// <summary>The PC could not be reached (network, TLS, timeout).</summary>
    public class TransportException : Exception
    {
        public TransportException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>The server presented a certificate other than the pinned one.</summary>
    public sealed class CertificateRejectedException : TransportException
    {
        public CertificateRejectedException(string presentedSha256)
            : base("The PC presented a different certificate than the one paired.")
        {
            PresentedSha256 = presentedSha256;
        }

        public string PresentedSha256 { get; }
    }
}
```

`Core/Net/ServerTrust.cs`:

```csharp
namespace InventorXrSo.Core.Net
{
    /// <summary>
    /// Which server certificate a transport accepts: exactly the pinned one, or (only to read the
    /// fingerprint before manual pairing) any, remembering what was presented.
    /// </summary>
    public sealed class ServerTrust
    {
        private ServerTrust(string pinnedSha256) { PinnedSha256 = pinnedSha256; }

        public static ServerTrust Pinned(string sha256Hex) => new ServerTrust(CertificatePin.Normalize(sha256Hex));
        public static ServerTrust FirstUse() => new ServerTrust(null);

        public string PinnedSha256 { get; }
        public string LastPresentedSha256 { get; private set; }
        public bool LastRejected { get; private set; }

        public bool Validate(byte[] der)
        {
            var presented = der == null ? null : CertificatePin.Sha256Hex(der);
            LastPresentedSha256 = presented;
            bool ok = presented != null && (PinnedSha256 == null || CertificatePin.FixedTimeEquals(presented, PinnedSha256));
            LastRejected = !ok;
            return ok;
        }
    }
}
```

- [ ] **Step 4: Pairing types** — `Core/Pairing/PairingException.cs`:

```csharp
using System;

namespace InventorXrSo.Core.Pairing
{
    public sealed class PairingException : Exception
    {
        public PairingException(string code, string message) : base(message) { Code = code; }
        /// <summary>PAIRING_EXPIRED, PAIRING_USED, PAIRING_INVALID or PAIRING_FAILED.</summary>
        public string Code { get; }
    }
}
```

`Core/Pairing/PairingPayload.cs`:

```csharp
using System;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>What the PC's pairing QR code carries.</summary>
    public sealed class PairingPayload
    {
        public PairingPayload(string host, int port, string oneTimeToken, string certSha256)
        {
            Host = host;
            Port = port;
            OneTimeToken = oneTimeToken;
            CertSha256 = certSha256;
        }

        public string Host { get; }
        public int Port { get; }
        public string OneTimeToken { get; }
        public string CertSha256 { get; }

        public static PairingPayload Parse(string text)
        {
            JObject json;
            try { json = JObject.Parse(text ?? ""); }
            catch (JsonReaderException) { throw new FormatException("The QR code is not an Inventor SO pairing code."); }
            try
            {
                if ((int?)json["v"] != 1) throw new FormatException("Unsupported pairing code version.");
                var host = (string)json["host"];
                var port = (int?)json["port"] ?? 0;
                var ott = (string)json["ott"];
                var sha = (string)json["cert_sha256"];
                if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535 || string.IsNullOrEmpty(ott))
                    throw new FormatException("Incomplete pairing code.");
                if (!CertificatePin.IsValid(sha)) throw new FormatException("Invalid certificate fingerprint in the pairing code.");
                return new PairingPayload(host, port, ott, CertificatePin.Normalize(sha));
            }
            catch (ArgumentException ex) { throw new FormatException("Malformed pairing code.", ex); }
        }
    }
}
```

`Core/Pairing/PairedServer.cs`:

```csharp
using System;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>A PC this headset is paired with: where it is, which certificate, which token.</summary>
    public sealed class PairedServer
    {
        public PairedServer(string host, int port, string certSha256, string clientName, string token)
        {
            Host = host;
            Port = port;
            CertSha256 = certSha256;
            ClientName = clientName;
            Token = token;
        }

        public string Host { get; }
        public int Port { get; }
        public string CertSha256 { get; }
        public string ClientName { get; }
        public string Token { get; }
        public string BaseUrl => MakeBaseUrl(Host, Port);

        public static string MakeBaseUrl(string host, int port) =>
            "https://" + (host.Contains(":") && !host.StartsWith("[") ? "[" + host + "]" : host) + ":" + port;

        public string ToJson() => new JObject
        {
            ["host"] = Host, ["port"] = Port, ["cert_sha256"] = CertSha256, ["client_name"] = ClientName, ["token"] = Token,
        }.ToString(Formatting.None);

        public static PairedServer FromJson(string json)
        {
            try
            {
                var o = JObject.Parse(json);
                var host = (string)o["host"];
                var port = (int?)o["port"] ?? 0;
                var sha = (string)o["cert_sha256"];
                var client = (string)o["client_name"];
                var token = (string)o["token"];
                if (string.IsNullOrEmpty(host) || port < 1 || port > 65535 || !CertificatePin.IsValid(sha) ||
                    string.IsNullOrEmpty(client) || string.IsNullOrEmpty(token))
                    throw new FormatException("Incomplete stored pairing.");
                return new PairedServer(host, port, CertificatePin.Normalize(sha), client, token);
            }
            catch (JsonReaderException ex) { throw new FormatException("Unreadable stored pairing.", ex); }
        }
    }
}
```

- [ ] **Step 5: Run the payload tests**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~PairingPayloadTests"`
Expected: 8 passed.

- [ ] **Step 6: Test support** — `CoreTests/Support/SystemHttpTransport.cs`:

```csharp
using System.Net.Http;
using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Tests.Support;

/// <summary>HttpClient implementation of the core transport, for tests only (Unity uses UnityWebRequest).</summary>
public sealed class SystemHttpTransport : IHttpTransport, IDisposable
{
    private readonly ServerTrust _trust;
    private readonly HttpClient _http;

    public SystemHttpTransport(ServerTrust trust)
    {
        _trust = trust;
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null && trust.Validate(cert.RawData) };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (request.Timeout != Timeout.InfiniteTimeSpan) timeout.CancelAfter(request.Timeout);
        try
        {
            using var message = ToMessage(request);
            using var response = await _http.SendAsync(message, timeout.Token);
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            return new TransportResponse((int)response.StatusCode, Headers(response), body);
        }
        catch (HttpRequestException ex) { throw Wrap(ex); }
    }

    public async Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct)
    {
        try
        {
            using var message = ToMessage(request);
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string line;
            while ((line = await reader.ReadLineAsync(ct)) != null) onLine(line);
            return (int)response.StatusCode;
        }
        catch (HttpRequestException ex) { throw Wrap(ex); }
        catch (IOException ex) when (!ct.IsCancellationRequested) { throw new TransportException(ex.Message, ex); }
    }

    public void Dispose() => _http.Dispose();

    private Exception Wrap(HttpRequestException ex) =>
        _trust.LastRejected ? new CertificateRejectedException(_trust.LastPresentedSha256) : new TransportException(ex.Message, ex);

    private static HttpRequestMessage ToMessage(TransportRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        if (request.Body != null)
        {
            message.Content = new ByteArrayContent(request.Body);
            if (request.Headers.TryGetValue("Content-Type", out var type)) message.Content.Headers.TryAddWithoutValidation("Content-Type", type);
        }
        foreach (var header in request.Headers)
            if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return message;
    }

    private static Dictionary<string, string> Headers(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers) headers[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers) headers[h.Key] = string.Join(", ", h.Value);
        return headers;
    }
}
```

`CoreTests/Support/BackendFixture.cs`:

```csharp
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Tests;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace InventorXrSo.Core.Tests.Support;

/// <summary>The real HTTPS host (self-signed, pairing enabled) in front of FakeAddIn, per test class.</summary>
public sealed class BackendFixture : IAsyncLifetime
{
    private WebApplication _app;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "xrso-" + Guid.NewGuid().ToString("N"));
    public FakeAddIn AddIn { get; private set; }
    public TokenRegistry Tokens { get; } = new TokenRegistry();
    public PairingStore Pairing { get; private set; }
    public string BaseUrl { get; private set; }
    public Uri Base => new Uri(BaseUrl);
    public string CertSha256 { get; private set; }
    public string EditorToken { get; } = TokenRegistry.Generate();
    public PairedServer EditorServer => new PairedServer(Base.Host, Base.Port, CertSha256, "editor", EditorToken);

    public SystemHttpTransport Transport() => new SystemHttpTransport(ServerTrust.Pinned(CertSha256));

    public async Task InitializeAsync()
    {
        AddIn = new FakeAddIn(Path.Combine(Root, "targets"));
        Tokens.Add("editor", EditorToken);
        var config = new InventorMcpConfig
        {
            HttpUrls = { "https://127.0.0.1:0" },
            HttpSelfSignedCertificate = true,
            HttpSelfSignedPath = Path.Combine(Root, "server.pfx"),
            HttpTokenFile = Path.Combine(Root, "tokens.txt"),
            DescriptorDirectory = AddIn.DescriptorDirectory,
            AssetDirectory = Path.Combine(Root, "assets"),
            AuditDirectory = Path.Combine(Root, "audit"),
            EnableExperimental = true,
        };
        var certificate = PairingSetup.ResolveCertificate(config);
        CertSha256 = SelfSignedCertificate.Sha256Hex(certificate);
        Pairing = new PairingStore(() => DateTimeOffset.UtcNow);
        _app = HttpHost.Build(Array.Empty<string>(), config, Tokens,
            new HostOptions { Certificate = certificate, Pairing = new PairingEndpoint(Pairing, Tokens, config.HttpTokenFile) });
        await _app.StartAsync();
        BaseUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/');
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await AddIn.DisposeAsync();
        try { Directory.Delete(Root, true); } catch { }
    }
}
```

- [ ] **Step 7: Write the failing pairing tests** — `CoreTests/Pairing/PairingClientTests.cs`:

```csharp
using Inventor.So.Mcp.Http.Pairing;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Pairing;

public class PairingClientTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _f;
    private readonly PairingClient _client = new PairingClient(trust => new SystemHttpTransport(trust));

    public PairingClientTests(BackendFixture fixture) { _f = fixture; }

    private PairingPayload Payload(PairingWindow window, string sha = null) =>
        PairingPayload.Parse(PairingSetup.QrPayload(_f.Base.Host, _f.Base.Port, window.OneTimeToken, sha ?? _f.CertSha256));

    [Fact]
    public async Task QrPayloadPairsWithThePinnedCertificate()
    {
        var window = _f.Pairing.Open("quest-qr", TimeSpan.FromMinutes(1));
        var server = await _client.PairWithQrAsync(Payload(window), "Quest 3", CancellationToken.None);
        Assert.Equal("quest-qr", server.ClientName);
        Assert.Equal(_f.CertSha256, server.CertSha256);
        Assert.Equal("quest-qr", _f.Tokens.Authenticate(server.Token));
    }

    [Fact]
    public async Task AWrongFingerprintIsRefusedBeforeTheSecretIsSent()
    {
        var window = _f.Pairing.Open("quest-mitm", TimeSpan.FromMinutes(1));
        var rejected = await Assert.ThrowsAsync<CertificateRejectedException>(() =>
            _client.PairWithQrAsync(Payload(window, new string('0', 64)), "Quest 3", CancellationToken.None));
        Assert.Equal(_f.CertSha256, rejected.PresentedSha256);
        // The secret never reached the server, so it still pairs.
        Assert.Equal("quest-mitm", (await _client.PairWithQrAsync(Payload(window), "Quest 3", CancellationToken.None)).ClientName);
    }

    [Fact]
    public async Task ManualPathProbesTheFingerprintThenPairsPinned()
    {
        var window = _f.Pairing.Open("quest-manual", TimeSpan.FromMinutes(1));
        var sha = await _client.ProbeFingerprintAsync(_f.Base.Host, _f.Base.Port, CancellationToken.None);
        Assert.Equal(_f.CertSha256, sha);
        var server = await _client.PairAsync(_f.Base.Host, _f.Base.Port, window.Code, ServerTrust.Pinned(sha), "Quest 3", CancellationToken.None);
        Assert.Equal("quest-manual", server.ClientName);
    }

    [Fact]
    public async Task AWrongCodeSurfacesTheServerErrorCode()
    {
        _f.Pairing.Open("quest-wrong", TimeSpan.FromMinutes(1));
        var ex = await Assert.ThrowsAsync<PairingException>(() =>
            _client.PairAsync(_f.Base.Host, _f.Base.Port, "not-the-code", ServerTrust.Pinned(_f.CertSha256), "Quest 3", CancellationToken.None));
        Assert.Equal("PAIRING_INVALID", ex.Code);
    }
}
```

- [ ] **Step 8: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~PairingClientTests"`
Expected: build FAILS, `The type or namespace name 'PairingClient' could not be found`.

- [ ] **Step 9: Implement** — `Core/Pairing/PairingClient.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>
    /// Pairs the headset with a PC. The QR path pins the fingerprint from the code; the manual path
    /// first reads the fingerprint (no secret sent), lets the user compare it with the PC console,
    /// then sends the 6-digit code over the pinned connection.
    /// </summary>
    public sealed class PairingClient
    {
        private readonly Func<ServerTrust, IHttpTransport> _transportFactory;

        public PairingClient(Func<ServerTrust, IHttpTransport> transportFactory) { _transportFactory = transportFactory; }

        public Task<PairedServer> PairWithQrAsync(PairingPayload payload, string deviceName, CancellationToken ct) =>
            PairAsync(payload.Host, payload.Port, payload.OneTimeToken, ServerTrust.Pinned(payload.CertSha256), deviceName, ct);

        /// <summary>SHA-256 of the certificate the PC presents; sends nothing but a health check.</summary>
        public async Task<string> ProbeFingerprintAsync(string host, int port, CancellationToken ct)
        {
            var trust = ServerTrust.FirstUse();
            await _transportFactory(trust).SendAsync(new TransportRequest("GET", PairedServer.MakeBaseUrl(host, port) + "/healthz"), ct);
            if (trust.LastPresentedSha256 == null) throw new TransportException("The PC did not present a certificate.");
            return trust.LastPresentedSha256;
        }

        public async Task<PairedServer> PairAsync(string host, int port, string secret, ServerTrust trust, string deviceName, CancellationToken ct)
        {
            var body = new JObject { ["secret"] = secret, ["device_name"] = deviceName }.ToString(Formatting.None);
            var response = await _transportFactory(trust).SendAsync(
                TransportRequest.Json("POST", PairedServer.MakeBaseUrl(host, port) + "/pair", body), ct);
            JObject json = null;
            try { json = JObject.Parse(response.Text); }
            catch (JsonReaderException) { }
            if (!response.IsSuccess)
                throw new PairingException((string)json?["error"]?["code"] ?? "PAIRING_FAILED",
                    (string)json?["error"]?["message"] ?? "Pairing failed (HTTP " + response.Status + ").");
            var client = (string)json?["client_name"];
            var token = (string)json?["token"];
            if (string.IsNullOrEmpty(client) || string.IsNullOrEmpty(token))
                throw new PairingException("PAIRING_FAILED", "The PC answered without a token.");
            return new PairedServer(host, port, trust.LastPresentedSha256, client, token);
        }
    }
}
```

- [ ] **Step 10: Run to verify pass**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`
Expected: all pass (18 tests).

- [ ] **Step 11: Commit**

```bash
git add "Inventor XR SO/Packages" "Inventor XR SO/Tests~"
git commit -m "feat(xr): core transport contract, certificate pinning and pairing client"
```


### Task B3: MCP client (requests and tool calls)

**Files:**
- Create: `Core/Net/SseParser.cs`, `Core/Mcp/McpExceptions.cs`, `Core/Mcp/McpClient.cs`
- Test: `CoreTests/Net/SseParserTests.cs`, `CoreTests/Mcp/McpClientTests.cs`

**Interfaces:**
- Consumes: B2 transport types, `BackendFixture`.
- Produces:
  - `SseParser(Action<string> onData)` with `Feed(string line)`, `Flush()`, `static IReadOnlyList<string> DataPayloads(string text)`.
  - `McpException(string code, string message)` with `Code`; `McpToolException(string tool, string code, string message, JObject details)` with `Tool`, `Details`; `McpUnauthorizedException()`; `McpSessionExpiredException()`.
  - `McpClient(IHttpTransport transport, string baseUrl, string token)`: `const ProtocolVersion = "2025-06-18"`, `SessionId`, `InitializeAsync(ct)`, `CallToolAsync(string name, JObject arguments, ct) : Task<JObject>` (the tool's JSON payload; throws `McpToolException` when the payload is `{"ok":false,"error":{...}}` or `isError` is set), `SubscribeAsync(string uri, ct)`, `RequestAsync(string method, JObject parameters, ct) : Task<JObject>` (JSON-RPC `result`).

- [ ] **Step 1: Write the failing tests** — `CoreTests/Net/SseParserTests.cs`:

```csharp
using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Tests.Net;

public class SseParserTests
{
    [Fact]
    public void DispatchesOnBlankLineAndJoinsMultiLineData()
    {
        var events = SseParser.DataPayloads("event: message\r\ndata: {\"a\":1}\r\n\r\n: comment\ndata: line1\ndata:line2\n\n");
        Assert.Equal(new[] { "{\"a\":1}", "line1\nline2" }, events);
    }

    [Fact]
    public void FlushEmitsATrailingEventWithoutBlankLine() =>
        Assert.Equal(new[] { "x" }, SseParser.DataPayloads("data: x"));

    [Fact]
    public void LinesWithoutDataEmitNothing() => Assert.Empty(SseParser.DataPayloads("id: 3\nretry: 100\n\n"));
}
```

`CoreTests/Mcp/McpClientTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~SseParserTests|FullyQualifiedName~McpClientTests"`
Expected: build FAILS (`SseParser`, `McpClient` missing).

- [ ] **Step 3: SSE parser** — `Core/Net/SseParser.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace InventorXrSo.Core.Net
{
    /// <summary>Server-sent events: feeds lines, emits the data of each event (only "data" fields matter here).</summary>
    public sealed class SseParser
    {
        private readonly Action<string> _onData;
        private readonly StringBuilder _data = new StringBuilder();
        private bool _hasData;

        public SseParser(Action<string> onData) { _onData = onData; }

        public void Feed(string line)
        {
            if (line.EndsWith("\r")) line = line.Substring(0, line.Length - 1);
            if (line.Length == 0)
            {
                Flush();
                return;
            }
            if (line[0] == ':') return;
            int colon = line.IndexOf(':');
            var field = colon < 0 ? line : line.Substring(0, colon);
            var value = colon < 0 ? "" : line.Substring(colon + 1);
            if (value.StartsWith(" ")) value = value.Substring(1);
            if (field != "data") return;
            if (_hasData) _data.Append('\n');
            _data.Append(value);
            _hasData = true;
        }

        public void Flush()
        {
            if (!_hasData) return;
            var data = _data.ToString();
            _data.Clear();
            _hasData = false;
            _onData(data);
        }

        public static IReadOnlyList<string> DataPayloads(string text)
        {
            var list = new List<string>();
            var parser = new SseParser(list.Add);
            foreach (var line in text.Split('\n')) parser.Feed(line);
            parser.Flush();
            return list;
        }
    }
}
```

- [ ] **Step 4: Exceptions** — `Core/Mcp/McpExceptions.cs`:

```csharp
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
```

- [ ] **Step 5: Client** — `Core/Mcp/McpClient.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Mcp
{
    /// <summary>
    /// Minimal MCP client over Streamable HTTP: JSON-RPC requests on POST /mcp (answered as JSON or as
    /// an SSE body), tool calls whose text content is Inventor SO's JSON payload, resource subscriptions
    /// and the GET event stream (Task B4).
    /// </summary>
    public sealed partial class McpClient
    {
        public const string ProtocolVersion = "2025-06-18";
        public const string ClientName = "inventor-xr-so";
        public const string ClientVersion = "0.1.0";

        private readonly IHttpTransport _transport;
        private readonly string _endpoint;
        private readonly string _token;
        private int _nextId;
        private string _sessionId;

        public McpClient(IHttpTransport transport, string baseUrl, string token)
        {
            _transport = transport;
            _endpoint = baseUrl.TrimEnd('/') + "/mcp";
            _token = token;
        }

        public string SessionId => _sessionId;

        public async Task InitializeAsync(CancellationToken ct)
        {
            _sessionId = null;
            await RequestAsync("initialize", new JObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = ClientName, ["version"] = ClientVersion },
            }, ct);
            var notification = new JObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" };
            await PostAsync(notification, ct);
        }

        public async Task<JObject> CallToolAsync(string name, JObject arguments, CancellationToken ct)
        {
            var result = await RequestAsync("tools/call", new JObject { ["name"] = name, ["arguments"] = arguments ?? new JObject() }, ct);
            var text = (string)result["content"]?[0]?["text"];
            bool isError = (bool?)result["isError"] ?? false;
            if (text == null) throw new McpToolException(name, "TOOL_NO_CONTENT", name + " returned no text content.", null);
            JObject payload;
            try { payload = JObject.Parse(text); }
            catch (JsonReaderException) { throw new McpToolException(name, "TOOL_ERROR", text, null); }
            if (payload["error"] is JObject error && ((bool?)payload["ok"] ?? false) == false)
                throw new McpToolException(name, (string)error["code"] ?? "TOOL_ERROR", (string)error["message"] ?? "", error);
            if (isError) throw new McpToolException(name, "TOOL_ERROR", text, payload);
            return payload;
        }

        public Task SubscribeAsync(string uri, CancellationToken ct) =>
            RequestAsync("resources/subscribe", new JObject { ["uri"] = uri }, ct);

        public async Task<JObject> RequestAsync(string method, JObject parameters, CancellationToken ct)
        {
            int id = Interlocked.Increment(ref _nextId);
            var message = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
            if (parameters != null) message["params"] = parameters;
            var reply = ReadReply(await PostAsync(message, ct), id);
            if (reply["error"] is JObject rpcError)
                throw new McpException("RPC_" + ((int?)rpcError["code"] ?? 0), (string)rpcError["message"] ?? method + " failed.");
            return reply["result"] as JObject ?? new JObject();
        }

        private TransportRequest NewRequest(string method, string accept)
        {
            var request = new TransportRequest(method, _endpoint);
            request.Headers["Accept"] = accept;
            request.Headers["Authorization"] = "Bearer " + _token;
            if (_sessionId != null)
            {
                request.Headers["Mcp-Session-Id"] = _sessionId;
                request.Headers["MCP-Protocol-Version"] = ProtocolVersion;
            }
            return request;
        }

        private async Task<TransportResponse> PostAsync(JObject message, CancellationToken ct)
        {
            var request = NewRequest("POST", "application/json, text/event-stream");
            request.Body = System.Text.Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
            request.Headers["Content-Type"] = "application/json";
            var response = await _transport.SendAsync(request, ct);
            ThrowForStatus(response.Status, response.Text);
            var session = response.Header("Mcp-Session-Id");
            if (!string.IsNullOrEmpty(session)) _sessionId = session;
            return response;
        }

        private void ThrowForStatus(int status, string body)
        {
            if (status == 401) throw new McpUnauthorizedException();
            if (status == 404 && _sessionId != null) throw new McpSessionExpiredException();
            if (status == 429) throw new McpException("RATE_LIMITED", "The PC is rate limiting this headset.");
            if (status < 200 || status >= 300)
                throw new McpException("HTTP_" + status, body == null || body.Length <= 200 ? body : body.Substring(0, 200));
        }

        internal static JObject ReadReply(TransportResponse response, int id)
        {
            var type = response.Header("Content-Type") ?? "";
            if (type.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var data in SseParser.DataPayloads(response.Text))
                {
                    var message = JObject.Parse(data);
                    if ((int?)message["id"] == id) return message;
                }
                throw new McpException("RPC_NO_REPLY", "The server closed the stream without answering request " + id + ".");
            }
            return JObject.Parse(response.Text);
        }
    }
}
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~SseParserTests|FullyQualifiedName~McpClientTests"`
Expected: 7 passed. If `ToolErrorsCarryTheirCode` reports a different code, print the payload (`ex.Details`) and align the assertion with the code the server actually forwards from `FakeAddIn` (the add-in fails with `WRONG_DOCUMENT_TYPE`); do not weaken it to "any code".

- [ ] **Step 7: Commit**

```bash
git add "Inventor XR SO/Packages" "Inventor XR SO/Tests~"
git commit -m "feat(xr): minimal MCP client over Streamable HTTP"
```

### Task B4: Event stream (resource updates)

**Files:**
- Modify: `bridge/tests/Bimwright.Ipt.Tests/FakeAddIn.cs` (event journal)
- Create: `Core/Mcp/McpClient.Events.cs`
- Test: `CoreTests/Mcp/McpEventStreamTests.cs`

**Interfaces:**
- Consumes: B3 `McpClient` (it is `partial`).
- Produces: `McpClient.RunEventStreamAsync(Action<string> onResourceUpdated, CancellationToken ct) : Task` — opens `GET /mcp` (SSE), calls back with the `uri` of each `notifications/resources/updated`; returns when the server closes the stream; throws `McpUnauthorizedException`, `McpSessionExpiredException`, `McpException("EVENT_STREAM_UNSUPPORTED")` on 405, `TransportException` on network loss. `FakeAddIn.RaiseDocumentChanged(bool geometry)`.

- [ ] **Step 1: FakeAddIn journal** — in `FakeAddIn.cs` add a field and a method:

```csharp
    private readonly List<JObject> _events = new();

    /// <summary>Simulate an edit in Inventor: advances the revision (and the visual revision for geometry) and journals it.</summary>
    public void RaiseDocumentChanged(bool geometry)
    {
        lock (_gate)
        {
            _sequence++;
            if (geometry) _visual++;
            _events.Add(new JObject { ["seq"] = _events.Count + 1, ["type"] = "document_changed", ["document_id"] = AssemblyId, ["geometry"] = geometry });
        }
    }
```

Replace the `get_events` case with:

```csharp
            case "get_events":
                lock (_gate)
                {
                    long after = (long?)p["after"] ?? 0;
                    return Ok(new JObject
                    {
                        ["epoch"] = "fake", ["cursor"] = _events.Count, ["resync_required"] = false,
                        ["events"] = new JArray(_events.Where(e => (long)e["seq"]! > after).Select(e => e.DeepClone())),
                    });
                }
```

Run: `dotnet test bridge/tests/Bimwright.Ipt.Tests`
Expected: same results as before the change.

- [ ] **Step 2: Write the failing test** — `CoreTests/Mcp/McpEventStreamTests.cs`:

```csharp
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Mcp;

public class McpEventStreamTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _f;
    public McpEventStreamTests(BackendFixture fixture) { _f = fixture; }

    [Fact]
    public async Task ADocumentChangeInInventorArrivesAsAResourceUpdate()
    {
        var client = new McpClient(_f.Transport(), _f.BaseUrl, _f.EditorToken);
        await client.InitializeAsync(CancellationToken.None);
        await client.SubscribeAsync("inventor://active-document", CancellationToken.None);

        var updated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = client.RunEventStreamAsync(uri => updated.TrySetResult(uri), cts.Token);
        await Task.Delay(500);   // let the GET stream open before the change
        _f.AddIn.RaiseDocumentChanged(geometry: true);

        var winner = await Task.WhenAny(updated.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(updated.Task, winner);
        Assert.Equal("inventor://active-document", await updated.Task);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~McpEventStreamTests"`
Expected: build FAILS, `'McpClient' does not contain a definition for 'RunEventStreamAsync'`.

- [ ] **Step 4: Implement** — `Core/Mcp/McpClient.Events.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Mcp
{
    public sealed partial class McpClient
    {
        public const string ResourceUpdated = "notifications/resources/updated";

        /// <summary>
        /// Listen on the session's GET stream until the server closes it. Resource updates are reported
        /// by URI; other server messages are ignored (M1 needs none).
        /// </summary>
        public async Task RunEventStreamAsync(Action<string> onResourceUpdated, CancellationToken ct)
        {
            var request = NewRequest("GET", "text/event-stream");
            request.Timeout = Timeout.InfiniteTimeSpan;
            var parser = new SseParser(data =>
            {
                JObject message;
                try { message = JObject.Parse(data); }
                catch (JsonReaderException) { return; }
                if ((string)message["method"] == ResourceUpdated)
                {
                    var uri = (string)message["params"]?["uri"];
                    if (uri != null) onResourceUpdated(uri);
                }
            });
            int status = await _transport.StreamLinesAsync(request, parser.Feed, ct);
            parser.Flush();
            if (status == 405) throw new McpException("EVENT_STREAM_UNSUPPORTED", "The server does not offer an event stream.");
            ThrowForStatus(status, null);
        }
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~McpEventStreamTests"`
Expected: PASS within ~2 s. If it fails with `EVENT_STREAM_UNSUPPORTED`, the SDK does not serve GET streams: stop and report to the controller — the session keeps working through its poll fallback (Task B8), but the design must record it.

- [ ] **Step 6: Commit**

```bash
git add bridge/tests/Bimwright.Ipt.Tests/FakeAddIn.cs "Inventor XR SO/Packages" "Inventor XR SO/Tests~"
git commit -m "feat(xr): MCP event stream for resource updates"
```


### Task B5: GLB reader, face map, handedness

**Files:**
- Create: `Core/Glb/GlbModel.cs`, `Core/Glb/FaceMap.cs`, `Core/Glb/Handedness.cs`
- Create: `Inventor XR SO/Assets/XrSo/Tests/EditMode/Fixtures/bolt-1cm.glb.bytes` (golden file written by the test)
- Modify: `docs/superpowers/specs/2026-09-26-inventor-xr-so-m1-design.md` (glTFast → own reader)
- Test: `CoreTests/Glb/GlbModelTests.cs`, `CoreTests/Glb/HandednessTests.cs`, `CoreTests/Support/RepoPaths.cs`

**Interfaces:**
- Produces:
  - `FaceRange(string faceId, int ordinal, int firstIndex, int indexCount)` with those properties.
  - `FaceMap(IEnumerable<FaceRange> faces, int indexCount)`: `Count`, `FaceRange FaceAtTriangle(int triangle)` (null when uncovered), `FaceRange Find(string faceId)`. Throws `FormatException` for ranges not made of whole triangles, outside the buffer or overlapping.
  - `GlbPrimitive`: `BodyIndex`, `BodyName`, `Visible`, `float[] Positions`, `float[] Normals`, `uint[] Indices`, `IReadOnlyList<FaceRange> Faces`, `FaceMap FaceMap`, `TriangleCount`.
  - `GlbModel.Parse(byte[] glb)` → `DocumentId`, `IReadOnlyList<GlbPrimitive> Primitives`. Throws `FormatException` / `NotSupportedException`.
  - `Handedness.FlipX(float[])`, `ReverseWinding(uint[])`, `ConvertMatrix(float[] columnMajor16)`.
  - Test support `RepoPaths.XrProject` (absolute path of `Inventor XR SO`).

- [ ] **Step 1: Write the failing tests** — `CoreTests/Support/RepoPaths.cs`:

```csharp
namespace InventorXrSo.Core.Tests.Support;

public static class RepoPaths
{
    /// <summary>The "Inventor XR SO" folder, found by walking up from the test binaries.</summary>
    public static string XrProject
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Packages", "com.occhipinti.inventorxrso.core"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Inventor XR SO project not found above " + AppContext.BaseDirectory);
        }
    }
}
```

`CoreTests/Glb/GlbModelTests.cs`:

```csharp
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Glb;

public class GlbModelTests
{
    /// <summary>What the server publishes for FakeAddIn's 1 cm bolt: one body, six faces, 12 triangles.</summary>
    internal static byte[] BoltGlb() => GlbBuilder.BuildDefinition(new GlbBuilder.MeshSource
    {
        Name = FakeAddIn.BoltId, DocumentId = FakeAddIn.BoltId, Bodies = new[] { FakeAddIn.Box(1.0, FakeAddIn.BoltId) },
    });

    [Fact]
    public void ReadsGeometryInMetresAndTheFaceTable()
    {
        var model = GlbModel.Parse(BoltGlb());
        Assert.Equal(FakeAddIn.BoltId, model.DocumentId);
        var body = Assert.Single(model.Primitives);
        Assert.Equal(24 * 3, body.Positions.Length);
        Assert.Equal(24 * 3, body.Normals.Length);
        Assert.Equal(36, body.Indices.Length);
        Assert.Equal(12, body.TriangleCount);
        Assert.Equal(0.01f, body.Positions.Max(), 5);
        Assert.Equal(6, body.Faces.Count);
        Assert.True(body.Visible);
    }

    [Theory]
    [InlineData(0, "ent_doc_bolt_f1")]
    [InlineData(1, "ent_doc_bolt_f1")]
    [InlineData(2, "ent_doc_bolt_f2")]
    [InlineData(11, "ent_doc_bolt_f6")]
    public void MapsTrianglesToFaces(int triangle, string faceId) =>
        Assert.Equal(faceId, GlbModel.Parse(BoltGlb()).Primitives[0].FaceMap.FaceAtTriangle(triangle).FaceId);

    [Fact]
    public void TrianglesOutsideEveryFaceMapToNothing() =>
        Assert.Null(GlbModel.Parse(BoltGlb()).Primitives[0].FaceMap.FaceAtTriangle(12));

    [Fact]
    public void RejectsCorruptFiles()
    {
        var glb = BoltGlb();
        glb[0] = 0;
        Assert.Throws<FormatException>(() => GlbModel.Parse(glb));
        Assert.Throws<FormatException>(() => GlbModel.Parse(new byte[4]));
    }

    [Fact]
    public void FaceRangesMustBeWholeNonOverlappingTriangles()
    {
        Assert.Throws<FormatException>(() => new FaceMap(new[] { new FaceRange("a", 1, 1, 3) }, 6));
        Assert.Throws<FormatException>(() => new FaceMap(new[] { new FaceRange("a", 1, 0, 9) }, 6));
        Assert.Throws<FormatException>(() => new FaceMap(new[] { new FaceRange("a", 1, 0, 6), new FaceRange("b", 2, 3, 3) }, 6));
    }

    /// <summary>
    /// Golden GLB for the Unity EditMode tests (Unity cannot run GlbBuilder). Regenerate with
    /// XRSO_UPDATE_FIXTURES=1 when GlbBuilder changes on purpose.
    /// </summary>
    [Fact]
    public void UnityFixtureMatchesTheServerOutput()
    {
        var path = Path.Combine(RepoPaths.XrProject, "Assets", "XrSo", "Tests", "EditMode", "Fixtures", "bolt-1cm.glb.bytes");
        var expected = BoltGlb();
        if (Environment.GetEnvironmentVariable("XRSO_UPDATE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, expected);
        }
        Assert.True(File.Exists(path), "Missing " + path + ": run once with XRSO_UPDATE_FIXTURES=1.");
        Assert.Equal(expected, File.ReadAllBytes(path));
    }
}
```

`CoreTests/Glb/HandednessTests.cs`:

```csharp
using InventorXrSo.Core.Glb;

namespace InventorXrSo.Core.Tests.Glb;

public class HandednessTests
{
    [Fact]
    public void FlipXNegatesEveryXOnly() =>
        Assert.Equal(new[] { -1f, 2, 3, -4, 5, 6 }, Handedness.FlipX(new[] { 1f, 2, 3, 4, 5, 6 }));

    [Fact]
    public void ReverseWindingKeepsTriangleOrder() =>
        Assert.Equal(new uint[] { 0, 2, 1, 3, 5, 4 }, Handedness.ReverseWinding(new uint[] { 0, 1, 2, 3, 4, 5 }));

    [Fact]
    public void TranslationXChangesSign()
    {
        var m = Identity();
        m[12] = 0.03f; m[13] = 0.02f; m[14] = 0.01f;
        var u = Handedness.ConvertMatrix(m);
        Assert.Equal(new[] { -0.03f, 0.02f, 0.01f }, new[] { u[12], u[13], u[14] });
    }

    [Fact]
    public void ConvertedMatrixActsOnFlippedPointsLikeTheOriginal()
    {
        float c = MathF.Cos(0.7f), s = MathF.Sin(0.7f);
        // Rotation about Y plus a translation, glTF column-major.
        var m = new[] { c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0.1f, 0.2f, 0.3f, 1 };
        var p = new[] { 0.5f, -0.25f, 0.75f };
        var expected = Handedness.FlipX(Apply(m, p));
        var actual = Apply(Handedness.ConvertMatrix(m), Handedness.FlipX(p));
        for (int i = 0; i < 3; i++) Assert.Equal(expected[i], actual[i], 5);
    }

    private static float[] Identity() => new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

    private static float[] Apply(float[] m, float[] p) => new[]
    {
        m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12],
        m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13],
        m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14],
    };
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~Glb"`
Expected: build FAILS (`InventorXrSo.Core.Glb` missing).

- [ ] **Step 3: FaceMap** — `Core/Glb/FaceMap.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Glb
{
    /// <summary>One B-rep face's slice of a primitive's index buffer (from <c>primitive.extras.faces</c>).</summary>
    public sealed class FaceRange
    {
        public FaceRange(string faceId, int ordinal, int firstIndex, int indexCount)
        {
            FaceId = faceId;
            Ordinal = ordinal;
            FirstIndex = firstIndex;
            IndexCount = indexCount;
        }

        public string FaceId { get; }
        public int Ordinal { get; }
        public int FirstIndex { get; }
        public int IndexCount { get; }
    }

    /// <summary>Triangle index → face, by binary search over the face ranges. No round trip to the PC.</summary>
    public sealed class FaceMap
    {
        private readonly FaceRange[] _faces;
        private readonly int[] _firstTriangle;
        private readonly int[] _endTriangle;

        public FaceMap(IEnumerable<FaceRange> faces, int indexCount)
        {
            _faces = faces.OrderBy(f => f.FirstIndex).ToArray();
            _firstTriangle = new int[_faces.Length];
            _endTriangle = new int[_faces.Length];
            for (int i = 0; i < _faces.Length; i++)
            {
                var f = _faces[i];
                if (f.FirstIndex < 0 || f.IndexCount <= 0 || f.FirstIndex % 3 != 0 || f.IndexCount % 3 != 0 || f.FirstIndex + f.IndexCount > indexCount)
                    throw new FormatException("Face " + f.FaceId + " is not a whole-triangle range inside the index buffer.");
                if (i > 0 && f.FirstIndex < _faces[i - 1].FirstIndex + _faces[i - 1].IndexCount)
                    throw new FormatException("Faces " + _faces[i - 1].FaceId + " and " + f.FaceId + " overlap.");
                _firstTriangle[i] = f.FirstIndex / 3;
                _endTriangle[i] = (f.FirstIndex + f.IndexCount) / 3;
            }
        }

        public int Count => _faces.Length;

        public FaceRange FaceAtTriangle(int triangle)
        {
            int lo = 0, hi = _faces.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (triangle < _firstTriangle[mid]) hi = mid - 1;
                else if (triangle >= _endTriangle[mid]) lo = mid + 1;
                else return _faces[mid];
            }
            return null;
        }

        public FaceRange Find(string faceId) => _faces.FirstOrDefault(f => f.FaceId == faceId);
    }
}
```

- [ ] **Step 4: GlbModel** — `Core/Glb/GlbModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Glb
{
    public sealed class GlbPrimitive
    {
        public GlbPrimitive(int bodyIndex, string bodyName, bool visible, float[] positions, float[] normals, uint[] indices, IReadOnlyList<FaceRange> faces)
        {
            BodyIndex = bodyIndex;
            BodyName = bodyName;
            Visible = visible;
            Positions = positions;
            Normals = normals;
            Indices = indices;
            Faces = faces;
            FaceMap = new FaceMap(faces, indices.Length);
        }

        public int BodyIndex { get; }
        public string BodyName { get; }
        public bool Visible { get; }
        /// <summary>xyz triples, metres, glTF (right-handed, Y up).</summary>
        public float[] Positions { get; }
        public float[] Normals { get; }
        public uint[] Indices { get; }
        public IReadOnlyList<FaceRange> Faces { get; }
        public FaceMap FaceMap { get; }
        public int TriangleCount => Indices.Length / 3;
    }

    /// <summary>
    /// Reader for the GLBs Inventor SO publishes (GlbBuilder): triangle primitives with float POSITION /
    /// NORMAL and integer indices, face table in primitive extras. Anything else is refused, not guessed.
    /// </summary>
    public sealed class GlbModel
    {
        private const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;

        public GlbModel(string documentId, IReadOnlyList<GlbPrimitive> primitives)
        {
            DocumentId = documentId;
            Primitives = primitives;
        }

        public string DocumentId { get; }
        public IReadOnlyList<GlbPrimitive> Primitives { get; }

        public static GlbModel Parse(byte[] glb)
        {
            if (glb == null || glb.Length < 20 || U32(glb, 0) != Magic) throw new FormatException("Not a GLB file.");
            if (U32(glb, 4) != 2) throw new FormatException("Only glTF 2.0 GLB files are supported.");
            if (U32(glb, 8) != glb.Length) throw new FormatException("GLB length does not match its header.");
            int jsonLength = (int)U32(glb, 12);
            if (U32(glb, 16) != JsonChunk || 20 + jsonLength > glb.Length) throw new FormatException("GLB JSON chunk missing.");
            JObject gltf;
            try { gltf = JObject.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength)); }
            catch (JsonReaderException ex) { throw new FormatException("GLB JSON chunk unreadable.", ex); }

            int binStart = 20 + jsonLength, binOffset = 0, binLength = 0;
            if (binStart + 8 <= glb.Length)
            {
                binLength = (int)U32(glb, binStart);
                if (U32(glb, binStart + 4) != BinChunk || binStart + 8 + binLength > glb.Length) throw new FormatException("GLB BIN chunk corrupt.");
                binOffset = binStart + 8;
            }
            var bin = new Bin(glb, binOffset, binLength);

            var primitives = new List<GlbPrimitive>();
            foreach (var mesh in gltf["meshes"] as JArray ?? new JArray())
            foreach (var p in mesh["primitives"] as JArray ?? new JArray())
            {
                if (((int?)p["mode"] ?? 4) != 4) throw new NotSupportedException("Only triangle primitives are supported.");
                var attributes = p["attributes"] as JObject ?? throw new FormatException("Primitive without attributes.");
                var positions = ReadFloats(gltf, bin, (int?)attributes["POSITION"] ?? throw new FormatException("Primitive without POSITION."), "VEC3");
                var normals = attributes["NORMAL"] == null ? new float[0] : ReadFloats(gltf, bin, (int)attributes["NORMAL"], "VEC3");
                var indices = p["indices"] == null
                    ? Enumerable.Range(0, positions.Length / 3).Select(i => (uint)i).ToArray()
                    : ReadIndices(gltf, bin, (int)p["indices"]);
                var extras = p["extras"] as JObject ?? new JObject();
                var faces = (extras["faces"] as JArray ?? new JArray())
                    .Select(f => new FaceRange((string)f["face_id"], (int?)f["ordinal"] ?? 0, (int)f["first_index"], (int)f["index_count"]))
                    .ToList();
                primitives.Add(new GlbPrimitive((int?)extras["body_index"] ?? primitives.Count + 1,
                    (string)extras["body_name"] ?? (string)mesh["name"] ?? "body", (bool?)extras["visible"] ?? true,
                    positions, normals, indices, faces));
            }
            return new GlbModel((string)gltf["asset"]?["extras"]?["document_id"], primitives);
        }

        private readonly struct Bin
        {
            public Bin(byte[] bytes, int offset, int length) { Bytes = bytes; Offset = offset; Length = length; }
            public byte[] Bytes { get; }
            public int Offset { get; }
            public int Length { get; }
        }

        private static (int start, int count, int componentType) Locate(JObject gltf, Bin bin, int accessorIndex, int componentsPerElement)
        {
            var accessor = gltf["accessors"]?[accessorIndex] ?? throw new FormatException("Missing accessor " + accessorIndex + ".");
            var view = gltf["bufferViews"]?[(int)accessor["bufferView"]] ?? throw new FormatException("Missing buffer view.");
            int componentType = (int)accessor["componentType"];
            int size = componentType == 5126 || componentType == 5125 ? 4 : componentType == 5123 ? 2 : componentType == 5121 ? 1 : 0;
            if (size == 0) throw new NotSupportedException("Accessor component type " + componentType + " is not supported.");
            if (view["byteStride"] != null && (int)view["byteStride"] != size * componentsPerElement)
                throw new NotSupportedException("Interleaved buffer views are not supported.");
            int count = (int)accessor["count"] * componentsPerElement;
            int start = ((int?)view["byteOffset"] ?? 0) + ((int?)accessor["byteOffset"] ?? 0);
            if (start < 0 || start + count * size > bin.Length) throw new FormatException("Accessor " + accessorIndex + " runs past the BIN chunk.");
            return (bin.Offset + start, count, componentType);
        }

        private static float[] ReadFloats(JObject gltf, Bin bin, int accessorIndex, string type)
        {
            if ((string)gltf["accessors"]?[accessorIndex]?["type"] != type) throw new FormatException("Accessor " + accessorIndex + " is not " + type + ".");
            var (start, count, componentType) = Locate(gltf, bin, accessorIndex, 3);
            if (componentType != 5126) throw new NotSupportedException("Only float vertex attributes are supported.");
            var values = new float[count];
            Buffer.BlockCopy(bin.Bytes, start, values, 0, count * 4);
            return values;
        }

        private static uint[] ReadIndices(JObject gltf, Bin bin, int accessorIndex)
        {
            var (start, count, componentType) = Locate(gltf, bin, accessorIndex, 1);
            var values = new uint[count];
            for (int i = 0; i < count; i++)
                values[i] = componentType == 5125 ? BitConverter.ToUInt32(bin.Bytes, start + i * 4)
                          : componentType == 5123 ? BitConverter.ToUInt16(bin.Bytes, start + i * 2)
                          : bin.Bytes[start + i];
            return values;
        }

        private static uint U32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);
    }
}
```

- [ ] **Step 5: Handedness** — `Core/Glb/Handedness.cs`:

```csharp
namespace InventorXrSo.Core.Glb
{
    /// <summary>
    /// glTF (right-handed) to Unity (left-handed): mirror X. Positions/normals negate X, triangles reverse
    /// winding (in place, so triangle order and face ranges are unchanged), matrices become S·M·S.
    /// </summary>
    public static class Handedness
    {
        public static float[] FlipX(float[] xyz)
        {
            var result = (float[])xyz.Clone();
            for (int i = 0; i < result.Length; i += 3) result[i] = -result[i];
            return result;
        }

        public static uint[] ReverseWinding(uint[] indices)
        {
            var result = (uint[])indices.Clone();
            for (int i = 0; i + 2 < result.Length; i += 3)
            {
                var b = result[i + 1];
                result[i + 1] = result[i + 2];
                result[i + 2] = b;
            }
            return result;
        }

        /// <summary>Column-major 4x4: negate every element in row 0 or column 0, but not both.</summary>
        public static float[] ConvertMatrix(float[] m)
        {
            var result = new float[16];
            for (int i = 0; i < 16; i++)
            {
                int row = i % 4, column = i / 4;
                result[i] = (row == 0) ^ (column == 0) ? -m[i] : m[i];
            }
            return result;
        }
    }
}
```

- [ ] **Step 6: Generate the golden fixture and run**

```powershell
$env:XRSO_UPDATE_FIXTURES = "1"; dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~UnityFixtureMatchesTheServerOutput"; Remove-Item Env:XRSO_UPDATE_FIXTURES
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~Glb"
```

Expected: fixture written; then all Glb tests pass (13).

- [ ] **Step 7: Record the deviation in the spec** — in `docs/superpowers/specs/2026-09-26-inventor-xr-so-m1-design.md`: in the §2 table add the row `| Lettura GLB | reader proprio nel Core (GlbModel), niente glTFast: i GLB vengono solo da GlbBuilder di questo repo, testati insieme |`; in §3.2 change the `XrSo.Scene` row's "GLB via glTFast" to "GLB via `GlbModel` (Core)" and its dependency column to "Backend"; replace §6 with: "Rischio eliminato: il Core legge direttamente i GLB di `GlbBuilder` (test sul formato reale nello stesso repo), la conversione di mano inverte l'avvolgimento dentro ogni triangolo senza cambiarne l'ordine, quindi i range di `extras.faces` restano validi."

- [ ] **Step 8: Commit**

```bash
git add "Inventor XR SO/Packages" "Inventor XR SO/Tests~" "Inventor XR SO/Assets/XrSo/Tests/EditMode/Fixtures" docs/superpowers/specs/2026-09-26-inventor-xr-so-m1-design.md
git commit -m "feat(xr): GLB reader with triangle-to-face map and handedness conversion"
```

### Task B6: Typed backend facade and asset cache

**Files:**
- Create: `Core/Backend/ToolNames.cs`, `Core/Backend/Dto.cs`, `Core/Backend/AssetCache.cs`, `Core/Backend/IInventorBackend.cs`, `Core/Backend/InventorBackend.cs`
- Test: `CoreTests/Backend/DtoTests.cs`, `CoreTests/Backend/InventorBackendTests.cs`

**Interfaces:**
- Consumes: B3/B4 `McpClient`, B2 `PairedServer`, `IHttpTransport`.
- Produces:
  - `CapabilitiesInfo.FromJson(JObject)`: `ServerVersion`, `InventorYear` (int?), `Reachable`, `UnreachableReason`, `AddInExperimentalEnabled`, `ServerExperimentalEnabled`, `ActiveDocumentKind`, `XrMesh`, `SceneGraph`, `Highlight`, `EventSubscriptions`, `IsXrReady`.
  - `DocumentState(string documentId, string revision, string visualRevision)` + `FromJson`.
  - `SceneNode`: `Name`, `OccurrenceId`, `DefinitionDocumentId`, `DefinitionKind`, `Visible`, `Suppressed`, `float[] MatrixGltf` (16, identity when absent), `IReadOnlyList<SceneNode> Children`.
  - `PlacedPart { SceneNode Node; float[] MatrixGltf }`.
  - `SceneGraph.FromJson(JObject)`: `DocumentId`, `Kind`, `Revision`, `VisualRevision`, `Truncated`, `Root`, `DefinitionIds`, `IEnumerable<PlacedPart> PlacedParts()`, `DocumentState State`.
  - `DefinitionMesh.FromJson(string definitionId, JObject)`: `DefinitionDocumentId`, `AssetId`, `AssetUrl`, `VisualRevision`.
  - `IAssetCache { bool TryGet(string assetId, out byte[] bytes); void Put(string assetId, byte[] bytes); }`, `MemoryAssetCache`, `FileAssetCache(string directory)`, `AssetIds.Matches(string assetId, byte[] bytes)`.
  - `IInventorBackend` (below) and `InventorBackend(IHttpTransport transport, PairedServer server, IAssetCache cache)`.

```csharp
public interface IInventorBackend
{
    Task ConnectAsync(CancellationToken ct);
    Task<CapabilitiesInfo> GetCapabilitiesAsync(CancellationToken ct);
    Task<DocumentState> GetDocumentStateAsync(CancellationToken ct);
    Task<SceneGraph> GetSceneGraphAsync(CancellationToken ct);
    Task<DefinitionMesh> GetDefinitionMeshAsync(string definitionDocumentId, CancellationToken ct);
    Task<byte[]> GetAssetAsync(DefinitionMesh mesh, CancellationToken ct);
    Task<string> PickFaceAsync(string occurrenceId, string faceId, CancellationToken ct);
    Task HighlightAsync(IReadOnlyList<string> entityIds, CancellationToken ct);
    Task ClearHighlightAsync(CancellationToken ct);
    /// <summary>Subscribe to the active document and report changes until the stream ends.</summary>
    Task RunEventsAsync(Action onChanged, CancellationToken ct);
}
```

- [ ] **Step 1: Write the failing tests** — `CoreTests/Backend/DtoTests.cs`:

```csharp
using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class DtoTests
{
    [Fact]
    public void CapabilitiesNeedTheXrToolsAndAReachableTarget()
    {
        var json = JObject.Parse(@"{""server"":{""version"":""0.1.0"",""experimental_enabled"":true},
            ""target"":{""inventor_year"":2027,""reachable"":true,""add_in_experimental_enabled"":true,""active_document_kind"":""assembly""},
            ""capabilities"":{""xr_mesh"":true,""scene_graph"":true,""highlight"":true,""event_subscriptions"":true}}");
        var caps = CapabilitiesInfo.FromJson(json);
        Assert.True(caps.IsXrReady);
        Assert.Equal(2027, caps.InventorYear);
        Assert.Equal("assembly", caps.ActiveDocumentKind);
        json["capabilities"]["highlight"] = false;
        Assert.False(CapabilitiesInfo.FromJson(json).IsXrReady);
    }

    [Fact]
    public void PlacedPartsSkipHiddenSuppressedAndSubassemblyNodes()
    {
        var json = JObject.Parse(@"{""document_id"":""asm"",""kind"":""assembly"",""revision"":""r1"",""visual_revision"":""v1"",
            ""definition_document_ids"":[""p"",""q""],
            ""root"":{""name"":""Top.iam"",""definition_kind"":""assembly"",""children"":[
              {""name"":""P:1"",""occurrence_id"":""ent_1"",""definition_document_id"":""p"",""definition_kind"":""part"",""visible"":true,""suppressed"":false,
               ""matrix_gltf"":[1,0,0,0,0,1,0,0,0,0,1,0,0.5,0,0,1],""children"":[]},
              {""name"":""P:2"",""occurrence_id"":""ent_2"",""definition_document_id"":""p"",""definition_kind"":""part"",""visible"":false,""suppressed"":false,""children"":[]},
              {""name"":""Sub:1"",""occurrence_id"":""ent_3"",""definition_document_id"":""s"",""definition_kind"":""assembly"",""visible"":true,""suppressed"":false,""children"":[
                {""name"":""Q:1"",""occurrence_id"":""ent_4"",""definition_document_id"":""q"",""definition_kind"":""part"",""visible"":true,""suppressed"":false,""children"":[]}]}]}}");
        var scene = SceneGraph.FromJson(json);
        var placed = scene.PlacedParts().ToList();
        Assert.Equal(new[] { "ent_1", "ent_4" }, placed.Select(p => p.Node.OccurrenceId));
        Assert.Equal(0.5f, placed[0].MatrixGltf[12]);
        Assert.Equal(1f, placed[1].MatrixGltf[15]);
        Assert.Equal(new[] { "p", "q" }, scene.DefinitionIds);
        Assert.Equal("v1", scene.State.VisualRevision);
    }

    [Fact]
    public void APartDocumentPlacesItsRootAtTheOrigin()
    {
        var json = JObject.Parse(@"{""document_id"":""p"",""kind"":""part"",""revision"":""r"",""visual_revision"":""v"",""definition_document_ids"":[""p""],
            ""root"":{""name"":""P.ipt"",""definition_document_id"":""p"",""definition_kind"":""part"",""visible"":true,""suppressed"":false,""children"":[]}}");
        var placed = Assert.Single(SceneGraph.FromJson(json).PlacedParts());
        Assert.Null(placed.Node.OccurrenceId);
        Assert.Equal("p", placed.Node.DefinitionDocumentId);
    }

    [Fact]
    public void AssetIdsAreCheckedAgainstTheirContent()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var id = "a_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.True(AssetIds.Matches(id, bytes));
        Assert.False(AssetIds.Matches(id, new byte[] { 1, 2 }));
        Assert.False(AssetIds.Matches("b_" + id.Substring(2), bytes));
    }

    [Fact]
    public void FileCacheSurvivesANewInstance()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xrso-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            new FileAssetCache(dir).Put("a_" + new string('1', 64), new byte[] { 9 });
            Assert.True(new FileAssetCache(dir).TryGet("a_" + new string('1', 64), out var bytes));
            Assert.Equal(new byte[] { 9 }, bytes);
            Assert.False(new FileAssetCache(dir).TryGet("../evil", out _));
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

`CoreTests/Backend/InventorBackendTests.cs`:

```csharp
using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Backend;

public class InventorBackendTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _f;
    public InventorBackendTests(BackendFixture fixture) { _f = fixture; }

    private async Task<InventorBackend> Connected(IAssetCache cache = null)
    {
        var backend = new InventorBackend(_f.Transport(), _f.EditorServer, cache ?? new MemoryAssetCache());
        await backend.ConnectAsync(CancellationToken.None);
        return backend;
    }

    [Fact]
    public async Task M1FlowAgainstTheFakeAddIn()
    {
        var ct = CancellationToken.None;
        var backend = await Connected();
        Assert.True((await backend.GetCapabilitiesAsync(ct)).IsXrReady);

        var state = await backend.GetDocumentStateAsync(ct);
        Assert.Equal(FakeAddIn.AssemblyId, state.DocumentId);

        var scene = await backend.GetSceneGraphAsync(ct);
        Assert.Equal("assembly", scene.Kind);
        Assert.Equal(new[] { FakeAddIn.BoltId, FakeAddIn.PlateId }.OrderBy(x => x), scene.DefinitionIds.OrderBy(x => x));
        Assert.Equal(3, scene.PlacedParts().Count());

        var mesh = await backend.GetDefinitionMeshAsync(FakeAddIn.BoltId, ct);
        var model = GlbModel.Parse(await backend.GetAssetAsync(mesh, ct));
        var face = model.Primitives[0].FaceMap.FaceAtTriangle(4).FaceId;

        var entity = await backend.PickFaceAsync("ent_occ_2", face, ct);
        Assert.Equal("ent_proxy_" + face, entity);
        await backend.HighlightAsync(new[] { entity }, ct);
        await backend.ClearHighlightAsync(ct);
        Assert.Contains("highlight_entity", _f.AddIn.Commands);
    }

    [Fact]
    public async Task ACachedAssetIsNotDownloadedAgain()
    {
        var cache = new CountingCache();
        var backend = await Connected(cache);
        var mesh = await backend.GetDefinitionMeshAsync(FakeAddIn.PlateId, CancellationToken.None);
        var first = await backend.GetAssetAsync(mesh, CancellationToken.None);
        var second = await backend.GetAssetAsync(mesh, CancellationToken.None);
        Assert.Equal(first, second);
        Assert.Equal(1, cache.Puts);
    }

    private sealed class CountingCache : IAssetCache
    {
        private readonly MemoryAssetCache _inner = new MemoryAssetCache();
        public int Puts { get; private set; }
        public bool TryGet(string assetId, out byte[] bytes) => _inner.TryGet(assetId, out bytes);
        public void Put(string assetId, byte[] bytes) { Puts++; _inner.Put(assetId, bytes); }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~Backend"`
Expected: build FAILS (`InventorXrSo.Core.Backend` missing).

- [ ] **Step 3: Tool names** — `Core/Backend/ToolNames.cs`:

```csharp
namespace InventorXrSo.Core.Backend
{
    public static class ToolNames
    {
        public const string GetCapabilities = "inventor_get_capabilities";
        public const string GetVisualRevision = "inventor_get_visual_revision";
        public const string GetSceneGraph = "inventor_get_scene_graph";
        public const string GetDisplayMesh = "inventor_get_display_mesh";
        public const string PickEntity = "inventor_pick_entity";
        public const string HighlightEntity = "inventor_highlight_entity";
        public const string ActiveDocumentUri = "inventor://active-document";
    }
}
```

- [ ] **Step 4: DTOs** — `Core/Backend/Dto.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class CapabilitiesInfo
    {
        public string ServerVersion { get; private set; }
        public bool ServerExperimentalEnabled { get; private set; }
        public int? InventorYear { get; private set; }
        public bool Reachable { get; private set; }
        public string UnreachableReason { get; private set; }
        public bool AddInExperimentalEnabled { get; private set; }
        public string ActiveDocumentKind { get; private set; }
        public bool XrMesh { get; private set; }
        public bool SceneGraph { get; private set; }
        public bool Highlight { get; private set; }
        public bool EventSubscriptions { get; private set; }
        public bool IsXrReady => Reachable && XrMesh && SceneGraph && Highlight;

        public static CapabilitiesInfo FromJson(JObject json)
        {
            var server = json["server"] ?? new JObject();
            var target = json["target"] ?? new JObject();
            var flags = json["capabilities"] ?? new JObject();
            return new CapabilitiesInfo
            {
                ServerVersion = (string)server["version"],
                ServerExperimentalEnabled = (bool?)server["experimental_enabled"] ?? false,
                InventorYear = (int?)target["inventor_year"],
                Reachable = (bool?)target["reachable"] ?? false,
                UnreachableReason = (string)target["reason"],
                AddInExperimentalEnabled = (bool?)target["add_in_experimental_enabled"] ?? false,
                ActiveDocumentKind = (string)target["active_document_kind"],
                XrMesh = (bool?)flags["xr_mesh"] ?? false,
                SceneGraph = (bool?)flags["scene_graph"] ?? false,
                Highlight = (bool?)flags["highlight"] ?? false,
                EventSubscriptions = (bool?)flags["event_subscriptions"] ?? false,
            };
        }
    }

    public sealed class DocumentState
    {
        public DocumentState(string documentId, string revision, string visualRevision)
        {
            DocumentId = documentId;
            Revision = revision;
            VisualRevision = visualRevision;
        }

        public string DocumentId { get; }
        public string Revision { get; }
        public string VisualRevision { get; }

        public static DocumentState FromJson(JObject json) =>
            new DocumentState((string)json["document_id"], (string)json["revision"], (string)json["visual_revision"]);
    }

    public sealed class SceneNode
    {
        private static readonly float[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        public string Name { get; private set; }
        public string OccurrenceId { get; private set; }
        public string DefinitionDocumentId { get; private set; }
        public string DefinitionKind { get; private set; }
        public bool Visible { get; private set; }
        public bool Suppressed { get; private set; }
        /// <summary>Column-major, metres, top-level assembly space (identity for the root).</summary>
        public float[] MatrixGltf { get; private set; }
        public IReadOnlyList<SceneNode> Children { get; private set; }

        public static SceneNode FromJson(JToken json) => new SceneNode
        {
            Name = (string)json["name"],
            OccurrenceId = (string)json["occurrence_id"],
            DefinitionDocumentId = (string)json["definition_document_id"],
            DefinitionKind = (string)json["definition_kind"],
            Visible = (bool?)json["visible"] ?? true,
            Suppressed = (bool?)json["suppressed"] ?? false,
            MatrixGltf = json["matrix_gltf"] is JArray m && m.Count == 16 ? m.Select(v => (float)v).ToArray() : (float[])Identity.Clone(),
            Children = (json["children"] as JArray ?? new JArray()).Select(FromJson).ToList(),
        };
    }

    public sealed class PlacedPart
    {
        public PlacedPart(SceneNode node, float[] matrixGltf)
        {
            Node = node;
            MatrixGltf = matrixGltf;
        }

        public SceneNode Node { get; }
        public float[] MatrixGltf { get; }
    }

    public sealed class SceneGraph
    {
        public string DocumentId { get; private set; }
        public string Kind { get; private set; }
        public string Revision { get; private set; }
        public string VisualRevision { get; private set; }
        public bool Truncated { get; private set; }
        public SceneNode Root { get; private set; }
        public IReadOnlyList<string> DefinitionIds { get; private set; }
        public DocumentState State => new DocumentState(DocumentId, Revision, VisualRevision);

        public static SceneGraph FromJson(JObject json) => new SceneGraph
        {
            DocumentId = (string)json["document_id"],
            Kind = (string)json["kind"],
            Revision = (string)json["revision"],
            VisualRevision = (string)json["visual_revision"],
            Truncated = (bool?)json["truncated"] ?? false,
            Root = SceneNode.FromJson(json["root"] ?? new JObject()),
            DefinitionIds = (json["definition_document_ids"] as JArray ?? new JArray()).Select(v => (string)v).ToList(),
        };

        /// <summary>Every visible, unsuppressed part to draw, with its top-level transform (leaves already carry it).</summary>
        public IEnumerable<PlacedPart> PlacedParts() => Walk(Root);

        private static IEnumerable<PlacedPart> Walk(SceneNode node)
        {
            if (!node.Visible || node.Suppressed) yield break;
            if (node.DefinitionKind == "part" && node.DefinitionDocumentId != null)
            {
                yield return new PlacedPart(node, node.MatrixGltf);
                yield break;
            }
            foreach (var child in node.Children)
            foreach (var placed in Walk(child))
                yield return placed;
        }
    }

    public sealed class DefinitionMesh
    {
        public string DefinitionDocumentId { get; private set; }
        public string AssetId { get; private set; }
        public string AssetUrl { get; private set; }
        public string VisualRevision { get; private set; }

        public static DefinitionMesh FromJson(string definitionDocumentId, JObject json) => new DefinitionMesh
        {
            DefinitionDocumentId = definitionDocumentId,
            AssetId = (string)json["asset"]?["asset_id"],
            AssetUrl = (string)json["asset"]?["asset_url"],
            VisualRevision = (string)json["visual_revision"],
        };
    }
}
```

- [ ] **Step 5: Asset cache** — `Core/Backend/AssetCache.cs`:

```csharp
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Backend
{
    public interface IAssetCache
    {
        bool TryGet(string assetId, out byte[] bytes);
        void Put(string assetId, byte[] bytes);
    }

    public static class AssetIds
    {
        private static readonly Regex Pattern = new Regex("^a_[0-9a-f]{64}$");

        public static bool IsValid(string assetId) => assetId != null && Pattern.IsMatch(assetId);

        /// <summary>Asset ids are "a_" + SHA-256 of the bytes: a download is checked, not trusted.</summary>
        public static bool Matches(string assetId, byte[] bytes)
        {
            if (!IsValid(assetId)) return false;
            string hash;
            using (var sha = SHA256.Create())
            {
                var builder = new StringBuilder(64);
                foreach (var b in sha.ComputeHash(bytes)) builder.Append(b.ToString("x2"));
                hash = builder.ToString();
            }
            return CertificatePin.FixedTimeEquals(assetId.Substring(2), hash);
        }
    }

    public sealed class MemoryAssetCache : IAssetCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _items = new ConcurrentDictionary<string, byte[]>();
        public bool TryGet(string assetId, out byte[] bytes) => _items.TryGetValue(assetId, out bytes);
        public void Put(string assetId, byte[] bytes) => _items[assetId] = bytes;
    }

    /// <summary>Content-addressed disk cache: an id never changes meaning, so entries never need invalidating.</summary>
    public sealed class FileAssetCache : IAssetCache
    {
        private readonly string _directory;

        public FileAssetCache(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
        }

        public bool TryGet(string assetId, out byte[] bytes)
        {
            bytes = null;
            if (!AssetIds.IsValid(assetId)) return false;
            var path = Path.Combine(_directory, assetId + ".glb");
            if (!File.Exists(path)) return false;
            bytes = File.ReadAllBytes(path);
            return true;
        }

        public void Put(string assetId, byte[] bytes)
        {
            if (!AssetIds.IsValid(assetId)) return;
            var path = Path.Combine(_directory, assetId + ".glb");
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Delete(temp);
            else File.Move(temp, path);
        }
    }
}
```

- [ ] **Step 6: Backend** — `Core/Backend/IInventorBackend.cs` with the interface from **Interfaces** above (namespace `InventorXrSo.Core.Backend`, usings `System`, `System.Collections.Generic`, `System.Threading`, `System.Threading.Tasks`). Then `Core/Backend/InventorBackend.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class InventorBackend : IInventorBackend
    {
        private readonly IHttpTransport _transport;
        private readonly PairedServer _server;
        private readonly IAssetCache _cache;
        private readonly McpClient _mcp;

        public InventorBackend(IHttpTransport transport, PairedServer server, IAssetCache cache)
        {
            _transport = transport;
            _server = server;
            _cache = cache;
            _mcp = new McpClient(transport, server.BaseUrl, server.Token);
        }

        public Task ConnectAsync(CancellationToken ct) => _mcp.InitializeAsync(ct);

        public async Task<CapabilitiesInfo> GetCapabilitiesAsync(CancellationToken ct) =>
            CapabilitiesInfo.FromJson(await _mcp.CallToolAsync(ToolNames.GetCapabilities, new JObject(), ct));

        public async Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) =>
            DocumentState.FromJson(await _mcp.CallToolAsync(ToolNames.GetVisualRevision, new JObject(), ct));

        public async Task<SceneGraph> GetSceneGraphAsync(CancellationToken ct) =>
            SceneGraph.FromJson(await _mcp.CallToolAsync(ToolNames.GetSceneGraph, new JObject { ["include_meshes"] = false }, ct));

        public async Task<DefinitionMesh> GetDefinitionMeshAsync(string definitionDocumentId, CancellationToken ct) =>
            DefinitionMesh.FromJson(definitionDocumentId,
                await _mcp.CallToolAsync(ToolNames.GetDisplayMesh, new JObject { ["document_id"] = definitionDocumentId }, ct));

        public async Task<byte[]> GetAssetAsync(DefinitionMesh mesh, CancellationToken ct)
        {
            if (_cache.TryGet(mesh.AssetId, out var cached)) return cached;
            var request = new TransportRequest("GET", Resolve(mesh.AssetUrl)) { Timeout = TimeSpan.FromMinutes(2) };
            request.Headers["Authorization"] = "Bearer " + _server.Token;
            var response = await _transport.SendAsync(request, ct);
            if (response.Status == 401) throw new McpUnauthorizedException();
            if (!response.IsSuccess)
                throw new McpException("ASSET_" + response.Status, "Asset " + mesh.AssetId + " could not be downloaded (HTTP " + response.Status + ").");
            if (!AssetIds.Matches(mesh.AssetId, response.Body))
                throw new McpException("ASSET_CORRUPT", "Asset " + mesh.AssetId + " does not match its content hash.");
            _cache.Put(mesh.AssetId, response.Body);
            return response.Body;
        }

        public async Task<string> PickFaceAsync(string occurrenceId, string faceId, CancellationToken ct) =>
            (string)(await _mcp.CallToolAsync(ToolNames.PickEntity, new JObject { ["occurrence_id"] = occurrenceId, ["face_id"] = faceId }, ct))["entity_id"];

        public Task HighlightAsync(IReadOnlyList<string> entityIds, CancellationToken ct) =>
            _mcp.CallToolAsync(ToolNames.HighlightEntity,
                new JObject { ["entity_ids"] = new JArray(entityIds.Cast<object>().ToArray()), ["mode"] = "highlight" }, ct);

        public Task ClearHighlightAsync(CancellationToken ct) =>
            _mcp.CallToolAsync(ToolNames.HighlightEntity, new JObject { ["mode"] = "clear" }, ct);

        public async Task RunEventsAsync(Action onChanged, CancellationToken ct)
        {
            await _mcp.SubscribeAsync(ToolNames.ActiveDocumentUri, ct);
            await _mcp.RunEventStreamAsync(_ => onChanged(), ct);
        }

        /// <summary>asset_url is relative ("/assets/…") unless the host has a public URL.</summary>
        private string Resolve(string assetUrl) =>
            assetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? assetUrl : _server.BaseUrl + "/" + assetUrl.TrimStart('/');
    }
}
```

- [ ] **Step 7: Run to verify pass**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~Backend"`
Expected: 7 passed. `FakeAddIn` answers `get_scene_graph` without `definition_document_ids`; the server adds them (`SceneComposer.Annotate`) — if `DefinitionIds` comes back empty, print the payload and report, do not synthesize ids on the client.

- [ ] **Step 8: Commit**

```bash
git add "Inventor XR SO/Packages" "Inventor XR SO/Tests~"
git commit -m "feat(xr): typed Inventor backend facade with verified asset cache"
```


### Task B7: Scene loading, diff, backoff, selection

**Files:**
- Create: `Core/Session/SceneLoader.cs`, `Core/Session/SceneDiff.cs`, `Core/Session/Backoff.cs`, `Core/Session/Delay.cs`
- Create: `Core/Selection/Selection.cs`, `Core/Selection/SelectionService.cs`
- Create: `CoreTests/Support/FakeBackend.cs`
- Test: `CoreTests/Session/SceneLoaderTests.cs`, `CoreTests/Session/SceneDiffTests.cs`, `CoreTests/Selection/SelectionTests.cs`

**Interfaces:**
- Consumes: B5 `GlbModel`, B6 `IInventorBackend`, DTOs, `McpToolException`.
- Produces:
  - `LoadedScene(SceneGraph graph, IReadOnlyDictionary<string, GlbModel> models, IReadOnlyDictionary<string, string> assetIds, IReadOnlyList<string> omitted)`: `Graph`, `Models` (by definition id), `AssetIds` (by definition id), `Omitted`.
  - `SceneLoader(IInventorBackend backend)`, `const MaxDefinitions = 200`, `Task<LoadedScene> LoadAsync(CancellationToken)`. Definitions answering `MESH_TOO_LARGE` or beyond the limit are listed in `Omitted`; any other error propagates.
  - `SceneDiff.Compare(DocumentState before, DocumentState after)` → `DocumentChanged`, `GeometryChanged`, `NeedsReload` (= either).
  - `Backoff` with `TimeSpan Next()` (1, 2, 4, 8, 16, 30, 30 … s) and `Reset()`.
  - `IDelay { Task Delay(TimeSpan, CancellationToken); }`, `TaskDelay : IDelay`.
  - `SelectionKind { None, Occurrence, Face }`; `Selection(SelectionKind kind, string occurrenceId, string faceId, string entityId)` + `static Selection None`.
  - `SelectionService(IInventorBackend backend)`: `Current`, `event Action<Selection> Changed`, `Task SelectAsync(string documentKind, string occurrenceId, string faceId, CancellationToken)`, `Task ClearAsync(CancellationToken)`, `static SelectionKind Resolve(string documentKind, Selection current, string occurrenceId)`.
  - Selection rule (spec §14 + DoD §52): part document → face; assembly → occurrence, but a second click on the occurrence already selected (or on a face of it) selects the face.
  - Test support: `FakeBackend : IInventorBackend` (in-memory, scriptable).

- [ ] **Step 1: Test double** — `CoreTests/Support/FakeBackend.cs`:

```csharp
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Support;

/// <summary>Scriptable in-memory backend for session and selection logic.</summary>
public sealed class FakeBackend : IInventorBackend
{
    public List<string> Calls { get; } = new();
    public CapabilitiesInfo Capabilities { get; set; } = CapabilitiesInfo.FromJson(JObject.Parse(
        @"{""target"":{""reachable"":true},""capabilities"":{""xr_mesh"":true,""scene_graph"":true,""highlight"":true}}"));
    public DocumentState State { get; set; } = new DocumentState("doc", "r1", "v1");
    public Func<SceneGraph> Scene { get; set; }
    public Dictionary<string, byte[]> Meshes { get; } = new();
    public HashSet<string> TooLarge { get; } = new();
    public Exception FailNext { get; set; }
    public Func<string, string, string> Pick { get; set; } = (occ, face) => "proxy:" + occ + ":" + face;
    public Action RaiseChanged { get; private set; }

    private Task Step(string call)
    {
        Calls.Add(call);
        if (FailNext is { } failure) { FailNext = null; throw failure; }
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(CancellationToken ct) => await Step("connect");
    public async Task<CapabilitiesInfo> GetCapabilitiesAsync(CancellationToken ct) { await Step("capabilities"); return Capabilities; }
    public async Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) { await Step("state"); return State; }
    public async Task<SceneGraph> GetSceneGraphAsync(CancellationToken ct) { await Step("scene"); return Scene(); }

    public async Task<DefinitionMesh> GetDefinitionMeshAsync(string id, CancellationToken ct)
    {
        await Step("mesh:" + id);
        if (TooLarge.Contains(id)) throw new McpToolException("inventor_get_display_mesh", "MESH_TOO_LARGE", "too large", null);
        return DefinitionMesh.FromJson(id, new JObject { ["asset"] = new JObject { ["asset_id"] = "a_" + id, ["asset_url"] = "/assets/a_" + id } });
    }

    public async Task<byte[]> GetAssetAsync(DefinitionMesh mesh, CancellationToken ct) { await Step("asset:" + mesh.DefinitionDocumentId); return Meshes[mesh.DefinitionDocumentId]; }
    public async Task<string> PickFaceAsync(string occ, string face, CancellationToken ct) { await Step("pick:" + occ + ":" + face); return Pick(occ, face); }
    public async Task HighlightAsync(IReadOnlyList<string> ids, CancellationToken ct) => await Step("highlight:" + string.Join(",", ids));
    public async Task ClearHighlightAsync(CancellationToken ct) => await Step("clear");

    public async Task RunEventsAsync(Action onChanged, CancellationToken ct)
    {
        await Step("events");
        RaiseChanged = onChanged;
        // A fresh stream per connection, open until the session cancels it.
        var open = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() => open.TrySetCanceled()))
            await open.Task;
    }

    /// <summary>Two-part assembly scene over definitions p and q.</summary>
    public static SceneGraph Assembly(string visual = "v1", string documentId = "doc") => SceneGraph.FromJson(JObject.Parse(
        @"{""document_id"":""" + documentId + @""",""kind"":""assembly"",""revision"":""r1"",""visual_revision"":""" + visual + @""",
          ""definition_document_ids"":[""p"",""q""],
          ""root"":{""name"":""Top.iam"",""definition_kind"":""assembly"",""children"":[
            {""name"":""P:1"",""occurrence_id"":""ent_1"",""definition_document_id"":""p"",""definition_kind"":""part"",""children"":[]},
            {""name"":""Q:1"",""occurrence_id"":""ent_2"",""definition_document_id"":""q"",""definition_kind"":""part"",""children"":[]}]}}"));
}
```

- [ ] **Step 2: Write the failing tests** — `CoreTests/Session/SceneLoaderTests.cs`:

```csharp
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Tests.Glb;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Session;

public class SceneLoaderTests
{
    private static FakeBackend Backend()
    {
        var backend = new FakeBackend { Scene = () => FakeBackend.Assembly() };
        backend.Meshes["p"] = GlbModelTests.BoltGlb();
        backend.Meshes["q"] = GlbModelTests.BoltGlb();
        return backend;
    }

    [Fact]
    public async Task LoadsEveryDefinitionOnce()
    {
        var scene = await new SceneLoader(Backend()).LoadAsync(CancellationToken.None);
        Assert.Equal(new[] { "p", "q" }, scene.Models.Keys.OrderBy(k => k));
        Assert.Equal("a_p", scene.AssetIds["p"]);
        Assert.Empty(scene.Omitted);
    }

    [Fact]
    public async Task ATooLargeMeshIsOmittedNotFatal()
    {
        var backend = Backend();
        backend.TooLarge.Add("q");
        var scene = await new SceneLoader(backend).LoadAsync(CancellationToken.None);
        Assert.Equal(new[] { "p" }, scene.Models.Keys);
        Assert.Equal(new[] { "q" }, scene.Omitted);
    }

    [Fact]
    public async Task OtherToolErrorsPropagate()
    {
        var backend = Backend();
        backend.FailNext = new McpToolException("inventor_get_scene_graph", "NO_DOCUMENT", "none", null);
        await Assert.ThrowsAsync<McpToolException>(() => new SceneLoader(backend).LoadAsync(CancellationToken.None));
    }
}
```

`CoreTests/Session/SceneDiffTests.cs`:

```csharp
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;

namespace InventorXrSo.Core.Tests.Session;

public class SceneDiffTests
{
    [Fact]
    public void NothingBeforeMeansReload() => Assert.True(SceneDiff.Compare(null, new DocumentState("d", "r", "v")).NeedsReload);

    [Fact]
    public void OtherDocumentIsADocumentChange()
    {
        var diff = SceneDiff.Compare(new DocumentState("a", "r", "v"), new DocumentState("b", "r", "v"));
        Assert.True(diff.DocumentChanged);
        Assert.True(diff.NeedsReload);
    }

    [Fact]
    public void VisualRevisionIsAGeometryChange()
    {
        var diff = SceneDiff.Compare(new DocumentState("a", "r1", "v1"), new DocumentState("a", "r2", "v2"));
        Assert.False(diff.DocumentChanged);
        Assert.True(diff.GeometryChanged);
    }

    [Fact]
    public void RevisionOnlyNeedsNoReload() =>
        Assert.False(SceneDiff.Compare(new DocumentState("a", "r1", "v1"), new DocumentState("a", "r2", "v1")).NeedsReload);

    [Fact]
    public void BackoffDoublesUpToThirtySecondsAndResets()
    {
        var backoff = new Backoff();
        Assert.Equal(new[] { 1, 2, 4, 8, 16, 30, 30 }, Enumerable.Range(0, 7).Select(_ => (int)backoff.Next().TotalSeconds));
        backoff.Reset();
        Assert.Equal(1, (int)backoff.Next().TotalSeconds);
    }
}
```

`CoreTests/Selection/SelectionTests.cs`:

```csharp
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Selection;

public class SelectionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public void ResolveFollowsTheInventorDefaults()
    {
        var none = InventorXrSo.Core.Selection.Selection.None;
        Assert.Equal(SelectionKind.Face, SelectionService.Resolve("part", none, null));
        Assert.Equal(SelectionKind.Occurrence, SelectionService.Resolve("assembly", none, "ent_1"));
        var occ = new InventorXrSo.Core.Selection.Selection(SelectionKind.Occurrence, "ent_1", null, "ent_1");
        Assert.Equal(SelectionKind.Face, SelectionService.Resolve("assembly", occ, "ent_1"));
        Assert.Equal(SelectionKind.Occurrence, SelectionService.Resolve("assembly", occ, "ent_2"));
        var face = new InventorXrSo.Core.Selection.Selection(SelectionKind.Face, "ent_1", "f", "proxy");
        Assert.Equal(SelectionKind.Face, SelectionService.Resolve("assembly", face, "ent_1"));
    }

    [Fact]
    public async Task AssemblyClickSelectsTheOccurrenceThenItsFace()
    {
        var backend = new FakeBackend();
        var service = new SelectionService(backend);
        var seen = new List<InventorXrSo.Core.Selection.Selection>();
        service.Changed += seen.Add;

        await service.SelectAsync("assembly", "ent_1", "f3", None);
        Assert.Equal(SelectionKind.Occurrence, service.Current.Kind);
        Assert.Equal("ent_1", service.Current.EntityId);
        Assert.Contains("highlight:ent_1", backend.Calls);

        await service.SelectAsync("assembly", "ent_1", "f3", None);
        Assert.Equal(SelectionKind.Face, service.Current.Kind);
        Assert.Equal("proxy:ent_1:f3", service.Current.EntityId);
        Assert.Equal(new[] { "highlight:ent_1", "clear", "pick:ent_1:f3", "highlight:proxy:ent_1:f3" }, backend.Calls);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task PartFacesNeedNoPick()
    {
        var backend = new FakeBackend();
        var service = new SelectionService(backend);
        await service.SelectAsync("part", null, "ent_face_7", None);
        Assert.Equal("ent_face_7", service.Current.EntityId);
        Assert.Equal(new[] { "highlight:ent_face_7" }, backend.Calls);
    }

    [Fact]
    public async Task AStalePickClearsTheSelectionAndRethrows()
    {
        var backend = new FakeBackend();
        var service = new SelectionService(backend);
        await service.SelectAsync("assembly", "ent_1", "f3", None);
        backend.Pick = (_, _) => throw new McpToolException("inventor_pick_entity", "INVALID_ARGUMENT", "face gone", null);
        await Assert.ThrowsAsync<McpToolException>(() => service.SelectAsync("assembly", "ent_1", "f3", None));
        Assert.Equal(SelectionKind.None, service.Current.Kind);
    }

    [Fact]
    public async Task ClickingNothingClears()
    {
        var backend = new FakeBackend();
        var service = new SelectionService(backend);
        await service.SelectAsync("assembly", "ent_1", "f3", None);
        await service.ClearAsync(None);
        Assert.Equal(SelectionKind.None, service.Current.Kind);
        Assert.Equal("clear", backend.Calls.Last());
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~Session|FullyQualifiedName~Selection"`
Expected: build FAILS (`InventorXrSo.Core.Session` / `.Selection` missing).

- [ ] **Step 4: Session pieces** — `Core/Session/SceneLoader.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Mcp;

namespace InventorXrSo.Core.Session
{
    public sealed class LoadedScene
    {
        public LoadedScene(SceneGraph graph, IReadOnlyDictionary<string, GlbModel> models, IReadOnlyDictionary<string, string> assetIds, IReadOnlyList<string> omitted)
        {
            Graph = graph;
            Models = models;
            AssetIds = assetIds;
            Omitted = omitted;
        }

        public SceneGraph Graph { get; }
        /// <summary>Parsed mesh per definition document id.</summary>
        public IReadOnlyDictionary<string, GlbModel> Models { get; }
        /// <summary>Asset id per definition: unchanged id = unchanged geometry, so the view can reuse its meshes.</summary>
        public IReadOnlyDictionary<string, string> AssetIds { get; }
        /// <summary>Definitions not shown (too large, or beyond <see cref="SceneLoader.MaxDefinitions"/>).</summary>
        public IReadOnlyList<string> Omitted { get; }
    }

    /// <summary>Scene graph plus one mesh per distinct definition (per-definition calls, so one oversized part does not sink the scene).</summary>
    public sealed class SceneLoader
    {
        public const int MaxDefinitions = 200;
        private readonly IInventorBackend _backend;

        public SceneLoader(IInventorBackend backend) { _backend = backend; }

        public async Task<LoadedScene> LoadAsync(CancellationToken ct)
        {
            var graph = await _backend.GetSceneGraphAsync(ct);
            var models = new Dictionary<string, GlbModel>();
            var assetIds = new Dictionary<string, string>();
            var omitted = new List<string>();
            foreach (var id in graph.DefinitionIds)
            {
                if (models.Count >= MaxDefinitions)
                {
                    omitted.Add(id);
                    continue;
                }
                try
                {
                    var mesh = await _backend.GetDefinitionMeshAsync(id, ct);
                    models[id] = GlbModel.Parse(await _backend.GetAssetAsync(mesh, ct));
                    assetIds[id] = mesh.AssetId;
                }
                catch (McpToolException ex) when (ex.Code == "MESH_TOO_LARGE")
                {
                    omitted.Add(id);
                }
            }
            return new LoadedScene(graph, models, assetIds, omitted);
        }
    }
}
```

`Core/Session/SceneDiff.cs`:

```csharp
using InventorXrSo.Core.Backend;

namespace InventorXrSo.Core.Session
{
    public sealed class SceneDiff
    {
        private SceneDiff(bool documentChanged, bool geometryChanged)
        {
            DocumentChanged = documentChanged;
            GeometryChanged = geometryChanged;
        }

        public bool DocumentChanged { get; }
        public bool GeometryChanged { get; }
        public bool NeedsReload => DocumentChanged || GeometryChanged;

        public static SceneDiff Compare(DocumentState before, DocumentState after)
        {
            if (before == null) return new SceneDiff(true, true);
            bool document = before.DocumentId != after.DocumentId;
            return new SceneDiff(document, document || before.VisualRevision != after.VisualRevision);
        }
    }
}
```

`Core/Session/Backoff.cs`:

```csharp
using System;

namespace InventorXrSo.Core.Session
{
    /// <summary>Reconnection delays: 1, 2, 4, 8, 16, then 30 s.</summary>
    public sealed class Backoff
    {
        private static readonly int[] Seconds = { 1, 2, 4, 8, 16, 30 };
        private int _attempt;

        public TimeSpan Next() => TimeSpan.FromSeconds(Seconds[Math.Min(_attempt++, Seconds.Length - 1)]);
        public void Reset() => _attempt = 0;
    }
}
```

`Core/Session/Delay.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace InventorXrSo.Core.Session
{
    /// <summary>Waiting, injectable so tests do not sleep.</summary>
    public interface IDelay
    {
        Task Delay(TimeSpan duration, CancellationToken ct);
    }

    public sealed class TaskDelay : IDelay
    {
        public Task Delay(TimeSpan duration, CancellationToken ct) => Task.Delay(duration, ct);
    }
}
```

- [ ] **Step 5: Selection** — `Core/Selection/Selection.cs`:

```csharp
namespace InventorXrSo.Core.Selection
{
    public enum SelectionKind { None, Occurrence, Face }

    public sealed class Selection
    {
        public static readonly Selection None = new Selection(SelectionKind.None, null, null, null);

        public Selection(SelectionKind kind, string occurrenceId, string faceId, string entityId)
        {
            Kind = kind;
            OccurrenceId = occurrenceId;
            FaceId = faceId;
            EntityId = entityId;
        }

        public SelectionKind Kind { get; }
        /// <summary>Occurrence picked (null in a part document).</summary>
        public string OccurrenceId { get; }
        /// <summary>Definition-local face id from the GLB (for the local highlight).</summary>
        public string FaceId { get; }
        /// <summary>Portable id in the active document (for Inventor); null until resolved.</summary>
        public string EntityId { get; }
    }
}
```

`Core/Selection/SelectionService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;

namespace InventorXrSo.Core.Selection
{
    /// <summary>
    /// Selection following Inventor's defaults (spec §14): faces in a part; occurrences in an assembly,
    /// and a second click on the selected occurrence selects the face under the ray. The local view is
    /// told first, then Inventor highlights the same entity.
    /// </summary>
    public sealed class SelectionService
    {
        private readonly IInventorBackend _backend;

        public SelectionService(IInventorBackend backend) { _backend = backend; }

        public Selection Current { get; private set; } = Selection.None;
        public event Action<Selection> Changed;

        public static SelectionKind Resolve(string documentKind, Selection current, string occurrenceId)
        {
            if (documentKind != "assembly") return SelectionKind.Face;
            bool sameOccurrence = current.Kind != SelectionKind.None && current.OccurrenceId == occurrenceId;
            return sameOccurrence ? SelectionKind.Face : SelectionKind.Occurrence;
        }

        public async Task SelectAsync(string documentKind, string occurrenceId, string faceId, CancellationToken ct)
        {
            var kind = Resolve(documentKind, Current, occurrenceId);
            bool hadServerHighlight = Current.Kind != SelectionKind.None;
            // The view shows the selection at once; Inventor follows after the round trips.
            Set(kind == SelectionKind.Occurrence
                ? new Selection(kind, occurrenceId, null, occurrenceId)
                : new Selection(kind, occurrenceId, faceId, occurrenceId == null ? faceId : null));
            if (hadServerHighlight) await _backend.ClearHighlightAsync(ct);
            if (kind == SelectionKind.Face && occurrenceId != null)
            {
                // Only the entity id was missing: no second Changed event.
                try { Current = new Selection(kind, occurrenceId, faceId, await _backend.PickFaceAsync(occurrenceId, faceId, ct)); }
                catch
                {
                    Set(Selection.None);
                    throw;
                }
            }
            await _backend.HighlightAsync(new[] { Current.EntityId }, ct);
        }

        public async Task ClearAsync(CancellationToken ct)
        {
            if (Current.Kind == SelectionKind.None) return;
            Set(Selection.None);
            await _backend.ClearHighlightAsync(ct);
        }

        private void Set(Selection selection)
        {
            Current = selection;
            Changed?.Invoke(selection);
        }
    }
}
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~Session|FullyQualifiedName~Selection"`
Expected: 14 passed.

- [ ] **Step 7: Commit**

```bash
git add "Inventor XR SO/Packages" "Inventor XR SO/Tests~"
git commit -m "feat(xr): scene loader, change detection and Inventor-style selection"
```

### Task B8: Session controller

**Files:**
- Create: `Core/Session/SessionController.cs`
- Test: `CoreTests/Session/SessionControllerTests.cs`

**Interfaces:**
- Consumes: B6 `IInventorBackend`, B7 `SceneLoader`, `SceneDiff`, `Backoff`, `IDelay`.
- Produces:
  - `SessionStatus { Idle, Connecting, NotReady, NoDocument, Online, Offline, NeedsPairing }`.
  - `SessionController(IInventorBackend backend, IDelay delay)`: `Status`, `ReadOnly` (true unless `Online`), `Capabilities`, `Document`, `Scene` (`LoadedScene`, kept while offline), `LastError`, `PollInterval` (default 5 s), events `StatusChanged(SessionStatus)` and `SceneLoaded(LoadedScene)` (null when the document closed), `Task RunAsync(CancellationToken)` (returns on cancel or `NeedsPairing`), `Task RefreshAsync(CancellationToken)`.
  - Behaviour: connect → capabilities (not XR-ready → `NotReady`, retry every 5 s) → refresh → `Online` → wait for an event or the poll interval → refresh … Any other failure → `Offline`, keep `Scene`, wait `Backoff.Next()`, reconnect. `McpUnauthorizedException` → `NeedsPairing`, stop. Tool codes `NO_DOCUMENT` → `NoDocument` (scene cleared, keep polling). `NO_TARGET`/`TARGET_UNAVAILABLE` → `NotReady`. Event stream ending or `EVENT_STREAM_UNSUPPORTED` → poll only.

- [ ] **Step 1: Write the failing tests** — `CoreTests/Session/SessionControllerTests.cs`:

```csharp
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Tests.Glb;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Session;

public class SessionControllerTests
{
    /// <summary>Delays complete when the test says so; each one is recorded.</summary>
    private sealed class ManualDelay : IDelay
    {
        private readonly List<TaskCompletionSource<bool>> _pending = new();
        public List<TimeSpan> Requested { get; } = new();

        public Task Delay(TimeSpan duration, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) { Requested.Add(duration); _pending.Add(tcs); }
            ct.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        }

        public void ReleaseAll()
        {
            List<TaskCompletionSource<bool>> all;
            lock (_pending) { all = _pending.ToList(); _pending.Clear(); }
            foreach (var t in all) t.TrySetResult(true);
        }
    }

    private static FakeBackend Backend()
    {
        var backend = new FakeBackend { Scene = () => FakeBackend.Assembly() };
        backend.Meshes["p"] = GlbModelTests.BoltGlb();
        backend.Meshes["q"] = GlbModelTests.BoltGlb();
        return backend;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task GoesOnlineWithTheScene()
    {
        var backend = Backend();
        var session = new SessionController(backend, new ManualDelay());
        LoadedScene loaded = null;
        session.SceneLoaded += s => loaded = s;
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online);
        Assert.False(session.ReadOnly);
        Assert.Equal(2, loaded.Models.Count);
        Assert.Equal("v1", session.Document.VisualRevision);
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task AnEventWithNewGeometryReloadsTheScene()
    {
        var backend = Backend();
        var session = new SessionController(backend, new ManualDelay());
        int loads = 0;
        session.SceneLoaded += _ => loads++;
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);

        backend.State = new DocumentState("doc", "r2", "v2");
        backend.Scene = () => FakeBackend.Assembly("v2");
        backend.RaiseChanged();
        await Until(() => loads == 2);

        backend.State = new DocumentState("doc", "r3", "v2");   // save only: no reload
        backend.RaiseChanged();
        await Until(() => backend.Calls.Count(c => c == "state") >= 3);
        Assert.Equal(2, loads);
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task NetworkLossKeepsTheSceneReadOnlyAndReconnects()
    {
        var backend = Backend();
        var delay = new ManualDelay();
        var session = new SessionController(backend, delay);
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);

        backend.FailNext = new TransportException("wifi gone");
        backend.RaiseChanged();
        await Until(() => session.Status == SessionStatus.Offline);
        Assert.True(session.ReadOnly);
        Assert.NotNull(session.Scene);
        Assert.Contains(TimeSpan.FromSeconds(1), delay.Requested);

        delay.ReleaseAll();
        await Until(() => session.Status == SessionStatus.Online);
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task ARevokedTokenStopsTheSessionForPairing()
    {
        var backend = Backend();
        backend.FailNext = new McpUnauthorizedException();
        var session = new SessionController(backend, new ManualDelay());
        await session.RunAsync(CancellationToken.None);
        Assert.Equal(SessionStatus.NeedsPairing, session.Status);
    }

    [Fact]
    public async Task MissingXrToolsMeanNotReady()
    {
        var backend = Backend();
        backend.Capabilities = CapabilitiesInfo.FromJson(JObject.Parse(@"{""target"":{""reachable"":true},""capabilities"":{}}"));
        var session = new SessionController(backend, new ManualDelay());
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.NotReady);
        Assert.DoesNotContain("scene", backend.Calls);
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task NoOpenDocumentClearsTheSceneAndWaits()
    {
        var backend = Backend();
        backend.FailNext = null;
        var session = new SessionController(backend, new ManualDelay());
        var scenes = new List<LoadedScene>();
        session.SceneLoaded += scenes.Add;
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);

        backend.FailNext = new McpToolException("inventor_get_visual_revision", "NO_DOCUMENT", "none open", null);
        backend.RaiseChanged();
        await Until(() => session.Status == SessionStatus.NoDocument);
        Assert.Null(session.Scene);
        Assert.Null(scenes.Last());
        cts.Cancel();
        await run;
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~SessionControllerTests"`
Expected: build FAILS (`SessionController` missing).

- [ ] **Step 3: Implement** — `Core/Session/SessionController.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;

namespace InventorXrSo.Core.Session
{
    public enum SessionStatus { Idle, Connecting, NotReady, NoDocument, Online, Offline, NeedsPairing }

    /// <summary>
    /// The headset's connection to Inventor (spec §5): connect, check capabilities, follow the active
    /// document through events plus a slow poll, and on any failure keep the last scene visible,
    /// read-only, while reconnecting with backoff.
    /// </summary>
    public sealed class SessionController
    {
        private static readonly TimeSpan NotReadyRetry = TimeSpan.FromSeconds(5);
        private readonly IInventorBackend _backend;
        private readonly IDelay _delay;
        private readonly SceneLoader _loader;

        public SessionController(IInventorBackend backend, IDelay delay)
        {
            _backend = backend;
            _delay = delay;
            _loader = new SceneLoader(backend);
        }

        public SessionStatus Status { get; private set; } = SessionStatus.Idle;
        public bool ReadOnly => Status != SessionStatus.Online;
        public CapabilitiesInfo Capabilities { get; private set; }
        public DocumentState Document { get; private set; }
        public LoadedScene Scene { get; private set; }
        public string LastError { get; private set; }
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

        public event Action<SessionStatus> StatusChanged;
        public event Action<LoadedScene> SceneLoaded;

        public async Task RunAsync(CancellationToken ct)
        {
            var backoff = new Backoff();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    SetStatus(SessionStatus.Connecting);
                    await _backend.ConnectAsync(ct);
                    Capabilities = await _backend.GetCapabilitiesAsync(ct);
                    if (!Capabilities.IsXrReady)
                    {
                        SetStatus(SessionStatus.NotReady);
                        await _delay.Delay(NotReadyRetry, ct);
                        continue;
                    }
                    backoff.Reset();
                    await RefreshAsync(ct);
                    await RunConnectedAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (McpUnauthorizedException ex)
                {
                    LastError = ex.Message;
                    SetStatus(SessionStatus.NeedsPairing);
                    return;
                }
                catch (McpToolException ex) when (ex.Code == "NO_TARGET" || ex.Code == "TARGET_UNAVAILABLE")
                {
                    LastError = ex.Message;
                    SetStatus(SessionStatus.NotReady);
                    if (!await Wait(NotReadyRetry, ct)) return;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    SetStatus(SessionStatus.Offline);
                    if (!await Wait(backoff.Next(), ct)) return;
                }
            }
        }

        /// <summary>Re-read the document state; reload the scene only when the document or its geometry changed.</summary>
        public async Task RefreshAsync(CancellationToken ct)
        {
            DocumentState state;
            try { state = await _backend.GetDocumentStateAsync(ct); }
            catch (McpToolException ex) when (ex.Code == "NO_DOCUMENT")
            {
                Document = null;
                if (Scene != null)
                {
                    Scene = null;
                    SceneLoaded?.Invoke(null);
                }
                SetStatus(SessionStatus.NoDocument);
                return;
            }
            if (SceneDiff.Compare(Document, state).NeedsReload || Scene == null)
            {
                var scene = await _loader.LoadAsync(ct);
                Scene = scene;
                Document = scene.Graph.State;
                SceneLoaded?.Invoke(scene);
            }
            else Document = state;
            SetStatus(SessionStatus.Online);
        }

        private async Task RunConnectedAsync(CancellationToken ct)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var changed = new SemaphoreSlim(0);
                Task events = _backend.RunEventsAsync(() => changed.Release(), linked.Token);
                var never = new TaskCompletionSource<bool>().Task;
                try
                {
                    while (true)
                    {
                        var wake = changed.WaitAsync(linked.Token);
                        var poll = _delay.Delay(PollInterval, linked.Token);
                        var done = await Task.WhenAny(wake, poll, events);
                        linked.Token.ThrowIfCancellationRequested();
                        if (done == events)
                        {
                            // A closed or unsupported stream leaves the poll; a broken one means offline.
                            try { await events; }
                            catch (McpException ex) when (ex.Code == "EVENT_STREAM_UNSUPPORTED") { }
                            events = never;
                            continue;
                        }
                        await RefreshAsync(ct);
                    }
                }
                finally
                {
                    linked.Cancel();
                }
            }
        }

        private async Task<bool> Wait(TimeSpan duration, CancellationToken ct)
        {
            try
            {
                await _delay.Delay(duration, ct);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
        }

        private void SetStatus(SessionStatus status)
        {
            if (Status == status) return;
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }
}
```

Note: in `NotReady` from capabilities the `_delay.Delay` inside `try` may throw `OperationCanceledException` on cancel, which the first `catch` handles.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~SessionControllerTests"`
Expected: 6 passed. Then the full core suite: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"` — all pass.

- [ ] **Step 5: Commit**

```bash
git add "Inventor XR SO/Packages" "Inventor XR SO/Tests~"
git commit -m "feat(xr): session controller with events, polling and offline recovery"
```

### Task B9: Test host for Unity and headset work without Inventor

**Files:**
- Create: `Inventor XR SO/Tests~/XrSo.TestHost/XrSo.TestHost.csproj`, `Inventor XR SO/Tests~/XrSo.TestHost/Program.cs`, `Inventor XR SO/Tests~/README.md`

**Interfaces:**
- Consumes: A4 host pieces, `FakeAddIn`.
- Produces: console `XrSo.TestHost` — options `--lan` (bind `https://0.0.0.0:8443`, QR host = LAN IP) else `https://127.0.0.1:0`; `--state <dir>` (default: temp). Prints **one JSON line on stdout** when ready: `{"ready":true,"base_url":"https://127.0.0.1:53211","cert_sha256":"…","editor_token":"…","pair_code":"123456","qr_payload":"{…}"}`, human pairing info (QR) on stderr. Pairing window 30 min, client name `quest3`. Every 20 s with `--churn`, calls `FakeAddIn.RaiseDocumentChanged(true)` to exercise refresh. Runs until stdin closes or Ctrl+C.

- [ ] **Step 1: Project** — `XrSo.TestHost.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <NoWarn>$(NoWarn);CS8632</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <ProjectReference Include="..\..\..\bridge\src\server\Bimwright.Ipt.Server.csproj" />
    <ProjectReference Include="..\..\..\bridge\src\server-http\Inventor.So.Mcp.Http.csproj" />
    <Compile Include="..\..\..\bridge\tests\Bimwright.Ipt.Tests\FakeAddIn.cs" Link="Bridge\FakeAddIn.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Program** — `Program.cs`:

```csharp
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Tests;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Inventor XR SO test host: the real HTTPS host and pairing in front of FakeAddIn (two bolts and a
// plate), so the Unity client and the headset can be developed without Inventor.
bool lan = args.Contains("--lan");
bool churn = args.Contains("--churn");
int stateIndex = Array.IndexOf(args, "--state");
string state = stateIndex >= 0 && stateIndex + 1 < args.Length ? args[stateIndex + 1] : Path.Combine(Path.GetTempPath(), "xrso-testhost");
Directory.CreateDirectory(state);

await using var addIn = new FakeAddIn(Path.Combine(state, "targets-" + Environment.ProcessId));
var tokens = new TokenRegistry();
var editorToken = TokenRegistry.Generate();
tokens.Add("editor", editorToken);
var config = new InventorMcpConfig
{
    HttpUrls = { lan ? "https://0.0.0.0:8443" : "https://127.0.0.1:0" },
    HttpSelfSignedCertificate = true,
    HttpSelfSignedPath = Path.Combine(state, "server.pfx"),
    HttpTokenFile = Path.Combine(state, "tokens-" + Environment.ProcessId + ".txt"),
    DescriptorDirectory = addIn.DescriptorDirectory,
    AssetDirectory = Path.Combine(state, "assets"),
    AuditDirectory = Path.Combine(state, "audit"),
    EnableExperimental = true,
};
var certificate = PairingSetup.ResolveCertificate(config);
var sha = SelfSignedCertificate.Sha256Hex(certificate);
var store = new PairingStore(() => DateTimeOffset.UtcNow);
var window = store.Open("quest3", TimeSpan.FromMinutes(30));
var app = HttpHost.Build(Array.Empty<string>(), config, tokens,
    new HostOptions { Certificate = certificate, Pairing = new PairingEndpoint(store, tokens, config.HttpTokenFile) });
await app.StartAsync();

var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First());
var host = lan ? PairingSetup.LanAddresses().FirstOrDefault() ?? "127.0.0.1" : "127.0.0.1";
var baseUrl = "https://" + host + ":" + address.Port;
Console.OutputEncoding = System.Text.Encoding.UTF8;
PairingSetup.Announce(Console.Error, window, host, address.Port, sha, Path.Combine(state, "pairing-qr.png"));
Console.Out.WriteLine(new JObject
{
    ["ready"] = true, ["base_url"] = baseUrl, ["cert_sha256"] = sha, ["editor_token"] = editorToken,
    ["pair_code"] = window.Code, ["qr_payload"] = PairingSetup.QrPayload(host, address.Port, window.OneTimeToken, sha),
}.ToString(Formatting.None));
Console.Out.Flush();

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
_ = Task.Run(() => { while (Console.In.Read() >= 0) { } stop.Cancel(); });
if (churn)
    _ = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stop.Token).ContinueWith(_ => { });
            if (!stop.IsCancellationRequested) addIn.RaiseDocumentChanged(true);
        }
    });
try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
await app.StopAsync();
```

- [ ] **Step 3: Readme** — `Inventor XR SO/Tests~/README.md`:

````markdown
# Inventor XR SO — .NET side

- `XrSo.Core` compiles the Unity core package as netstandard2.1 / C# 9 (what Unity accepts).
- `XrSo.Core.Tests` runs the core against the real HTTPS host and `FakeAddIn`:
  `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"`.
  Regenerate the Unity GLB fixture after an intentional `GlbBuilder` change with `XRSO_UPDATE_FIXTURES=1`.
- `XrSo.TestHost` serves a fake assembly without Inventor:
  `dotnet run --project "Inventor XR SO/Tests~/XrSo.TestHost"` (loopback, for the Editor) or add `--lan`
  (port 8443, for the headset; Windows Firewall must allow inbound TCP 8443) and `--churn` (a geometry
  change every 20 s). The first stdout line is JSON with `base_url`, `cert_sha256`, `editor_token`,
  `pair_code`, `qr_payload`.
````

- [ ] **Step 4: Verify**

```powershell
dotnet build "Inventor XR SO/Tests~/XrSo.TestHost"
dotnet run --project "Inventor XR SO/Tests~/XrSo.TestHost"
```

Expected: stderr shows the QR and code; stdout's first line is the JSON with `"ready":true`. Stop with Ctrl+C.

- [ ] **Step 5: Commit**

```bash
git add "Inventor XR SO/Tests~"
git commit -m "feat(xr): test host serving a fake assembly over pinned HTTPS"
```


---

## Part C — Unity app

Prerequisite: Unity Hub → Installs → 6000.6.3f1 → **Android Build Support** (with OpenJDK and Android SDK & NDK Tools) installed. Check:

```powershell
Test-Path "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Data\PlaybackEngines\AndroidPlayer"
```

Expected: `True`. If `False`, stop and ask the user to finish the module install.

Batch runs go through the helper created in C1 (`Unity.exe` is a GUI program: PowerShell must wait for it explicitly). **Close the Unity Editor before any batch run.** Steps marked **(human, Editor UI)** cannot be scripted reliably; the executor asks the user to perform them and to confirm.

### Task C1: Unity project, packages, player settings

**Files:**
- Create: `Inventor XR SO/Tools~/Invoke-Unity.ps1`
- Create: `Inventor XR SO/Assets/XrSo/Editor/Bootstrap/InventorXrSo.Bootstrap.asmdef`, `XrSoBootstrap.cs`
- Create: `Inventor XR SO/Assets/XrSo/Editor/Setup/InventorXrSo.Editor.asmdef`, `XrSoProjectSetup.cs`
- Create: `Inventor XR SO/Assets/XrSo/Tests/EditMode/InventorXrSo.Tests.EditMode.asmdef`, `CoreInUnityTests.cs`
- Generated by Unity and committed: `Inventor XR SO/Packages/manifest.json`, `packages-lock.json`, `ProjectSettings/**`, `.meta` files, `Assets/XrSo/Settings/*`, `Assets/XrSo/Materials/*`

**Interfaces:**
- Produces: `Invoke-Unity.ps1 -Arguments <string[]> [-Log <path>]` (exit code = Unity's); menu `Inventor XR SO/Configure Project` (`InventorXrSo.Editor.XrSoProjectSetup.Configure`, batch entry `ConfigureBatch`); materials `Assets/XrSo/Materials/CadBody.mat`, `OccurrenceHighlight.mat`; URP asset `Assets/XrSo/Settings/XrSoUrp.asset`.

- [ ] **Step 1: Batch helper** — `Inventor XR SO/Tools~/Invoke-Unity.ps1`:

```powershell
# Runs Unity 6000.6.3f1 in batch mode on this project and waits for it (Unity.exe is a GUI program).
param(
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [string]$Log = (Join-Path $env:TEMP "xrso-unity.log")
)
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe"
$project = Split-Path -Parent $PSScriptRoot
$all = @("-batchmode", "-projectPath", "`"$project`"", "-logFile", "`"$Log`"") + $Arguments
$process = Start-Process -FilePath $unity -ArgumentList $all -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) { Get-Content $Log -Tail 80 }
exit $process.ExitCode
```

- [ ] **Step 2: Bootstrap editor script** (no package dependencies, so it compiles on a bare project) — `Assets/XrSo/Editor/Bootstrap/InventorXrSo.Bootstrap.asmdef`:

```json
{
  "name": "InventorXrSo.Bootstrap",
  "rootNamespace": "InventorXrSo.Bootstrap",
  "references": [],
  "includePlatforms": ["Editor"],
  "autoReferenced": false
}
```

`Assets/XrSo/Editor/Bootstrap/XrSoBootstrap.cs`:

```csharp
using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace InventorXrSo.Bootstrap
{
    /// <summary>Adds the packages the app needs (latest versions compatible with this editor).</summary>
    public static class XrSoBootstrap
    {
        public static readonly string[] Packages =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.xr.management",
            "com.unity.xr.oculus",
            "com.unity.test-framework",
            "com.unity.ugui",
            "com.meta.xr.sdk.core",
        };

        private static AddAndRemoveRequest _request;

        /// <summary>Batch entry: -executeMethod InventorXrSo.Bootstrap.XrSoBootstrap.AddPackages (no -quit).</summary>
        public static void AddPackages()
        {
            _request = Client.AddAndRemove(Packages, null);
            EditorApplication.update += Wait;
        }

        private static void Wait()
        {
            if (!_request.IsCompleted) return;
            EditorApplication.update -= Wait;
            if (_request.Status == StatusCode.Success)
            {
                foreach (var package in _request.Result) Debug.Log("XRSO package " + package.name + " " + package.version);
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("XRSO package install failed: " + _request.Error?.message);
                EditorApplication.Exit(1);
            }
        }
    }
}
```

- [ ] **Step 3: First open** (creates `Packages/manifest.json`, `ProjectSettings/`, resolves Newtonsoft through the core package):

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-quit"
```

Expected: exit code 0; `Inventor XR SO/Packages/manifest.json` exists; the log has no `error CS`.

- [ ] **Step 4: Meta scoped registry** — add it to the generated manifest:

```powershell
$path = "Inventor XR SO/Packages/manifest.json"
$manifest = Get-Content $path -Raw | ConvertFrom-Json
$registry = [pscustomobject]@{ name = "Meta XR"; url = "https://npm.developer.oculus.com"; scopes = @("com.meta.xr") }
$manifest | Add-Member -NotePropertyName scopedRegistries -NotePropertyValue @($registry) -Force
$manifest | ConvertTo-Json -Depth 10 | Set-Content $path -Encoding utf8
```

- [ ] **Step 5: Install packages**

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Bootstrap.XrSoBootstrap.AddPackages"
```

Expected: exit code 0 and one `XRSO package …` log line per package. If `com.meta.xr.sdk.core` fails to resolve, stop and report the log lines (registry unreachable or package renamed); do not substitute another package.

- [ ] **Step 6: Project setup script** — `Assets/XrSo/Editor/Setup/InventorXrSo.Editor.asmdef`:

```json
{
  "name": "InventorXrSo.Editor",
  "rootNamespace": "InventorXrSo.Editor",
  "references": ["Unity.RenderPipelines.Universal.Runtime", "Unity.RenderPipelines.Core.Runtime"],
  "includePlatforms": ["Editor"],
  "autoReferenced": false
}
```

`Assets/XrSo/Editor/Setup/XrSoProjectSetup.cs`:

```csharp
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InventorXrSo.Editor
{
    /// <summary>Quest 3 player settings, URP and the shared materials. Safe to run again.</summary>
    public static class XrSoProjectSetup
    {
        public const string MaterialsFolder = "Assets/XrSo/Materials";
        private const string SettingsFolder = "Assets/XrSo/Settings";

        [MenuItem("Inventor XR SO/Configure Project")]
        public static void Configure()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = "Occhipinti";
            PlayerSettings.productName = "Inventor XR SO";
            PlayerSettings.SetApplicationIdentifier(android, "com.occhipinti.inventorxrso");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            ConfigureUrp();
            CreateMaterials();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Batch entry: -executeMethod InventorXrSo.Editor.XrSoProjectSetup.ConfigureBatch</summary>
        public static void ConfigureBatch()
        {
            try
            {
                Configure();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void ConfigureUrp()
        {
            var path = SettingsFolder + "/XrSoUrp.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                Directory.CreateDirectory(SettingsFolder);
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, SettingsFolder + "/XrSoUrpRenderer.asset");
                asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.msaaSampleCount = 4;
                AssetDatabase.CreateAsset(asset, path);
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>Creates the materials whose shader is available (the face overlay shader arrives in Task C4).</summary>
        public static void CreateMaterials()
        {
            Directory.CreateDirectory(MaterialsFolder);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Create("CadBody", lit, m => m.SetColor("_BaseColor", new Color(0.72f, 0.74f, 0.77f)));
            Create("OccurrenceHighlight", lit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.25f, 0.55f, 1f));
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.05f, 0.15f, 0.35f));
            });
            Create("FaceHighlight", Shader.Find("XrSo/HighlightOverlay"), m => m.SetColor("_Color", new Color(1f, 0.6f, 0.1f, 0.6f)));
        }

        private static void Create(string name, Shader shader, Action<Material> setup)
        {
            var path = MaterialsFolder + "/" + name + ".mat";
            if (shader == null || AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var material = new Material(shader) { name = name };
            setup(material);
            AssetDatabase.CreateAsset(material, path);
        }
    }
}
```

- [ ] **Step 7: Run the setup**

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoProjectSetup.ConfigureBatch"
```

Expected: exit code 0; `Assets/XrSo/Materials/CadBody.mat`, `OccurrenceHighlight.mat` and `Assets/XrSo/Settings/XrSoUrp.asset` exist.

- [ ] **Step 8: (human, Editor UI) XR plug-in and Meta setup** — ask the user to open the project in Unity 6000.6.3f1 and:
  1. *Edit → Project Settings → XR Plug-in Management*, Android tab: tick **Oculus**.
  2. *Meta → Tools → Project Setup Tool*, Android: **Fix All**, then **Apply All** for recommended items.
  3. Select nothing else; *File → Save Project*, close the Editor.

  Then commit what changed under `ProjectSettings/`, `Assets/XR/`, `Assets/Oculus/`, `Assets/Resources/` (if created).

- [ ] **Step 9: Write the EditMode smoke test** — `Assets/XrSo/Tests/EditMode/InventorXrSo.Tests.EditMode.asmdef`:

```json
{
  "name": "InventorXrSo.Tests.EditMode",
  "rootNamespace": "InventorXrSo.Tests",
  "references": ["InventorXrSo.Core", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"]
}
```

`Assets/XrSo/Tests/EditMode/CoreInUnityTests.cs`:

```csharp
using System.IO;
using InventorXrSo.Core.Glb;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public class CoreInUnityTests
    {
        public const string BoltFixture = "Assets/XrSo/Tests/EditMode/Fixtures/bolt-1cm.glb.bytes";

        [Test]
        public void TheCorePackageParsesTheServerGlbInsideUnity()
        {
            var model = GlbModel.Parse(File.ReadAllBytes(BoltFixture));
            Assert.AreEqual(1, model.Primitives.Count);
            Assert.AreEqual(12, model.Primitives[0].TriangleCount);
            Assert.AreEqual("ent_doc_bolt_f4", model.Primitives[0].FaceMap.FaceAtTriangle(6).FaceId);
        }
    }
}
```

- [ ] **Step 10: Run the EditMode tests**

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode.xml`""
```

Expected: exit code 0; `xrso-editmode.xml` reports 1 passed.

- [ ] **Step 11: Commit**

```bash
git add "Inventor XR SO"
git status --short "Inventor XR SO"   # confirm no Library/, Temp/, Logs/ or *.csproj at the root
git commit -m "feat(xr): Unity project for Quest 3 with URP, Meta XR and core package"
```

### Task C2: UnityWebRequest transport with pinning and streaming

**Files:**
- Create: `Assets/XrSo/Runtime/InventorXrSo.Unity.asmdef`
- Create: `Assets/XrSo/Runtime/Net/UnityHttpTransport.cs`, `Assets/XrSo/Runtime/Net/PinningCertificateHandler.cs`, `Assets/XrSo/Runtime/Net/LineDownloadHandler.cs`
- Modify: `Assets/XrSo/Tests/EditMode/InventorXrSo.Tests.EditMode.asmdef` (add `InventorXrSo.Unity`)
- Modify: `Inventor XR SO/Tests~/XrSo.TestHost/Program.cs` (`--churn <seconds>`)
- Test: `Assets/XrSo/Tests/EditMode/TestHostProcess.cs`, `Assets/XrSo/Tests/EditMode/UnityHttpTransportTests.cs`

**Interfaces:**
- Consumes: B2 `IHttpTransport`, `ServerTrust`, `CertificateRejectedException`, `TransportException`; B3/B4 `McpClient`; B9 test host.
- Produces: `UnityHttpTransport(ServerTrust trust) : IHttpTransport` (main thread only).

- [ ] **Step 1: Test host churn interval** — in `XrSo.TestHost/Program.cs` replace `bool churn = args.Contains("--churn");` with:

```csharp
int churnIndex = Array.IndexOf(args, "--churn");
int churnSeconds = churnIndex >= 0 && churnIndex + 1 < args.Length && int.TryParse(args[churnIndex + 1], out var s) ? s : 0;
```

and the churn block's condition and delay with `if (churnSeconds > 0)` and `TimeSpan.FromSeconds(churnSeconds)`. Update `Tests~/README.md`: `--churn <seconds>`. Run `dotnet build "Inventor XR SO/Tests~/XrSo.TestHost"`.

- [ ] **Step 2: Runtime assembly** — `Assets/XrSo/Runtime/InventorXrSo.Unity.asmdef`:

```json
{
  "name": "InventorXrSo.Unity",
  "rootNamespace": "InventorXrSo.Unity",
  "references": ["InventorXrSo.Core"],
  "includePlatforms": [],
  "autoReferenced": true
}
```

Add `"InventorXrSo.Unity"` to the `references` of the EditMode test asmdef.

- [ ] **Step 3: Write the failing tests** — `Assets/XrSo/Tests/EditMode/TestHostProcess.cs`:

```csharp
using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    /// <summary>Starts Tests~/XrSo.TestHost (FakeAddIn behind the real HTTPS host) for one test.</summary>
    internal sealed class TestHostProcess : IDisposable
    {
        private readonly Process _process;

        private TestHostProcess(Process process, JObject ready)
        {
            _process = process;
            BaseUrl = (string)ready["base_url"];
            CertSha256 = (string)ready["cert_sha256"];
            EditorToken = (string)ready["editor_token"];
            PairCode = (string)ready["pair_code"];
            QrPayload = (string)ready["qr_payload"];
        }

        public string BaseUrl { get; }
        public string CertSha256 { get; }
        public string EditorToken { get; }
        public string PairCode { get; }
        public string QrPayload { get; }

        public static TestHostProcess Start(params string[] extraArgs)
        {
            var dll = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tests~", "XrSo.TestHost", "bin", "Debug", "net8.0", "XrSo.TestHost.dll"));
            if (!File.Exists(dll)) Assert.Ignore("Build the test host first: dotnet build \"Inventor XR SO/Tests~/XrSo.TestHost\"");
            var info = new ProcessStartInfo("dotnet", "\"" + dll + "\" " + string.Join(" ", extraArgs))
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            var process = Process.Start(info);
            process.ErrorDataReceived += (_, __) => { };
            process.BeginErrorReadLine();
            var line = process.StandardOutput.ReadLine();
            if (line == null) throw new InvalidOperationException("The test host exited before it was ready.");
            return new TestHostProcess(process, JObject.Parse(line));
        }

        public void Dispose()
        {
            try
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(5000)) _process.Kill();
            }
            catch (InvalidOperationException) { }
        }
    }
}
```

`Assets/XrSo/Tests/EditMode/UnityHttpTransportTests.cs`:

```csharp
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Unity.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace InventorXrSo.Tests
{
    public class UnityHttpTransportTests
    {
        private static IEnumerator Await(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "timed out");
        }

        [UnityTest]
        public IEnumerator APinnedConnectionRunsAToolCall()
        {
            using (var host = TestHostProcess.Start())
            {
                var mcp = new McpClient(new UnityHttpTransport(ServerTrust.Pinned(host.CertSha256)), host.BaseUrl, host.EditorToken);
                var task = Call(mcp);
                yield return Await(task);
                Assert.IsNull(task.Exception, task.Exception?.ToString());
                Assert.IsTrue((bool)task.Result["capabilities"]["xr_mesh"]);
            }
        }

        [UnityTest]
        public IEnumerator AnotherCertificateIsRejected()
        {
            using (var host = TestHostProcess.Start())
            {
                var mcp = new McpClient(new UnityHttpTransport(ServerTrust.Pinned(new string('0', 64))), host.BaseUrl, host.EditorToken);
                var task = Call(mcp);
                yield return Await(task);
                Assert.IsInstanceOf<CertificateRejectedException>(task.Exception?.GetBaseException());
            }
        }

        [UnityTest]
        public IEnumerator TheEventStreamDeliversUpdates()
        {
            using (var host = TestHostProcess.Start("--churn", "2"))
            {
                var mcp = new McpClient(new UnityHttpTransport(ServerTrust.Pinned(host.CertSha256)), host.BaseUrl, host.EditorToken);
                var init = Subscribe(mcp);
                yield return Await(init);
                Assert.IsNull(init.Exception, init.Exception?.ToString());
                string uri = null;
                var cts = new CancellationTokenSource();
                var stream = mcp.RunEventStreamAsync(u => uri = u, cts.Token);
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (uri == null && DateTime.UtcNow < deadline) yield return null;
                cts.Cancel();
                Assert.AreEqual("inventor://active-document", uri);
                yield return Await(stream.ContinueWith(_ => { }));
            }
        }

        private static async Task<JObject> Call(McpClient mcp)
        {
            await mcp.InitializeAsync(CancellationToken.None);
            return await mcp.CallToolAsync("inventor_get_capabilities", new JObject(), CancellationToken.None);
        }

        private static async Task Subscribe(McpClient mcp)
        {
            await mcp.InitializeAsync(CancellationToken.None);
            await mcp.SubscribeAsync("inventor://active-document", CancellationToken.None);
        }
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run the EditMode command from C1 Step 10.
Expected: compile error `The type or namespace name 'Net' does not exist in the namespace 'InventorXrSo.Unity'` (non-zero exit, log shows it).

- [ ] **Step 5: Implement** — `Assets/XrSo/Runtime/Net/PinningCertificateHandler.cs`:

```csharp
using InventorXrSo.Core.Net;
using UnityEngine.Networking;

namespace InventorXrSo.Unity.Net
{
    /// <summary>Accepts only the certificate the trust pins (hostname is not checked: the pin is the identity).</summary>
    internal sealed class PinningCertificateHandler : CertificateHandler
    {
        private readonly ServerTrust _trust;
        public PinningCertificateHandler(ServerTrust trust) { _trust = trust; }
        protected override bool ValidateCertificate(byte[] certificateData) => _trust.Validate(certificateData);
    }
}
```

`Assets/XrSo/Runtime/Net/LineDownloadHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace InventorXrSo.Unity.Net
{
    /// <summary>Hands each received line to a callback as bytes arrive (for SSE); stores nothing.</summary>
    internal sealed class LineDownloadHandler : DownloadHandlerScript
    {
        private readonly Action<string> _onLine;
        private readonly List<byte> _pending = new List<byte>();

        public LineDownloadHandler(Action<string> onLine) : base(new byte[16 * 1024]) { _onLine = onLine; }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            for (int i = 0; i < dataLength; i++)
            {
                if (data[i] == (byte)'\n')
                {
                    _onLine(Encoding.UTF8.GetString(_pending.ToArray()));
                    _pending.Clear();
                }
                else _pending.Add(data[i]);
            }
            return true;
        }

        protected override void CompleteContent()
        {
            if (_pending.Count == 0) return;
            _onLine(Encoding.UTF8.GetString(_pending.ToArray()));
            _pending.Clear();
        }
    }
}
```

`Assets/XrSo/Runtime/Net/UnityHttpTransport.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using UnityEngine.Networking;

namespace InventorXrSo.Unity.Net
{
    /// <summary>
    /// The core's HTTPS transport on UnityWebRequest (works on Quest/IL2CPP), pinned by a
    /// <see cref="ServerTrust"/>. Call from the main thread; completions come back on it.
    /// </summary>
    public sealed class UnityHttpTransport : IHttpTransport
    {
        private readonly ServerTrust _trust;

        public UnityHttpTransport(ServerTrust trust) { _trust = trust; }

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
        {
            var web = Create(request, new DownloadHandlerBuffer());
            var tcs = new TaskCompletionSource<TransportResponse>();
            var registration = ct.Register(() => web.Abort());
            web.SendWebRequest().completed += _ =>
            {
                registration.Dispose();
                try
                {
                    if (ct.IsCancellationRequested) tcs.TrySetCanceled(ct);
                    else if (web.result == UnityWebRequest.Result.ConnectionError) tcs.TrySetException(Failure(web));
                    else tcs.TrySetResult(new TransportResponse((int)web.responseCode, Headers(web), web.downloadHandler.data));
                }
                finally { web.Dispose(); }
            };
            return tcs.Task;
        }

        public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct)
        {
            var web = Create(request, new LineDownloadHandler(onLine));
            var tcs = new TaskCompletionSource<int>();
            var registration = ct.Register(() => web.Abort());
            web.SendWebRequest().completed += _ =>
            {
                registration.Dispose();
                try
                {
                    if (ct.IsCancellationRequested) tcs.TrySetCanceled(ct);
                    else if (web.result == UnityWebRequest.Result.ConnectionError) tcs.TrySetException(Failure(web));
                    else tcs.TrySetResult((int)web.responseCode);
                }
                finally { web.Dispose(); }
            };
            return tcs.Task;
        }

        private UnityWebRequest Create(TransportRequest request, DownloadHandler download)
        {
            var web = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = download,
                certificateHandler = new PinningCertificateHandler(_trust),
                disposeCertificateHandlerOnDispose = true,
                disposeDownloadHandlerOnDispose = true,
                disposeUploadHandlerOnDispose = true,
                timeout = request.Timeout == Timeout.InfiniteTimeSpan ? 0 : Math.Max(1, (int)Math.Ceiling(request.Timeout.TotalSeconds)),
            };
            if (request.Body != null) web.uploadHandler = new UploadHandlerRaw(request.Body);
            foreach (var header in request.Headers) web.SetRequestHeader(header.Key, header.Value);
            return web;
        }

        private Exception Failure(UnityWebRequest web) =>
            _trust.LastRejected ? new CertificateRejectedException(_trust.LastPresentedSha256) : (Exception)new TransportException(web.error);

        private static IDictionary<string, string> Headers(UnityWebRequest web) =>
            web.GetResponseHeaders() ?? new Dictionary<string, string>();
    }
}
```

- [ ] **Step 6: Run to verify pass**

```powershell
dotnet build "Inventor XR SO/Tests~/XrSo.TestHost"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode.xml`""
```

Expected: exit code 0; 4 passed. If `AnotherCertificateIsRejected` sees a `TransportException` instead, UnityWebRequest did not call the handler (check `LastPresentedSha256` in the log) — report it; do not relax the assertion.

- [ ] **Step 7: Commit**

```bash
git add "Inventor XR SO/Assets/XrSo" "Inventor XR SO/Tests~"
git commit -m "feat(xr): pinned UnityWebRequest transport with SSE streaming"
```

### Task C3: Building the CAD scene

**Files:**
- Create: `Assets/XrSo/Runtime/Scene/MeshFactory.cs`, `MatrixUtil.cs`, `CadInstance.cs`, `CadBody.cs`, `CadSceneView.cs`, `ScenePlacement.cs`
- Test: `Assets/XrSo/Tests/EditMode/CadSceneViewTests.cs`

**Interfaces:**
- Consumes: B5 `GlbModel`, `GlbPrimitive`, `FaceRange`, `Handedness`; B7 `LoadedScene`; B6 `SceneGraph`.
- Produces:
  - `MeshFactory.Build(GlbPrimitive) : Mesh` (Unity handedness, UInt32 indices, one submesh), `MeshFactory.BuildFaceOverlay(Mesh source, FaceRange range) : Mesh`.
  - `MatrixUtil.Apply(Transform, float[] unityColumnMajor)`.
  - `CadInstance : MonoBehaviour` — `OccurrenceId`, `DefinitionId`, `IReadOnlyList<CadBody> Bodies`, `Init(string occurrenceId, string definitionId)`.
  - `CadBody : MonoBehaviour` — `Instance`, `Primitive`, `Mesh`, `Renderer`, `Init(CadInstance, GlbPrimitive, Mesh)`.
  - `CadSceneView : MonoBehaviour` — `BodyMaterial` (get/set), `Instances`, `Show(LoadedScene)` (null clears), `Find(string occurrenceId) : CadInstance`, `event Action Rebuilt`, meshes reused by asset id.
  - `ScenePlacement.LocalBounds(Transform root) : Bounds`, `ScenePlacement.InFront(Bounds localBounds, Vector3 headPosition, Vector3 headForward) : Pose`.

- [ ] **Step 1: Write the failing tests** — `Assets/XrSo/Tests/EditMode/CadSceneViewTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class CadSceneViewTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        internal static LoadedScene BoltScene()
        {
            var bolt = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture));
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_asm"",""kind"":""assembly"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],
                ""root"":{""name"":""Fake.iam"",""definition_kind"":""assembly"",""children"":[
                  {""name"":""Bolt:1"",""occurrence_id"":""ent_occ_1"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",""children"":[]},
                  {""name"":""Bolt:2"",""occurrence_id"":""ent_occ_2"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",
                   ""matrix_gltf"":[1,0,0,0,0,1,0,0,0,0,1,0,0.03,0,0,1],""children"":[]}]}}"));
            return new LoadedScene(graph,
                new Dictionary<string, GlbModel> { ["doc_bolt"] = bolt },
                new Dictionary<string, string> { ["doc_bolt"] = "a_bolt" },
                new List<string>());
        }

        private CadSceneView View()
        {
            _root = new GameObject("scene");
            var view = _root.AddComponent<CadSceneView>();
            view.BodyMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            return view;
        }

        [Test]
        public void MeshesAreMirroredIntoUnitySpace()
        {
            var mesh = MeshFactory.Build(GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture)).Primitives[0]);
            Assert.AreEqual(24, mesh.vertexCount);
            Assert.AreEqual(36, mesh.GetIndices(0).Length);
            Assert.LessOrEqual(mesh.bounds.max.x, 0.0001f);
            Assert.AreEqual(-0.01f, mesh.bounds.min.x, 1e-5f);
        }

        [Test]
        public void EveryOccurrenceIsPlacedAndInstancesShareTheMesh()
        {
            var view = View();
            view.Show(BoltScene());
            Assert.AreEqual(2, view.Instances.Count);
            Assert.AreEqual(-0.03f, view.Find("ent_occ_2").transform.localPosition.x, 1e-5f);
            Assert.AreSame(view.Find("ent_occ_1").Bodies[0].Mesh, view.Find("ent_occ_2").Bodies[0].Mesh);
        }

        [Test]
        public void ARayOnTheTopFaceFindsTheInventorFace()
        {
            var view = View();
            view.Show(BoltScene());
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(new Vector3(-0.005f, 1f, 0.005f), Vector3.down, out var hit, 5f));
            var body = hit.collider.GetComponent<CadBody>();
            Assert.AreEqual("ent_occ_1", body.Instance.OccurrenceId);
            Assert.AreEqual("ent_doc_bolt_f4", body.Primitive.FaceMap.FaceAtTriangle(hit.triangleIndex).FaceId);
        }

        [Test]
        public void ShowingTheSameAssetAgainReusesItsMesh()
        {
            var view = View();
            view.Show(BoltScene());
            var before = view.Find("ent_occ_1").Bodies[0].Mesh;
            view.Show(BoltScene());
            Assert.AreSame(before, view.Find("ent_occ_1").Bodies[0].Mesh);
            view.Show(null);
            Assert.AreEqual(0, view.Instances.Count);
        }

        [Test]
        public void TheModelLandsInFrontOfTheHeadAtOneToOne()
        {
            var pose = ScenePlacement.InFront(new Bounds(Vector3.zero, Vector3.one * 0.1f), new Vector3(0, 1.6f, 0), new Vector3(0, -1, 1));
            Assert.AreEqual(0f, pose.position.x, 1e-4f);
            Assert.AreEqual(1.4f, pose.position.y, 1e-4f);
            Assert.AreEqual(1.0f, pose.position.z, 1e-4f);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure** — EditMode command from C1 Step 10. Expected: compile error, `InventorXrSo.Unity.Scene` missing.

- [ ] **Step 3: Implement** — `Assets/XrSo/Runtime/Scene/MeshFactory.cs`:

```csharp
using System;
using InventorXrSo.Core.Glb;
using UnityEngine;
using UnityEngine.Rendering;

namespace InventorXrSo.Unity.Scene
{
    public static class MeshFactory
    {
        /// <summary>One Unity mesh per body: X mirrored, winding reversed, triangle order kept (face ranges stay valid).</summary>
        public static Mesh Build(GlbPrimitive primitive)
        {
            var positions = Handedness.FlipX(primitive.Positions);
            var normals = Handedness.FlipX(primitive.Normals);
            var indices = Handedness.ReverseWinding(primitive.Indices);
            var mesh = new Mesh { name = primitive.BodyName, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(ToVectors(positions));
            if (normals.Length == positions.Length) mesh.SetNormals(ToVectors(normals));
            var ints = new int[indices.Length];
            for (int i = 0; i < ints.Length; i++) ints[i] = (int)indices[i];
            mesh.SetIndices(ints, MeshTopology.Triangles, 0, true);
            if (normals.Length != positions.Length) mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>The triangles of one face over the same vertices, for the highlight overlay.</summary>
        public static Mesh BuildFaceOverlay(Mesh source, FaceRange range)
        {
            var all = source.GetIndices(0);
            var slice = new int[range.IndexCount];
            Array.Copy(all, range.FirstIndex, slice, 0, range.IndexCount);
            var overlay = new Mesh { name = source.name + " face", indexFormat = source.indexFormat };
            overlay.SetVertices(source.vertices);
            overlay.SetNormals(source.normals);
            overlay.SetIndices(slice, MeshTopology.Triangles, 0, true);
            return overlay;
        }

        private static Vector3[] ToVectors(float[] xyz)
        {
            var vectors = new Vector3[xyz.Length / 3];
            for (int i = 0; i < vectors.Length; i++) vectors[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
            return vectors;
        }
    }
}
```

`Assets/XrSo/Runtime/Scene/MatrixUtil.cs`:

```csharp
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    public static class MatrixUtil
    {
        /// <summary>Apply a column-major 4x4 (already in Unity handedness) as local TRS.</summary>
        public static void Apply(Transform target, float[] columnMajor)
        {
            var m = new Matrix4x4();
            for (int column = 0; column < 4; column++)
            for (int row = 0; row < 4; row++)
                m[row, column] = columnMajor[column * 4 + row];
            target.localPosition = m.GetColumn(3);
            target.localRotation = m.rotation;
            target.localScale = m.lossyScale;
        }
    }
}
```

`Assets/XrSo/Runtime/Scene/CadInstance.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>One placed part occurrence (or the part itself in a part document).</summary>
    public sealed class CadInstance : MonoBehaviour
    {
        private readonly List<CadBody> _bodies = new List<CadBody>();

        public string OccurrenceId { get; private set; }
        public string DefinitionId { get; private set; }
        public IReadOnlyList<CadBody> Bodies => _bodies;

        public void Init(string occurrenceId, string definitionId)
        {
            OccurrenceId = occurrenceId;
            DefinitionId = definitionId;
        }

        internal void Add(CadBody body) => _bodies.Add(body);
    }
}
```

`Assets/XrSo/Runtime/Scene/CadBody.cs`:

```csharp
using InventorXrSo.Core.Glb;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>One solid body of an instance: what the ray hits, and how to turn the hit triangle into a face.</summary>
    public sealed class CadBody : MonoBehaviour
    {
        public CadInstance Instance { get; private set; }
        public GlbPrimitive Primitive { get; private set; }
        public Mesh Mesh { get; private set; }
        public Renderer Renderer { get; private set; }

        public void Init(CadInstance instance, GlbPrimitive primitive, Mesh mesh)
        {
            Instance = instance;
            Primitive = primitive;
            Mesh = mesh;
            Renderer = GetComponent<Renderer>();
            instance.Add(this);
        }
    }
}
```

`Assets/XrSo/Runtime/Scene/CadSceneView.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Draws a <see cref="LoadedScene"/>: one mesh per definition body, instanced per occurrence, with colliders for picking.</summary>
    public sealed class CadSceneView : MonoBehaviour
    {
        [SerializeField] private Material bodyMaterial;
        private readonly Dictionary<string, Mesh[]> _meshesByAsset = new Dictionary<string, Mesh[]>();
        private readonly List<CadInstance> _instances = new List<CadInstance>();

        public Material BodyMaterial { get => bodyMaterial; set => bodyMaterial = value; }
        public IReadOnlyList<CadInstance> Instances => _instances;
        public event Action Rebuilt;

        public CadInstance Find(string occurrenceId) => _instances.FirstOrDefault(i => i.OccurrenceId == occurrenceId);

        public void Show(LoadedScene scene)
        {
            foreach (var instance in _instances) if (instance != null) Release(instance.gameObject);
            _instances.Clear();
            var used = new HashSet<string>();
            if (scene != null)
            {
                foreach (var placed in scene.Graph.PlacedParts())
                {
                    var definition = placed.Node.DefinitionDocumentId;
                    if (!scene.Models.TryGetValue(definition, out var model)) continue;   // omitted
                    var assetId = scene.AssetIds[definition];
                    used.Add(assetId);
                    if (!_meshesByAsset.TryGetValue(assetId, out var meshes))
                    {
                        meshes = model.Primitives.Select(MeshFactory.Build).ToArray();
                        _meshesByAsset[assetId] = meshes;
                    }
                    _instances.Add(CreateInstance(placed.Node.Name ?? definition, placed.Node.OccurrenceId, definition, placed.MatrixGltf, model, meshes));
                }
            }
            foreach (var stale in _meshesByAsset.Keys.Where(k => !used.Contains(k)).ToList())
            {
                foreach (var mesh in _meshesByAsset[stale]) Release(mesh);
                _meshesByAsset.Remove(stale);
            }
            Rebuilt?.Invoke();
        }

        private CadInstance CreateInstance(string name, string occurrenceId, string definition, float[] matrixGltf, GlbModel model, Mesh[] meshes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            MatrixUtil.Apply(go.transform, Handedness.ConvertMatrix(matrixGltf));
            var instance = go.AddComponent<CadInstance>();
            instance.Init(occurrenceId, definition);
            for (int i = 0; i < model.Primitives.Count; i++)
            {
                var primitive = model.Primitives[i];
                if (!primitive.Visible) continue;
                var child = new GameObject(primitive.BodyName);
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                child.AddComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
                child.AddComponent<MeshCollider>().sharedMesh = meshes[i];
                child.AddComponent<CadBody>().Init(instance, primitive, meshes[i]);
            }
            return instance;
        }

        private static void Release(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
```

`Assets/XrSo/Runtime/Scene/ScenePlacement.cs`:

```csharp
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Where the model appears (spec §7.1): 1:1, in front of the user, slightly below eye height.</summary>
    public static class ScenePlacement
    {
        public const float MinDistance = 1.0f;
        public const float BelowEyes = 0.2f;

        public static Bounds LocalBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var bounds = new Bounds(root.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (var renderer in renderers)
            {
                var b = renderer.bounds;
                bounds.Encapsulate(root.InverseTransformPoint(b.min));
                bounds.Encapsulate(root.InverseTransformPoint(b.max));
            }
            return bounds;
        }

        public static Pose InFront(Bounds localBounds, Vector3 headPosition, Vector3 headForward)
        {
            var forward = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            float distance = Mathf.Max(MinDistance, localBounds.extents.magnitude * 1.5f);
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            var centre = headPosition + forward * distance;
            centre.y = headPosition.y - BelowEyes;
            return new Pose(centre - rotation * localBounds.center, rotation);
        }
    }
}
```

- [ ] **Step 4: Run to verify pass** — EditMode command. Expected: all pass (5 new). If `ARayOnTheTopFaceFindsTheInventorFace` finds another face, compare `hit.point` with the fixture's top face (y = 0.01 m): a wrong face means the winding flip or index order changed — fix `MeshFactory`, not the test.

- [ ] **Step 5: Commit**

```bash
git add "Inventor XR SO/Assets/XrSo"
git commit -m "feat(xr): CAD scene view with instancing, colliders and 1:1 placement"
```

### Task C4: Selection highlight

**Files:**
- Create: `Assets/XrSo/Runtime/Shaders/HighlightOverlay.shader`, `Assets/XrSo/Runtime/Scene/SelectionVisuals.cs`
- Test: `Assets/XrSo/Tests/EditMode/SelectionVisualsTests.cs`
- Generated: `Assets/XrSo/Materials/FaceHighlight.mat` (re-run `ConfigureBatch`)

**Interfaces:**
- Consumes: C3 `CadSceneView`, `CadBody`, `MeshFactory.BuildFaceOverlay`; B7 `Selection`.
- Produces: `SelectionVisuals : MonoBehaviour` — `Configure(CadSceneView view, Material occurrence, Material face)`, `Show(Selection)`, `Clear()`; clears itself when the view is rebuilt.

- [ ] **Step 1: Write the failing tests** — `Assets/XrSo/Tests/EditMode/SelectionVisualsTests.cs`:

```csharp
using InventorXrSo.Core.Selection;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class SelectionVisualsTests
    {
        private GameObject _root;
        private CadSceneView _view;
        private SelectionVisuals _visuals;
        private Material _body, _occurrence, _face;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            _body = new Material(shader); _occurrence = new Material(shader); _face = new Material(shader);
            _root = new GameObject("scene");
            _view = _root.AddComponent<CadSceneView>();
            _view.BodyMaterial = _body;
            _view.Show(CadSceneViewTests.BoltScene());
            _visuals = _root.AddComponent<SelectionVisuals>();
            _visuals.Configure(_view, _occurrence, _face);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void AnOccurrenceIsTintedAndRestored()
        {
            _visuals.Show(new Selection(SelectionKind.Occurrence, "ent_occ_2", null, "ent_occ_2"));
            Assert.AreSame(_occurrence, _view.Find("ent_occ_2").Bodies[0].Renderer.sharedMaterial);
            Assert.AreSame(_body, _view.Find("ent_occ_1").Bodies[0].Renderer.sharedMaterial);
            _visuals.Show(Selection.None);
            Assert.AreSame(_body, _view.Find("ent_occ_2").Bodies[0].Renderer.sharedMaterial);
        }

        [Test]
        public void AFaceGetsAnOverlayOfItsTrianglesOnly()
        {
            _visuals.Show(new Selection(SelectionKind.Face, "ent_occ_1", "ent_doc_bolt_f4", "proxy"));
            var overlay = _view.Find("ent_occ_1").GetComponentsInChildren<MeshFilter>()[1];
            Assert.AreEqual("FaceHighlight", overlay.gameObject.name);
            Assert.AreEqual(6, overlay.sharedMesh.GetIndices(0).Length);
            Assert.AreSame(_face, overlay.GetComponent<MeshRenderer>().sharedMaterial);
            _visuals.Clear();
            Assert.AreEqual(1, _view.Find("ent_occ_1").GetComponentsInChildren<MeshFilter>().Length);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure** — EditMode command. Expected: compile error, `SelectionVisuals` missing.

- [ ] **Step 3: Shader** — `Assets/XrSo/Runtime/Shaders/HighlightOverlay.shader` (single-pass instanced stereo safe, drawn slightly towards the camera to avoid z-fighting with the face below):

```hlsl
Shader "XrSo/HighlightOverlay"
{
    Properties { _Color ("Color", Color) = (1, 0.6, 0.1, 0.6) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag (Varyings input) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
```

- [ ] **Step 4: SelectionVisuals** — `Assets/XrSo/Runtime/Scene/SelectionVisuals.cs`:

```csharp
using System.Collections.Generic;
using InventorXrSo.Core.Selection;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Local highlight: tinted occurrence, or an overlay on the selected face's triangles.</summary>
    public sealed class SelectionVisuals : MonoBehaviour
    {
        [SerializeField] private CadSceneView view;
        [SerializeField] private Material occurrenceMaterial;
        [SerializeField] private Material faceMaterial;
        private readonly List<(Renderer renderer, Material original)> _tinted = new List<(Renderer, Material)>();
        private readonly List<GameObject> _overlays = new List<GameObject>();

        public void Configure(CadSceneView sceneView, Material occurrence, Material face)
        {
            if (view != null) view.Rebuilt -= Clear;
            view = sceneView;
            occurrenceMaterial = occurrence;
            faceMaterial = face;
            view.Rebuilt += Clear;
        }

        private void OnEnable() { if (view != null) { view.Rebuilt -= Clear; view.Rebuilt += Clear; } }
        private void OnDisable() { if (view != null) view.Rebuilt -= Clear; }

        public void Show(Selection selection)
        {
            Clear();
            if (selection == null || selection.Kind == SelectionKind.None) return;
            var instance = view.Find(selection.OccurrenceId);
            if (instance == null) return;
            foreach (var body in instance.Bodies)
            {
                if (selection.Kind == SelectionKind.Occurrence)
                {
                    _tinted.Add((body.Renderer, body.Renderer.sharedMaterial));
                    body.Renderer.sharedMaterial = occurrenceMaterial;
                    continue;
                }
                var range = body.Primitive.FaceMap.Find(selection.FaceId);
                if (range == null) continue;
                var overlay = new GameObject("FaceHighlight");
                overlay.transform.SetParent(body.transform, false);
                overlay.AddComponent<MeshFilter>().sharedMesh = MeshFactory.BuildFaceOverlay(body.Mesh, range);
                overlay.AddComponent<MeshRenderer>().sharedMaterial = faceMaterial;
                _overlays.Add(overlay);
            }
        }

        public void Clear()
        {
            foreach (var (renderer, original) in _tinted) if (renderer != null) renderer.sharedMaterial = original;
            _tinted.Clear();
            foreach (var overlay in _overlays)
            {
                if (overlay == null) continue;
                var mesh = overlay.GetComponent<MeshFilter>().sharedMesh;
                if (Application.isPlaying) { Destroy(mesh); Destroy(overlay); }
                else { DestroyImmediate(mesh); DestroyImmediate(overlay); }
            }
            _overlays.Clear();
        }
    }
}
```

- [ ] **Step 5: Material and tests**

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoProjectSetup.ConfigureBatch"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode.xml`""
```

Expected: `Assets/XrSo/Materials/FaceHighlight.mat` exists; all EditMode tests pass (2 new). In EditMode `Destroy` is not allowed, which is why `Clear` branches on `Application.isPlaying`.

- [ ] **Step 6: Commit**

```bash
git add "Inventor XR SO/Assets/XrSo"
git commit -m "feat(xr): occurrence tint and face overlay highlight"
```


### Task C5: Credential storage and manual address entry

**Files:**
- Create: `Core/Pairing/CredentialStore.cs`, `Core/Pairing/PairingAddress.cs`
- Create: `Inventor XR SO/Assets/Plugins/Android/CredentialVault.java`
- Create: `Assets/XrSo/Runtime/Pairing/KeystoreProtector.cs`, `Assets/XrSo/Runtime/Pairing/CredentialStores.cs`
- Test: `CoreTests/Pairing/CredentialStoreTests.cs`

**Interfaces:**
- Produces: `IProtector { string Protect(string); string Unprotect(string); }`, `PlainProtector`, `ICredentialStore { PairedServer Load(); void Save(PairedServer); void Clear(); }`, `FileCredentialStore(string path, IProtector protector)` (unreadable or undecryptable file → deleted, `Load` returns null → the user pairs again), `PairingAddress.TryParse(string text, out string host, out int port)` (default port 8443, accepts `host`, `host:port`, `[v6]:port`), `CredentialStores.Create() : ICredentialStore` (Android: Keystore AES-GCM; Editor: plain file), Java `com.occhipinti.inventorxrso.CredentialVault.encrypt/decrypt`.

- [ ] **Step 1: Write the failing tests** — `CoreTests/Pairing/CredentialStoreTests.cs`:

```csharp
using InventorXrSo.Core.Pairing;

namespace InventorXrSo.Core.Tests.Pairing;

public class CredentialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "xrso-cred-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "pairing.dat");
    private static readonly PairedServer Server = new PairedServer("192.168.1.20", 8443, new string('a', 64), "quest3", "secret-token-secret-token-secret-token");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class Reversing : IProtector
    {
        public string Protect(string plain) => new string(plain.Reverse().ToArray());
        public string Unprotect(string text) => new string(text.Reverse().ToArray());
    }

    [Fact]
    public void SavesProtectedAndLoadsBack()
    {
        var store = new FileCredentialStore(FilePath, new Reversing());
        store.Save(Server);
        Assert.DoesNotContain("secret-token", File.ReadAllText(FilePath));
        var loaded = new FileCredentialStore(FilePath, new Reversing()).Load();
        Assert.Equal(Server.Token, loaded.Token);
        Assert.Equal(Server.CertSha256, loaded.CertSha256);
    }

    [Fact]
    public void NothingSavedLoadsNull() => Assert.Null(new FileCredentialStore(FilePath, new PlainProtector()).Load());

    [Fact]
    public void AnUnreadableFileIsForgotten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "garbage");
        Assert.Null(new FileCredentialStore(FilePath, new PlainProtector()).Load());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ClearRemovesThePairing()
    {
        var store = new FileCredentialStore(FilePath, new PlainProtector());
        store.Save(Server);
        store.Clear();
        Assert.Null(store.Load());
    }

    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20", 8443)]
    [InlineData("192.168.1.20:9443", "192.168.1.20", 9443)]
    [InlineData(" pc.local:8443 ", "pc.local", 8443)]
    [InlineData("[fe80::1]:8443", "fe80::1", 8443)]
    public void AddressesParse(string text, string host, int port)
    {
        Assert.True(PairingAddress.TryParse(text, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("")]
    [InlineData(":8443")]
    [InlineData("host:0")]
    [InlineData("host:99999")]
    [InlineData("host:abc")]
    public void BadAddressesDoNot(string text) => Assert.False(PairingAddress.TryParse(text, out _, out _));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~CredentialStoreTests"`
Expected: build FAILS (`FileCredentialStore`, `PairingAddress` missing).

- [ ] **Step 3: Implement** — `Core/Pairing/CredentialStore.cs`:

```csharp
using System;
using System.IO;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>Encrypts the stored pairing (Android Keystore on the headset).</summary>
    public interface IProtector
    {
        string Protect(string plain);
        string Unprotect(string protectedText);
    }

    public sealed class PlainProtector : IProtector
    {
        public string Protect(string plain) => plain;
        public string Unprotect(string protectedText) => protectedText;
    }

    public interface ICredentialStore
    {
        PairedServer Load();
        void Save(PairedServer server);
        void Clear();
    }

    public sealed class FileCredentialStore : ICredentialStore
    {
        private readonly string _path;
        private readonly IProtector _protector;

        public FileCredentialStore(string path, IProtector protector)
        {
            _path = path;
            _protector = protector;
        }

        public PairedServer Load()
        {
            if (!File.Exists(_path)) return null;
            try { return PairedServer.FromJson(_protector.Unprotect(File.ReadAllText(_path))); }
            catch (Exception)
            {
                // Corrupt file or a reset Keystore key: forget it, the user pairs again.
                Clear();
                return null;
            }
        }

        public void Save(PairedServer server)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
            var temp = _path + ".tmp";
            File.WriteAllText(temp, _protector.Protect(server.ToJson()));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(temp, _path);
        }

        public void Clear()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
```

`Core/Pairing/PairingAddress.cs`:

```csharp
namespace InventorXrSo.Core.Pairing
{
    /// <summary>The PC address typed on the headset: host, host:port or [ipv6]:port.</summary>
    public static class PairingAddress
    {
        public const int DefaultPort = 8443;

        public static bool TryParse(string text, out string host, out int port)
        {
            host = null;
            port = DefaultPort;
            var value = (text ?? "").Trim();
            if (value.Length == 0) return false;
            string portText = null;
            if (value.StartsWith("["))
            {
                int close = value.IndexOf(']');
                if (close < 0) return false;
                host = value.Substring(1, close - 1);
                if (close + 1 < value.Length)
                {
                    if (value[close + 1] != ':') return false;
                    portText = value.Substring(close + 2);
                }
            }
            else
            {
                int colon = value.LastIndexOf(':');
                host = colon < 0 ? value : value.Substring(0, colon);
                if (colon >= 0) portText = value.Substring(colon + 1);
            }
            if (string.IsNullOrWhiteSpace(host)) return false;
            if (portText != null && (!int.TryParse(portText, out port) || port < 1 || port > 65535)) return false;
            return true;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --filter "FullyQualifiedName~CredentialStoreTests"`
Expected: 13 passed.

- [ ] **Step 5: Android Keystore** — `Inventor XR SO/Assets/Plugins/Android/CredentialVault.java`:

```java
package com.occhipinti.inventorxrso;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** AES-GCM with a non-exportable Android Keystore key: protects the stored pairing token. */
public final class CredentialVault {
    private static final String ALIAS = "inventor_xr_so_credentials";
    private static final String STORE = "AndroidKeyStore";

    private CredentialVault() {}

    private static SecretKey key() throws Exception {
        KeyStore keyStore = KeyStore.getInstance(STORE);
        keyStore.load(null);
        if (keyStore.containsAlias(ALIAS)) {
            return ((KeyStore.SecretKeyEntry) keyStore.getEntry(ALIAS, null)).getSecretKey();
        }
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, STORE);
        generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return generator.generateKey();
    }

    public static String encrypt(String plain) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        byte[] iv = cipher.getIV();
        byte[] body = cipher.doFinal(plain.getBytes(StandardCharsets.UTF_8));
        byte[] out = new byte[1 + iv.length + body.length];
        out[0] = (byte) iv.length;
        System.arraycopy(iv, 0, out, 1, iv.length);
        System.arraycopy(body, 0, out, 1 + iv.length, body.length);
        return Base64.encodeToString(out, Base64.NO_WRAP);
    }

    public static String decrypt(String blob) throws Exception {
        byte[] in = Base64.decode(blob, Base64.NO_WRAP);
        int ivLength = in[0];
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, in, 1, ivLength));
        return new String(cipher.doFinal(in, 1 + ivLength, in.length - 1 - ivLength), StandardCharsets.UTF_8);
    }
}
```

`Assets/XrSo/Runtime/Pairing/KeystoreProtector.cs`:

```csharp
#if UNITY_ANDROID && !UNITY_EDITOR
using InventorXrSo.Core.Pairing;
using UnityEngine;

namespace InventorXrSo.Unity.Pairing
{
    /// <summary>Calls CredentialVault.java; a Java exception surfaces as AndroidJavaException (Load then forgets the file).</summary>
    public sealed class KeystoreProtector : IProtector
    {
        private const string Vault = "com.occhipinti.inventorxrso.CredentialVault";

        public string Protect(string plain)
        {
            using (var vault = new AndroidJavaClass(Vault)) return vault.CallStatic<string>("encrypt", plain);
        }

        public string Unprotect(string protectedText)
        {
            using (var vault = new AndroidJavaClass(Vault)) return vault.CallStatic<string>("decrypt", protectedText);
        }
    }
}
#endif
```

`Assets/XrSo/Runtime/Pairing/CredentialStores.cs`:

```csharp
using System.IO;
using InventorXrSo.Core.Pairing;
using UnityEngine;

namespace InventorXrSo.Unity.Pairing
{
    public static class CredentialStores
    {
        public static ICredentialStore Create()
        {
            var path = Path.Combine(Application.persistentDataPath, "pairing.dat");
#if UNITY_ANDROID && !UNITY_EDITOR
            return new FileCredentialStore(path, new KeystoreProtector());
#else
            return new FileCredentialStore(path, new PlainProtector());
#endif
        }
    }
}
```

- [ ] **Step 6: Unity compiles** — EditMode command from C1 Step 10. Expected: exit code 0 (the Java file is compiled only by the Android build, verified in C9).

- [ ] **Step 7: Commit**

```bash
git add "Inventor XR SO/Packages" "Inventor XR SO/Tests~" "Inventor XR SO/Assets"
git commit -m "feat(xr): Keystore-protected pairing storage and address parsing"
```

### Task C6: Controller ray and environment modes

**Files:**
- Create: `Assets/XrSo/Runtime/Scene/CadRaycaster.cs`
- Create: `Assets/XrSo/Xr/InventorXrSo.Xr.asmdef`, `Assets/XrSo/Xr/ControllerRay.cs`, `Assets/XrSo/Xr/EnvironmentModeController.cs`
- Test: `Assets/XrSo/Tests/EditMode/CadRaycasterTests.cs`

**Interfaces:**
- Consumes: C3 `CadBody`.
- Produces: `CadRaycaster.TryPick(Ray ray, float maxDistance, out CadBody body, out int triangle, out Vector3 point) : bool`; `ControllerRay : MonoBehaviour` (`Configure(Transform origin, LineRenderer line)`, `OVRInput.Controller Controller`, `event Action<CadBody, int> Picked`, `event Action PickedNothing`); `EnvironmentMode { MixedReality, StudioVr }`, `EnvironmentModeController` (`Configure(Camera eye, OVRPassthroughLayer passthrough)`, `Mode`, `Set(EnvironmentMode)`).

- [ ] **Step 1: Write the failing test** — `Assets/XrSo/Tests/EditMode/CadRaycasterTests.cs`:

```csharp
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class CadRaycasterTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        [Test]
        public void PicksTheBodyAndTriangleAndIgnoresOtherColliders()
        {
            _root = new GameObject("scene");
            var view = _root.AddComponent<CadSceneView>();
            view.BodyMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            view.Show(CadSceneViewTests.BoltScene());
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);   // not CAD: must be skipped
            blocker.transform.SetParent(_root.transform);
            blocker.transform.position = new Vector3(-0.005f, 0.5f, 0.005f);
            blocker.transform.localScale = Vector3.one * 0.01f;
            Physics.SyncTransforms();

            Assert.IsTrue(CadRaycaster.TryPick(new Ray(new Vector3(-0.005f, 1f, 0.005f), Vector3.down), 5f, out var body, out var triangle, out _));
            Assert.AreEqual("ent_doc_bolt_f4", body.Primitive.FaceMap.FaceAtTriangle(triangle).FaceId);
            Assert.IsFalse(CadRaycaster.TryPick(new Ray(new Vector3(5f, 1f, 5f), Vector3.down), 5f, out _, out _, out _));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure** — EditMode command. Expected: compile error, `CadRaycaster` missing.

- [ ] **Step 3: Implement** — `Assets/XrSo/Runtime/Scene/CadRaycaster.cs`:

```csharp
using System;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Nearest CAD body along a ray; colliders that are not CAD bodies are passed through.</summary>
    public static class CadRaycaster
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        public static bool TryPick(Ray ray, float maxDistance, out CadBody body, out int triangle, out Vector3 point)
        {
            body = null;
            triangle = -1;
            point = default;
            int count = Physics.RaycastNonAlloc(ray, Hits, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(Hits, 0, count, HitDistance.Instance);
            for (int i = 0; i < count; i++)
            {
                var candidate = Hits[i].collider.GetComponent<CadBody>();
                if (candidate == null || Hits[i].triangleIndex < 0) continue;
                body = candidate;
                triangle = Hits[i].triangleIndex;
                point = Hits[i].point;
                return true;
            }
            return false;
        }

        private sealed class HitDistance : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitDistance Instance = new HitDistance();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
```

`Assets/XrSo/Xr/InventorXrSo.Xr.asmdef`:

```json
{
  "name": "InventorXrSo.Xr",
  "rootNamespace": "InventorXrSo.Xr",
  "references": ["InventorXrSo.Core", "InventorXrSo.Unity", "Oculus.VR", "UnityEngine.UI"],
  "includePlatforms": [],
  "autoReferenced": true
}
```

(If the log reports `Oculus.VR` unknown, list the Meta package's asmdefs with `Get-ChildItem "Inventor XR SO/Library/PackageCache" -Recurse -Filter *.asmdef | Select-String '"name"' | Select-String Oculus` and use the one that defines `OVRInput`.)

`Assets/XrSo/Xr/ControllerRay.cs`:

```csharp
using System;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>Ray from the dominant controller (spec §9): trigger picks the CAD body under it. Only picks; never edits.</summary>
    public sealed class ControllerRay : MonoBehaviour
    {
        [SerializeField] private Transform origin;
        [SerializeField] private LineRenderer line;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;

        public OVRInput.Controller Controller { get => controller; set => controller = value; }
        public event Action<CadBody, int> Picked;
        public event Action PickedNothing;

        public void Configure(Transform rayOrigin, LineRenderer rayLine)
        {
            origin = rayOrigin;
            line = rayLine;
        }

        private void OnDisable() { if (line != null) line.enabled = false; }
        private void OnEnable() { if (line != null) line.enabled = true; }

        private void Update()
        {
            var ray = new Ray(origin.position, origin.forward);
            bool hit = CadRaycaster.TryPick(ray, maxDistance, out var body, out var triangle, out var point);
            line.positionCount = 2;
            line.SetPosition(0, ray.origin);
            line.SetPosition(1, hit ? point : ray.origin + ray.direction * maxDistance);
            if (!OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller)) return;
            if (hit) Picked?.Invoke(body, triangle);
            else PickedNothing?.Invoke();
        }
    }
}
```

`Assets/XrSo/Xr/EnvironmentModeController.cs`:

```csharp
using UnityEngine;

namespace InventorXrSo.Xr
{
    public enum EnvironmentMode { MixedReality, StudioVr }

    /// <summary>Spec §6: same UI and workflows, only the background changes (passthrough or a neutral studio).</summary>
    public sealed class EnvironmentModeController : MonoBehaviour
    {
        [SerializeField] private Camera eye;
        [SerializeField] private OVRPassthroughLayer passthrough;
        [SerializeField] private Color studioColor = new Color(0.16f, 0.17f, 0.19f, 1f);

        public EnvironmentMode Mode { get; private set; } = EnvironmentMode.MixedReality;

        public void Configure(Camera centerEye, OVRPassthroughLayer layer)
        {
            eye = centerEye;
            passthrough = layer;
        }

        public void Set(EnvironmentMode mode)
        {
            Mode = mode;
            bool mixed = mode == EnvironmentMode.MixedReality;
            if (OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = mixed;
            if (passthrough != null) passthrough.enabled = mixed;
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = mixed ? new Color(0, 0, 0, 0) : studioColor;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass** — EditMode command. Expected: all pass (1 new).

- [ ] **Step 5: Commit**

```bash
git add "Inventor XR SO/Assets/XrSo"
git commit -m "feat(xr): controller ray picking and MR / Studio VR environments"
```


### Task C7: Home, pairing flow, status badge and app wiring

**Files:**
- Modify: `Assets/XrSo/Runtime/InventorXrSo.Unity.asmdef` (add `"UnityEngine.UI"` to `references`)
- Create: `Assets/XrSo/Runtime/Ui/UiText.cs`, `Assets/XrSo/Runtime/Ui/UiFactory.cs`, `Assets/XrSo/Runtime/Ui/HomePanel.cs`, `Assets/XrSo/Runtime/Ui/StatusBadge.cs`
- Create: `Assets/XrSo/Xr/AppController.cs`
- Create: `Assets/XrSo/Editor/Setup/XrSoSceneBuilder.cs`; modify `InventorXrSo.Editor.asmdef` references
- Modify: `XrSoProjectSetup.CreateMaterials` (ray material)
- Test: `Assets/XrSo/Tests/EditMode/HomePanelTests.cs`
- Generated: `Assets/XrSo/Scenes/Main.unity`

**Interfaces:**
- Consumes: everything above.
- Produces: `UiText` (all Italian strings, `Status(SessionStatus)`), `HomePanel.Create(Transform parent)`, `ShowMessage(string title, string body)`, `SetActions(params (string label, Action action)[])`, `PromptText(string title, string hint, string initial, Action<string> onSubmit, Action onCancel)`, `Press(string key)`, `Canvas`; `StatusBadge.Create(Transform head)`, `SetStatus(string)`, `Flash(string)`; `AppController` (`Configure(...)`); menu `Inventor XR SO/Build Main Scene` (`XrSoSceneBuilder.BuildBatch`).

- [ ] **Step 1: Write the failing tests** — `Assets/XrSo/Tests/EditMode/HomePanelTests.cs`:

```csharp
using System;
using System.Linq;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Tests
{
    public class HomePanelTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown() { if (_root != null) UnityEngine.Object.DestroyImmediate(_root); }

        [Test]
        public void TheKeypadBuildsTheEntryAndSubmitsIt()
        {
            _root = new GameObject("root");
            var home = HomePanel.Create(_root.transform);
            string submitted = null;
            home.PromptText(UiText.EnterPcAddress, UiText.EnterPcAddressHint, "192.168.", s => submitted = s, () => { });
            foreach (var key in new[] { "1", ".", "2", "0", "⌫", "5", UiText.KeyOk }) home.Press(key);
            Assert.AreEqual("192.168.1.25", submitted);
        }

        [Test]
        public void ActionsBecomeButtons()
        {
            _root = new GameObject("root");
            var home = HomePanel.Create(_root.transform);
            int clicks = 0;
            home.SetActions((UiText.PairWithQr, () => clicks++), (UiText.PairWithCode, () => { }));
            var buttons = home.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ToArray();
            Assert.AreEqual(2, buttons.Length);
            buttons[0].onClick.Invoke();
            Assert.AreEqual(1, clicks);
        }

        [Test]
        public void EveryStatusHasItalianText()
        {
            foreach (SessionStatus status in Enum.GetValues(typeof(SessionStatus)))
                Assert.IsFalse(string.IsNullOrEmpty(UiText.Status(status)), status.ToString());
        }
    }
}
```

- [ ] **Step 2: Run to verify failure** — EditMode command. Expected: compile error, `InventorXrSo.Unity.Ui` missing.

- [ ] **Step 3: Strings** — `Assets/XrSo/Runtime/Ui/UiText.cs`:

```csharp
using InventorXrSo.Core.Session;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Every user-visible string (MVP language: Italian).</summary>
    public static class UiText
    {
        public const string AppTitle = "Inventor XR SO";
        public const string NotPaired = "Nessun PC associato";
        public const string NotPairedBody = "Avvia sul PC Inventor.So.Mcp.Http con --pair, poi scansiona il QR o inserisci il codice.";
        public const string PairWithQr = "Scansiona QR";
        public const string PairWithCode = "Inserisci codice";
        public const string EnterPcAddress = "Indirizzo del PC";
        public const string EnterPcAddressHint = "Come mostrato sul PC, es. 192.168.1.20:8443";
        public const string EnterCode = "Codice di associazione";
        public const string EnterCodeHint = "Le 6 cifre mostrate sul PC";
        public const string CheckFingerprint = "Confronta il certificato";
        public const string CheckFingerprintBody = "Deve coincidere con quello mostrato sul PC:\n";
        public const string FingerprintMatches = "Coincide";
        public const string Cancel = "Annulla";
        public const string KeyOk = "OK";
        public const string KeyBack = "⌫";
        public const string Pairing = "Associazione in corso…";
        public const string ScanQr = "Inquadra il QR mostrato sul PC";
        public const string CameraDenied = "Permesso fotocamera negato: usa il codice.";
        public const string NoCamera = "Fotocamera non disponibile: usa il codice.";
        public const string QrTimeout = "QR non trovato: riprova o usa il codice.";
        public const string QrInvalid = "Questo QR non è un codice di associazione Inventor SO.";
        public const string BadAddress = "Indirizzo non valido.";
        public const string CertificateChanged = "Il PC presenta un certificato diverso da quello associato. Associa di nuovo il PC.";
        public const string TokenRevoked = "Il PC non riconosce più questo visore. Associa di nuovo il PC.";
        public const string EnterMixedReality = "Entra · Mixed Reality";
        public const string EnterStudio = "Entra · Studio VR";
        public const string ForgetPc = "Dimentica PC";
        public const string Home = "Home";
        public const string Offline = "Offline · sola lettura";
        public const string OfflineNoSelection = "Offline: selezione non disponibile";
        public const string NoFaceHere = "Nessuna faccia in questo punto";
        public const string SelectionFailed = "Selezione non riuscita: ";
        public const string Omitted = " componenti non mostrati (mesh troppo grandi o oltre il limite)";
        public const string PcLabel = "PC: ";
        public const string BackendLabel = "Backend: ";
        public const string InventorLabel = "Inventor: ";
        public const string DocumentLabel = "Documento: ";
        public const string ExperimentalOff = "Abilita il tier sperimentale sull'add-in (INVENTOR_SO_EXPERIMENTAL=1) e avvia il server con --enable-experimental.";

        public static string Status(SessionStatus status)
        {
            switch (status)
            {
                case SessionStatus.Idle: return "In attesa";
                case SessionStatus.Connecting: return "Connessione…";
                case SessionStatus.NotReady: return "Inventor non pronto";
                case SessionStatus.NoDocument: return "Nessun documento aperto in Inventor";
                case SessionStatus.Online: return "Connesso";
                case SessionStatus.Offline: return Offline;
                case SessionStatus.NeedsPairing: return "Associazione richiesta";
                default: return status.ToString();
            }
        }
    }
}
```

- [ ] **Step 4: UI building blocks** — `Assets/XrSo/Runtime/Ui/UiFactory.cs`:

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Plain uGUI built in code: no prefabs to keep in sync.</summary>
    public static class UiFactory
    {
        public static readonly Color Background = new Color(0.08f, 0.09f, 0.11f, 0.92f);
        public static readonly Color Accent = new Color(0.20f, 0.45f, 0.85f, 1f);
        public static readonly Color Key = new Color(0.22f, 0.24f, 0.28f, 1f);

        public static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>World-space canvas; 1 canvas unit = 1 mm.</summary>
        public static Canvas WorldCanvas(Transform parent, string name, Vector2 sizeMm)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = sizeMm;
            go.transform.localScale = Vector3.one * 0.001f;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static Text Label(Transform parent, string text, int size, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = Color.white;
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public static Button Button(Transform parent, string text, Color color, int fontSize, Action onClick)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            var label = Label(go.transform, text, fontSize, FontStyle.Bold);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform);
            return button;
        }
    }
}
```

- [ ] **Step 5: HomePanel** — `Assets/XrSo/Runtime/Ui/HomePanel.cs`:

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Technical Home (spec §5.1): status text, action buttons and a keypad for addresses and codes.</summary>
    public sealed class HomePanel : MonoBehaviour
    {
        private static readonly string[] Keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", ".", ":", UiText.KeyBack, UiText.KeyOk, UiText.Cancel };
        private Text _title, _body, _entryText;
        private RectTransform _actions, _keypad;
        private string _entry = "";
        private Action<string> _submit;
        private Action _cancel;

        public Canvas Canvas { get; private set; }

        public static HomePanel Create(Transform parent)
        {
            var canvas = UiFactory.WorldCanvas(parent, "Home", new Vector2(820, 620));
            var panel = canvas.gameObject.AddComponent<HomePanel>();
            panel.Canvas = canvas;
            panel.Build();
            return panel;
        }

        private void Build()
        {
            var background = UiFactory.Panel(transform, "Background", UiFactory.Background);
            UiFactory.Stretch(background);
            var layout = background.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 28, 28);
            layout.spacing = 16;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            _title = UiFactory.Label(background, UiText.AppTitle, 40, FontStyle.Bold);
            _body = UiFactory.Label(background, "", 26);
            _entryText = UiFactory.Label(background, "", 44, FontStyle.Bold);
            _actions = Grid(background, "Actions", new Vector2(360, 72), 2);
            _keypad = Grid(background, "Keypad", new Vector2(140, 72), 5);
            foreach (var key in Keys)
            {
                var k = key;
                UiFactory.Button(_keypad, key, key == UiText.KeyOk ? UiFactory.Accent : UiFactory.Key, 30, () => Press(k));
            }
            HideEntry();
        }

        private static RectTransform Grid(Transform parent, string name, Vector2 cell, int columns)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = new Vector2(12, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            return (RectTransform)go.transform;
        }

        public void ShowMessage(string title, string body)
        {
            _title.text = title;
            _body.text = body;
            HideEntry();
        }

        public void SetActions(params (string label, Action action)[] actions)
        {
            var old = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in _actions) old.Add(child);   // not while iterating: DestroyImmediate edits the list
            foreach (var child in old)
            {
                child.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            foreach (var (label, action) in actions) UiFactory.Button(_actions, label, UiFactory.Accent, 28, action);
        }

        public void PromptText(string title, string hint, string initial, Action<string> onSubmit, Action onCancel)
        {
            _title.text = title;
            _body.text = hint;
            _entry = initial ?? "";
            _entryText.text = _entry;
            _submit = onSubmit;
            _cancel = onCancel;
            SetActions();
            _entryText.gameObject.SetActive(true);
            _keypad.gameObject.SetActive(true);
        }

        public void Press(string key)
        {
            if (key == UiText.KeyBack)
            {
                if (_entry.Length > 0) _entry = _entry.Substring(0, _entry.Length - 1);
            }
            else if (key == UiText.KeyOk)
            {
                var submit = _submit;
                var value = _entry;
                HideEntry();
                submit?.Invoke(value);
                return;
            }
            else if (key == UiText.Cancel)
            {
                var cancel = _cancel;
                HideEntry();
                cancel?.Invoke();
                return;
            }
            else if (_entry.Length < 64) _entry += key;
            _entryText.text = _entry;
        }

        private void HideEntry()
        {
            _submit = null;
            _cancel = null;
            _entryText.gameObject.SetActive(false);
            _keypad.gameObject.SetActive(false);
        }
    }
}
```

- [ ] **Step 6: StatusBadge** — `Assets/XrSo/Runtime/Ui/StatusBadge.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Small, lazily head-following status (spec §5.5: offline visible but not invasive).</summary>
    public sealed class StatusBadge : MonoBehaviour
    {
        private static readonly Vector3 Offset = new Vector3(-0.28f, -0.22f, 0.9f);
        private Transform _head;
        private Text _status, _flash;
        private float _flashUntil;

        public static StatusBadge Create(Transform head)
        {
            var canvas = UiFactory.WorldCanvas(null, "StatusBadge", new Vector2(320, 110));
            var badge = canvas.gameObject.AddComponent<StatusBadge>();
            badge._head = head;
            var background = UiFactory.Panel(canvas.transform, "Background", UiFactory.Background);
            UiFactory.Stretch(background);
            var layout = background.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            badge._status = UiFactory.Label(background, "", 22, FontStyle.Bold);
            badge._flash = UiFactory.Label(background, "", 18);
            return badge;
        }

        public void SetStatus(string text) => _status.text = text;

        public void Flash(string text)
        {
            _flash.text = text;
            _flashUntil = Time.unscaledTime + 3f;
        }

        private void LateUpdate()
        {
            if (_flash.text.Length > 0 && Time.unscaledTime > _flashUntil) _flash.text = "";
            if (_head == null) return;
            var target = _head.position + _head.rotation * Offset;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            transform.rotation = Quaternion.LookRotation(transform.position - _head.position, Vector3.up);
        }
    }
}
```

- [ ] **Step 7: Run the tests** — add `"UnityEngine.UI"` to `InventorXrSo.Unity.asmdef` references and to the EditMode test asmdef references, then run the EditMode command. Expected: all pass (3 new).

- [ ] **Step 8: AppController** — `Assets/XrSo/Xr/AppController.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Net;
using InventorXrSo.Unity.Pairing;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Wires M1 together: pairing, session, Home, scene, selection. Everything runs on the main thread
    /// (awaits resume on Unity's synchronization context).
    /// </summary>
    public sealed class AppController : MonoBehaviour
    {
        [SerializeField] private CadSceneView sceneView;
        [SerializeField] private SelectionVisuals selectionVisuals;
        [SerializeField] private ControllerRay ray;
        [SerializeField] private EnvironmentModeController environment;
        [SerializeField] private Transform head;
        [SerializeField] private QrScanner qrScanner;

        private HomePanel _home;
        private StatusBadge _badge;
        private ICredentialStore _store;
        private PairedServer _server;
        private SessionController _session;
        private SelectionService _selection;
        private CancellationTokenSource _run;
        private bool _inSession;
        private bool _placed;

        public void Configure(CadSceneView view, SelectionVisuals visuals, ControllerRay controllerRay, EnvironmentModeController env, Transform centerEye, QrScanner scanner)
        {
            sceneView = view;
            selectionVisuals = visuals;
            ray = controllerRay;
            environment = env;
            head = centerEye;
            qrScanner = scanner;
        }

        private void Start()
        {
            _home = HomePanel.Create(null);
            XrUi.MakeInteractive(_home.Canvas, head.GetComponent<Camera>());
            _badge = StatusBadge.Create(head);
            _badge.gameObject.SetActive(false);
            ray.Picked += OnPicked;
            ray.PickedNothing += OnPickedNothing;
            ray.enabled = false;
            sceneView.gameObject.SetActive(false);
            _store = CredentialStores.Create();
            _server = _store.Load();
            ShowHome();
            if (_server == null) ShowPairing(UiText.NotPaired, UiText.NotPairedBody);
            else StartSession();
        }

        private void Update()
        {
            if (_inSession && OVRInput.GetDown(OVRInput.Button.Start)) LeaveSession();
        }

        private void OnDestroy() => _run?.Cancel();

        // --- pairing ---

        private void ShowPairing(string title, string body)
        {
            ShowHome();
            _home.ShowMessage(title, body);
            _home.SetActions((UiText.PairWithQr, StartQr), (UiText.PairWithCode, AskAddress));
        }

        private PairingClient Pairing() => new PairingClient(trust => new UnityHttpTransport(trust));

        private void StartQr()
        {
            _home.ShowMessage(UiText.ScanQr, "");
            _home.SetActions((UiText.Cancel, () => { qrScanner.Stop(); ShowPairing(UiText.NotPaired, UiText.NotPairedBody); }));
            qrScanner.Scan(text =>
            {
                PairingPayload payload;
                try { payload = PairingPayload.Parse(text); }
                catch (FormatException) { ShowPairing(UiText.QrInvalid, UiText.NotPairedBody); return; }
                Complete(Pairing().PairWithQrAsync(payload, SystemInfo.deviceName, CancellationToken.None));
            }, error => ShowPairing(error, UiText.NotPairedBody));
        }

        private void AskAddress()
        {
            _home.PromptText(UiText.EnterPcAddress, UiText.EnterPcAddressHint, "192.168.", async text =>
            {
                if (!PairingAddress.TryParse(text, out var host, out var port)) { ShowPairing(UiText.BadAddress, UiText.NotPairedBody); return; }
                _home.ShowMessage(UiText.Pairing, host + ":" + port);
                try
                {
                    var sha = await Pairing().ProbeFingerprintAsync(host, port, CancellationToken.None);
                    _home.ShowMessage(UiText.CheckFingerprint, UiText.CheckFingerprintBody + CertificatePin.Display(sha));
                    _home.SetActions((UiText.FingerprintMatches, () => AskCode(host, port, sha)),
                                     (UiText.Cancel, () => ShowPairing(UiText.NotPaired, UiText.NotPairedBody)));
                }
                catch (Exception ex) { ShowPairing(ex.Message, UiText.NotPairedBody); }
            }, () => ShowPairing(UiText.NotPaired, UiText.NotPairedBody));
        }

        private void AskCode(string host, int port, string sha)
        {
            _home.PromptText(UiText.EnterCode, UiText.EnterCodeHint, "", code =>
                    Complete(Pairing().PairAsync(host, port, code.Trim(), ServerTrust.Pinned(sha), SystemInfo.deviceName, CancellationToken.None)),
                () => ShowPairing(UiText.NotPaired, UiText.NotPairedBody));
        }

        private async void Complete(Task<PairedServer> pairing)
        {
            _home.ShowMessage(UiText.Pairing, "");
            _home.SetActions();
            try
            {
                _server = await pairing;
                _store.Save(_server);
                StartSession();
            }
            catch (PairingException ex) { ShowPairing(ex.Message, UiText.NotPairedBody); }
            catch (CertificateRejectedException) { ShowPairing(UiText.CertificateChanged, UiText.NotPairedBody); }
            catch (Exception ex) { ShowPairing(ex.Message, UiText.NotPairedBody); }
        }

        private void Forget()
        {
            _run?.Cancel();
            _session = null;
            _store.Clear();
            _server = null;
            sceneView.Show(null);
            ShowPairing(UiText.NotPaired, UiText.NotPairedBody);
        }

        // --- session ---

        private void StartSession()
        {
            _run?.Cancel();
            _run = new CancellationTokenSource();
            var backend = new InventorBackend(new UnityHttpTransport(ServerTrust.Pinned(_server.CertSha256)), _server,
                new FileAssetCache(Path.Combine(Application.persistentDataPath, "assets")));
            _session = new SessionController(backend, new TaskDelay());
            _selection = new SelectionService(backend);
            _selection.Changed += selectionVisuals.Show;
            _session.StatusChanged += _ => RefreshHome();
            _session.SceneLoaded += OnSceneLoaded;
            RefreshHome();
            RunSession(_session, _run.Token);
        }

        private async void RunSession(SessionController session, CancellationToken ct)
        {
            await session.RunAsync(ct);
            if (session != _session || session.Status != SessionStatus.NeedsPairing) return;
            _store.Clear();
            _server = null;
            LeaveSession();
            ShowPairing(UiText.TokenRevoked, UiText.NotPairedBody);
        }

        private void OnSceneLoaded(LoadedScene scene)
        {
            sceneView.Show(scene);
            if (scene != null && _inSession && !_placed) Place();
            if (scene != null && scene.Omitted.Count > 0) _badge.Flash(scene.Omitted.Count + UiText.Omitted);
            RefreshHome();
        }

        private void RefreshHome()
        {
            if (_session == null) return;
            var status = UiText.Status(_session.Status);
            _badge.SetStatus(status);
            if (_inSession) return;
            var caps = _session.Capabilities;
            var body = UiText.PcLabel + _server.Host + ":" + _server.Port + "  (" + status + ")\n"
                     + UiText.BackendLabel + (caps?.ServerVersion ?? "-") + "\n"
                     + UiText.InventorLabel + (caps?.InventorYear?.ToString() ?? "-") + "\n"
                     + UiText.DocumentLabel + (_session.Scene?.Graph.Root.Name ?? "-") + " (" + (_session.Scene?.Graph.Kind ?? caps?.ActiveDocumentKind ?? "-") + ")";
            if (caps != null && !caps.IsXrReady) body += "\n" + UiText.ExperimentalOff;
            if (_session.Status == SessionStatus.Offline && _session.LastError != null) body += "\n" + _session.LastError;
            _home.ShowMessage(UiText.AppTitle, body);
            if (_session.Status == SessionStatus.Online && _session.Scene != null)
                _home.SetActions((UiText.EnterMixedReality, () => EnterSession(EnvironmentMode.MixedReality)),
                                 (UiText.EnterStudio, () => EnterSession(EnvironmentMode.StudioVr)),
                                 (UiText.ForgetPc, Forget));
            else _home.SetActions((UiText.ForgetPc, Forget));
        }

        private void ShowHome()
        {
            _inSession = false;
            ray.enabled = false;
            _home.gameObject.SetActive(true);
            _badge.gameObject.SetActive(false);
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            _home.transform.position = head.position + forward * 1.0f;
            _home.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void EnterSession(EnvironmentMode mode)
        {
            environment.Set(mode);
            _inSession = true;
            _home.gameObject.SetActive(false);
            _badge.gameObject.SetActive(true);
            sceneView.gameObject.SetActive(true);
            ray.enabled = true;
            if (!_placed) Place();
            RefreshHome();
        }

        private void LeaveSession()
        {
            ShowHome();
            RefreshHome();
        }

        private void Place()
        {
            var root = sceneView.transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var pose = ScenePlacement.InFront(ScenePlacement.LocalBounds(root), head.position, head.forward);
            root.SetPositionAndRotation(pose.position, pose.rotation);
            _placed = true;
        }

        // --- selection (read-only in M1: never edits the CAD) ---

        private async void OnPicked(CadBody body, int triangle)
        {
            if (_session == null || _session.Status != SessionStatus.Online || _session.Scene == null)
            {
                _badge.Flash(UiText.OfflineNoSelection);
                return;
            }
            var face = body.Primitive.FaceMap.FaceAtTriangle(triangle)?.FaceId;
            if (face == null && SelectionService.Resolve(_session.Scene.Graph.Kind, _selection.Current, body.Instance.OccurrenceId) == SelectionKind.Face)
            {
                _badge.Flash(UiText.NoFaceHere);
                return;
            }
            try { await _selection.SelectAsync(_session.Scene.Graph.Kind, body.Instance.OccurrenceId, face, _run.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _badge.Flash(UiText.SelectionFailed + ex.Message); }
        }

        private async void OnPickedNothing()
        {
            if (_selection == null) return;
            try { await _selection.ClearAsync(_run.Token); }
            catch (Exception) { selectionVisuals.Clear(); }
        }
    }
}
```

`Assets/XrSo/Xr/XrUi.cs`:

```csharp
using UnityEngine;

namespace InventorXrSo.Xr
{
    public static class XrUi
    {
        /// <summary>Let the controller ray (OVRInputModule) press this world-space canvas.</summary>
        public static void MakeInteractive(Canvas canvas, Camera eye)
        {
            canvas.worldCamera = eye;
            if (canvas.GetComponent<OVRRaycaster>() == null) canvas.gameObject.AddComponent<OVRRaycaster>();
        }
    }
}
```

`AppController` references `QrScanner` (Task C8). Until C8 lands, create the stub `Assets/XrSo/Xr/QrScanner.cs` (C8 replaces its body):

```csharp
using System;
using UnityEngine;

namespace InventorXrSo.Xr
{
    public sealed class QrScanner : MonoBehaviour
    {
        public void Scan(Action<string> onResult, Action<string> onError) => onError(Unity.Ui.UiText.NoCamera);
        public void Stop() { }
    }
}
```

- [ ] **Step 9: Scene builder** — set `InventorXrSo.Editor.asmdef` `references` to `["InventorXrSo.Core", "InventorXrSo.Unity", "InventorXrSo.Xr", "Oculus.VR", "Unity.RenderPipelines.Universal.Runtime", "Unity.RenderPipelines.Core.Runtime", "UnityEngine.UI"]`. In `XrSoProjectSetup.CreateMaterials` add:

```csharp
            Create("Ray", Shader.Find("Universal Render Pipeline/Unlit"), m => m.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f)));
```

`Assets/XrSo/Editor/Setup/XrSoSceneBuilder.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Xr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace InventorXrSo.Editor
{
    /// <summary>Builds Assets/XrSo/Scenes/Main.unity from code, so the scene is reproducible.</summary>
    public static class XrSoSceneBuilder
    {
        public const string ScenePath = "Assets/XrSo/Scenes/Main.unity";

        [MenuItem("Inventor XR SO/Build Main Scene")]
        public static void Build()
        {
            XrSoProjectSetup.CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rigPath = AssetDatabase.FindAssets("OVRCameraRig t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/OVRCameraRig.prefab"));
            if (rigPath == null) throw new InvalidOperationException("OVRCameraRig.prefab not found: is com.meta.xr.sdk.core installed?");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(rigPath));
            if (rig.GetComponent<OVRManager>() == null) rig.AddComponent<OVRManager>();
            var passthrough = rig.AddComponent<OVRPassthroughLayer>();
            passthrough.overlayType = OVROverlay.OverlayType.Underlay;
            var centerEye = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            var right = rig.transform.Find("TrackingSpace/RightHandAnchor/RightControllerAnchor") ?? rig.transform.Find("TrackingSpace/RightHandAnchor");

            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cad = new GameObject("CadModel");
            var view = cad.AddComponent<CadSceneView>();
            view.BodyMaterial = Material("CadBody");
            var visuals = cad.AddComponent<SelectionVisuals>();
            visuals.Configure(view, Material("OccurrenceHighlight"), Material("FaceHighlight"));

            var rayObject = new GameObject("ControllerRay");
            rayObject.transform.SetParent(right, false);
            var line = rayObject.AddComponent<LineRenderer>();
            line.widthMultiplier = 0.003f;
            line.useWorldSpace = true;
            line.sharedMaterial = Material("Ray");
            var ray = rayObject.AddComponent<ControllerRay>();
            ray.Configure(right, line);

            var events = new GameObject("EventSystem", typeof(EventSystem));
            var input = events.AddComponent<OVRInputModule>();
            input.rayTransform = right;
            input.joyPadClickButton = OVRInput.Button.PrimaryIndexTrigger;

            var environment = rig.AddComponent<EnvironmentModeController>();
            environment.Configure(centerEye.GetComponent<Camera>(), passthrough);
            var scanner = new GameObject("QrScanner").AddComponent<QrScanner>();
            new GameObject("App").AddComponent<AppController>().Configure(view, visuals, ray, environment, centerEye, scanner);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        public static void BuildBatch()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(XrSoProjectSetup.MaterialsFolder + "/" + name + ".mat")
            ?? throw new InvalidOperationException("Missing material " + name + ": run Inventor XR SO/Configure Project.");
    }
}
```

- [ ] **Step 10: Build the scene and re-run the tests**

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoSceneBuilder.BuildBatch"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode.xml`""
```

Expected: `Assets/XrSo/Scenes/Main.unity` exists; all EditMode tests pass. If `OVRInputModule`/`OVRRaycaster`/`OVRPassthroughLayer` member names differ in the installed SDK, fix the call to the installed API (read the SDK source under `Library/PackageCache/com.meta.xr.sdk.core*/Scripts`) and note it in the commit message.

- [ ] **Step 11: (human, Editor UI) Passthrough support** — ask the user to open `Main.unity`, select `OVRCameraRig`, and in **OVR Manager → Quest Features → General** set *Passthrough Support* to **Supported**, and under *Insight Passthrough* tick **Enable Passthrough**; save and close. Commit the changed scene/`ProjectSettings`.

- [ ] **Step 12: Commit**

```bash
git add "Inventor XR SO/Assets" "Inventor XR SO/ProjectSettings"
git commit -m "feat(xr): Home, pairing flow, status badge and M1 app wiring"
```

### Task C8: QR code scanning

**Files:**
- Create: `Inventor XR SO/Assets/Plugins/ZXing/zxing.dll` (ZXing.Net 0.16.9, Apache-2.0, `lib/netstandard2.0`), `Inventor XR SO/Assets/Plugins/ZXing/LICENSE-ZXing.txt`
- Create: `Assets/XrSo/Runtime/Pairing/QrDecoder.cs`
- Modify: `Assets/XrSo/Xr/QrScanner.cs` (real implementation)
- Modify: `Assets/Plugins/Android/AndroidManifest.xml` (camera permissions)
- Test: `Assets/XrSo/Tests/EditMode/QrDecoderTests.cs`

**Interfaces:**
- Produces: `QrDecoder.Decode(Color32[] pixels, int width, int height) : string` (Unity bottom-up pixels, null when no QR), `QrScanner.Scan(Action<string> onResult, Action<string> onError)`, `Stop()`.

- [ ] **Step 1: Get ZXing.Net** — this downloads a file: **ask the user first** ("download ZXing.Net 0.16.9 from nuget.org, ~1 MB, Apache-2.0, to Inventor XR SO/Assets/Plugins/ZXing?"). After approval:

```powershell
$tmp = Join-Path $env:TEMP "zxing.nupkg.zip"
Invoke-WebRequest "https://www.nuget.org/api/v2/package/ZXing.Net/0.16.9" -OutFile $tmp
Expand-Archive $tmp -DestinationPath "$env:TEMP\zxing" -Force
New-Item -ItemType Directory -Force "Inventor XR SO/Assets/Plugins/ZXing" | Out-Null
Copy-Item "$env:TEMP\zxing\lib\netstandard2.0\zxing.dll" "Inventor XR SO/Assets/Plugins/ZXing/zxing.dll"
Get-ChildItem "$env:TEMP\zxing" -Filter "*LICENSE*" -Recurse | Select-Object -First 1 | Copy-Item -Destination "Inventor XR SO/Assets/Plugins/ZXing/LICENSE-ZXing.txt"
```

If no license file is in the package, write `LICENSE-ZXing.txt` with "ZXing.Net 0.16.9 — Apache License 2.0 — https://github.com/micjahn/ZXing.Net". Add `"zxing.dll"` to the EditMode test asmdef `precompiledReferences`.

- [ ] **Step 2: Write the failing test** — `Assets/XrSo/Tests/EditMode/QrDecoderTests.cs`:

```csharp
using InventorXrSo.Unity.Pairing;
using NUnit.Framework;
using UnityEngine;
using ZXing;
using ZXing.Common;

namespace InventorXrSo.Tests
{
    public class QrDecoderTests
    {
        [Test]
        public void DecodesAPairingQrFromUnityPixels()
        {
            const string payload = "{\"v\":1,\"host\":\"192.168.1.20\",\"port\":8443,\"ott\":\"abc\",\"cert_sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"}";
            var writer = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE, Options = new EncodingOptions { Width = 300, Height = 300, Margin = 2 } };
            var image = writer.Write(payload);   // BGRA32, top row first
            var pixels = new Color32[image.Width * image.Height];
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int src = ((image.Height - 1 - y) * image.Width + x) * 4;   // Unity rows start at the bottom
                pixels[y * image.Width + x] = new Color32(image.Pixels[src + 2], image.Pixels[src + 1], image.Pixels[src], 255);
            }
            Assert.AreEqual(payload, QrDecoder.Decode(pixels, image.Width, image.Height));
        }

        [Test]
        public void ABlankImageHasNoCode() =>
            Assert.IsNull(QrDecoder.Decode(new Color32[64 * 64], 64, 64));
    }
}
```

- [ ] **Step 3: Run to verify failure** — EditMode command. Expected: compile error, `QrDecoder` missing.

- [ ] **Step 4: Implement** — `Assets/XrSo/Runtime/Pairing/QrDecoder.cs`:

```csharp
using UnityEngine;
using ZXing;
using ZXing.Common;

namespace InventorXrSo.Unity.Pairing
{
    public static class QrDecoder
    {
        private static readonly BarcodeReaderGeneric Reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions { PossibleFormats = new[] { BarcodeFormat.QR_CODE }, TryHarder = true },
        };

        /// <summary>Text of the first QR code in a Unity image (rows bottom-up), or null.</summary>
        public static string Decode(Color32[] pixels, int width, int height)
        {
            var rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int sourceRow = (height - 1 - y) * width;   // flip: a mirrored QR would not decode
                for (int x = 0; x < width; x++)
                {
                    var c = pixels[sourceRow + x];
                    int i = (y * width + x) * 4;
                    rgba[i] = c.r; rgba[i + 1] = c.g; rgba[i + 2] = c.b; rgba[i + 3] = 255;
                }
            }
            return Reader.Decode(new RGBLuminanceSource(rgba, width, height, RGBLuminanceSource.BitmapFormat.RGBA32))?.Text;
        }
    }
}
```

Replace `Assets/XrSo/Xr/QrScanner.cs` with:

```csharp
using System;
using System.Collections;
using InventorXrSo.Unity.Pairing;
using InventorXrSo.Unity.Ui;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace InventorXrSo.Xr
{
    /// <summary>Reads the PC's pairing QR through the Quest 3 passthrough camera (Horizon OS camera permission).</summary>
    public sealed class QrScanner : MonoBehaviour
    {
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        private const float TimeoutSeconds = 60f;
        private WebCamTexture _camera;
        private Coroutine _loop;

        public void Scan(Action<string> onResult, Action<string> onError)
        {
            Stop();
            _loop = StartCoroutine(Run(onResult, onError));
        }

        public void Stop()
        {
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
            if (_camera != null)
            {
                _camera.Stop();
                Destroy(_camera);
                _camera = null;
            }
        }

        private IEnumerator Run(Action<string> onResult, Action<string> onError)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                Permission.RequestUserPermission(HeadsetCameraPermission);
                float waited = 0f;
                while (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) && waited < 20f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
                {
                    _loop = null;
                    onError(UiText.CameraDenied);
                    yield break;
                }
            }
#endif
            if (WebCamTexture.devices.Length == 0)
            {
                _loop = null;
                onError(UiText.NoCamera);
                yield break;
            }
            _camera = new WebCamTexture(WebCamTexture.devices[0].name, 1280, 960);
            _camera.Play();
            float deadline = Time.unscaledTime + TimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                yield return new WaitForSecondsRealtime(0.3f);
                if (_camera.width < 32) continue;
                var text = QrDecoder.Decode(_camera.GetPixels32(), _camera.width, _camera.height);
                if (text == null) continue;
                Stop();
                onResult(text);
                yield break;
            }
            Stop();
            onError(UiText.QrTimeout);
        }
    }
}
```

- [ ] **Step 5: Run to verify pass** — EditMode command. Expected: all pass (2 new).

- [ ] **Step 6: (human, Editor UI) Manifest permissions** — ask the user to run *Meta → Tools → Update AndroidManifest.xml* (creates `Assets/Plugins/Android/AndroidManifest.xml` if missing). Then add inside `<manifest>`:

```xml
    <uses-permission android:name="android.permission.CAMERA" />
    <uses-permission android:name="horizonos.permission.HEADSET_CAMERA" />
```

The passthrough camera needs Horizon OS v74 or later on the Quest 3; if the camera stays black, the manual code path is the fallback (spec §4.1).

- [ ] **Step 7: Commit**

```bash
git add "Inventor XR SO/Assets"
git commit -m "feat(xr): pairing QR scan through the Quest 3 passthrough camera"
```

### Task C9: Quest build, end-to-end check with Inventor, documentation

**Files:**
- Create: `Assets/XrSo/Editor/Setup/XrSoBuild.cs`
- Create: `Inventor XR SO/README.md`
- Modify: `README.md` (short "Inventor XR SO (Quest 3)" section), `docs/DEVELOPMENT.md` (M1 entry with results)

**Interfaces:**
- Produces: menu `Inventor XR SO/Build Quest APK` (`XrSoBuild.BuildApkBatch`) → `Inventor XR SO/Builds/InventorXrSo.apk`.

- [ ] **Step 1: Build script** — `Assets/XrSo/Editor/Setup/XrSoBuild.cs`:

```csharp
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace InventorXrSo.Editor
{
    public static class XrSoBuild
    {
        public const string ApkPath = "Builds/InventorXrSo.apk";

        [MenuItem("Inventor XR SO/Build Quest APK")]
        public static void BuildApk()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { XrSoSceneBuilder.ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("APK build " + report.summary.result + " with " + report.summary.totalErrors + " error(s).");
        }

        public static void BuildApkBatch()
        {
            try
            {
                BuildApk();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
```

- [ ] **Step 2: Build**

```powershell
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoBuild.BuildApkBatch" -Log "$env:TEMP\xrso-build.log"
```

Expected: exit code 0, `Inventor XR SO/Builds/InventorXrSo.apk` exists (ignored by git). IL2CPP stripping errors about Newtonsoft are handled by the Unity package's `link.xml`; if the log shows missing members at runtime (step 5), add `Assets/XrSo/link.xml` preserving `InventorXrSo.Core`.

- [ ] **Step 3: (human) Headset and PC preparation** — ask the user to:
  1. Enable developer mode on the Quest 3 and connect it by USB (accept the debugging prompt in the headset).
  2. Allow inbound TCP 8443 in Windows Firewall on the Inventor PC (a security setting: the user does it, e.g. *Windows Defender Firewall → Advanced settings → Inbound Rules → New Rule → Port → TCP 8443 → Private networks*).
  3. Start Inventor 2027 with the experimental add-in build and `INVENTOR_SO_EXPERIMENTAL=1`, open a small test assembly.

- [ ] **Step 4: Install**

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb devices
& $adb install -r "Inventor XR SO/Builds/InventorXrSo.apk"
```

Expected: the headset listed as `device`; `Success`.

- [ ] **Step 5: First run against the test host** (no Inventor needed): `dotnet run --project "Inventor XR SO/Tests~/XrSo.TestHost" -- --lan --churn 20`. On the headset: open *Inventor XR SO* (Unknown Sources), **Inserisci codice** → PC address and code from the console → compare fingerprints → Enter MR → two bolts and a plate at 1:1; trigger on a bolt tints it; trigger again highlights a face; every 20 s the scene refreshes. Capture logs with `& $adb logcat -s Unity` if anything fails.

- [ ] **Step 6: Definition of Done with Inventor (spec §1)** — stop the test host; start the real host:

```powershell
dotnet bridge/src/server-http/bin/Debug/net8.0/Inventor.So.Mcp.Http.dll --http-urls https://0.0.0.0:8443 --http-self-signed --http-token-file "$env:LOCALAPPDATA\InventorSO\inventor-so-mcp\http\tokens.txt" --pair quest3 --enable-experimental
```

On the headset use **Dimentica PC**, then verify and record each item (pass/fail + note):

| # | Check | Result |
|---|---|---|
| 1 | QR pairing (and, separately, code pairing with fingerprint check) | |
| 2 | Home shows PC, backend version, Inventor 2027, active document and kind | |
| 3 | Active assembly rendered 1:1 in front of the user (compare one dimension with a real measurement) | |
| 4 | Trigger on a component → occurrence tinted on headset **and** selected/highlighted in Inventor | |
| 5 | Second trigger on it → face overlay on headset, same face highlighted in Inventor | |
| 6 | Part document: trigger selects faces directly | |
| 7 | Switch active document in Inventor → headset follows within ~5 s | |
| 8 | Edit a parameter in Inventor → only that part's mesh reloads | |
| 9 | Wi-Fi off on the headset → model stays, badge "Offline · sola lettura"; Wi-Fi on → back online without re-pairing | |
| 10 | Restart the app → reconnects with the stored pairing; remove the token line on the PC and restart the host → headset asks to pair again | |
| 11 | Studio VR and Mixed Reality both work from Home | |

Any failing item: stop, use superpowers:systematic-debugging, fix with a test, re-run the table.

- [ ] **Step 7: Documentation** — `Inventor XR SO/README.md`: purpose (link to the product spec and the M1 design), folder layout (Core package, Tests~, Assets/XrSo, Tools~), how to run core tests, EditMode tests, the test host, build/install, pairing on the PC, prerequisites (Unity 6000.6.3f1 + Android module, Quest 3 developer mode, Horizon OS v74+ for QR, firewall rule, experimental tier). In the root `README.md` add a short section linking it. In `docs/DEVELOPMENT.md` add "Inventor XR SO — Milestone 1 — <date>" with: what was built, test counts (backend, core, EditMode), the DoD table results from Step 6, and known limits (no QR without camera permission, 200-definition limit, M1 is read-only).

- [ ] **Step 8: Commit**

```bash
git add "Inventor XR SO" README.md docs/DEVELOPMENT.md
git commit -m "docs(xr): Milestone 1 build, install and verification results"
```

- [ ] **Step 9: Finish** — use superpowers:finishing-a-development-branch (PR from `feat/xr-so-m1`).

---

## Spec coverage

| Spec item (M1 design) | Tasks |
|---|---|
| §1.1 pairing QR + fallback code | A1–A4, B2, C5, C7, C8 |
| §1.2 connect + capabilities | B3, B6, B8 |
| §1.3 Home info | C7 |
| §1.4 scene graph + GLB, 1:1 | B5, B6, B7, C3 |
| §1.5–1.7 select occurrence/face, face id, highlight both sides | B5, B6, B7, C3, C4, C6, C7 |
| §1.8 follow active document | B4, B8, C7 |
| §1.9 offline read-only, reconnect | B8, C7 |
| §3.1 PairingService, self-signed cert, `/pair` errors, audit by client name | A1–A4 (audit: requests after pairing are audited under the new client name by the existing audit filter) |
| §3.2 modules | B1–B9, C1–C8 |
| §4.4 incremental refresh | B6 (asset cache), B7 (asset ids), C3 (mesh reuse) |
| §5 error table | B3 (401, session), B7 (MESH_TOO_LARGE), B8 (offline, NotReady, NoDocument, NeedsPairing), C2/C7 (fingerprint changed) |
| §6 GLB face-map risk | B5 (resolved by design change), C3 test |
| §7 tests | every task; manual DoD C9 |
