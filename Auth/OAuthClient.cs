using System.Net.Http.Json;
using ClaudeUsageTray.Localization;

namespace ClaudeUsageTray.Auth;

public sealed class OAuthClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public bool IsLoggedIn => TokenStore.Load() is not null;

    /// <summary>
    /// Runs the full PKCE login: opens the system browser to claude.ai's consent
    /// screen and waits for the loopback redirect carrying the authorization code.
    /// </summary>
    public async Task LoginAsync(CancellationToken cancellationToken)
    {
        using var listener = LoopbackListener.BindFirstAvailable(OAuthConfig.LoopbackPorts);
        var pkce = Pkce.Create();
        var state = Pkce.CreateState();
        var redirectUri = OAuthConfig.RedirectUri(listener.Port);

        var authorizeUrl = BuildAuthorizeUrl(redirectUri, pkce.Challenge, state);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

        var result = await listener.WaitForRedirectAsync(timeoutCts.Token);

        if (result.Error is not null)
            throw new InvalidOperationException(Strings.LoginRejected(result.Error));
        if (result.Code is null || result.State != state)
            throw new InvalidOperationException(Strings.LoginInvalidResponse);

        var tokenResponse = await ExchangeCodeAsync(result.Code, state, pkce.Verifier, redirectUri, cancellationToken);
        TokenStore.Save(ToTokenSet(tokenResponse));
    }

    public void Logout() => TokenStore.Clear();

    /// <summary>Returns a currently-valid access token, refreshing it first if needed.</summary>
    public async Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken)
    {
        var tokens = TokenStore.Load();
        if (tokens is null)
            return null;

        if (!tokens.IsExpiredOrExpiringSoon)
            return tokens.AccessToken;

        return await RefreshAsync(tokens, cancellationToken);
    }

    private async Task<string?> RefreshAsync(TokenSet tokens, CancellationToken cancellationToken)
    {
        if (tokens.RefreshToken is null)
            return tokens.AccessToken;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed while we waited for the lock.
            var current = TokenStore.Load();
            if (current is not null && !current.IsExpiredOrExpiringSoon)
                return current.AccessToken;

            var body = new
            {
                grant_type = "refresh_token",
                refresh_token = tokens.RefreshToken,
                client_id = OAuthConfig.ClientId,
                scope = OAuthConfig.Scope,
            };

            using var response = await _http.PostAsJsonAsync(OAuthConfig.TokenUrl, body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Refresh token no longer valid - drop the stored session so the tray
                // falls back to a "please log in again" state instead of looping.
                TokenStore.Clear();
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(Strings.TokenRefreshEmptyResponse);

            var refreshed = ToTokenSet(tokenResponse) with { RefreshToken = tokenResponse.RefreshToken ?? tokens.RefreshToken };
            TokenStore.Save(refreshed);
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<TokenResponse> ExchangeCodeAsync(string code, string state, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        var body = new
        {
            grant_type = "authorization_code",
            code,
            redirect_uri = redirectUri,
            client_id = OAuthConfig.ClientId,
            code_verifier = codeVerifier,
            state,
        };

        using var response = await _http.PostAsJsonAsync(OAuthConfig.TokenUrl, body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(Strings.TokenExchangeFailed((int)response.StatusCode, text));
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(Strings.TokenExchangeEmptyResponse);
    }

    private static TokenSet ToTokenSet(TokenResponse response) => new(
        response.AccessToken,
        response.RefreshToken,
        response.ExpiresIn is { } seconds ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null,
        response.Scope);

    private static string BuildAuthorizeUrl(string redirectUri, string codeChallenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = OAuthConfig.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = OAuthConfig.Scope,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        };
        var queryString = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{OAuthConfig.AuthorizeUrl}?{queryString}";
    }
}
