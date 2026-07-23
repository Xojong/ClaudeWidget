using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ClaudeWidget.Services;

/// <summary>
/// Exchanges a refresh token for a fresh access token.
///
/// Rate limiting here is not politeness, it is self-preservation: the refresh
/// endpoint has been observed handing out 429s and Cloudflare blocks to clients
/// that call it in a loop (claude-code issues #38248, #47754). We only ever call
/// it when the token is actually about to die, and never more than once per
/// <see cref="MinInterval"/>.
/// </summary>
public sealed class TokenRefresher(HttpClient http, CredentialStore credentials)
{
    private const string TokenEndpoint = "https://platform.claude.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(30);

    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public bool CanAttempt => DateTimeOffset.UtcNow - _lastAttempt >= MinInterval;

    /// <summary>Returns the new access token, or null if refresh was skipped or failed.</summary>
    public async Task<string?> TryRefreshAsync(OAuthCredentials current, CancellationToken ct)
    {
        if (current.FromEnvironment) return null;           // env-supplied tokens aren't ours to rotate
        if (string.IsNullOrWhiteSpace(current.RefreshToken)) return null;
        if (!CanAttempt) return null;

        _lastAttempt = DateTimeOffset.UtcNow;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = JsonContent.Create(new
                {
                    grant_type = "refresh_token",
                    refresh_token = current.RefreshToken,
                    client_id = ClientId,
                }),
            };

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var payload = await response.Content
                .ReadFromJsonAsync<TokenResponse>(ct).ConfigureAwait(false);

            if (payload?.AccessToken is not { Length: > 0 } access) return null;

            DateTimeOffset? expiresAt = payload.ExpiresIn is { } secs
                ? DateTimeOffset.UtcNow.AddSeconds(secs)
                : null;

            credentials.WriteBack(access, payload.RefreshToken ?? current.RefreshToken, expiresAt);
            return access;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
    }
}
