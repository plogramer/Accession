using Accession.App.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.App.ViewModels.Shell;

/// <summary>One entry in the inventory shell's left navigation.</summary>
public sealed partial class NavItem(string title, ViewModelBase content) : ObservableObject
{
    public string Title { get; } = title;

    public ViewModelBase Content { get; } = content;

    /// <summary>Count shown next to the title (e.g. errors); empty hides it.</summary>
    [ObservableProperty]
    public partial string Badge { get; set; } = string.Empty;
}
