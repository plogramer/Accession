using System.IO;
using System.Windows;
using System.Windows.Threading;
using Accession.App.Services;
using Accession.App.ViewModels;
using Accession.Core.Runtime;
using Accession.Core.Settings;
using Accession.Core.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Accession.App;

public partial class App : Application
{
    private IHost? _host;
    private bool _showingError;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureLogging();
        RegisterGlobalExceptionHandlers();

        try
        {
            _host = BuildHost();
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
        builder.Services.AddSingleton<ISettingsService>(sp => new JsonSettingsService(
            AppPaths.SettingsFile,
            sp.GetRequiredService<ILogger<JsonSettingsService>>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(sp => new BusyTracker(sp.GetRequiredService<TimeProvider>()));

        // UI services
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IWindowPlacementService, WindowPlacementService>();

        // View models and windows
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
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
