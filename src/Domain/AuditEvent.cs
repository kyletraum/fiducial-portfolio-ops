namespace Portfolio.Domain;

/// <summary>
/// security.md "Audit": one authentication event (slice-03.md step 1). APPEND-ONLY: a trigger
/// refuses UPDATE, DELETE and TRUNCATE, as on <see cref="IngestRun"/>. That binds the
/// application's code paths, not the connecting role, which can still remove the trigger
/// (slice-03.md D6, gate G-7).
/// </summary>
/// <remarks>
/// <see cref="Actor"/> is a value, never a foreign key into Identity's user table, so
/// <c>reset-owner</c> can clear the user without rewriting history (C3-32). No column ever
/// holds a secret: not the setup code, not a passkey assertion, not a cookie or a token.
/// </remarks>
public class AuditEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public AuditEventKind Kind { get; set; }
    /// <summary>The user name the event is about, or <see cref="UnknownActor"/>.</summary>
    public required string Actor { get; set; }
    /// <summary>What was acted on, where the kind has a subject; null where it does not.</summary>
    public string? Subject { get; set; }
    /// <summary>The environment the event happened in, e.g. "Development".</summary>
    public required string Environment { get; set; }
    /// <summary>The connection's remote address as the API saw it.</summary>
    public string? ConnectionAddress { get; set; }

    /// <summary>A failed sign-in whose passkey names no user here.</summary>
    public const string UnknownActor = "unknown";
}

/// <summary>The six authentication event kinds slice 03 records (step 5).</summary>
public enum AuditEventKind
{
    Enrolment,
    SignInSucceeded,
    SignInFailed,
    SignOut,
    SessionsRevoked,
    OwnerReset,
}
