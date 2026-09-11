namespace AtlasSoftPlc.Web.Auth;

/// <summary>Cuenta de usuario persistida con hash Argon2id y roles.</summary>
public sealed record UserAccount(
    string Username,
    string PasswordHash,
    string Role,
    string? DisplayName = null);