using Accession.Core.Settings;
using Accession.Data.Locking;
using Accession.Data.Sessions;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.UI.Components;

namespace Accession.Tests.WebUi;

/// <summary>The web open prompts run on a background thread and wait for the page's answer.</summary>
public sealed class WebOpenInteractionTests
{
    private static readonly LockHolder Holder = new("LITSUPPORT\\john.roe", "LIT-WS-042", 4242, Guid.NewGuid(),
        new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 27, 8, 5, 0, TimeSpan.Zero));

    [Fact]
    public async Task Lock_conflict_open_read_only()
    {
        var (interaction, center) = Create();

        var result = Task.Run(() => interaction.ResolveLockConflict(new LockConflict(Holder, false, IsOtherWindowHere: false)));
        var dialog = await WaitForDialog(center);

        Assert.DoesNotContain(dialog.Choices, c => c.Key == "takeover"); // not stale: no take over
        Assert.Contains(dialog.Facts, f => f.Value == "LIT-WS-042");
        center.Answer(dialog, "readonly");
        Assert.Equal(LockConflictChoice.OpenReadOnly, await result);
    }

    [Fact]
    public async Task Lock_conflict_take_over_needs_a_second_confirmation()
    {
        var (interaction, center) = Create();

        var result = Task.Run(() => interaction.ResolveLockConflict(new LockConflict(Holder, true, IsOtherWindowHere: false)));
        var first = await WaitForDialog(center);
        center.Answer(first, "takeover");
        var confirm = await WaitForDialog(center, notSame: first);
        center.Answer(confirm, "confirm");

        Assert.Equal(LockConflictChoice.TakeOver, await result);
    }

    [Fact]
    public async Task Lock_conflict_take_over_declined_cancels_the_open()
    {
        var (interaction, center) = Create();

        var result = Task.Run(() => interaction.ResolveLockConflict(new LockConflict(Holder, true, IsOtherWindowHere: false)));
        var first = await WaitForDialog(center);
        center.Answer(first, "takeover");
        var confirm = await WaitForDialog(center, notSame: first);
        center.Answer(confirm, confirm.CancelKey);

        Assert.Equal(LockConflictChoice.Cancel, await result);
    }

    [Fact]
    public async Task Lock_message_names_the_user_and_computer()
    {
        var (interaction, center) = Create();

        var result = Task.Run(() => interaction.ResolveLockConflict(new LockConflict(Holder, false, IsOtherWindowHere: false)));
        var dialog = await WaitForDialog(center);

        Assert.Equal("Inventory is locked", dialog.Title);
        Assert.Contains(@"LITSUPPORT\john.roe has this inventory open on LIT-WS-042", dialog.Message, StringComparison.Ordinal);
        Assert.Contains("open it read-only", dialog.Message, StringComparison.Ordinal);
        center.Answer(dialog, dialog.CancelKey);
        Assert.Equal(LockConflictChoice.Cancel, await result);
    }

    [Fact]
    public async Task Another_window_on_this_computer_offers_read_only_without_take_over()
    {
        var (interaction, center) = Create();

        var result = Task.Run(() => interaction.ResolveLockConflict(new LockConflict(Holder, IsStale: true, IsOtherWindowHere: true)));
        var dialog = await WaitForDialog(center);

        Assert.Equal("Inventory already open", dialog.Title);
        Assert.DoesNotContain(dialog.Choices, c => c.Key == "takeover");
        center.Answer(dialog, "readonly");
        Assert.Equal(LockConflictChoice.OpenReadOnly, await result);
    }

    [Fact]
    public async Task Root_unreachable_change_path_uses_the_native_folder_picker()
    {
        var (interaction, center) = Create(pickedFolder: @"\\evidence02\intake\NW-2026-0142");

        var result = Task.Run(() => interaction.ResolveRootUnreachable(@"\\evidence01\intake\NW-2026-0142"));
        center.Answer(await WaitForDialog(center), "change");

        var resolution = await result;
        Assert.Equal(RootUnreachableChoice.ChangeRootPath, resolution.Choice);
        Assert.Equal(@"\\evidence02\intake\NW-2026-0142", resolution.NewRootPath);
    }

    [Fact]
    public async Task Root_unreachable_asks_again_when_the_picker_is_cancelled()
    {
        var (interaction, center) = Create(pickedFolder: null);

        var result = Task.Run(() => interaction.ResolveRootUnreachable(@"\\evidence01\intake"));
        var first = await WaitForDialog(center);
        center.Answer(first, "change");
        var second = await WaitForDialog(center, notSame: first);
        center.Answer(second, "offline");

        Assert.Equal(RootUnreachableChoice.ContinueOffline, (await result).Choice);
    }

    [Fact]
    public async Task Upgrade_is_confirmed_only_by_the_upgrade_button()
    {
        var (interaction, center) = Create();

        var result = Task.Run(() => interaction.ConfirmUpgrade(1, 2));
        center.Answer(await WaitForDialog(center), "cancel");

        Assert.False(await result);
    }

    private static (WebOpenInteraction Interaction, DialogCenter Center) Create(string? pickedFolder = null)
    {
        var center = new DialogCenter();
        var interaction = new WebOpenInteraction(center, new FolderPicker(pickedFolder), new InlineDispatcher(), new SettingsStub());
        return (interaction, center);
    }

    private static async Task<ChoiceDialog> WaitForDialog(DialogCenter center, ChoiceDialog? notSame = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (center.Current is ChoiceDialog dialog && !ReferenceEquals(dialog, notSame))
            {
                return dialog;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("No dialog was shown.");
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public void Defer(Action action) => action();

        public T Invoke<T>(Func<T> action) => action();
    }

    private sealed class FolderPicker(string? folder) : INativeDialogs
    {
        public string? PickFolder(string title, string? initialDirectory = null) => folder;

        public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => throw new NotSupportedException();

        public string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null) =>
            throw new NotSupportedException();
    }

    private sealed class SettingsStub : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged
        {
            add { }
            remove { }
        }

        public void Update(Action<AppSettings> change) => change(Current);

        public void AddRecentInventory(string path, string displayName)
        {
        }

        public void RemoveRecentInventory(string path)
        {
        }
    }
}
