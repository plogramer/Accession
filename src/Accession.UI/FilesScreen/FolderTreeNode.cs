using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.UI.FilesScreen;

/// <summary>A folder as the tree needs it.</summary>
public sealed record FolderInfo(long FolderId, string Name, bool HasChildren, bool IsLink);

/// <summary>A folder in the Files tree. Children load the first time it is expanded.</summary>
public sealed partial class FolderTreeNode : ObservableObject
{
    private readonly Func<long, IReadOnlyList<FolderInfo>> _loadChildren;
    private bool _loaded;

    public FolderTreeNode(FolderInfo info, Func<long, IReadOnlyList<FolderInfo>> loadChildren, int depth = 0)
    {
        Info = info;
        Depth = depth;
        _loadChildren = loadChildren;
    }

    public FolderInfo Info { get; }

    public int Depth { get; }

    public ObservableCollection<FolderTreeNode> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _loaded)
        {
            return;
        }

        _loaded = true;
        foreach (var child in _loadChildren(Info.FolderId))
        {
            Children.Add(new FolderTreeNode(child, _loadChildren, Depth + 1));
        }
    }
}
