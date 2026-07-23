namespace ClaudeWidget.Models;

/// <summary>A single limit bucket, normalised for display.</summary>
/// <param name="Percent">Percent of the limit consumed (0-100).</param>
/// <param name="Severity">API-provided severity ("normal", ...), or null.</param>
public sealed record UsageBucket(
    string Label,
    double Percent,
    string? Severity,
    DateTimeOffset? ResetsAt);

/// <summary>The three buckets the widget shows, plus when they were fetched.</summary>
public sealed record UsageSnapshot(
    UsageBucket? Session,
    UsageBucket? WeeklyAll,
    UsageBucket? Scoped,
    DateTimeOffset FetchedAt)
{
    /// <summary>Reset time driving the countdown — the 5-hour window is what actually gates you.</summary>
    public DateTimeOffset? PrimaryResetsAt => Session?.ResetsAt ?? WeeklyAll?.ResetsAt;
}

/// <summary>
/// Why a fetch ended the way it did. Services report this code rather than a
/// finished sentence so the text can be produced in the current language at the
/// moment it is displayed — otherwise switching language would leave the last
/// fetch's message stranded in the old one.
/// </summary>
public enum UsageStatus
{
    None,
    NoToken,
    NeedsAuth,
    RateLimited,
    NetworkError,
    Timeout,
    ParseError,
    /// <summary>Detail carries the HTTP status code.</summary>
    HttpError,
    /// <summary>Client-side throttle, not a failure.</summary>
    Waiting,
    /// <summary>Fell back to the local log. Detail carries its age in minutes.</summary>
    StaleLocal,
}

/// <summary>What the UI is currently able to show.</summary>
public enum WidgetState
{
    Loading,
    Ok,
    /// <summary>Fetch failed but we still have an earlier snapshot to display (dimmed).</summary>
    Stale,
    /// <summary>No usable token — user must re-authenticate.</summary>
    NeedsAuth,
    /// <summary>Failed with nothing cached to fall back on.</summary>
    Error,
}
