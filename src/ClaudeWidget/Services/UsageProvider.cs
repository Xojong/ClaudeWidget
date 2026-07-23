using ClaudeWidget.Models;

namespace ClaudeWidget.Services;

/// <summary>
/// Decides where usage numbers come from: Claude Code's local log first, the
/// OAuth API only when that log has gone quiet.
///
/// The ordering is deliberate. The API endpoint is rate limited per token and
/// the quota is already spoken for by whatever appends to usage-history.jsonl
/// once a minute; a widget that polls it independently reliably trips a 429 and
/// then shows nothing. Reading the log is free, always current to within a
/// minute, and leaves the quota alone. The API is the fallback for when Claude
/// Code isn't running, which is exactly when the widget is otherwise blind.
/// </summary>
public sealed class UsageProvider(UsageHistoryReader history, UsageClient api)
{
    /// <summary>
    /// How stale a local entry may be before we go to the network. The writer
    /// appends every ~60s, so this tolerates a couple of missed writes.
    /// </summary>
    public static readonly TimeSpan FreshEnough = TimeSpan.FromMinutes(3);

    public async Task<UsageResult> GetAsync(string scopedModelName, bool autoRefresh, CancellationToken ct)
    {
        var local = history.TryReadLatest();

        if (local is not null && local.Age <= FreshEnough)
            return new UsageResult(local.Snapshot, WidgetState.Ok);

        var remote = await api.FetchAsync(scopedModelName, autoRefresh, ct).ConfigureAwait(false);
        if (remote.Snapshot is not null) return remote;

        // Network unavailable or rate limited. A stale local reading still beats
        // an empty widget — surface it, flagged as stale so the UI dims it.
        if (local is not null)
            return new UsageResult(
                local.Snapshot, WidgetState.Stale, UsageStatus.StaleLocal, (int)local.Age.TotalMinutes);

        return remote;
    }
}
