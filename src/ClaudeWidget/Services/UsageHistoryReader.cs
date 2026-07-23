using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeWidget.Models;

namespace ClaudeWidget.Services;

public sealed record HistoryEntry(UsageSnapshot Snapshot, DateTimeOffset RecordedAt)
{
    public TimeSpan Age => DateTimeOffset.Now - RecordedAt;
}

/// <summary>
/// Reads ~/.claude/usage-history.jsonl — the rolling log Claude Code appends to
/// roughly every 60 seconds while a session is live.
///
/// This is the widget's preferred source, and the reason matters: the OAuth
/// usage endpoint is rate limited per token, and that once-a-minute writer is
/// already consuming the quota. Adding a second poller on top is what trips the
/// limit — observed directly, the writer's own entries stop the moment the 429s
/// begin. Reading its output costs nothing and is never more than a minute
/// behind.
///
/// Values here are fractions (0.29), unlike the API's percentages (29.0).
/// </summary>
public sealed class UsageHistoryReader
{
    /// <summary>Only the tail matters; don't read a log that has grown for months.</summary>
    private const int TailBytes = 16 * 1024;

    public static string HistoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "usage-history.jsonl");

    public HistoryEntry? TryReadLatest()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return null;

            // FileShare.ReadWrite: the writer keeps this open.
            using var stream = new FileStream(
                HistoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            if (stream.Length == 0) return null;

            var take = (int)Math.Min(TailBytes, stream.Length);
            stream.Seek(-take, SeekOrigin.End);

            var buffer = new byte[take];
            var read = stream.Read(buffer, 0, take);
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);

            var lastLine = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(line => line.StartsWith('{') && line.EndsWith('}'));

            if (lastLine is null) return null;

            var row = JsonSerializer.Deserialize<HistoryRow>(lastLine);
            return row is null ? null : ToEntry(row);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HistoryEntry? ToEntry(HistoryRow row)
    {
        if (row.Timestamp is not { } ts) return null;

        var recordedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000)).ToLocalTime();

        var snapshot = new UsageSnapshot(
            Session: Bucket("5H", row.Session, row.SessionReset),
            WeeklyAll: Bucket("7D", row.Weekly, row.WeeklyReset),
            Scoped: Bucket(row.ScopedLabel ?? "Scoped", row.Scoped, row.ScopedReset),
            FetchedAt: recordedAt);

        return snapshot is { Session: null, WeeklyAll: null, Scoped: null }
            ? null
            : new HistoryEntry(snapshot, recordedAt);

        static UsageBucket? Bucket(string label, double? fraction, double? resetUnixSeconds) =>
            fraction is { } f
                ? new UsageBucket(
                    label,
                    Math.Clamp(f * 100, 0, 100),
                    Severity: null,
                    ResetsAt: resetUnixSeconds is { } r
                        ? DateTimeOffset.FromUnixTimeSeconds((long)r).ToLocalTime()
                        : null)
                : null;
    }

    private sealed class HistoryRow
    {
        [JsonPropertyName("ts")] public double? Timestamp { get; set; }
        [JsonPropertyName("session")] public double? Session { get; set; }
        [JsonPropertyName("weekly")] public double? Weekly { get; set; }
        [JsonPropertyName("scoped")] public double? Scoped { get; set; }
        [JsonPropertyName("session_reset")] public double? SessionReset { get; set; }
        [JsonPropertyName("weekly_reset")] public double? WeeklyReset { get; set; }
        [JsonPropertyName("scoped_reset")] public double? ScopedReset { get; set; }
        [JsonPropertyName("scoped_label")] public string? ScopedLabel { get; set; }
    }
}
