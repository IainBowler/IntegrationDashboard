using System.Security.Cryptography;
using System.Text;

namespace Api.Services.Auth;

public static class TokenHasher
{
    /// <summary>Lowercase hex SHA-256 — matches the CHAR(64) TokenHash column.</summary>
    public static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
