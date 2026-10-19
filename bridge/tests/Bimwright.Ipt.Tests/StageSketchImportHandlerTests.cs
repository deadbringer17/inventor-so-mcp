using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class StageSketchImportHandlerTests
{
    private static SketchImportStagingStore Store(int size = 8, int chunk = 4)
        => new(new SketchImportStagingPolicy(size, size, 1, chunk, TimeSpan.FromMinutes(1), 32));
    private static InventorCommandContext Context(SketchImportStagingStore? store, bool readOnly = false,
        bool experimental = true, Func<bool>? deadline = null) => new()
        { SketchImports = store, ReadOnly = readOnly, AllowExperimental = experimental,
          RequireAtomicWrites = true, IsDeadlineExceeded = deadline };
    private static InventorCommandResult Run(InventorCommandContext context, JObject payload)
    {
        var handler = new StageSketchImportHandler();
        var dispatcher = new CommandDispatcher(new Dictionary<string, IInventorCommand> { [handler.Name] = handler }, 1024 * 1024);
        return dispatcher.Dispatch(context, new InventorCommandEnvelope { Id = Guid.NewGuid(), Command = handler.Name, Params = payload });
    }
    private static JObject Payload(string action) => new() { ["action"] = action, ["owner"] = "owner", ["session"] = "session" };
    private static string Begin(InventorCommandContext context, int length)
    {
        JObject payload = Payload("begin"); payload["byte_length"] = length;
        var result = Run(context, payload); Assert.True(result.Ok, result.Error?.Message);
        return (string)result.Data!["transfer_id"]!;
    }
    private static JObject Chunk(string id, int offset, byte[] bytes)
    {
        var p = Payload("chunk"); p["transfer_id"] = id; p["offset"] = offset;
        p["data_base64"] = Convert.ToBase64String(bytes); return p;
    }
    private static JObject Seal(string id, byte[] bytes)
    {
        var p = Payload("seal"); p["transfer_id"] = id;
        p["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)); return p;
    }
    private static void Error(string code, InventorCommandResult result)
    { Assert.False(result.Ok); Assert.Equal(code, result.Error!.Code); Assert.NotNull(result.Error.Details!["reason"]); }

    [Fact]
    public void DispatcherTransfersSealsAndDiscardsWithoutInventorApplication()
    {
        using var store = Store(); var ctx = Context(store);
        string id = Begin(ctx, 5);
        var first = Run(ctx, Chunk(id, 0, new byte[] { 1, 2, 3, 4 })); Assert.True(first.Ok);
        Assert.Equal(4, (int)first.Data!["received_bytes"]!);
        var second = Run(ctx, Chunk(id, 4, new byte[] { 5 })); Assert.True(second.Ok);
        Assert.Equal(5, (int)second.Data!["received_bytes"]!);
        var sealedResult = Run(ctx, Seal(id, new byte[] { 1, 2, 3, 4, 5 })); Assert.True(sealedResult.Ok);
        Assert.True((bool)sealedResult.Data!["sealed"]!); Assert.Equal(id, (string?)sealedResult.Data!["transfer_id"]);
        using (var lease = store.Acquire("owner", "session", id))
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, lease!.ReadBytes());
        var discard = Payload("discard"); discard["transfer_id"] = id;
        Assert.True((bool)Run(ctx, discard).Data!["discarded"]!);
        Assert.False((bool)Run(ctx, discard).Data!["discarded"]!);
        Assert.Equal(0, store.ReservedBytes);
    }

    [Fact]
    public void GatesRefuseBeforeReservationIncludingDirectHandlerReadOnly()
    {
        using var store = Store(); var p = Payload("begin"); p["byte_length"] = 1;
        Assert.Equal(InventorErrorCodes.READ_ONLY, Run(Context(store, readOnly: true), p).Error!.Code);
        Error(InventorErrorCodes.READ_ONLY, new StageSketchImportHandler().Execute(Context(store, readOnly: true), p));
        Error(InventorErrorCodes.EXPERIMENTAL_DISABLED, Run(Context(store, experimental: false), p));
        Error(InventorErrorCodes.TIMEOUT, Run(Context(store, deadline: () => true), p));
        Error(InventorErrorCodes.INVALID_ARGUMENT, Run(Context(null), p)); Assert.Equal(0, store.ReservedBytes);
    }

    [Theory]
    [InlineData("byte_length", "1")]
    [InlineData("byte_length", "1.0")]
    [InlineData("byte_length", "2147483648")]
    [InlineData("byte_length", "null")]
    [InlineData("owner", "1")]
    [InlineData("session", "null")]
    [InlineData("action", "true")]
    public void JsonTypesAndIntegerRangeAreStrict(string field, string value)
    {
        using var store = Store(); var p = Payload("begin"); p["byte_length"] = 1;
        p[field] = field == "byte_length" && value == "1" ? new JValue("1") : JToken.Parse(value);
        Error(InventorErrorCodes.INVALID_ARGUMENT, Run(Context(store), p)); Assert.Equal(0, store.ReservedBytes);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("application")]
    [InlineData("transfer_id")]
    [InlineData("Action")]
    public void UndeclaredFieldsAreRejected(string field)
    {
        using var store = Store(); var p = Payload("begin"); p["byte_length"] = 1; p[field] = "private-value";
        var result = Run(Context(store), p); Error(InventorErrorCodes.INVALID_ARGUMENT, result);
        Assert.DoesNotContain("private-value", result.Error!.Message); Assert.Equal(0, store.ReservedBytes);
    }

    [Fact]
    public void MissingTransferAndForeignIdentityHaveStructuredErrors()
    {
        using var store = Store(); var ctx = Context(store); string id = Begin(ctx, 1);
        var chunk = Chunk(id, 0, new byte[] { 1 }); chunk["owner"] = "foreign";
        Error(InventorErrorCodes.ASSET_NOT_FOUND, Run(ctx, chunk));
        var seal = Seal(id, new byte[] { 1 }); seal["session"] = "foreign";
        Error(InventorErrorCodes.ASSET_NOT_FOUND, Run(ctx, seal));
        Error(InventorErrorCodes.ASSET_NOT_FOUND, Run(ctx, Chunk("missing", 0, new byte[] { 1 })));
        Assert.True(Run(ctx, Chunk(id, 0, new byte[] { 1 })).Ok);
    }

    [Fact]
    public void InvalidBase64OversizedEncodedInputAndWrongOffsetDoNotConsumeTransfer()
    {
        using var store = Store(); var ctx = Context(store); string id = Begin(ctx, 1);
        var chunk = Chunk(id, 0, new byte[] { 1 }); chunk["data_base64"] = "invalid!";
        Error(InventorErrorCodes.INVALID_ARGUMENT, Run(ctx, chunk));
        chunk["data_base64"] = new string('A', StageSketchImportHandler.MaxBase64Characters + 1);
        Error(InventorErrorCodes.INVALID_ARGUMENT, Run(ctx, chunk));
        Error(InventorErrorCodes.INVALID_ARGUMENT, Run(ctx, Chunk(id, 1, new byte[] { 1 })));
        Assert.True(Run(ctx, Chunk(id, 0, new byte[] { 1 })).Ok);
        Assert.True(Run(ctx, Seal(id, new byte[] { 1 })).Ok);
        using var lease = store.Acquire("owner", "session", id); Assert.Equal(new byte[] { 1 }, lease!.ReadBytes());
    }

    [Fact]
    public void ExactChunkBoundaryIsAcceptedAndDecodedBufferDoesNotGetClearedInStore()
    {
        using var store = Store(StageSketchImportHandler.MaxChunkBytes, StageSketchImportHandler.MaxChunkBytes);
        var ctx = Context(store); var bytes = new byte[StageSketchImportHandler.MaxChunkBytes]; bytes[0] = 71; bytes[bytes.Length - 1] = 99;
        string id = Begin(ctx, bytes.Length); Assert.True(Run(ctx, Chunk(id, 0, bytes)).Ok);
        Assert.True(Run(ctx, Seal(id, bytes)).Ok);
        using var lease = store.Acquire("owner", "session", id); Assert.Equal(bytes, lease!.ReadBytes());
    }

    [Fact]
    public void DeadlineAfterDecodeRefusesAppendWithoutAdvancingOffset()
    {
        using var store = Store(); string id = Begin(Context(store), 1); int calls = 0;
        Error(InventorErrorCodes.TIMEOUT, Run(Context(store, deadline: () => ++calls > 1), Chunk(id, 0, new byte[] { 1 })));
        Assert.True(Run(Context(store), Chunk(id, 0, new byte[] { 1 })).Ok);
    }

    [Fact]
    public void CapacityIncompleteSealAndBadHashRemainGenericAndStructured()
    {
        using var store = Store(); var ctx = Context(store); string id = Begin(ctx, 1);
        var p = Payload("begin"); p["byte_length"] = 1;
        Error(InventorErrorCodes.INVALID_ARGUMENT, Run(ctx, p));
        Error(InventorErrorCodes.INVALID_ARGUMENT, Run(ctx, Seal(id, new byte[] { 1 })));
        Assert.True(Run(ctx, Chunk(id, 0, new byte[] { 1 })).Ok);
        Error(InventorErrorCodes.INVALID_ARGUMENT, Run(ctx, Seal(id, new byte[] { 2 })));
        Assert.Equal(0, store.ReservedBytes);
    }
}
