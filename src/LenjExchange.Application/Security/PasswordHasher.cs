using System.Globalization;
using System.Security.Cryptography;

namespace LenjExchange.Application.Security;

/// <summary>
/// PBKDF2 (Rfc2898, HMAC-SHA256) with a random 16-byte salt. Stored format:
/// <c>PBKDF2-SHA256$iterations$saltBase64$hashBase64</c>, so the iteration count can be raised later
/// without invalidating existing hashes.
/// </summary>
public sealed class PasswordHasher
{
    public const int DefaultIterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Scheme = "PBKDF2-SHA256";

    private readonly int _iterations;

    public PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < 10_000) throw new ArgumentOutOfRangeException(nameof(iterations));
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', Scheme, _iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string stored) =>
        !stored.StartsWith(Scheme + "$" + _iterations.ToString(CultureInfo.InvariantCulture) + "$", StringComparison.Ordinal);
}
