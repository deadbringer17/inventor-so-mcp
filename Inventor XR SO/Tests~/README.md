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
