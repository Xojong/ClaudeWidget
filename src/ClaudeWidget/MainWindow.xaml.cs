using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeWidget.Models;
using ClaudeWidget.Services;
using ClaudeWidget.ViewModels;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ClaudeWidget;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly WidgetSettings _settings;

    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _countdownTimer = new();

    /// <summary>
    /// Coalesces setting writes. Dragging the opacity or size slider fires
    /// continuously; without this, each pixel of travel would be a disk write.
    /// </summary>
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };

    private CancellationTokenSource? _inFlight;
    private Forms.NotifyIcon? _trayIcon;
    private int _consecutiveFailures;
    private bool _shuttingDown;

    public MainWindow()
    {
        InitializeComponent();

        _settings = App.Settings.Load();
        Strings.Language = Strings.Parse(_settings.Language);
        DataContext = _vm;

        ApplySettingsToWindow();
        MenuButton.ToolTip = Strings.MenuTooltip;
        _vm.ApplyDisplayOptions(_settings);

        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) => _vm.UpdateCountdown();

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            App.Settings.Save(_settings);
        };

        MouseLeftButtonDown += OnDragStart;
        MouseRightButtonUp += (_, _) => ShowMenu();
        MouseWheel += OnMouseWheel;
        // Preview (tunnelling) so marking it handled suppresses the window-level
        // MouseLeftButtonDown — otherwise DragMove() captures the mouse and the
        // button press turns into a drag instead of opening the menu.
        MenuButton.PreviewMouseLeftButtonDown += OnMenuButtonClick;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    // --- lifecycle -----------------------------------------------------------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position after the first layout pass: SizeToContent means the final
        // size isn't known until now, and we need it to clamp against the screen.
        ClampToVirtualScreen();
        SetUpTrayIcon();

        _countdownTimer.Start();
        RestartRefreshTimer();
        await RefreshAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _saveTimer.Stop();          // PersistWindowState writes everything anyway
        PersistWindowState();
        _refreshTimer.Stop();
        _countdownTimer.Stop();
        _inFlight?.Cancel();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (!_shuttingDown)
        {
            _shuttingDown = true;
            System.Windows.Application.Current.Shutdown();
        }
    }

    // --- data ----------------------------------------------------------------

    private async Task RefreshAsync()
    {
        if (App.DemoMode)
        {
            _vm.Apply(DemoResult());
            RestartRefreshTimer();
            return;
        }

        _inFlight?.Cancel();
        _inFlight?.Dispose();
        _inFlight = new CancellationTokenSource();
        var token = _inFlight.Token;

        var result = await App.Provider.GetAsync(
            _settings.ScopedModelName, _settings.AutoRefreshToken, token);

        if (token.IsCancellationRequested) return;

        _vm.Apply(result);

        // Back off when the endpoint is unhappy, but never stop polling entirely.
        // A client-side throttle reports Loading and is not a failure.
        var failed = result.Snapshot is null && result.State is not WidgetState.Loading;
        _consecutiveFailures = failed ? Math.Min(_consecutiveFailures + 1, 3) : 0;
        RestartRefreshTimer();
    }

    /// <summary>Representative values matching a real response, for --demo.</summary>
    private static UsageResult DemoResult() => new(
        new UsageSnapshot(
            Session: new UsageBucket("5H", 29, "normal", DateTimeOffset.Now.AddMinutes(171)),
            WeeklyAll: new UsageBucket("7D", 15, "normal", DateTimeOffset.Now.AddDays(6)),
            Scoped: new UsageBucket("Fable", 26, "normal", DateTimeOffset.Now.AddDays(6)),
            FetchedAt: DateTimeOffset.Now),
        WidgetState.Ok);

    private void RestartRefreshTimer()
    {
        var backoff = 1 << _consecutiveFailures;   // 1x, 2x, 4x, 8x
        _refreshTimer.Stop();
        _refreshTimer.Interval = TimeSpan.FromMinutes(_settings.RefreshMinutes * backoff);
        _refreshTimer.Start();
    }

    // --- window behaviour ----------------------------------------------------

    private void ApplySettingsToWindow()
    {
        Left = _settings.Left;
        Top = _settings.Top;
        Topmost = _settings.AlwaysOnTop;
        RootScale.ScaleX = RootScale.ScaleY = _settings.Scale;
        ApplySurfaceOpacity(_settings.Opacity);
    }

    /// <summary>
    /// Tints the surface brushes rather than setting Window.Opacity.
    /// Window.Opacity would fade the whole visual tree — text and gauges
    /// included — which is exactly what makes small numbers unreadable. Putting
    /// the alpha in the background and border brushes lets the panel go
    /// translucent while everything drawn on top stays fully opaque.
    /// </summary>
    private void ApplySurfaceOpacity(double opacity)
    {
        var alpha = Math.Clamp(opacity, 0.3, 1.0);

        RootBorder.Background = Frozen(Color.FromArgb((byte)Math.Round(0xFF * alpha), 0x14, 0x14, 0x1A));
        RootBorder.BorderBrush = Frozen(Color.FromArgb((byte)Math.Round(0x30 * alpha), 0xFF, 0xFF, 0xFF));

        static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    private void PersistWindowState()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        App.Settings.Save(_settings);
    }

    private void ClampToVirtualScreen()
    {
        // Guards against the widget being stranded off-screen after a monitor
        // is unplugged or the layout changes between runs.
        var minX = SystemParameters.VirtualScreenLeft;
        var minY = SystemParameters.VirtualScreenTop;
        var maxX = minX + SystemParameters.VirtualScreenWidth - ActualWidth;
        var maxY = minY + SystemParameters.VirtualScreenHeight - ActualHeight;

        Left = Math.Clamp(Left, minX, Math.Max(minX, maxX));
        Top = Math.Clamp(Top, minY, Math.Max(minY, maxY));
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (_settings.LockPosition) return;
        if (e.ButtonState != MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove throws if the button was already released; harmless.
        }

        PersistWindowState();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;

        SetScale(_settings.Scale + (e.Delta > 0 ? 0.05 : -0.05));
        e.Handled = true;
    }

    private void SetScale(double scale)
    {
        _settings.Scale = Math.Clamp(Math.Round(scale, 2), 0.5, 2.0);
        RootScale.ScaleX = RootScale.ScaleY = _settings.Scale;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Switches language in place. The menu is rebuilt on every open so it picks
    /// the new text up for free; the widget's own live text has to be told.
    /// </summary>
    private void SetLanguage(AppLanguage language)
    {
        if (Strings.Language == language) return;

        Strings.Language = language;
        _settings.Language = Strings.Code;
        App.Settings.Save(_settings);

        MenuButton.ToolTip = Strings.MenuTooltip;
        _vm.RefreshLocalizedText();
    }

    // --- menu ----------------------------------------------------------------

    private void OnMenuButtonClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowMenu();
    }

    private void ShowMenu()
    {
        // The widget never activates on its own (no taskbar button, never
        // focused), so without this the menu opens without keyboard focus:
        // arrow keys go to whatever app is underneath and the menu closes.
        Activate();
        SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);

        var menu = BuildMenu();
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(Item(Strings.RefreshNow, async () => await RefreshAsync()));
        menu.Items.Add(new Separator());

        // Refresh interval: 1-5 minutes.
        var interval = new MenuItem { Header = Strings.RefreshInterval };
        for (var minutes = 1; minutes <= 5; minutes++)
        {
            var m = minutes;
            interval.Items.Add(Item(Strings.Minutes(m), () =>
            {
                _settings.RefreshMinutes = m;
                _consecutiveFailures = 0;
                RestartRefreshTimer();
                App.Settings.Save(_settings);
            }, isChecked: _settings.RefreshMinutes == m));
        }
        menu.Items.Add(interval);

        // Size: presets plus a fine slider (Ctrl+wheel does the same thing).
        var size = new MenuItem { Header = $"{Strings.Size}  ({_settings.Scale * 100:0}%)" };
        foreach (var preset in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 })
        {
            var p = preset;
            size.Items.Add(Item($"{p * 100:0}%", () => SetScale(p),
                isChecked: Math.Abs(_settings.Scale - p) < 0.001));
        }
        size.Items.Add(new Separator());
        size.Items.Add(SliderItem(0.5, 2.0, _settings.Scale, SetScale, Percent));
        menu.Items.Add(size);

        var opacity = new MenuItem { Header = $"{Strings.Opacity}  ({_settings.Opacity * 100:0}%)" };
        opacity.Items.Add(SliderItem(0.3, 1.0, _settings.Opacity, value =>
        {
            _settings.Opacity = Math.Round(value, 2);
            ApplySurfaceOpacity(_settings.Opacity);
            ScheduleSave();
        }, Percent));
        menu.Items.Add(opacity);

        var display = new MenuItem { Header = Strings.Display };
        display.Items.Add(Item(Strings.Labels, () => ToggleDisplay(s => s.ShowLabels = !s.ShowLabels),
            isChecked: _settings.ShowLabels));
        display.Items.Add(Item(Strings.TimeRemaining, () => ToggleDisplay(s => s.ShowRemaining = !s.ShowRemaining),
            isChecked: _settings.ShowRemaining));
        display.Items.Add(Item(Strings.ResetClock, () => ToggleDisplay(s => s.ShowResetClock = !s.ShowResetClock),
            isChecked: _settings.ShowResetClock));
        display.Items.Add(Item(Strings.ModelRow(_settings.ScopedModelName),
            () => ToggleDisplay(s => s.ShowScopedRow = !s.ShowScopedRow),
            isChecked: _settings.ShowScopedRow));
        menu.Items.Add(display);

        var language = new MenuItem { Header = Strings.LanguageMenu };
        language.Items.Add(Item(Strings.KoreanName, () => SetLanguage(AppLanguage.Korean),
            isChecked: Strings.Language is AppLanguage.Korean));
        language.Items.Add(Item(Strings.EnglishName, () => SetLanguage(AppLanguage.English),
            isChecked: Strings.Language is AppLanguage.English));
        menu.Items.Add(language);

        menu.Items.Add(new Separator());

        menu.Items.Add(Item(Strings.AlwaysOnTop, () =>
        {
            _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
            Topmost = _settings.AlwaysOnTop;
            App.Settings.Save(_settings);
        }, isChecked: _settings.AlwaysOnTop));

        menu.Items.Add(Item(Strings.LockPosition, () =>
        {
            _settings.LockPosition = !_settings.LockPosition;
            App.Settings.Save(_settings);
        }, isChecked: _settings.LockPosition));

        menu.Items.Add(Item(Strings.RunAtStartup, () =>
            SettingsStore.SetRunAtStartup(!SettingsStore.IsRunAtStartupEnabled()),
            isChecked: SettingsStore.IsRunAtStartupEnabled()));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Exit, Close));

        return menu;

        void ToggleDisplay(Action<WidgetSettings> mutate)
        {
            mutate(_settings);
            _vm.ApplyDisplayOptions(_settings);
            App.Settings.Save(_settings);
        }
    }

    /// <summary>
    /// Builds a menu entry. <paramref name="isChecked"/> null means a plain
    /// command; true/false makes it checkable, which is what actually gets WPF
    /// to draw the check mark. The menu is rebuilt on every open, so letting
    /// WPF toggle the visual state on click is harmless — the Click handler owns
    /// the real state.
    /// </summary>
    private static MenuItem Item(string header, Action onClick, bool? isChecked = null)
    {
        var item = new MenuItem { Header = header };

        if (isChecked is { } chk)
        {
            item.IsCheckable = true;
            item.IsChecked = chk;
        }

        item.Click += (_, _) => onClick();
        return item;
    }

    private static string Percent(double fraction) => $"{fraction * 100:0}%";

    /// <summary>A slider with a live readout, hosted in a menu item that stays open while you drag.</summary>
    private static MenuItem SliderItem(
        double min, double max, double value, Action<double> onChange, Func<double, string> format)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Width = 104,
            SmallChange = 0.05,
            LargeChange = 0.1,
            IsSnapToTickEnabled = true,
            TickFrequency = 0.05,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var readout = new TextBlock
        {
            Text = format(value),
            Width = 34,
            Margin = new Thickness(8, 0, 0, 0),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = System.Windows.Application.Current.TryFindResource("TextMutedBrush") as Brush
                         ?? Brushes.Gray,
        };

        slider.ValueChanged += (_, e) =>
        {
            readout.Text = format(e.NewValue);
            onChange(e.NewValue);
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(slider);
        row.Children.Add(readout);

        return new MenuItem { Header = row, StaysOpenOnClick = true };
    }

    // --- tray ----------------------------------------------------------------

    private void SetUpTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Visible = true,
            Text = "Claude Usage",
        };

        // Reuse the WPF menu rather than maintaining a parallel WinForms one.
        _trayIcon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                Dispatcher.Invoke(() =>
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    SetForegroundWindow(handle);
                    ShowMenu();
                });
            }
            else if (e.Button == Forms.MouseButtons.Left)
            {
                // Recovery path: yank the widget back somewhere visible.
                Dispatcher.Invoke(() =>
                {
                    Show();
                    Left = SystemParameters.WorkArea.Left + 40;
                    Top = SystemParameters.WorkArea.Top + 40;
                    PersistWindowState();
                });
            }
        };
    }

    /// <summary>Draws the tray icon at runtime so there's no .ico asset to ship.</summary>
    private static Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            using var track = new Drawing.Pen(Drawing.Color.FromArgb(90, 255, 255, 255), 5f);
            using var value = new Drawing.Pen(Drawing.Color.FromArgb(255, 74, 222, 128), 5f)
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round,
            };

            var rect = new Drawing.RectangleF(4, 4, 24, 24);
            g.DrawEllipse(track, rect);
            g.DrawArc(value, rect, -90, 260);
        }

        var handle = bitmap.GetHicon();
        try
        {
            // Clone so the icon survives DestroyIcon on the temporary handle.
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
