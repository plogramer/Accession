using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.Presentation.Mvvm;

/// <summary>Base class for all view models. Supports data-annotation validation (INotifyDataErrorInfo).</summary>
public abstract class ViewModelBase : ObservableValidator
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
