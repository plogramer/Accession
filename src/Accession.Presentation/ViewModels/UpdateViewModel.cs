using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Core.Updates;
using Accession.Presentation.Mvvm;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels;

public enum UpdateChoice
{
    Close,
    Download,
    Skip,
}

/// <summary>The outcome of an update check: a newer version with its release notes, up to date, or why it could not check.</summary>
public sealed partial class UpdateViewModel : DialogViewModelBase
{
    private readonly UpdateCheckResult _result;

    public UpdateViewModel(UpdateCheckResult result, DisplayTimeZone zone = DisplayTimeZone.Local)
    {
        ArgumentNullException.ThrowIfNull(result);
        _result = result;
        Title = IsAvailable ? "New version available" : "Check for updates";
        Published = result.Release?.PublishedAt is { } at ? TimeFormatter.Format(at, zone) : string.Empty;
    }

    public bool IsAvailable => _result.Status is UpdateStatus.Available or UpdateStatus.Skipped;

    public string CurrentVersion => AppVersion.Display(_result.Current);

    public string LatestVersion => _result.Release is { } release ? $"{AppVersion.Display(release.Version)}  ({release.Title})" : "—";

    public string Published { get; }

    public string Message => _result.Status switch
    {
        UpdateStatus.Available or UpdateStatus.Skipped => "A newer version of Accession is available. Download opens its page on GitHub.",
        UpdateStatus.UpToDate => "You have the latest version.",
        UpdateStatus.Failed => $"GitHub could not be reached, so the check did not run. Check the internet connection or proxy, and try again later.\n\n{_result.Error}",
        _ => string.Empty,
    };

    public string MessageTone => _result.Status == UpdateStatus.Failed ? "warning" : "info";

    public string Notes => IsAvailable && _result.Release is { Notes.Length: > 0 } release ? release.Notes : string.Empty;

    public UpdateChoice Choice { get; private set; }

    [RelayCommand]
    private void Download()
    {
        Choice = UpdateChoice.Download;
        Close(true);
    }

    [RelayCommand]
    private void Skip()
    {
        Choice = UpdateChoice.Skip;
        Close(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Choice = UpdateChoice.Close;
        Close(false);
    }
}
