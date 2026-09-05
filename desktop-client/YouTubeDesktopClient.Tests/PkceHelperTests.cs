using Xunit;
using YouTubeDesktopClient.Auth;

public class PkceHelperTests
{
    [Fact]
    public void GenerateCodeVerifier_ProducesUrlSafeStringOfSufficientLength()
    {
        var verifier = PkceHelper.GenerateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
    }

    [Fact]
    public void GenerateCodeVerifier_ProducesDifferentValuesEachCall()
    {
        Assert.NotEqual(PkceHelper.GenerateCodeVerifier(), PkceHelper.GenerateCodeVerifier());
    }

    [Fact]
    public void DeriveCodeChallenge_IsDeterministicForSameVerifier()
    {
        var verifier = "test-verifier-1234567890123456789012345";

        Assert.Equal(PkceHelper.DeriveCodeChallenge(verifier), PkceHelper.DeriveCodeChallenge(verifier));
    }

    [Fact]
    public void DeriveCodeChallenge_MatchesKnownRfc7636Example()
    {
        // From RFC 7636 Appendix B.
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var expected = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        Assert.Equal(expected, PkceHelper.DeriveCodeChallenge(verifier));
    }
}
