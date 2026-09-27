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
/// Dialogs shown by the web page (<see cref="DialogHost"/>), as a stack: a dialog can open a question on top
/// of itself. Code on any thread asks with <see cref="AskAsync"/> or opens a form with <see cref="Open"/>;
/// the page answers with <see cref="Answer"/>, forms close with <see cref="Close"/>.
/// </summary>
public sealed class DialogCenter
{
    private readonly object _gate = new();
    private readonly List<object> _items = [];
    // By reference: ChoiceDialog is a record, and two open questions may look alike.
    private readonly Dictionary<ChoiceDialog, TaskCompletionSource<string>> _answers = new(ReferenceEqualityComparer.Instance);

    /// <summary>Raised on any thread when dialogs were opened or closed.</summary>
    public event EventHandler? Changed;

    /// <summary>Open dialogs, bottom first (<see cref="ChoiceDialog"/> or <c>FormDialog</c>).</summary>
    public IReadOnlyList<object> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    /// <summary>The top dialog, or null.</summary>
    public object? Current
    {
        get
        {
            lock (_gate)
            {
                return _items.Count == 0 ? null : _items[^1];
            }
        }
    }

    public Task<string> AskAsync(ChoiceDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _items.Add(dialog);
            _answers[dialog] = answer;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return answer.Task;
    }

    /// <summary>Answers a question with a choice key.</summary>
    public void Answer(ChoiceDialog dialog, string key)
    {
        TaskCompletionSource<string>? answer;
        lock (_gate)
        {
            if (!_answers.Remove(dialog, out answer))
            {
                return;
            }

            RemoveItem(dialog);
        }

        answer.TrySetResult(key);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows a form until <see cref="Close"/> is called.</summary>
    public void Open(object form)
    {
        ArgumentNullException.ThrowIfNull(form);
        lock (_gate)
        {
            _items.Add(form);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Close(object form)
    {
        bool removed;
        lock (_gate)
        {
            removed = RemoveItem(form);
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool RemoveItem(object item)
    {
        var index = _items.FindIndex(i => ReferenceEquals(i, item));
        if (index >= 0)
        {
            _items.RemoveAt(index);
        }

        return index >= 0;
    }

    /// <summary>Cancels every question (e.g. the page is closing); forms are dismissed by their owners.</summary>
    public void CancelAll()
    {
        List<(ChoiceDialog Dialog, TaskCompletionSource<string> Answer)> pending;
        lock (_gate)
        {
            pending = [.. _answers.Select(a => (a.Key, a.Value))];
            _answers.Clear();
            _items.RemoveAll(i => i is ChoiceDialog);
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
