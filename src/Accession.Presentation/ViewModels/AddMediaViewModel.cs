using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Accession.Core.Inventories;
using Accession.Data.MediaManagement;
using Accession.Data.Sessions;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels;

/// <summary>Add Media / New Media Found dialog (requirements MED-01, MED-02, DSC-02, section 8.7).</summary>
public sealed partial class AddMediaViewModel : DialogViewModelBase
{
    private readonly InventorySession _session;
    private readonly MediaService _media;
    private readonly IDialogService _dialogs;

    public AddMediaViewModel(string title, InventorySession session, IEnumerable<DiscoveredFolder> folders, MediaService media, IDialogService dialogs, bool canScan)
    {
        CanScan = canScan;
        StartScanning = canScan;
        Title = title;
        _session = session;
        _media = media;
        _dialogs = dialogs;
        RootPath = session.Config.RootPath;
        IsNewMediaFound = title == "New Media Found";
        foreach (var folder in folders)
        {
            AddCandidate(new MediaCandidate(folder.MediaId, folder.FullPath, folder.PreviouslyDeleted, isChecked: !folder.PreviouslyDeleted));
        }
    }

    public string RootPath { get; }

    public bool CanScan { get; }

    [ObservableProperty]
    public partial bool StartScanning { get; set; }

    /// <summary>Media added when the dialog closed with OK.</summary>
    public IReadOnlyList<Accession.Core.Model.Media> Added { get; private set; } = [];

    public bool IsNewMediaFound { get; }

    public string CancelText => IsNewMediaFound ? "Not now" : "Cancel";

    public ObservableCollection<MediaCandidate> Candidates { get; } = [];

    public bool HasCandidates => Candidates.Count > 0;

    public int CheckedCount => Candidates.Count(c => c.IsChecked);

    [RelayCommand]
    private void Browse()
    {
        var folder = _dialogs.PickFolder("Select a media folder (directly inside the root folder)", RootPath);
        if (folder is null)
        {
            return;
        }

        var full = PathRules.NormalizeDirectory(folder);
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !PathRules.AreSameDirectory(parent, RootPath))
        {
            _dialogs.ShowWarning("Add media", $"Media folders must be directly inside the root folder:\n\n{RootPath}");
            return;
        }

        var existing = Candidates.FirstOrDefault(c => string.Equals(c.FullPath, full, PathRules.Comparison));
        if (existing is not null)
        {
            existing.IsChecked = true;
            return;
        }

        AddCandidate(new MediaCandidate(Path.GetFileName(full), full, previouslyDeleted: false, isChecked: true));
        OnPropertyChanged(nameof(HasCandidates));
    }

    /// <summary>Adds in the background: the insert can wait for a running scan's database batch.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task Add()
    {
        var paths = Candidates.Where(c => c.IsChecked).Select(c => c.FullPath).ToList();
        AddMediaResult result;
        try
        {
            result = await Task.Run(() => _media.Add(_session, paths));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            _dialogs.ShowError("Add media", $"The media could not be added.\n\n{ex.Message}", ex);
            return;
        }

        if (result.Rejected.Count > 0)
        {
            _dialogs.ShowWarning("Add media",
                (result.Added.Count > 0 ? $"{result.Added.Count:N0} media added. " : string.Empty) +
                $"{result.Rejected.Count:N0} folder(s) were not added:\n\n" +
                string.Join(Environment.NewLine, result.Rejected.Select(r => $"• {Path.GetFileName(r.Path.TrimEnd('\\', '/'))}: {r.Reason}")));
        }

        if (result.Added.Count > 0)
        {
            Added = result.Added;
            Close(true);
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private bool CanAdd() => CheckedCount > 0;

    private void AddCandidate(MediaCandidate candidate)
    {
        candidate.PropertyChanged += OnCandidateChanged;
        Candidates.Add(candidate);
        OnPropertyChanged(nameof(CheckedCount));
        AddCommand.NotifyCanExecuteChanged();
    }

    private void OnCandidateChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CheckedCount));
        AddCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class MediaCandidate : ObservableObject
{
    public MediaCandidate(string mediaId, string fullPath, bool previouslyDeleted, bool isChecked)
    {
        MediaId = mediaId;
        FullPath = fullPath;
        PreviouslyDeleted = previouslyDeleted;
        IsChecked = isChecked;
    }

    public string MediaId { get; }

    public string FullPath { get; }

    public bool PreviouslyDeleted { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
