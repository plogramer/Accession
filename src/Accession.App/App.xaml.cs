using System.IO;
using System.Windows;
using System.Windows.Threading;
using Accession.App.Platform;
using Accession.App.Services;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels;
using Accession.Core.Runtime;
using Accession.Core.Settings;
using Accession.Core.Threading;
using Accession.Presentation.ViewModels.MediaScreen;
using Accession.Presentation.ViewModels.Shell;
using Accession.Data.MediaManagement;
using Accession.Data.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Accession.App;

public partial class App : Application
{
    private IHost? _host;

    /// <summary>The application's service provider; used by views that cannot get it by injection (the BlazorWebView).</summary>
    internal static IServiceProvider Services { get; private set; } = default!;
    private bool _showingError;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureLogging();
        RegisterGlobalExceptionHandlers();

        try
        {
            // WebView2 keeps its profile next to the executable by default, which is read-only under Program Files.
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(AppPaths.LocalDataFolder, "WebView2"));

            _host = BuildHost();
            Services = _host.Services;
            await _host.StartAsync();

            Log.Information("Accession {Version} started by {User} on {Machine}",
                typeof(App).Assembly.GetName().Version,
                _host.Services.GetRequiredService<IUserContext>().UserName,
                Environment.MachineName);

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            _host.Services.GetRequiredService<MainWindowViewModel>().Initialize();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            MessageBox.Show($"Accession could not start.\n\n{ex.Message}\n\nSee the log in {AppPaths.LogFolder}.",
                "Accession", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Synchronous on purpose: the process ends when OnExit returns, so logs must be flushed first.
        try
        {
            // Normally closed by the main window; this covers other shutdown paths.
            if (_host?.Services.GetService<ScanHost>() is { } scans)
            {
                Task.Run(scans.ShutdownAsync).Wait(TimeSpan.FromSeconds(30));
            }

            _host?.Services.GetService<InventoryHost>()?.Close();

            if (_host is not null)
            {
                Task.Run(() => _host.StopAsync(TimeSpan.FromSeconds(5))).Wait();
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while stopping the application host");
        }
        finally
        {
            Log.Information("Accession exited with code {ExitCode}", e.ApplicationExitCode);
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        // Core
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IUserContext, EnvironmentUserContext>();
        builder.Services.AddSingleton<IAppInfo>(new AssemblyAppInfo(typeof(App).Assembly));
        builder.Services.AddSingleton<ISettingsService>(sp => new JsonSettingsService(
            AppPaths.SettingsFile,
            sp.GetRequiredService<ILogger<JsonSettingsService>>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(sp => new BusyTracker(sp.GetRequiredService<TimeProvider>()));

        // Data
        builder.Services.AddSingleton<InventorySessionFactory>();
        builder.Services.AddSingleton<InventoryCreationService>();
        builder.Services.AddSingleton<InventoryOpenService>();
        builder.Services.AddSingleton<RootPathService>();
        builder.Services.AddSingleton<InventoryPropertiesService>();
        builder.Services.AddSingleton<MediaDiscoveryService>();
        builder.Services.AddSingleton<MediaService>();
        builder.Services.AddSingleton<Accession.Data.Browsing.FileBrowserQueries>();
        builder.Services.AddSingleton<Accession.Data.Browsing.CategoryQueries>();
        builder.Services.AddSingleton(sp => new Accession.Data.Queries.DashboardQueries(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppPaths.AppFolderName, "Queries", "Dashboard"),
            sp.GetRequiredService<ILogger<Accession.Data.Queries.DashboardQueries>>()));

        // Platform
        builder.Services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        builder.Services.AddSingleton<IDesktop, WindowsDesktop>();

        // UI services
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IWindowPlacementService, WindowPlacementService>();
        builder.Services.AddSingleton<InventoryHost>();
        builder.Services.AddSingleton<IOpenInteraction, WpfOpenInteraction>();
        builder.Services.AddSingleton<InventoryWorkflows>();
        builder.Services.AddSingleton<MediaWorkflows>();
        builder.Services.AddSingleton<ScanHost>();
        builder.Services.AddSingleton<FileBrowserNavigator>();

        // Web UI (preview)
        builder.Services.AddWpfBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        // View models and windows
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddTransient<StartViewModel>();
        builder.Services.AddTransient<InventoryShellViewModel>();
        builder.Services.AddTransient<WebShellViewModel>();
        builder.Services.AddTransient<MediaListViewModel>();
        builder.Services.AddTransient<Accession.Presentation.ViewModels.Dashboard.DashboardViewModel>();
        builder.Services.AddTransient<Accession.Presentation.ViewModels.Browsing.FileBrowserViewModel>();
        builder.Services.AddTransient<Accession.Presentation.ViewModels.Browsing.CategoriesViewModel>();
        builder.Services.AddTransient<Accession.Presentation.ViewModels.Browsing.AuditLogViewModel>();
        builder.Services.AddTransient<Accession.Presentation.ViewModels.Scanning.ScanQueueViewModel>();
        builder.Services.AddTransient<Accession.Presentation.ViewModels.Scanning.ErrorsViewModel>();
        AddDialog<NewInventoryViewModel>(builder.Services);
        AddDialog<SettingsViewModel>(builder.Services);
        AddDialog<InventoryPropertiesViewModel>(builder.Services);
        AddDialog<ChangeRootPathViewModel>(builder.Services);
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    /// <summary>Registers a transient dialog view model and a Func factory for it.</summary>
    private static void AddDialog<TViewModel>(IServiceCollection services) where TViewModel : class
    {
        services.AddTransient<TViewModel>();
        services.AddSingleton<Func<TViewModel>>(sp => sp.GetRequiredService<TViewModel>);
    }

    private static void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppPaths.LogFolder, "accession-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating})", args.IsTerminating);
            Log.CloseAndFlush();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on the UI thread");
        e.Handled = true;

        // Guard against an error raised while showing the error dialog.
        if (_showingError)
        {
            return;
        }

        _showingError = true;
        try
        {
            var message = "An unexpected error occurred. You can continue working, but if the problem persists please restart Accession.";
            var dialogs = _host?.Services.GetService<IDialogService>();
            if (dialogs is not null)
            {
                dialogs.ShowError("Unexpected error", message, e.Exception);
            }
            else
            {
                MessageBox.Show($"{message}\n\n{e.Exception.Message}", "Accession", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _showingError = false;
        }
    }
}
