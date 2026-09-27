namespace Accession.UI.Components;

/// <summary>A button in a <see cref="ChoiceDialog"/>.</summary>
public sealed record DialogChoice(string Key, string Label, DialogChoiceStyle Style = DialogChoiceStyle.Normal);

public enum DialogChoiceStyle
{
    Normal,
    Primary,
    Danger,
}

/// <summary>A labelled value shown in a dialog ("Locked by: jane.doe").</summary>
public sealed record DialogFact(string Label, string Value);

/// <summary>
/// A question for the user with a few buttons. <see cref="CancelKey"/> is returned for Esc and the close button.
/// </summary>
public sealed record ChoiceDialog(string Title, string Message, IReadOnlyList<DialogChoice> Choices, string CancelKey)
{
    public IReadOnlyList<DialogFact> Facts { get; init; } = [];

    /// <summary>Shown under the facts, e.g. a warning.</summary>
    public string? Note { get; init; }

    public DialogKind Kind { get; init; } = DialogKind.Question;
}

public enum DialogKind
{
    Question,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Queue of dialogs shown by the web page (<see cref="DialogHost"/>). Code on any thread asks with
/// <see cref="AskAsync"/>; the page answers with <see cref="Answer"/>. One dialog shows at a time.
/// </summary>
public sealed class DialogCenter
{
    private readonly object _gate = new();
    private readonly List<(ChoiceDialog Dialog, TaskCompletionSource<string> Answer)> _pending = [];

    /// <summary>Raised on any thread when the dialog to show changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The dialog to show now, or null.</summary>
    public ChoiceDialog? Current
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count == 0 ? null : _pending[0].Dialog;
            }
        }
    }

    public Task<string> AskAsync(ChoiceDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _pending.Add((dialog, answer));
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return answer.Task;
    }

    /// <summary>Answers the current dialog with a choice key.</summary>
    public void Answer(ChoiceDialog dialog, string key)
    {
        TaskCompletionSource<string>? answer = null;
        lock (_gate)
        {
            var index = _pending.FindIndex(p => ReferenceEquals(p.Dialog, dialog));
            if (index >= 0)
            {
                answer = _pending[index].Answer;
                _pending.RemoveAt(index);
            }
        }

        if (answer is not null)
        {
            answer.TrySetResult(key);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Cancels every waiting dialog (e.g. the page is closing).</summary>
    public void CancelAll()
    {
        List<(ChoiceDialog Dialog, TaskCompletionSource<string> Answer)> pending;
        lock (_gate)
        {
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (var (dialog, answer) in pending)
        {
            answer.TrySetResult(dialog.CancelKey);
        }

        if (pending.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
