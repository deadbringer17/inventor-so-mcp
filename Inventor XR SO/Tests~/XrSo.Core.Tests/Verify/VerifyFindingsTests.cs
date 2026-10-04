using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Verify;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Verify;

public class VerifyFindingsTests
{
    [Fact]
    public void InterferencePairsBecomeErrorsSortedByVolumeWithIdsAndBoxes()
    {
        var report = InterferenceReport.FromJson(new JObject
        {
            ["revision"] = "r", ["analyzed"] = 4, ["count"] = 2, ["total_volume_mm3"] = 2010.5,
            ["pairs"] = new JArray(
                new JObject { ["a_occurrence_id"] = "a", ["b_occurrence_id"] = "c", ["a_name"] = "M7_A", ["b_name"] = "M7_C", ["volume_mm3"] = 10.5 },
                new JObject { ["a_occurrence_id"] = "a", ["b_occurrence_id"] = "b", ["a_name"] = "M7_A", ["b_name"] = "M7_B", ["volume_mm3"] = 2000.0,
                    ["boxes"] = new JArray(new JObject { ["min_mm"] = new JArray(15, 0, 0), ["max_mm"] = new JArray(20, 20, 20) }) }),
        });
        var rows = VerifyFindings.FromInterference(report);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Interferenza: M7_A ↔ M7_B", rows[0].Title);
        Assert.Equal("2000 mm³", rows[0].Detail);
        Assert.Equal(FindingSeverity.Error, rows[0].Severity);
        Assert.Equal(new[] { "a", "b" }, rows[0].OccurrenceIds);
        Assert.Single(rows[0].Boxes);
        Assert.Equal("10,5 mm³", rows[1].Detail);
        Assert.Equal("2 interferenze su 4 occorrenze, 2010,5 mm³ in totale.", VerifyFindings.Summary(report));
    }

    [Fact]
    public void NoInterferenceIsAnExplicitResult()
    {
        var report = InterferenceReport.FromJson(new JObject { ["revision"] = "r", ["analyzed"] = 4, ["count"] = 0, ["pairs"] = new JArray() });
        Assert.Empty(VerifyFindings.FromInterference(report));
        Assert.Equal("Nessuna interferenza su 4 occorrenze.", VerifyFindings.Summary(report));
    }

    [Fact]
    public void HealthRowsCoverRelationshipsUnconstrainedAndBom()
    {
        var report = HealthReport.FromJson(new JObject
        {
            ["revision"] = "r", ["healthy"] = false, ["occurrence_count"] = 4,
            ["failing_constraints"] = new JArray(new JObject { ["name"] = "M7_Sick", ["health"] = "kInconsistentHealth", ["a_occurrence_id"] = "b", ["b_occurrence_id"] = null }),
            ["failing_joints"] = new JArray(),
            ["occurrences"] = new JArray(new JObject { ["name"] = "M7_D", ["occurrence_id"] = "d", ["unconstrained"] = true }),
            ["bom"] = new JObject { ["valid"] = false, ["findings"] = new JArray(new JObject { ["severity"] = "error", ["code"] = "PART_NUMBER_MISSING", ["message"] = "Row has no part number." }) },
        });
        var rows = VerifyFindings.FromHealth(report);
        Assert.Equal(new[] { "Vincolo in errore: M7_Sick", "Non vincolato: M7_D", "Distinta: numero di parte mancante" }, rows.Select(r => r.Title).ToArray());
        Assert.Equal(new[] { "b" }, rows[0].OccurrenceIds);
        Assert.Equal(FindingSeverity.Warning, rows[1].Severity);
        Assert.Empty(rows[2].OccurrenceIds);
        Assert.Equal("Assieme con 3 problemi: 1 vincoli o giunti in errore, 1 componenti non vincolati, 1 righe di distinta.", VerifyFindings.Summary(report));
    }

    [Fact]
    public void AHealthyAssemblySaysSo()
    {
        var report = HealthReport.FromJson(new JObject { ["revision"] = "r", ["healthy"] = true, ["occurrence_count"] = 2, ["bom"] = new JObject { ["valid"] = true } });
        Assert.Empty(VerifyFindings.FromHealth(report));
        Assert.Equal("Assieme sano: nessun vincolo in errore, nessun componente libero, distinta valida.", VerifyFindings.Summary(report));
    }
}
