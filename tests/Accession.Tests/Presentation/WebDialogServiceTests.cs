using Accession.Core.Model;
using Accession.Core.Settings;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels;
using Accession.Tests.TestSupport;
using Accession.UI.Components;
using Accession.UI.Forms;

namespace Accession.Tests.Presentation;

/// <summary>Dialogs shown in the web page: the caller waits until the page answers or the form closes.</summary>
public sealed class WebDialogServiceTests
{
    private readonly DialogCenter _center = new();
    private readonly RecordingNative _native = new();
    private readonly WebDialogService _dialogs;

    public WebDialogServiceTests()
    {
        _dialogs = new WebDialogService(_center, new BlockingWaiter(), _native, new RecordingDesktop());
    }

    [Fact]
    public async Task Delete_media_form_needs_the_typed_media_id()
    {
        var vm = new DeleteMediaViewModel(new Media { MediaId = "123-123_002", FileCount = 10, ScanCount = 1 }, SizeUnitSystem.Decimal);
        var result = Task.Run(() => _dialogs.ShowDialog(vm), TestContext.Current.CancellationToken);
        var form = await WaitFor<FormDialog>();

        var delete = form.Buttons.Single(b => b.Label == "Delete media");
        Assert.False(delete.Command.CanExecute(null));
        var confirm = form.Sections.SelectMany(s => s.Items).OfType<TextField>().Single();
        confirm.Set!("123-123_002");
        Assert.True(delete.Command.CanExecute(null));
        delete.Command.Execute(null);

        Assert.True(await result);
        Assert.Null(_center.Current); // the form closed
    }

    [Fact]
    public async Task Escape_dismisses_a_form_with_no_result()
    {
        var vm = new DeleteMediaViewModel(new Media { MediaId = "M1" }, SizeUnitSystem.Decimal);
        var result = Task.Run(() => _dialogs.ShowDialog(vm), TestContext.Current.CancellationToken);

        (await WaitFor<FormDialog>()).Dismiss!();

        Assert.False(await result);
    }

    [Fact]
    public async Task Settings_form_saves_through_the_view_model()
    {
        var settings = new TestSettings();
        var vm = new SettingsViewModel(settings, _dialogs);
        var result = Task.Run(() => _dialogs.ShowDialog(vm), TestContext.Current.CancellationToken);
        var form = await WaitFor<FormDialog>();

        form.Sections.SelectMany(s => s.Items).OfType<NumberField>().Single(f => f.Label == "Hashing threads").Set(6);
        form.Sections.SelectMany(s => s.Items).OfType<SelectField>().Single(f => f.Label == "Sizes").Set(nameof(SizeUnitSystem.Binary));
        form.Buttons.Single(b => b.Label == "Save").Command.Execute(null);

        Assert.True(await result);
        Assert.Equal(6, settings.Current.HashingThreads);
        Assert.Equal(SizeUnitSystem.Binary, settings.Current.SizeUnit);
    }

    [Fact]
    public async Task Confirm_is_a_yes_no_question()
    {
        var answer = Task.Run(() => _dialogs.Confirm("Rescan", "Replace the file list?"), TestContext.Current.CancellationToken);
        var question = await WaitFor<ChoiceDialog>();

        Assert.Equal(["No", "Yes"], question.Choices.Select(c => c.Label));
        _center.Answer(question, "yes");

        Assert.True(await answer);
    }

    [Fact]
    public async Task Error_shows_the_technical_details()
    {
        var shown = Task.Run(() => _dialogs.ShowError("Add media", "The media could not be added.", new IOException("disk full")),
            TestContext.Current.CancellationToken);
        var form = await WaitFor<FormDialog>();

        var details = form.Sections.SelectMany(s => s.Items).OfType<CodeItem>().Single().Text();
        Assert.Contains("disk full", details, StringComparison.Ordinal);
        form.Buttons.Single(b => b.Label == "OK").Command.Execute(null);
        await shown;
    }

    [Fact]
    public async Task A_dialog_can_ask_a_question_on_top_of_itself()
    {
        var vm = new DeleteMediaViewModel(new Media { MediaId = "M1" }, SizeUnitSystem.Decimal);
        var outer = Task.Run(() => _dialogs.ShowDialog(vm), TestContext.Current.CancellationToken);
        var form = await WaitFor<FormDialog>();

        var inner = Task.Run(() => _dialogs.Confirm("Sure?", "Really?"), TestContext.Current.CancellationToken);
        var question = await WaitFor<ChoiceDialog>();
        Assert.Equal(2, _center.Items.Count);
        Assert.Same(question, _center.Current);
        _center.Answer(question, "no");
        Assert.False(await inner);

        form.Dismiss!();
        Assert.False(await outer);
    }

    [Fact]
    public void Pickers_stay_native()
    {
        Assert.Equal(@"C:\picked", _dialogs.PickFolder("Select"));
    }

    [Fact]
    public void Dialogs_without_a_web_form_are_a_programming_error()
    {
        Assert.Throws<NotSupportedException>(() => _dialogs.ShowDialog(new UnknownDialog()));
        Assert.Empty(_center.Items);
    }

    private async Task<T> WaitFor<T>() where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (_center.Current is T item)
            {
                return item;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No {typeof(T).Name} was shown.");
    }

    private sealed class BlockingWaiter : IModalWaiter
    {
        public T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();
    }

    private sealed class RecordingNative : INativeDialogs
    {
        public string? PickFolder(string title, string? initialDirectory = null) => @"C:\picked";

        public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => null;

        public string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null) => null;
    }
}

/// <summary>A dialog view model with no web form (the source generator needs it public).</summary>
public sealed class UnknownDialog : DialogViewModelBase;
