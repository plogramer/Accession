using System.Windows;
using Accession.App.Services;
using Accession.Presentation.ViewModels;

namespace Accession.App;

public partial class MainWindow : Window
{
    private const string PlacementKey = "MainWindow";

    public MainWindow(MainWindowViewModel viewModel, WebAppViewModel page, IWindowPlacementService placement)
    {
        InitializeComponent();
        DataContext = viewModel;

        // The page follows the session itself (Start screen, or the inventory shell).
        page.OnNavigatedTo();
        Page.DataContext = page;

        placement.Restore(this, PlacementKey);
        var closeApproved = false;
        Closing += async (_, e) =>
        {
            if (closeApproved)
            {
                return;
            }

            // Closing the inventory may ask the user (running scan) and waits for the scan to stop.
            e.Cancel = true;
            placement.Save(this, PlacementKey);
            if (await viewModel.PrepareCloseAsync())
            {
                closeApproved = true;
                Close();
            }
        };
    }
}
