using System.Collections.ObjectModel;
using Accession.Data.Browsing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.App.ViewModels.Browsing;

/// <summary>A lazily loaded folder in the File browser tree.</summary>
public sealed partial class FolderTreeItem : ObservableObject
{
    private static readonly FolderTreeItem Placeholder = new();
    private readonly Func<long, IReadOnlyList<FolderNode>>? _loadChildren;
    private bool _loaded;

    public FolderTreeItem(FolderNode node, Func<long, IReadOnlyList<FolderNode>> loadChildren)
    {
        Node = node;
        _loadChildren = loadChildren;
        if (node.HasChildren)
        {
            Children.Add(Placeholder);
        }
    }

    private FolderTreeItem()
    {
        Node = new FolderNode { Name = "…" };
    }

    public FolderNode Node { get; }

    public string Name => Node.IsReparsePoint ? Node.Name + "  (link, not followed)" : Node.Name;

    public ObservableCollection<FolderTreeItem> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _loaded || _loadChildren is null)
        {
            return;
        }

        _loaded = true;
        Children.Clear();
        foreach (var child in _loadChildren(Node.FolderId))
        {
            Children.Add(new FolderTreeItem(child, _loadChildren));
        }
    }
}
