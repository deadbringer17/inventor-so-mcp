using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

public sealed class InventorMcpConfig
{
    public bool ReadOnly { get; set; }
    public bool EnableSendCode { get; set; }
    public bool EnableToolBaker { get; set; } = true;
    public bool EnableAdaptiveBake { get; set; }
    public bool FullAccess { get; set; }
    /// <summary>Expose the experimental tier (implemented, not yet live-verified). See docs §26.3.</summary>
    public bool EnableExperimental { get; set; }
    /// <summary>"stdio" or "http"; set by the host, not by configuration.</summary>
    public string Transport { get; set; } = "stdio";

    // --- Remote host (Inventor.So.Mcp.Http) ---
    /// <summary>Kestrel URLs. Loopback by default; a non-loopback URL needs HTTPS or HttpAllowInsecureLan.</summary>
    public List<string> HttpUrls { get; set; } = new() { "http://127.0.0.1:8787" };
    /// <summary>PFX certificate for HTTPS URLs. The password comes only from INVENTOR_SO_HTTP_CERT_PASSWORD.</summary>
    public string? HttpCertificatePath { get; set; }
    public string? HttpCertificatePassword { get; set; }
    /// <summary>Token file, one "name:token" per line. Names identify clients in the audit log.</summary>
    public string? HttpTokenFile { get; set; }
    /// <summary>Single token from INVENTOR_SO_HTTP_TOKEN (client name "default").</summary>
    public string? HttpToken { get; set; }
    public bool HttpAllowInsecureLan { get; set; }
    public List<string> HttpAllowedOrigins { get; set; } = new();
    public int HttpRateLimitPerMinute { get; set; } = 240;
    /// <summary>Public base URL used to build asset_url (e.g. https://pc.lan:8787). Null in stdio mode.</summary>
    public string? PublicBaseUrl { get; set; }

    // --- Asset store ---
    public string AssetDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "inventor-so-mcp", "assets");
    public int AssetTtlMinutes { get; set; } = 60;
    public long AssetMaxBytes { get; set; } = 64L * 1024 * 1024;
    public long AssetTotalBytes { get; set; } = 1024L * 1024 * 1024;

    // --- Audit ---
    public bool AuditEnabled { get; set; } = true;
    public string AuditDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "inventor-so-mcp", "audit");

    // --- Release packages ---
    public string ReleaseDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "releases");
    public int TimeoutMs { get; set; } = 30000;
    public int MaxResponseBytes { get; set; } = 5_000_000;
    public string? TargetId { get; set; }
    public List<string> Toolsets { get; set; } = new();

    public string DescriptorDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "inventor-so-mcp");
    public string BakeDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "inventor-so-mcp", "baked");

    public static InventorMcpConfig Load(string[] args)
    {
        var config = new InventorMcpConfig();
        ApplyJson(config, args);   // lowest precedence
        ApplyEnv(config);          // middle
        ApplyCli(config, args);    // highest
        return config;
    }

    // --- precedence layer 1: JSON file (via --config <path>) ---
    private static void ApplyJson(InventorMcpConfig c, string[] args)
    {
        var path = ValueAfter(args, "--config");
        if (path is null || !File.Exists(path)) return;
        var o = JObject.Parse(File.ReadAllText(path));
        if (o["readOnly"] is { } ro) c.ReadOnly = ro.Value<bool>();
        if (o["enableSendCode"] is { } sc) c.EnableSendCode = sc.Value<bool>();
        if (o["enableToolBaker"] is { } tb) c.EnableToolBaker = tb.Value<bool>();
        if (o["enableAdaptiveBake"] is { } ab) c.EnableAdaptiveBake = ab.Value<bool>();
        if (o["fullAccess"] is { } fa) c.FullAccess = fa.Value<bool>();
        if (o["timeoutMs"] is { } tm) c.TimeoutMs = tm.Value<int>();
        if (o["maxResponseBytes"] is { } mb) c.MaxResponseBytes = mb.Value<int>();
        if (o["target"] is { } tg) c.TargetId = tg.Value<string>();
        if (o["toolsets"] is JArray arr) c.Toolsets = arr.Select(x => x.Value<string>()!).ToList();
        if (o["enableExperimental"] is { } ex) c.EnableExperimental = ex.Value<bool>();
        if (o["httpUrls"] is JArray urls) c.HttpUrls = urls.Select(x => x.Value<string>()!).ToList();
        if (o["httpCertificatePath"] is { } cp) c.HttpCertificatePath = cp.Value<string>();
        if (o["httpTokenFile"] is { } tf) c.HttpTokenFile = tf.Value<string>();
        if (o["httpAllowInsecureLan"] is { } il) c.HttpAllowInsecureLan = il.Value<bool>();
        if (o["httpAllowedOrigins"] is JArray origins) c.HttpAllowedOrigins = origins.Select(x => x.Value<string>()!).ToList();
        if (o["httpRateLimitPerMinute"] is { } rl) c.HttpRateLimitPerMinute = rl.Value<int>();
        if (o["publicBaseUrl"] is { } pb) c.PublicBaseUrl = pb.Value<string>();
        if (o["assetDirectory"] is { } ad) c.AssetDirectory = ad.Value<string>()!;
        if (o["assetTtlMinutes"] is { } at) c.AssetTtlMinutes = at.Value<int>();
        if (o["assetMaxBytes"] is { } am) c.AssetMaxBytes = am.Value<long>();
        if (o["assetTotalBytes"] is { } atb) c.AssetTotalBytes = atb.Value<long>();
        if (o["auditEnabled"] is { } ae) c.AuditEnabled = ae.Value<bool>();
        if (o["auditDirectory"] is { } adir) c.AuditDirectory = adir.Value<string>()!;
        if (o["releaseDirectory"] is { } rd) c.ReleaseDirectory = rd.Value<string>()!;
    }

    // --- precedence layer 2: environment variables ---
    private static void ApplyEnv(InventorMcpConfig c)
    {
        if (Bool("BIMWRIGHT_INVENTOR_READ_ONLY") is { } ro) c.ReadOnly = ro;
        if (Bool("BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE") is { } sc) c.EnableSendCode = sc;
        if (Bool("BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER") is { } tb) c.EnableToolBaker = tb;
        if (Bool("BIMWRIGHT_INVENTOR_ENABLE_ADAPTIVE_BAKE") is { } ab) c.EnableAdaptiveBake = ab;
        if (Bool("BIMWRIGHT_INVENTOR_FULL_ACCESS") is { } fa) c.FullAccess = fa;
        if (Int("BIMWRIGHT_INVENTOR_TIMEOUT_MS") is { } tm) c.TimeoutMs = tm;
        if (Int("BIMWRIGHT_INVENTOR_MAX_RESPONSE_BYTES") is { } mb) c.MaxResponseBytes = mb;
        var tg = Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_TARGET");
        if (!string.IsNullOrWhiteSpace(tg)) c.TargetId = tg;
        if (Bool("INVENTOR_SO_EXPERIMENTAL") is { } ex) c.EnableExperimental = ex;
        var urls = Environment.GetEnvironmentVariable("INVENTOR_SO_HTTP_URLS");
        if (!string.IsNullOrWhiteSpace(urls)) c.HttpUrls = SplitCsv(urls);
        var cert = Environment.GetEnvironmentVariable("INVENTOR_SO_HTTP_CERT");
        if (!string.IsNullOrWhiteSpace(cert)) c.HttpCertificatePath = cert;
        var certPassword = Environment.GetEnvironmentVariable("INVENTOR_SO_HTTP_CERT_PASSWORD");
        if (!string.IsNullOrEmpty(certPassword)) c.HttpCertificatePassword = certPassword;
        var tokenFile = Environment.GetEnvironmentVariable("INVENTOR_SO_HTTP_TOKEN_FILE");
        if (!string.IsNullOrWhiteSpace(tokenFile)) c.HttpTokenFile = tokenFile;
        var token = Environment.GetEnvironmentVariable("INVENTOR_SO_HTTP_TOKEN");
        if (!string.IsNullOrWhiteSpace(token)) c.HttpToken = token.Trim();
        var origins = Environment.GetEnvironmentVariable("INVENTOR_SO_HTTP_ORIGINS");
        if (!string.IsNullOrWhiteSpace(origins)) c.HttpAllowedOrigins = SplitCsv(origins);
        if (Bool("INVENTOR_SO_HTTP_ALLOW_INSECURE_LAN") is { } lan) c.HttpAllowInsecureLan = lan;
        if (Int("INVENTOR_SO_HTTP_RATE_LIMIT") is { } rate) c.HttpRateLimitPerMinute = rate;
        var publicUrl = Environment.GetEnvironmentVariable("INVENTOR_SO_PUBLIC_URL");
        if (!string.IsNullOrWhiteSpace(publicUrl)) c.PublicBaseUrl = publicUrl.Trim();
        if (Bool("INVENTOR_SO_AUDIT") is { } audit) c.AuditEnabled = audit;
        var ts = Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_TOOLSETS");
        if (!string.IsNullOrWhiteSpace(ts)) c.Toolsets = SplitCsv(ts);
    }

    // --- precedence layer 3: CLI flags (highest) ---
    private static void ApplyCli(InventorMcpConfig c, string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--read-only":            c.ReadOnly = NextBool(args, ref i, true); break;
                case "--enable-send-code":     c.EnableSendCode = true; break;
                case "--disable-toolbaker":    c.EnableToolBaker = false; break;
                case "--enable-adaptive-bake": c.EnableAdaptiveBake = true; break;
                case "--full-access":          c.FullAccess = true; break;
                case "--enable-experimental":  c.EnableExperimental = true; break;
                case "--http-urls":            c.HttpUrls = SplitCsv(Next(args, ref i)); break;
                case "--http-cert":            c.HttpCertificatePath = Next(args, ref i); break;
                case "--http-token-file":      c.HttpTokenFile = Next(args, ref i); break;
                case "--http-allow-insecure-lan": c.HttpAllowInsecureLan = true; break;
                case "--http-origins":         c.HttpAllowedOrigins = SplitCsv(Next(args, ref i)); break;
                case "--http-rate-limit":      if (int.TryParse(Next(args, ref i), out var r)) c.HttpRateLimitPerMinute = r; break;
                case "--public-url":           c.PublicBaseUrl = Next(args, ref i); break;
                case "--no-audit":             c.AuditEnabled = false; break;
                case "--toolsets":             c.Toolsets = SplitCsv(Next(args, ref i)); break;
                case "--target":               c.TargetId = Next(args, ref i); break;
                case "--timeout-ms":           if (int.TryParse(Next(args, ref i), out var t)) c.TimeoutMs = t; break;
                case "--max-response-bytes":   if (int.TryParse(Next(args, ref i), out var m)) c.MaxResponseBytes = m; break;
            }
        }
    }

    private static List<string> SplitCsv(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string? ValueAfter(string[] a, string flag)
    {
        var i = Array.IndexOf(a, flag);
        return (i >= 0 && i + 1 < a.Length) ? a[i + 1] : null;
    }
    private static string Next(string[] a, ref int i) => (i + 1 < a.Length) ? a[++i] : "";
    private static bool NextBool(string[] a, ref int i, bool bareValue)
    {
        if (i + 1 < a.Length && bool.TryParse(a[i + 1], out var b)) { i++; return b; }
        return bareValue;
    }
    private static bool? Bool(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return null;
        return v.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
    private static int? Int(string name)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : null;
}
