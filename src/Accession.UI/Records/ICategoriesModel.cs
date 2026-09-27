using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace Accession.UI.Records;

/// <summary>Categories screen (requirement CAT-02, section 8.12). Categories are defined by the application.</summary>
public interface ICategoriesModel : INotifyPropertyChanged
{
    ObservableCollection<CategoryRowVm> Categories { get; }
    CategoryRowVm? SelectedCategory { get; set; }
    ObservableCollection<ExtensionCountVm> Extensions { get; }

    /// <summary>Files of the selected category.</summary>
    ICommand ShowFilesCommand { get; }

    /// <summary>Parameter: the <see cref="ExtensionCountVm"/>.</summary>
    ICommand ShowExtensionFilesCommand { get; }
}
