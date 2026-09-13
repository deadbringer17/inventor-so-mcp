using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class AtomicCadBatchTests
{
    private sealed class Backend : ICadBatchBackend
    {
        public string DocumentId { get; set; } = "doc";
        public string Revision { get; set; } = "revision";
        public string? RestoredRevision;
        public int Value;
        public int Begins, Commits, Rollbacks;
        public bool FailValidation, FailRollback;
        private int _before;
        public void Begin(string name) { _before = Value; Begins++; Revision = "revision-after-edit"; }
        public void RestoreRevision(string revision) { RestoredRevision = revision; Revision = revision; }
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
        // A preview that left no change must not invalidate the revision it was planned against.
        Assert.Equal("revision", backend.RestoredRevision);
        Assert.Equal("revision", (string?)result["revision"]);
    }

    [Fact]
    public void CommittedBatchKeepsTheNewRevision()
    {
        var backend = new Backend();
        var result = AtomicCadBatch.Run(backend, "doc", "revision", Steps(), false);
        Assert.Null(backend.RestoredRevision);
        Assert.Equal("revision-after-edit", (string?)result["revision"]);
    }

    [Fact]
    public void FailedPreviewNeverRestoresTheRevision()
    {
        var backend = new Backend();
        Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision", Steps(true), true));
        Assert.Null(backend.RestoredRevision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("7")]
    [InlineData("\"name=length\"")]
    public void AnOperationWithoutAnArgumentsObjectIsRefusedByName(string? arguments)
    {
        var backend = new Backend();
        var step = new JObject { ["command"] = "set_parameter" };
        if (arguments != null) step["arguments"] = JToken.Parse(arguments);
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision",
            new JArray(step), false));
        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal(0, error.StepIndex);
        Assert.Equal("set_parameter", error.Command);
        Assert.Contains("arguments object", error.Message);
        Assert.Contains("name:string", error.Message);   // the catalogue says what the step needs
        Assert.Equal(0, backend.Begins);
    }

    [Fact]
    public void TheRejectionNamesTheStepAndListsTheVocabulary()
    {
        var backend = new Backend();
        var steps = Steps();
        steps.Add(new JObject { ["command"] = "export_step", ["arguments"] = new JObject() });
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision", steps, false));
        Assert.Contains("operation 2", error.Message);
        Assert.Contains("export_step", error.Message);
        Assert.Contains("set_parameter", error.Message);          // the allowed list is spelled out
        Assert.Contains("inventor://batch-commands", error.Message);
    }

    /// <summary>
    /// The executable vocabulary is now read from the published catalogue, so an edit to the
    /// catalogue is an edit to what a batch may do. Frozen here on purpose: widening the surface
    /// should fail this test and be a deliberate change, not a side effect of writing documentation.
    /// </summary>
    private static readonly string[] FrozenVocabulary =
    {
        "add_sketch_constraint", "add_sketch_dimension", "chamfer", "circular_pattern", "close_sketch",
        "create_flat_pattern", "create_parameter", "create_sketch", "create_work_axis", "create_work_plane",
        "draw_arc", "draw_circle", "draw_line", "draw_point", "draw_rectangle", "extrude", "fillet", "hole",
        "project_geometry", "rectangular_pattern", "revolve", "set_parameter", "set_sheet_metal_rule",
        "sheet_metal_contour_flange", "sheet_metal_corner_chamfer", "sheet_metal_corner_round",
        "sheet_metal_cut", "sheet_metal_face", "sheet_metal_flange", "sheet_metal_fold", "sheet_metal_hem",
        "sheet_metal_lofted_flange", "sheet_metal_punch", "sheet_metal_refold", "sheet_metal_rip",
        "sheet_metal_unfold",
    };

    [Fact]
    public void TheCatalogueIsExactlyTheExecutableVocabulary()
        => Assert.Equal(FrozenVocabulary, AtomicCadBatch.AllowedCommands.OrderBy(n => n, StringComparer.Ordinal));

    [Fact]
    public void EveryAllowedCommandIsDocumented()
    {
        Assert.Equal(AtomicCadBatch.AllowedCommands.OrderBy(n => n, StringComparer.Ordinal),
            CadBatchCommandCatalog.Names.OrderBy(n => n, StringComparer.Ordinal));
        foreach (var entry in CadBatchCommandCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Summary), entry.Name + " has no summary");
            foreach (var argument in entry.Required.Concat(entry.Optional))
                Assert.Contains(":", argument);   // every argument carries a type
        }
    }
    [Theory]
    [InlineData("other", "revision")]
    [InlineData("doc", "stale")]
    public void StalePlanNeverStarts(string doc, string revision)
    {
        var backend = new Backend();
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, doc, revision, Steps(), false));
        Assert.Equal(doc == "doc" ? "STALE_REVISION" : "DOCUMENT_CHANGED", error.Code);
        Assert.Equal(0, backend.Begins);
    }
    [Fact]
    public void FailureAfterPartialModificationRollsBackEntireBatch()
    {
        var backend = new Backend { Value = 4 };
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision", Steps(true), false));
        Assert.StartsWith("ROLLED_BACK", error.Message);
        Assert.Equal("ROLLED_BACK", error.Code);
        Assert.Equal(1, error.StepIndex);
        Assert.Equal("extrude", error.Command);
        Assert.Equal(4, backend.Value);
        Assert.Equal(0, backend.Commits);
    }
    [Fact]
    public void FailedValidationPreventsCommit()
    {
        var backend = new Backend { FailValidation = true };
        Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision", Steps(), false));
        Assert.Equal(0, backend.Value);
        Assert.Equal(0, backend.Commits);
    }
    [Fact]
    public void LateForbiddenStepRejectsBeforeAnyModification()
    {
        var backend = new Backend();
        var steps = Steps();
        steps.Add(new JObject { ["command"] = "send_code", ["arguments"] = new JObject() });
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision", steps, false));
        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal(2, error.StepIndex);
        Assert.Equal("send_code", error.Command);
        Assert.Equal(0, backend.Begins);
    }
    [Fact]
    public void DeadlineBetweenStepsRollsBack()
    {
        var backend = new Backend();
        Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision", Steps(), false, () => backend.Value > 0));
        Assert.Equal(0, backend.Value);
        Assert.Equal(1, backend.Rollbacks);
    }
    [Fact]
    public void RollbackFailureIsNeverReportedAsSuccess()
    {
        var backend = new Backend { FailRollback = true };
        var ex = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision", Steps(true), false));
        Assert.StartsWith("ROLLBACK_FAILED", ex.Message);
        Assert.Equal("ROLLBACK_FAILED", ex.Code);
    }
    [Fact]
    public void ChangedDocumentStopsBatchAndRollsBack()
    {
        var backend = new Backend();
        var steps = Steps();
        steps[0]["arguments"]!["switch_document"] = true;
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision", steps, false));
        Assert.Equal("ROLLED_BACK", error.Code);
        Assert.Equal("DOCUMENT_CHANGED", error.StepCode);
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
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "revision",
            new JArray(new JObject { ["command"] = command, ["arguments"] = new JObject() }), false));
        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Contains(command, error.Message);
        Assert.Equal(0, backend.Begins);
        Assert.Equal(0, backend.Value);
    }
}
