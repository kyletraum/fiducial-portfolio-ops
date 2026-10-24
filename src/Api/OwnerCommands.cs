using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portfolio.Domain;
using Portfolio.Infrastructure;

namespace Portfolio.Api;

/// <summary>
/// slice-03.md step 7: the two recovery commands, run from the host with no browser session:
///
///     docker compose run --rm api revoke-sessions
///     docker compose run --rm api reset-owner
///
/// A service, so the integration tests call it through the app's own services, and the process
/// itself is tested once (round 2 TST-22). Each writes its audit event. Nothing it prints is secret.
/// </summary>
public sealed class OwnerCommands(PortfolioDbContext db, UserManager<IdentityUser> users, IHostEnvironment environment, TimeProvider clock)
{
    public const string RevokeSessions = "revoke-sessions";
    public const string ResetOwner = "reset-owner";

    /// <summary>The mode, when the process was started to run a command instead of the API.</summary>
    public static string? ModeFrom(string[] args) => args is [var mode] && mode is RevokeSessions or ResetOwner ? mode : null;

    public async Task<int> RunAsync(string mode, TextWriter output, CancellationToken ct = default)
    {
        switch (mode)
        {
            case RevokeSessions:
                var revoked = await RevokeSessionsAsync(ct);
                output.WriteLine(revoked == 0 ? "No owner is enrolled; nothing to revoke." : "Every session is revoked. Each browser must sign in again.");
                return 0;
            case ResetOwner:
                var reset = await ResetOwnerAsync(ct);
                output.WriteLine(reset == 0
                    ? "No owner is enrolled; nothing to reset."
                    : "The owner and their passkeys are removed. Enrolment is open again, and needs a setup code that has not been used: redeploy, or set a new SETUP_CODE.");
                return 0;
            default:
                output.WriteLine($"Unknown command '{mode}'.");
                return 2;
        }
    }

    /// <summary>
    /// Rotates every user's security stamp: each session cookie is refused at its next stamp check,
    /// within the validation interval (D4). The answer to a stolen cookie that is still in use.
    /// </summary>
    public async Task<int> RevokeSessionsAsync(CancellationToken ct = default)
    {
        var owners = await db.Users.ToListAsync(ct);
        foreach (var owner in owners)
        {
            Check(await users.UpdateSecurityStampAsync(owner));
            db.AuditEvents.Add(Audit(AuditEventKind.SessionsRevoked, owner.UserName!));
        }
        await db.SaveChangesAsync(ct);
        return owners.Count;
    }

    /// <summary>
    /// Removes the owner, and with them their passkeys, so enrolment is open again. audit_event keeps
    /// its rows (the actor is a value, C3-32), and used_setup_code keeps every spent code, so the old
    /// code cannot re-enrol: the next owner needs a fresh one (SCO-4).
    /// </summary>
    public async Task<int> ResetOwnerAsync(CancellationToken ct = default)
    {
        var owners = await db.Users.ToListAsync(ct);
        foreach (var owner in owners)
        {
            Check(await users.DeleteAsync(owner));
            db.AuditEvents.Add(Audit(AuditEventKind.OwnerReset, owner.UserName!));
        }
        await db.SaveChangesAsync(ct);
        return owners.Count;
    }

    AuditEvent Audit(AuditEventKind kind, string actor) => new()
    {
        Kind = kind,
        Actor = actor,
        Subject = "host command",
        Environment = environment.EnvironmentName,
        OccurredAt = clock.GetUtcNow(),
    };

    static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
