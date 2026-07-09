using System.Security.Cryptography;
using System.Text.Json;

namespace ClaudeUsageTray.Auth;

/// <summary>
/// Persists the OAuth token this app obtained through its own login flow, encrypted
/// with DPAPI for the current Windows user. This is our own app's data directory -
/// distinct from, and never reading from, Claude Desktop's or Claude Code's storage.
/// </summary>
public static class TokenStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeUsageTray",
        "oauth-token.dat");

    public static TokenSet? Load()
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<TokenSet>(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Save(TokenSet tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(tokens);
        var encrypted = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public static void Clear()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
