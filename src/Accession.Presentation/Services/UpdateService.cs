using System.Globalization;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Core.Updates;
using Accession.Presentation.Platform;
using Accession.Presentation.ViewModels;
using Accession.UI.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.Services;

/// <summary>
/// New versions (published as GitHub releases): a quiet check a few seconds after start-up (at most once a day, when
/// "Check for new versions" is on), a banner while a newer version is available, and "Check for updates…" on request.
/// </summary>
public sealed partial class UpdateService(
    UpdateChecker checker,
    IDialogService dialogs,
    IDesktop desktop,
    ToastService toasts,
    ISettingsService settings,
    IUiDispatcher ui,
    TimeProvider time,
    ILogger<UpdateService> logger) : ObservableObject
{
    /// <summary>Wait after start-up, so the check never slows opening the app.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    private bool _started;

    /// <summary>The newer version shown in the banner; null when there is none, or it was dismissed or skipped.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerText))]
    public partial ReleaseInfo? Available { get; private set; }

    /// <summary>"Accession 0.2 is available. You have 0.1." (empty: no banner).</summary>
    public string BannerText => Available is { } release
        ? $"Accession {AppVersion.Display(release.Version)} is available. You have {AppVersion.Display(checker.Current)}."
        : string.Empty;

    /// <summary>Starts the automatic check once (after <see cref="StartupDelay"/>, in the background).</summary>
    public void StartAutomaticCheck()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var delay = Task.Delay(StartupDelay, time); // timed from now, not from when the background task gets to run
        _ = Task.Run(async () =>
        {
            try
            {
                await delay.ConfigureAwait(false);
                var result = await checker.CheckAsync(manual: false).ConfigureAwait(false);
                if (result.Status == UpdateStatus.Failed)
                {
                    logger.LogInformation("Could not check for a new version: {Error}", result.Error);
                }
                else if (result is { Status: UpdateStatus.Available, Release: { } release })
                {
                    ui.Post(() => Available = release);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogWarning(ex, "The update check failed");
            }
        });
    }

    /// <summary>Asks GitHub now and shows the outcome (a newer version, up to date, or why it could not check).</summary>
    [RelayCommand]
    private async Task CheckNow()
    {
        toasts.Show("Checking for a new version…");
        var result = await checker.CheckAsync(manual: true);
        if (result is { Status: UpdateStatus.Available, Release: { } release })
        {
            Available = release;
        }

        ShowDialog(result);
    }

    /// <summary>The banner's "What's new": the release notes.</summary>
    [RelayCommand]
    private void ShowDetails()
    {
        if (Available is { } release)
        {
            ShowDialog(new UpdateCheckResult(UpdateStatus.Available, checker.Current, release));
        }
    }

    [RelayCommand]
    private void Download()
    {
        if (Available is { } release)
        {
            desktop.OpenUrl(release.Url);
        }
    }

    [RelayCommand]
    private void Skip()
    {
        if (Available is { } release)
        {
            checker.Skip(release);
            Available = null;
            toasts.Show($"Version {AppVersion.Display(release.Version)} will not be announced again. A later version will.");
        }
    }

    /// <summary>Hides the banner until the next start.</summary>
    [RelayCommand]
    private void Dismiss() => Available = null;

    private void ShowDialog(UpdateCheckResult result)
    {
        var dialog = new UpdateViewModel(result, settings.Current.DisplayTimeZone);
        dialogs.ShowDialog(dialog);
        switch (dialog.Choice)
        {
            case UpdateChoice.Download when result.Release is { } release:
                desktop.OpenUrl(release.Url);
                break;
            case UpdateChoice.Skip when result.Release is { } release:
                Available = release;
                Skip();
                break;
        }
    }
}
