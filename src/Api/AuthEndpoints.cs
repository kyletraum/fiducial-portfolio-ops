using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portfolio.Domain;
using Portfolio.Infrastructure;

namespace Portfolio.Api;

/// <summary>
/// Slice 03 step 2: first-run enrolment by setup code, passkey sign-in, sign-out. Every route is
/// under /api/v1/auth. The anonymous ones carry AllowAnonymous now, so step 4's FallbackPolicy
/// leaves them open; the two that need a session say so.
/// </summary>
static class AuthEndpoints
{
    public static void MapAuthApi(this RouteGroupBuilder v1)
    {
        var auth = v1.MapGroup("/auth").WithTags("Authentication");

        auth.MapGet("/state", GetState).WithName("GetAuthState").AllowAnonymous();

        // The anonymous ceremonies: no token can exist before sign-in, so they are exempt from the
        // CSRF filter (R1), and are covered instead by layers 1 and 2, ValidateOrigin, the Strict
        // challenge cookie and the sign-in limiter.
        var ceremonies = auth.MapGroup("").AllowAnonymous()
            .WithMetadata(new CsrfTokens.Exempt())
            .RequireRateLimiting(SignInRateLimit.Policy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        ceremonies.MapPost("/enrolment/options", BeginEnrolment).WithName("BeginEnrolment")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
        ceremonies.MapPost("/enrolment", CompleteEnrolment).WithName("CompleteEnrolment")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
        ceremonies.MapPost("/sign-in/options", BeginSignIn).WithName("BeginSignIn")
            .Produces(StatusCodes.Status200OK, contentType: "application/json");
        ceremonies.MapPost("/sign-in", CompleteSignIn).WithName("CompleteSignIn")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapGet("/csrf-token", CsrfTokens.Issue).WithName("GetCsrfToken").RequireAuthorization();
        auth.MapPost("/sign-out", SignOut).WithName("SignOut").RequireAuthorization().RequireCsrfToken()
            .ProducesProblem(StatusCodes.Status403Forbidden);
        auth.MapGet("/session", GetSession).WithName("GetSession").RequireAuthorization();
    }

    static async Task<Ok<AuthStateResponse>> GetState(PortfolioDbContext db, IHostEnvironment env, CancellationToken ct) =>
        TypedResults.Ok(new AuthStateResponse(await db.Users.AnyAsync(ct), env.EnvironmentName));

    static async Task<Results<ContentHttpResult, ProblemHttpResult>> BeginEnrolment(
        BeginEnrolmentRequest request, PortfolioDbContext db, SignInManager<IdentityUser> signIn, IConfiguration config, CancellationToken ct)
    {
        if (await EnrolmentRefusalAsync(request.SetupCode, db, config, ct) is { } refused)
            return refused;

        // C3-3: nothing exists before the ceremony succeeds. The options are made for a user who
        // does not exist yet; CompleteEnrolment creates them with the passkey, in one transaction.
        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            Name = request.UserName,
            DisplayName = request.UserName,
        });
        return TypedResults.Text(options, "application/json");
    }

    static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> CompleteEnrolment(
        CompleteEnrolmentRequest request, PortfolioDbContext db, SignInManager<IdentityUser> signIn,
        UserManager<IdentityUser> users, IConfiguration config, HttpContext http, CancellationToken ct)
    {
        if (await EnrolmentRefusalAsync(request.SetupCode, db, config, ct) is { } refused)
            return refused;

        PasskeyAttestationResult attested;
        try
        {
            attested = await signIn.PerformPasskeyAttestationAsync(request.Credential.GetRawText());
        }
        catch (InvalidOperationException)
        {
            // The challenge cookie is missing, expired or already spent (SRC-3): the framework
            // throws, which would otherwise be a 500.
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: ProblemTitles.NoCeremony);
        }
        if (!attested.Succeeded)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: ProblemTitles.PasskeyRefused, detail: attested.Failure.Message);

        var user = new IdentityUser { Id = attested.UserEntity.Id, UserName = attested.UserEntity.Name };
        var spent = SetupCode.Sha256Hex(request.SetupCode);
        var outcome = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // One enrolment at a time, and the checks repeated inside the transaction: two concurrent
            // finishes cannot both create an owner (SRC-18).
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('portfolio-enrolment'))", ct);
            if (await db.Users.AnyAsync(ct))
                return ProblemTitles.AlreadyEnrolled;
            if (await db.UsedSetupCodes.AnyAsync(c => c.Sha256 == spent, ct))
                return ProblemTitles.SetupCodeRefused;

            var created = await users.CreateAsync(user);
            var stored = created.Succeeded ? await users.AddOrUpdatePasskeyAsync(user, attested.Passkey) : created;
            if (!stored.Succeeded)
                return string.Join("; ", stored.Errors.Select(e => e.Description));

            db.UsedSetupCodes.Add(new UsedSetupCode { Sha256 = spent });
            db.AuditEvents.Add(Audit(http, AuditEventKind.Enrolment, user.UserName!));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return null;
        });
        switch (outcome)
        {
            case null: break;
            case ProblemTitles.AlreadyEnrolled: return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: ProblemTitles.AlreadyEnrolled);
            case ProblemTitles.SetupCodeRefused: return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: ProblemTitles.SetupCodeRefused);
            default: return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Enrolment refused", detail: outcome);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        return TypedResults.Ok(new SessionResponse(user.UserName!));
    }

    static async Task<ContentHttpResult> BeginSignIn(SignInManager<IdentityUser> signIn) =>
        TypedResults.Text(await signIn.MakePasskeyRequestOptionsAsync(user: null), "application/json");

    static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> CompleteSignIn(
        JsonElement credential, SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
        PortfolioDbContext db, HttpContext http, CancellationToken ct)
    {
        // The assertion, then the sign-in, rather than PasskeySignInAsync: that returns a bare
        // Failed, and the page needs to tell "not registered here" from other refusals (Spike I §3).
        PasskeyAssertionResult<IdentityUser> asserted;
        try
        {
            asserted = await signIn.PerformPasskeyAssertionAsync(credential.GetRawText());
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: ProblemTitles.NoCeremony);
        }
        if (!asserted.Succeeded)
        {
            db.AuditEvents.Add(Audit(http, AuditEventKind.SignInFailed, AuditEvent.UnknownActor));
            await db.SaveChangesAsync(ct);
            // Identity's CredentialDoesNotBelongToUser: the passkey, or its user, is unknown here
            // [SOURCE@dotnet/aspnetcore@v10.0.13 src/Identity/Core/src/PasskeyHandler.cs:404-468].
            var unknown = asserted.Failure.Message.Contains("does not belong to the specified user", StringComparison.Ordinal);
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: unknown ? ProblemTitles.NotRegisteredHere : ProblemTitles.PasskeyRefused, detail: asserted.Failure.Message);
        }

        // As PasskeySignInAsync does: store the new sign count, then sign in.
        await users.AddOrUpdatePasskeyAsync(asserted.User, asserted.Passkey);
        await signIn.SignInAsync(asserted.User, isPersistent: true);
        db.AuditEvents.Add(Audit(http, AuditEventKind.SignInSucceeded, asserted.User.UserName!));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new SessionResponse(asserted.User.UserName!));
    }

    static async Task<NoContent> SignOut(
        SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, PortfolioDbContext db, HttpContext http, CancellationToken ct)
    {
        // SignOutAsync deletes this browser's cookie only. Rotating the stamp refuses every copy of
        // it at its next check, at most one validation interval away (D4, SRC-16).
        if (await users.GetUserAsync(http.User) is { } user)
        {
            await users.UpdateSecurityStampAsync(user);
            db.AuditEvents.Add(Audit(http, AuditEventKind.SignOut, user.UserName!));
            await db.SaveChangesAsync(ct);
        }
        await signIn.SignOutAsync();
        // The token belongs to this user; a fresh one comes with the next sign-in (C3-36).
        http.Response.Cookies.Delete(CsrfTokens.CookieName);
        return TypedResults.NoContent();
    }

    static Ok<SessionResponse> GetSession(HttpContext http) =>
        TypedResults.Ok(new SessionResponse(http.User.Identity!.Name!));

    /// <summary>
    /// Null when enrolment may proceed: no owner yet, a setup code is configured, the presented one
    /// matches it, and it has not been spent. A spent code reads as any wrong one.
    /// </summary>
    static async Task<ProblemHttpResult?> EnrolmentRefusalAsync(string presented, PortfolioDbContext db, IConfiguration config, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: ProblemTitles.AlreadyEnrolled);
        var configured = config["Enrolment:SetupCode"] ?? "";
        if (!SetupCode.Matches(configured, presented))
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: ProblemTitles.SetupCodeRefused);
        var spent = SetupCode.Sha256Hex(presented);
        if (await db.UsedSetupCodes.AnyAsync(c => c.Sha256 == spent, ct))
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: ProblemTitles.SetupCodeRefused);
        return null;
    }

    static AuditEvent Audit(HttpContext http, AuditEventKind kind, string actor) => new()
    {
        Kind = kind,
        Actor = actor,
        Environment = http.RequestServices.GetRequiredService<IHostEnvironment>().EnvironmentName,
        ConnectionAddress = http.Connection.RemoteIpAddress?.ToString(),
        OccurredAt = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
    };
}
