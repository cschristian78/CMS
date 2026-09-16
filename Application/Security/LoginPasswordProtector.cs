using Microsoft.AspNetCore.Identity;

namespace ClientManagementSystem.Security;

/// <summary>
/// One-way password hashing for <c>dbo.tUser.LoginPw</c> using ASP.NET Core Identity's
/// PBKDF2 PasswordHasher. Hashes are not reversible — SQL access cannot recover the plaintext.
/// </summary>
public static class LoginPasswordProtector
{
    public const string UnsetSentinel = "none";
    public const int MinPasswordLength = 4;
    public const int StoredHashMaxLength = 500;

    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object HashUser = new();

    public static bool IsUnset(string? stored)
        => string.IsNullOrWhiteSpace(stored)
           || string.Equals(stored.Trim(), UnsetSentinel, StringComparison.OrdinalIgnoreCase);

    /// <summary>Identity PasswordHasher payloads are Base64 and typically start with <c>AQAAAA</c>.</summary>
    public static bool IsHashed(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || IsUnset(stored))
        {
            return false;
        }

        var value = stored.Trim();
        return value.StartsWith("AQAAAA", StringComparison.Ordinal);
    }

    public static string Hash(string plaintextPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextPassword);
        return Hasher.HashPassword(HashUser, plaintextPassword);
    }

    public static bool Verify(string stored, string plaintextPassword, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(plaintextPassword) || string.IsNullOrWhiteSpace(stored) || IsUnset(stored))
        {
            return false;
        }

        var trimmed = stored.Trim();
        if (IsHashed(trimmed))
        {
            var result = Hasher.VerifyHashedPassword(HashUser, trimmed, plaintextPassword);
            needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
            return result is PasswordVerificationResult.Success
                or PasswordVerificationResult.SuccessRehashNeeded;
        }

        // Legacy plaintext row — accept once, then caller should re-hash on success.
        if (string.Equals(plaintextPassword, trimmed, StringComparison.Ordinal))
        {
            needsRehash = true;
            return true;
        }

        return false;
    }
}
