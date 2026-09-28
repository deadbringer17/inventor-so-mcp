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
