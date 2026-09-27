using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Data.Locking;
using Accession.Data.Sessions;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels;

/// <summary>"Inventory In Use" dialog (requirements LCK-04, section 8.16).</summary>
public sealed partial class LockConflictViewModel : DialogViewModelBase
{
    private readonly IDialogService _dialogs;

    public LockConflictViewModel(LockHolder holder, bool isStale, IDialogService dialogs, DisplayTimeZone timeZone)
    {
        _dialogs = dialogs;
        Title = "Inventory In Use";
        UserName = holder.UserName;
        MachineName = holder.MachineName;
        LockedAt = TimeFormatter.Format(holder.LockedAtUtc, timeZone);
        LastActivity = TimeFormatter.Format(holder.HeartbeatAtUtc, timeZone);
        IsStale = isStale;
    }

    public string UserName { get; }

    public string MachineName { get; }

    public string LockedAt { get; }

    public string LastActivity { get; }

    /// <summary>No activity for more than 10 minutes: Take Over is allowed.</summary>
    public bool IsStale { get; }

    public string StaleMinutes => ((int)InventoryLockService.StaleAfter.TotalMinutes).ToString(System.Globalization.CultureInfo.CurrentCulture);

    public LockConflictChoice Choice { get; private set; } = LockConflictChoice.Cancel;

    [RelayCommand]
    private void OpenReadOnly()
    {
        Choice = LockConflictChoice.OpenReadOnly;
        Close(true);
    }

    [RelayCommand(CanExecute = nameof(IsStale))]
    private void TakeOver()
    {
        var confirmed = _dialogs.Confirm(
            "Take over lock",
            $"Take over the lock from {UserName} on {MachineName}?\n\n" +
            "Only do this if you are sure they are no longer using the inventory (for example, their computer crashed). " +
            "The takeover is recorded in the audit log.");
        if (confirmed)
        {
            Choice = LockConflictChoice.TakeOver;
            Close(true);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Choice = LockConflictChoice.Cancel;
        Close(false);
    }
}
