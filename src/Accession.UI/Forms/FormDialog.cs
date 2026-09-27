using System.ComponentModel;
using System.Windows.Input;
using Accession.UI.Components;
using Accession.UI.FilesScreen;

namespace Accession.UI.Forms;

/// <summary>
/// A dialog described as data: sections of fields and a row of buttons. Built by the host from a dialog view
/// model; values are read and written through delegates, so the view model keeps its logic and validation.
/// </summary>
public sealed class FormDialog
{
    public FormDialog(string title, IReadOnlyList<FormSection> sections, IReadOnlyList<FormButton> buttons)
    {
        Title = title;
        Sections = sections;
        Buttons = buttons;
    }

    public string Title { get; }
    public IReadOnlyList<FormSection> Sections { get; }
    public IReadOnlyList<FormButton> Buttons { get; }

    /// <summary>CSS width, e.g. "560px".</summary>
    public string Width { get; init; } = "560px";

    /// <summary>Objects whose changes re-render the form (the view model, its error list, collections).</summary>
    public IReadOnlyList<object> Observed { get; init; } = [];

    /// <summary>Esc or the close button.</summary>
    public Action? Dismiss { get; init; }

    /// <summary>Enter in a single-line field runs this button's command.</summary>
    public FormButton? DefaultButton => Buttons.FirstOrDefault(b => b.IsDefault);
}

/// <summary>A group of fields with an optional heading.</summary>
public sealed record FormSection(string? Title, IReadOnlyList<FormItem> Items);

/// <summary>A dialog button.</summary>
public sealed record FormButton(string Label, ICommand Command, DialogChoiceStyle Style = DialogChoiceStyle.Normal)
{
    /// <summary>Runs on Enter.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Placed on the left (e.g. "Restore defaults").</summary>
    public bool IsSecondary { get; init; }
}

/// <summary>Base of all form items.</summary>
public abstract record FormItem(string Label)
{
    public string? Hint { get; init; }

    /// <summary>Current validation message; empty when valid.</summary>
    public Func<string>? Error { get; init; }

    /// <summary>Hidden when this returns false.</summary>
    public Func<bool>? Visible { get; init; }

    /// <summary>Takes the full width in a two-column section.</summary>
    public bool Wide { get; init; }
}

/// <summary>A text box, optionally with a Browse button (native picker in the host).</summary>
public sealed record TextField(string Label, Func<string> Get, Action<string>? Set) : FormItem(Label)
{
    public string? Placeholder { get; init; }
    public bool Multiline { get; init; }
    public bool Mono { get; init; }
    public ICommand? Browse { get; init; }

    /// <summary>Selected and focused when the dialog opens.</summary>
    public bool AutoFocus { get; init; }
}

public sealed record NumberField(string Label, Func<int> Get, Action<int> Set, int Min, int Max) : FormItem(Label)
{
    public int Step { get; init; } = 1;
}

public sealed record SelectField(string Label, IReadOnlyList<SelectOption> Options, Func<string> Get, Action<string> Set) : FormItem(Label);

public sealed record CheckField(string Label, Func<bool> Get, Action<bool> Set) : FormItem(Label)
{
    public Func<bool>? Enabled { get; init; }
}

/// <summary>A read-only value (facts about the inventory).</summary>
public sealed record InfoField(string Label, Func<string> Get) : FormItem(Label)
{
    public bool Mono { get; init; }
}

/// <summary>A paragraph; <see cref="Tone"/> "info", "warning" or "danger" shows it as a banner.</summary>
public sealed record NoteItem(Func<string> Text) : FormItem(string.Empty)
{
    public string? Tone { get; init; }
}

/// <summary>A list of choices with tick boxes (e.g. media folders to add).</summary>
public sealed record ChecklistField(string Label, Func<IReadOnlyList<ChecklistEntry>> Entries) : FormItem(Label)
{
    public string EmptyText { get; init; } = "Nothing to choose.";
    public ICommand? Add { get; init; }
    public string AddLabel { get; init; } = "Add…";
}

/// <summary>One choice in a <see cref="ChecklistField"/>; <see cref="Source"/> raises changes of the tick state.</summary>
public sealed record ChecklistEntry(string Title, string Subtitle, Func<bool> Get, Action<bool> Set, INotifyPropertyChanged? Source = null)
{
    public string? Badge { get; init; }
}

/// <summary>Plain lines, e.g. media not found under a new root.</summary>
public sealed record LinesItem(string Label, Func<IReadOnlyList<string>> Lines) : FormItem(Label);

/// <summary>Monospace text such as exception details.</summary>
public sealed record CodeItem(string Label, Func<string> Text) : FormItem(Label);
