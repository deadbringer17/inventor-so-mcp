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
