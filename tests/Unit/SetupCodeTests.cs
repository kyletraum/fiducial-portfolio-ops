using Portfolio.Domain;

namespace Portfolio.Unit;

public class SetupCodeTests
{
    const string Code = "abcdefghjkmnpqrstuvwxyzabcdef"; // 29 characters from the generator's alphabet

    [Theory]
    [InlineData("abcdefghjkmnpqrstuvwxyzabcdef")]
    [InlineData("ABCDE-FGHJK-MNPQR-STUVW-XYZAB-CDEF")]
    [InlineData("  abcde fghjk mnpqr stuvw xyzab cdef \n")]
    public void A_pasted_code_matches_however_it_is_spaced_or_cased(string presented) =>
        Assert.True(SetupCode.Matches(Code, presented));

    [Theory]
    [InlineData("abcdefghjkmnpqrstuvwxyzabcdeg")] // one character different
    [InlineData("abcdefghjkmnpqrstuvwxyzabcde")]  // one short
    [InlineData("abcdefghjkmnpqrstuvwxyzabcdeff")] // one long
    [InlineData("")]
    public void Anything_else_does_not(string presented) =>
        Assert.False(SetupCode.Matches(Code, presented));

    [Theory]
    [InlineData("")]
    [InlineData(" - ")]
    public void An_unset_configured_code_matches_nothing_not_even_itself(string configured) =>
        Assert.False(SetupCode.Matches(configured, configured));

    [Fact]
    public void The_hash_is_of_the_normalised_code_in_lower_case_hex()
    {
        Assert.Equal(SetupCode.Sha256Hex(Code), SetupCode.Sha256Hex("ABCDE-FGHJK-MNPQR-STUVW-XYZAB-CDEF"));
        Assert.Matches("^[0-9a-f]{64}$", SetupCode.Sha256Hex(Code));
        Assert.NotEqual(SetupCode.Sha256Hex(Code), SetupCode.Sha256Hex(Code[..^1] + "g"));
    }

    [Fact]
    public void The_configured_length_carries_at_least_128_bits()
    {
        // Aspire's lower-case alphabet: 23 letters (no i, l or o). GT-W.
        Assert.True(SetupCode.Length * Math.Log2(23) >= 128);
    }
}
