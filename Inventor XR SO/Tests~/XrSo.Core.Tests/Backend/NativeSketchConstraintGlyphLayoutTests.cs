using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchConstraintGlyphLayoutTests
{
    private static readonly DocumentState State = new("part","r7","v7");
    private static JObject Constraint(string id, string type = "kGroundConstraintObject", bool annotation = true)
    {
        var result = new JObject
        {
            ["id"] = id, ["type"] = "constraint", ["native_type"] = type, ["supported"] = true,
            ["binding_state"] = "unavailable", ["binding_reason"] = "Native associations could not be read."
        };
        if(annotation) result["annotation_mm"] = new JArray(10,20);
        return result;
    }
    private static JObject Line(string id = "ent_line") => new()
    {
        ["id"] = id, ["type"] = "line", ["supported"] = true,
        ["start_mm"] = new JArray(1,2), ["end_mm"] = new JArray(8,9), ["strokes_mm"] = new JArray(1,2,8,9)
    };
    private static JObject Binding(string id, string subpart = "Entity") => new()
    { ["entity_id"] = id, ["type"] = "line", ["subpart"] = subpart };
    private static NativeSketchSnapshot Snapshot(IEnumerable<JObject> constraints, params JObject[] entities)
    {
        var json = new JObject
        {
            ["schema_version"] = 1, ["document_id"] = "part", ["revision"] = "r7", ["sketch_id"] = "ent_sketch", ["sketch_name"] = "Existing",
            ["frame"] = new JObject { ["origin_mm"] = new JArray(0,0,0), ["x_axis"] = new JArray(1,0,0), ["y_axis"] = new JArray(0,1,0) },
            ["entities"] = new JArray(entities), ["dimensions"] = new JArray(), ["constraints"] = new JArray(constraints), ["partial"] = false
        };
        return NativeSketchSnapshot.Parse(json,State,"ent_sketch");
    }
    private static void Complete(JObject constraint, params JObject[] bindings)
    { constraint["binding_state"] = "complete"; constraint["related_entities"] = new JArray(bindings); constraint.Remove("binding_reason"); }

    [Fact]
    public void NativeAnnotationDoesNotInventAnAssociationOrMutateTheSnapshot()
    {
        var snapshot = Snapshot(new[] { Constraint("ent_fix") });
        string before = snapshot.Constraints[0].Data.ToString();
        var result = NativeSketchConstraintGlyphLayout.Build(snapshot);
        var glyph = Assert.Single(result.Entries);
        Assert.Equal(NativeSketchConstraintGlyphState.Visible,glyph.State);
        Assert.Equal(NativeSketchConstraintAnchorSource.NativeAnnotation,glyph.AnchorSource);
        Assert.Equal(new CadPoint(10,20),glyph.AnchorMm.Value);
        Assert.Equal(glyph.AnchorMm,glyph.PositionMm);
        Assert.False(glyph.AssociationsAvailable);
        Assert.Equal("Native associations could not be read.",glyph.AssociationReason);
        Assert.Equal("Fissaggio",glyph.Label);
        Assert.Empty(glyph.Constraint.RelatedEntities);
        Assert.Equal(before,snapshot.Constraints[0].Data.ToString());
        Assert.Same(State,result.State);
    }

    [Fact]
    public void ExplicitRelatedSubpartTakesPrecedenceOverWholeEntityFallback()
    {
        var constraint = Constraint("ent_constraint",annotation:false);
        Complete(constraint,Binding("ent_a"),Binding("ent_b","End"));
        var snapshot = Snapshot(new[] {constraint},Line("ent_a"),Line("ent_b"));
        var glyph = Assert.Single(NativeSketchConstraintGlyphLayout.Build(snapshot).Entries);
        Assert.True(glyph.AssociationsAvailable);
        Assert.Equal(NativeSketchConstraintAnchorSource.RelatedSubpart,glyph.AnchorSource);
        Assert.Equal(new CadPoint(8,9),glyph.AnchorMm.Value);
        Assert.Equal("",glyph.AssociationReason);
    }

    [Fact]
    public void WholeEntityUsesAnExistingAnchorWithoutSynthesizingAMidpoint()
    {
        var constraint = Constraint("ent_constraint",annotation:false);
        Complete(constraint,Binding("ent_line"));
        var glyph = Assert.Single(NativeSketchConstraintGlyphLayout.Build(Snapshot(new[] {constraint},Line())).Entries);
        Assert.Equal(NativeSketchConstraintAnchorSource.RelatedEntityAnchor,glyph.AnchorSource);
        Assert.Equal(new CadPoint(1,2),glyph.AnchorMm.Value);
    }

    [Fact]
    public void MissingPositionIsDistinctFromAHiddenFilterAndDoesNotUseUnrelatedGeometry()
    {
        var missing = Constraint("ent_missing",annotation:false);
        var hidden = Constraint("ent_hidden","kParallelConstraintObject");
        var snapshot = Snapshot(new[] {missing,hidden},Line());
        var result = NativeSketchConstraintGlyphLayout.Build(snapshot,new NativeSketchConstraintGlyphOptions(nativeTypes:new[] {"kGroundConstraintObject"}));
        var unpositionable = result.Entries.Single(e=>e.Reference.EntityId == "ent_missing");
        Assert.Equal(NativeSketchConstraintGlyphState.Unpositionable,unpositionable.State);
        Assert.Equal(NativeSketchConstraintAnchorSource.None,unpositionable.AnchorSource);
        Assert.Null(unpositionable.AnchorMm); Assert.Null(unpositionable.PositionMm);
        var filtered = result.Entries.Single(e=>e.Reference.EntityId == "ent_hidden");
        Assert.Equal(NativeSketchConstraintGlyphState.HiddenByFilter,filtered.State);
        Assert.NotNull(filtered.AnchorMm); Assert.Null(filtered.PositionMm);
        Assert.NotEmpty(unpositionable.Reason); Assert.NotEmpty(filtered.Reason);
    }

    [Fact]
    public void CompleteBindingsWithoutAnchorsRemainUnpositionableEvenWhenStrokesExist()
    {
        var entity = Line(); entity.Remove("start_mm"); entity.Remove("end_mm");
        var constraint = Constraint("ent_constraint",annotation:false); Complete(constraint,Binding("ent_line"));
        var glyph = Assert.Single(NativeSketchConstraintGlyphLayout.Build(Snapshot(new[] {constraint},entity)).Entries);
        Assert.True(glyph.AssociationsAvailable);
        Assert.Equal(NativeSketchConstraintGlyphState.Unpositionable,glyph.State);
        Assert.Null(glyph.AnchorMm);
    }

    [Theory]
    [InlineData("document_id","foreign")]
    [InlineData("revision","r6")]
    [InlineData("sketch_id","ent_other")]
    public void ContradictoryAssociationScopeCannotChooseAnAnchorEvenWhenTheEntityIdExists(string field,string value)
    {
        var binding = Binding("ent_line","Start"); binding[field] = value;
        var constraint = Constraint("ent_constraint",annotation:false); Complete(constraint,binding);
        var snapshot = Snapshot(new[] {constraint},Line());
        var glyph = Assert.Single(NativeSketchConstraintGlyphLayout.Build(snapshot).Entries);
        Assert.False(glyph.AssociationsAvailable); Assert.NotEmpty(glyph.AssociationReason);
        Assert.Empty(glyph.RelatedEntities);
        Assert.Equal(NativeSketchConstraintGlyphState.Unpositionable,glyph.State);
        Assert.Null(glyph.AnchorMm); Assert.Null(glyph.PositionMm);
        // The layout reports its view decision without rewriting the parsed source DTO.
        Assert.Equal("complete",glyph.Constraint.BindingState);
        Assert.Equal(value,(string)glyph.Constraint.Data["related_entities"]![0]![field]!);
    }

    [Fact]
    public void AForeignAssociationDoesNotInvalidateAnIndependentNativeAnnotation()
    {
        var binding = Binding("ent_line"); binding["revision"] = "r6";
        var constraint = Constraint("ent_constraint"); Complete(constraint,binding);
        var glyph = Assert.Single(NativeSketchConstraintGlyphLayout.Build(Snapshot(new[] {constraint},Line())).Entries);
        Assert.False(glyph.AssociationsAvailable);
        Assert.Equal(NativeSketchConstraintGlyphState.Visible,glyph.State);
        Assert.Equal(NativeSketchConstraintAnchorSource.NativeAnnotation,glyph.AnchorSource);
        Assert.Equal(new CadPoint(10,20),glyph.AnchorMm.Value);
    }

    [Fact]
    public void MatchingExplicitScopeIsAcceptedAndDataCopiesCannotChangeTheLayoutOrSource()
    {
        var binding = Binding("ent_line","End"); binding["document_id"] = "part"; binding["revision"] = "r7"; binding["sketch_id"] = "ent_sketch";
        var constraint = Constraint("ent_constraint",annotation:false); Complete(constraint,binding);
        var snapshot = Snapshot(new[] {constraint},Line());
        var result = NativeSketchConstraintGlyphLayout.Build(snapshot);
        var glyph = Assert.Single(result.Entries);
        Assert.True(glyph.AssociationsAvailable); Assert.Equal(new CadPoint(8,9),glyph.AnchorMm.Value);
        Assert.Equal("ent_line",Assert.Single(glyph.RelatedEntities).EntityId);
        var copy = glyph.Constraint.Data; copy["related_entities"]![0]!["revision"] = "changed";
        Assert.Equal("r7",(string)glyph.Constraint.Data["related_entities"]![0]!["revision"]!);
        Assert.True(Assert.Single(NativeSketchConstraintGlyphLayout.Build(snapshot).Entries).AssociationsAvailable);
        Assert.Equal(new CadPoint(8,9),glyph.PositionMm.Value);
    }

    [Fact]
    public void ParserRejectsBindingsToAnEntityOutsideTheSnapshotBeforeLayout()
    {
        var constraint = Constraint("ent_constraint",annotation:false); Complete(constraint,Binding("ent_foreign","Start"));
        Assert.Throws<FormatException>(()=>Snapshot(new[] {constraint},Line()));
    }

    [Fact]
    public void CurrentSelectionOverridesVisibilityFilterAndReceivesTheFirstBudgetSlot()
    {
        var snapshot = Snapshot(new[] {Constraint("ent_a"),Constraint("ent_z","kParallelConstraintObject")});
        var selected = snapshot.Constraints.Single(c=>c.Reference.EntityId == "ent_z").Reference;
        var result = NativeSketchConstraintGlyphLayout.Build(snapshot,new NativeSketchConstraintGlyphOptions(maxVisible:1,showAll:false,nativeTypes:Array.Empty<string>()),selected);
        Assert.True(result.SelectedReferenceFound);
        var glyph = Assert.Single(result.Entries.Where(e=>e.State == NativeSketchConstraintGlyphState.Visible));
        Assert.Equal("ent_z",glyph.Reference.EntityId); Assert.True(glyph.Selected);
        Assert.Equal(NativeSketchConstraintGlyphState.HiddenByFilter,result.Entries.Single(e=>!e.Selected).State);
        result = NativeSketchConstraintGlyphLayout.Build(snapshot,new NativeSketchConstraintGlyphOptions(maxVisible:1),selected);
        Assert.Equal(NativeSketchConstraintGlyphState.OmittedByBudget,result.Entries.Single(e=>!e.Selected).State);
    }

    [Theory]
    [InlineData("other","r7","ent_sketch",SketchSubpart.Entity)]
    [InlineData("part","r8","ent_sketch",SketchSubpart.Entity)]
    [InlineData("part","r7","ent_other",SketchSubpart.Entity)]
    [InlineData("part","r7","ent_sketch",SketchSubpart.Start)]
    public void StaleOrForeignSelectionsCannotOverrideFilters(string document,string revision,string sketch,SketchSubpart part)
    {
        var snapshot = Snapshot(new[] {Constraint("ent_fix")});
        var selected = new SketchEntityRef(document,revision,sketch,"ent_fix","constraint",part);
        var result = NativeSketchConstraintGlyphLayout.Build(snapshot,new NativeSketchConstraintGlyphOptions(showAll:false),selected);
        Assert.False(result.SelectedReferenceFound);
        Assert.Equal(NativeSketchConstraintGlyphState.HiddenByFilter,Assert.Single(result.Entries).State);
    }

    [Fact]
    public void PlacementIsStableAcrossSnapshotOrderAndReportsBoundedCollisionFailure()
    {
        var constraints = Enumerable.Range(0,34).Select(i=>Constraint("ent_"+i.ToString("D2"))).ToArray();
        var first = NativeSketchConstraintGlyphLayout.Build(Snapshot(constraints));
        var reversed = NativeSketchConstraintGlyphLayout.Build(Snapshot(constraints.Reverse()));
        Assert.Equal(first.Entries.Select(e=>e.Reference.EntityId),reversed.Entries.Select(e=>e.Reference.EntityId));
        Assert.Equal(first.Entries.Select(e=>e.PositionMm),reversed.Entries.Select(e=>e.PositionMm));
        Assert.All(first.Entries.Take(33),e=>Assert.False(e.MayOverlap));
        var last = first.Entries.Last();
        Assert.True(last.MayOverlap); Assert.NotEmpty(last.Reason);
        Assert.Equal(NativeSketchConstraintGlyphState.Visible,last.State);
        Assert.All(first.Entries,e=>Assert.Equal(new CadPoint(10,20),e.AnchorMm.Value));
        var positions = first.Entries.Take(33).Select(e=>e.PositionMm.Value).ToArray();
        for(int i=0;i<positions.Length;i++) for(int j=i+1;j<positions.Length;j++)
            Assert.True(Math.Abs(positions[i].X-positions[j].X) >= 9 || Math.Abs(positions[i].Y-positions[j].Y) >= 5);
    }

    [Fact]
    public void UnsupportedConstraintKeepsItsDiagnosticAndSnapshotPartialState()
    {
        var unsupported = Constraint("ent_unknown","kUnknownConstraintObject"); unsupported["supported"] = false;
        unsupported["unavailable_reason"] = "Unsupported native type.";
        var result = NativeSketchConstraintGlyphLayout.Build(Snapshot(new[] {unsupported}));
        Assert.True(result.SnapshotPartial);
        var glyph = Assert.Single(result.Entries);
        Assert.Equal(NativeSketchConstraintGlyphState.Unsupported,glyph.State);
        Assert.Equal("Unsupported native type.",glyph.Reason);
        Assert.Equal("kUnknownConstraintObject",glyph.NativeType);
        Assert.Null(glyph.PositionMm);
    }

    [Fact]
    public void OptionsFreezeFiltersAndEnforceFiniteBoundedFootprintsAndBudget()
    {
        var types = new[] {"kGroundConstraintObject"}; var options = new NativeSketchConstraintGlyphOptions(nativeTypes:types);
        types[0] = "changed"; Assert.Equal("kGroundConstraintObject",Assert.Single(options.NativeTypes));
        Assert.Throws<ArgumentNullException>(()=>NativeSketchConstraintGlyphLayout.Build(null));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new NativeSketchConstraintGlyphOptions(maxVisible:0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new NativeSketchConstraintGlyphOptions(maxVisible:513));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new NativeSketchConstraintGlyphOptions(widthMm:double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new NativeSketchConstraintGlyphOptions(heightMm:0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new NativeSketchConstraintGlyphOptions(gapMm:double.PositiveInfinity));
        Assert.Throws<ArgumentException>(()=>new NativeSketchConstraintGlyphOptions(nativeTypes:new[] {""}));
    }
}
