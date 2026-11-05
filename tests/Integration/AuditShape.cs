using Portfolio.Domain;

namespace Portfolio.Integration;

/// <summary>
/// security.md "Audit": every entry carries actor, time, subject and environment. The audit tests
/// count a delta per kind; this checks the row each one added, for all six kinds (the slice-03
/// assessment, DoD 7's note: a row's shape had been asserted for one kind only).
/// </summary>
static class AuditShape
{
    /// <summary>A ceremony, over HTTP: the caller's address, and no subject.</summary>
    public static void Ceremony(AuditEvent e, AuditEventKind kind, string actor, DateTimeOffset from, DateTimeOffset to)
    {
        Common(e, kind, actor, from, to);
        Assert.Null(e.Subject);
        Assert.Equal(LoopbackConnection.Address.ToString(), e.ConnectionAddress);
    }

    /// <summary>A host command: no connection to record, and a subject that says where it came from.</summary>
    public static void HostCommand(AuditEvent e, AuditEventKind kind, string actor, DateTimeOffset from, DateTimeOffset to)
    {
        Common(e, kind, actor, from, to);
        Assert.Equal("host command", e.Subject);
        Assert.Null(e.ConnectionAddress);
    }

    static void Common(AuditEvent e, AuditEventKind kind, string actor, DateTimeOffset from, DateTimeOffset to)
    {
        Assert.Equal(kind, e.Kind);
        Assert.Equal(actor, e.Actor);
        Assert.Equal("Production", e.Environment); // AuthFixture runs the Api as Production
        // The app's clock, not the database's default; a millisecond either side for timestamptz's
        // microseconds.
        Assert.InRange(e.OccurredAt, from.AddMilliseconds(-1), to.AddMilliseconds(1));
    }
}
