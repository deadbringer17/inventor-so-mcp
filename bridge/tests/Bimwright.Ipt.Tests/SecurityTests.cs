using Bimwright.Ipt.Shared.Security;
using Bimwright.Ipt.Shared.Transport;

namespace Bimwright.Ipt.Tests;

public sealed class ErrorSanitizerTests
{
    [Theory]
    [InlineData(@"Cannot open C:\Lavori\Progetti Software\pezzo.ipt now", "Cannot open <path> now")]
    [InlineData(@"Cannot open C:\Lavori\Progetti Software\pezzo.ipt: access denied", "Cannot open <path>: access denied")]
    [InlineData(@"Cannot open \\server\share\Cartella Uno\pezzo.ipt now", "Cannot open <path> now")]
    [InlineData(@"Cannot open \\server\share\cartella now", "Cannot open <path> now")]
    [InlineData("Cannot open \"C:\\Lavori\\Progetti Software\\pezzo.ipt\" now", "Cannot open <path> now")]
    [InlineData("Cannot open 'C:\\Lavori\\Progetti Software' now", "Cannot open <path> now")]
    [InlineData("Cannot open \"\\\\srv\\share\\a b\\c.ipt\" now", "Cannot open <path> now")]
    [InlineData(@"Cannot open C:\secret now", "Cannot open <path> now")]
    [InlineData(@"Cannot open C:\Lavori v1.2\pezzo.ipt now", "Cannot open <path> now")]
    public void MasksPaths(string input, string expected)
        => Assert.Equal(expected, ErrorSanitizer.Sanitize(input));

    [Fact]
    public void NoPathFragmentLeaks()
    {
        var s = ErrorSanitizer.Sanitize(@"fail C:\Lavori\Progetti Software\pezzo.ipt end");
        Assert.DoesNotContain("Software", s);
        Assert.DoesNotContain("pezzo", s);
    }

    [Fact]
    public void CollapsesCrLf()
        => Assert.Equal("a b c", ErrorSanitizer.Sanitize("a\r\nb\nc"));

    [Fact]
    public void NullAndExceptionOverloads()
    {
        Assert.Equal("", ErrorSanitizer.Sanitize((string?)null));
        Assert.Equal("boom", ErrorSanitizer.Sanitize(new System.InvalidOperationException("boom")));
    }
}

public sealed class SecretMaskerTests
{
    [Fact]
    public void MasksAuthTokenJsonField()
    {
        var s = SecretMasker.Mask("{\"auth_token\": \"abc-short\",\"x\":1}");
        Assert.DoesNotContain("abc-short", s);
        Assert.Contains("***", s);
    }

    [Fact]
    public void MasksLongTokenLikeStrings()
    {
        var token = AuthToken.Generate();
        var s = SecretMasker.Mask("token=" + token + " ok");
        Assert.DoesNotContain(token, s);
        Assert.Contains("ok", s);
    }

    [Fact]
    public void LeavesShortTextAndEmpty()
    {
        Assert.Equal("hello world", SecretMasker.Mask("hello world"));
        Assert.Equal("", SecretMasker.Mask(null));
        Assert.Equal("", SecretMasker.Mask(""));
    }
}

public sealed class AuthTokenTests
{
    [Fact]
    public void GenerateIs64LowercaseHexAndUnique()
    {
        var a = AuthToken.Generate();
        var b = AuthToken.Generate();
        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void VerifyBehaviour()
    {
        var t = AuthToken.Generate();
        Assert.True(AuthToken.Verify(t, t));
        Assert.True(AuthToken.Verify(t, new string(t.ToCharArray())));
        Assert.False(AuthToken.Verify(t, null));
        Assert.False(AuthToken.Verify(t, ""));
        Assert.False(AuthToken.Verify(null, t));
        Assert.False(AuthToken.Verify("", ""));
        Assert.False(AuthToken.Verify(t, t.Substring(1)));
        Assert.False(AuthToken.Verify(t, t + "0"));
        var diff = (t[0] == 'a' ? 'b' : 'a') + t.Substring(1);
        Assert.False(AuthToken.Verify(t, diff));
    }
}
