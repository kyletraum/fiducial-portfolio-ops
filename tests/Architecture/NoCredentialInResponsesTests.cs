// security.md "Verification" row 1 and FR-3.2: no API response contains a credential. slice-03.md
// step 5 (C3-27, round 2 TST-19): read from the COMMITTED OpenAPI document, here in a required job
// (the Contract job is advisory). RESPONSE schemas only: the enrolment request must carry the setup
// code, and a request-side rule would only invite renaming the field to dodge it.
using System.Text.Json;

namespace Portfolio.Architecture;

public class NoCredentialInResponsesTests
{
    /// <summary>Name stems a credential property would carry, compared case-insensitively.</summary>
    static readonly string[] CredentialStems =
        ["password", "hash", "secret", "token", "credential", "privatekey", "publickey", "securitystamp", "setupcode", "cookie", "apikey"];

    /// <summary>schema.property → why it is not a credential leak.</summary>
    static readonly Dictionary<string, string> Allowed = new()
    {
        // The page's CSRF token: issued to the signed-in user, for that user's own requests (step 3).
        ["CsrfTokenResponse.token"] = "the antiforgery request token is this response's purpose",
    };

    [Fact]
    public void No_response_schema_has_a_credential_named_property()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(OpenApiPath()));
        var (operations, properties) = ResponseProperties(document.RootElement);

        // Not vacuous: the authentication operations are in the document, and the scan saw a real
        // number of properties (an untyped response contributes none).
        Assert.Superset(new HashSet<string> { "GetSession", "GetCsrfToken", "CompleteSignIn", "CompleteEnrolment", "GetAuthState" }, operations);
        Assert.True(properties.Count >= 30, $"only {properties.Count} response properties scanned");

        var flagged = Flagged(properties).ToList();
        Assert.True(flagged.Count == 0, "credential-like response properties: " + string.Join(", ", flagged));
    }

    [Fact]
    public void The_scan_flags_a_credential_property_in_a_response()
    {
        // Seen failing: the same scan, on a document whose response carries a password hash.
        using var document = JsonDocument.Parse("""
            {"paths":{"/x":{"get":{"operationId":"X","responses":{"200":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Leaky"}}}}}}}},
             "components":{"schemas":{"Leaky":{"properties":{"userName":{},"inner":{"$ref":"#/components/schemas/Inner"}}},"Inner":{"properties":{"passwordHash":{}}}}}}
            """);

        var (_, properties) = ResponseProperties(document.RootElement);

        Assert.Equal(["Inner.passwordHash"], Flagged(properties));
    }

    static IEnumerable<string> Flagged(IEnumerable<string> properties) =>
        properties.Where(p => !Allowed.ContainsKey(p))
            .Where(p => CredentialStems.Any(stem => p[(p.IndexOf('.') + 1)..].Replace("_", "").Contains(stem, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Every operation id, and every "Schema.property" reachable from any response.</summary>
    static (HashSet<string> Operations, HashSet<string> Properties) ResponseProperties(JsonElement root)
    {
        var operations = new HashSet<string>();
        var properties = new HashSet<string>();
        var schemas = root.TryGetProperty("components", out var c) && c.TryGetProperty("schemas", out var s) ? s : default;
        var seen = new HashSet<string>();

        void Walk(JsonElement schema, string owner)
        {
            if (schema.ValueKind != JsonValueKind.Object) return;
            if (schema.TryGetProperty("$ref", out var reference))
            {
                var name = reference.GetString()!.Split('/')[^1];
                if (seen.Add(name) && schemas.ValueKind == JsonValueKind.Object && schemas.TryGetProperty(name, out var target))
                    Walk(target, name);
                return;
            }
            if (schema.TryGetProperty("properties", out var props))
                foreach (var p in props.EnumerateObject())
                {
                    properties.Add($"{owner}.{p.Name}");
                    Walk(p.Value, owner);
                }
            foreach (var key in new[] { "items", "additionalProperties" })
                if (schema.TryGetProperty(key, out var inner)) Walk(inner, owner);
            foreach (var key in new[] { "allOf", "anyOf", "oneOf" })
                if (schema.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var item in list.EnumerateArray()) Walk(item, owner);
        }

        foreach (var path in root.GetProperty("paths").EnumerateObject())
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Value.ValueKind != JsonValueKind.Object) continue;
                if (operation.Value.TryGetProperty("operationId", out var id)) operations.Add(id.GetString()!);
                if (!operation.Value.TryGetProperty("responses", out var responses)) continue;
                foreach (var response in responses.EnumerateObject())
                    if (response.Value.TryGetProperty("content", out var content))
                        foreach (var media in content.EnumerateObject())
                            if (media.Value.TryGetProperty("schema", out var schema))
                                Walk(schema, "response");
            }
        return (operations, properties);
    }

    static string OpenApiPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "aspire.config.json")))
                return Path.Combine(dir.FullName, "src", "Api", "openapi", "portfolio-api.json");
        throw new InvalidOperationException("No aspire.config.json above " + AppContext.BaseDirectory);
    }
}
