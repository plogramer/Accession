using System.ComponentModel;
using System.Windows.Input;
using Accession.Core.Settings;
using Accession.Core.Threading;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Shell;
using Accession.UI.App;
using CommunityToolkit.Mvvm.Input;
using Accession.Presentation.Platform;
using Accession.UI.Components;

namespace Accession.Presentation.ViewModels;

/// <summary>
/// The web UI's page: the Start screen, or the inventory shell while an inventory is open,
/// plus theme, busy overlay, notifications and dialogs. One web view serves both screens.
/// </summary>
public sealed class WebAppViewModel : ViewModelBase, IAppModel
{
    private readonly InventoryHost _host;
    private readonly ISettingsService _settings;
    private readonly BusyTracker _busy;
    private readonly MainWindowViewModel _main;
    private readonly Func<StartViewModel> _startFactory;
    private readonly Func<WebShellViewModel> _shellFactory;
    private ViewModelBase? _screen;
    private readonly UpdateService? _updates;
    private static readonly ICommand Nothing = new RelayCommand(() => { }, () => false);

    public WebAppViewModel(
        InventoryHost host,
        ISettingsService settings,
        BusyTracker busy,
        MainWindowViewModel main,
        ToastService toasts,
        DialogCenter dialogs,
        Func<StartViewModel> startFactory,
        Func<WebShellViewModel> shellFactory,
        IDesktop desktop,
        UpdateService? updates = null)
    {
        _updates = updates;
        if (updates is not null)
        {
            updates.PropertyChanged += (_, _) => OnPropertyChanged(nameof(UpdateText));
        }

        HelpCommand = new RelayCommand<string?>(topic => desktop.ShowHelp(topic ?? HelpTopics.Contents));
        _host = host;
        _settings = settings;
        _busy = busy;
        _main = main;
        Toasts = toasts;
        Dialogs = dialogs;
        _startFactory = startFactory;
        _shellFactory = shellFactory;
    }

    public object Screen => _screen ?? throw new InvalidOperationException("The web page has not been shown yet.");

    public string Theme
    {
        get => _settings.Current.WebTheme;
        set
        {
            if (value != Theme)
            {
                _settings.Update(s => s.WebTheme = value);
                OnPropertyChanged();
            }
        }
    }

    public ToastService Toasts { get; }

    public DialogCenter Dialogs { get; }

    public bool IsBusy => _busy.IsBusy;

    public string BusyMessage => _busy.Message ?? string.Empty;

    public bool CanCancelBusy => _busy.CanCancel;

    public ICommand CancelBusyCommand => _main.CancelBusyCommand;

    public ICommand NewInventoryCommand => _main.NewInventoryCommand;

    public ICommand OpenInventoryCommand => _main.OpenInventoryCommand;

    public ICommand OpenSettingsCommand => _main.OpenSettingsCommand;

    public ICommand HelpCommand { get; }

    public string UpdateText => _updates?.BannerText ?? string.Empty;

    public ICommand ShowUpdateCommand => _updates?.ShowDetailsCommand ?? Nothing;

    public ICommand DownloadUpdateCommand => _updates?.DownloadCommand ?? Nothing;

    public ICommand SkipUpdateCommand => _updates?.SkipCommand ?? Nothing;

    public ICommand DismissUpdateCommand => _updates?.DismissCommand ?? Nothing;

    public ICommand CheckForUpdatesCommand => _updates?.CheckNowCommand ?? Nothing;

    /// <summary>The page rendered at least once (the web view works).</summary>
    public bool IsPageRendered { get; private set; }

    public void PageRendered()
    {
        IsPageRendered = true;
        _updates?.StartAutomaticCheck(); // once, a few seconds after the app is up
    }

    public override void OnNavigatedTo()
    {
        _host.SessionChanged += OnSessionChanged;
        _busy.PropertyChanged += OnBusyChanged;
        ShowScreenForSession();
    }

    public override void OnNavigatedFrom()
    {
        _host.SessionChanged -= OnSessionChanged;
        _busy.PropertyChanged -= OnBusyChanged;
        Dialogs.CancelAll();
        SetScreen(null);
    }

    private void OnSessionChanged(object? sender, EventArgs e) => ShowScreenForSession();

    private void OnBusyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(BusyMessage));
        OnPropertyChanged(nameof(CanCancelBusy));
    }

    private void ShowScreenForSession()
    {
        if (_host.HasSession && _screen is not WebShellViewModel)
        {
            SetScreen(_shellFactory());
        }
        else if (!_host.HasSession && _screen is not StartViewModel)
        {
            SetScreen(_startFactory());
        }
    }

    private void SetScreen(ViewModelBase? screen)
    {
        _screen?.OnNavigatedFrom();
        _screen = screen;
        screen?.OnNavigatedTo();
        OnPropertyChanged(nameof(Screen));
    }
}
