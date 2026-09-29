using System.Net;
using Accession.Core.Runtime;
using Accession.Core.Updates;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels;
using Accession.Presentation.WebForms;
using Accession.Tests.TestSupport;
using Accession.Tests.WebUi;
using Accession.UI.App;
using Accession.UI.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Updates;

/// <summary>New versions from GitHub releases: reading the latest release, when to ask, skip, and what the user sees.</summary>
public sealed class UpdateCheckTests
{
    private const string LatestJson =
        """
        {
          "tag_name": "v0.2",
          "name": "Accession 0.2",
          "body": "- Copy To\r\n- Faster Dashboard",
          "html_url": "https://github.com/plogramer/Accession/releases/tag/v0.2",
          "published_at": "2026-10-15T09:30:00Z",
          "draft": false,
          "prerelease": false
        }
        """;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
    private readonly TestSettings _settings = new();

    private static ReleaseInfo Release(string tag) => new(AppVersion.Parse(tag)!, tag, "Accession " + tag, "notes", "https://example/" + tag, null);

    private UpdateChecker Checker(IReleaseSource source, string version = "0.1.0") => new(source, _settings, new App(version), _time);

    [Theory]
    [InlineData("v0.2", "0.2")]
    [InlineData("0.1", "0.1")]
    [InlineData("0.1.0", "0.1")]
    [InlineData("V1.2.3-beta", "1.2.3")]
    [InlineData("0.1.0+abc123", "0.1")]
    public void Versions_are_read_from_tags(string tag, string display)
    {
        Assert.Equal(display, AppVersion.Display(AppVersion.Parse(tag)!));
        Assert.Equal(AppVersion.Parse("0.1"), AppVersion.Parse("0.1.0")); // same version
        Assert.Null(AppVersion.Parse("latest-build"));
    }

    [Fact]
    public async Task The_latest_release_is_read_from_the_github_api()
    {
        var handler = new Handler(HttpStatusCode.OK, LatestJson);
        var source = new GitHubReleaseSource(new HttpClient(handler), "plogramer/Accession", new App("0.1.0"));

        var release = await source.LatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://api.github.com/repos/plogramer/Accession/releases/latest", handler.Request!.RequestUri!.ToString());
        Assert.Contains("Accession/0.1.0", handler.Request.Headers.UserAgent.ToString(), StringComparison.Ordinal); // GitHub requires one
        Assert.Equal((new Version(0, 2, 0, 0), "v0.2", "Accession 0.2"), (release!.Version, release.Tag, release.Title));
        Assert.Equal("- Copy To\n- Faster Dashboard", release.Notes);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero), release.PublishedAt);

        var none = new GitHubReleaseSource(new HttpClient(new Handler(HttpStatusCode.NotFound, "{}")), "plogramer/Accession", new App("0.1.0"));
        Assert.Null(await none.LatestAsync(TestContext.Current.CancellationToken)); // no release yet
    }

    [Fact]
    public async Task Automatic_checks_run_once_a_day_and_only_when_turned_on()
    {
        var source = new Source(Release("v0.2"));
        var checker = Checker(source);

        var first = await checker.CheckAsync(manual: false, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Available, first.Status);
        Assert.Equal(_time.GetUtcNow(), _settings.Current.LastUpdateCheckUtc);

        _time.Advance(TimeSpan.FromHours(20));
        Assert.Equal(UpdateStatus.NotChecked, (await checker.CheckAsync(manual: false, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(UpdateStatus.Available, (await checker.CheckAsync(manual: true, TestContext.Current.CancellationToken)).Status); // on request: always

        _time.Advance(TimeSpan.FromDays(2));
        _settings.Update(s => s.CheckForUpdates = false);
        Assert.Equal(UpdateStatus.NotChecked, (await checker.CheckAsync(manual: false, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Same_or_older_versions_are_up_to_date_and_skipped_versions_are_not_announced()
    {
        Assert.Equal(UpdateStatus.UpToDate, (await Checker(new Source(Release("v0.1"))).CheckAsync(true, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(UpdateStatus.UpToDate, (await Checker(new Source((ReleaseInfo?)null)).CheckAsync(true, TestContext.Current.CancellationToken)).Status);

        var checker = Checker(new Source(Release("v0.2")));
        _settings.Update(s => s.LastUpdateCheckUtc = null); // the checks above count as today's
        checker.Skip(Release("v0.2"));
        Assert.Equal(UpdateStatus.Skipped, (await checker.CheckAsync(false, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(UpdateStatus.Available, (await checker.CheckAsync(true, TestContext.Current.CancellationToken)).Status); // shown when asked

        _time.Advance(TimeSpan.FromDays(2));
        Assert.Equal(UpdateStatus.Available, (await Checker(new Source(Release("v0.3"))).CheckAsync(false, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task No_internet_fails_quietly_and_tries_again_next_time()
    {
        var checker = Checker(new Source(new HttpRequestException("No such host is known.")));

        var result = await checker.CheckAsync(manual: false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal("No such host is known.", result.Error);
        Assert.Null(_settings.Current.LastUpdateCheckUtc); // not counted as today's check
    }

    [Fact]
    public async Task The_banner_appears_after_start_up_and_skip_hides_it_for_that_version()
    {
        var desktop = new RecordingDesktop();
        var service = new UpdateService(Checker(new Source(Release("v0.2"))), new NoDialogs(), desktop, new ToastService(TimeProvider.System),
            _settings, new InlineUiDispatcher(), _time, NullLogger<UpdateService>.Instance);

        service.StartAutomaticCheck();
        Assert.Empty(service.BannerText); // waits a few seconds first
        _time.Advance(UpdateService.StartupDelay);
        await WaitUntil(() => service.BannerText.Length > 0);

        Assert.Equal("Accession 0.2 is available. You have 0.1.", service.BannerText);
        service.DownloadCommand.Execute(null);
        Assert.Equal(["https://example/v0.2"], desktop.Opened);

        service.SkipCommand.Execute(null);
        Assert.Empty(service.BannerText);
        Assert.Equal("v0.2", _settings.Current.SkippedUpdateVersion);
    }

    [Fact]
    public async Task The_banner_and_the_dialog_render()
    {
        var app = new FakeApp(new FakeStart()) { UpdateText = "Accession 0.2 is available. You have 0.1." };
        var html = await WebUiRenderer.RenderAsync<AppRoot>(new Dictionary<string, object?> { [nameof(AppRoot.Model)] = app });
        WebUiRenderTests.WritePreview("start-update", html);
        Assert.Contains("Accession 0.2 is available. You have 0.1.", html);
        Assert.Contains("What&#x27;s new", html);
        Assert.Contains("Skip this version", html);
        Assert.Contains("Check for updates", html);

        var release = Release("v0.2") with { Notes = "- Copy To: files to one folder as <sha1>_<name>\n- Faster Dashboard on large inventories", PublishedAt = new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero) };
        var available = new UpdateViewModel(new UpdateCheckResult(UpdateStatus.Available, new Version(0, 1, 0, 0), release), Accession.Core.Settings.DisplayTimeZone.Utc);
        var dialog = await FormDialogTests.Render(DialogForms.Build(available, () => { })!, "form-update");
        Assert.Contains("New version available", dialog);
        Assert.Contains("Faster Dashboard", dialog);
        Assert.Contains(">Download<", dialog);

        var upToDate = new UpdateViewModel(new UpdateCheckResult(UpdateStatus.UpToDate, new Version(0, 1, 0, 0), Release("v0.1")));
        dialog = await FormDialogTests.Render(DialogForms.Build(upToDate, () => { })!, "form-update-latest");
        Assert.Contains("You have the latest version.", dialog);
        Assert.DoesNotContain(">Download<", dialog);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    private sealed class App(string version) : IAppInfo
    {
        public string Version => version;
    }

    private sealed class Source : IReleaseSource
    {
        private readonly ReleaseInfo? _release;
        private readonly Exception? _error;

        public Source(ReleaseInfo? release) => _release = release;

        public Source(Exception error) => _error = error;

        public int Calls { get; private set; }

        public Task<ReleaseInfo?> LatestAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return _error is null ? Task.FromResult(_release) : Task.FromException<ReleaseInfo?>(_error);
        }
    }

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
