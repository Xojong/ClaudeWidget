using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ClaudeWidget.Services;

public sealed class WidgetSettings
{
    public double Left { get; set; } = 60;
    public double Top { get; set; } = 60;
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.92;
    public int RefreshMinutes { get; set; } = 2;

    public bool ShowLabels { get; set; } = true;
    public bool ShowRemaining { get; set; } = true;
    public bool ShowResetClock { get; set; } = true;
    public bool ShowScopedRow { get; set; } = true;

    public bool AlwaysOnTop { get; set; } = true;
    public bool LockPosition { get; set; }
    public bool AutoRefreshToken { get; set; } = true;

    /// <summary>Which model-scoped weekly bucket to surface, matched on the API's display_name.</summary>
    public string ScopedModelName { get; set; } = "Fable";

    /// <summary>UI language: "ko" or "en".</summary>
    public string Language { get; set; } = "ko";
}

public sealed class SettingsStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ClaudeWidget";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeWidget",
        "settings.json");

    public WidgetSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                if (JsonSerializer.Deserialize<WidgetSettings>(json) is { } loaded)
                    return Sanitize(loaded);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings shouldn't stop the widget from starting.
        }

        return new WidgetSettings();
    }

    public void Save(WidgetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static WidgetSettings Sanitize(WidgetSettings s)
    {
        s.Scale = Math.Clamp(s.Scale, 0.5, 2.0);
        s.Opacity = Math.Clamp(s.Opacity, 0.3, 1.0);
        s.RefreshMinutes = Math.Clamp(s.RefreshMinutes, 1, 5);
        if (string.IsNullOrWhiteSpace(s.ScopedModelName)) s.ScopedModelName = "Fable";
        s.Language = Strings.Parse(s.Language) == AppLanguage.English ? "en" : "ko";
        return s;
    }

    public static bool IsRunAtStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValueName) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static void SetRunAtStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;

            if (!enabled)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                return;
            }

            // Environment.ProcessPath is the apphost .exe, which is what we want
            // registered — not the managed .dll.
            if (Environment.ProcessPath is { Length: > 0 } exe)
                key.SetValue(RunValueName, $"\"{exe}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}
