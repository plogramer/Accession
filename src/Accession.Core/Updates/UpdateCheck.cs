using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Accession.Core.Runtime;
using Accession.Core.Settings;

namespace Accession.Core.Updates;

/// <summary>A published version of Accession (a GitHub release).</summary>
/// <param name="Tag">The release's tag, e.g. "v0.2".</param>
/// <param name="Notes">The release notes (Markdown as written on GitHub).</param>
/// <param name="Url">The release page, where the download is.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string Title, string Notes, string Url, DateTimeOffset? PublishedAt);

public interface IReleaseSource
{
    /// <summary>The latest published release (drafts and pre-releases excluded); null when there is none yet.</summary>
    Task<ReleaseInfo?> LatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the latest release of a public GitHub repository from the GitHub API
/// (<c>GET /repos/{owner}/{repo}/releases/latest</c>, no sign-in). Only the request itself is sent: no user or inventory data.
/// </summary>
public sealed class GitHubReleaseSource(HttpClient http, string repository, IAppInfo app) : IReleaseSource
{
    /// <summary>Where Accession is published.</summary>
    public const string Repository = "plogramer/Accession";

    public async Task<ReleaseInfo?> LatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Accession", app.Version)); // GitHub requires a User-Agent
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // no release published yet
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(json);
    }

    /// <summary>A release from the API's JSON; null when its tag is not a version (e.g. "latest-build").</summary>
    public static ReleaseInfo? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
        var tag = Text("tag_name");
        if (AppVersion.Parse(tag) is not { } version)
        {
            return null;
        }

        DateTimeOffset? published = DateTimeOffset.TryParse(Text("published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;
        var title = Text("name");
        return new ReleaseInfo(version, tag, title.Length > 0 ? title : tag, Text("body").ReplaceLineEndings("\n").Trim(), Text("html_url"), published);
    }
}

/// <summary>Version numbers as used by Accession: "0.1", "0.1.0", "v0.2" (the build and revision parts default to 0).</summary>
public static class AppVersion
{
    public static Version? Parse(string? text)
    {
        var value = (text ?? string.Empty).Trim().TrimStart('v', 'V').Split('+', '-')[0];
        return Version.TryParse(value, out var version) ? Normalize(version) : null;
    }

    /// <summary>0.1 and 0.1.0 are the same version.</summary>
    public static Version Normalize(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    /// <summary>"0.2" or "0.2.1" (trailing zero parts left out).</summary>
    public static string Display(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version.Revision > 0 ? version.ToString(4) : version.Build > 0 ? version.ToString(3) : version.ToString(2);
    }
}

public enum UpdateStatus
{
    /// <summary>The automatic check is off, or ran less than a day ago.</summary>
    NotChecked,
    UpToDate,
    Available,

    /// <summary>A newer version, which the user chose to skip (automatic checks only).</summary>
    Skipped,

    /// <summary>GitHub could not be reached (offline, proxy, rate limit). Automatic checks stay silent.</summary>
    Failed,
}

public sealed record UpdateCheckResult(UpdateStatus Status, Version Current, ReleaseInfo? Release = null, string? Error = null);

/// <summary>
/// Compares the latest release with this version. Automatic checks run at most once a day and only when "Check for new
/// versions" is on; a version the user skipped is not announced again. A manual check always asks.
/// </summary>
public sealed class UpdateChecker(IReleaseSource source, ISettingsService settings, IAppInfo app, TimeProvider time)
{
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromDays(1);

    public Version Current => AppVersion.Parse(app.Version) ?? new Version(0, 0, 0, 0);

    public async Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var current = settings.Current;
        if (!manual && (!current.CheckForUpdates || current.LastUpdateCheckUtc is { } last && now - last < AutomaticInterval))
        {
            return new UpdateCheckResult(UpdateStatus.NotChecked, Current);
        }

        ReleaseInfo? latest;
        try
        {
            latest = await source.LatestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new UpdateCheckResult(UpdateStatus.Failed, Current, Error: ex.Message);
        }

        settings.Update(s => s.LastUpdateCheckUtc = now);
        if (latest is null || latest.Version <= Current)
        {
            return new UpdateCheckResult(UpdateStatus.UpToDate, Current, latest);
        }

        return !manual && string.Equals(current.SkippedUpdateVersion, latest.Tag, StringComparison.OrdinalIgnoreCase)
            ? new UpdateCheckResult(UpdateStatus.Skipped, Current, latest)
            : new UpdateCheckResult(UpdateStatus.Available, Current, latest);
    }

    /// <summary>Do not announce this version again (a later one is announced).</summary>
    public void Skip(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        settings.Update(s => s.SkippedUpdateVersion = release.Tag);
    }
}
