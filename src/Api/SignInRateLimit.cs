using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Portfolio.Api;

/// <summary>
/// slice-03.md step 3, DoD 5: one fixed window over the anonymous authentication endpoints. There is
/// no password to guess (D7) and no lockout (C3-8); this bounds the setup-code checks and the
/// failed-sign-in audit rows any local process can cause (C3-43).
/// </summary>
/// <remarks>
/// A named policy (<c>AddFixedWindowLimiter</c>), not <c>GlobalLimiter</c>, which would throttle every
/// request. It partitions by policy name: one window for every caller, never keyed by the remote
/// address, which on loopback is one value and, with forwarded headers on, whatever the caller claims
/// (C3-7, C3-9). [SOURCE@dotnet/aspnetcore@v10.0.13
/// src/Middleware/RateLimiting/src/RateLimiterOptionsExtensions.cs:56-70]
/// </remarks>
public sealed class SignInRateLimit
{
    public const string Policy = "sign-in";

    /// <summary>
    /// Recorded in slice-03.md step 3. A sign-in or an enrolment is two requests (options, then
    /// finish), so ten a minute is five attempts: room for a person retrying, none for a loop.
    /// </summary>
    public int PermitLimit { get; set; } = 10;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public static void AddSignInRateLimit(IServiceCollection services, IConfiguration config)
    {
        services.Configure<SignInRateLimit>(config.GetSection("RateLimits:SignIn"));
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<SignInRateLimit>>((o, limits) =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests; // the in-box default is 503
            o.AddFixedWindowLimiter(Policy, w =>
            {
                w.PermitLimit = limits.Value.PermitLimit;
                w.Window = limits.Value.Window;
                w.QueueLimit = 0;
            });
            // The response, and nothing logged: no partition key, no body (OPS-7).
            o.OnRejected = async (context, ct) =>
            {
                // The lease's RetryAfter is the window's length, not the time left in it (SRC-14).
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                await TypedResults.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: ProblemTitles.TooManySignInAttempts)
                    .ExecuteAsync(context.HttpContext);
            };
        });
    }
}
