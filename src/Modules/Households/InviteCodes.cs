using System.Security.Cryptography;
using System.Text;

namespace FamilyApp.Modules.Households;

/// <summary>
/// Invite codes are 128 random bits, shown once in the invite link and stored only as a SHA-256
/// hash (ADR-027). A plain, fast hash is enough here because the input is random: unlike a
/// password, there is nothing to guess. That also lets us look an invite up by its hash.
/// </summary>
internal static class InviteCodes
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));
}
