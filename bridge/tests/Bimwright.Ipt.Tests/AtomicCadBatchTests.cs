using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class AtomicCadBatchTests
{
    private sealed class Backend : ICadBatchBackend
    {
        public string DocumentId { get; set; } = "doc";
        public string Revision { get; set; } = "revision";
        public int Value;
        public int Begins, Commits, Rollbacks;
        public bool FailValidation, FailRollback;
        private int _before;
        public void Begin(string name) { _before = Value; Begins++; }
        public JObject Execute(string command, JObject arguments)
        {
            Value++;
            if ((bool?)arguments["fail"] == true) throw new InvalidOperationException("feature failed after partial modification");
            if ((bool?)arguments["switch_document"] == true) DocumentId = "other";
            return new JObject { ["value"] = Value };
        }
        public void Validate() { if (FailValidation) throw new InvalidOperationException("unhealthy feature"); }
        public void Commit() => Commits++;
        public void Rollback() { Rollbacks++; if (FailRollback) throw new InvalidOperationException("COM abort failed"); Value = _before; }
    }
    private static JArray Steps(bool fail = false) => new(
        new JObject { ["command"] = "set_parameter", ["arguments"] = new JObject() },
        new JObject { ["command"] = "extrude", ["arguments"] = new JObject { ["fail"] = fail } });

    [Fact]
    public void ValidBatchCommitsOnce()
    {
        var backend = new Backend();
        var result = AtomicCadBatch.Run(backend, "doc", "revision", Steps(), false);
        Assert.Equal("committed", (string?)result["status"]);
        Assert.Equal(2, backend.Value);
        Assert.Equal(1, backend.Commits);
        Assert.Equal(0, backend.Rollbacks);
    }
    [Fact]
    public void PreviewExecutesAndRestoresState()
    {
        var backend = new Backend { Value = 10 };
        var result = AtomicCadBatch.Run(backend, "doc", "revision", Steps(), true);
        Assert.Equal("preview_rolled_back", (string?)result["status"]);
        Assert.Equal(10, backend.Value);
        Assert.Equal(0, backend.Commits);
        Assert.Equal(1, backend.Rollbacks);
    }
    [Theory]
    [InlineData("other", "revision")]
    [InlineData("doc", "stale")]
    public void StalePlanNeverStarts(string doc, string revision)
    {
        var backend = new Backend();
        Assert.Throws<InvalidOperationException>(() => AtomicCadBatch.Run(backend, doc, revision, Steps(), false));
        Assert.Equal(0, backend.Begins);
    }
    [Fact]
    public void FailureAfterPartialModificationRollsBackEntireBatch()
    {
        var backend = new Backend { Value = 4 };
        var error = Assert.Throws<InvalidOperationException>(() => AtomicCadBatch.Run(backend, "doc", "revision", Steps(true), false));
        Assert.StartsWith("ROLLED_BACK", error.Message);
        Assert.Equal(4, backend.Value);
        Assert.Equal(0, backend.Commits);
    }
    [Fact]
    public void FailedValidationPreventsCommit()
    {
        var backend = new Backend { FailValidation = true };
        Assert.Throws<InvalidOperationException>(() => AtomicCadBatch.Run(backend, "doc", "revision", Steps(), false));
        Assert.Equal(0, backend.Value);
        Assert.Equal(0, backend.Commits);
    }
    [Fact]
    public void LateForbiddenStepRejectsBeforeAnyModification()
    {
        var backend = new Backend();
        var steps = Steps();
        steps.Add(new JObject { ["command"] = "send_code", ["arguments"] = new JObject() });
        Assert.Throws<ArgumentException>(() => AtomicCadBatch.Run(backend, "doc", "revision", steps, false));
        Assert.Equal(0, backend.Begins);
    }
    [Fact]
    public void DeadlineBetweenStepsRollsBack()
    {
        var backend = new Backend();
        Assert.Throws<InvalidOperationException>(() => AtomicCadBatch.Run(backend, "doc", "revision", Steps(), false, () => backend.Value > 0));
        Assert.Equal(0, backend.Value);
        Assert.Equal(1, backend.Rollbacks);
    }
    [Fact]
    public void RollbackFailureIsNeverReportedAsSuccess()
    {
        var backend = new Backend { FailRollback = true };
        var ex = Assert.Throws<InvalidOperationException>(() => AtomicCadBatch.Run(backend, "doc", "revision", Steps(true), false));
        Assert.StartsWith("ROLLBACK_FAILED", ex.Message);
    }
    [Fact]
    public void ChangedDocumentStopsBatchAndRollsBack()
    {
        var backend = new Backend();
        var steps = Steps();
        steps[0]["arguments"]!["switch_document"] = true;
        Assert.Throws<InvalidOperationException>(() => AtomicCadBatch.Run(backend, "doc", "revision", steps, false));
        Assert.Equal(0, backend.Value);
        Assert.Equal(0, backend.Commits);
    }

    [Theory]
    [InlineData("set_sheet_metal_rule")]
    [InlineData("sheet_metal_face")]
    [InlineData("sheet_metal_flange")]
    [InlineData("sheet_metal_cut")]
    [InlineData("create_flat_pattern")]
    [InlineData("sheet_metal_hem")]
    [InlineData("sheet_metal_fold")]
    [InlineData("sheet_metal_contour_flange")]
    [InlineData("sheet_metal_corner_round")]
    [InlineData("sheet_metal_corner_chamfer")]
    [InlineData("sheet_metal_unfold")]
    [InlineData("sheet_metal_refold")]
    [InlineData("sheet_metal_punch")]
    [InlineData("draw_point")]
    [InlineData("sheet_metal_rip")]
    [InlineData("sheet_metal_lofted_flange")]
    public void SheetMetalOperationsRunInsideTheTransaction(string command)
    {
        var backend = new Backend();
        var result = AtomicCadBatch.Run(backend, "doc", "revision",
            new JArray(new JObject { ["command"] = command, ["arguments"] = new JObject() }), false);
        Assert.Equal("committed", (string?)result["status"]);
        Assert.Equal(1, backend.Commits);
    }

    [Theory]
    [InlineData("unfold")]
    [InlineData("sheet_metal_contour_roll")]
    [InlineData("convert_to_sheet_metal")]
    [InlineData("save_document")]
    public void UnlistedOperationsAreRefusedBeforeAnythingRuns(string command)
    {
        var backend = new Backend();
        Assert.Throws<ArgumentException>(() => AtomicCadBatch.Run(backend, "doc", "revision",
            new JArray(new JObject { ["command"] = command, ["arguments"] = new JObject() }), false));
        Assert.Equal(0, backend.Begins);
        Assert.Equal(0, backend.Value);
    }
}
