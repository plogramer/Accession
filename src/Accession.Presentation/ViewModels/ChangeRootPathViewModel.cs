using System.IO;
using Accession.Data.Sessions;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels;

/// <summary>Change Root Path dialog (requirement INV-09): preview media not found, then apply.</summary>
public sealed partial class ChangeRootPathViewModel : DialogViewModelBase
{
    private readonly InventoryHost _host;
    private readonly RootPathService _rootPaths;
    private readonly IDialogService _dialogs;
    private RootPathPreview? _preview;

    public ChangeRootPathViewModel(InventoryHost host, RootPathService rootPaths, IDialogService dialogs)
    {
        _host = host;
        _rootPaths = rootPaths;
        _dialogs = dialogs;
        Title = "Change Root Path";
        CurrentRootPath = host.Config?.RootPath ?? string.Empty;
    }

    public string CurrentRootPath { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial string NewRootPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PreviewText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> MediaNotFound { get; set; } = [];

    public bool HasMediaNotFound => MediaNotFound.Count > 0;

    partial void OnNewRootPathChanged(string value) => RefreshPreview();

    partial void OnMediaNotFoundChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasMediaNotFound));

    [RelayCommand]
    private void Browse()
    {
        var folder = _dialogs.PickFolder("Select the new root folder", string.IsNullOrWhiteSpace(NewRootPath) ? null : NewRootPath);
        if (folder is not null)
        {
            NewRootPath = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (_host.Session is not { } session || _preview is not { Exists: true } preview)
        {
            return;
        }

        if (preview.MediaNotFound.Count > 0 && !_dialogs.Confirm(
                "Change root path",
                $"{preview.MediaNotFound.Count:N0} registered media were not found under the new root:\n\n" +
                string.Join(Environment.NewLine, preview.MediaNotFound.Take(20)) +
                (preview.MediaNotFound.Count > 20 ? $"{Environment.NewLine}…" : string.Empty) +
                "\n\nChange the root path anyway?"))
        {
            return;
        }

        try
        {
            _rootPaths.Apply(session, NewRootPath);
            _host.Refresh();
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _dialogs.ShowError("Change root path", $"The root path could not be changed.\n\n{ex.Message}", ex);
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private bool CanApply() => _preview is { Exists: true };

    private void RefreshPreview()
    {
        _preview = null;
        MediaNotFound = [];
        if (string.IsNullOrWhiteSpace(NewRootPath) || _host.Session is not { } session)
        {
            PreviewText = string.Empty;
        }
        else
        {
            try
            {
                _preview = _rootPaths.Preview(session, NewRootPath);
                MediaNotFound = _preview.MediaNotFound;
                PreviewText = !_preview.Exists
                    ? "The folder does not exist or cannot be reached."
                    : _preview.MediaNotFound.Count == 0
                        ? "All registered media were found under this folder."
                        : $"{_preview.MediaNotFound.Count:N0} registered media were not found under this folder:";
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                PreviewText = "The path is not valid.";
            }
        }

        ApplyCommand.NotifyCanExecuteChanged();
    }
}
