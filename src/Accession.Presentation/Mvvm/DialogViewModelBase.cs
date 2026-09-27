using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.Presentation.Mvvm;

/// <summary>Base class for view models shown in a modal dialog by <see cref="Services.IDialogService"/>.</summary>
public abstract partial class DialogViewModelBase : ViewModelBase
{
    [ObservableProperty]
    public partial string Title { get; set; } = "Accession";

    /// <summary>Whether the user can resize the dialog.</summary>
    public virtual bool CanResize => false;

    /// <summary>Raised when the view model wants its dialog closed, with the dialog result.</summary>
    public event EventHandler<bool?>? CloseRequested;

    protected void Close(bool? result) => CloseRequested?.Invoke(this, result);
}
