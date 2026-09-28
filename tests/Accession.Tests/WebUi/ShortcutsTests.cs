using System.Windows.Input;
using Accession.UI.App;
using Accession.UI.Components;
using Accession.UI.Shell;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Tests.WebUi;

public sealed class ShortcutsTests
{
    [Fact]
    public async Task Alt_digit_selects_the_nth_screen()
    {
        var shell = new FakeShell(new FakeDashboard());
        var app = new FakeApp(shell);

        await Shortcuts.HandleAsync(app, "alt+3");

        Assert.Equal("Files", shell.SelectedItem.Key);
    }

    [Fact]
    public async Task Alt_digit_beyond_the_list_does_nothing()
    {
        var shell = new FakeShell(new FakeDashboard());

        await Shortcuts.HandleAsync(new FakeApp(shell), "alt+9");

        Assert.Equal("Dashboard", shell.SelectedItem.Key);
    }

    [Fact]
    public async Task F5_refreshes_the_current_screen()
    {
        var refreshes = 0;
        var app = new FakeApp(new FakeShell(new FakeDashboard { RefreshCommand = new RelayCommand(() => refreshes++) }));

        await Shortcuts.HandleAsync(app, "f5");

        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task Ctrl_n_runs_new_inventory_and_question_mark_toggles_help()
    {
        var app = new FakeApp(new FakeStart());
        var created = 0;
        var appWithCommand = new CommandApp(app, new RelayCommand(() => created++));

        Assert.False(await Shortcuts.HandleAsync(appWithCommand, "ctrl+n"));
        Assert.Equal(1, created);
        Assert.True(await Shortcuts.HandleAsync(appWithCommand, "?"));
    }

    [Fact]
    public async Task Shortcuts_are_ignored_while_a_dialog_is_open()
    {
        var shell = new FakeShell(new FakeDashboard());
        var app = new FakeApp(shell);
        _ = app.Dialogs.AskAsync(new ChoiceDialog("Q", "?", [new("ok", "OK")], "ok"));

        await Shortcuts.HandleAsync(app, "alt+2");

        Assert.Equal("Dashboard", shell.SelectedItem.Key);
    }

    [Fact]
    public async Task F1_opens_the_help_for_the_screen_shown()
    {
        var shell = new FakeShell(new FakeDashboard(), files: new FakeFiles());
        shell.SelectedItem = shell.NavItems.First(n => n.Key == "Files");
        var app = new FakeApp(shell);
        var start = new FakeApp(new FakeStart());

        await Shortcuts.HandleAsync(app, "f1");
        await Shortcuts.HandleAsync(start, "f1");

        Assert.Equal([HelpTopics.Files], app.HelpShown);
        Assert.Equal([HelpTopics.Start], start.HelpShown);
    }

    [Fact]
    public async Task The_screen_header_has_a_help_button()
    {
        var html = await WebUiRenderer.RenderAsync<AppRoot>(new Dictionary<string, object?> { [nameof(AppRoot.Model)] = new FakeApp(new FakeShell(new FakeDashboard())) });

        Assert.Contains("aria-label=\"Help for this screen\"", html);
    }

    [Fact]
    public async Task Selectable_tables_are_keyboard_navigable()
    {
        var shell = new FakeShell(new FakeDashboard(), media: new FakeMedia());
        shell.SelectedItem = shell.NavItems.First(n => n.Key == "Media");
        var html = await WebUiRenderer.RenderAsync<AppRoot>(new Dictionary<string, object?> { [nameof(AppRoot.Model)] = new FakeApp(shell) });

        Assert.Contains("data-keynav=\"true\"", html);
        Assert.Contains("aria-label=\"Media\"", html);
    }

    /// <summary>Wraps an app model to replace its New inventory command.</summary>
    private sealed class CommandApp(FakeApp inner, ICommand newInventory) : IAppModel
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        {
            add => inner.PropertyChanged += value;
            remove => inner.PropertyChanged -= value;
        }

        public object Screen => inner.Screen;
        public string Theme { get => inner.Theme; set => inner.Theme = value; }
        public ToastService Toasts => inner.Toasts;
        public DialogCenter Dialogs => inner.Dialogs;
        public bool IsBusy => inner.IsBusy;
        public string BusyMessage => inner.BusyMessage;
        public bool CanCancelBusy => inner.CanCancelBusy;
        public ICommand CancelBusyCommand => inner.CancelBusyCommand;
        public ICommand NewInventoryCommand => newInventory;
        public ICommand OpenInventoryCommand => inner.OpenInventoryCommand;
        public ICommand OpenSettingsCommand => inner.OpenSettingsCommand;
        public ICommand HelpCommand => inner.HelpCommand;

        public void PageRendered() => inner.PageRendered();

        public string UpdateText => inner.UpdateText;
        public ICommand ShowUpdateCommand => inner.ShowUpdateCommand;
        public ICommand DownloadUpdateCommand => inner.DownloadUpdateCommand;
        public ICommand SkipUpdateCommand => inner.SkipUpdateCommand;
        public ICommand DismissUpdateCommand => inner.DismissUpdateCommand;
        public ICommand CheckForUpdatesCommand => inner.CheckForUpdatesCommand;
    }
}
