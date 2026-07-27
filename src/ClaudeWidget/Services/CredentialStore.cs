using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeWidget.Services;

public sealed record OAuthCredentials(
    string AccessToken,
    DateTimeOffset? ExpiresAt,
    bool FromEnvironment)
{
    /// <summary>True when the access token is expired or within the given margin of it.</summary>
    public bool IsExpiringSoon(TimeSpan margin) =>
        ExpiresAt is { } exp && DateTimeOffset.UtcNow >= exp - margin;
}

/// <summary>
/// Reads the Claude Code OAuth token. Lookup order matches the other usage
/// widgets: CLAUDE_CODE_OAUTH_TOKEN env var first, then ~/.claude/.credentials.json.
///
/// The file is re-read on every poll rather than cached, because Claude Code
/// rewrites it whenever it refreshes the token — that alone keeps us valid for
/// as long as the user is actively using Claude Code.
///
/// Strictly read-only, and the refresh token is deliberately never even parsed:
/// it is one-time-use, and the CLI is its only rightful spender. This class
/// once wrote refreshed tokens back here; that rotation raced the CLI's
/// in-memory copy and revoked the whole token family, logging out both apps.
/// </summary>
public sealed class CredentialStore
{
    public static string CredentialsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        ".credentials.json");

    public OAuthCredentials? Read()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return new OAuthCredentials(fromEnv.Trim(), null, FromEnvironment: true);

        try
        {
            if (!File.Exists(CredentialsPath)) return null;

            using var stream = new FileStream(
                CredentialsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var root = JsonNode.Parse(stream)?["claudeAiOauth"];
            if (root is null) return null;

            var access = root["accessToken"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(access)) return null;

            DateTimeOffset? expires = root["expiresAt"]?.GetValue<long>() is { } ms
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : null;

            return new OAuthCredentials(access, expires, FromEnvironment: false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
