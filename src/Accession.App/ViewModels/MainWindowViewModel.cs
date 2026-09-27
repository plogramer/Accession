using System.Windows;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.Core.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    public MainWindowViewModel(INavigationService navigation, BusyTracker busy)
    {
        _navigation = navigation;
        Busy = busy;
        _navigation.CurrentChanged += (_, _) => OnPropertyChanged(nameof(CurrentScreen));
    }

    public string Title => "Accession";

    public ViewModelBase? CurrentScreen => _navigation.Current;

    public BusyTracker Busy { get; }

#if DEBUG
    public bool IsDebugBuild => true;
#else
    public bool IsDebugBuild => false;
#endif

    public void Initialize() => _navigation.NavigateTo<HomeViewModel>();

    [RelayCommand]
    private void CancelBusy() => Busy.Cancel();

    [RelayCommand]
    private static void Exit() => Application.Current.MainWindow?.Close();

    /// <summary>Debug-only menu item to verify global error handling.</summary>
    [RelayCommand]
    private static void ThrowTestException() =>
        throw new InvalidOperationException("Test exception from the Help menu (debug builds only).");
}
