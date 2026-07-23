using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using ClaudeWidget.Models;
using ClaudeWidget.Services;

namespace ClaudeWidget.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GaugeRowViewModel : ObservableObject
{
    private double _percent;
    private string _label = "";
    private Brush _valueBrush = Brushes.Gray;
    private bool _isVisible = true;
    private bool _hasData;

    public double Percent
    {
        get => _percent;
        private set
        {
            if (!Set(ref _percent, value)) return;
            Raise(nameof(PercentText));
            Raise(nameof(PercentSuffix));
        }
    }

    /// <summary>Rounded whole number — no decimal point to spend pixels on.</summary>
    public string PercentText => _hasData ? Math.Round(_percent).ToString("0") : "–";

    /// <summary>Rendered as a separate, smaller run beside the number. Empty when there's no value, so the placeholder doesn't read "–%".</summary>
    public string PercentSuffix => _hasData ? "%" : "";

    public string Label
    {
        get => _label;
        private set => Set(ref _label, value);
    }

    public Brush ValueBrush
    {
        get => _valueBrush;
        private set => Set(ref _valueBrush, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set { if (Set(ref _isVisible, value)) Raise(nameof(Visibility)); }
    }

    public Visibility Visibility => _isVisible ? Visibility.Visible : Visibility.Collapsed;

    public void Update(UsageBucket? bucket, string fallbackLabel)
    {
        _hasData = bucket is not null;
        Label = Abbreviate(bucket?.Label ?? fallbackLabel);
        Percent = bucket?.Percent ?? 0;
        ValueBrush = BrushFor(bucket);
        Raise(nameof(PercentText));
        Raise(nameof(PercentSuffix));
    }

    /// <summary>
    /// Squeezes a model name into three characters by dropping vowels after the
    /// first letter — "Fable" becomes "Fbl". Short names are left alone.
    /// </summary>
    internal static string Abbreviate(string name)
    {
        if (name.Length <= 4) return name;

        var kept = new List<char> { name[0] };
        foreach (var c in name.Skip(1))
        {
            if ("aeiouAEIOU".Contains(c)) continue;
            kept.Add(c);
            if (kept.Count == 3) break;
        }

        return new string(kept.ToArray());
    }

    private static Brush BrushFor(UsageBucket? bucket)
    {
        if (bucket is null) return Resource("TextMutedBrush");

        // Trust the API's own severity when it says something is wrong; fall back
        // to thresholds otherwise (it reports "normal" for most of the range).
        var key = bucket.Severity?.ToLowerInvariant() switch
        {
            "critical" or "severe" or "exceeded" => "GaugeDangerBrush",
            "warning" or "warn" => "GaugeWarnBrush",
            _ => bucket.Percent switch
            {
                <= 60 => "GaugeOkBrush",
                <= 85 => "GaugeWarnBrush",
                _ => "GaugeDangerBrush",
            },
        };

        return Resource(key);
    }

    private static Brush Resource(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

public sealed class MainViewModel : ObservableObject
{
    private UsageSnapshot? _snapshot;
    private WidgetState _state = WidgetState.Loading;
    private UsageStatus _status;
    private int _statusDetail;
    private DateTimeOffset? _fetchedAt;
    private string _remainingText = "";
    private string _resetClockText = "--:--";

    public GaugeRowViewModel SessionRow { get; } = new();
    public GaugeRowViewModel WeeklyRow { get; } = new();
    public GaugeRowViewModel ScopedRow { get; } = new();

    public ObservableCollection<GaugeRowViewModel> Rows { get; }

    public MainViewModel()
    {
        Rows = [SessionRow, WeeklyRow, ScopedRow];
        SessionRow.Update(null, "5H");
        WeeklyRow.Update(null, "7D");
        ScopedRow.Update(null, "Fbl");
    }

    public WidgetState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(ContentOpacity));
            Raise(nameof(StatusText));
            Raise(nameof(StatusTooltip));
            Raise(nameof(SourceTooltip));
            Raise(nameof(StatusVisibility));
            Raise(nameof(FooterVisibility));
        }
    }

    /// <summary>Stale data stays on screen but recedes, so a silent failure is still visible.</summary>
    public double ContentOpacity => State is WidgetState.Stale ? 0.45 : 1.0;

    /// <summary>
    /// Deliberately a single glyph. Spelling the error out inline would let a
    /// transient failure widen the whole widget, which is the one thing this
    /// design is trying to avoid — the detail lives in <see cref="StatusTooltip"/>.
    /// </summary>
    public string StatusText => State switch
    {
        WidgetState.NeedsAuth => "⚠",
        WidgetState.Error => "⚠",
        WidgetState.Loading => "…",
        _ => "",
    };

    public string StatusTooltip => State switch
    {
        WidgetState.NeedsAuth => Strings.NeedsAuthDetail,
        WidgetState.Error => DescribeStatus(),
        WidgetState.Loading => _status is UsageStatus.Waiting ? Strings.Waiting : Strings.Loading,
        _ => "",
    };

    /// <summary>Turns the last fetch's status code into text, in whatever language is current.</summary>
    private string DescribeStatus() => _status switch
    {
        UsageStatus.NoToken => Strings.NoToken,
        UsageStatus.NeedsAuth => Strings.NeedsAuthDetail,
        UsageStatus.RateLimited => Strings.RateLimited,
        UsageStatus.NetworkError => Strings.NetworkError,
        UsageStatus.Timeout => Strings.Timeout,
        UsageStatus.ParseError => Strings.ParseError,
        UsageStatus.HttpError => Strings.HttpError(_statusDetail),
        UsageStatus.Waiting => Strings.Waiting,
        UsageStatus.StaleLocal => Strings.StaleLocal(_statusDetail),
        _ => Strings.UnknownError,
    };

    public Visibility StatusVisibility =>
        State is WidgetState.NeedsAuth or WidgetState.Error or WidgetState.Loading
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>
    /// Hover text on the whole widget — costs no pixels but explains where the
    /// numbers came from. Computed rather than stored so a language switch
    /// re-renders it without waiting for the next fetch.
    /// </summary>
    public string SourceTooltip
    {
        get
        {
            if (_fetchedAt is not { } at) return "";
            var asOf = Strings.AsOf(at.ToString("HH:mm"));
            return State is WidgetState.Stale ? $"{DescribeStatus()} · {asOf}" : asOf;
        }
    }

    /// <summary>Re-renders every localised string in place after a language switch.</summary>
    public void RefreshLocalizedText()
    {
        Raise(nameof(StatusText));
        Raise(nameof(StatusTooltip));
        Raise(nameof(SourceTooltip));
        UpdateCountdown();
    }

    /// <summary>Reads as "(2:11 남음)" beside the reset clock, or standalone if the clock is hidden.</summary>
    public string RemainingText
    {
        get => _remainingText;
        private set => Set(ref _remainingText, value);
    }

    public string ResetClockText
    {
        get => _resetClockText;
        private set => Set(ref _resetClockText, value);
    }

    // --- Display options (driven by settings; each one the user turns off is
    // height or width the widget stops occupying) ---

    private bool _showLabels = true;
    private bool _showRemaining = true;
    private bool _showResetClock = true;

    public Visibility LabelVisibility => _showLabels ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CountdownVisibility => _showRemaining ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResetClockVisibility => _showResetClock ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FooterVisibility =>
        _showRemaining || _showResetClock || StatusVisibility is Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public void ApplyDisplayOptions(Services.WidgetSettings settings)
    {
        _showLabels = settings.ShowLabels;
        _showRemaining = settings.ShowRemaining;
        _showResetClock = settings.ShowResetClock;
        ScopedRow.IsVisible = settings.ShowScopedRow;

        Raise(nameof(LabelVisibility));
        Raise(nameof(CountdownVisibility));
        Raise(nameof(ResetClockVisibility));
        Raise(nameof(FooterVisibility));

        // The parentheses depend on whether the clock is showing.
        UpdateCountdown();
    }

    public void Apply(UsageResult result)
    {
        _status = result.Status;
        _statusDetail = result.Detail;

        if (result.Snapshot is { } snapshot)
        {
            _snapshot = snapshot;
            _fetchedAt = snapshot.FetchedAt;
            SessionRow.Update(snapshot.Session, "5H");
            WeeklyRow.Update(snapshot.WeeklyAll, "7D");
            ScopedRow.Update(snapshot.Scoped, "Fbl");

            // A stale reading is real data, just old — show it dimmed rather
            // than pretending it's current.
            State = result.State is WidgetState.Stale ? WidgetState.Stale : WidgetState.Ok;
        }
        else
        {
            // Keep the last good numbers up rather than blanking the widget.
            State = result.State switch
            {
                WidgetState.NeedsAuth => WidgetState.NeedsAuth,
                WidgetState.Loading => WidgetState.Loading,
                _ => _snapshot is not null ? WidgetState.Stale : WidgetState.Error,
            };
        }

        Raise(nameof(StatusTooltip));
        Raise(nameof(SourceTooltip));
        UpdateCountdown();
    }

    /// <summary>Pure local arithmetic against the cached reset time — never hits the network.</summary>
    public void UpdateCountdown()
    {
        if (_snapshot?.PrimaryResetsAt is not { } resetsAt)
        {
            ResetClockText = "--:--";
            RemainingText = Wrap(Strings.Remaining("-:--"));
            return;
        }

        var local = resetsAt.ToLocalTime();
        ResetClockText = local.ToString("HH:mm");

        var remaining = local - DateTimeOffset.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        RemainingText = Wrap(Strings.Remaining($"{(int)remaining.TotalHours}:{remaining.Minutes:00}"));

        // Parenthesised only when it sits beside the clock it qualifies.
        string Wrap(string text) => _showResetClock ? $"({text})" : text;
    }
}
