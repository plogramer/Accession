using System.Windows.Input;

namespace Accession.UI.Shell;

/// <summary>A screen that F5 reloads.</summary>
public interface IRefreshableScreen
{
    ICommand RefreshCommand { get; }
}
