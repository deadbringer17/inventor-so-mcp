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
}
