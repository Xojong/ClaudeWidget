using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using ClaudeWidget.Models;

namespace ClaudeWidget.Services;

/// <param name="Status">Why the fetch ended as it did; localised at display time.</param>
/// <param name="Detail">Status-specific number (HTTP code, age in minutes).</param>
public sealed record UsageResult(
    UsageSnapshot? Snapshot,
    WidgetState State,
    UsageStatus Status = UsageStatus.None,
    int Detail = 0);

/// <summary>
/// Fetches and normalises Claude usage from the OAuth usage endpoint — the same
/// one the Claude web UI uses.
/// </summary>
public sealed class UsageClient(HttpClient http, CredentialStore credentials, TokenRefresher refresher)
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";

    /// <summary>
    /// Floor on how often we'll actually hit the network, independent of the
    /// poll interval. This endpoint is rate limited per token and the quota is
    /// shared with whatever else is asking — Claude Code's own status line, the
    /// dashboard plugin, another widget. Observed behaviour is a Cloudflare 429
    /// after a handful of calls in quick succession, so repeated manual
    /// "refresh now" clicks are served from cache rather than burning quota.
    /// </summary>
    private static readonly TimeSpan MinFetchInterval = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan DefaultThrottle = TimeSpan.FromMinutes(1);

    private DateTimeOffset _lastFetchAt = DateTimeOffset.MinValue;
    private DateTimeOffset _throttledUntil = DateTimeOffset.MinValue;
    private UsageSnapshot? _lastSnapshot;

    public async Task<UsageResult> FetchAsync(string scopedModelName, bool autoRefresh, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _throttledUntil || now - _lastFetchAt < MinFetchInterval)
        {
            // Loading, not Error: this is our own throttle, so it must not feed
            // the caller's failure backoff.
            return _lastSnapshot is not null
                ? new UsageResult(_lastSnapshot, WidgetState.Ok)
                : new UsageResult(null, WidgetState.Loading, UsageStatus.Waiting);
        }

        _lastFetchAt = now;

        var creds = credentials.Read();
        if (creds is null)
            return new UsageResult(null, WidgetState.NeedsAuth, UsageStatus.NoToken);

        var token = creds.AccessToken;

        // Pre-emptive refresh: cheaper than eating a 401 and retrying.
        if (autoRefresh && creds.IsExpiringSoon(TimeSpan.FromMinutes(5)) && refresher.CanAttempt)
            token = await refresher.TryRefreshAsync(creds, ct).ConfigureAwait(false) ?? token;

        var (response, error) = await SendAsync(token, ct).ConfigureAwait(false);
        if (error is not null) return error;

        if (response!.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();

            // Claude Code may have rotated the token underneath us since we read it.
            var reread = credentials.Read();
            if (reread is not null && reread.AccessToken != token)
            {
                token = reread.AccessToken;
            }
            else if (autoRefresh && reread is not null)
            {
                var refreshed = await refresher.TryRefreshAsync(reread, ct).ConfigureAwait(false);
                if (refreshed is null)
                    return new UsageResult(null, WidgetState.NeedsAuth, UsageStatus.NeedsAuth);
                token = refreshed;
            }
            else
            {
                return new UsageResult(null, WidgetState.NeedsAuth, UsageStatus.NeedsAuth);
            }

            (response, error) = await SendAsync(token, ct).ConfigureAwait(false);
            if (error is not null) return error;

            if (response!.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                return new UsageResult(null, WidgetState.NeedsAuth, UsageStatus.NeedsAuth);
            }
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Retry-After has been observed coming back as 0, which we can't
                // take literally without hammering straight back into the limit.
                var retryAfter = response.Headers.RetryAfter?.Delta
                                 ?? (response.Headers.RetryAfter?.Date is { } date
                                     ? date - DateTimeOffset.UtcNow
                                     : null);

                var wait = retryAfter is { } d && d > TimeSpan.Zero ? d : DefaultThrottle;
                _throttledUntil = DateTimeOffset.UtcNow + wait;

                return new UsageResult(null, WidgetState.Error, UsageStatus.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
                return new UsageResult(null, WidgetState.Error, UsageStatus.HttpError, (int)response.StatusCode);

            try
            {
                var payload = await response.Content
                    .ReadFromJsonAsync<OAuthUsageResponse>(ct).ConfigureAwait(false);

                if (payload is null)
                    return new UsageResult(null, WidgetState.Error, UsageStatus.ParseError);

                _lastSnapshot = Normalize(payload, scopedModelName);
                return new UsageResult(_lastSnapshot, WidgetState.Ok);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new UsageResult(null, WidgetState.Error, UsageStatus.ParseError);
            }
        }
    }

    private async Task<(HttpResponseMessage?, UsageResult?)> SendAsync(string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
            return (await http.SendAsync(request, ct).ConfigureAwait(false), null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, new UsageResult(null, WidgetState.Error, UsageStatus.Timeout));
        }
        catch (HttpRequestException)
        {
            return (null, new UsageResult(null, WidgetState.Error, UsageStatus.NetworkError));
        }
    }

    /// <summary>
    /// Maps the API payload onto our three buckets.
    ///
    /// The <c>limits</c> array is authoritative — it is the only place
    /// model-scoped usage (e.g. Fable) appears. The top-level five_hour/seven_day
    /// objects are a fallback for accounts/versions that don't send <c>limits</c>;
    /// the top-level seven_day_opus/seven_day_sonnet fields are always null in
    /// practice and are intentionally not read at all.
    /// </summary>
    internal static UsageSnapshot Normalize(OAuthUsageResponse payload, string scopedModelName)
    {
        UsageBucket? session = null, weeklyAll = null, scoped = null;

        if (payload.Limits is { Count: > 0 } limits)
        {
            session = ToBucket(limits.FirstOrDefault(l => l.Kind == "session"), "5H");
            weeklyAll = ToBucket(limits.FirstOrDefault(l => l.Kind == "weekly_all"), "7D");

            var scopedEntries = limits.Where(l => l.Kind == "weekly_scoped").ToList();
            var match = scopedEntries.FirstOrDefault(l => string.Equals(
                              l.Scope?.Model?.DisplayName, scopedModelName, StringComparison.OrdinalIgnoreCase))
                        ?? scopedEntries.FirstOrDefault();

            if (match is not null)
                scoped = ToBucket(match, match.Scope?.Model?.DisplayName ?? scopedModelName);
        }

        session ??= FromWindow(payload.FiveHour, "5H");
        weeklyAll ??= FromWindow(payload.SevenDay, "7D");

        return new UsageSnapshot(session, weeklyAll, scoped, DateTimeOffset.Now);

        static UsageBucket? ToBucket(LimitEntry? entry, string label) =>
            entry?.Percent is { } pct
                ? new UsageBucket(label, Math.Clamp(pct, 0, 100), entry.Severity, entry.ResetsAt)
                : null;

        static UsageBucket? FromWindow(UsageWindow? window, string label) =>
            window?.Utilization is { } pct
                ? new UsageBucket(label, Math.Clamp(pct, 0, 100), null, window.ResetsAt)
                : null;
    }
}
