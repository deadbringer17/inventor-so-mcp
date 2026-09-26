using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ValidationSpecTests
{
    [Theory]
    [InlineData("part", "rebuild,feature_health")]
    [InlineData("assembly", "rebuild,constraint_health")]
    [InlineData("drawing", "rebuild")]
    public void DefaultsAlwaysRun(string kind, string expected)
        => Assert.Equal(expected, string.Join(",", ValidationSpec.Parse(null, kind).Rules.Select(r => r.Name)));

    [Fact]
    public void ClearanceTakesMillimetres()
    {
        var spec = ValidationSpec.Parse(new JArray("interference", "min_clearance:2.5mm"), "assembly");
        Assert.True(spec.Has("interference"));
        Assert.Equal(2.5, spec.Find("min_clearance")!.ValueMm);
        Assert.Contains("min_clearance:2.5mm", spec.ToJson().Select(t => (string)t!));
    }

    [Fact]
    public void RepeatedCheckKeepsTheLast()
    {
        var spec = ValidationSpec.Parse(new JArray("min_clearance:2mm", "min_clearance:5 mm"), "assembly");
        Assert.Single(spec.Rules, r => r.Name == "min_clearance");
        Assert.Equal(5, spec.Find("min_clearance")!.ValueMm);
    }

    [Theory]
    [InlineData("interfernce", "part")]         // misspelt
    [InlineData("interference", "part")]        // wrong kind
    [InlineData("min_clearance", "assembly")]   // missing value
    [InlineData("min_clearance:-1mm", "assembly")]
    [InlineData("min_clearance:abc", "assembly")]
    [InlineData("rebuild:3mm", "part")]         // value on a flag
    [InlineData("drawing_references", "part")]
    public void BadChecksAreRefusedWithTheVocabulary(string check, string kind)
    {
        var error = Assert.Throws<ArgumentException>(() => ValidationSpec.Parse(new JArray(check), kind));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void NonArrayIsRefused()
        => Assert.Throws<ArgumentException>(() => ValidationSpec.Parse(new JValue("rebuild"), "part"));

    [Fact]
    public void TooManyChecksAreRefused()
        => Assert.Throws<ArgumentException>(() => ValidationSpec.Parse(new JArray(Enumerable.Repeat("rebuild", 17)), "part"));

    [Fact]
    public void DescribeListsEveryCheck()
    {
        var names = ValidationSpec.Describe()["checks"]!.Select(c => (string)c["name"]!).ToArray();
        Assert.Contains("min_clearance:<n>mm", names);
        Assert.Contains("drawing_references", names);
    }
}

public sealed class CatalogTierTests
{
    private sealed class Backend : ICadBatchBackend, ICadBatchValidatingBackend
    {
        public string DocumentId => "doc";
        public string Revision => "rev";
        public int Begins, Commits;
        public ValidationSpec? Seen;
        public bool FailSpec;
        public void Begin(string name) => Begins++;
        public JObject Execute(string command, JObject arguments) => new() { ["command"] = command };
        public void Validate() { }
        public void Validate(ValidationSpec spec)
        {
            Seen = spec;
            if (FailSpec) throw new CodedFailureException(InventorErrorCodes.VALIDATION_FAILED, "Interference: A/B",
                new JObject { ["check"] = "interference", ["pair"] = new JArray("A", "B") });
        }
        public void Commit() => Commits++;
        public void Rollback() { }
        public void RestoreRevision(string revision) { }
    }

    private static JArray One(string command) => new(new JObject { ["command"] = command, ["arguments"] = new JObject() });

    [Fact]
    public void StableAndExperimentalAreDisjointAndComplete()
    {
        Assert.Empty(CadBatchCommandCatalog.Names.Intersect(CadBatchCommandCatalog.ExperimentalNames));
        Assert.Equal(CadBatchCommandCatalog.AllNames.Count, CadBatchCommandCatalog.Names.Count + CadBatchCommandCatalog.ExperimentalNames.Count);
        Assert.Equal(CadBatchCommandCatalog.AllNames.Count, CadBatchCommandCatalog.AllNames.Distinct().Count());
        foreach (var entry in CadBatchCommandCatalog.All)
        {
            Assert.NotEmpty(entry.Documents);
            Assert.All(entry.Documents, d => Assert.Contains(d, CadDocumentKinds.All));
            Assert.All(entry.Required.Concat(entry.Optional), a => Assert.Contains(":", a));
            // Every stable command predates the tier split and is part-only.
            if (!entry.Experimental) Assert.Equal(new[] { "part" }, entry.Documents);
        }
    }

    [Fact]
    public void ExperimentalCommandIsRefusedUnlessTheHostAllowsIt()
    {
        var backend = new Backend();
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "rev", One("shell"), false, null,
            new CadBatchOptions { DocumentKind = "part" }));
        Assert.Equal(InventorErrorCodes.EXPERIMENTAL_DISABLED, error.Code);
        Assert.Equal(0, backend.Begins);

        var ok = AtomicCadBatch.Run(backend, "doc", "rev", One("shell"), false, null,
            new CadBatchOptions { DocumentKind = "part", AllowExperimental = true });
        Assert.Equal("committed", (string?)ok["status"]);
    }

    [Fact]
    public void UnregisteredCommandIsRefusedBeforeTheTransaction()
    {
        var backend = new Backend();
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "rev", One("shell"), false, null,
            new CadBatchOptions { DocumentKind = "part", AllowExperimental = true, IsRegistered = _ => false }));
        Assert.Equal(InventorErrorCodes.EXPERIMENTAL_DISABLED, error.Code);
        Assert.Equal(0, backend.Begins);
    }

    [Fact]
    public void WrongDocumentKindIsRefusedBeforeTheTransaction()
    {
        var backend = new Backend();
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "rev", One("extrude"), false, null,
            new CadBatchOptions { DocumentKind = "assembly" }));
        Assert.Equal(InventorErrorCodes.WRONG_DOCUMENT_TYPE, error.Code);
        Assert.Equal(0, error.StepIndex);
        Assert.Equal(0, backend.Begins);
    }

    [Fact]
    public void BadValidateIsRefusedBeforeTheTransaction()
    {
        var backend = new Backend();
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "rev", One("set_parameter"), false, null,
            new CadBatchOptions { DocumentKind = "part", Validate = new JArray("interference") }));
        Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT, error.Code);
        Assert.Equal(0, backend.Begins);
    }

    [Fact]
    public void UnsupportedCheckIsRefusedBeforeTheTransaction()
    {
        var backend = new Backend();
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "rev", One("set_parameter"), false, null,
            new CadBatchOptions { DocumentKind = "part", Validate = new JArray("sketch_fully_constrained"), SupportsCheck = n => n != "sketch_fully_constrained" }));
        Assert.Equal(InventorErrorCodes.EXPERIMENTAL_DISABLED, error.Code);
        Assert.Equal(0, backend.Begins);
    }

    [Fact]
    public void ValidationRunsBeforeCommitAndIsReported()
    {
        var backend = new Backend();
        var result = AtomicCadBatch.Run(backend, "doc", "rev", One("activate_model_state"), false, null,
            new CadBatchOptions { DocumentKind = "assembly", AllowExperimental = true, Validate = new JArray("min_clearance:1mm") });
        Assert.Equal(1, backend.Commits);
        Assert.Equal(1, backend.Seen!.Find("min_clearance")!.ValueMm);
        Assert.Contains("min_clearance:1mm", result["validated"]!.Select(t => (string)t!));
    }

    [Fact]
    public void FailedValidationRollsBackWithItsDetails()
    {
        var backend = new Backend { FailSpec = true };
        var error = Assert.Throws<CadBatchException>(() => AtomicCadBatch.Run(backend, "doc", "rev", One("activate_model_state"), false, null,
            new CadBatchOptions { DocumentKind = "assembly", AllowExperimental = true, Validate = new JArray("interference") }));
        Assert.Equal(InventorErrorCodes.ROLLED_BACK, error.Code);
        Assert.Equal(InventorErrorCodes.VALIDATION_FAILED, error.StepCode);
        Assert.Equal(0, backend.Commits);
        Assert.Equal("interference", (string?)error.Details()["step_details"]!["check"]);
    }

    [Fact]
    public void DescribeHidesExperimentalUnlessAsked()
    {
        var stable = CadBatchCommandCatalog.Describe()["commands"]!.Select(c => (string)c["command"]!).ToArray();
        var all = CadBatchCommandCatalog.Describe(includeExperimental: true)["commands"]!.Select(c => (string)c["command"]!).ToArray();
        Assert.DoesNotContain("shell", stable);
        Assert.Contains("shell", all);
        Assert.Equal(CadBatchCommandCatalog.AllNames.Count, all.Length);
    }
}
