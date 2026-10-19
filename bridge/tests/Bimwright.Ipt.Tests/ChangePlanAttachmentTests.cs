using System;
using System.Linq;
using System.IO;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Server.Tools;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Imports;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class ChangePlanAttachmentTests
{
    private sealed class Fixture
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
        public readonly ImportResourceStore Imports;
        public readonly ChangePlanStore Plans;
        public readonly string Id;
        public readonly ImportResourceStore.Lease Lease;
        public Fixture()
        {
            Imports = new(new ImportResourceStorePolicy(8, 16, 8, 1024, 32, TimeSpan.FromSeconds(1), new[] { "bmp" }), () => Now);
            Plans = new(TimeSpan.FromMinutes(15), () => Now);
            Id = Imports.Put("alice", new byte[] { 1, 2, 3 }, "bmp").Id;
            Lease = Imports.Acquire("alice", new[] { Id })!;
        }
        public ChangePlanStore.Plan Create() => Plans.Create("alice", "doc", "rev", Ops(), null, null, Lease);
        public void ExpireImport()
        {
            Now = Now.AddSeconds(2);
            Assert.Null(Imports.Describe("alice", Id));
            Assert.Equal(3, Imports.StoredBytes);
            Assert.Equal(new byte[] { 1, 2, 3 }, Lease.Read(Id));
        }
        public void AssertReleased()
        {
            Assert.Equal(0, Imports.StoredBytes);
            Assert.Throws<ObjectDisposedException>(() => Lease.Read(Id));
        }
    }
    private static JArray Ops() => new(new JObject { ["command"] = "set_parameter", ["arguments"] = new JObject { ["name"] = "D", ["value"] = "2 mm" } });

    [Fact]
    public void TakenPlanOwnsBytesPastExpiryUntilConsumerFinishes()
    {
        var f = new Fixture(); var created = f.Create(); f.ExpireImport();
        var taken = f.Plans.Take(created.Id, "alice", "doc", "rev");
        f.Plans.Discard(created.Id); f.Plans.Dispose(); created.Dispose();
        Assert.Equal(new byte[] { 1, 2, 3 }, f.Lease.Read(f.Id));
        taken.Dispose(); taken.Dispose(); f.AssertReleased();
    }

    [Fact]
    public void CreateAndListViewsCannotDisposeStoredAttachmentOrMutateListedContent()
    {
        var f = new Fixture(); var created = f.Create();
        created.Preview["value"] = 2; created.Impact["value"] = 3;
        var listed = Assert.Single(f.Plans.List("alice"));
        listed.Operations[0]!["arguments"]!["value"] = "99 mm";
        listed.Preview["value"] = 99; listed.Impact["value"] = 99;
        listed.Dispose(); created.Dispose(); f.ExpireImport();
        using var taken = f.Plans.Take(created.Id, "alice", "doc", "rev");
        Assert.Equal("2 mm", (string?)taken.Operations[0]!["arguments"]!["value"]);
        Assert.Equal(2, (int)taken.Preview["value"]!); Assert.Equal(3, (int)taken.Impact["value"]!);
        Assert.DoesNotContain("attachment", taken.ToJson().ToString(), StringComparison.OrdinalIgnoreCase);
        taken.Dispose(); f.AssertReleased();
    }

    [Fact]
    public void DiscardReleasesLeaseAndIsIdempotent()
    {
        var f = new Fixture(); var plan = f.Create(); f.ExpireImport();
        f.Plans.Discard(plan.Id); f.Plans.Discard(plan.Id); f.AssertReleased();
    }

    [Theory]
    [InlineData("list")] [InlineData("create")] [InlineData("take")]
    public void EverySweepEntryPointReleasesExpiredPlans(string operation)
    {
        var f = new Fixture(); var plan = f.Create(); f.ExpireImport(); f.Now = f.Now.AddMinutes(15);
        if (operation == "list") Assert.Empty(f.Plans.List("alice"));
        if (operation == "create") f.Plans.Create("alice", "doc", "rev", Ops(), null, null);
        if (operation == "take") Assert.Throws<PlanException>(() => f.Plans.Take(plan.Id, "alice", "doc", "rev"));
        f.AssertReleased(); f.Plans.Dispose();
    }

    [Fact]
    public void OwnerEvictionReleasesOldestLeaseWithoutEvictingOtherOwners()
    {
        var f = new Fixture(); var plan = f.Create(); f.ExpireImport();
        f.Plans.Create("bob", "doc", "rev", Ops(), null, null);
        for (int i = 0; i < ChangePlanStore.MaxPlansPerOwner; i++)
            f.Plans.Create("alice", "doc", "rev", Ops(), null, null);
        Assert.DoesNotContain(f.Plans.List("alice"), p => p.Id == plan.Id);
        Assert.Single(f.Plans.List("bob")); f.AssertReleased(); f.Plans.Dispose();
    }

    [Theory]
    [InlineData("document")] [InlineData("revision")] [InlineData("hash")]
    public void FailedTakeValidationConsumesPlanAndReleasesLease(string mismatch)
    {
        var f = new Fixture(); var plan = f.Create(); f.ExpireImport();
        if (mismatch == "hash") plan.Operations[0]!["arguments"]!["value"] = "3 mm";
        var error = Assert.Throws<PlanException>(() => f.Plans.Take(plan.Id, "alice", mismatch == "document" ? "other" : "doc", mismatch == "revision" ? "other" : "rev"));
        Assert.Equal(InventorErrorCodes.PLAN_MISMATCH, error.Code);
        Assert.Empty(f.Plans.List("alice")); f.AssertReleased();
    }

    [Fact]
    public void ForeignTakeDoesNotReleaseOrConsumeOwnersLease()
    {
        var f = new Fixture(); var plan = f.Create(); f.ExpireImport();
        Assert.Equal(InventorErrorCodes.PLAN_NOT_FOUND, Assert.Throws<PlanException>(() => f.Plans.Take(plan.Id, "bob", "doc", "rev")).Code);
        Assert.Equal(new byte[] { 1, 2, 3 }, f.Lease.Read(f.Id));
        f.Plans.Dispose(); f.AssertReleased();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CreateFailureReleasesIncomingLease(bool disposed)
    {
        var f = new Fixture(); f.ExpireImport();
        if (disposed) f.Plans.Dispose();
        if (disposed) Assert.Throws<ObjectDisposedException>(() => f.Create());
        else Assert.Throws<NullReferenceException>(() => f.Plans.Create("alice", "doc", "rev", null!, null, null, f.Lease));
        f.AssertReleased();
    }

    [Fact]
    public void StoreDisposalReleasesPendingPlansAndRefusesNewAccess()
    {
        var f = new Fixture(); f.Create(); f.ExpireImport(); f.Plans.Dispose(); f.Plans.Dispose();
        f.AssertReleased(); Assert.Throws<ObjectDisposedException>(() => f.Plans.List("alice"));
    }

    private sealed class CountedAttachment : IDisposable
    {
        private readonly IDisposable _inner;
        public int Count;
        public CountedAttachment(IDisposable inner) => _inner = inner;
        public void Dispose() { Interlocked.Increment(ref Count); _inner.Dispose(); }
    }

    [Fact]
    public async Task ConcurrentTakeDiscardAndStoreDisposalTransferOwnershipExactlyOnce()
    {
        for (int i = 0; i < 40; i++)
        {
            var f = new Fixture(); var counted = new CountedAttachment(f.Lease);
            var plan = f.Plans.Create("alice", "doc", "rev", Ops(), null, null, counted); f.ExpireImport();
            ChangePlanStore.Plan? taken = null;
            using var ready = new ManualResetEventSlim();
            await Task.WhenAll(Task.Run(() =>
            {
                ready.Wait();
                try { taken = f.Plans.Take(plan.Id, "alice", "doc", "rev"); }
                catch (PlanException) { }
                catch (ObjectDisposedException) { }
            }), Task.Run(() => { ready.Wait(); f.Plans.Discard(plan.Id); }),
                Task.Run(() => { ready.Set(); f.Plans.Dispose(); }));
            if (taken != null)
            {
                Assert.Equal(0, counted.Count); Assert.Equal(new byte[] { 1, 2, 3 }, f.Lease.Read(f.Id));
                await Task.WhenAll(Task.Run(taken.Dispose), Task.Run(taken.Dispose));
            }
            Assert.Equal(1, counted.Count); f.AssertReleased();
        }
    }

    private sealed class ThrowsOnDispose : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("private source path must not escape");
    }

    [Fact]
    public void ThrowingAttachmentDoesNotPreventOtherCleanupOrMaskValidationFailure()
    {
        var f = new Fixture(); f.Create(); f.ExpireImport();
        var bad = f.Plans.Create("alice", "doc", "rev", Ops(), null, null, new ThrowsOnDispose());
        Assert.Equal(InventorErrorCodes.PLAN_MISMATCH, Assert.Throws<PlanException>(() => f.Plans.Take(bad.Id, "alice", "wrong", "rev")).Code);
        f.Plans.Create("alice", "doc", "rev", Ops(), null, null, new ThrowsOnDispose());
        f.Plans.Dispose(); f.AssertReleased();
    }
    private sealed class Caller : ICallerIdentity
    {
        public string Client => "alice";
        public string? SessionId => null;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CommitConsumerReleasesLeaseOnSuccessAndGatewayFailure(bool fail)
    {
        string directory = Path.Combine(Path.GetTempPath(), "plan-lease-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var fake = new FakeAddIn(Path.Combine(directory, "targets"));
            fake.Override = request => request.Command != "atomic_batch" ? null : fail
                ? InventorCommandResult.Fail(request.Id, InventorErrorCodes.STALE_REVISION, "Document changed.", new())
                : InventorCommandResult.Success(request.Id, new JObject { ["status"] = "committed" }, new());
            var config = new InventorMcpConfig { DescriptorDirectory = fake.DescriptorDirectory,
                AssetDirectory = Path.Combine(directory, "assets"), TimeoutMs = 5000 };
            var f = new Fixture(); var plan = f.Create(); f.ExpireImport();
            var tools = new PlanningTools(new PluginClient(config), f.Plans, config, new Caller(), new AssetStore(config));
            var result = JObject.Parse(await tools.CommitPlan(plan.Id, "doc", "rev"));
            if (fail) Assert.Equal(InventorErrorCodes.STALE_REVISION, (string?)result["error"]?["code"]);
            else Assert.Equal("committed", (string?)result["status"]);
            f.AssertReleased(); Assert.Empty(f.Plans.List("alice")); f.Plans.Dispose();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CommitConsumerReleasesLeaseWhenCancelled()
    {
        string directory = Path.Combine(Path.GetTempPath(), "plan-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var fake = new FakeAddIn(Path.Combine(directory, "targets"));
            var config = new InventorMcpConfig { DescriptorDirectory = fake.DescriptorDirectory,
                AssetDirectory = Path.Combine(directory, "assets"), TimeoutMs = 5000 };
            var f = new Fixture(); var plan = f.Create(); f.ExpireImport();
            var tools = new PlanningTools(new PluginClient(config), f.Plans, config, new Caller(), new AssetStore(config));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.CommitPlan(plan.Id, "doc", "rev", cancellation.Token));
            f.AssertReleased(); f.Plans.Dispose();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CancellationDuringImpactDiscardsUndeliveredPlanAndPropagatesCancellation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "plan-impact-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var fake = new FakeAddIn(Path.Combine(directory, "targets"));
            using var plans = new ChangePlanStore();
            using var cancellation = new CancellationTokenSource();
            bool dependencyReached = false, planExistedDuringImpact = false;
            fake.Override = request =>
            {
                if (request.Command == "atomic_batch")
                    return InventorCommandResult.Success(request.Id, new JObject { ["status"] = "preview" }, new());
                if (request.Command != "get_dependencies") return null;
                dependencyReached = true;
                planExistedDuringImpact = plans.List("alice").Count == 1;
                cancellation.Cancel();
                return InventorCommandResult.Success(request.Id, new JObject(), new());
            };
            var config = new InventorMcpConfig { DescriptorDirectory = fake.DescriptorDirectory,
                AssetDirectory = Path.Combine(directory, "assets"), TimeoutMs = 5000, EnableExperimental = true };
            var tools = new PlanningTools(new PluginClient(config), plans, config, new Caller(), new AssetStore(config));
            var operations = new[] { new AtomicOperation { Command = "set_parameter",
                Arguments = System.Text.Json.JsonSerializer.SerializeToElement(new { name = "D", value = "2 mm" }) } };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                tools.PlanChange("doc", "rev", operations, ct: cancellation.Token));
            Assert.True(dependencyReached); Assert.True(planExistedDuringImpact);
            Assert.True(cancellation.IsCancellationRequested); Assert.Empty(plans.List("alice"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

}
