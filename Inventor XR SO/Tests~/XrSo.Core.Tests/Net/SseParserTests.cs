using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Tests.Net;

public class SseParserTests
{
    [Fact]
    public void DispatchesOnBlankLineAndJoinsMultiLineData()
    {
        var events = SseParser.DataPayloads("event: message\r\ndata: {\"a\":1}\r\n\r\n: comment\ndata: line1\ndata:line2\n\n");
        Assert.Equal(new[] { "{\"a\":1}", "line1\nline2" }, events);
    }

    [Fact]
    public void FlushEmitsATrailingEventWithoutBlankLine() =>
        Assert.Equal(new[] { "x" }, SseParser.DataPayloads("data: x"));

    [Fact]
    public void LinesWithoutDataEmitNothing() => Assert.Empty(SseParser.DataPayloads("id: 3\nretry: 100\n\n"));

    [Fact]
    public void CrLfLinesAreHandled() =>
        Assert.Equal(new[] { "a", "b" }, SseParser.DataPayloads("data: a\r\n\r\ndata: b\r\n\r\n"));

    [Fact]
    public void ADataFieldWithoutAColonIsAnEmptyValue() =>
        Assert.Equal(new[] { "", "x" }, SseParser.DataPayloads("data\n\ndata: x\n\n"));

    [Fact]
    public void OnlyOneLeadingSpaceIsStripped()
    {
        Assert.Equal(new[] { "x" }, SseParser.DataPayloads("data:x\n\n"));
        Assert.Equal(new[] { " x" }, SseParser.DataPayloads("data:  x\n\n"));
    }

    [Fact]
    public void CommentsAndLookalikeFieldsAreIgnored() =>
        Assert.Equal(new[] { "y" }, SseParser.DataPayloads(":data: no\ndatax: no\nDATA: no\ndata: y\n\n"));

    [Fact]
    public void AFinalEventWithoutATrailingBlankLineIsEmitted() =>
        Assert.Equal(new[] { "a", "b" }, SseParser.DataPayloads("data: a\n\ndata: b\r\n"));

    [Fact]
    public void ACarriageReturnIsOnlyStrippedFromTheLineEnd() =>
        Assert.Equal(new[] { "a\rb" }, SseParser.DataPayloads("data: a\rb\r\n\r\n"));
}
