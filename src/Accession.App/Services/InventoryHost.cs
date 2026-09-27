using Accession.Core.Model;
using Accession.Data.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.App.Services;

/// <summary>Holds the inventory the user has open and tells the UI when it changes.</summary>
public sealed class InventoryHost : ObservableObject
{
    public InventorySession? Session { get; private set; }

    public bool HasSession => Session is not null;

    public bool IsReadOnly => Session?.IsReadOnly ?? false;

    /// <summary>An inventory is open and may be modified.</summary>
    public bool CanModify => Session is { IsReadOnly: false };

    public bool IsRootAvailable => Session?.IsRootAvailable ?? false;

    public InventoryConfig? Config => Session?.Config;

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

    /// <summary>Notifies bindings after the session's config or state changed.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private void OnBecameReadOnly(object? sender, EventArgs e) => UiThread.Post(() =>
    {
        Refresh();
        LockLost?.Invoke(this, EventArgs.Empty);
    });
}
