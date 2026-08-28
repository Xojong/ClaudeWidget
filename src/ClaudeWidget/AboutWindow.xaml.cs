using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using ClaudeWidget.Services;

namespace ClaudeWidget;

/// <summary>
/// Name, version, developer and repository — the only place the widget says
/// who made it. Styled like the menu rather than the widget so it reads as a
/// dialog, not a second widget. Modal on the main window; Esc/Enter close it.
/// </summary>
public partial class AboutWindow : Window
{
    public const string ProductName = "ClaudeWidget";
    public const string Developer = "Xojong";
    public const string RepositoryUrl = "https://github.com/Xojong/ClaudeWidget";

    public AboutWindow()
    {
        InitializeComponent();

        NameText.Text = ProductName;
        VersionText.Text = $"v{Version}";
        TaglineText.Text = Strings.AboutTagline;
        DeveloperLabel.Text = Strings.Developer;
        DeveloperText.Text = Developer;
        RepoLabel.Text = Strings.Repository;
        RepoText.Text = RepositoryUrl["https://".Length..];
        CloseButton.Content = Strings.Close;

        MouseLeftButtonDown += OnDragStart;
        Loaded += (_, _) => ClampToWorkArea();
    }

    /// <summary>"1.1.0" — the csproj Version, minus the trailing revision .NET pads on.</summary>
    public static string Version
    {
        get
        {
            var v = typeof(AboutWindow).Assembly.GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    /// <summary>
    /// CenterOwner centres on the widget, which may sit at a screen edge; pull the
    /// dialog back inside the work area so its close button is never off-screen.
    /// </summary>
    private void ClampToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
    }

    private void OnRepoClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // No browser association — the URL is still readable on the dialog.
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
