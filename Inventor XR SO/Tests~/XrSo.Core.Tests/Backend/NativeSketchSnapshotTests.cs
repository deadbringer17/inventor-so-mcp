using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchSnapshotTests
{
    private static readonly DocumentState State = new("part", "r7", "v7");
    private static JObject Snapshot(params JObject[] entities) => new()
    {
        ["schema_version"] = 1, ["document_id"] = "part", ["revision"] = "r7",
        ["sketch_id"] = "ent_sketch", ["sketch_name"] = "Existing",
        ["frame"] = new JObject { ["origin_mm"] = new JArray(0,0,0), ["x_axis"] = new JArray(1,0,0), ["y_axis"] = new JArray(0,1,0) },
        ["entities"] = new JArray(entities), ["dimensions"] = new JArray(), ["constraints"] = new JArray(),
        ["partial"] = false, ["constraint_status"] = "underconstrained"
    };
    private static JObject Curve(string id, string type = "SketchLine") => new()
    { ["id"] = id, ["type"] = type, ["supported"] = true, ["strokes_mm"] = new JArray(0,0,10,0) };
    private static JObject ImageObject(bool supported = false) => new()
    {
        ["id"] = "ent_image", ["type"] = "image", ["supported"] = supported,
        ["native_type"] = "kSketchImageObject", ["visible"] = true, ["transparent"] = false,
        ["width_mm"] = 20, ["height_mm"] = 10,
        ["image_frame"] = new JObject
        {
            ["schema"] = 1, ["provenance"] = "native_corner_reference_keys",
            ["corners_mm"] = new JArray(new JArray(3, 4), new JArray(15, 20), new JArray(23, 14), new JArray(11, -2))
        },
        ["image_source"] = new JObject
        {
            ["schema"] = 1, ["kind"] = "managed_original", ["format"] = "bmp",
            ["sha256"] = new string('a', 64), ["asset_id"] = "a_" + new string('a', 64),
            ["byte_length"] = 70, ["pixel_width"] = 2, ["pixel_height"] = 2
        }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImageMetadataParsesRotatedRectangleWithoutInventingStrokesOrPixelProof(bool supported)
    {
        JObject image = ImageObject(supported);
        var snapshot = NativeSketchSnapshot.Parse(Snapshot(image), State, "ent_sketch");
        var entity = Assert.Single(snapshot.Entities); var typed = Assert.IsType<NativeSketchImage>(entity.Image);
        Assert.Equal(supported, entity.Supported); Assert.Equal(!supported, snapshot.Partial);
        Assert.Equal(new CadPoint(3, 4), typed.CornersMm[0]); Assert.Equal(new CadPoint(23, 14), typed.CornersMm[2]);
        Assert.Equal(20, typed.WidthMm); Assert.Equal(10, typed.HeightMm); Assert.True(typed.Visible); Assert.False(typed.Transparent);
        Assert.Equal("kSketchImageObject", typed.NativeType); Assert.Equal(new string('a', 64), typed.Sha256);
        Assert.Equal("a_" + typed.Sha256, typed.AssetId); Assert.Equal(70, typed.ByteLength);
        Assert.Equal(2, typed.PixelWidth); Assert.Equal(2, typed.PixelHeight); Assert.Equal("bmp", typed.Format);
        Assert.Equal("native_corner_reference_keys", typed.Provenance); Assert.Equal("managed_original", typed.SourceKind);
        Assert.Empty(entity.Strokes); Assert.Empty(snapshot.PickAll(new CadPoint(3, 4), 1));
        image["image_frame"]!["corners_mm"]![0]![0] = 99;
        image["image_source"]!["pixel_width"] = 99;
        var copy = entity.Data; copy["image_frame"]!["corners_mm"]![0]![1] = 99;
        Assert.Equal(new CadPoint(3, 4), typed.CornersMm[0]); Assert.Equal(2, typed.PixelWidth);
        var collection = Assert.IsAssignableFrom<IList<CadPoint>>(typed.CornersMm);
        Assert.Throws<NotSupportedException>(() => collection[0] = new CadPoint(0, 0));
    }

    [Theory]
    [InlineData("byte_length", "33554433")]
    [InlineData("byte_length", "53")]
    [InlineData("byte_length", "9999999999999999999999999999")]
    [InlineData("byte_length", "70.0")]
    [InlineData("pixel_width", "0")]
    [InlineData("pixel_width", "2147483647")]
    [InlineData("pixel_height", "-2")]
    [InlineData("sha256", "\"wrong\"")]
    [InlineData("sha256", "\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
    [InlineData("asset_id", "\"a_wrong\"")]
    [InlineData("format", "\"png\"")]
    [InlineData("schema", "2")]
    [InlineData("schema", "\"1\"")]
    [InlineData("kind", "\"native_pixels\"")]
    public void InvalidImageSourceMetadataFailsClosed(string field, string value)
    {
        var image = ImageObject(); image["image_source"]![field] = JToken.Parse(value);
        Assert.Throws<FormatException>(() => NativeSketchSnapshot.Parse(Snapshot(image), State, "ent_sketch"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("source_missing")]
    [InlineData("schema")]
    [InlineData("provenance")]
    [InlineData("count")]
    [InlineData("coordinate_dimension")]
    [InlineData("coordinate_string")]
    [InlineData("nonfinite")]
    [InlineData("overflow")]
    [InlineData("zero_side")]
    [InlineData("width")]
    [InlineData("closure")]
    [InlineData("skew")]
    [InlineData("source_url")]
    [InlineData("frame_bytes")]
    [InlineData("visible")]
    [InlineData("transparent")]
    [InlineData("native_type")]
    [InlineData("type")]
    public void InvalidImageQuadAndUntrustedFieldsAreRejected(string error)
    {
        var image = ImageObject(); var frame = (JObject)image["image_frame"]!; var corners = (JArray)frame["corners_mm"]!;
        switch (error)
        {
            case "missing": image.Remove("image_frame"); break;
            case "source_missing": image.Remove("image_source"); break;
            case "schema": frame["schema"] = 2; break;
            case "provenance": frame["provenance"] = "bounding_box"; break;
            case "count": corners.RemoveAt(3); break;
            case "coordinate_dimension": corners[0] = new JArray(3, 4, 0); break;
            case "coordinate_string": corners[0] = new JArray("3", 4); break;
            case "nonfinite": corners[0] = new JArray(double.NaN, 4); break;
            case "overflow": corners[0] = new JArray(double.MaxValue, double.MaxValue); break;
            case "zero_side": corners[1] = corners[0].DeepClone(); break;
            case "width": image["width_mm"] = 19; break;
            case "closure": corners[2] = new JArray(23.01, 14); break;
            case "skew":
                image["width_mm"] = 20; image["height_mm"] = Math.Sqrt(101);
                frame["corners_mm"] = new JArray(new JArray(0, 0), new JArray(20, 0), new JArray(21, -10), new JArray(1, -10)); break;
            case "source_url": image["image_source"]!["url"] = "https://untrusted.invalid/image"; break;
            case "frame_bytes": frame["bytes"] = "not-pixels"; break;
            case "visible": image["visible"] = 1; break;
            case "transparent": image["transparent"] = "false"; break;
            case "native_type": image["native_type"] = 1; break;
            case "type": image["type"] = "line"; break;
        }
        Assert.Throws<FormatException>(() => NativeSketchSnapshot.Parse(Snapshot(image), State, "ent_sketch"));
    }

    [Fact]
    public void LegacyUnsupportedImageRemainsReadableAndSupportedImageRequiresMetadata()
    {
        var image = new JObject { ["id"] = "ent_image", ["type"] = "image", ["supported"] = false };
        var snapshot = NativeSketchSnapshot.Parse(Snapshot(image), State, "ent_sketch");
        Assert.True(snapshot.Partial); Assert.Null(Assert.Single(snapshot.Entities).Image);
        image["supported"] = true;
        Assert.Throws<FormatException>(() => NativeSketchSnapshot.Parse(Snapshot(image), State, "ent_sketch"));
    }

    [Fact]
    public void SourceByteBudgetIncludesItsExactUpperBoundaryWithoutClaimingBytesWereRead()
    {
        var image = ImageObject(); image["image_source"]!["byte_length"] = 32 * 1024 * 1024;
        var entity = Assert.Single(NativeSketchSnapshot.Parse(Snapshot(image), State, "ent_sketch").Entities);
        Assert.Equal(32 * 1024 * 1024, entity.Image.ByteLength); Assert.False(entity.Supported);
        Assert.Null(entity.Data["image_source"]!["bytes"]);
    }

    [Theory]
    [InlineData("[\"top_left\",\"top_right\",\"bottom_right\",\"bottom_left\"]", true)]
    [InlineData("[\"top_left\",\"top_right\",\"bottom_left\",\"bottom_right\"]", false)]
    [InlineData("[\"top_left\",\"top_right\",\"bottom_right\"]", false)]
    [InlineData("[1,2,3,4]", false)]
    [InlineData("null", false)]
    public void ExplicitCornerOrderMustMatchNativeReferenceKeyOrder(string order, bool valid)
    {
        var image = ImageObject(); image["image_frame"]!["corner_order"] = JToken.Parse(order);
        if (valid) Assert.NotNull(Assert.Single(NativeSketchSnapshot.Parse(Snapshot(image), State, "ent_sketch").Entities).Image);
        else Assert.Throws<FormatException>(() => NativeSketchSnapshot.Parse(Snapshot(image), State, "ent_sketch"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ImageDimensionsMustBePositiveAndFinite(double value)
    {
        var image = ImageObject(); image["height_mm"] = value;
        Assert.Throws<FormatException>(() => NativeSketchSnapshot.Parse(Snapshot(image), State, "ent_sketch"));
    }
    [Fact]
    public void WindowContainmentCrossingAndAnchorsUseCurrentOpaqueReferences()
    {
        var line=Curve("ent_line","line");line["strokes_mm"]=new JArray(-10,0,10,0);line["start_mm"]=new JArray(-10,0);line["end_mm"]=new JArray(10,0);
        var inside=Curve("ent_inside","spline");inside["strokes_mm"]=new JArray(-.5,-.5,0,.5,.5,-.5);
        var point=new JObject {["id"]="ent_point",["type"]="point",["supported"]=true,["point_mm"]=new JArray(1,1)};
        var parsed=NativeSketchSnapshot.Parse(Snapshot(line,inside,point),State,"ent_sketch");
        var contained=NativeSketchWindow.Query(parsed,new CadPoint(-1,-1),new CadPoint(1,1),NativeSketchWindowMode.Contained);
        Assert.Equal(new[] {"ent_inside","ent_point"},contained.Select(h=>h.Reference.EntityId));
        var crossing=NativeSketchWindow.Query(parsed,new CadPoint(1,1),new CadPoint(-1,-1),NativeSketchWindowMode.Crossing);
        Assert.Equal(new[] {"ent_inside","ent_line","ent_point"},crossing.Select(h=>h.Reference.EntityId));
        Assert.All(crossing,h=>Assert.True(h.Reference.Matches(State,"ent_sketch")));
        Assert.Empty(NativeSketchWindow.Query(parsed,new CadPoint(-1,-1),new CadPoint(1,1),NativeSketchWindowMode.Crossing,"line",SketchSubpart.Start));
        Assert.Equal(SketchSubpart.Start,Assert.Single(NativeSketchWindow.Query(parsed,new CadPoint(-11,-1),new CadPoint(-9,1),NativeSketchWindowMode.Contained,"line",SketchSubpart.Start)).Reference.Subpart);
        Assert.Throws<ArgumentException>(()=>NativeSketchWindow.Query(parsed,new CadPoint(0,0),new CadPoint(0,1),NativeSketchWindowMode.Crossing));
        Assert.Throws<ArgumentOutOfRangeException>(()=>NativeSketchWindow.Query(parsed,new CadPoint(0,0),new CadPoint(1,1),(NativeSketchWindowMode)99));
    }
    [Fact]
    public void WindowCrossingChecksEverySegmentAndTheCircleCurveRatherThanItsFilledDisk()
    {
        var bent=Curve("ent_bent","spline");bent["strokes_mm"]=new JArray(-5,-5,-5,5,5,5);
        var circle=Curve("ent_circle","circle");circle["center_mm"]=new JArray(0,0);circle["radius_mm"]=5;
        // Deliberately sparse strokes: the native circle parameters are authoritative.
        circle["strokes_mm"]=new JArray(-5,0,5,0);
        var parsed=NativeSketchSnapshot.Parse(Snapshot(bent,circle),State,"ent_sketch");
        Assert.Empty(NativeSketchWindow.Query(parsed,new CadPoint(-1,-1),new CadPoint(1,1),NativeSketchWindowMode.Crossing));
        Assert.Equal("ent_circle",Assert.Single(NativeSketchWindow.Query(parsed,new CadPoint(4.9,-.1),new CadPoint(5.1,.1),NativeSketchWindowMode.Crossing)).Reference.EntityId);
        Assert.Equal(2,NativeSketchWindow.Query(parsed,new CadPoint(-6,-6),new CadPoint(6,6),NativeSketchWindowMode.Contained).Count);
        Assert.Equal("ent_bent",Assert.Single(NativeSketchWindow.Query(parsed,new CadPoint(-5.1,-1),new CadPoint(-4.9,1),NativeSketchWindowMode.Crossing,"spline")).Reference.EntityId);
        Assert.Empty(NativeSketchWindow.Query(parsed,new CadPoint(-5,-5),new CadPoint(4.99,5),NativeSketchWindowMode.Contained,"circle"));
    }
    [Fact]
    public void PatternDefinitionIsRevisionScopedDefensivelyCopiedAndRejectsInvalidCounts()
    {
        var json=Snapshot();json["constraints"]=new JArray(new JObject {["id"]="ent_pattern",["type"]="constraint",["supported"]=true,
            ["pattern_definition"]=new JObject {["kind"]="circular",["count"]=new JObject {["value"]=4,["expression"]="4 ul"},
                ["angle_deg"]=new JObject {["value"]=360},["associative"]=true,["fitted"]=true,["natural_axis"]=true,["symmetric"]=false,
                ["element_count"]=4,["suppressed_elements"]=new JArray(2)}});
        var parsed=NativeSketchSnapshot.Parse(json,State,"ent_sketch");var pattern=Assert.Single(parsed.Constraints);
        Assert.Equal("r7",pattern.Reference.Revision);var copy=pattern.Data;copy["pattern_definition"]!["count"]!["value"]=6;
        Assert.Equal(4,(double)pattern.Data["pattern_definition"]!["count"]!["value"]!);
        json["constraints"]![0]!["pattern_definition"]!["count"]!["value"]=2.5;
        Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(json,State,"ent_sketch"));
        json["constraints"]![0]!["pattern_definition"]!["count"]!["value"]=4;
        ((JArray)json["constraints"]![0]!["pattern_definition"]!["suppressed_elements"]!).Add(2);
        Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(json,State,"ent_sketch"));
    }

    [Fact]
    public void ConstraintAssociationsAreCurrentValidatedAndDefensivelyCopied()
    {
        var line=Curve("ent_line","line");line["start_mm"]=new JArray(0,0);
        var json=Snapshot(line);json["constraints"]=new JArray(new JObject {["id"]="ent_constraint",["type"]="constraint",["supported"]=true,
            ["binding_state"]="complete",["annotation_mm"]=new JArray(0,0),["related_entities"]=new JArray(new JObject {["entity_id"]="ent_line",["type"]="line",["subpart"]="Start"})});
        var parsed=NativeSketchSnapshot.Parse(json,State,"ent_sketch");var constraint=Assert.Single(parsed.Constraints);
        var reference=Assert.Single(constraint.RelatedEntities);Assert.True(reference.Matches(State,"ent_sketch"));Assert.Equal(SketchSubpart.Start,reference.Subpart);
        Assert.Equal(new CadPoint(0,0),constraint.AnnotationMm.Value);
        json["constraints"][0]["related_entities"][0]["entity_id"]="ent_foreign";Assert.Equal("ent_line",reference.EntityId);
        Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(json,State,"ent_sketch"));
        json["constraints"][0]["related_entities"][0]["entity_id"]="ent_line";json["constraints"][0]["related_entities"][0]["subpart"]="Center";
        Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(json,State,"ent_sketch"));
        json["constraints"][0]["related_entities"][0]["subpart"]="Start";((JArray)json["constraints"][0]["related_entities"]).Add(json["constraints"][0]["related_entities"][0].DeepClone());
        Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(json,State,"ent_sketch"));
    }
    [Fact]
    public void MissingCapabilitiesRemainReadableButNeverAuthorizeMutation()
    {
        var parsed=NativeSketchSnapshot.Parse(Snapshot(Curve("ent_line")),State,"ent_sketch");
        Assert.False(parsed.Partial); Assert.Empty(parsed.SupportedVariants); Assert.False(parsed.SupportsVariant("line"));
        var json=Snapshot(Curve("ent_line")); json["supported_variants"]=new JArray("line","constraint_ground");
        parsed=NativeSketchSnapshot.Parse(json,State,"ent_sketch");
        ((JArray)json["supported_variants"]).Clear();
        Assert.True(parsed.SupportsVariant("line")); Assert.False(parsed.SupportsVariant("LINE")); Assert.False(parsed.SupportsVariant("constraint_smooth"));
    }
    [Theory]
    [InlineData("[\"line\",\"line\"]")][InlineData("[true]")][InlineData("[\"\"]")][InlineData("{}")]
    public void MalformedCapabilitiesFailClosed(string value)
    {
        var json=Snapshot(Curve("ent_line")); json["supported_variants"]=JToken.Parse(value);
        Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(json,State,"ent_sketch"));
    }

    [Theory]
    [InlineData("document_id", "other")]
    [InlineData("revision", "r8")]
    [InlineData("sketch_id", "ent_other")]
    public void RefusesSnapshotFromAnotherCadState(string field, string value)
    {
        var json = Snapshot(Curve("ent_line")); json[field] = value;
        Assert.Throws<FormatException>(() => NativeSketchSnapshot.Parse(json, State, "ent_sketch"));
    }

    [Fact]
    public void KeepsNativeSplineTypeAndDefensiveDataWhilePickingOverlapsDeterministically()
    {
        var json = Snapshot(Curve("ent_b", "SketchControlPointSpline"), Curve("ent_a"));
        var parsed = NativeSketchSnapshot.Parse(json, State, "ent_sketch");
        ((JObject)json["entities"][0])["type"] = "copied_line";
        parsed.Entities[0].Data["type"] = "mutated";
        Assert.Equal("SketchControlPointSpline", parsed.Entities[0].Type);
        Assert.Equal("SketchControlPointSpline", (string)parsed.Entities[0].Data["type"]);
        Assert.Equal(new[] { "ent_a", "ent_b" }, parsed.PickAll(new CadPoint(5, .1), .2).Select(e => e.Reference.EntityId));
        Assert.Single(parsed.PickAll(new CadPoint(5, .1), .2, "SketchControlPointSpline"));
        var reference = parsed.Entities[0].Reference.WithSubpart(SketchSubpart.Start);
        Assert.True(reference.Matches(State, "ent_sketch"));
        Assert.False(reference.Matches(new DocumentState("part", "r8", "v8"), "ent_sketch"));
        Assert.False(reference.Matches(State, "ent_other"));
    }

    [Fact]
    public void UnknownObjectsRemainVisibleInSnapshotAndMakeItPartial()
    {
        var unknown = new JObject { ["type"] = "SketchImage", ["supported"] = false, ["unavailable_reason"] = "Image display pending" };
        var parsed = NativeSketchSnapshot.Parse(Snapshot(unknown), State, "ent_sketch");
        Assert.True(parsed.Partial);
        Assert.Equal("SketchImage", Assert.Single(parsed.Entities).Type);
        Assert.Equal("Image display pending", parsed.Entities[0].UnavailableReason);
        Assert.Empty(parsed.PickAll(new CadPoint(0,0), 1));
    }

    [Fact]
    public void RefusesMissingOrDuplicateIdentityIncludingConstraints()
    {
        var missing = Curve("ent_line"); missing.Remove("id");
        Assert.Throws<FormatException>(() => NativeSketchSnapshot.Parse(Snapshot(missing), State, "ent_sketch"));
        var duplicate = Snapshot(Curve("ent_line"));
        duplicate["constraints"] = new JArray(new JObject { ["type"] = "CoincidentConstraint", ["supported"] = true, ["id"] = "ent_line" });
        Assert.Throws<FormatException>(() => NativeSketchSnapshot.Parse(duplicate, State, "ent_sketch"));
    }

    [Fact]
    public void PointAndDegenerateCurveCanBePickedWithoutDivisionByZero()
    {
        var line = Curve("ent_line"); line["strokes_mm"] = new JArray(1,2,1,2);
        var point = new JObject { ["id"] = "ent_point", ["type"] = "SketchPoint", ["supported"] = true, ["point_mm"] = new JArray(1,2) };
        var parsed = NativeSketchSnapshot.Parse(Snapshot(line, point), State, "ent_sketch");
        Assert.Equal(2, parsed.PickAll(new CadPoint(1,2), .01).Count);
        Assert.All(parsed.Entities, e => Assert.Equal(0, e.DistanceTo(new CadPoint(1,2))));
        Assert.Throws<ArgumentOutOfRangeException>(() => parsed.PickAll(new CadPoint(1,2), double.NaN));
    }

    [Fact]
    public void CurveAndSubpartsRemainDistinctAtTheSameLocation()
    {
        var line=Curve("ent_line","line"); line["start_mm"]=new JArray(0,0); line["end_mm"]=new JArray(10,0);
        var circle=Curve("ent_circle","circle"); circle["center_mm"]=new JArray(0,0);
        var parsed=NativeSketchSnapshot.Parse(Snapshot(line,circle),State,"ent_sketch");
        var hits=parsed.PickTargets(new CadPoint(0,0),.01);
        Assert.Contains(hits,h=>h.Reference.EntityId=="ent_line" && h.Reference.Subpart==SketchSubpart.Entity);
        Assert.Contains(hits,h=>h.Reference.EntityId=="ent_line" && h.Reference.Subpart==SketchSubpart.Start);
        Assert.Contains(hits,h=>h.Reference.EntityId=="ent_circle" && h.Reference.Subpart==SketchSubpart.Center);
        Assert.DoesNotContain(hits,h=>h.Reference.Subpart==SketchSubpart.End);
        var endpoint=Assert.Single(parsed.PickTargets(new CadPoint(10,0),.01,"line",SketchSubpart.End));
        Assert.True(endpoint.Reference.Matches(State,"ent_sketch")); Assert.Equal(0,endpoint.DistanceMm);
        Assert.Empty(parsed.PickTargets(new CadPoint(10,0),.01,"circle",SketchSubpart.End));
    }
    [Fact]
    public void MalformedNativeAnchorCannotEnterSelection()
    {
        var line=Curve("ent_line"); line["start_mm"]=new JArray(double.NaN,0);
        Assert.Throws<ArgumentException>(()=>NativeSketchSnapshot.Parse(Snapshot(line),State,"ent_sketch"));
    }
    [Fact]
    public void RefusesGlobalStrokeBudgetEvenWhenEveryCurveFitsItsIndividualLimit()
    {
        var entities = Enumerable.Range(0, 13).Select(i =>
        { var curve = Curve("ent_" + i); curve["strokes_mm"] = new JArray(Enumerable.Repeat(0, 16384)); return curve; }).ToArray();
        Assert.Throws<FormatException>(() => NativeSketchSnapshot.Parse(Snapshot(entities), State, "ent_sketch"));
    }
}
