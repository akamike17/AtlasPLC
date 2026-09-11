using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace AtlasSoftPlc.Web.Auth;

/// <summary>
/// Hashing de contraseñas con Argon2id (ganador del Password Hashing Competition 2015).
/// Parámetros OWASP recomendados (2024): m=19 MiB, t=2, p=1.
/// </summary>
public sealed class PasswordHasher
{
    private const int MemorySizeKiB = 19 * 1024; // 19 MiB
    private const int Iterations = 2;            // t
    private const int DegreeOfParallelism = 1;   // p
    private const int SaltSize = 16;             // 128 bits
    private const int HashSize = 32;             // 256 bits

    /// <summary>Hash de una contraseña nueva. Devuelve el formato codificado: argon2id$t$m$p$salt$hash.</summary>
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        using var argon = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            Iterations = Iterations,
            MemorySize = MemorySizeKiB,
        };
        var hash = argon.GetBytes(HashSize);
        return Encode(Iterations, MemorySizeKiB, DegreeOfParallelism, salt, hash);
    }

    /// <summary>Verifica una contraseña contra un hash codificado (sin excepciones, con comprobación de tiempo aproximadamente constante).</summary>
    public bool Verify(string password, string encoded)
    {
        try
        {
            var (t, m, p, salt, expected) = Decode(encoded);
            using var argon = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                DegreeOfParallelism = p,
                Iterations = t,
                MemorySize = m,
            };
            var actual = argon.GetBytes(expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static string Encode(int t, int m, int p, byte[] salt, byte[] hash) =>
        $"argon2id$t={t}$m={m}$p={p}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";

    private static (int t, int m, int p, byte[] salt, byte[] hash) Decode(string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 6 || parts[0] != "argon2id")
            throw new FormatException("Hash Argon2id malformado");
        int t = int.Parse(parts[1].AsSpan(2));
        int m = int.Parse(parts[2].AsSpan(2));
        int p = int.Parse(parts[3].AsSpan(2));
        var salt = Convert.FromBase64String(parts[4]);
        var hash = Convert.FromBase64String(parts[5]);
        return (t, m, p, salt, hash);
    }
}