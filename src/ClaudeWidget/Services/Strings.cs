using System.Globalization;

namespace ClaudeWidget.Services;

public enum AppLanguage
{
    Korean,
    English,
}

/// <summary>
/// UI text in both supported languages.
///
/// A plain static table rather than .resx: the string count is small and fixed,
/// and satellite assemblies are awkward to carry through a single-file publish.
/// Everything resolves at call time, so switching language re-renders correctly
/// without restarting.
/// </summary>
public static class Strings
{
    public static AppLanguage Language { get; set; } = AppLanguage.Korean;

    public static string Code => Language == AppLanguage.English ? "en" : "ko";

    public static AppLanguage Parse(string? code) =>
        string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.English
            : AppLanguage.Korean;

    private static string Pick(string ko, string en) =>
        Language == AppLanguage.English ? en : ko;

    // --- menu ---
    public static string RefreshNow => Pick("지금 새로고침", "Refresh now");
    public static string RefreshInterval => Pick("새로고침 주기", "Refresh interval");
    public static string Minutes(int n) => Pick($"{n}분", $"{n} min");
    public static string Size => Pick("크기", "Size");
    public static string Opacity => Pick("투명도", "Opacity");
    public static string Display => Pick("표시", "Display");
    public static string Labels => Pick("라벨 (5H/7D/Fbl)", "Labels (5H/7D/Fbl)");
    public static string TimeRemaining => Pick("남은 시간", "Time remaining");
    public static string ResetClock => Pick("5H 리셋 시각", "5H reset time");
    public static string WeeklyResetClock => Pick("주간 리셋 시각 (7D/Fbl)", "Weekly reset time (7D/Fbl)");
    public static string ModelRow(string model) => Pick($"{model} 행", $"{model} row");
    public static string AlwaysOnTop => Pick("항상 위", "Always on top");
    public static string LockPosition => Pick("위치 잠금", "Lock position");
    public static string RunAtStartup => Pick("Windows 시작 시 실행", "Run at Windows startup");
    public static string LanguageMenu => Pick("언어", "Language");
    public static string KoreanName => "한국어";
    public static string EnglishName => "English";
    public static string Exit => Pick("종료", "Exit");
    public static string MenuTooltip => Pick("메뉴", "Menu");

    // --- footer ---
    /// <summary>The countdown beside the reset clock, e.g. "1:12 남음" / "1:12 left".</summary>
    public static string Remaining(string clock) => Pick($"{clock} 남음", $"{clock} left");

    private static readonly CultureInfo KoreanCulture = CultureInfo.GetCultureInfo("ko-KR");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A weekly reset as weekday + clock, e.g. "화 21:00" / "Tue 21:00". Expects local time.</summary>
    public static string WeekdayClock(DateTimeOffset local) =>
        local.ToString("ddd HH:mm", Language == AppLanguage.English ? EnglishCulture : KoreanCulture);

    // --- status ---
    public static string Loading => Pick("불러오는 중", "Loading");
    /// <summary>Shown in the reset-clock slot — the one fix only the user can do.
    /// Names Claude Code explicitly: someone who installed just the widget has no
    /// other way to know which app owns the login.</summary>
    public static string LoginNeeded => Pick("Claude Code 로그인 필요", "Sign in to Claude Code");
    public static string NeedsAuthDetail =>
        Pick("재인증 필요 — Claude Code에서 다시 로그인하세요",
             "Re-authentication required — sign in again in Claude Code");
    public static string NoToken => Pick("토큰 없음", "No token found");
    public static string RateLimited => Pick("요청 제한", "Rate limited");
    public static string NetworkError => Pick("네트워크 오류", "Network error");
    public static string Timeout => Pick("시간 초과", "Timed out");
    public static string ParseError => Pick("응답 파싱 실패", "Failed to parse response");
    public static string HttpError(int code) => $"HTTP {code}";
    public static string Waiting => Pick("대기 중", "Waiting");
    public static string UnknownError => Pick("오류", "Error");

    /// <summary>Age of a fallback reading from the local log.</summary>
    public static string StaleLocal(int minutes) => minutes >= 60
        ? Pick($"로컬 기록 {minutes / 60}시간 전", $"local log, {minutes / 60}h old")
        : Pick($"로컬 기록 {Math.Max(1, minutes)}분 전", $"local log, {Math.Max(1, minutes)}m old");

    public static string AsOf(string clock) => Pick($"{clock} 기준", $"as of {clock}");

    // --- errors ---
    public static string StartupFailed => Pick("위젯을 시작하지 못했습니다.", "Failed to start the widget.");
}
