using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeWidget.Services;

public sealed record OAuthCredentials(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    bool FromEnvironment)
{
    /// <summary>True when the access token is expired or close enough that we should refresh first.</summary>
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
            return new OAuthCredentials(fromEnv.Trim(), null, null, FromEnvironment: true);

        try
        {
            if (!File.Exists(CredentialsPath)) return null;

            using var stream = new FileStream(
                CredentialsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var root = JsonNode.Parse(stream)?["claudeAiOauth"];
            if (root is null) return null;

            var access = root["accessToken"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(access)) return null;

            var refresh = root["refreshToken"]?.GetValue<string>();
            DateTimeOffset? expires = root["expiresAt"]?.GetValue<long>() is { } ms
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : null;

            return new OAuthCredentials(access, refresh, expires, FromEnvironment: false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Persists a refreshed token back into .credentials.json.
    ///
    /// This write-back is deliberate and load-bearing: OAuth refresh tokens
    /// rotate, so if we refresh without saving the new pair, the refresh token
    /// still sitting in Claude Code's file becomes invalid and breaks the CLI's
    /// own login. Unknown fields in the file are preserved (JsonNode round-trip),
    /// and the replace is atomic with a .bak kept behind.
    /// </summary>
    public bool WriteBack(string accessToken, string? refreshToken, DateTimeOffset? expiresAt)
    {
        try
        {
            if (!File.Exists(CredentialsPath)) return false;

            JsonNode? doc;
            using (var read = new FileStream(CredentialsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                doc = JsonNode.Parse(read);

            if (doc?["claudeAiOauth"] is not JsonObject oauth) return false;

            oauth["accessToken"] = accessToken;
            if (!string.IsNullOrWhiteSpace(refreshToken)) oauth["refreshToken"] = refreshToken;
            if (expiresAt is { } exp) oauth["expiresAt"] = exp.ToUnixTimeMilliseconds();

            var tmp = CredentialsPath + ".tmp";
            var bak = CredentialsPath + ".bak";
            File.WriteAllText(tmp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Replace(tmp, CredentialsPath, bak, ignoreMetadataErrors: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
