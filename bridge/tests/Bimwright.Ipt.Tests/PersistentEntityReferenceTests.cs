using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Tests;

public sealed class PersistentEntityReferenceTests
{
    [Fact]
    public void ReferenceRoundTripsKeyAndContextWithoutCultureDependence()
    {
        var source = new PersistentEntityReference { DocumentId = "doc_123", EntityType = "face_proxy", Key = [0, 255, 34], Context = [9, 0, 1] };
        var token = source.Encode();
        Assert.DoesNotContain("/", token);
        var restored = PersistentEntityReference.Decode(token);
        Assert.Equal(source.DocumentId, restored.DocumentId);
        Assert.Equal(source.Key, restored.Key);
        Assert.Equal(source.Context, restored.Context);
        Assert.Equal(token, restored.Encode());
    }

    [Theory]
    [InlineData("")]
    [InlineData("edge:1")]
    [InlineData("ent_!!!")]
    [InlineData("ent_e30")]
    public void InvalidOrLegacyPositionalReferencesFail(string id)
        => Assert.Throws<ArgumentException>(() => PersistentEntityReference.Decode(id));

    [Fact]
    public void OversizedReferenceRejectedBeforeDecoding()
        => Assert.Throws<ArgumentException>(() => PersistentEntityReference.Decode("ent_" + new string('x', PersistentEntityReference.MaximumEncodedLength)));
}
