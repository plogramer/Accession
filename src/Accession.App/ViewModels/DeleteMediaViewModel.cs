using Accession.App.Mvvm;
using Accession.Core.Formatting;
using Accession.Core.Model;
using Accession.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels;

/// <summary>Delete Media confirmation (requirement MED-05, section 8.8): the user types the Media ID.</summary>
public sealed partial class DeleteMediaViewModel : DialogViewModelBase
{
    public DeleteMediaViewModel(Media media, SizeUnitSystem sizeUnit)
    {
        Title = "Delete Media";
        MediaId = media.MediaId;
        Impact = media.ScanCount == 0 && media.FileCount == 0
            ? "It has not been scanned, so no file records will be removed."
            : $"This removes {media.FileCount:N0} file and {media.FolderCount:N0} folder records " +
              $"({SizeFormatter.Format(media.TotalBytes, sizeUnit)}) and their scan errors from this inventory.";
    }

    public string MediaId { get; }

    public string Impact { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial string ConfirmText { get; set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(IsConfirmed))]
    private void Delete() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);

    private bool IsConfirmed() => string.Equals(ConfirmText.Trim(), MediaId, StringComparison.Ordinal);
}
