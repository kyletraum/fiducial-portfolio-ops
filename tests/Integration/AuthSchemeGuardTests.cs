using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md step 2, round 2 TST-3: the guard against <see cref="TestAuth"/> leaking. The app's own
/// composition, unmodified, in Production and in Development, has exactly Identity's schemes and
/// Identity's defaults. A test scheme, a bypass switched on by configuration, or a second default
/// would each change what is asserted here. (No database: building the host does not touch one.)
/// </summary>
public class AuthSchemeGuardTests
{
    static readonly string[] IdentitySchemes =
    [
        IdentityConstants.ApplicationScheme,
        IdentityConstants.ExternalScheme,
        IdentityConstants.TwoFactorRememberMeScheme,
        IdentityConstants.TwoFactorUserIdScheme,
    ];

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task The_app_authenticates_with_Identitys_schemes_and_nothing_else(string environment)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment(environment).UseSetting("ConnectionStrings:portfolio", "Host=never-contacted"));
        var schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Equal(IdentitySchemes.Order(), (await schemes.GetAllSchemesAsync()).Select(s => s.Name).Order());
        Assert.Equal(IdentityConstants.ApplicationScheme, (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);
        Assert.Equal(IdentityConstants.ApplicationScheme, (await schemes.GetDefaultChallengeSchemeAsync())?.Name);
    }

    [Fact]
    public async Task The_test_scheme_signs_in_only_a_request_that_asks()
    {
        // The other half: with TestAuth applied, an anonymous request is still anonymous.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production").UseSetting("ConnectionStrings:portfolio", "Host=never-contacted").WithTestAuth());

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/auth/session", ImportSupport.Ct)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await factory.CreateSignedInClient("someone").GetAsync("/api/v1/auth/session", ImportSupport.Ct)).StatusCode);
    }
}
