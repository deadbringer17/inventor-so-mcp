using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Audit;
using Bimwright.Ipt.Server.Events;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class BomAnalysisTests
{
    private static JObject Bom(params (string pn, string? desc, string path, double qty)[] rows) => new()
    {
        ["bom"] = new JArray(rows.Select(r => new JObject { ["part_number"] = r.pn, ["description"] = r.desc, ["path"] = r.path, ["qty"] = r.qty })),
        ["truncated"] = false,
    };

    [Fact]
    public void FindsDuplicatesBlanksAndBadQuantities()
    {
        var rows = BomAnalysis.ReadRows(Bom(("P-1", "Plate", "a.ipt", 2), ("P-1", "Plate", "b.ipt", 1), ("", "Loose", "c.ipt", 1), ("P-2", null, "d.ipt", 0)));
        var result = BomAnalysis.Validate(rows, truncated: true);
        Assert.False((bool)result["valid"]!);
        var codes = result["findings"]!.Select(f => (string)f["code"]!).ToArray();
        Assert.Contains("PART_NUMBER_DUPLICATE", codes);
        Assert.Contains("PART_NUMBER_MISSING", codes);
        Assert.Contains("QUANTITY_INVALID", codes);
        Assert.Contains("DESCRIPTION_MISSING", codes);
        Assert.Contains("BOM_TRUNCATED", codes);
    }

    [Fact]
    public void CleanBomIsValid()
    {
        var result = BomAnalysis.Validate(BomAnalysis.ReadRows(Bom(("P-1", "Plate", "a.ipt", 2), ("P-2", "Bolt", "b.ipt", 8))), false);
        Assert.True((bool)result["valid"]!);
        Assert.Equal(10.0, (double)result["total_quantity"]!);
    }

    [Fact]
    public void CompareReportsAddedRemovedAndChanged()
    {
        var before = BomAnalysis.ReadRows(Bom(("P-1", "Plate", "a.ipt", 2), ("P-2", "Bolt", "b.ipt", 8)));
        var after = BomAnalysis.ReadRows(Bom(("P-1", "Plate v2", "a.ipt", 3), ("P-3", "Nut", "c.ipt", 8)));
        var diff = BomAnalysis.Compare(before, after);
        Assert.False((bool)diff["identical"]!);
        Assert.Equal("P-3", (string?)diff["added"]![0]!["part_number"]);
        Assert.Equal("P-2", (string?)diff["removed"]![0]!["part_number"]);
        var changes = diff["changed"]![0]!["changes"]!;
        Assert.Equal(3.0, (double)changes["qty"]!["after"]!);
        Assert.Equal("Plate v2", (string?)changes["description"]!["after"]);
    }

    [Fact]
    public void CompareMergesRowsSharingAPartNumber()
    {
        var before = BomAnalysis.ReadRows(Bom(("P-1", "Plate", "a.ipt", 1), ("P-1", "Plate", "a.ipt", 1)));
        var after = BomAnalysis.ReadRows(Bom(("P-1", "Plate", "a.ipt", 2)));
        Assert.True((bool)BomAnalysis.Compare(before, after)["identical"]!);
    }

    [Fact]
    public void CsvQuotesAndNeutralisesFormulas()
    {
        var csv = BomAnalysis.ToCsv(BomAnalysis.ReadRows(Bom(("=HYPERLINK(1)", "a, \"b\"", "x.ipt", 1))));
        var line = csv.Split("\r\n")[1];
        Assert.StartsWith("'=HYPERLINK(1),\"a, \"\"b\"\"\",1,", line);
    }

    [Fact]
    public void GarbageBaselineIsRefused() => Assert.Throws<System.ArgumentException>(() => BomAnalysis.ReadRows(new JValue(3)));
}

public sealed class ChangePlanStoreTests
{
    private System.DateTimeOffset _now = new(2026, 9, 26, 8, 0, 0, System.TimeSpan.Zero);
    private ChangePlanStore Store() => new(System.TimeSpan.FromMinutes(15), () => _now);
    private static JArray Ops(string value = "25 mm") => new(new JObject { ["command"] = "set_parameter", ["arguments"] = new JObject { ["value"] = value, ["name"] = "D" } });

    [Fact]
    public void HashIsCanonical()
    {
        var a = new JArray(new JObject { ["command"] = "set_parameter", ["arguments"] = new JObject { ["name"] = "D", ["value"] = "25 mm" } });
        Assert.Equal(ChangePlanStore.HashOf(a, null), ChangePlanStore.HashOf(Ops(), null));
        Assert.NotEqual(ChangePlanStore.HashOf(Ops("26 mm"), null), ChangePlanStore.HashOf(Ops(), null));
        Assert.NotEqual(ChangePlanStore.HashOf(Ops(), new JArray("interference")), ChangePlanStore.HashOf(Ops(), null));
    }

    [Fact]
    public void CommitNeedsSameOwnerDocumentAndRevisionAndConsumesThePlan()
    {
        var store = Store();
        var plan = store.Create("agent", "doc_1", "e:5", Ops(), null, "bigger shaft");
        Assert.Equal(InventorErrorCodes.PLAN_NOT_FOUND, Assert.Throws<PlanException>(() => store.Take(plan.Id, "quest", "doc_1", "e:5")).Code);
        var taken = store.Take(plan.Id, "agent", "doc_1", "e:5");
        Assert.Equal(plan.Hash, taken.Hash);
        Assert.Equal(InventorErrorCodes.PLAN_NOT_FOUND, Assert.Throws<PlanException>(() => store.Take(plan.Id, "agent", "doc_1", "e:5")).Code);
    }

    [Fact]
    public void MovedRevisionIsAMismatch()
    {
        var store = Store();
        var plan = store.Create("agent", "doc_1", "e:5", Ops(), null, null);
        Assert.Equal(InventorErrorCodes.PLAN_MISMATCH, Assert.Throws<PlanException>(() => store.Take(plan.Id, "agent", "doc_1", "e:6")).Code);
    }

    [Fact]
    public void PlansExpire()
    {
        var store = Store();
        var plan = store.Create("agent", "doc_1", "e:5", Ops(), null, null);
        _now = _now.AddMinutes(16);
        Assert.Empty(store.List("agent"));
        Assert.Equal(InventorErrorCodes.PLAN_NOT_FOUND, Assert.Throws<PlanException>(() => store.Take(plan.Id, "agent", "doc_1", "e:5")).Code);
    }

    [Fact]
    public void OwnersAreCappedOldestFirst()
    {
        var store = Store();
        var first = store.Create("agent", "d", "r", Ops(), null, null);
        for (int i = 0; i < ChangePlanStore.MaxPlansPerOwner; i++) { _now = _now.AddSeconds(1); store.Create("agent", "d", "r", Ops(), null, null); }
        Assert.Equal(ChangePlanStore.MaxPlansPerOwner, store.List("agent").Count);
        Assert.DoesNotContain(store.List("agent"), p => p.Id == first.Id);
    }

    [Theory]
    [InlineData("export_step")]
    [InlineData("send_code")]
    public void PlanningRefusesCommandsOutsideTheCatalogue(string command)
    {
        var ops = new JArray(new JObject { ["command"] = command, ["arguments"] = new JObject() });
        Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT, Assert.Throws<PlanException>(() => PlanningTools.CheckOperations(ops, true)).Code);
    }

    [Fact]
    public void PlanningRefusesExperimentalCommandsWithoutTheOptIn()
    {
        var ops = new JArray(new JObject { ["command"] = "shell", ["arguments"] = new JObject { ["thickness_mm"] = 2 } });
        Assert.Equal(InventorErrorCodes.EXPERIMENTAL_DISABLED, Assert.Throws<PlanException>(() => PlanningTools.CheckOperations(ops, false)).Code);
        PlanningTools.CheckOperations(ops, true);
    }
}

public sealed class AuditLogTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-audit-" + System.Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static IDictionary<string, JsonElement> Args(string json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void RecordNeverContainsArgumentValues()
    {
        var record = AuditLog.Record("inventor_atomic_batch",
            Args("{\"document_id\":\"doc_1\",\"expected_revision\":\"e:4\",\"preview\":false,\"operations\":[{\"command\":\"set_parameter\",\"arguments\":{\"value\":\"SECRET-42 mm\"}}]}"),
            "{\"status\":\"committed\",\"revision\":\"e:5\",\"document_id\":\"doc_1\"}", null, "quest", "s1", "Quest Viewer", 12);
        Assert.Equal("committed", (string?)record["result"]);
        Assert.Equal("e:4", (string?)record["revision_before"]);
        Assert.Equal("e:5", (string?)record["revision_after"]);
        Assert.Equal("quest", (string?)record["client"]);
        Assert.Equal("Quest Viewer", (string?)record["declared_client"]);
        Assert.DoesNotContain("SECRET", record.ToString());
        Assert.Contains("operations", record["argument_names"]!.Select(t => (string)t!));
        Assert.Equal(64, ((string)record["arguments_sha256"]!).Length);
    }

    [Fact]
    public void ErrorsAreRecordedWithTheirCode()
    {
        var record = AuditLog.Record("inventor_commit_plan", Args("{\"plan_id\":\"plan_x\"}"),
            "{\"ok\":false,\"error\":{\"code\":\"PLAN_MISMATCH\",\"message\":\"m\"}}", null, "stdio", null, null, 3);
        Assert.Equal("error", (string?)record["result"]);
        Assert.Equal("PLAN_MISMATCH", (string?)record["error_code"]);
        Assert.Equal("plan_x", (string?)record["plan_id"]);
    }

    [Theory]
    [InlineData("inventor_atomic_batch", true)]
    [InlineData("inventor_set_camera", true)]
    [InlineData("inventor_build_release_package", true)]
    [InlineData("inventor_set_parameter", true)]      // unreviewed legacy write under full access
    [InlineData("inventor_get_display_mesh", false)]
    [InlineData("inventor_get_capabilities", false)]
    public void OnlyNonReadsAreAudited(string tool, bool audited) => Assert.Equal(audited, AuditLog.IsAudited(tool));

    [Fact]
    public void WritesJsonLines()
    {
        var log = new AuditLog(_dir);
        log.Write(new JObject { ["tool"] = "a" });
        log.Write(new JObject { ["tool"] = "b" });
        var lines = File.ReadAllLines(log.CurrentFile!);
        Assert.Equal(2, lines.Length);
        Assert.Equal("b", (string?)JObject.Parse(lines[1])["tool"]);
        Assert.NotNull(JObject.Parse(lines[0])["timestamp"]);
    }

    [Fact]
    public void DisabledLogWritesNothing()
    {
        var log = new AuditLog((string?)null);
        log.Write(new JObject { ["tool"] = "a" });
        Assert.False(log.Enabled);
        Assert.False(Directory.Exists(_dir));
    }
}

public sealed class CapabilityComputationTests
{
    private static JObject AddIn(bool experimental, params string[] commands) => new()
    {
        ["inventor_year"] = 2027, ["experimental_build"] = experimental, ["experimental_enabled"] = experimental,
        ["commands"] = new JArray(commands),
    };

    [Fact]
    public void UnreachableAddInMeansNoCadCapabilities()
    {
        var result = CapabilityTools.Compute(new InventorMcpConfig(), null, null, "NO_TARGET: none");
        Assert.False((bool)result["target"]!["reachable"]!);
        Assert.False((bool)result["capabilities"]!["atomic_batch"]!);
        Assert.Empty((JArray)result["batch_commands"]!["runnable"]!);
    }

    [Fact]
    public void ExperimentalCapabilitiesNeedBothSides()
    {
        var addIn = AddIn(true, "atomic_batch", "get_display_mesh", "shell", "set_parameter");
        Assert.False((bool)CapabilityTools.Compute(new InventorMcpConfig(), null, addIn, null)["capabilities"]!["xr_mesh"]!);
        var both = CapabilityTools.Compute(new InventorMcpConfig { EnableExperimental = true }, null, addIn, null);
        Assert.True((bool)both["capabilities"]!["xr_mesh"]!);
        var runnable = both["batch_commands"]!["runnable"]!.Select(t => (string)t!).ToArray();
        Assert.Contains("shell", runnable);
        Assert.Contains("set_parameter", runnable);
        Assert.DoesNotContain("extrude", runnable);   // not registered by this fake add-in
    }

    [Fact]
    public void ExperimentalCommandsOfADefaultBuildAreExplained()
    {
        var result = CapabilityTools.Compute(new InventorMcpConfig { EnableExperimental = true }, null, AddIn(false, "atomic_batch"), null);
        var shell = result["batch_commands"]!["unavailable"]!.First(u => (string?)u["command"] == "shell");
        Assert.Contains("SoExperimental", (string?)shell["reason"]);
        Assert.False((bool)result["capabilities"]!["content_center"]!);
    }

    [Fact]
    public void ToolSchemaPublishesContracts()
    {
        var tools = new CapabilityTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = Path.GetTempPath() }), new InventorMcpConfig());
        var batch = JObject.Parse(tools.GetToolSchema("inventor_atomic_batch"));
        Assert.True((bool)batch["requires_document_id_and_revision"]!);
        Assert.NotNull(batch["batch_catalog"]);
        var all = JObject.Parse(tools.GetToolSchema());
        Assert.Contains("inventor_get_display_mesh", all["hidden_experimental"]!.Select(t => (string)t!));
        Assert.Equal("INVALID_ARGUMENT", (string?)JObject.Parse(tools.GetToolSchema("inventor_send_code"))["error"]!["code"]);
    }
}

public sealed class SubscriptionTests
{
    [Fact]
    public void EventsMapToTheResourcesTheyChange()
    {
        var uris = EventSubscriptionService.AffectedUris(new JArray(
            new JObject { ["type"] = "selection_changed" }, new JObject { ["type"] = "document_changed" }));
        Assert.Contains("inventor://events", uris);
        Assert.Contains("inventor://selection", uris);
        Assert.Contains("inventor://active-document", uris);
        Assert.Empty(EventSubscriptionService.AffectedUris(new JArray()));
        Assert.DoesNotContain("inventor://active-document",
            EventSubscriptionService.AffectedUris(new JArray(new JObject { ["type"] = "camera_changed" })));
    }
}

public sealed class ReleaseHelperTests
{
    [Theory]
    [InlineData("release", true)]
    [InlineData("Rev B_2026-09", true)]
    [InlineData("../x", false)]
    [InlineData("", false)]
    public void PackageNamesAreSafe(string name, bool ok) => Assert.Equal(ok, ReleaseTools.ValidName(name));

    [Fact]
    public void ChecksumsCoverEveryFileButThemselves()
    {
        var dir = Path.Combine(Path.GetTempPath(), "so-rel-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "abc");
            File.WriteAllText(Path.Combine(dir, "checksums.sha256"), "old");
            var sums = ReleaseTools.Checksums(dir);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  a.txt\n", sums);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExportPathsOutsideTheArtifactRootAreRefused()
        => Assert.Throws<System.InvalidOperationException>(() => ReleaseTools.CheckArtifactPath("/etc/passwd"));
}
