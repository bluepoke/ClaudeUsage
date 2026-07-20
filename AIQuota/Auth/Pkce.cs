using System.Security.Cryptography;

namespace AIQuota.Auth;

public sealed record PkcePair(string Verifier, string Challenge);

public static class Pkce
{
    public static PkcePair Create()
    {
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        return new PkcePair(verifier, challenge);
    }

    public static string CreateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .Replace('+', '-')
        .Replace('/', '_')
        .Replace("=", "");
}
