using System.Collections.ObjectModel;
using System.Globalization;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Data.Browsing;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.ViewModels.Browsing;

/// <summary>Categories screen (requirement CAT-02, section 8.12). Read-only: categories are defined by the application.</summary>
public sealed partial class CategoriesViewModel : ViewModelBase, IDisposable
{
    private readonly InventoryHost _host;
    private readonly CategoryQueries _queries;
    private readonly ISettingsService _settings;
    private readonly FileBrowserNavigator _navigator;
    private readonly ILogger<CategoriesViewModel> _logger;

    public CategoriesViewModel(InventoryHost host, CategoryQueries queries, ISettingsService settings, FileBrowserNavigator navigator, ILogger<CategoriesViewModel> logger)
    {
        _host = host;
        _queries = queries;
        _settings = settings;
        _navigator = navigator;
        _logger = logger;
        _host.MediaChanged += OnMediaChanged;
        Load();
    }

    public ObservableCollection<CategoryRowVm> Categories { get; } = [];

    public ObservableCollection<ExtensionCountVm> Extensions { get; } = [];

    [ObservableProperty]
    public partial CategoryRowVm? SelectedCategory { get; set; }

    public void Dispose() => _host.MediaChanged -= OnMediaChanged;

    [RelayCommand]
    private void ShowFiles()
    {
        if (SelectedCategory is { } category)
        {
            _navigator.ShowFiles(new FileFilter { CategoryId = category.CategoryId });
        }
    }

    [RelayCommand]
    private void ShowExtensionFiles(ExtensionCountVm? extension)
    {
        if (extension is not null)
        {
            _navigator.ShowFiles(new FileFilter { Extension = extension.RawExtension });
        }
    }

    partial void OnSelectedCategoryChanged(CategoryRowVm? value) => LoadExtensions();

    private void Load()
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        var selected = SelectedCategory?.CategoryId;
        var unit = _settings.Current.SizeUnit;
        Categories.Clear();
        try
        {
            foreach (var c in _queries.Categories(session.Database))
            {
                Categories.Add(new CategoryRowVm(c.CategoryId, c.Name, c.Description ?? string.Empty,
                    c.FileCount.ToString("N0", CultureInfo.CurrentCulture), SizeFormatter.Format(c.TotalBytes, unit), c.FileCount));
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading categories failed");
        }

        SelectedCategory = Categories.FirstOrDefault(c => c.CategoryId == selected) ?? Categories.FirstOrDefault();
    }

    private void LoadExtensions()
    {
        Extensions.Clear();
        if (SelectedCategory is not { } category || _host.Session is not { } session)
        {
            return;
        }

        var unit = _settings.Current.SizeUnit;
        try
        {
            foreach (var e in _queries.Extensions(session.Database, category.CategoryId))
            {
                Extensions.Add(new ExtensionCountVm(e.Extension, e.Extension.Length == 0 ? "(none)" : "." + e.Extension,
                    e.FileCount.ToString("N0", CultureInfo.CurrentCulture), SizeFormatter.Format(e.TotalBytes, unit), e.FileCount > 0));
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading extensions failed");
        }
    }

    private void OnMediaChanged(object? sender, EventArgs e) => Load();
}

public sealed record CategoryRowVm(int CategoryId, string Name, string Description, string Files, string Size, long FileCount);

public sealed record ExtensionCountVm(string RawExtension, string Extension, string Files, string Size, bool HasFiles);
