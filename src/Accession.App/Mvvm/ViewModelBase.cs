using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.App.Mvvm;

/// <summary>Base class for all view models.</summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>Called when the navigation service makes this view model the current screen.</summary>
    public virtual void OnNavigatedTo()
    {
    }

    /// <summary>Called when the navigation service leaves this view model.</summary>
    public virtual void OnNavigatedFrom()
    {
    }
}
