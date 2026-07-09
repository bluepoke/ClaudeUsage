using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ClaudeUsageTray.Auth;

namespace ClaudeUsageTray;

public sealed record UsageWindow(
    [property: JsonPropertyName("utilization")] double Utilization,
    [property: JsonPropertyName("resets_at")] DateTimeOffset? ResetsAt)
{
    // The API already returns utilization as a 0-100 percentage, not a 0-1 fraction.
    public int Percent => (int)Math.Round(Math.Clamp(Utilization, 0, 100));
}

public sealed record UsageApiResponse(
    [property: JsonPropertyName("five_hour")] UsageWindow? FiveHour,
    [property: JsonPropertyName("seven_day")] UsageWindow? SevenDay);

public sealed record UsageSnapshot(
    int SessionPercent,
    int WeeklyPercent,
    DateTimeOffset? SessionResetsAt,
    DateTimeOffset? WeeklyResetsAt,
    DateTimeOffset FetchedAt);

public enum UsageFetchStatus
{
    Ok,
    NotLoggedIn,
    AuthExpired,
    NetworkError,
}

public sealed record UsageFetchResult(UsageFetchStatus Status, UsageSnapshot? Snapshot, string? Error = null);

/// <summary>
/// Calls the same GET /api/oauth/usage endpoint the official Claude Code CLI uses to
/// report the account's five-hour session and seven-day (weekly) rate-limit usage.
/// </summary>
public sealed class UsageApiClient(OAuthClient oauth)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        var accessToken = await oauth.GetValidAccessTokenAsync(cancellationToken);
        if (accessToken is null)
            return new UsageFetchResult(UsageFetchStatus.NotLoggedIn, null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, OAuthConfig.UsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new UsageFetchResult(UsageFetchStatus.AuthExpired, null);
            if (!response.IsSuccessStatusCode)
                return new UsageFetchResult(UsageFetchStatus.NetworkError, null, $"HTTP {(int)response.StatusCode}");

            var data = await response.Content.ReadFromJsonAsync<UsageApiResponse>(cancellationToken: cancellationToken);
            if (data is null)
                return new UsageFetchResult(UsageFetchStatus.NetworkError, null, "Leere Antwort");

            var snapshot = new UsageSnapshot(
                data.FiveHour?.Percent ?? 0,
                data.SevenDay?.Percent ?? 0,
                data.FiveHour?.ResetsAt,
                data.SevenDay?.ResetsAt,
                DateTimeOffset.Now);

            return new UsageFetchResult(UsageFetchStatus.Ok, snapshot);
        }
        catch (HttpRequestException ex)
        {
            return new UsageFetchResult(UsageFetchStatus.NetworkError, null, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new UsageFetchResult(UsageFetchStatus.NetworkError, null, "Zeitüberschreitung");
        }
        catch (System.Text.Json.JsonException ex)
        {
            return new UsageFetchResult(UsageFetchStatus.NetworkError, null, $"Unerwartetes Antwortformat: {ex.Message}");
        }
    }
}
