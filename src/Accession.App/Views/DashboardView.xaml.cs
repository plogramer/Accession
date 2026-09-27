using System.Windows.Controls;
using System.Windows.Input;
using Accession.Presentation.ViewModels.Dashboard;
using Accession.UI.Dashboard;

namespace Accession.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    private void ByMedia_DoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.OpenMediaCommand.Execute((sender as DataGrid)?.SelectedItem as DashboardMediaRowVm);

    private void ByCategory_DoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.OpenCategoryCommand.Execute((sender as ListBox)?.SelectedItem as BarRow);

    private void ByExtension_DoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.OpenExtensionCommand.Execute((sender as DataGrid)?.SelectedItem as ExtensionRowVm);

    private void ByYear_DoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.OpenYearCommand.Execute((sender as ListBox)?.SelectedItem as BarRow);

    private void LargestFiles_DoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.OpenLargeFileCommand.Execute((sender as DataGrid)?.SelectedItem as LargeFileRowVm);

    private void Duplicates_Click(object sender, MouseButtonEventArgs e) => ViewModel?.OpenDuplicatesCommand.Execute(null);
}
