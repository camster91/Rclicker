using System.Buffers.Text;
using RClicker.Sessions;

namespace RClicker.Tests.Sessions;

public class SessionTokenTests
{
    [Fact]
    public void Generate_ProducesAtLeast128BitsEncodedAsUrlSafeText()
    {
        var token = SessionToken.Generate();

        Assert.Equal(SessionToken.TextLength, token.Length);
        Assert.True(SessionToken.IsWellFormed(token));
        var bytes = Base64Url.DecodeFromChars(token);
        Assert.Equal(SessionToken.ByteLength, bytes.Length);
        Assert.True(bytes.Length * 8 >= 128);
        Assert.Equal(token, Uri.EscapeDataString(token)); // Safe in a URL without escaping.
    }

    [Fact]
    public void Generate_IsUniqueAndUnpredictableAcrossManyTokens()
    {
        const int count = 2000;
        var tokens = Enumerable.Range(0, count).Select(_ => SessionToken.Generate()).ToList();

        Assert.Equal(count, tokens.Distinct(StringComparer.Ordinal).Count());

        // Crude randomness sanity check: across 2000 * 256 bits, roughly half the bits are set.
        long ones = tokens.Sum(t => Base64Url.DecodeFromChars(t).Sum(b => (long)System.Numerics.BitOperations.PopCount(b)));
        double ratio = ones / (count * 256.0);
        Assert.InRange(ratio, 0.48, 0.52);

        // No shared prefix that would hint at a counter or timestamp.
        Assert.True(tokens.Select(t => t[..6]).Distinct(StringComparer.Ordinal).Count() > count * 0.99);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 42 chars
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")] // '+' is not base64url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    public void IsWellFormed_RejectsBadShapes(string? token) => Assert.False(SessionToken.IsWellFormed(token));

    [Fact]
    public void Redact_NeverRevealsTheWholeToken()
    {
        var token = SessionToken.Generate();
        var redacted = SessionToken.Redact(token);

        Assert.DoesNotContain(token, redacted, StringComparison.Ordinal);
        Assert.True(redacted.Length <= 5);
        Assert.Equal("(none)", SessionToken.Redact(null));
    }
}
