using System.Collections.ObjectModel;
using System.Windows.Input;
using Accession.UI.App;
using Accession.UI.Components;
using Accession.UI.Dashboard;
using Accession.UI.FilesScreen;
using Accession.UI.MediaScreen;
using Accession.UI.Shell;
using Accession.UI.Start;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Tests.WebUi;

/// <summary>Sample dashboard data for rendering the web UI without a database.</summary>
internal sealed class FakeDashboard : ObservableObject, IDashboardModel
{
    /// <param name="scanInProgress">Show a running scan.</param>
    /// <param name="unselected">Media keys (1–5) left out of the media selection.</param>
    public FakeDashboard(bool scanInProgress = false, params long[] unselected)
    {
        ScanInProgress = scanInProgress;
        string[] ids = ["123-123_001", "123-123_002", "123-123_003", "123-124_001", "123-125_001"];
        foreach (var (id, i) in ids.Select((id, i) => (id, i)))
        {
            MediaFilter.Add(new MediaFilterItem(i + 1, id, !unselected.Contains(i + 1)));
        }

        ByMedia.Add(Media(1, "123-123_001", "Completed", "4,812", "412,390", "1.84 TB", 1, "0", "2026-09-14 16:02", "1,204"));
        ByMedia.Add(Media(2, "123-123_002", "Completed with errors", "2,107", "198,455", "912.6 GB", 1, "37", "2026-09-15 09:41", "322"));
        ByMedia.Add(Media(3, "123-123_003", scanInProgress ? "Hashing" : "Completed", "1,388", "96,020", "310.2 GB", scanInProgress ? .42 : 1, "0", scanInProgress ? "running" : "2026-09-20 11:18", scanInProgress ? "—" : "88"));
        ByMedia.Add(Media(4, "123-124_001", "Missing", "655", "31,774", "54.9 GB", 1, "2", "2026-09-02 13:30", "12"));
        ByMedia.Add(Media(5, "123-125_001", "New", "—", "—", "—", 0, "—", "never", "—"));

        (string Name, long Files, double Tb)[] categories =
        [
            ("Email", 211_430, 0.62), ("Documents", 168_902, 0.41), ("Spreadsheets", 64_118, 0.19), ("Images", 122_604, 0.33),
            ("Video", 3_204, 0.71), ("Archives", 9_877, 0.28), ("PDF", 71_220, 0.16), ("Databases", 412, 0.09), ("No extension", 4_872, 0.02),
        ];
        var maxTb = categories.Max(c => c.Tb);
        var totalTb = categories.Sum(c => c.Tb);
        foreach (var c in categories.OrderByDescending(c => c.Tb))
        {
            ByCategory.Add(new BarRow(c.Name, c.Files.ToString("N0"), $"{c.Tb * 1000:0.0} GB", string.Empty,
                $"{100 * c.Tb / totalTb:0.0} %", DashboardScale.MaxBarLength * c.Tb / maxTb, c.Files, (long)(c.Tb * 1e12)));
        }

        (string Ext, string Category, long Files, double Gb)[] extensions =
        [
            ("msg", "Email", 180_221, 512.4), ("mp4", "Video", 2_811, 688.1), ("pst", "Email", 64, 102.7), ("docx", "Documents", 98_331, 211.9),
            ("jpg", "Images", 101_455, 280.3), ("xlsx", "Spreadsheets", 52_904, 160.2), ("pdf", "PDF", 71_220, 158.8), ("zip", "Archives", 8_120, 240.6),
            ("pptx", "Documents", 12_064, 120.5), ("png", "Images", 21_149, 49.9), ("(none)", "No extension", 4_872, 20.3),
        ];
        foreach (var e in extensions.OrderByDescending(e => e.Gb))
        {
            ByExtension.Add(new ExtensionRowVm(e.Ext, e.Category, e.Files.ToString("N0"), $"{e.Gb:0.0} GB", $"{100 * e.Gb / 3120:0.0} %", e.Files, (long)(e.Gb * 1e9)));
        }

        if (!scanInProgress)
        {
            (string Year, long Files)[] years =
            [
                ("2012", 3_120), ("2013", 6_844), ("2014", 12_410), ("2015", 20_918), ("2016", 31_207), ("2017", 44_860), ("2018", 61_322),
                ("2019", 88_140), ("2020", 121_774), ("2021", 139_502), ("2022", 118_960), ("2023", 57_205), ("2024", 18_370),
            ];
            var maxYear = years.Max(y => y.Files);
            foreach (var y in years)
            {
                ByYear.Add(new BarRow(y.Year, y.Files.ToString("N0"), string.Empty, string.Empty, string.Empty,
                    DashboardScale.MaxBarLength * y.Files / maxYear, y.Files, 0));
            }

            LargestFiles.Add(new LargeFileRowVm(1, "123-123_001", @"\Users\jsmith\Mail\archive_2019-2021.pst", "48.2 GB", "2021-12-30 17:44", 48_200_000_000));
            LargestFiles.Add(new LargeFileRowVm(2, "123-123_002", @"\Shares\Finance\Backups\ledger_full.bak", "31.7 GB", "2022-03-01 02:10", 31_700_000_000));
            LargestFiles.Add(new LargeFileRowVm(1, "123-123_001", @"\Users\jsmith\Videos\board_meeting_2020-06.mp4", "12.9 GB", "2020-06-18 11:05", 12_900_000_000));
            LargestFiles.Add(new LargeFileRowVm(3, "123-123_003", @"\Engineering\CAD\plant_layout_v14.zip", "9.4 GB", "2023-01-09 08:31", 9_400_000_000));
            LargestFiles.Add(new LargeFileRowVm(4, "123-124_001", @"\Laptop\Documents\Q4 export.xlsx", "2.1 GB", "2022-10-27 19:52", 2_100_000_000));
        }
    }

    public ObservableCollection<MediaFilterItem> MediaFilter { get; } = [];
    public string FilterText => AllMediaSelected ? "All media" : NoMediaSelected ? "No media selected" : $"{MediaFilter.Count(m => m.IsChecked)} of 5 media";
    public string MediaSearch { get; set; } = string.Empty;
    public IReadOnlyList<MediaFilterItem> VisibleMediaFilter => [.. MediaFilter.Where(m => m.MediaId.Contains(MediaSearch, StringComparison.OrdinalIgnoreCase))];
    public string SelectionSummary => $"{MediaFilter.Count(m => m.IsChecked)} of {MediaFilter.Count} selected";
    public bool AllMediaSelected => MediaFilter.All(m => m.IsChecked);
    public bool NoMediaSelected => MediaFilter.All(m => !m.IsChecked);
    public string MediaNote => AllMediaSelected ? "in this inventory" : "selected of 5";
    public bool IsLoading => false;
    public bool IsLoadingDetails => false;
    public bool ScanInProgress { get; }
    public string LoadError => string.Empty;
    public string MediaCount => "5";
    public string FolderCount => "8,962";
    public string FileCount => "738,639";
    public string TotalSize => "3.12 TB";
    public string HashedPercent => ScanInProgress ? "92.4 %" : "100.0 %";
    public string UniqueFiles => ScanInProgress ? "—" : "712,013";
    public string DuplicateFiles => ScanInProgress ? "—" : "26,626";
    public string DuplicateSize => ScanInProgress ? string.Empty : "184.3 GB";
    public string DuplicateNote => ScanInProgress ? "available when the scan finishes" : "by SHA-1";
    public string ErrorCount => "39";
    public ObservableCollection<DashboardMediaRowVm> ByMedia { get; } = [];
    public ObservableCollection<BarRow> ByCategory { get; } = [];
    public BarRow? SelectedCategory { get; set; }
    public ObservableCollection<ExtensionRowVm> ByExtension { get; } = [];
    public string ExtensionSearch { get; set; } = string.Empty;
    public string ExtensionHeader => SelectedCategory is { } c ? $"By extension – {c.Label}" : "By extension";
    public ObservableCollection<BarRow> ByYear { get; } = [];
    public ObservableCollection<LargeFileRowVm> LargestFiles { get; } = [];
    public ICommand RefreshCommand { get; init; } = new RelayCommand(() => { });
    public ICommand SelectAllMediaCommand { get; } = new RelayCommand(() => { });
    public ICommand UnselectAllMediaCommand { get; } = new RelayCommand(() => { });
    public ICommand SelectOnlyMediaCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand ToggleAllMediaCommand { get; } = new RelayCommand(() => { });
    public ICommand ClearCategoryCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenMediaCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand OpenCategoryCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand OpenExtensionCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand OpenYearCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand OpenLargeFileCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand OpenDuplicatesCommand { get; } = new RelayCommand(() => { });

    private DashboardMediaRowVm Media(long key, string id, string status, string folders, string files, string size,
        double hashed, string errors, string lastScanned, string duplicates) => new()
    {
        Selection = MediaFilter.First(m => m.MediaKey == key),
        MediaKey = key,
        MediaId = id,
        Status = status,
        Folders = folders,
        Files = files,
        Size = size,
        Hashed = hashed == 0 && files == "—" ? "—" : $"{Math.Floor(hashed * 100):0} %",
        Errors = errors,
        LastScanned = lastScanned,
        Scans = 1,
        FileCount = 0,
        TotalBytes = 0,
        HashedRatio = hashed,
        Duplicates = duplicates,
    };
}

internal sealed partial class FakeShell : ObservableObject, IShellModel
{
    public FakeShell(FakeDashboard dashboard, bool scanning = false, bool readOnly = false, IMediaModel? media = null,
        IFilesModel? files = null, IReadOnlyDictionary<string, object>? screens = null)
    {
        // Screens a test doesn't give a model get a placeholder, which ScreenHost shows as "not available".
        object Screen(string key) => screens is not null && screens.TryGetValue(key, out var model) ? model : new object();
        IsScanActive = scanning;
        IsReadOnly = readOnly;
        NavItems =
        [
            new ShellNavItem("Dashboard", "dashboard", "Overview", dashboard),
            new ShellNavItem("Media", "media", "Inventory", media ?? Screen("Media")) { Badge = media is null ? string.Empty : "5" },
            new ShellNavItem("Files", "files", "Inventory", files ?? Screen("Files")),
            new ShellNavItem("Categories", "categories", "Inventory", Screen("Categories")),
            new ShellNavItem("Scan Queue", "queue", "Scanning", Screen("Scan Queue")) { Badge = scanning ? "2" : string.Empty },
            new ShellNavItem("Errors", "errors", "Scanning", Screen("Errors")) { Badge = "39" },
            new ShellNavItem("Audit Log", "audit", "Records", Screen("Audit Log")),
        ];
        SelectedItem = NavItems[0];
    }

    public string ClientName => "Northwind Holdings";
    public string MatterName => "Northwind v. Contoso Ltd.";
    public string MatterCode => "NW-2026-0142";
    public string MatterUrl { get; init; } = "https://dms.northwind.example/matters/NW-2026-0142";
    public string UserName => @"LITSUPPORT\jane.doe";
    public string RootPath => @"\\evidence01\intake\NW-2026-0142";
    public string SchemaText => "Schema v1";
    public string ReadOnlyText => "Read-only · john.roe on LIT-PC12";
    public string ReadOnlyTooltip => @"LITSUPPORT\john.roe has this inventory open for editing on LIT-PC12.";
    public bool IsReadOnly { get; }
    public bool IsOffline => false;
    public string Notice => string.Empty;
    public string ScanStatus => IsScanActive ? "123-123_003 · Hashing 41,210 of 96,020 files · 212 MB/s" : string.Empty;
    public bool IsScanActive { get; }
    public IReadOnlyList<ShellNavItem> NavItems { get; }

    [ObservableProperty]
    public partial ShellNavItem SelectedItem { get; set; }

    public ICommand ScanNowCommand { get; } = new RelayCommand(() => { });
    public ICommand PauseScanCommand { get; } = new RelayCommand(() => { });
    public ICommand ResumeScanCommand { get; } = new RelayCommand(() => { }, () => false);
    public ICommand CancelScanCommand { get; } = new RelayCommand(() => { });
    public ICommand AddMediaCommand { get; } = new RelayCommand(() => { });
    public ICommand DiscoverMediaCommand { get; } = new RelayCommand(() => { });
    public ICommand ShowPropertiesCommand { get; } = new RelayCommand(() => { });
    public ICommand ChangeRootPathCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenMatterLinkCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenSettingsCommand { get; } = new RelayCommand(() => { });
    public ICommand CloseInventoryCommand { get; } = new RelayCommand(() => { });
    public ICommand ExportCommand { get; } = new RelayCommand(() => { });
    public ICommand DismissNoticeCommand { get; } = new RelayCommand(() => { });
}

/// <summary>The whole page: a screen plus theme, busy overlay, toasts and dialogs.</summary>
internal sealed partial class FakeApp(object screen) : ObservableObject, IAppModel
{
    public object Screen { get; } = screen;
    public string Theme { get; set; } = "light";
    public ToastService Toasts { get; } = new(TimeProvider.System);
    public DialogCenter Dialogs { get; } = new();
    public bool IsBusy { get; set; }
    public string BusyMessage { get; set; } = string.Empty;
    public bool CanCancelBusy => false;
    public ICommand CancelBusyCommand { get; } = new RelayCommand(() => { });
    public ICommand NewInventoryCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenInventoryCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenSettingsCommand { get; } = new RelayCommand(() => { });

    public void PageRendered()
    {
    }
}

internal sealed class FakeStart : ObservableObject, IStartModel
{
    public FakeStart(bool empty = false)
    {
        if (!empty)
        {
            RecentInventories.Add(new RecentInventoryItem("Northwind v. Contoso Ltd. (NW-2026-0142)", @"\\evidence01\intake\NW-2026-0142\NW-2026-0142.accession", "2026-09-27 14:10", true));
            RecentInventories.Add(new RecentInventoryItem("Fabrikam Arbitration (FB-2026-0077)", @"\\evidence01\intake\FB-2026-0077\FB-2026-0077.accession", "2026-09-22 09:31", true));
            RecentInventories.Add(new RecentInventoryItem("Adatum Internal Review (AD-2025-0311)", @"D:\Cases\AD-2025-0311\AD-2025-0311.accession", "2026-08-30 16:48", false));
        }
    }

    public string AppName => "Accession";
    public string Tagline => "Inventory, hash, and report every media you receive.";
    public string Version => "0.1.0";
    public ObservableCollection<RecentInventoryItem> RecentInventories { get; } = [];
    public ICommand NewInventoryCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenInventoryCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenRecentCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand RemoveRecentCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand OpenSettingsCommand { get; } = new RelayCommand(() => { });
}
