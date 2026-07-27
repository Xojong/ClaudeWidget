using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ClaudeWidget.Services;

namespace ClaudeWidget;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;

    // Declaration order is the initialisation order — Http must exist before the
    // services that take it.
    public static HttpClient Http { get; } = CreateHttpClient();
    public static CredentialStore Credentials { get; } = new();
    public static UsageClient Usage { get; } = new(Http, Credentials);
    public static UsageHistoryReader History { get; } = new();
    public static UsageProvider Provider { get; } = new(History, Usage);
    public static SettingsStore Settings { get; } = new();

    /// <summary>
    /// `--demo` renders representative numbers without calling the API. Useful
    /// for dialling in size and opacity, and for checking the layout when the
    /// usage endpoint is rate limited (its quota is shared with Claude Code).
    /// </summary>
    public static bool DemoMode { get; private set; }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ClaudeWidget/1.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        return client;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase)))
        {
            await RunProbeAsync();
            Shutdown();
            return;
        }

        DemoMode = e.Args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));

        // Set before the window is built so a startup failure can be reported in
        // the user's language.
        Strings.Language = Strings.Parse(Settings.Load().Language);

        // Separate mutex for demo mode so a preview can run alongside the real one.
        var mutexName = DemoMode ? "ClaudeWidget.SingleInstance.Demo" : "ClaudeWidget.SingleInstance";
        _instanceMutex = new Mutex(initiallyOwned: true, mutexName, out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        // Startup is handled separately from the runtime handler below: if the
        // window itself fails to build, swallowing that would leave a running
        // process with nothing on screen and no way to tell why.
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            LogError(ex);
            System.Windows.MessageBox.Show(
                $"{Strings.StartupFailed}\n\n{ex.GetType().Name}: {ex.Message}",
                "ClaudeWidget",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Past this point a background failure should dim the widget, never kill it.
        DispatcherUnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Swallowed so a transient fault can't take the widget down — but never
        // silently: an unexplained blank widget is impossible to diagnose
        // otherwise.
        LogError(e.Exception);
        e.Handled = true;
    }

    public static string ErrorLogPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeWidget",
        "error.log");

    private static void LogError(Exception ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ErrorLogPath)!);
            System.IO.File.AppendAllText(ErrorLogPath, $"[{DateTimeOffset.Now:O}]\n{ex}\n\n");
        }
        catch
        {
            // Logging must never be the thing that breaks the app.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// `ClaudeWidget.exe --probe` prints the parsed snapshot to a console.
    /// Kept in the shipping build because it is the fastest way to tell whether a
    /// display problem is in the UI or in the data.
    /// </summary>
    private static async Task RunProbeAsync()
    {
        if (!AttachConsole(AttachParentProcess)) AllocConsole();

        var settings = Settings.Load();
        Strings.Language = Strings.Parse(settings.Language);
        Console.WriteLine($"credentials : {CredentialStore.CredentialsPath}");

        var creds = Credentials.Read();
        Console.WriteLine(creds is null
            ? "token       : (none)"
            : $"token       : {creds.AccessToken[..Math.Min(12, creds.AccessToken.Length)]}… " +
              $"expires {creds.ExpiresAt?.LocalDateTime.ToString() ?? "?"} " +
              $"(env={creds.FromEnvironment})");

        var local = History.TryReadLatest();
        Console.WriteLine($"history     : {UsageHistoryReader.HistoryPath}");
        Console.WriteLine(local is null
            ? "local entry : (none)"
            : $"local entry : {local.RecordedAt.LocalDateTime:HH:mm:ss} " +
              $"({(int)local.Age.TotalSeconds}s ago, fresh={local.Age <= UsageProvider.FreshEnough})");

        var result = await Provider.GetAsync(settings.ScopedModelName, CancellationToken.None);
        Console.WriteLine($"state       : {result.State} / {result.Status}" +
                          (result.Detail != 0 ? $" ({result.Detail})" : ""));

        if (result.Snapshot is { } s)
        {
            foreach (var (name, bucket) in new[]
                     {
                         ("session   ", s.Session),
                         ("weekly_all", s.WeeklyAll),
                         ("scoped    ", s.Scoped),
                     })
            {
                Console.WriteLine(bucket is null
                    ? $"{name}  : (absent)"
                    : $"{name}  : {bucket.Percent,5:0.#}%  label={bucket.Label,-8} " +
                      $"severity={bucket.Severity ?? "-",-8} resets={bucket.ResetsAt?.LocalDateTime.ToString() ?? "-"}");
            }
        }

        Console.Out.Flush();
        FreeConsole();
    }

    private const uint AttachParentProcess = unchecked((uint)-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();
}
