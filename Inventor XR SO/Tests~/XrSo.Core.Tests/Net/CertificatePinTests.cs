using System.Security.Cryptography;
using System.Text;
using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Tests.Net;

public class CertificatePinTests
{
    [Fact]
    public void HashIsLowercaseHexOfTheDerBytes()
    {
        var der = Encoding.ASCII.GetBytes("certificate");
        var expected = Convert.ToHexString(SHA256.HashData(der)).ToLowerInvariant();
        Assert.Equal(expected, CertificatePin.Sha256Hex(der));
    }

    [Theory]
    [InlineData("AB12 CD34")]
    [InlineData("ab:12:cd:34")]
    public void NormalizeStripsSeparatorsAndCase(string raw) =>
        Assert.Equal(new string('a', 56) + "ab12cd34", CertificatePin.Normalize(new string('A', 56) + raw));

    [Fact]
    public void InvalidFingerprintsAreRejected()
    {
        Assert.False(CertificatePin.IsValid("abc"));
        Assert.False(CertificatePin.IsValid(new string('g', 64)));
        Assert.False(CertificatePin.IsValid(null));
        Assert.True(CertificatePin.IsValid(new string('0', 64)));
        Assert.Throws<ArgumentException>(() => CertificatePin.Normalize("abc"));
    }

    [Fact]
    public void FixedTimeEqualsComparesContent()
    {
        Assert.True(CertificatePin.FixedTimeEquals(new string('a', 64), new string('a', 64)));
        Assert.False(CertificatePin.FixedTimeEquals(new string('a', 64), new string('b', 64)));
        Assert.False(CertificatePin.FixedTimeEquals(new string('a', 64), "a"));
    }

    [Fact]
    public void DisplayMatchesTheServerFormat() =>
        Assert.Equal("ABCD 0123", CertificatePin.Display("abcd0123"));
}
