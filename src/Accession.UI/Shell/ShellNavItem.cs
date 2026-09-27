using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.UI.Shell;

/// <summary>One entry in the web shell's side navigation.</summary>
/// <param name="key">Stable key and title.</param>
/// <param name="icon">Name understood by the Icon component.</param>
/// <param name="group">Section heading in the side navigation.</param>
/// <param name="content">
/// The screen's model (e.g. an <c>IDashboardModel</c>); <see cref="ScreenHost"/> picks the page by its type.
/// </param>
public sealed partial class ShellNavItem(string key, string icon, string group, object content) : ObservableObject
{
    public string Key { get; } = key;

    public string Icon { get; } = icon;

    public object Content { get; } = content;

    public string Group { get; } = group;

    /// <summary>Count shown next to the title (e.g. errors); empty hides it.</summary>
    [ObservableProperty]
    public partial string Badge { get; set; } = string.Empty;
}
