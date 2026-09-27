using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Accession.App.ViewModels.Browsing;
using Accession.Core.Settings;
using Accession.Data.Browsing;

namespace Accession.App.Views;

public partial class FileBrowserView : UserControl
{
    private const string LayoutKey = "Files";
    private bool _layoutApplied;

    public FileBrowserView()
    {
        InitializeComponent();
    }

    private FileBrowserViewModel? ViewModel => DataContext as FileBrowserViewModel;

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is { } vm)
        {
            vm.SelectedFolder = e.NewValue as FolderTreeItem;
        }
    }

    /// <summary>Sorting is done by the database (keyset paging), not in memory.</summary>
    private void FileGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (ViewModel is not { } vm || !Enum.TryParse<FileSortColumn>(e.Column.SortMemberPath, out var column))
        {
            return;
        }

        vm.SortBy(column);
        foreach (var c in FileGrid.Columns)
        {
            c.SortDirection = null;
        }

        e.Column.SortDirection = vm.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
    }

    private void FileGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (FindScrollViewer(FileGrid) is { } scroll)
        {
            scroll.ScrollChanged -= OnScrollChanged;
            scroll.ScrollChanged += OnScrollChanged;
        }

        if (!_layoutApplied)
        {
            _layoutApplied = true;
            ApplyColumnLayout();
            BuildColumnMenu();
        }
    }

    /// <summary>Loads the next page when the user scrolls near the end.</summary>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 20 &&
            ViewModel is { HasMore: true, IsLoading: false } vm)
        {
            vm.LoadMoreCommand.Execute(null);
        }
    }

    private void ApplyColumnLayout()
    {
        if (ViewModel?.Settings.Current.GridLayouts.TryGetValue(LayoutKey, out var layout) != true || layout is null)
        {
            return;
        }

        foreach (var column in FileGrid.Columns)
        {
            var saved = layout.FirstOrDefault(l => l.Key == column.Header?.ToString());
            if (saved is not null)
            {
                column.Visibility = saved.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                if (saved.Width > 0)
                {
                    column.Width = new DataGridLength(saved.Width);
                }
            }
        }
    }

    /// <summary>Right-click on a column header: show/hide columns (saved in user settings).</summary>
    private void BuildColumnMenu()
    {
        var menu = new ContextMenu();
        foreach (var column in FileGrid.Columns)
        {
            var item = new MenuItem { Header = column.Header, IsCheckable = true, IsChecked = column.Visibility == Visibility.Visible };
            var target = column;
            item.Click += (_, _) =>
            {
                target.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                SaveColumnLayout();
            };
            menu.Items.Add(item);
        }

        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(ContextMenuProperty, menu));
        FileGrid.ColumnHeaderStyle = style;
    }

    private void SaveColumnLayout()
    {
        var layout = FileGrid.Columns.Select(c => new GridColumnLayout
        {
            Key = c.Header?.ToString() ?? string.Empty,
            DisplayIndex = c.DisplayIndex,
            IsVisible = c.Visibility == Visibility.Visible,
            Width = c.ActualWidth,
        }).ToList();
        ViewModel?.Settings.Update(s => s.GridLayouts[LayoutKey] = layout);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroll)
            {
                return scroll;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
