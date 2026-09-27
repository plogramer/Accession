using Accession.Data.Sessions;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels;

/// <summary>Shown when the inventory's root folder cannot be reached at open (requirement DSC-06).</summary>
public sealed partial class RootUnreachableViewModel : DialogViewModelBase
{
    private readonly IDialogService _dialogs;

    public RootUnreachableViewModel(string rootPath, IDialogService dialogs)
    {
        _dialogs = dialogs;
        Title = "Root folder not available";
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public RootUnreachableResolution Resolution { get; private set; } = new(RootUnreachableChoice.Cancel);

    [RelayCommand]
    private void Retry() => Finish(new RootUnreachableResolution(RootUnreachableChoice.Retry));

    [RelayCommand]
    private void ChangeRootPath()
    {
        var folder = _dialogs.PickFolder("Select the new root folder that contains the media folders");
        if (folder is not null)
        {
            Finish(new RootUnreachableResolution(RootUnreachableChoice.ChangeRootPath, folder));
        }
    }

    [RelayCommand]
    private void ContinueOffline() => Finish(new RootUnreachableResolution(RootUnreachableChoice.ContinueOffline));

    [RelayCommand]
    private void Cancel()
    {
        Resolution = new RootUnreachableResolution(RootUnreachableChoice.Cancel);
        Close(false);
    }

    private void Finish(RootUnreachableResolution resolution)
    {
        Resolution = resolution;
        Close(true);
    }
}
