using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;
using Xunit;

namespace XrSo.Core.Tests.Backend;

public sealed class AssemblyTests
{
    private static readonly DocumentState State = new("doc_assy", "r1", "v1");
    private static JObject Occurrence(string id) => new()
    {
        ["occurrence_id"] = id, ["name"] = id, ["editable"] = true, ["grounded"] = false,
        ["suppressed"] = false, ["adaptive"] = false, ["dof_translation"] = 1, ["dof_rotation"] = 0,
        ["dof_complete"] = true, ["translation_axes"] = new JArray { new JArray(1, 0, 0) },
        ["rotation_axes"] = new JArray(), ["rotation_center_mm"] = new JArray(20, 30, 40)
    };
    private static JObject Reference(string id, string owner, string geometry, string kind = "face") => new()
    {
        ["id"] = id, ["occurrence_id"] = owner, ["name"] = id, ["kind"] = kind,
        ["geometry"] = geometry, ["point_mm"] = new JArray(1, 2, 3)
    };
    private static JObject Context() => new()
    {
        ["document_id"] = State.DocumentId, ["revision"] = State.Revision, ["kind"] = "assembly", ["truncated"] = false,
        ["occurrences"] = new JArray(Occurrence("ent_a"), Occurrence("ent_b")),
        ["references"] = new JArray(Reference("ent_fa", "ent_a", "kPlaneSurface"), Reference("ent_fb", "ent_b", "kPlaneSurface"))
    };

    [Fact]
    public void SliderAxesAndCountsArePreserved()
    {
        var context = AssemblyContext.Parse(Context(), State);
        var occurrence = context.Occurrences[0];
        Assert.True(occurrence.CanMove);
        Assert.Equal(1, occurrence.TotalDof);
        Assert.Equal(1, Assert.Single(occurrence.TranslationAxes).X);
        Assert.Equal(40, occurrence.Center!.Value.Z);
    }
    [Theory]
    [InlineData("grounded", true)]
    [InlineData("editable", false)]
    [InlineData("dof_complete", false)]
    [InlineData("suppressed", true)]
    [InlineData("adaptive", true)]
    public void UnsafeOccurrenceCannotMove(string field, bool value)
    {
        var json = Context(); json["occurrences"]![0]![field] = value;
        Assert.False(AssemblyContext.Parse(json, State).Occurrences[0].CanMove);
    }
    [Fact]
    public void FlexibleSubassemblyIsNotEditableButKeepsItsDefinitionForTheEntry()
    {
        var json = Context(); var item = (JObject)json["occurrences"]![1]!;
        item["definition_kind"] = "assembly"; item["flexible"] = true; item["editable"] = false; item["unavailable_reason"] = "flexible";
        var occurrence = AssemblyContext.Parse(json, State).Occurrences[1];
        Assert.True(occurrence.Flexible); Assert.False(occurrence.Editable); Assert.False(occurrence.CanMove);
        Assert.Equal("assembly", occurrence.Kind); Assert.Equal("flexible", occurrence.UnavailableReason);
        Assert.False(AssemblyContext.Parse(Context(), State).Occurrences[1].Flexible);
    }
    [Fact]
    public void UnknownCountsNeverBecomeFreeOrGrounded()
    {
        var json = Context(); json["occurrences"]![0]!["dof_translation"] = null;
        var occurrence = AssemblyContext.Parse(json, State).Occurrences[0];
        Assert.Null(occurrence.TotalDof); Assert.False(occurrence.DofComplete); Assert.False(occurrence.CanMove);
    }
    [Fact]
    public void StaleAndForeignReferenceContextsAreRejected()
    {
        var json = Context(); json["revision"] = "r2";
        Assert.Throws<FormatException>(() => AssemblyContext.Parse(json, State));
        json = Context(); json["references"]![0]!["occurrence_id"] = "ent_foreign";
        Assert.Throws<FormatException>(() => AssemblyContext.Parse(json, State));
    }
    [Fact]
    public void InvalidAxisIsRejected()
    {
        var json = Context(); json["occurrences"]![0]!["translation_axes"] = new JArray { new JArray(0, 0, 0) };
        Assert.Throws<FormatException>(() => AssemblyContext.Parse(json, State));
    }
    [Fact]
    public void PlanePairOffersCompatibleConstraintsAndCreatesNumericDraft()
    {
        var refs = AssemblyContext.Parse(Context(), State).References;
        Assert.Equal(new[] { "mate", "flush", "angle" }, AssemblyOperations.CompatibleConstraints(refs[0], refs[1]));
        var operation = AssemblyOperations.Constraint("flush", refs[0], refs[1], 12.5);
        Assert.Equal("assembly_constraint", (string?)operation["command"]);
        Assert.Equal(12.5, (double)operation["arguments"]!["offset_mm"]!);
        Assert.Empty(AssemblyOperations.CompatibleConstraints(refs[0], refs[0]));
        Assert.Throws<ArgumentException>(() => AssemblyOperations.Constraint("insert", refs[0], refs[1], 0));
    }
    [Fact]
    public void RotationalJointRequiresCircularEdgesOfDifferentComponents()
    {
        var json = Context();
        var refs = AssemblyContext.Parse(json, State).References;
        Assert.Throws<ArgumentException>(() => AssemblyOperations.Joint("rotational", refs[0], refs[1]));
        json["references"] = new JArray(Reference("ent_ea", "ent_a", "kCircleCurve", "edge"), Reference("ent_eb", "ent_b", "kCircleCurve", "edge"));
        refs = AssemblyContext.Parse(json, State).References;
        Assert.Equal("rotational", (string?)AssemblyOperations.Joint("rotational", refs[0], refs[1])["arguments"]!["joint_type"]);
        Assert.Equal(new[] { "insert" }, AssemblyOperations.CompatibleConstraints(refs[0], refs[1]));
    }
    [Fact]
    public void MovePreservesCadUnitsAndRequiresCompleteRotation()
    {
        var op = AssemblyOperations.Move("ent_a", new CadPoint(10, 20, 30), new CadPoint(0, 0, 1), new CadPoint(50, 0, 0), 90);
        Assert.Equal(30, (double)op["arguments"]!["translation_mm"]![2]);
        Assert.Equal(90, (double)op["arguments"]!["rotation_degrees"]!);
        Assert.Throws<ArgumentException>(() => AssemblyOperations.Move("ent_a", default, degrees: 90));
        Assert.Throws<ArgumentException>(() => AssemblyOperations.Move("ent_a", default, degrees: double.NaN));
    }
}
