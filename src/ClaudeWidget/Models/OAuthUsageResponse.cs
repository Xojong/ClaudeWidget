using System.Text.Json.Serialization;

namespace ClaudeWidget.Models;

/// <summary>
/// Shape of GET https://api.anthropic.com/api/oauth/usage
///
/// Only the parts we actually consume are modelled. Note that the top-level
/// per-model buckets (seven_day_opus, seven_day_sonnet, ...) come back null on
/// current accounts — model-scoped usage lives in <see cref="Limits"/> instead.
/// </summary>
public sealed class OAuthUsageResponse
{
    [JsonPropertyName("five_hour")] public UsageWindow? FiveHour { get; set; }
    [JsonPropertyName("seven_day")] public UsageWindow? SevenDay { get; set; }
    [JsonPropertyName("limits")] public List<LimitEntry>? Limits { get; set; }
}

/// <summary>Legacy/fallback bucket. <c>utilization</c> is a percentage (0-100), not a fraction.</summary>
public sealed class UsageWindow
{
    [JsonPropertyName("utilization")] public double? Utilization { get; set; }
    [JsonPropertyName("resets_at")] public DateTimeOffset? ResetsAt { get; set; }
}

/// <summary>
/// One row of the <c>limits</c> array. <c>kind</c> is the discriminator:
/// <c>session</c> (5-hour window), <c>weekly_all</c>, <c>weekly_scoped</c>
/// (model-specific — see <see cref="LimitScope"/>).
/// </summary>
public sealed class LimitEntry
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("group")] public string? Group { get; set; }
    [JsonPropertyName("percent")] public double? Percent { get; set; }
    [JsonPropertyName("severity")] public string? Severity { get; set; }
    [JsonPropertyName("resets_at")] public DateTimeOffset? ResetsAt { get; set; }
    [JsonPropertyName("scope")] public LimitScope? Scope { get; set; }
    [JsonPropertyName("is_active")] public bool? IsActive { get; set; }
}

public sealed class LimitScope
{
    [JsonPropertyName("model")] public ScopeModel? Model { get; set; }
}

public sealed class ScopeModel
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
}
