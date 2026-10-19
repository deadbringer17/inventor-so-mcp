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
    public void SketchReferencesRoundTripWithoutABRepContext()
    {
        foreach(var type in new[] {"sketch","sketch_entity","sketch_constraint","sketch_dimension","sketch_text","sketch_image"})
        {
            var source=new PersistentEntityReference {DocumentId="doc_s1",EntityType=type,Key=[1,2,3],Context=[]};
            var restored=PersistentEntityReference.Decode(source.Encode());
            Assert.Empty(restored.Context); Assert.Equal(type,restored.EntityType); Assert.Equal(source.Key,restored.Key);
        }
    }
    [Theory]
    [InlineData("face")]
    [InlineData("edge")]
    [InlineData("face_proxy")]
    [InlineData("unrecognized")]
    public void ContextFreeSketchContractDoesNotRelaxBRepOrUnknownReferences(string type)
    {
        var source=new PersistentEntityReference {DocumentId="doc_s1",EntityType=type,Key=[1],Context=[]};
        Assert.Throws<ArgumentException>(()=>source.Encode());
    }
    [Fact]
    public void OversizedReferenceRejectedBeforeDecoding()
        => Assert.Throws<ArgumentException>(() => PersistentEntityReference.Decode("ent_" + new string('x', PersistentEntityReference.MaximumEncodedLength)));
}
