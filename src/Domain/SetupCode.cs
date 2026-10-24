using System.Security.Cryptography;
using System.Text;

namespace Portfolio.Domain;

/// <summary>
/// The first-run setup code (slice-03.md step 1, C3-3, D2): how a presented code is compared
/// with the configured one, and the hash that makes it single-use across an owner reset.
/// </summary>
/// <remarks>
/// The AppHost generates the code: 29 characters from Aspire's 23-letter lower-case alphabet,
/// which already leaves out i, l and o, so about 131 bits and nothing to misread (gate GT-W).
/// A person may paste it with stray spaces, hyphens or capitals, so both sides are normalised
/// before comparing. The comparison is in fixed time.
/// </remarks>
public static class SetupCode
{
    /// <summary>The configured code's length: 29 × log2(23) ≈ 131 bits.</summary>
    public const int Length = 29;

    /// <summary>Lower-cases, and drops whitespace and hyphens.</summary>
    public static string Normalise(string code)
    {
        var sb = new StringBuilder(code.Length);
        foreach (var c in code)
            if (!char.IsWhiteSpace(c) && c != '-')
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// True when <paramref name="presented"/> is the configured code. Fixed-time over the
    /// normalised bytes; an empty configured code never matches.
    /// </summary>
    public static bool Matches(string configured, string presented)
    {
        var expected = Encoding.UTF8.GetBytes(Normalise(configured));
        var actual = Encoding.UTF8.GetBytes(Normalise(presented));
        return expected.Length > 0 && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>
    /// Lower-case hex SHA-256 of the normalised code: what <c>used_setup_code</c> keeps. A
    /// random code of ~131 bits needs no salt; the hash only has to identify a code already
    /// spent, never to protect a weak one.
    /// </summary>
    public static string Sha256Hex(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(code))));
}
