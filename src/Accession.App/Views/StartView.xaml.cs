using System.Windows.Controls;
using System.Windows.Input;
using Accession.Presentation.ViewModels;
using Accession.UI.Start;

namespace Accession.App.Views;

public partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
    }

    private void RecentList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void RecentList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelected();
            e.Handled = true;
        }
    }

    private void OpenSelected()
    {
        if (DataContext is StartViewModel viewModel && RecentList.SelectedItem is RecentInventoryItem item)
        {
            viewModel.OpenRecentCommand.Execute(item);
        }
    }
}
