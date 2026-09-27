using Accession.Core.Model;
using Accession.Data.Sessions;
using Accession.Presentation.Platform;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.Presentation.Services;

/// <summary>Holds the inventory the user has open and tells the UI when it changes.</summary>
public sealed class InventoryHost : ObservableObject
{
    private readonly IUiDispatcher _ui;

    public InventoryHost(IUiDispatcher ui)
    {
        _ui = ui;
    }

    public InventorySession? Session { get; private set; }

    public bool HasSession => Session is not null;

    public bool IsReadOnly => Session?.IsReadOnly ?? false;

    /// <summary>An inventory is open and may be modified.</summary>
    public bool CanModify => Session is { IsReadOnly: false };

    public bool IsRootAvailable => Session?.IsRootAvailable ?? false;

    public InventoryConfig? Config => Session?.Config;

    /// <summary>Non-blocking notice shown at the top of the inventory shell (e.g. "2 media not scanned yet"); empty hides it.</summary>
    public string Notice { get; private set; } = string.Empty;

    /// <summary>Raised on the UI thread after media were added, deleted or re-discovered.</summary>
    public event EventHandler? MediaChanged;

    /// <summary>Raised after an inventory was opened or closed.</summary>
    public event EventHandler? SessionChanged;

    /// <summary>Raised on the UI thread when the lock was taken over and the session became read-only.</summary>
    public event EventHandler? LockLost;

    public void Open(InventorySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Close();
        Session = session;
        session.BecameReadOnly += OnBecameReadOnly;
        Refresh();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        var session = Session;
        if (session is null)
        {
            return;
        }

        session.BecameReadOnly -= OnBecameReadOnly;
        Session = null;
        Notice = string.Empty;
        try
        {
            session.Close();
        }
        finally
        {
            Refresh();
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetNotice(string notice)
    {
        Notice = notice;
        OnPropertyChanged(nameof(Notice));
    }

    public void NotifyMediaChanged() => _ui.Post(() => MediaChanged?.Invoke(this, EventArgs.Empty));

    /// <summary>Notifies bindings after the session's config or state changed.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private void OnBecameReadOnly(object? sender, EventArgs e) => _ui.Post(() =>
    {
        Refresh();
        LockLost?.Invoke(this, EventArgs.Empty);
    });
}
