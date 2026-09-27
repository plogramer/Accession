using System.Windows;
using Accession.Presentation.Mvvm;

namespace Accession.App.Views;

/// <summary>Generic modal window; the content is picked by the view model's DataTemplate.</summary>
public partial class DialogWindow : Window
{
    private readonly DialogViewModelBase _viewModel;

    public DialogWindow(DialogViewModelBase viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        ResizeMode = viewModel.CanResize ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;

        _viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => _viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, bool? result)
    {
        if (result.HasValue)
        {
            DialogResult = result;
        }
        else
        {
            Close();
        }
    }
}
