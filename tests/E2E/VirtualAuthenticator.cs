using System.Text.Json;
using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// Chromium's own WebAuthn test hook (CDP <c>WebAuthn</c> domain), as Spikes H and I drove it: the
/// page's navigator.credentials calls are answered by a software authenticator. Chromium only;
/// Firefox has none (C3-49).
/// </summary>
public sealed class VirtualAuthenticator
{
    readonly ICDPSession cdp;
    readonly string id;

    VirtualAuthenticator(ICDPSession cdp, string id) { this.cdp = cdp; this.id = id; }

    /// <param name="presence">
    /// False leaves a ceremony waiting until <see cref="SetPresenceAsync"/>, so a test can see the
    /// page while the browser has control.
    /// </param>
    public static async Task<VirtualAuthenticator> AttachAsync(IBrowserContext context, IPage page, bool presence = true)
    {
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("WebAuthn.enable");
        var added = await cdp.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
        {
            ["options"] = new Dictionary<string, object>
            {
                ["protocol"] = "ctap2", ["transport"] = "internal",
                ["hasResidentKey"] = true, ["hasUserVerification"] = true, ["isUserVerified"] = true,
                ["automaticPresenceSimulation"] = presence,
            },
        });
        return new VirtualAuthenticator(cdp, added!.Value.GetProperty("authenticatorId").GetString()!);
    }

    /// <summary>The one credential this authenticator holds, private key included.</summary>
    public async Task<JsonElement> CredentialAsync()
    {
        var result = await cdp.SendAsync("WebAuthn.getCredentials", new Dictionary<string, object> { ["authenticatorId"] = id });
        return result!.Value.GetProperty("credentials")[0].Clone();
    }

    /// <summary>A copy of a credential, starting at <paramref name="signCount"/>.</summary>
    public Task AddAsync(JsonElement credential, int signCount) =>
        cdp.SendAsync("WebAuthn.addCredential", new Dictionary<string, object>
        {
            ["authenticatorId"] = id,
            ["credential"] = new Dictionary<string, object>
            {
                ["credentialId"] = credential.GetProperty("credentialId").GetString()!,
                ["isResidentCredential"] = true,
                ["rpId"] = credential.GetProperty("rpId").GetString()!,
                ["privateKey"] = credential.GetProperty("privateKey").GetString()!,
                ["userHandle"] = credential.GetProperty("userHandle").GetString()!,
                ["signCount"] = signCount,
            },
        });

    /// <summary>A credential no app here registered: a passkey another app left on the device.</summary>
    public Task AddStrangerAsync() =>
        cdp.SendAsync("WebAuthn.addCredential", new Dictionary<string, object>
        {
            ["authenticatorId"] = id,
            ["credential"] = new Dictionary<string, object>
            {
                ["credentialId"] = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                ["isResidentCredential"] = true,
                ["rpId"] = "localhost",
                ["privateKey"] = Convert.ToBase64String(System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKey()),
                // Shaped as Identity's are, the UTF-8 of a GUID string. Random bytes are usually not valid
                // UTF-8, and Identity then fails the lookup another way (EXECUTED: a flaky test).
                ["userHandle"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                ["signCount"] = 0,
            },
        });

    public Task SetPresenceAsync(bool presence) =>
        cdp.SendAsync("WebAuthn.setAutomaticPresenceSimulation", new Dictionary<string, object> { ["authenticatorId"] = id, ["enabled"] = presence });
}
