using System.Globalization;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Data.Locking;
using Accession.Data.Sessions;
using Accession.Presentation.Platform;
using Accession.UI.Components;

namespace Accession.Presentation.Services;

/// <summary>
/// Answers the open workflow's questions with web dialogs (web UI). The open runs on a background thread,
/// which waits here while the page shows the dialog; the folder picker stays the native Windows dialog.
/// </summary>
public sealed class WebOpenInteraction(DialogCenter dialogs, INativeDialogs nativeDialogs, IUiDispatcher ui, ISettingsService settings)
    : IOpenInteraction
{
    public const string Cancel = "cancel";

    public bool ConfirmUpgrade(int fromVersion, int toVersion) => Ask(new ChoiceDialog(
        "Upgrade inventory",
        $"This inventory was created by an older version of Accession (schema version {fromVersion}). " +
        $"It must be upgraded to version {toVersion} before it can be opened.\n\nA backup copy of the inventory file is made first.",
        [new DialogChoice(Cancel, "Cancel"), new DialogChoice("upgrade", "Back up and upgrade", DialogChoiceStyle.Primary)],
        Cancel)) == "upgrade";

    public LockConflictChoice ResolveLockConflict(LockConflict conflict)
    {
        var holder = conflict.Holder;
        var zone = settings.Current.DisplayTimeZone;
        var staleMinutes = ((int)InventoryLockService.StaleAfter.TotalMinutes).ToString(CultureInfo.CurrentCulture);

        // Another Accession window of this user on this computer: two writable windows would both write to the file.
        if (conflict.IsOtherWindowHere)
        {
            return Ask(new ChoiceDialog(
                "Inventory already open",
                "This inventory is already open in another Accession window on this computer. Do you want to open it read-only here?",
                [new DialogChoice(Cancel, "Cancel"), new DialogChoice("readonly", "Open read-only", DialogChoiceStyle.Primary)],
                Cancel) { Kind = DialogKind.Info }) == "readonly"
                ? LockConflictChoice.OpenReadOnly
                : LockConflictChoice.Cancel;
        }

        List<DialogChoice> choices = [new(Cancel, "Cancel")];
        if (conflict.IsStale)
        {
            choices.Add(new DialogChoice("takeover", "Take over lock", DialogChoiceStyle.Danger));
        }

        choices.Add(new DialogChoice("readonly", "Open read-only", DialogChoiceStyle.Primary));

        var answer = Ask(new ChoiceDialog(
            "Inventory is locked",
            $"{holder.UserName} has this inventory open on {holder.MachineName}. Do you want to open it read-only? You can browse and export, but not change it.",
            choices,
            Cancel)
        {
            Kind = DialogKind.Warning,
            Facts =
            [
                new DialogFact("Locked by", holder.UserName),
                new DialogFact("Computer", holder.MachineName),
                new DialogFact("Opened", TimeFormatter.Format(holder.LockedAtUtc, zone)),
                new DialogFact("Last activity", TimeFormatter.Format(holder.HeartbeatAtUtc, zone)),
            ],
            Note = conflict.IsStale
                ? $"There has been no activity for more than {staleMinutes} minutes. If {holder.MachineName} crashed or was switched off, you can take over the lock."
                : null,
        });

        switch (answer)
        {
            case "readonly":
                return LockConflictChoice.OpenReadOnly;
            case "takeover":
                var confirmed = Ask(new ChoiceDialog(
                    "Take over lock",
                    $"Take over the lock from {holder.UserName} on {holder.MachineName}?\n\n" +
                    "Only do this if you are sure they are no longer using the inventory (for example, their computer crashed). " +
                    "The takeover is recorded in the audit log.",
                    [new DialogChoice(Cancel, "Cancel"), new DialogChoice("confirm", "Take over", DialogChoiceStyle.Danger)],
                    Cancel) { Kind = DialogKind.Warning }) == "confirm";
                return confirmed ? LockConflictChoice.TakeOver : LockConflictChoice.Cancel;
            default:
                return LockConflictChoice.Cancel;
        }
    }

    public RootUnreachableResolution ResolveRootUnreachable(string rootPath)
    {
        while (true)
        {
            var answer = Ask(new ChoiceDialog(
                "Root folder not available",
                "The folder that contains this inventory's media cannot be reached. It may be on a network share that is offline, or it was moved.",
                [
                    new DialogChoice(Cancel, "Cancel"),
                    new DialogChoice("offline", "Open offline"),
                    new DialogChoice("change", "Change root path…"),
                    new DialogChoice("retry", "Retry", DialogChoiceStyle.Primary),
                ],
                Cancel)
            {
                Kind = DialogKind.Warning,
                Facts = [new DialogFact("Root folder", rootPath)],
                Note = "Offline, you can browse and export, but not scan or discover media.",
            });

            switch (answer)
            {
                case "retry":
                    return new RootUnreachableResolution(RootUnreachableChoice.Retry);
                case "offline":
                    return new RootUnreachableResolution(RootUnreachableChoice.ContinueOffline);
                case "change":
                    var folder = ui.Invoke(() => nativeDialogs.PickFolder("Select the new root folder that contains the media folders"));
                    if (folder is not null)
                    {
                        return new RootUnreachableResolution(RootUnreachableChoice.ChangeRootPath, folder);
                    }

                    continue; // picker cancelled: ask again
                default:
                    return new RootUnreachableResolution(RootUnreachableChoice.Cancel);
            }
        }
    }

    /// <summary>Shows a dialog and waits for the answer. Must not run on the UI thread (the page needs it to answer).</summary>
    private string Ask(ChoiceDialog dialog) => dialogs.AskAsync(dialog).GetAwaiter().GetResult();
}
