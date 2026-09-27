using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.Core.Formatting;
using Accession.Core.Scanning;
using Accession.Core.Settings;
using Accession.Data.Scanning;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels.Scanning;

/// <summary>Scan Queue / Progress screen (requirements SCN-02, SCN-05, SCN-24, section 8.9).</summary>
public sealed partial class ScanQueueViewModel : ViewModelBase, IDisposable
{
    private readonly ScanHost _scans;
    private readonly ISettingsService _settings;

    public ScanQueueViewModel(ScanHost scans, ISettingsService settings)
    {
        _scans = scans;
        _settings = settings;
        _scans.PropertyChanged += OnScansChanged;
        Refresh();
    }

    public bool IsRunning => _scans.Current is not null;

    public bool IsIdle => !IsRunning;

    public string IdleText => _scans.CanScan
        ? "No scan is running. Select media on the Media screen and choose Scan."
        : "Scanning is not available: the inventory is read-only or the root folder cannot be reached.";

    public string CurrentMedia { get; private set; } = string.Empty;

    public string Phase { get; private set; } = string.Empty;

    public string Counts { get; private set; } = string.Empty;

    public double Percent { get; private set; }

    public bool IsIndeterminate { get; private set; }

    public string HashedText { get; private set; } = string.Empty;

    public string BytesText { get; private set; } = string.Empty;

    public string RateText { get; private set; } = string.Empty;

    public string Elapsed { get; private set; } = string.Empty;

    public string Eta { get; private set; } = string.Empty;

    public string Errors { get; private set; } = string.Empty;

    public string Threads { get; private set; } = string.Empty;

    public string CurrentPath { get; private set; } = string.Empty;

    public ObservableCollection<QueueRow> Queue { get; } = [];

    public bool HasQueue => Queue.Count > 0;

    public void Dispose() => _scans.PropertyChanged -= OnScansChanged;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() => _scans.Pause();

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume() => _scans.Resume();

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _scans.Cancel();

    [RelayCommand]
    private void MoveUp(QueueRow? row)
    {
        if (row is not null)
        {
            _scans.MoveUp(row.MediaKey);
        }
    }

    [RelayCommand]
    private void MoveDown(QueueRow? row)
    {
        if (row is not null)
        {
            _scans.MoveDown(row.MediaKey);
        }
    }

    [RelayCommand]
    private void Remove(QueueRow? row)
    {
        if (row is not null)
        {
            _scans.Remove(row.MediaKey);
        }
    }

    private bool CanPause() => _scans.CanPause;

    private bool CanResume() => _scans.CanResume;

    private bool CanCancel() => _scans.CanCancel;

    private void OnScansChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var unit = _settings.Current.SizeUnit;
        var culture = CultureInfo.CurrentCulture;
        var current = _scans.Current;
        var p = _scans.Progress is { } snapshot && current is not null && snapshot.MediaKey == current.MediaKey ? snapshot : null;

        CurrentMedia = current is null ? string.Empty : $"{current.MediaId}  ({ScanTypeText(current.Type)})";
        Phase = current is null ? string.Empty
            : _scans.State == CoordinatorState.Paused ? "Paused"
            : p is null ? "Starting…"
            : p.Phase == ScanPhase.Enumerating ? "Listing folders and files (hashing in parallel)"
            : "Hashing (listing done)";
        Counts = p is null ? string.Empty
            : $"Folders {p.FoldersFound.ToString("N0", culture)}   Files {p.FilesFound.ToString("N0", culture)}   Size {SizeFormatter.Format(p.BytesFound, unit)}";
        Percent = p?.PercentByBytes ?? 0;
        IsIndeterminate = current is not null && (p is null || (!p.EnumerationDone && p.BytesFound == 0));
        HashedText = p is null ? string.Empty
            : $"Hashed {p.FilesHashed.ToString("N0", culture)} / {p.FilesFound.ToString("N0", culture)} files   ({p.PercentByBytes:0} %)";
        BytesText = p is null ? string.Empty : $"{SizeFormatter.Format(p.BytesHashed, unit)} / {SizeFormatter.Format(p.BytesFound, unit)}";
        RateText = p is null ? string.Empty
            : $"{SizeFormatter.Format((long)p.BytesPerSecond, unit)}/s   •   {p.FilesPerSecond.ToString("N0", culture)} files/s";
        Elapsed = p is null ? string.Empty : Format(p.Elapsed);
        Eta = p is null ? string.Empty
            : p.IsPaused ? "—"
            : p.Eta is { } eta ? Format(eta)
            : "estimating…";
        Errors = p?.Errors.ToString("N0", culture) ?? string.Empty;
        Threads = _scans.CurrentOptions is { } o ? $"listing {o.EnumerationThreads} / hashing {o.HashingThreads}" : string.Empty;
        CurrentPath = p?.CurrentPath ?? string.Empty;

        Queue.Clear();
        var position = 1;
        foreach (var item in _scans.Waiting)
        {
            Queue.Add(new QueueRow(position++, item.MediaKey, item.MediaId, ScanTypeText(item.Type)));
        }

        OnPropertyChanged(string.Empty);
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static string ScanTypeText(Accession.Core.Model.ScanType type) => type switch
    {
        Accession.Core.Model.ScanType.Full => "full scan",
        Accession.Core.Model.ScanType.Resume => "resume",
        _ => "retry failed files",
    };

    private static string Format(TimeSpan span) =>
        span.TotalHours >= 24 ? $"{(int)span.TotalDays}d {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}" : span.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}

public sealed record QueueRow(int Position, long MediaKey, string MediaId, string Type);
