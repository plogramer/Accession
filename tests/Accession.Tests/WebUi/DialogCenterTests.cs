using Accession.UI.Components;

namespace Accession.Tests.WebUi;

public sealed class DialogCenterTests
{
    private static ChoiceDialog Dialog(string title) =>
        new(title, "Message", [new DialogChoice("no", "No"), new DialogChoice("yes", "Yes")], "no");

    [Fact]
    public async Task Latest_dialog_is_on_top_and_answers_go_to_the_right_question()
    {
        var center = new DialogCenter();
        var first = Dialog("First");
        var second = Dialog("First"); // looks the same: still a different question
        var firstAnswer = center.AskAsync(first);
        var secondAnswer = center.AskAsync(second);

        Assert.Same(second, center.Current);
        center.Answer(first, "yes");

        Assert.Equal("yes", await firstAnswer);
        Assert.Same(second, center.Current);
        Assert.Single(center.Items);
        Assert.False(secondAnswer.IsCompleted);
    }

    [Fact]
    public async Task Cancel_all_answers_every_dialog_with_its_cancel_key()
    {
        var center = new DialogCenter();
        var answer = center.AskAsync(Dialog("Question"));

        center.CancelAll();

        Assert.Equal("no", await answer);
        Assert.Null(center.Current);
    }

    [Fact]
    public void Answering_a_dialog_that_is_not_pending_does_nothing()
    {
        var center = new DialogCenter();
        var changes = 0;
        center.Changed += (_, _) => changes++;

        center.Answer(Dialog("Unknown"), "yes");

        Assert.Equal(0, changes);
    }
}
