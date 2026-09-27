using System.Windows;
using Accession.App.Services;
using Accession.App.ViewModels;

namespace Accession.App;

public partial class MainWindow : Window
{
    private const string PlacementKey = "MainWindow";

    public MainWindow(MainWindowViewModel viewModel, IWindowPlacementService placement)
    {
        InitializeComponent();
        DataContext = viewModel;

        placement.Restore(this, PlacementKey);
        Closing += (_, _) => placement.Save(this, PlacementKey);
    }
}
