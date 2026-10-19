using InventorXrSo.Core.Backend;

namespace XrSo.Core.Tests.Backend;

public sealed class SketchImportResourceTests
{
    private static SketchImportResourcePolicy Policy(long total = 1000, long perResource = 1000, int dependencies = 4)
        => new SketchImportResourcePolicy(8, dependencies, 64, perResource, total, 10000, 1000, 100000,
            new[] { new SketchImportFormat(SketchImportResourceKind.Image, "png"),
                new SketchImportFormat(SketchImportResourceKind.Points, "xlsx"),
                new SketchImportFormat(SketchImportResourceKind.Acad, "dwg"),
                new SketchImportFormat(SketchImportResourceKind.Acad, "dxf") },
            new Dictionary<string, double> { ["mm"] = 1, ["cm"] = 10, ["inch"] = 25.4 });
    private static SketchImportResource Image(string id = "asset_image", string[] dependencies = null, long bytes = 100,
        int width = 20, int height = 10, string format = "png")
        => new SketchImportResource(id, SketchImportResourceKind.Image, format, bytes, dependencies,
            image: new SketchImportImageMetadata(width, height));
    private static SketchImportResource Geometry(SketchImportResourceKind kind = SketchImportResourceKind.Points,
        string unit = "mm", double factor = 1, long? count = 3, SketchImportBounds bounds = null)
        => new SketchImportResource("asset_geometry", kind, kind == SketchImportResourceKind.Points ? "xlsx" : "dwg", 100,
            geometry: new SketchImportGeometryMetadata(unit, factor, count, bounds));
    private static void Valid(params SketchImportResource[] resources)
        => SketchImportResourceValidator.Validate(new SketchImportResourceBundle(resources[0].AssetId, resources), Policy());
    private static void Invalid(params SketchImportResource[] resources)
        => Assert.Throws<ArgumentException>(() => Valid(resources));

    [Fact]
    public void AcceptsImagePointsAndAcadMetadataWithExplicitPolicy()
    {
        Valid(Image());
        Valid(Geometry(bounds: new SketchImportBounds(-10, 0, 10, 20)));
        Valid(Geometry(SketchImportResourceKind.Acad, "inch", 25.4, null));
        Valid(Geometry(unit: "cm", factor: 10, count: 1, bounds: new SketchImportBounds(0, 0, 0, 0)));
    }

    [Fact]
    public void SharedDependenciesFormADagAndAreCountedOnce()
    {
        Valid(Image("root", new[] { "left", "right" }), Image("left", new[] { "leaf" }),
            Image("right", new[] { "leaf" }), Image("leaf"));
    }

    [Theory]
    [InlineData("C:\\image.png")] [InlineData("/sdcard/image.png")] [InlineData("../image")]
    [InlineData("https://host/image")] [InlineData("")] [InlineData("asset image")]
    [InlineData("asset.image")] [InlineData("asset\nimage")]
    public void AssetIdsCannotCarryPathsUrlsOrWhitespace(string id) => Invalid(Image(id));

    [Fact]
    public void RejectsMissingRootDuplicateIdsNullResourcesAndEmptyBundles()
    {
        Assert.Throws<ArgumentException>(() => SketchImportResourceValidator.Validate(
            new SketchImportResourceBundle("missing", new[] { Image() }), Policy()));
        Invalid(Image(), Image());
        Invalid(Image(), null);
        Assert.Throws<ArgumentException>(() => SketchImportResourceValidator.Validate(
            new SketchImportResourceBundle("root", Array.Empty<SketchImportResource>()), Policy()));
    }

    [Fact]
    public void RejectsMissingDuplicateCyclicSelfAndUnrelatedDependencies()
    {
        Invalid(Image("root", new[] { "missing" }));
        Invalid(Image("root", new[] { "leaf", "leaf" }), Image("leaf"));
        Invalid(Image("root", new[] { "leaf" }), Image("leaf", new[] { "root" }));
        Invalid(Image("root", new[] { "root" }));
        Invalid(Image("root"), Image("unrelated"));
        Invalid(Image("root", new[] { "/sdcard/leaf" }));
        Assert.Throws<ArgumentException>(() => SketchImportResourceValidator.Validate(
            new SketchImportResourceBundle("root", new[] { Image("root", new[] { "leaf" }), Image("leaf") }), Policy(dependencies: 0)));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1001)]
    public void RejectsInvalidOrOversizedDeclaredByteLengths(long bytes) => Invalid(Image(bytes: bytes));

    [Fact]
    public void AggregateByteBudgetCannotWrapAtLongMaxValue()
    {
        var bundle = new SketchImportResourceBundle("root", new[] {
            Image("root", new[] { "leaf" }, long.MaxValue), Image("leaf", bytes: 1) });
        Assert.Throws<ArgumentException>(() => SketchImportResourceValidator.Validate(bundle, Policy(long.MaxValue, long.MaxValue)));
        SketchImportResourceValidator.Validate(new SketchImportResourceBundle("root", new[] { Image("root", bytes: long.MaxValue) }),
            Policy(long.MaxValue, long.MaxValue));
    }

    [Fact]
    public void UsesKindAndExactFormatAllowListRatherThanFilenameGuessing()
    {
        Invalid(Image(format: "dwg")); Invalid(Image(format: "PNG")); Invalid(Image(format: ".png"));
        Invalid(Image(format: "folder/png")); Invalid(Image(format: "jpeg"));
        Invalid(new SketchImportResource("asset_image", (SketchImportResourceKind)99, "png", 100,
            image: new SketchImportImageMetadata(20, 10)));
    }

    [Theory]
    [InlineData(0, 10)] [InlineData(-1, 10)] [InlineData(10, 0)] [InlineData(101, 100)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void RejectsInvalidPixelDimensionsWithoutIntegerOverflow(int width, int height)
        => Invalid(Image(width: width, height: height));

    [Fact]
    public void RequiresMetadataConsistentWithResourceKind()
    {
        Invalid(new SketchImportResource("image", SketchImportResourceKind.Image, "png", 10));
        Invalid(new SketchImportResource("image", SketchImportResourceKind.Image, "png", 10,
            image: new SketchImportImageMetadata(2, 2), geometry: new SketchImportGeometryMetadata("mm", 1)));
        Invalid(new SketchImportResource("points", SketchImportResourceKind.Points, "xlsx", 10));
        Invalid(new SketchImportResource("points", SketchImportResourceKind.Points, "xlsx", 10,
            image: new SketchImportImageMetadata(2, 2), geometry: new SketchImportGeometryMetadata("mm", 1, 2)));
        Invalid(Geometry(SketchImportResourceKind.Acad, count: 2));
    }

    [Theory]
    [InlineData(null)] [InlineData(0L)] [InlineData(-1L)] [InlineData(1001L)]
    public void RequiresPositivePointCountWithinExplicitBudget(long? count) => Invalid(Geometry(count: count));

    [Theory]
    [InlineData("unknown", 1)] [InlineData("mm", 10)] [InlineData("cm", 1)]
    [InlineData("mm", 0)] [InlineData("mm", -1)] [InlineData("mm", double.NaN)]
    [InlineData("mm", double.PositiveInfinity)] [InlineData("mm", double.NegativeInfinity)]
    [InlineData(null, 1)]
    public void UnitsAndConversionMustExactlyMatchCallerPolicy(string unit, double factor)
        => Invalid(Geometry(unit: unit, factor: factor));

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    [InlineData(100001)] [InlineData(-100001)]
    public void RejectsNonFiniteOrOutOfBudgetSourceBounds(double coordinate)
        => Invalid(Geometry(bounds: new SketchImportBounds(coordinate, 0, coordinate, 0)));

    [Fact]
    public void BoundsAreOrderedAndConvertedToMillimetersBeforeBudgetCheck()
    {
        Invalid(Geometry(bounds: new SketchImportBounds(2, 0, 1, 1)));
        Invalid(Geometry(bounds: new SketchImportBounds(0, 2, 1, 1)));
        Invalid(Geometry(unit: "cm", factor: 10, bounds: new SketchImportBounds(0, 0, 10001, 1)));
        var huge = new SketchImportResourcePolicy(1, 0, 64, 100, 100, 100, 10, double.MaxValue,
            new[] { new SketchImportFormat(SketchImportResourceKind.Points, "xlsx") },
            new Dictionary<string, double> { ["large"] = double.MaxValue });
        Assert.Throws<ArgumentException>(() => SketchImportResourceValidator.Validate(new SketchImportResourceBundle("asset_geometry",
            new[] { Geometry(unit: "large", factor: double.MaxValue, bounds: new SketchImportBounds(2, 0, 2, 0)) }), huge));
    }

    [Fact]
    public void CallerCollectionsCannotMutateAFrozenBundleOrPolicy()
    {
        string[] dependencies = { "leaf" };
        var root = Image("root", dependencies);
        var resources = new[] { root, Image("leaf") };
        var bundle = new SketchImportResourceBundle("root", resources);
        var formats = new[] { new SketchImportFormat(SketchImportResourceKind.Image, "png") };
        var units = new Dictionary<string, double> { ["mm"] = 1 };
        var policy = new SketchImportResourcePolicy(2, 1, 64, 100, 200, 1000, 10, 1000, formats, units);
        dependencies[0] = "missing"; resources[0] = null;
        formats[0] = new SketchImportFormat(SketchImportResourceKind.Image, "jpeg"); units["mm"] = 10;
        SketchImportResourceValidator.Validate(bundle, policy);
        Assert.Equal("leaf", root.Dependencies[0]);
    }

    [Fact]
    public void PolicyHasNoImplicitDesktopBudgetAndRejectsInvalidDeclarations()
    {
        var format = new SketchImportFormat(SketchImportResourceKind.Image, "png");
        SketchImportResourcePolicy Make(IEnumerable<SketchImportFormat> formats, IEnumerable<KeyValuePair<string, double>> units,
            double coordinateLimit = 1000, int resourceLimit = 1)
            => new SketchImportResourcePolicy(resourceLimit, 0, 64, 100, 100, 1000, 10, coordinateLimit, formats, units);
        Assert.Throws<ArgumentException>(() => Make(Array.Empty<SketchImportFormat>(), Array.Empty<KeyValuePair<string, double>>()));
        Assert.Throws<ArgumentException>(() => Make(new[] { format, format }, Array.Empty<KeyValuePair<string, double>>()));
        Assert.Throws<ArgumentException>(() => Make(new[] { format }, new Dictionary<string, double> { ["mm"] = double.NaN }));
        Assert.Throws<ArgumentException>(() => Make(new[] { format }, Array.Empty<KeyValuePair<string, double>>(), double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => Make(new[] { format }, Array.Empty<KeyValuePair<string, double>>(), resourceLimit: 0));
    }
}
