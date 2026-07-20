using System.Text.Json.Serialization;

namespace AIQuota.Auth;

public sealed record TokenSet(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("scope")] string? Scope)
{
    public bool IsExpiredOrExpiringSoon =>
        ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow.AddSeconds(60);
}

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn,
    [property: JsonPropertyName("scope")] string? Scope);
