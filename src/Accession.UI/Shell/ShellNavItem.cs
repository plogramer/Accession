using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.UI.Shell;

/// <summary>One entry in the web shell's side navigation.</summary>
/// <param name="key">Stable key, also the classic screen's title.</param>
/// <param name="icon">Name understood by the Icon component.</param>
/// <param name="group">Section heading in the side navigation.</param>
/// <param name="content">
/// The screen's model (e.g. an <c>IDashboardModel</c>); <see cref="ScreenHost"/> picks the page by its type.
/// Null while the screen exists only in the classic UI.
/// </param>
public sealed partial class ShellNavItem(string key, string icon, string group, object? content = null) : ObservableObject
{
    public string Key { get; } = key;

    public string Icon { get; } = icon;

    public object? Content { get; } = content;

    /// <summary>False while the screen exists only in the classic UI.</summary>
    public bool IsAvailable => Content is not null;

    public string Group { get; } = group;

    /// <summary>Count shown next to the title (e.g. errors); empty hides it.</summary>
    [ObservableProperty]
    public partial string Badge { get; set; } = string.Empty;
}
