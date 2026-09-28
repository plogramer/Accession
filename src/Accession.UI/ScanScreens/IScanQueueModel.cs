using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace Accession.UI.ScanScreens;

/// <summary>Scan Queue / Progress screen (requirements SCN-02, SCN-05, SCN-24, section 8.9).</summary>
public interface IScanQueueModel : INotifyPropertyChanged
{
    bool IsRunning { get; }
    bool IsPaused { get; }
    string IdleText { get; }
    string CurrentMedia { get; }
    string Phase { get; }
    string Counts { get; }

    /// <summary>0–100.</summary>
    double Percent { get; }

    bool IsIndeterminate { get; }
    string HashedText { get; }
    string BytesText { get; }
    string RateText { get; }
    string Elapsed { get; }
    string Eta { get; }
    string Errors { get; }
    string Threads { get; }
    string CurrentPath { get; }

    /// <summary>"12 % of 512.96 GiB" for a large file being hashed; empty otherwise.</summary>
    string CurrentFileProgress { get; }
    ObservableCollection<QueueRow> Queue { get; }

    ICommand PauseCommand { get; }
    ICommand ResumeCommand { get; }
    ICommand CancelCommand { get; }

    /// <summary>Parameter: the <see cref="QueueRow"/>.</summary>
    ICommand MoveUpCommand { get; }

    /// <summary>Parameter: the <see cref="QueueRow"/>.</summary>
    ICommand MoveDownCommand { get; }

    /// <summary>Parameter: the <see cref="QueueRow"/>.</summary>
    ICommand RemoveCommand { get; }
}
