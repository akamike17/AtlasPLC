using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AtlasSoftPlc.Targets;

public sealed record DeploymentConfirmationContext(
    string User,
    string SessionId,
    Guid ProjectId,
    string ProjectHash,
    TargetIdentity Target,
    Guid Nonce,
    DateTimeOffset ExpiresUtc);

public interface IDeploymentConfirmationVerifier
{
    bool Verify(string token, DeploymentConfirmationContext expected);
}

/// <summary>Verificador HMAC para el flujo de confirmación; no ejecuta ningún deploy.</summary>
public sealed class HmacDeploymentConfirmationVerifier : IDeploymentConfirmationVerifier
{
    private readonly byte[] _key;
    public HmacDeploymentConfirmationVerifier(byte[] key) => _key = key is { Length: > 0 } ? key.ToArray() : throw new ArgumentException("La clave no puede estar vacía.", nameof(key));

    public bool Verify(string token, DeploymentConfirmationContext expected)
    {
        try
        {
            var parts = token.Split('.', 2);
            if (parts.Length != 2) return false;
            var payloadBytes = Base64Url.Decode(parts[0]);
            var signature = Base64Url.Decode(parts[1]);
            using var hmac = new HMACSHA256(_key);
            if (!CryptographicOperations.FixedTimeEquals(signature, hmac.ComputeHash(payloadBytes))) return false;
            var payload = JsonSerializer.Deserialize<ConfirmationPayload>(payloadBytes);
            return payload is not null &&
                string.Equals(payload.User, expected.User, StringComparison.Ordinal) &&
                string.Equals(payload.SessionId, expected.SessionId, StringComparison.Ordinal) &&
                payload.ProjectId == expected.ProjectId &&
                string.Equals(payload.ProjectHash, expected.ProjectHash, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(payload.Manufacturer, expected.Target.Manufacturer, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(payload.Family, expected.Target.Family, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(payload.Model, expected.Target.Model, StringComparison.OrdinalIgnoreCase) &&
                payload.Nonce == expected.Nonce &&
                payload.ExpiresUtc == expected.ExpiresUtc;
        }
        catch (FormatException) { return false; }
        catch (JsonException) { return false; }
    }

    private sealed record ConfirmationPayload(string User, string SessionId, Guid ProjectId, string ProjectHash, string Manufacturer, string Family, string Model, Guid Nonce, DateTimeOffset ExpiresUtc);
    public string CreateForTests(DeploymentConfirmationContext context)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ConfirmationPayload(context.User, context.SessionId, context.ProjectId, context.ProjectHash, context.Target.Manufacturer, context.Target.Family, context.Target.Model, context.Nonce, context.ExpiresUtc));
        using var hmac = new HMACSHA256(_key);
        return $"{Base64Url.Encode(payload)}.{Base64Url.Encode(hmac.ComputeHash(payload))}";
    }
    private static class Base64Url
    {
        public static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        public static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    }
}
