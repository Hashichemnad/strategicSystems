using System.Text;
using Strategy.Platform.Identity.Infrastructure;

namespace Strategy.UnitTests.Identity;

public class TotpCodeTests
{
    // RFC 6238 appendix B, SHA-1 secret "12345678901234567890"; the RFC's 8-digit codes, last 6 digits.
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(20000000000L, "353130")]
    public void Codes_match_the_rfc_6238_test_vectors(long unixSeconds, string expected) =>
        TotpCode.Compute(RfcKey, TotpCode.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))).ShouldBe(expected);

    [Fact]
    public void Base32_keys_decode_as_authenticator_apps_read_them()
    {
        TotpCode.DecodeBase32("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ").ShouldBe(RfcKey);
        TotpCode.DecodeBase32("gezd gnbv gy3t qojq gezd gnbv gy3t qojq").ShouldBe(RfcKey); // as shown to users
        TotpCode.DecodeBase32("GEZ1").ShouldBeNull(); // '1' is not base32
        TotpCode.DecodeBase32(null).ShouldBeNull();
    }

    [Fact]
    public void The_matching_step_is_found_within_two_steps_either_side_and_not_beyond()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var current = TotpCode.StepAt(now);
        for (var offset = -2; offset <= 2; offset++)
        {
            TotpCode.MatchingStep(RfcKey, TotpCode.Compute(RfcKey, current + offset), now).ShouldBe(current + offset);
        }

        TotpCode.MatchingStep(RfcKey, TotpCode.Compute(RfcKey, current - 3), now).ShouldBeNull();
        TotpCode.MatchingStep(RfcKey, TotpCode.Compute(RfcKey, current + 3), now).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("１２３４５６")] // full-width digits are not accepted
    public void Malformed_codes_never_match(string code) =>
        TotpCode.MatchingStep(RfcKey, code, DateTimeOffset.UtcNow).ShouldBeNull();
}
