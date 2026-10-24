namespace Portfolio.Domain;

/// <summary>
/// A setup code that has completed an enrolment. Append-only, and keyed by the code's hash, so
/// a code is refused once it has been used, even after <c>reset-owner</c> clears the user
/// (round 2, SCO-4 and TST-20). Kept outside Identity's rows for exactly that reason.
/// </summary>
public class UsedSetupCode
{
    /// <summary><see cref="SetupCode.Sha256Hex"/> of the code.</summary>
    public required string Sha256 { get; set; }
    public DateTimeOffset UsedAt { get; set; }
}
