using Accession.Presentation.Mvvm;

namespace Accession.Presentation.Services;

/// <summary>Switches the main window's content between screens (view models).</summary>
public interface INavigationService
{
    ViewModelBase? Current { get; }

    event EventHandler? CurrentChanged;

    /// <summary>Resolves <typeparamref name="TViewModel"/> from DI and makes it the current screen.</summary>
    TViewModel NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
}
