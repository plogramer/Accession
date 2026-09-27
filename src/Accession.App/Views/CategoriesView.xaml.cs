using System.Windows.Controls;
using System.Windows.Input;
using Accession.App.ViewModels.Browsing;

namespace Accession.App.Views;

public partial class CategoriesView : UserControl
{
    public CategoriesView()
    {
        InitializeComponent();
    }

    private void ExtensionGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CategoriesViewModel vm && ExtensionGrid.SelectedItem is ExtensionCountVm extension)
        {
            vm.ShowExtensionFilesCommand.Execute(extension);
        }
    }
}
