using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Portfolio.Integration;

/// <summary>
/// A WebAuthn authenticator in C# (slice-03.md step 2, round 2 R2), so CI runs the real passkey
/// ceremony against Identity without a browser: an ES256 key, a "none" attestation, and assertions
/// signed over authenticatorData and the client data's hash, as a platform authenticator would.
/// </summary>
/// <remarks>
/// Built from Identity's own parsers at v10.0.13 (src/Identity/Core/src/Passkeys): the credential
/// JSON is <c>PublicKeyCredential.toJSON()</c>'s shape; the COSE key is read in CTAP2 canonical
/// order; the signature is DER. The hostile variants are what make the origin check, user
/// verification, the sign counter and malformed input testable.
/// </remarks>
/// <param name="registeredElsewhere">
/// A user handle to present without registering here: a passkey another app (or a refused enrolment,
/// Spike I §3) left on the device.
/// </param>
public sealed class SoftAuthenticator(string origin, byte[]? registeredElsewhere = null)
{
    readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    readonly byte[] credentialId = RandomNumberGenerator.GetBytes(32);
    byte[]? userHandle = registeredElsewhere;
    uint signCount;

    /// <summary>
    /// A user handle no user here has, shaped as Identity's are: the UTF-8 of a GUID string. Random
    /// bytes are usually not valid UTF-8, and Identity then refuses another way, "Passkey not
    /// accepted" rather than "not registered here" (EXECUTED: a CI run, 2026-10-25; the E2E's virtual
    /// authenticator had the same fix in step 4).
    /// </summary>
    public static byte[] ForeignUserHandle() => System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString());

    /// <summary>What a ceremony may be made to do wrong.</summary>
    public sealed record Variant
    {
        public string? Origin { get; init; }
        public bool CrossOrigin { get; init; }
        public bool UserVerified { get; init; } = true;
        /// <summary>Send this counter instead of the next one (a cloned or replayed authenticator).</summary>
        public uint? SignCount { get; init; }
        public bool MalformedCbor { get; init; }
    }

    static readonly Variant Honest = new();

    /// <summary>The response to <c>navigator.credentials.create()</c>, as JSON.</summary>
    public string Register(string creationOptionsJson, Variant? variant = null)
    {
        variant ??= Honest;
        var options = JsonNode.Parse(creationOptionsJson)!;
        userHandle = Base64Url.DecodeFromChars(options["user"]!["id"]!.GetValue<string>());
        var clientData = ClientData("webauthn.create", options["challenge"]!.GetValue<string>(), variant);

        var authData = AuthenticatorData(options["rp"]!["id"]!.GetValue<string>(), variant, attested: true, counter: 0);
        var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString(authData);
        attestation.WriteEndMap();
        var attestationObject = variant.MalformedCbor ? [0xA3, 0x63, 0x66] : attestation.Encode();

        return Credential(new JsonObject
        {
            ["clientDataJSON"] = B64(clientData),
            ["attestationObject"] = B64(attestationObject),
            ["transports"] = new JsonArray("internal"),
        });
    }

    /// <summary>The response to <c>navigator.credentials.get()</c>, as JSON.</summary>
    public string Assert(string requestOptionsJson, Variant? variant = null)
    {
        variant ??= Honest;
        var options = JsonNode.Parse(requestOptionsJson)!;
        var clientData = ClientData("webauthn.get", options["challenge"]!.GetValue<string>(), variant);
        var counter = variant.SignCount ?? ++signCount;
        var authData = AuthenticatorData(options["rpId"]!.GetValue<string>(), variant, attested: false, counter);
        var signature = key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return Credential(new JsonObject
        {
            ["clientDataJSON"] = B64(clientData),
            ["authenticatorData"] = B64(variant.MalformedCbor ? authData[..10] : authData),
            ["signature"] = B64(signature),
            ["userHandle"] = userHandle is null ? null : B64(userHandle),
        });
    }

    byte[] ClientData(string type, string challenge, Variant variant) =>
        JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["type"] = type,
            ["challenge"] = challenge,
            ["origin"] = variant.Origin ?? origin,
            ["crossOrigin"] = variant.CrossOrigin,
        });

    byte[] AuthenticatorData(string rpId, Variant variant, bool attested, uint counter)
    {
        const byte UserPresent = 0x01, UserVerified = 0x04, AttestedData = 0x40;
        var flags = (byte)(UserPresent | (variant.UserVerified ? UserVerified : 0) | (attested ? AttestedData : 0));
        var data = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(rpId))) { flags };
        data.AddRange(BigEndian(counter));
        if (attested)
        {
            data.AddRange(new byte[16]); // AAGUID: none
            data.AddRange(BigEndian((ushort)credentialId.Length));
            data.AddRange(credentialId);
            data.AddRange(CosePublicKey());
        }
        return [.. data];
    }

    /// <summary>EC2 / ES256 / P-256, keys in CTAP2 canonical order: 1, 3, -1, -2, -3.</summary>
    byte[] CosePublicKey()
    {
        var point = key.ExportParameters(includePrivateParameters: false).Q;
        var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
        cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2);   // kty: EC2
        cose.WriteInt32(3); cose.WriteInt32(-7);  // alg: ES256
        cose.WriteInt32(-1); cose.WriteInt32(1);  // crv: P-256
        cose.WriteInt32(-2); cose.WriteByteString(point.X!);
        cose.WriteInt32(-3); cose.WriteByteString(point.Y!);
        cose.WriteEndMap();
        return cose.Encode();
    }

    string Credential(JsonObject response) => new JsonObject
    {
        ["id"] = B64(credentialId),
        ["rawId"] = B64(credentialId),
        ["type"] = "public-key",
        ["authenticatorAttachment"] = "platform",
        ["clientExtensionResults"] = new JsonObject(),
        ["response"] = response,
    }.ToJsonString();

    static string B64(byte[] bytes) => Base64Url.EncodeToString(bytes);

    static byte[] BigEndian(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); return b; }
    static byte[] BigEndian(ushort value) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, value); return b; }
}
