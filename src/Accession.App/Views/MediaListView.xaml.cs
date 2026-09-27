using System.Windows.Controls;
using Accession.Presentation.ViewModels.MediaScreen;
using Accession.UI.MediaScreen;

namespace Accession.App.Views;

public partial class MediaListView : UserControl
{
    public MediaListView()
    {
        InitializeComponent();
    }

    private void MediaGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MediaListViewModel viewModel && sender is DataGrid grid)
        {
            viewModel.SetSelectedRows(grid.SelectedItems.OfType<MediaRowViewModel>());
        }
    }
}
