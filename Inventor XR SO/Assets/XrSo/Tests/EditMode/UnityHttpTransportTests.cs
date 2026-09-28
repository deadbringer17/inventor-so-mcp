using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
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
        public IEnumerator QrPairsOverPinnedUnityTransportAndRunsMcp()
        {
            using (var host = TestHostProcess.Start())
            {
                var client = new PairingClient(trust => new UnityHttpTransport(trust));
                var pairing = client.PairWithQrAsync(PairingPayload.Parse(host.QrPayload), "quest-test", CancellationToken.None);
                yield return Await(pairing);
                Assert.IsNull(pairing.Exception, pairing.Exception?.GetBaseException().GetType().Name);
                Assert.AreEqual(host.CertSha256, pairing.Result.CertSha256);
                var mcp = new McpClient(new UnityHttpTransport(ServerTrust.Pinned(pairing.Result.CertSha256)),
                    pairing.Result.BaseUrl, pairing.Result.Token);
                var call = Call(mcp);
                yield return Await(call);
                Assert.IsNull(call.Exception, call.Exception?.GetBaseException().GetType().Name);
                Assert.IsTrue((bool)call.Result["capabilities"]["xr_mesh"]);
            }
        }

        [UnityTest]
        public IEnumerator ManualPairingProbesThenPinsTheConfirmedCertificate()
        {
            using (var host = TestHostProcess.Start())
            {
                var client = new PairingClient(trust => new UnityHttpTransport(trust));
                var uri = new Uri(host.BaseUrl);
                var probe = client.ProbeFingerprintAsync(uri.Host, uri.Port, CancellationToken.None);
                yield return Await(probe);
                Assert.IsNull(probe.Exception);
                Assert.AreEqual(host.CertSha256, probe.Result);
                var pair = client.PairAsync(uri.Host, uri.Port, host.PairCode, ServerTrust.Pinned(probe.Result), "quest-test", CancellationToken.None);
                yield return Await(pair);
                Assert.IsNull(pair.Exception, pair.Exception?.GetBaseException().GetType().Name);
                Assert.AreEqual(host.CertSha256, pair.Result.CertSha256);
            }
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

        [UnityTest]
        public IEnumerator CancellingFromAThreadPoolThreadAbortsWithoutUnityErrors()
        {
            using (var host = TestHostProcess.Start())
            {
                var mcp = new McpClient(new UnityHttpTransport(ServerTrust.Pinned(host.CertSha256)), host.BaseUrl, host.EditorToken);
                var init = Subscribe(mcp);
                yield return Await(init);
                Assert.IsNull(init.Exception, init.Exception?.ToString());

                var cts = new CancellationTokenSource();
                // No --churn: the server never pushes an event, so this stays pending until cancelled.
                var stream = mcp.RunEventStreamAsync(_ => { }, cts.Token);
                yield return null; // let SendWebRequest actually start before cancelling.

                // Cancel from a thread-pool thread: the registration callback then runs off the main
                // thread, which is exactly the race UnityHttpTransport must hop back from before
                // calling UnityWebRequest.Abort().
                Task.Run(() => cts.Cancel());

                yield return Await(stream.ContinueWith(_ => { }));
                Assert.IsTrue(stream.IsCanceled, stream.Exception?.ToString());
                LogAssert.NoUnexpectedReceived();
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
