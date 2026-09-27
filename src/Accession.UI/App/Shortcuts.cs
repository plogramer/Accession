using System.Windows.Input;
using Accession.UI.Shell;

namespace Accession.UI.App;

/// <summary>What a keyboard shortcut does (keys arrive from accession.js as "ctrl+n", "alt+3", "f5", "?").</summary>
public static class Shortcuts
{
    public static readonly IReadOnlyList<(string Keys, string Action)> Help =
    [
        ("Ctrl N", "New inventory"),
        ("Ctrl O", "Open inventory"),
        ("Ctrl ,", "Settings"),
        ("F5", "Refresh the screen"),
        ("Ctrl F", "Find (the screen's search box)"),
        ("Alt 1…7", "Go to a screen in the side navigation"),
        ("↑ ↓", "Move through table rows"),
        ("Enter", "Open the selected row, confirm a dialog"),
        ("Esc", "Close a dialog or menu"),
        ("?", "Show this list"),
    ];

    /// <summary>Runs the shortcut. Returns true when the help list should be toggled.</summary>
    public static async Task<bool> HandleAsync(IAppModel model, string key)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Dialogs.Current is not null || model.IsBusy)
        {
            return false; // a dialog or the busy overlay has the keyboard
        }

        var shell = model.Screen as IShellModel;
        switch (key)
        {
            case "ctrl+n":
                await Run(model.NewInventoryCommand);
                break;
            case "ctrl+o":
                await Run(model.OpenInventoryCommand);
                break;
            case "ctrl+,":
                await Run(model.OpenSettingsCommand);
                break;
            case "f5" when shell?.SelectedItem.Content is IRefreshableScreen screen:
                await Run(screen.RefreshCommand);
                break;
            case ['a', 'l', 't', '+', var digit] when shell is not null && digit is >= '1' and <= '9':
                var index = digit - '1';
                if (index < shell.NavItems.Count)
                {
                    shell.SelectedItem = shell.NavItems[index];
                }

                break;
            case "?":
                return true;
        }

        return false;
    }

    private static async Task Run(ICommand command)
    {
        await Task.Yield(); // commands may open native pickers or dialogs (see ObservingComponentBase.Run)
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
