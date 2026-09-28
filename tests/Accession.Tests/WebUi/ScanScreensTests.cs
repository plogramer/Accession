using System.Collections.ObjectModel;
using System.Windows.Input;
using Accession.UI.App;
using Accession.UI.FilesScreen;
using Accession.UI.ScanScreens;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Tests.WebUi;

public sealed class ScanScreensTests
{
    [Fact]
    public async Task Running_scan_shows_progress_stats_and_the_waiting_queue()
    {
        var html = await Render(new FakeQueue(running: true), "Scan Queue", "scan-queue");

        Assert.Contains("123-123_003  (full scan)", html);
        Assert.Contains("Hashing (listing done)", html);
        Assert.Contains("42 %", html);
        Assert.Contains("212 MB/s", html);
        Assert.Contains("38 % of 12.4 GB", html); // a large file being hashed shows its own progress
        Assert.Contains("123-124_001", html);
        Assert.Contains("title=\"Move up\" disabled", html); // first waiting item
        Assert.Contains("Pause", html);
    }

    [Fact]
    public async Task Paused_scan_explains_itself_and_offers_resume()
    {
        var html = await Render(new FakeQueue(running: true, paused: true), "Scan Queue");

        Assert.Contains("The scan is paused.", html);
        Assert.Contains("Resume", html);
    }

    [Fact]
    public async Task Idle_queue()
    {
        var html = await Render(new FakeQueue(running: false), "Scan Queue");

        Assert.Contains("No scan is running", html);
        Assert.Contains("No media are waiting.", html);
    }

    [Fact]
    public async Task Errors_page_lists_errors_with_filters_and_pager()
    {
        var html = await Render(new FakeErrors(), "Errors", "errors");

        Assert.Contains("File locked", html);
        Assert.Contains(@"\M1\locked\file0003.pst", html);
        Assert.Contains("All error types", html);
        Assert.Contains("of 1,250 errors", html);
        Assert.Contains("Copy path", html);
    }

    private static async Task<string> Render(object screen, string key, string? preview = null)
    {
        var shell = new FakeShell(new FakeDashboard(), screens: new Dictionary<string, object> { [key] = screen });
        shell.SelectedItem = shell.NavItems.First(n => n.Key == key);
        var html = await WebUiRenderer.RenderAsync<AppRoot>(new Dictionary<string, object?> { [nameof(AppRoot.Model)] = new FakeApp(shell) });
        if (preview is not null)
        {
            WebUiRenderTests.WritePreview(preview, html);
        }

        return html;
    }

    private sealed class FakeQueue : ObservableObject, IScanQueueModel
    {
        public FakeQueue(bool running, bool paused = false)
        {
            IsRunning = running;
            IsPaused = paused;
            if (running)
            {
                Queue.Add(new QueueRow(1, 4, "123-124_001", "full scan"));
                Queue.Add(new QueueRow(2, 5, "123-125_001", "full scan"));
                Queue.Add(new QueueRow(3, 2, "123-123_002", "retry failed files"));
            }
        }

        public bool IsRunning { get; }
        public bool IsPaused { get; }
        public string IdleText => "No scan is running. Select media on the Media screen and choose Scan.";
        public string CurrentMedia => "123-123_003  (full scan)";
        public string Phase => IsPaused ? "Paused" : "Hashing (listing done)";
        public string Counts => "1,066 folders · 96,020 files · 310.2 GB";
        public double Percent => 42;
        public bool IsIndeterminate => false;
        public string HashedText => "41,210 / 96,020 files";
        public string BytesText => "130.3 GB / 310.2 GB";
        public string RateText => "212 MB/s · 318 files/s";
        public string Elapsed => "00:10:14";
        public string Eta => "00:14:08";
        public string Errors => "0";
        public string Threads => "listing 4 / hashing 4";
        public string CurrentPath => @"\123-123_003\Engineering\CAD\plant_layout_v14.zip";
        public string CurrentFileProgress => IsRunning ? "38 % of 12.4 GB" : string.Empty;
        public ObservableCollection<QueueRow> Queue { get; } = [];
        public ICommand PauseCommand { get; } = new RelayCommand(() => { });
        public ICommand ResumeCommand { get; } = new RelayCommand(() => { });
        public ICommand CancelCommand { get; } = new RelayCommand(() => { });
        public ICommand MoveUpCommand { get; } = new RelayCommand<object?>(_ => { });
        public ICommand MoveDownCommand { get; } = new RelayCommand<object?>(_ => { });
        public ICommand RemoveCommand { get; } = new RelayCommand<object?>(_ => { });
    }

    private sealed class FakeErrors : ObservableObject, IErrorsModel
    {
        public FakeErrors()
        {
            PageRows = [.. Enumerable.Range(0, 12).Select(i => new ErrorRow(i + 1, 1, "2026-09-15 02:1" + (i % 10), i % 4 == 0 ? "123-123_002" : "123-123_001",
                $@"\M1\locked\file{i:0000}.pst", "File", i % 5 == 0 ? "Access denied" : "File locked", i % 7 == 6 ? "Warning" : "Error",
                i % 5 == 0 ? "5" : "32", i % 5 == 0 ? "Access to the path is denied." : "The process cannot access the file because it is being used by another process."))];
            SelectedRow = PageRows[3];
        }

        public IReadOnlyList<SelectOption> MediaFilterOptions { get; } = [new(string.Empty, "All media"), new("1", "123-123_001")];
        public string MediaFilterValue { get; set; } = string.Empty;
        public IReadOnlyList<SelectOption> ErrorTypeFilterOptions { get; } = [new(string.Empty, "All error types"), new("FileLocked", "File locked")];
        public string ErrorTypeFilterValue { get; set; } = string.Empty;
        public bool ShowInfo { get; set; }
        public IReadOnlyList<ErrorRow> PageRows { get; }
        public ErrorRow? SelectedRow { get; set; }
        public int PageIndex => 0;
        public int ErrorsPageSize => 500;
        public IReadOnlyList<int> PageSizes { get; } = [500, 1_000];
        public long TotalCount => 1_250;
        public Task GoToPageAsync(int pageIndex) => Task.CompletedTask;
        public Task SetPageSizeAsync(int pageSize) => Task.CompletedTask;
        public ICommand CopyPathCommand { get; } = new RelayCommand(() => { });
        public ICommand OpenContainingFolderCommand { get; } = new RelayCommand(() => { });
        public ICommand RetryFailedCommand { get; } = new RelayCommand(() => { });
        public ICommand RefreshCommand { get; } = new RelayCommand(() => { });
    }
}
