using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Experience;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Ui;
using Newtonsoft.Json.Linq;

namespace XrSo.Core.Tests.Experience
{
    public class ActivityTests
    {
        [Fact]
        public void HomeIsStableUntilAnExplicitChoiceAndProtectedWorkCannotBeLost()
        {
            var controller = new ActivityController();
            Assert.Equal(Activity.Home, controller.Current);
            Assert.True(controller.Request(Activity.Design, false));
            int generation = controller.Generation;
            Assert.False(controller.Request(Activity.Home, true));
            Assert.Equal(Activity.Design, controller.Current);
            Assert.Equal(generation, controller.Generation);
            Assert.True(controller.Request(Activity.Presentation, false));
            Assert.True(controller.Generation > generation);
        }

        [Fact]
        public void PairingRetainsIntentWithoutChangingActivity()
        {
            var controller = new ActivityController();
            Assert.False(controller.Request(Activity.Verification, false, true));
            Assert.Equal(Activity.Home, controller.Current);
            Assert.Equal(Activity.Verification, controller.Pending);
            Assert.True(controller.Request(controller.Pending.Value, false));
            Assert.Null(controller.Pending);
            Assert.Equal(Activity.Verification, controller.Current);
        }

        [Theory]
        [InlineData(Activity.Home)]
        [InlineData(Activity.Capture)]
        [InlineData(Activity.Presentation)]
        [InlineData(Activity.Verification)]
        public void NoAuthoringCallsOutsideDesign(Activity activity)
        {
            foreach (var tool in new[] { "inventor_plan_change", "inventor_commit_plan", "inventor_atomic_batch",
                "inventor_history_xr", "inventor_sample_parameter_motion", "inventor_create_part", "inventor_future_tool" })
                Assert.False(ActivityPolicy.AllowsTool(activity, tool), tool);
        }

        [Theory]
        [InlineData("design.extrude")]
        [InlineData("assembly.open")]
        [InlineData("commit.apply")]
        [InlineData("doc.save")]
        [InlineData("inspect.section")]
        [InlineData("inspect.verify.health")]
        [InlineData("inspect.visibility.hide")]
        [InlineData("inspect.pick.0")]
        public void PresentationHidesAllTechnicalAndDynamicVerificationActions(string id)
            => Assert.False(ActivityPolicy.AllowsAction(Activity.Presentation, id));

        [Fact]
        public void VerificationHasM7ButDesignOnlyHasCompactInspection()
        {
            foreach (var id in new[] { "inspect.visibility.hide", "inspect.verify.health", "inspect.verify.distance" })
            {
                Assert.True(ActivityPolicy.AllowsAction(Activity.Verification, id));
                Assert.False(ActivityPolicy.AllowsAction(Activity.Design, id));
            }
            Assert.True(ActivityPolicy.AllowsAction(Activity.Design, "inspect.section"));
        }

        [Fact]
        public void CapturedPaletteRingAndVoiceActionsCannotOutliveTheirActivity()
        {
            int writes = 0;
            var controller = new ActivityController();
            controller.Request(Activity.Design, false);
            var provider = new Provider(new XrAction("design.extrude", "Estrusione", "strumenti", () => true, () => writes++));
            var catalog = new ActionCatalog(new Provider())
            { ActivityProbe = () => controller.Current, ActivityGenerationProbe = () => controller.Generation };
            catalog.SetActive(provider);
            var oldPalette = catalog.Find("design.extrude");
            var oldRing = catalog.Context(SelectionKind.Face).Single();
            var oldVoice = catalog.ResolveVoice("Estrusione").Action;
            controller.Request(Activity.Presentation, false);
            Assert.Null(catalog.Find("design.extrude"));
            Assert.Empty(catalog.Palette("strumenti"));
            Assert.Empty(catalog.Context(SelectionKind.Face));
            Assert.Equal(VoiceMatchKind.NotFound, catalog.ResolveVoice("Estrusione").Kind);
            Assert.False(oldPalette.TryInvoke());
            Assert.False(oldRing.TryInvoke());
            Assert.False(oldVoice.TryInvoke());
            controller.Request(Activity.Design, false);
            Assert.False(oldPalette.TryInvoke());
            Assert.True(catalog.TryInvoke("design.extrude"));
            Assert.Equal(1, writes);
        }

        [Fact]
        public async Task BackendGuardRejectsCallsBeforeTransportIncludingLateHistoryCallbacks()
        {
            var spy = new TransportSpy();
            var controller = new ActivityController();
            controller.Request(Activity.Presentation, false);
            var client = new McpClient(spy, "https://example.test", "token")
            { ToolAllowed = tool => ActivityPolicy.AllowsTool(controller.Current, tool) };
            foreach (string tool in new[] { "inventor_plan_change", "inventor_commit_plan", "inventor_history_xr" })
            {
                var error = await Assert.ThrowsAsync<McpException>(() => client.CallToolAsync(tool, new JObject(), CancellationToken.None));
                Assert.Equal("ACTIVITY_FORBIDDEN", error.Code);
            }
            Assert.Equal(0, spy.Calls);
        }

        [Fact]
        public async Task BackendCannotPreviewOrUndoFromAReadOnlyActivity()
        {
            var spy = new TransportSpy();
            var backend = new InventorBackend(spy, new PairedServer("example.test", 443, "", "test", "token"), new MemoryAssetCache())
            { ToolAllowed = tool => ActivityPolicy.AllowsTool(Activity.Presentation, tool) };
            var state = new DocumentState("document", "revision", "visual");
            var previewError = await Assert.ThrowsAsync<McpException>(() => backend.PreviewDesignAsync(state,
                new JArray(DesignOperations.Parameter("Length", 20, "mm")), CancellationToken.None));
            Assert.Equal("ACTIVITY_FORBIDDEN", previewError.Code);
            var historyError = await Assert.ThrowsAsync<McpException>(() => backend.ApplyHistoryAsync(
                new DesignHistory(state, true, false, "old-ticket"), false, CancellationToken.None));
            Assert.Equal("ACTIVITY_FORBIDDEN", historyError.Code);
            Assert.Equal(0, spy.Calls);
        }

        private sealed class Provider : IActionProvider
        {
            private readonly XrAction[] _actions;
            public Provider(params XrAction[] actions) { _actions = actions; }
            public IReadOnlyList<XrTab> Tabs => new[] { new XrTab("strumenti", "Strumenti") };
            public IEnumerable<XrAction> Actions => _actions;
            public IEnumerable<XrAction> ContextActions(SelectionKind selection) => _actions;
            public CommitBarState CommitBar => null;
        }
        private sealed class TransportSpy : IHttpTransport
        {
            public int Calls;
            public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct) => throw new InvalidOperationException();
            public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
            { Calls++; throw new InvalidOperationException("Forbidden call reached transport"); }
        }
    }
}
